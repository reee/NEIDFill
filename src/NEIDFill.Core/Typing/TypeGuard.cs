using System;

namespace NEIDFill.Core.Typing
{
    /// <summary>键入目标（当前前台窗口）的分类。</summary>
    public enum ForegroundKind { None, Self, Desktop, Other }

    /// <summary>是否允许把身份证号键入前台输入框的判定结果。</summary>
    public enum TypeDecision { Type, Disabled, EmptyId, NoTarget, SelfForeground, Duplicate }

    /// <summary>
    /// 键入前置判定（纯逻辑，无 Win32 依赖，便于单测）。
    /// 约定：id 由调用方先归一化（去空白/升位）；lastTypedTick 只在成功注入后更新；
    /// 时间用 Environment.TickCount 的差值比较，unchecked 处理其约 24.9 天的溢出回绕。
    /// </summary>
    public static class TypeGuard
    {
        /// <summary>同一号码在该时间窗内重复读卡只键入一次。</summary>
        public const int DefaultDedupeMs = 2000;

        /// <summary>按固定优先级判定：开关 → 空号 → 目标可用性 → 自身前台 → 同号去抖。</summary>
        public static TypeDecision Decide(bool enabled, ForegroundKind target, string id,
            string lastTypedId, int lastTypedTick, int nowTick, int dedupeMs = DefaultDedupeMs)
        {
            if (!enabled) return TypeDecision.Disabled;
            if (string.IsNullOrEmpty(id)) return TypeDecision.EmptyId;
            if (target == ForegroundKind.None || target == ForegroundKind.Desktop) return TypeDecision.NoTarget;
            if (target == ForegroundKind.Self) return TypeDecision.SelfForeground;
            // TickCount 差值用 unchecked 相减，回绕（约 24.9 天）时仍得正确差值
            if (!string.IsNullOrEmpty(lastTypedId)
                && string.Equals(lastTypedId, id, StringComparison.Ordinal))
            {
                int diff = unchecked(nowTick - lastTypedTick);
                if (diff >= 0 && diff < dedupeMs) return TypeDecision.Duplicate;
            }
            return TypeDecision.Type;
        }
    }
}
