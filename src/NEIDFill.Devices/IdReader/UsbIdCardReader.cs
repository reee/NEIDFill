using System;
using System.IO;
using System.Threading;
using NEIDFill.Core.IdCard;

namespace NEIDFill.Devices.IdReader
{
    /// <summary>
    /// USB 身份证读卡器（sdtapi.dll A-family + Routon 三类卡分流）。
    /// 会话/轮询/复位行为模型移植自 NECapture 对精伦 iDR240 的实机确证：
    /// 1) InitComm(1001)==1 后保持会话不断开（原程序 worker 同款）；
    /// 2) Read() 无条件高频轮询 Authenticate()（原程序 worker 同款）：Authenticate()==1 是「新鲜放卡」的唯一可靠边沿，
    ///    任何前置 CardOn 调用都可能把该边沿推迟，某些固件首次读卡前 CardOn 甚至恒为 0，故绝不用 CardOn 做读取门控；
    ///    CardOn() 只作「卡在位」诊断：连续在位超过尝试预算仍未读成才报错，无卡在位期间做空闲自愈；
    /// 3) 认证 1 后先 Routon_DecideIDCardType() 判型再按型读取；返回码可能瞬时 -4，300ms 级重试可恢复；
    ///    返回 1=新读（数据有效），0=缓存读（防同卡连读的关键防线，一律视为失败继续轮询）。
    /// 本程序只取号码不读回人像：芯片人像由厂商 DLL 写入临时目录内 photo.jpg，本类只负责清理该临时文件。
    /// 读卡失败只抛异常、不断开连接；仅设备级异常（拔线/驱动）才复位连接。
    /// 空闲时每 60 秒静默重建会话（CloseComm+InitComm），保证拔插读卡器后旧句柄失效也能自愈；重建失败抛异常交上层转自动重连。
    /// 非线程安全：由调用方保证单实例使用。
    /// </summary>
    public sealed class UsbIdCardReader
    {
        private static readonly int[] PortCandidates = { 1001, 2001, 2002 };

        /// <summary>读卡轮询间隔（CardOn 等卡与 Authenticate 尝试共用；原程序 worker 亦为高频轮询）。</summary>
        private const int PollIntervalMs = 150;

        /// <summary>检测到卡在位后的尝试预算：覆盖用户按提示「拿开重放」的 1～2 轮操作；无卡时不计时间。</summary>
        private const int ReadBudgetMs = 30000;

        /// <summary>空闲静默重建会话的间隔：读卡器被拔插导致旧句柄失效时靠它自愈；健康设备上重建无感。</summary>
        private const int IdleReconnectMs = 60000;

        private volatile bool _open;
        private volatile bool _cancelRead;
        private readonly int _preferredPort;
        private readonly string _photoDir;

        /// <param name="preferredPort">FillSettings.ReaderPort，0 表示自动探测。</param>
        public UsbIdCardReader(int preferredPort)
        {
            _preferredPort = preferredPort;
            // 照片临时目录放 TEMP 而非 exe 目录：装机到 Program Files 后普通权限不可写
            _photoDir = Path.Combine(Path.GetTempPath(), "NEIDFill_chip");
            try { Directory.CreateDirectory(_photoDir); } catch (Exception) { }
        }

        public bool IsConnected { get { return _open; } }

        public bool TryConnect(out string method)
        {
            _cancelRead = false;
            return TryConnectCore(out method);
        }

        /// <summary>断开并重建会话（Disconnect + 端口探测）。不碰 _cancelRead：供 Read 空闲自愈静默调用，避免清掉并发到达的取消。</summary>
        private bool TryConnectCore(out string method)
        {
            method = null;
            Disconnect();

            // 指定端口先探一遍；失败后回退候选时跳过重复端口，并把回退信息写进 method 供 UI 展示
            if (_preferredPort > 0 && SdtNative.InitComm(_preferredPort) == 1)
            {
                _open = true;
                method = "USB 身份证阅读器（通道 " + _preferredPort + "）";
                return true;
            }

            foreach (int port in PortCandidates)
            {
                if (port == _preferredPort) continue; // 指定端口刚探过，不重复
                if (SdtNative.InitComm(port) != 1) continue;

                _open = true;
                method = "USB 身份证阅读器（通道 " + port + "）";
                if (_preferredPort > 0)
                    method += "；指定通道 " + _preferredPort + " 不可用";
                return true;
            }
            return false;
        }

