using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NEIDFill.Core.IdCard;

namespace NEIDFill.Core.Tests
{
    [TestClass]
    public class Gb18030CodecTests
    {
        [TestMethod]
        public void Null返回空串()
        {
            Assert.AreEqual("", Gb18030Codec.Decode(null));
        }

        [TestMethod]
        public void 空数组返回空串()
        {
            Assert.AreEqual("", Gb18030Codec.Decode(Array.Empty<byte>()));
        }

        [TestMethod]
        public void ASCII直通()
        {
            Assert.AreEqual("ABC",
                Gb18030Codec.Decode(new byte[] { (byte)'A', (byte)'B', (byte)'C' }));
        }

        [TestMethod]
        public void NUL截断()
        {
            Assert.AreEqual("AB",
                Gb18030Codec.Decode(new byte[] { (byte)'A', (byte)'B', 0, (byte)'C', (byte)'D' }));
        }

        [TestMethod]
        public void GBK中文姓名()
        {
            Assert.AreEqual("张三",
                Gb18030Codec.Decode(new byte[] { 0xD5, 0xC5, 0xC8, 0xFD }));
        }

        [TestMethod]
        public void 前后空格被Trim()
        {
            Assert.AreEqual("AB",
                Gb18030Codec.Decode(new byte[] { 0x20, (byte)'A', (byte)'B', 0x20 }));
        }

        [TestMethod]
        public void 截断的多字节序列不抛异常()
        {
            string s = Gb18030Codec.Decode(new byte[] { 0xD5 });
            Assert.IsNotNull(s);
        }
    }
}
