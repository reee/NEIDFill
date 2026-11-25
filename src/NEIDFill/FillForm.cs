using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NEIDFill.Core.IdCard;
using NEIDFill.Core.Settings;
using NEIDFill.Core.Typing;
using NEIDFill.Devices.IdReader;

namespace NEIDFill
{
    /// <summary>
    /// NEIDFill 单窗体：后台自动读卡 → 把身份证号以条码枪语义键入当前前台输入框。
    /// 不落库、不上传、不显示人像；读卡后立即清除临时人像（隐私）。
    /// 读卡器非线程安全：单 worker 独占（Thread + volatile _stop）；停止一律走异步 Join，UI 不冻结。
    /// 全程不 Activate/不抢焦点——抢焦点就等于把键入打回自己（托盘恢复窗口的用户主动操作除外）。
    /// 状态区两行：第一行连接/读卡状态（worker 独占），第二行上次操作结果（HandleCard/保存失败独占），
    /// 互不覆盖；设置保存失败不会被随后的读卡状态冲掉。
    /// 常驻托盘（最小化隐藏到托盘）；运行即自动读卡，退出即断开并释放读卡器。
    /// </summary>
    internal sealed class FillForm : Form
    {
        // Gray/Green/Red 沿用原值；Amber 取 #8A5A00：白底对比度约 5:1（≥WCAG 4.5:1），
        // 比常规黄/橙深，能在浅底卡片上作为「等待/重试」语义色可读。
        // 状态行只用 Amber（连接中/重试）、Green（已连接）、Red（错误）；Gray 用于结果行与组标题。
        private static readonly Color Gray = Color.FromArgb(0x66, 0x66, 0x66);
        private static readonly Color Green = Color.FromArgb(0x15, 0x80, 0x21);
        private static readonly Color Red = Color.FromArgb(0xC6, 0x28, 0x28);
        private static readonly Color Amber = Color.FromArgb(0x8A, 0x5A, 0x00);

        // 状态卡片用整块底色区分状态：已连接＝浅绿，其余（等待/重试/琥珀、错误/红）＝浅灰。
        // 前景语义色在对应浅底上对比度均 ≥4.5:1：Green/浅绿≈4.6，Amber、Red/浅灰≈5.3、5.0。
        private static readonly Color ConnectedBack = Color.FromArgb(0xEA, 0xF7, 0xEC);
        private static readonly Color IdleBack = Color.FromArgb(0xF2, 0xF2, 0xF2);

        private readonly FillSettings _settings;
        private readonly string _settingsPath;

        private UsbIdCardReader _reader;
        private Thread _readWorker;
        private volatile bool _stop;

        // 关窗：取消→异步清理→再 Close 两阶段标志
        private bool _closing;
        private bool _closeReady;

        // 同号去抖：只在成功注入后更新（TypeGuard 契约），worker 线程独占访问
        private string _lastTypedId = "";
        private int _lastTypedTick;

        // —— 控件 ——
        private Panel _stateCard;
        private Label _stateLabel;
        private Label _resultLabel;
        private CheckBox _autoTypeCheck;
        private CheckBox _appendEnterCheck;
        private ToolTip _tip;

        // —— 托盘 ——
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private bool _minimizeHintShown;

        public FillForm(FillSettings settings, string settingsPath)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _settings = settings;
            _settingsPath = settingsPath;
            _reader = new UsbIdCardReader();

            BuildUi();
        }

        // ================================================================ UI 搭建

