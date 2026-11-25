using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NEIDFill.Core.Settings;

namespace NEIDFill.Core.Tests
{
    [TestClass]
    public class FillSettingsTests
    {
        [TestMethod]
        public void 缺文件用默认值()
        {
            var s = FillSettings.Load(Path.Combine(Path.GetTempPath(), "no_such_fill_" + Guid.NewGuid().ToString("N") + ".json"));
            Assert.IsTrue(s.AutoType);
            Assert.IsFalse(s.AppendEnter);
        }

        [TestMethod]
        public void 保存读回往返()
        {
            string path = Path.Combine(Path.GetTempPath(), "neidfill_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var s = new FillSettings { AutoType = false, AppendEnter = true };
                s.Save(path);

                var back = FillSettings.Load(path);
                Assert.IsFalse(back.AutoType);
                Assert.IsTrue(back.AppendEnter);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [TestMethod]
        public void 损坏配置静默回退默认值()
        {
            string path = Path.Combine(Path.GetTempPath(), "neidfill_bad_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ autoType: oops");
            try
            {
                var s = FillSettings.Load(path);
                Assert.IsTrue(s.AutoType);
                Assert.IsFalse(s.AppendEnter);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [TestMethod]
        public void 保存不写已废弃readerPort()
        {
            // 已废弃的端口设置不应再写入新保存的文件（读卡通道始终自动探测）
            string path = Path.Combine(Path.GetTempPath(), "neidfill_nowrite_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var s = new FillSettings { AutoType = false, AppendEnter = true };
                s.Save(path);

                string json = File.ReadAllText(path, Encoding.UTF8);
                Assert.IsFalse(json.Contains("readerPort"), "保存结果不应包含已废弃的 readerPort");
                Assert.IsTrue(json.Contains("\"autoType\""));
                Assert.IsTrue(json.Contains("\"appendEnter\""));
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [TestMethod]
        public void 旧配置readerPort字段被忽略()
        {
            // 旧版设置含 readerPort：读卡通道改为始终自动探测，未知字段应被静默忽略而不报错
            string path = Path.Combine(Path.GetTempPath(), "neidfill_legacy_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path,
                "{ \"readerPort\": 9999, \"autoType\": false, \"appendEnter\": true }",
                new UTF8Encoding(false));
            try
            {
                var s = FillSettings.Load(path);
                Assert.IsFalse(s.AutoType);
                Assert.IsTrue(s.AppendEnter);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [TestMethod]
        public void 保存会创建父目录()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neidfill_dir_" + Guid.NewGuid().ToString("N"), "sub");
            string path = Path.Combine(dir, "fill.json");
            try
            {
                var s = new FillSettings { AppendEnter = true };
                s.Save(path);

                var back = FillSettings.Load(path);
                Assert.IsTrue(back.AppendEnter);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
                try { Directory.Delete(Path.GetDirectoryName(dir), true); } catch { }
            }
        }
    }
}
