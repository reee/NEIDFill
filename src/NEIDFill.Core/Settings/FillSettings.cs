using System;
using System.IO;

namespace NEIDFill.Core.Settings
{
    /// <summary>NEIDFill（读卡→键入前台）小工具的设置。
    /// 存为 neidfill.ini（默认 %LOCALAPPDATA%\NEIDFill），两行 key=value 纯文本
    /// （旧版 readerPort 一类未知行一律忽略）。</summary>
    public sealed class FillSettings
    {
        /// <summary>读卡后自动键入前台输入框。</summary>
        public bool AutoType = true;
        /// <summary>键入后追加回车。</summary>
        public bool AppendEnter = false;

        public static FillSettings Load(string path)
        {
            FillSettings s = new FillSettings();
            try
            {
                if (!File.Exists(path)) return s;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    // 以第一个 = 为界拆 key/value；无 = 的行静默忽略
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    bool value;
                    // 值只接受 true/false，其余（如 notabool、9999）忽略该行
                    if (!bool.TryParse(line.Substring(eq + 1).Trim(), out value)) continue;
                    if (string.Equals(key, "autoType", StringComparison.OrdinalIgnoreCase)) s.AutoType = value;
                    else if (string.Equals(key, "appendEnter", StringComparison.OrdinalIgnoreCase)) s.AppendEnter = value;
                    // 未知 key 忽略；重复 key 后值覆盖前值
                }
            }
            catch (Exception)
            {
                // 配置文件读取失败时使用已解析到的值（至少是默认值），不阻塞启动
            }
            return s;
        }

        public void Save(string path)
        {
            // 直接写程序目录（调用方保证目录存在）；写入异常上抛，由调用方提示保存失败
            string text = "autoType=" + (AutoType ? "true" : "false") + Environment.NewLine +
                "appendEnter=" + (AppendEnter ? "true" : "false") + Environment.NewLine;
            File.WriteAllText(path, text);
        }
    }
}
