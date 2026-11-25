using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NEIDFill.Core.Typing;

namespace NEIDFill
{
    /// <summary>
    /// 前台窗口分类 + 条码枪语义键盘注入（SendInput 逐字符 KEYEVENTF_UNICODE）。
    /// 不用剪贴板、不 SetForegroundWindow、不点击坐标——只向“当前前台”发键，
    /// 与扫码枪行为一致，目标输入框无需任何配合。
    /// </summary>
    internal static class KeyboardTyper
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x2;
        private const uint KEYEVENTF_UNICODE = 0x4;
        private const ushort VK_RETURN = 0x0D;

        private static readonly int SelfPid = Process.GetCurrentProcess().Id;
        private static readonly int InputSize = Marshal.SizeOf<INPUT>();

        // ================================================================ Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        /// <summary>INPUT 的匿名 union：三者全部 FieldOffset(0)（漏了 MOUSEINPUT 会让联合体尺寸不对）。</summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION U;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // ================================================================ 分类

        /// <summary>当前前台窗口分类：无前台 / 本进程 / 桌面（资源管理器壳窗口）/ 其他。</summary>
        public static ForegroundKind GetForeground(out int pid, out IntPtr hwnd)
        {
            pid = 0;
            hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ForegroundKind.None;

            int fgPid;
            GetWindowThreadProcessId(hwnd, out fgPid);
            pid = fgPid;
            if (fgPid == SelfPid) return ForegroundKind.Self;

            var sb = new StringBuilder(256);
            if (GetClassName(hwnd, sb, sb.Capacity) > 0)
            {
                string cls = sb.ToString();
                // 锁屏/登录界面是安全桌面，注入必失败：归为 Desktop（等价 NoTarget）
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd"
                    || cls == "LogonUI" || cls == "Winlogon")
                    return ForegroundKind.Desktop;
            }
            return ForegroundKind.Other;
        }

        // ================================================================ 注入

        /// <summary>
        /// 键入结果：Completed=全部字符已发出（回车是否成功另看 EnterSent；整体成功＝Completed 且 Error 为 null）；
        /// TypedChars=已发出的字符数，部分中断时上层据此提示“已键入 n/len”；Error=失败原因（成功为 null）。
        /// </summary>
        internal struct TypeResult
        {
            public bool Completed;
            public int TypedChars;
            public bool EnterSent;
            public string Error;
        }

        /// <summary>
        /// 把 text 逐字符以 Unicode 键事件键入 expectedHwnd（判定时的前台窗口），可选追加回车。
        /// 每个字符发送前重新取前台并与 expectedHwnd 比对（防判定后焦点切换的 TOCTOU，也防键入中途切窗）；
        /// SendInput 返回值不足即失败——最常见原因是目标以管理员权限运行（UIPI 拦截注入）。
        /// 中途失败返回部分结果（TypedChars=已发字符数），号码键了一半时上层能如实呈现。
        /// </summary>
        public static TypeResult TypeText(string text, bool appendEnter, int intervalMs, IntPtr expectedHwnd)
        {
            var res = new TypeResult();
            if (string.IsNullOrEmpty(text))
            {
                res.Error = "号码为空";
                return res;
            }

            for (int i = 0; i < text.Length; i++)
            {
                // 每个字符发送前重新取前台：焦点中途变化就按部分结果返回
                int pid;
                IntPtr hwnd;
                ForegroundKind kind = GetForeground(out pid, out hwnd);
                if (kind != ForegroundKind.Other || hwnd != expectedHwnd)
                {
                    res.TypedChars = i;
                    res.Error = "前台已变化";
                    return res;
                }

                var pair = new INPUT[2]; // 单字符 down+up 一次发送，返回值必须等于条数
                pair[0] = UnicodeKey(text[i], false);
                pair[1] = UnicodeKey(text[i], true);
                if (SendInput((uint)pair.Length, pair, InputSize) != pair.Length)
                {
                    res.TypedChars = i;
                    res.Error = "注入被拒绝（目标可能是管理员）";
                    return res;
                }
                if (intervalMs > 0) Thread.Sleep(intervalMs);
            }

            res.Completed = true;
            res.TypedChars = text.Length;

            if (appendEnter)
            {
                // 号码键完到发回车之间焦点也可能已切走：发回车前再校验一次
                int pid;
                IntPtr hwnd;
                ForegroundKind kind = GetForeground(out pid, out hwnd);
                if (kind != ForegroundKind.Other || hwnd != expectedHwnd)
                {
                    res.EnterSent = false;
                    res.Error = "回车未发送：前台已变化";
                    return res;
                }

                var pair = new INPUT[2];
                pair[0] = VirtualKey(VK_RETURN, false);
                pair[1] = VirtualKey(VK_RETURN, true);
                if (SendInput((uint)pair.Length, pair, InputSize) != pair.Length)
                {
                    res.EnterSent = false;
                    res.Error = "回车未发送：注入被拒绝";
                    return res;
                }
                res.EnterSent = true;
            }
            return res;
        }

        private static INPUT UnicodeKey(char ch, bool up)
        {
            var inp = new INPUT { type = INPUT_KEYBOARD };
            inp.U.ki.wVk = 0;
            inp.U.ki.wScan = (ushort)ch;
            inp.U.ki.dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0);
            return inp;
        }

        private static INPUT VirtualKey(ushort vk, bool up)
        {
            var inp = new INPUT { type = INPUT_KEYBOARD };
            inp.U.ki.wVk = vk;
            inp.U.ki.wScan = 0;
            inp.U.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
            return inp;
        }
    }
}
