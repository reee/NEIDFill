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
    /// 同一读卡器硬件只允许一个实例占用（原生设备独占），互斥锁防双开互抢。
    /// </summary>
    internal static class Program
    {
        private static Mutex _singleInstance;
        private static DateTime _lastErrorUi = DateTime.MinValue;
        private static readonly object LogLock = new object();

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
                MessageBox.Show(
                    "NEIDFill 已在运行中，请勿重复打开（会互抢读卡器）。",
                    "已在运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

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
                // 独立设置文件 neidfill.json（存于程序目录）
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "neidfill.json");
                FillSettings settings = FillSettings.Load(settingsPath);
                form = new FillForm(settings, settingsPath);
            }
            catch (Exception ex)
            {
                // 构造期异常发生在消息循环之前，不会走 ThreadException：明确报错而不是静默死进程
                LogCrash("STARTUP", ex);
                MessageBox.Show("NEIDFill 启动失败：\n" + ex.Message +
                    "\n\n详细见 crash.log。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.Run(form);
        }

        private static void LogCrash(string kind, Exception ex)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                string text = "---- " + DateTime.Now.ToString("s") + " " + kind + " ----\r\n" +
                    (ex != null ? ex.ToString() : "(null)") + "\r\n";
                // 脱敏：异常消息可能夹带 18 位身份证号（末位可为 X/x，前后不紧邻数字）
                text = Regex.Replace(text, @"(?<![0-9])[0-9]{17}[0-9Xx](?![0-9])", "[证件号已脱敏]");
                lock (LogLock)
                {
                    // 大小上限：追加前若已超过 1MiB 先截断为一行标记，防止无限增长
                    const long MaxLogBytes = 1024 * 1024;
                    if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                        File.WriteAllText(path, "---- 日志超过 1MiB，已截断 ----\r\n");
                    File.AppendAllText(path, text);
                }
            }
            catch (Exception) { }
        }
    }
}