        public IdCardSnapshot Read()
        {
            if (!_open)
                throw new InvalidOperationException("读卡器未连接");

            // 预算耗尽后的失败原因（分型诊断），在 try 外抛出以免被设备异常复位路径吞掉
            string failureMessage = null;
            try
            {
                // 无条件高频轮询 Authenticate()，不用 CardOn 做任何前置门控（参见类注释第 2 条）
                string lastFailure = null;
                // 用差值而非 deadline 比较：TickCount 在系统运行 ~24.9 天后溢出为负，直接比较会瞬间超时
                int idleStart = Environment.TickCount; // 无卡计时起点（空闲重建用）
                int onSince = Environment.TickCount;   // 连续在位起点
                bool onCard = false;                   // 当前是否判定为在位
                while (failureMessage == null)
                {
                    // 取消优先：每轮先查 _cancelRead，重建也不吞取消
                    if (_cancelRead) throw new OperationCanceledException("读卡已取消");

                    if (SdtNative.Authenticate() == 1)
                    {
                        // 每次读卡前清掉上一张的人像文件：临时 photo.jpg 绝不在两次读卡之间驻留
                        TryDeletePhoto();

                        IdCardSnapshot snap;
                        string failure;
                        int type = SdtNative.Routon_DecideIDCardType();
                        if (type == 100) ReadNormal(out snap, out failure);
                        else if (type == 101) ReadForeign(out snap, out failure);
                        else if (type == 102) ReadGat(out snap, out failure);
                        else if (type > 0)
                        {
                            snap = null;
                            failure = "未识别的卡类型（" + type + "）。支持：大陆居民身份证、外国人永久居留证、港澳台居民居住证";
                        }
                        else
                        {
                            snap = null;
                            failure = "证件类型判定失败（返回码 " + type + "），请重新放卡";
                        }

                        if (snap != null) return snap;
                        // 返回码瞬时 -4 / 缓存读 / 号码不完整：记下分型原因，下个轮询拍重试（实测 300ms 级重试可恢复）。
                        lastFailure = failure;
                    }

                    // CardOn 仅作在位诊断：连续在位达预算仍没读成才报错；卡拿开则重置在位计时，不算失败
                    if (SdtNative.CardOn() == 1)
                    {
                        if (!onCard) { onCard = true; onSince = Environment.TickCount; }
                        idleStart = Environment.TickCount; // 卡在位期间不累计无卡空闲时间
                        if (unchecked(Environment.TickCount - onSince) >= ReadBudgetMs)
                        {
                            // 预算内从未认证成功则报认证未就绪，否则报最近一次分型失败原因
                            failureMessage = lastFailure ??
                                "已检测到证件，但认证未就绪。请将证件拿开 3 秒后重新平放在读卡器正中央";
                        }
                    }
                    else
                    {
                        onCard = false;
                        // 无卡空闲自愈：每 60 秒静默重建会话，读卡器被拔插后旧句柄失效也能恢复；
                        // 静默重建（不碰 _cancelRead）；失败说明设备确实不在，_open 已置 false，
                        // 抛异常交上层走 !IsConnected → TryConnect 的自动重试路径，设备插回后自愈
                        if (unchecked(Environment.TickCount - idleStart) >= IdleReconnectMs)
                        {
                            if (!TryConnectCore(out _))
                                throw new InvalidOperationException("读卡器连接已断开，正在重新连接……");
                            idleStart = Environment.TickCount;
                        }
                    }

                    Thread.Sleep(PollIntervalMs);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // 读卡过程中设备被拔出/驱动异常：复位连接，下一次读线程才会重新 InitComm
                Disconnect();
                throw;
            }

            // 普通读卡失败/超时：只抛异常不断连，上层显示错误后继续等卡
            throw new InvalidOperationException(failureMessage);
        }

        // ================================================================ 三类分流读取

        // 三类读取统一 void + out failure：调用方据此区分「rc != 1」与「rc == 1 但校验失败」
        private void ReadNormal(out IdCardSnapshot snap, out string failure)
        {
            // 缓冲尺寸按原程序 StringBuilder 容量放大取整；内容按 GB18030 解码（与系统区域代码页无关）
            var name = new byte[256];
            var sex = new byte[64];
            var folk = new byte[64];
            var birthday = new byte[64];
            var code = new byte[64];
            var address = new byte[512];
            var agency = new byte[256];
            var beginDate = new byte[64];
            var endDate = new byte[64];

            int rc = SdtNative.ReadBaseInfosPhoto(
                name, sex, folk, birthday, code, address, agency, beginDate, endDate, _photoDir);
            snap = null;
            failure = null;
            if (rc != 1)
            {
                failure = "读取证件失败（返回码 " + rc + "），请将证件拿开 3 秒后重新平放";
                return;
            }

            string id = Decode(code);
            // 只认 1=新读且号码完整：0=缓存读（卡一直静置时返回上一张数据）会造成同一张卡反复触发；
            // 芯片数据可信但偶发空缓冲，号码不完整按失败继续轮询。
            if (id.Length != 18 || !IdValidator.IsValid(id))
            {
                failure = "号码校验未通过，请将证件拿开 3 秒后重新平放";
                return;
            }

            snap = new IdCardSnapshot
            {
                Name = Decode(name),
                Sex = Decode(sex),
                Folk = Decode(folk),
                Birthday = Decode(birthday),
                Code = id,
                Address = Decode(address),
                Agency = Decode(agency),
                ExpireStart = Decode(beginDate),
                ExpireEnd = Decode(endDate),
            };
        }

        private void ReadForeign(out IdCardSnapshot snap, out string failure)
        {
            var enName = new byte[512];
            var genderCode = new byte[64];
            var gender = new byte[64];
            var code = new byte[128];
            var nationCode = new byte[64];
            var nation = new byte[128];
            var cnName = new byte[128];
            var birthday = new byte[64];
            var beginDate = new byte[64];
            var endDate = new byte[64];
            var cardVersion = new byte[64];
            var agency = new byte[64];
            var cardType = new byte[64];
            var oldFutureItem = new byte[64];
            var code15 = new byte[64];
            var newFutureItem = new byte[128];

            int rc = SdtNative.Routon_ReadAllForeignBaseInfosNewPhoto(
                enName, genderCode, gender, code, nationCode, nation, cnName, birthday,
                beginDate, endDate, cardVersion, agency, cardType, oldFutureItem, code15,
                newFutureItem, _photoDir);
            snap = null;
            failure = null;
            if (rc != 1)
            {
                failure = "读取证件失败（返回码 " + rc + "），请将证件拿开 3 秒后重新平放";
                return;
            }

            // 与原程序一致：姓名取外文姓名、民族列填国籍；永居证无住址字段，地址留空
            string id = Decode(code);
            if (id.Length == 0)
            {
                failure = "证件号码为空，读取失败";
                return;
            }
            snap = new IdCardSnapshot
            {
                Name = Decode(enName),
                Sex = Decode(gender),
                Folk = Decode(nation),
                Birthday = Decode(birthday),
                Code = id,
                Address = "",
                Agency = Decode(agency),
                ExpireStart = Decode(beginDate),
                ExpireEnd = Decode(endDate),
            };
        }

        private void ReadGat(out IdCardSnapshot snap, out string failure)
        {
            var name = new byte[128];
            var sex = new byte[64];
            var futureItem1 = new byte[64];
            var birthday = new byte[64];
            var address = new byte[512];
            var cardNo = new byte[128];
            var agency = new byte[128];
            var beginDate = new byte[64];
            var endDate = new byte[64];
            var passId = new byte[64];
            var signCnt = new byte[64];
            var futureItem2 = new byte[64];
            var cardType = new byte[64];
            var futureItem3 = new byte[64];

            int rc = SdtNative.Routon_ReadAllGATBaseInfosPhoto(
                name, sex, futureItem1, birthday, address, cardNo, agency, beginDate, endDate,
                passId, signCnt, futureItem2, cardType, futureItem3, _photoDir);
            snap = null;
            failure = null;
            if (rc != 1)
            {
                failure = "读取证件失败（返回码 " + rc + "），请将证件拿开 3 秒后重新平放";
                return;
            }

            // 与原程序一致：港澳台证民族列留空（证面无民族项）
            string id = Decode(cardNo);
            if (id.Length == 0)
            {
                failure = "证件号码为空，读取失败";
                return;
            }
            snap = new IdCardSnapshot
            {
                Name = Decode(name),
                Sex = Decode(sex),
                Folk = "",
                Birthday = Decode(birthday),
                Code = id,
                Address = Decode(address),
                Agency = Decode(agency),
                ExpireStart = Decode(beginDate),
                ExpireEnd = Decode(endDate),
            };
        }

        // ================================================================ 照片清理与卡位

        private string PhotoFile { get { return Path.Combine(_photoDir, "photo.jpg"); } }

        private void TryDeletePhoto()
        {
            try { if (File.Exists(PhotoFile)) File.Delete(PhotoFile); } catch (Exception) { }
        }

        /// <summary>NEIDFill 只取号码，读卡后立即清除临时人像（隐私：临时 photo.jpg 不驻留）。</summary>
        public void ClearPhotoCache() { TryDeletePhoto(); }

        public void WaitCardRemoved()
        {
            // 自动连读防重：同卡留在读卡区时 Authenticate 会再次返回 1（原程序用 CardOn 门控，同款语义）。
            // 无时间上限——操作员一直不拿开就一直等；CancelRead/断连随时可脱身。
            while (!_cancelRead && _open)
            {
                int on;
                try { on = SdtNative.CardOn(); }
                catch (Exception) { return; } // 设备异常：交由下一轮 Read 的复位路径处理
                if (on != 1) return;
                Thread.Sleep(PollIntervalMs);
            }
            if (_cancelRead) throw new OperationCanceledException("读卡已取消");
        }

        public void CancelRead()
        {
            _cancelRead = true;
        }

        public void Disconnect()
        {
            if (_open)
            {
                try { SdtNative.CloseComm(); } catch { }
                _open = false;
                // 现场是公共机器：断连（含关窗）时清掉临时目录里最后一张芯片人像，不留隐私残留
                TryDeletePhoto();
            }
        }

        /// <summary>厂商 DLL 按 GBK/GB18030 写缓冲：取到首个 NUL 为止的字节按 GB18030 解码。</summary>
        private static string Decode(byte[] buf)
        {
            return Gb18030Codec.Decode(buf);
        }
    }
}
