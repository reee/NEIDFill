using System;
using System.IO;
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
            var s = FillSettings.Load(Path.Combine(Path.GetTempPath(), "no_such_fill_" + Guid.NewGuid().ToString("N") + ".ini"));
            Assert.IsTrue(s.AutoType);
            Assert.IsFalse(s.AppendEnter);
        }

        [TestMethod]
        public void 保存读回往返()
        {
            string path = Path.Combine(Path.GetTempPath(), "neidfill_" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                var s = new FillSettings { AutoType = false, AppendEnter = true };
                s.Save(path);

                var back = FillSettings.Load(path);
                Assert.IsFalse(back.AutoType);
                Assert.IsTrue(back.AppendEnter);

                // 第二个组合也各读回一致
                s.AutoType = true;
                s.AppendEnter = false;
                s.Save(path);
                back = FillSettings.Load(path);
                Assert.IsTrue(back.AutoType);
                Assert.IsFalse(back.AppendEnter);

                // 文件内容为两行 key=value 纯文本，无 JSON 残留
                string text = File.ReadAllText(path);
                Assert.IsTrue(text.Contains("autoType="));
                Assert.IsTrue(text.Contains("appendEnter="));
                Assert.IsFalse(text.Contains("{"));
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // 非法值（notabool）、未知 key（readerPort）、无 = 的垃圾行都被静默忽略
        private static readonly string[] BadLines =
        {
            "autoType=false",
            "appendEnter=notabool",
            "readerPort=9999",
            "垃圾行",
        };

        [TestMethod]
        public void 坏行与未知行被忽略()
        {
            string path = Path.Combine(Path.GetTempPath(), "neidfill_bad_" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllLines(path, BadLines);
            try
            {
                var s = FillSettings.Load(path);
                Assert.IsFalse(s.AutoType);
                Assert.IsFalse(s.AppendEnter); // 非法值不生效，保持默认
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [TestMethod]
        public void 大小写与空白容错()
        {
            string path = Path.Combine(Path.GetTempPath(), "neidfill_ws_" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(path, "  AUTOTYPE = TRUE  \nappendEnter = False\n");
            try
            {
                var s = FillSettings.Load(path);
                Assert.IsTrue(s.AutoType);
                Assert.IsFalse(s.AppendEnter);
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
