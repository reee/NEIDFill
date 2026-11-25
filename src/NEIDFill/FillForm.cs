using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Reflection;
using System.Threading;
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
    /// 读卡器非线程安全：单 worker 独占（Thread + volatile _stop）。
    /// 全程不 Activate/不抢焦点——抢焦点就等于把键入打回自己。
    /// </summary>
    internal sealed class FillForm : Form
    {
        private static readonly Color Gray = Color.FromArgb(0x66, 0x66, 0x66);
        private static readonly Color Green = Color.FromArgb(0x15, 0x80, 0x21);
        private static readonly Color Red = Color.FromArgb(0xC6, 0x28, 0x28);

        private readonly FillSettings _settings;
        private readonly string _settingsPath;

        private UsbIdCardReader _reader;
        private Thread _readWorker;
        private volatile bool _stop;

        // 同号去抖：只在成功注入后更新（TypeGuard 契约），worker 线程独占访问
        private string _lastTypedId = "";
        private int _lastTypedTick;

        // —— 控件 ——
        private Label _status;
        private Label _nameValue;
        private CheckBox _autoReadCheck;
        private CheckBox _autoTypeCheck;
        private CheckBox _appendEnterCheck;
        private NumericUpDown _portBox;
        private Button _applyBtn;

        public FillForm(FillSettings settings, string settingsPath)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _settings = settings;
            _settingsPath = settingsPath;
            _reader = new UsbIdCardReader(settings.ReaderPort);

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
            Text = "身份证读卡填入 · NEIDFill v" + ver;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;
            // 缩放只走一条路：exe 声明系统 DPI 感知（app.manifest），控件按 96dpi 设计坐标由 WinForms 自动缩放
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            int w = 470;
            ClientSize = new Size(w, 250);

            // —— 顶部状态 ——
            _status = new Label
            {
                Location = new Point(16, 14),
                Size = new Size(w - 32, 24),
                AutoEllipsis = true,
                ForeColor = Gray,
                Text = "正在连接读卡器……",
            };
            Controls.Add(_status);

            // —— 中部浅灰卡片：只有一行姓名（号码不再上界面）——
            var card = new Panel
            {
                Location = new Point(16, 44),
                Size = new Size(w - 32, 48),
                BackColor = Color.FromArgb(0xF4, 0xF4, 0xF4),
            };
            var lblName = new Label
            {
                Text = "姓名",
                AutoSize = true,
                ForeColor = Gray,
                Location = new Point(12, 12),
            };
            _nameValue = new Label
            {
                Text = "",
                AutoSize = true,
                MaximumSize = new Size(card.Width - 80, 0),
                Location = new Point(52, 12),
            };
            card.Controls.Add(lblName);
            card.Controls.Add(_nameValue);
            Controls.Add(card);

            // —— 自动读卡开关（仅界面态，不写设置文件）——
            _autoReadCheck = new CheckBox
            {
                Text = "自动读卡",
                AutoSize = true,
                Checked = true,
                Location = new Point(18, 104),
            };
            _autoReadCheck.CheckedChanged += (s, e) => ApplyAutoReadState();
            Controls.Add(_autoReadCheck);

            // —— 开关系列（变更即写回设置文件）——
            _autoTypeCheck = new CheckBox
            {
                Text = "自动填入前台输入框",
                AutoSize = true,
                Checked = _settings.AutoType,
                Location = new Point(110, 104),
            };
            _appendEnterCheck = new CheckBox
            {
                Text = "输入后回车",
                AutoSize = true,
                Checked = _settings.AppendEnter,
                Location = new Point(265, 104),
            };
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
            Controls.Add(_autoTypeCheck);
            Controls.Add(_appendEnterCheck);

            // —— 端口行 ——
            var lblPort = new Label
            {
                Text = "端口",
                AutoSize = true,
                ForeColor = Gray,
                Location = new Point(16, 140),
            };
            _portBox = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 9999,
                Value = _settings.ReaderPort,
                Location = new Point(54, 136),
                Size = new Size(70, 26),
            };
            _applyBtn = new Button
            {
                Text = "应用",
                Location = new Point(134, 134),
                Size = new Size(64, 30),
            };
            _applyBtn.Click += (s, e) => ApplyPort();
            var lblPortHint = new Label
            {
                Text = "0 = 自动探测",
                AutoSize = true,
                ForeColor = Gray,
                Location = new Point(210, 140),
            };
            Controls.Add(lblPort);
            Controls.Add(_portBox);
            Controls.Add(_applyBtn);
            Controls.Add(lblPortHint);

            // —— 底部使用提示 ——
            var hint = new Label
            {
                Text = "使用前把光标放进目标输入框并清空；本程序在前台或桌面时不会填入。同号 2 秒内只填一次。",
                ForeColor = Gray,
                Location = new Point(16, 178),
                Size = new Size(w - 32, 56),
            };
            Controls.Add(hint);

            Shown += (s, e) =>
            {
                if (_autoReadCheck.Checked) StartWorker();
                else SetStatus("自动读卡已暂停（勾选「自动读卡」恢复）", Gray);
            };
        }

        // ================================================================ worker 生命周期

        private void StartWorker()
        {
            _stop = false;
            _readWorker = new Thread(ReadWorkerLoop) { IsBackground = true, Name = "read" };
            _readWorker.Start();
        }

        /// <summary>自动读卡开关：取消勾选＝停 worker 并释放读卡器；重新勾选＝重启（绝不允许双 worker 并存）。</summary>
        private void ApplyAutoReadState()
        {
            if (_autoReadCheck.Checked)
            {
                if (_readWorker != null && _readWorker.IsAlive)
                {
                    // 旧 worker 还没退出：绝不启动第二个 worker 抢同一个非线程安全读卡器
                    SetStatus("读卡线程尚未退出，请稍后重试", Red);
                    return;
                }
                SetStatus("自动读卡中，正在等待证件……", Green);
                StartWorker();
            }
            else
            {
                _stop = true;
                try { _reader.CancelRead(); } catch (Exception) { }
                bool joined = _readWorker == null || _readWorker.Join(3000);
                if (!joined)
                {
                    // 线程卡在原生调用里：不 Disconnect（会并发访问非线程安全读卡器），
                    // 保持未勾选；worker 脱身后因 _stop 自行退出，用户再勾一次即可恢复
                    SetStatus("正在暂停：读卡线程尚未退出，请稍后重试", Red);
                    return;
                }
                _readWorker = null;
                try { _reader.Disconnect(); } catch (Exception) { }   // 释放读卡器硬件
                SetStatus("自动读卡已暂停（勾选「自动读卡」恢复）", Gray);
            }
        }

        /// <summary>读卡循环：未连接自动重试；任何异常都兜住重试，绝不终结进程。</summary>
        private void ReadWorkerLoop()
        {
            while (!_stop)
            {
                try
                {
                    if (!_reader.IsConnected)
                    {
                        string method;
                        if (!_reader.TryConnect(out method))
                        {
                            SetStatus("读卡器未连接，正在自动重试……", Red);
                            Thread.Sleep(1500);
                            continue;
                        }
                        SetStatus("自动读卡中 · 已连接 " + method + "，请放置证件", Green);
                    }
                    if (_stop) break;

                    IdCardSnapshot snap = _reader.Read();
                    HandleCard(snap);

                    _reader.WaitCardRemoved();
                }
                catch (OperationCanceledException)
                {
                    break; // 端口重应用/关窗取消
                }
                catch (InvalidOperationException ex)
                {
                    // 读卡失败只抛异常不断连；若读卡器已被拔出 IsConnected 已为 false，下一轮自动走 TryConnect
                    SetStatus(ex.Message, Red);
                    Thread.Sleep(1200);
                }
                catch (Exception ex)
                {
                    // 后台线程未捕获异常会直接终结进程：兜住、断开重试
                    SetStatus("读卡异常：" + ex.Message, Red);
                    try { _reader.Disconnect(); } catch (Exception) { }
                    Thread.Sleep(1500);
                }
            }
        }

        // ================================================================ 读卡→判定→键入

        private void HandleCard(IdCardSnapshot snap)
        {
            string id = IdValidator.Normalize(snap.Code);
            // 隐私：NEIDFill 只取号码，读卡后立即清除临时人像
            try { _reader.ClearPhotoCache(); } catch (Exception) { }

            string name = snap.Name ?? "";
            // 状态栏只显示姓名，不显示完整号码（id 仅用于判定与键入）
            string who = string.IsNullOrEmpty(name) ? "（无姓名）" : name;
            Post(() => { _nameValue.Text = name; });

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
                        SetStatus("已填入：" + who, Green);
                    }
                    else if (res.TypedChars == id.Length)
                    {
                        // 整段号码已键入（只是回车等后续失败）：去抖同样更新，避免重读卡再键一遍
                        _lastTypedId = id;
                        _lastTypedTick = Environment.TickCount;
                        SystemSounds.Exclamation.Play();
                        SetStatus("号码已填入，但回车未发送：" + res.Error + "（请手动回车）", Red);
                    }
                    else if (res.TypedChars > 0)
                    {
                        SystemSounds.Exclamation.Play();
                        SetStatus("已键入 " + res.TypedChars + "/" + id.Length + " 个字符后中断：" + res.Error + "；请先清空输入框再重试", Red);
                    }
                    else
                    {
                        SystemSounds.Exclamation.Play();
                        SetStatus("未填入：" + res.Error, Red);
                    }
                    break;

                case TypeDecision.SelfForeground:
                    SystemSounds.Exclamation.Play();
                    SetStatus("未填入：本程序在前台，请切到目标输入框后重新读卡", Red);
                    break;

                case TypeDecision.NoTarget:
                    SystemSounds.Exclamation.Play();
                    SetStatus("未填入：当前前台不是可输入窗口（或停留在桌面）", Red);
                    break;

                case TypeDecision.Disabled:
                    SetStatus("已读卡：" + who + "（自动填入已关闭）", Gray);
                    break;

                case TypeDecision.EmptyId:
                    SystemSounds.Exclamation.Play();
                    SetStatus("读到的号码为空，未填入", Red);
                    break;

                case TypeDecision.Duplicate:
                    SetStatus("同号 2 秒内已填入，本次跳过", Gray);
                    break;
            }
        }

        // ================================================================ 端口热应用

        /// <summary>换端口＝停 worker→断开→重建 reader→重启；worker 没退出就本次不重启；「自动读卡」未勾选则保持暂停。</summary>
        private void ApplyPort()
        {
            _stop = true;
            try { _reader.CancelRead(); } catch (Exception) { }
            if (_readWorker != null && !_readWorker.Join(3000))
            {
                // Join 超时说明 worker 卡在原生调用里：不解散对象、不新建 worker（避免双 worker 抢非线程安全读卡器）。
                // _stop 保持 true：worker 脱身后自行退出，用户再点一次「应用」即可重试。
                SetStatus("读卡线程未退出，请稍后重试", Red);
                return;
            }
            _readWorker = null;
            try { _reader.Disconnect(); } catch (Exception) { }

            int port = (int)_portBox.Value;
            _reader = new UsbIdCardReader(port);
            _settings.ReaderPort = port;
            try { _settings.Save(_settingsPath); }
            catch (Exception ex) { SetStatus("端口已生效，但设置保存失败：" + ex.Message, Red); }
            if (_autoReadCheck.Checked)
            {
                StartWorker();
            }
            else
            {
                // 暂停状态下换端口：只应用端口，不启动 worker，保持暂停
                try { _reader.Disconnect(); } catch (Exception) { }
                SetStatus("端口已应用，自动读卡已暂停（勾选「自动读卡」恢复）", Gray);
            }
        }

        private void SaveSettingsQuiet()
        {
            try { _settings.Save(_settingsPath); }
            catch (Exception ex) { SetStatus("设置保存失败：" + ex.Message, Red); }
        }

        // ================================================================ 关窗清理

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _stop = true;
            try { _reader.CancelRead(); } catch (Exception) { }
            // 只有 worker 退出才 Disconnect：读卡器非线程安全，worker 还在原生调用里就会并发访问。
            // Join 超时则跳过清理——进程即将退出，句柄由 OS 回收（与 ApplyPort 的做法一致）。
            bool joined = _readWorker == null || _readWorker.Join(3000);
            if (joined)
            {
                try { _reader.Disconnect(); } catch (Exception) { } // Disconnect 会清临时人像
            }
            try { _settings.Save(_settingsPath); } catch (Exception) { }
            base.OnFormClosing(e);
        }

        // ================================================================ 线程封送

        private void SetStatus(string text, Color color)
        {
            Post(() =>
            {
                _status.Text = text;
                _status.ForeColor = color;
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
