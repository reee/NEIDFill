using Microsoft.VisualStudio.TestTools.UnitTesting;
using NEIDFill.Core.Typing;

namespace NEIDFill.Core.Tests
{
    [TestClass]
    public class TypeGuardTests
    {
        const string Id = "500101199001011234";

        [TestMethod]
        public void 开关关闭_Disabled()
        {
            Assert.AreEqual(TypeDecision.Disabled,
                TypeGuard.Decide(false, ForegroundKind.Other, Id, null, 0, 0));
        }

        [TestMethod]
        public void 空号_EmptyId()
        {
            Assert.AreEqual(TypeDecision.EmptyId,
                TypeGuard.Decide(true, ForegroundKind.Other, "", null, 0, 0));
            Assert.AreEqual(TypeDecision.EmptyId,
                TypeGuard.Decide(true, ForegroundKind.Other, null, null, 0, 0));
        }

        [TestMethod]
        public void 无前台_NoTarget()
        {
            Assert.AreEqual(TypeDecision.NoTarget,
                TypeGuard.Decide(true, ForegroundKind.None, Id, null, 0, 0));
        }

        [TestMethod]
        public void 前台是桌面_NoTarget()
        {
            Assert.AreEqual(TypeDecision.NoTarget,
                TypeGuard.Decide(true, ForegroundKind.Desktop, Id, null, 0, 0));
        }

        [TestMethod]
        public void 前台是自身_SelfForeground()
        {
            Assert.AreEqual(TypeDecision.SelfForeground,
                TypeGuard.Decide(true, ForegroundKind.Self, Id, null, 0, 0));
        }

        [TestMethod]
        public void 前台其他窗口且无历史_Type()
        {
            Assert.AreEqual(TypeDecision.Type,
                TypeGuard.Decide(true, ForegroundKind.Other, Id, null, 0, 1000));
        }

        [TestMethod]
        public void 同号窗口内_Duplicate_恰好到窗_Type()
        {
            // 按 < dedupeMs：1999ms 去抖，2000ms 放行
            Assert.AreEqual(TypeDecision.Duplicate,
                TypeGuard.Decide(true, ForegroundKind.Other, Id, Id, 0, 1999));
            Assert.AreEqual(TypeDecision.Type,
                TypeGuard.Decide(true, ForegroundKind.Other, Id, Id, 0, 2000));
        }

        [TestMethod]
        public void 不同号_Type()
        {
            Assert.AreEqual(TypeDecision.Type,
                TypeGuard.Decide(true, ForegroundKind.Other, Id, "500101199001011235", 0, 10));
        }

        [TestMethod]
        public void TickCount回绕_unchecked差值仍正确()
        {
            // last = int.MaxValue-100，now = int.MinValue+100：回绕后差值 201ms，应判去抖
            int last = int.MaxValue - 100;
            int now = int.MinValue + 100;
            Assert.AreEqual(TypeDecision.Duplicate,
                TypeGuard.Decide(true, ForegroundKind.Other, Id, Id, last, now));
        }

        [TestMethod]
        public void 历史号为空不去抖_Type()
        {
            Assert.AreEqual(TypeDecision.Type,
                TypeGuard.Decide(true, ForegroundKind.Other, Id, "", 0, 10));
        }
    }
}
