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
            Assert.AreEqual(0, s.ReaderPort);
            Assert.IsTrue(s.AutoType);
            Assert.IsFalse(s.AppendEnter);
        }

        [TestMethod]
        public void 保存读回往返()
        {
            string path = Path.Combine(Path.GetTempPath(), "neidfill_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var s = new FillSettings { ReaderPort = 2002, AutoType = false, AppendEnter = true };
                s.Save(path);

                var back = FillSettings.Load(path);
                Assert.AreEqual(2002, back.ReaderPort);
                Assert.IsFalse(back.AutoType);
                Assert.IsTrue(back.AppendEnter);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [TestMethod]
        public void 损坏配置静默回退默认值()
        {
            string path = Path.Combine(Path.GetTempPath(), "neidfill_bad_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ readerPort: oops");
            try
            {
                var s = FillSettings.Load(path);
                Assert.AreEqual(0, s.ReaderPort);
                Assert.IsTrue(s.AutoType);
                Assert.IsFalse(s.AppendEnter);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [TestMethod]
        public void 端口越界回退_负数归零超限封顶()
        {
            string path = Path.Combine(Path.GetTempPath(), "neidfill_port_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var s = new FillSettings { ReaderPort = 70000 };
                s.Save(path);
                Assert.AreEqual(9999, FillSettings.Load(path).ReaderPort);

                File.WriteAllText(path, "{ \"readerPort\": -5 }", new UTF8Encoding(false));
                Assert.AreEqual(0, FillSettings.Load(path).ReaderPort);
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
                var s = new FillSettings { ReaderPort = 100 };
                s.Save(path);

                var back = FillSettings.Load(path);
                Assert.AreEqual(100, back.ReaderPort);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
                try { Directory.Delete(Path.GetDirectoryName(dir), true); } catch { }
            }
        }
    }
}