        private void BuildUi()
        {
            // 标题版本：优先 FileVersion（含构建时刻 yyyy.MM.dd.HHmm），拿不到则回退稳定的 AssemblyVersion
            string ver = null;
            try { ver = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion; }
            catch (Exception) { }
            if (string.IsNullOrEmpty(ver)) ver = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Text = "NEIDFill · 身份证读卡填入 v" + ver;
            Font = new Font("Microsoft YaHei UI", 9F);
            bool hc = SystemInformation.HighContrast;
            BackColor = hc ? SystemColors.Window : Color.White;
            // 缩放只走一条路：exe 声明系统 DPI 感知（app.manifest），控件按 96dpi 设计坐标由 WinForms 自动缩放
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            // 96dpi 设计坐标，8px 网格；100%/125%/150% 缩放下所有尺寸与字体同比例放大，不会裁切
            int w = 480;
            ClientSize = new Size(w, 176);
            _tip = new ToolTip();

            // —— 顶部状态卡片：整块底色区分状态（初始为连接中＝浅灰）；状态/结果各一行，
            // 超长用省略号截断（AutoEllipsis），完整文案在悬停 ToolTip 里 ——
            _stateCard = new Panel
            {
                Location = new Point(16, 16),
                Size = new Size(w - 32, 62),
                BackColor = hc ? SystemColors.Control : IdleBack,
            };
            // 第一行：连接/读卡状态，单行短句。行高实测约 21px、给 26px；宽 416（卡片内左右各留 16px 对称）。
            // Location 的 Y 为 96dpi 设计值 18（初始无结果行＝整卡居中 (62-26)/2）；运行时的精确居中
            // 由 SetResult 按缩放后的实际 Height 重新计算，DPI 缩放下不偏移
            _stateLabel = new Label
            {
                Location = new Point(16, 18),
                Size = new Size(w - 64, 26),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 13F),
                ForeColor = UiColor(Amber),
                AccessibleName = "读卡状态",
                Text = "正在连接读卡器……",
            };
            // 第二行：上次操作结果，持久显示，单行；宽度与状态行一致（左右各 16px 对称）。行高约 17px、给 22px。
            // 初始无结果（尚未读卡）：隐藏，让状态文字独占卡片并垂直居中
            _resultLabel = new Label
            {
                Location = new Point(16, 34),
                Size = new Size(w - 64, 22),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = UiColor(Gray),
                AccessibleName = "上次操作结果",
                Text = "",
                Visible = false,
            };
            _stateCard.Controls.Add(_stateLabel);
            _stateCard.Controls.Add(_resultLabel);
            Controls.Add(_stateCard);
            _tip.SetToolTip(_stateLabel, _stateLabel.Text);

            // —— 组：填入设置（组标题 9pt 灰字；标签统一 4 字，全称进 AccessibleName/ToolTip；两列对齐）——
            // 运行即自动读卡，没有“自动读卡”开关：需要把读卡器让给其它程序时直接退出程序
            Controls.Add(new Label
            {
                Text = "填入设置",
                AutoSize = true,
                ForeColor = hc ? SystemColors.WindowText : Gray,
                Location = new Point(16, 86),
            });
            _autoTypeCheck = new CheckBox
            {
                Text = "自动填入",
                AutoSize = true,
                Checked = _settings.AutoType,
                Location = new Point(16, 108),
                AccessibleName = "自动填入前台输入框",
                TabIndex = 0,
            };
            _appendEnterCheck = new CheckBox
            {
                Text = "追加回车",
                AutoSize = true,
                Checked = _settings.AppendEnter,
                Location = new Point(160, 108),
                AccessibleName = "键入号码后追加回车",
                TabIndex = 1,
            };
            // 开关系列（变更即写回设置文件）
            _autoTypeCheck.CheckedChanged += (s, e) =>
            {
                _settings.AutoType = _autoTypeCheck.Checked;
                SaveSettingsQuiet();
            };
            _appendEnterCheck.CheckedChanged += (s, e) =>
            {
                _settings.AppendEnter = _appendEnterCheck.Checked;
                SaveSettingsQuiet();
            };
            _tip.SetToolTip(_autoTypeCheck, "自动填入前台输入框");
            _tip.SetToolTip(_appendEnterCheck, "键入号码后追加回车");
            Controls.Add(_autoTypeCheck);
            Controls.Add(_appendEnterCheck);

