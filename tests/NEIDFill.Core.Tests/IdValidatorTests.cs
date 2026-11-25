using Microsoft.VisualStudio.TestTools.UnitTesting;
using NEIDFill.Core.IdCard;

namespace NEIDFill.Core.Tests
{
    [TestClass]
    public class IdValidatorTests
    {
        // GB 11643 校验位已核对的合法号
        const string ValidX = "11010519491231002X";   // 末位 X
        const string ValidNum = "110101199003071233"; // 末位数字

        [TestMethod]
        public void 合法号_末位X()
        {
            string error;
            Assert.IsTrue(IdValidator.Validate(ValidX, out error));
            Assert.AreEqual(null, error);
            Assert.IsTrue(IdValidator.IsValid(ValidX));
        }

        [TestMethod]
        public void 合法号_末位数字()
        {
            Assert.IsTrue(IdValidator.IsValid(ValidNum));
        }

        [TestMethod]
        public void 小写x归一化后合法()
        {
            Assert.IsTrue(IdValidator.IsValid("11010519491231002x"));
        }

        [TestMethod]
        public void 前后空白归一化后合法()
        {
            Assert.IsTrue(IdValidator.IsValid("  11010519491231002X  "));
        }

        [TestMethod]
        public void 长度17位_Rejected()
        {
            string error;
            Assert.IsFalse(IdValidator.IsValid("11010519491231002"));
            Assert.IsFalse(IdValidator.Validate("11010519491231002", out error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [TestMethod]
        public void 长度19位_Rejected()
        {
            string error;
            Assert.IsFalse(IdValidator.Validate(ValidX + "1", out error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        // 正则收紧为 [0-9] 的回归：全角数字必须被拒
        [TestMethod]
        public void 前17位含全角数字_Rejected()
        {
            string error;
            Assert.IsFalse(IdValidator.Validate("１1010519491231002X", out error));
            Assert.IsTrue(error.Contains("数字"));
        }

        [TestMethod]
        public void 前17位含阿拉伯印度数字_Rejected()
        {
            // １１０１０５... 的阿拉伯-印度数字写法（U+0660..U+0669）
            Assert.IsFalse(IdValidator.IsValid(
                "\u0661\u0661\u0660\u0661\u0660\u0665" +
                "\u0661\u0669\u0664\u0669\u0661\u0662\u0663\u0661\u0660\u0660\u0662" + "X"));
        }

        [TestMethod]
        public void 无效地区码_Rejected()
        {
            string error;
            Assert.IsFalse(IdValidator.Validate("99010519491231002X", out error));
            Assert.IsTrue(error.Contains("地区码"));
        }

        [TestMethod]
        public void 二月三十_Rejected()
        {
            string error;
            Assert.IsFalse(IdValidator.Validate("11010519490230002X", out error));
            Assert.IsTrue(error.Contains("出生日期"));
        }

        [TestMethod]
        public void 未来出生日期_Rejected()
        {
            string error;
            Assert.IsFalse(IdValidator.Validate("11010520990101002X", out error));
            Assert.IsTrue(error.Contains("出生日期"));
        }

        [TestMethod]
        public void 校验位错误_Rejected()
        {
            // 正确末位应为 X，写成 1
            string error;
            Assert.IsFalse(IdValidator.Validate("110105194912310021", out error));
            Assert.IsTrue(error.Contains("校验位"));
        }

        [TestMethod]
        public void 空输入_Rejected()
        {
            string error;
            Assert.IsFalse(IdValidator.Validate(null, out error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Assert.IsFalse(IdValidator.Validate("", out error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Assert.IsFalse(IdValidator.Validate("   ", out error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [TestMethod]
        public void 归一化_null返回null()
        {
            Assert.IsNull(IdValidator.Normalize(null));
        }

        [TestMethod]
        public void 归一化_去空白并大写X()
        {
            Assert.AreEqual("11010519491231002X", IdValidator.Normalize(" 110105 19491231 002x "));
        }
    }
}
