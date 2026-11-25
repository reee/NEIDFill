using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NEIDFill.Core;

namespace NEIDFill.Core.Tests
{
    [TestClass]
    public class JsonTests
    {
        [TestMethod]
        public void 对象各类型取值()
        {
            var o = (Dictionary<string, object>)Json.Parse(
                "{\"s\":\"str\",\"n\":42,\"b\":true,\"nul\":null,\"arr\":[1,2],\"obj\":{\"k\":\"v\"}}");
            Assert.AreEqual("str", o["s"]);
            Assert.AreEqual(42d, o["n"]);
            Assert.AreEqual(true, o["b"]);
            Assert.IsNull(o["nul"]);
            Assert.AreEqual(2, ((List<object>)o["arr"]).Count);
            Assert.AreEqual("v", ((Dictionary<string, object>)o["obj"])["k"]);
        }

        [TestMethod]
        public void GetInt_数字缺失与非数字()
        {
            object o = Json.Parse("{\"n\":7,\"s\":\"x\"}");
            Assert.AreEqual(7, Json.GetInt(o, "n"));
            Assert.AreEqual(0, Json.GetInt(o, "missing"));
            Assert.AreEqual(0, Json.GetInt(o, "s"));
        }

        [TestMethod]
        public void 转义字符串解码()
        {
            string s = (string)Json.Parse(@"""a\""b\\c\/d\b\f\n\r\t\u4e2d""");
            Assert.AreEqual("a\"b\\c/d\b\f\n\r\t\u4e2d", s);
        }

        [TestMethod]
        public void 顶层数组()
        {
            var a = (List<object>)Json.Parse("[1,\"a\",true,null]");
            Assert.AreEqual(4, a.Count);
            Assert.AreEqual(1d, a[0]);
            Assert.AreEqual("a", a[1]);
            Assert.AreEqual(true, a[2]);
            Assert.IsNull(a[3]);
        }

        [TestMethod]
        public void 尾部多余内容_Throws()
        {
            Assert.ThrowsException<FormatException>(() => Json.Parse("{}{}"));
        }

        [TestMethod]
        public void 字符串未闭合_Throws()
        {
            Assert.ThrowsException<FormatException>(() => Json.Parse(@"""abc"));
        }

        [TestMethod]
        public void 非法转义q_Throws()
        {
            Assert.ThrowsException<FormatException>(() => Json.Parse(@"""a\qb"""));
        }

        [TestMethod]
        public void u转义非十六进制_Throws()
        {
            Assert.ThrowsException<FormatException>(() => Json.Parse(@"""x\u12G4"""));
        }

        [TestMethod]
        public void u转义不足四位_Throws()
        {
            Assert.ThrowsException<FormatException>(() => Json.Parse(@"""x\u12"""));
        }

        [TestMethod]
        public void 七十个嵌套数组_Throws()
        {
            string deep = new string('[', 70) + new string(']', 70);
            Assert.ThrowsException<FormatException>(() => Json.Parse(deep));
        }

        [TestMethod]
        public void 十层嵌套正常解析()
        {
            string ok = new string('[', 10) + new string(']', 10);
            var a = (List<object>)Json.Parse(ok);
            Assert.AreEqual(1, a.Count);
        }

        [TestMethod]
        public void 空输入_Throws()
        {
            Assert.ThrowsException<FormatException>(() => Json.Parse(null));
            Assert.ThrowsException<FormatException>(() => Json.Parse(""));
        }
    }
}