            // —— 底部使用提示：单行，超长省略号截断，全文在悬停 ToolTip 里 ——
            var hint = new Label
            {
                Text = "先点目标输入框并清空；前台是本程序时不填；同号 2 秒只填一次。",
                ForeColor = hc ? SystemColors.WindowText : Gray,
                Location = new Point(16, 140),
                Size = new Size(w - 32, 22),
                AutoEllipsis = true,
            };
            Controls.Add(hint);
            _tip.SetToolTip(hint, hint.Text);

            BuildTray();

            Shown += (s, e) =>
            {
                // 运行即自动读卡：窗口显示即启动读卡 worker
                SetState("正在连接读卡器……", Amber);
                StartWorker();
            };
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon
            {
                Visible = true,
                Text = "NEIDFill · 正在连接读卡器……",
            };
            try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { _tray.Icon = SystemIcons.Application; }

            _trayMenu = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("显示主窗口");
            showItem.Click += (s, e) => RestoreFromTray();
            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += (s, e) => Close();
            _trayMenu.Items.Add(showItem);
            _trayMenu.Items.Add(exitItem);
            _tray.ContextMenuStrip = _trayMenu;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
        }

        /// <summary>从托盘/第二实例请求恢复窗口：Show + 还原 + 激活（用户主动操作，允许抢焦点）。</summary>
        internal void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                Hide(); // 最小化＝隐藏到托盘；关窗仍走退出
                if (!_minimizeHintShown)
                {
                    _minimizeHintShown = true;
                    try { _tray.ShowBalloonTip(2000, "NEIDFill", "已最小化到托盘，双击图标可恢复", ToolTipIcon.Info); }
                    catch (Exception) { }
                }
            }
        }

        // ================================================================ worker 生命周期

        private void StartWorker()
        {
            _stop = false;
            _readWorker = new Thread(ReadWorkerLoop) { IsBackground = true, Name = "read" };
            _readWorker.Start();
        }

        /// <summary>
        /// 异步停止 worker（仅关窗清理用）：Join 放 Task.Run 里，UI 不冻结。
        /// 只有 Join 成功才置空 _readWorker 并 Disconnect——worker 还在原生调用里时
        /// 绝不能并发 Disconnect（非线程安全读卡器）。
        /// </summary>
        private async Task<bool> StopWorkerAsync(int timeoutMs)
        {
            Thread w = _readWorker;
            if (w == null) return true;
            _stop = true;
            try { _reader.CancelRead(); } catch (Exception) { }
            bool joined = await Task.Run(() => w.Join(timeoutMs));
            if (joined)
            {
                _readWorker = null;
                try { _reader.Disconnect(); } catch (Exception) { }   // 释放读卡器硬件
                return true;
            }
            return false;
        }

        /// <summary>读卡循环：未连接自动重试；任何异常都兜住重试，绝不终结进程。worker 只更新状态行。</summary>
        private void ReadWorkerLoop()
        {
            while (!_stop)
            {
                try
                {
                    if (!_reader.IsConnected)
                    {
                        if (!_reader.TryConnect())
                        {
                            // 等待/重试＝琥珀（README：连接成功绿、等待/重试琥珀、错误红）
                            SetState("读卡器未连接，正在重试…", Amber);
                            Thread.Sleep(1500);
                            continue;
                        }
                        // 单行短句，不展示通道细节；超长会被省略号截断（全文在 ToolTip）
                        SetState("已连接读卡器，请放置证件", Green);
                    }
                    if (_stop) break;

                    IdCardSnapshot snap = _reader.Read();
                    HandleCard(snap);

                    _reader.WaitCardRemoved();
                }
                catch (OperationCanceledException)
                {
                    break; // 关窗取消
                }
                catch (InvalidOperationException ex)
                {
                    // 读卡失败只抛异常不断连；若读卡器已被拔出 IsConnected 已为 false，下一轮自动走 TryConnect
                    SetState(ex.Message, Red);
                    Thread.Sleep(1200);
                }
                catch (Exception)
                {
                    // 后台线程未捕获异常会直接终结进程：兜住、断开重试；状态行只报单行短句（异常细节在 crash.log）
                    SetState("读卡异常，正在重试", Red);
                    try { _reader.Disconnect(); } catch (Exception) { }
                    Thread.Sleep(1500);
                }
            }
        }

        // ================================================================ 读卡→判定→键入

        /// <summary>HandleCard 只写结果行：不会被随后的读卡状态覆盖（状态行归 worker）。</summary>
        private void HandleCard(IdCardSnapshot snap)
        {
            string id = IdValidator.Normalize(snap.Code);
            // 隐私：NEIDFill 只取号码，读卡后立即清除临时人像
            try { _reader.ClearPhotoCache(); } catch (Exception) { }

            string name = snap.Name ?? "";
            // 结果行只显示姓名，不显示完整号码（id 仅用于判定与键入）
            string who = string.IsNullOrEmpty(name) ? "（无姓名）" : name;

            ForegroundKind kind = KeyboardTyper.GetForeground(out _, out IntPtr fgHwnd);
            TypeDecision decision = TypeGuard.Decide(
                _settings.AutoType, kind, id, _lastTypedId, _lastTypedTick, Environment.TickCount);

            switch (decision)
            {
                case TypeDecision.Type:
                    KeyboardTyper.TypeResult res = KeyboardTyper.TypeText(id, _settings.AppendEnter, 10, fgHwnd);
                    if (res.Completed && res.Error == null)
                    {
                        _lastTypedId = id;
                        _lastTypedTick = Environment.TickCount;
                        SystemSounds.Asterisk.Play();
                        SetResult("已填入：" + who, Green);
                    }
                    else if (res.TypedChars == id.Length)
                    {
                        // 整段号码已键入（只是回车等后续失败）：去抖同样更新，避免重读卡再键一遍
                        _lastTypedId = id;
                        _lastTypedTick = Environment.TickCount;
                        SystemSounds.Exclamation.Play();
                        SetResult("已填入，回车未发送（请手动回车）", Red);
                    }
                    else if (res.TypedChars > 0)
                    {
                        SystemSounds.Exclamation.Play();
                        SetResult("已键入 " + res.TypedChars + "/" + id.Length + " 字符，请清空后重试", Red);
                    }
                    else
                    {
                        SystemSounds.Exclamation.Play();
                        SetResult("未填入：" + res.Error, Red);
                    }
                    break;

                case TypeDecision.SelfForeground:
                    SystemSounds.Exclamation.Play();
                    SetResult("本程序在前台，未填入", Red);
                    break;

                case TypeDecision.NoTarget:
                    SystemSounds.Exclamation.Play();
                    SetResult("前台无可输入窗口，未填入", Red);
                    break;

                case TypeDecision.Disabled:
                    SetResult("已读卡：" + who + "（未填入）", Gray);
                    break;

                case TypeDecision.EmptyId:
                    SystemSounds.Exclamation.Play();
                    SetResult("读到的号码为空，未填入", Red);
                    break;

                case TypeDecision.Duplicate:
                    SetResult("同号 2 秒内已填入，本次跳过", Gray);
                    break;
            }
        }

        private void SaveSettingsQuiet()
        {
            // 保存失败只写结果行：不会被 worker 的读卡状态覆盖
            try { _settings.Save(_settingsPath); }
            catch (Exception) { SetResult("设置保存失败", Red); }
        }

        // ================================================================ 关窗清理

        /// <summary>
        /// 取消关闭→同步快清理＋异步停 worker→再 Close。同步部分不碰 Join，UI 停顿远小于 100ms；
        /// Join 超时则跳过 Disconnect（进程即将退出，句柄由 OS 回收）。
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_closeReady)
            {
                base.OnFormClosing(e);
                return;
            }
            if (_closing)
            {
                e.Cancel = true; // 清理期间的重复关闭请求
                return;
            }
            _closing = true;
            e.Cancel = true;
            Enabled = false; // 清理期间（最长 1.5s）禁止继续交互，避免与异步清理竞态

            // 快清理（全部瞬时操作）：设置落盘、托盘释放
            try { _settings.Save(_settingsPath); } catch (Exception) { }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            if (_trayMenu != null) { _trayMenu.Dispose(); _trayMenu = null; }
            _tip.Dispose(); // 标签 ToolTip 全文提示随窗体退出释放

            CloseCleanupAsync();
        }

        private async void CloseCleanupAsync()
        {
            if (_readWorker == null)
            {
                try { _reader.Disconnect(); } catch (Exception) { } // 释放读卡器（Disconnect 会清临时人像）
            }
            else
            {
                // StopWorkerAsync：Join 在 Task.Run 内；仅 Join 成功才 Disconnect
                await StopWorkerAsync(1500);
            }
            _closeReady = true;
            Close();
        }

        // ================================================================ 线程封送与状态显示

        /// <summary>高对比度下语义色失效：一律用 SystemColors.WindowText（保证与窗体/卡片底色对比）。</summary>
        private static Color UiColor(Color c)
        {
            return SystemInformation.HighContrast ? SystemColors.WindowText : c;
        }

        /// <summary>状态行（worker 独占）：文字 + 前景色 + 卡片整块底色 + 托盘文字 + ToolTip 全文。</summary>
        private void SetState(string text, Color color)
        {
            Post(() =>
            {
                // 底色按原始语义色区分（UiColor 之前）：Green＝浅绿底，其余＝浅灰底；
                // 高对比度下不用自定义浅色：Green＝Window，其余＝Control，文字仍 WindowText
                bool hc = SystemInformation.HighContrast;
                _stateCard.BackColor = color == Green
                    ? (hc ? SystemColors.Window : ConnectedBack)
                    : (hc ? SystemColors.Control : IdleBack);
                Color c = UiColor(color);
                _stateLabel.Text = text;
                _stateLabel.ForeColor = c;
                if (!_closing) _tip.SetToolTip(_stateLabel, text); // 清理期 _tip 已释放，跳过
                if (_tray != null)
                {
                    // NotifyIcon.Text 在 .NET Framework 上限 63 字符：超长直接截断（ToolTip 里仍有全文）；
                    // 状态文案已是单行，替换换行只是兜底
                    string t = "NEIDFill · " + text.Replace('\n', ' ');
                    _tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
                }
            });
        }

        /// <summary>
        /// 结果行（HandleCard/设置保存失败独占）：持久显示，下一次结果才覆盖；ToolTip 全文。
        /// 空文本＝无结果：隐藏结果行，状态行按实际高度在整卡垂直居中；
        /// 有结果：状态行与结果行（含 2px 间隙）整体按实际高度居中。
        /// 坐标一律由运行时 Height 推导——控件尺寸已随 AutoScaleMode.Dpi 缩放，
        /// 96dpi 下无结果 state Top 18；有结果 state Top 6 / result Top 34，125%/150% 下自适应不偏移。
        /// Top/Visible 只在结果更新时调整，SetState 不动布局，避免 worker 高频状态刷新引起抖动。
        /// </summary>
        private void SetResult(string text, Color color)
        {
            Post(() =>
            {
                int cardH = _stateCard.ClientSize.Height;
                if (string.IsNullOrEmpty(text))
                {
                    _resultLabel.Visible = false;
                    _stateLabel.Top = (cardH - _stateLabel.Height) / 2;
                }
                else
                {
                    const int gap = 2; // 两行文本块之间的视觉间隙
                    int total = _stateLabel.Height + gap + _resultLabel.Height;
                    int top = (cardH - total) / 2;
                    _stateLabel.Top = top;
                    _resultLabel.Top = top + _stateLabel.Height + gap;
                    _resultLabel.Visible = true;
                }
                _resultLabel.Text = text;
                _resultLabel.ForeColor = UiColor(color);
                if (!_closing) _tip.SetToolTip(_resultLabel, text); // 清理期 _tip 已释放，跳过
            });
        }

        private void Post(Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch (Exception) { } // 关窗竞态时控件已死：吞掉，不炸 worker
        }
    }
}
