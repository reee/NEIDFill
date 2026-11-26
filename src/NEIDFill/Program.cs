using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using NEIDFill.Core.Settings;

namespace NEIDFill
{
    /// <summary>
    /// NEIDFill 入口：读卡→把号码键入当前前台输入框的独立小工具。
    /// 同一读卡器硬件只允许一个实例占用（原生设备独占），互斥锁防双开互抢；
    /// 第二实例不重复运行，而是通过命名事件让第一实例显示/激活窗口。
    /// 设置与 crash.log 存于 %LOCALAPPDATA%\NEIDFill（不可用时回退程序目录；日志再回退临时目录）。
    /// </summary>
    internal static class Program
    {
        private static Mutex _singleInstance;
        // 二实例→第一实例的“显示窗口”通道：第一实例持有事件并在线程池等待，第二实例 OpenExisting+Set
        private static EventWaitHandle _showWindowEvent;
        private static RegisteredWaitHandle _showWindowWait;
        private static DateTime _lastErrorUi = DateTime.MinValue;
        private static readonly object LogLock = new object();
        private static string _dataDir; // 惰性解析一次；LocalAppData 不可用时回退程序目录

        [STAThread]
        private static void Main()
        {
            // 文档要求这两个方法在创建任何窗口之前调用（单实例提示框也是窗口）
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            _singleInstance = new Mutex(true, @"Local\NEIDFill.SingleInstance", out createdNew);
            if (!createdNew)
            {
                // 第二实例：请求已在运行的实例显示/激活窗口；只有通道也失败才回退提示框
                if (!SignalShowWindow())
                {
                    MessageBox.Show(
                        "NEIDFill 已在运行中，请勿重复打开（会互抢读卡器）。",
                        "已在运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            // 显示窗口事件在互斥锁拿到后立即创建：缩小“第一实例还在启动、第二实例就来信号”的窗口期
            try
            {
                _showWindowEvent = new EventWaitHandle(
                    false, EventResetMode.AutoReset, @"Local\NEIDFill.ShowWindow");
            }
            catch (Exception) { }

            // 兜底日志：未处理异常先落盘 crash.log；UI 线程异常限流弹窗，进程不无声退出
            Application.ThreadException += (s, e) =>
            {
                LogCrash("UI", e.Exception);
                if (DateTime.Now - _lastErrorUi >= TimeSpan.FromSeconds(30))
                {
                    _lastErrorUi = DateTime.Now;
                    MessageBox.Show(
                        "程序遇到错误（已写入 crash.log）：\n" + e.Exception.Message +
                        "\n\n可继续使用；若反复出错，请结束并重启程序。",
                        "运行错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                LogCrash("FATAL", e.ExceptionObject as Exception);

            FillForm form = null;
            try
            {
                // 独立设置文件 neidfill.ini（存于 %LOCALAPPDATA%\NEIDFill）
                string settingsPath = Path.Combine(GetDataDir(), "neidfill.ini");
                form = new FillForm(FillSettings.Load(settingsPath), settingsPath);
                // 强制创建句柄：否则第一实例 Show 之前第二实例 Set()，回调因 !IsHandleCreated 丢信号
                // （仍在 try 内：句柄创建异常走启动兜底路径；Shown 触发时机不受影响）
                _ = form.Handle;
            }
            catch (Exception ex)
            {
                // 构造期异常发生在消息循环之前，不会走 ThreadException：明确报错而不是静默死进程
                LogCrash("STARTUP", ex);
                MessageBox.Show("NEIDFill 启动失败：\n" + ex.Message +
                    "\n\n详细见 crash.log。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 注册“显示窗口”等待：第二实例 Set() 后在 UI 线程恢复/激活主窗口
            if (_showWindowEvent != null)
            {
                _showWindowWait = ThreadPool.RegisterWaitForSingleObject(
                    _showWindowEvent, ShowWindowWaitCallback, form, Timeout.Infinite, false);
            }

            Application.Run(form);
        }

        /// <summary>第二实例调用：通知第一实例显示窗口。OpenExisting 重试约 5×200ms（第一实例可能还在启动）。</summary>
        private static bool SignalShowWindow()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using (EventWaitHandle ev = EventWaitHandle.OpenExisting(@"Local\NEIDFill.ShowWindow"))
                        ev.Set();
                    return true;
                }
                catch (WaitHandleCannotBeOpenedException) { }
                catch (Exception) { }
                Thread.Sleep(200);
            }
            return false;
        }

        /// <summary>线程池等待回调：封送到 UI 线程显示/恢复/激活主窗口（窗口还没建好就忽略）。</summary>
        private static void ShowWindowWaitCallback(object state, bool timedOut)
        {
            if (timedOut) return;
            FillForm form = (FillForm)state;
            try
            {
                if (form.IsDisposed || !form.IsHandleCreated) return;
                form.BeginInvoke((Action)(() => form.RestoreFromTray()));
            }
            catch (Exception) { }
        }

        /// <summary>数据目录 %LOCALAPPDATA%\NEIDFill（不存在则创建）；创建失败回退程序目录。惰性解析一次。</summary>
        private static string GetDataDir()
        {
            if (_dataDir != null) return _dataDir;
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                {
                    string dir = Path.Combine(local, "NEIDFill");
                    Directory.CreateDirectory(dir);
                    _dataDir = dir;
                    return _dataDir;
                }
            }
            catch (Exception) { }
            _dataDir = AppDomain.CurrentDomain.BaseDirectory;
            return _dataDir;
        }

        private static void LogCrash(string kind, Exception ex)
        {
            try
            {
                string text = "---- " + DateTime.Now.ToString("s") + " " + kind + " ----\r\n" +
                    (ex != null ? ex.ToString() : "(null)") + "\r\n";
                // 脱敏：异常消息可能夹带 18 位身份证号（末位可为 X/x，前后不紧邻数字）
                text = Regex.Replace(text, @"(?<![0-9])[0-9]{17}[0-9Xx](?![0-9])", "[证件号已脱敏]");
                lock (LogLock)
                {
                    // 依次尝试：数据目录 → 程序目录 → 临时目录；全程不抛
                    string[] candidates =
                    {
                        Path.Combine(GetDataDir(), "crash.log"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                        Path.Combine(Path.GetTempPath(), "NEIDFill-crash.log"),
                    };
                    foreach (string path in candidates)
                    {
                        try
                        {
                            // 大小上限：追加前若已超过 1MiB 先截断为一行标记，防止无限增长
                            const long MaxLogBytes = 1024 * 1024;
                            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                                File.WriteAllText(path, "---- 日志超过 1MiB，已截断 ----\r\n");
                            File.AppendAllText(path, text);
                            return;
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
