using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NEIDFill.Core.Settings
{
    /// <summary>NEIDFill（读卡→键入前台）小工具的设置。</summary>
    public sealed class FillSettings
    {
        /// <summary>读卡器端口参数；0 = 启动时自动探测。</summary>
        public int ReaderPort = 0;
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
                string json = File.ReadAllText(path, Encoding.UTF8);
                Dictionary<string, object> o = Json.Parse(json) as Dictionary<string, object>;
                if (o == null) return s;

                s.ReaderPort = ClampPort(Json.GetInt(o, "readerPort"));

                object v;
                if (o.TryGetValue("autoType", out v) && v is bool) s.AutoType = (bool)v;
                if (o.TryGetValue("appendEnter", out v) && v is bool) s.AppendEnter = (bool)v;
            }
            catch (Exception)
            {
                // 配置文件损坏时使用默认值，不阻塞启动
            }
            return s;
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"readerPort\": " + ReaderPort.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"autoType\": " + (AutoType ? "true" : "false") + ",");
            sb.AppendLine("  \"appendEnter\": " + (AppendEnter ? "true" : "false"));
            sb.AppendLine("}");
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 原子写：先写同目录临时文件再替换，避免写入中途断电/杀进程留下截断文件（Load 会静默回退默认值）
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception)
            {
                // Replace 失败（罕见：占用/权限）时退化为直接覆盖，保证设置不丢
                try
                {
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
                catch
                {
                    // 主备两条路径都失败：清理临时文件（尽力而为）后上抛，让调用方提示保存失败
                    try { File.Delete(tmp); } catch { }
                    throw;
                }
            }
        }

        /// <summary>端口范围保护：取不到或负数用 0（自动探测），上限 9999。</summary>
        private static int ClampPort(int p)
        {
            if (p < 0) return 0;
            if (p > 9999) return 9999;
            return p;
        }
    }
}
