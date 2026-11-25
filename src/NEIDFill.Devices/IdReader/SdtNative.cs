using System;
using System.Runtime.InteropServices;

namespace NEIDFill.Devices.IdReader
{
    /// <summary>
    /// sdtapi.dll（sfzscan 版）A-family 接口族声明。设备行为模型移植自 NECapture 对精伦 iDR240 的
    /// 实机确证：InitComm(1001) 返回 1 后保持会话，轮询 Authenticate()
    /// 直至返回 1，再按 Routon_DecideIDCardType() 分流读取，返回 1(新读)/0(缓存)。
    /// 本程序必带芯片照片，正文一律走 ReadBaseInfosPhoto（不带照片的 ReadBaseInfos 不再声明）。
    /// SDT_* 族在 iDR240 上寻卡恒 0x80，且用无效 snr 调 SDT_ReadBaseMsg 会在厂商 DLL 内触发
    /// 访问违例（.NET 4 起不可捕获），读卡一律不走 SDT_*。
    /// </summary>
    internal static class SdtNative
    {
        // ---------- 会话与认证 ----------

        /// <summary>打开设备。1=成功，其余=失败。</summary>
        [DllImport("sdtapi.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int InitComm(int port);

        [DllImport("sdtapi.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CloseComm();

        /// <summary>SAM/卡片认证。1=就绪可读取，0=未就绪。读卡前须轮询至 1。</summary>
        [DllImport("sdtapi.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int Authenticate();

        /// <summary>卡片在位检测。1=在位。</summary>
        [DllImport("sdtapi.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CardOn();

        // ---------- 三类证件读取（Routon 族，sfzscan 版 sdtapi.dll 才有导出） ----------

        /// <summary>认证后判定证件类型：100=居民身份证，101=外国人永居证，102=港澳台居住证。</summary>
        [DllImport("sdtapi.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int Routon_DecideIDCardType();

        /// <summary>
        /// 读居民身份证正文+照片。photoPath 语义（NECapture 实测）：传目录，人像写到目录内 photo.jpg；
        /// 传具体 .jpg 文件路径会导致读取失败。返回 1=新读取成功，0=缓存读取，负数=失败。
        /// 字段用 byte[] [Out] 而非 StringBuilder+CharSet.Ansi：Ansi 编组按系统默认代码页解码，
        /// 非中文区域会把 GBK 姓名解成乱码；byte[] 取回原始字节由调用方按 GB18030 解码，与区域无关。
        /// 参数顺序＝DLL 声明顺序：姓名 性别 民族 出生日期 号码 地址 机关 有效期起 有效期止。
        /// </summary>
        [DllImport("sdtapi.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ReadBaseInfosPhoto(
            [Out] byte[] name, [Out] byte[] sex, [Out] byte[] folk, [Out] byte[] birthday,
            [Out] byte[] code, [Out] byte[] address, [Out] byte[] agency,
            [Out] byte[] beginDate, [Out] byte[] endDate, string photoPath);

        /// <summary>
        /// 读外国人永居证全部字段+照片。参数顺序＝原程序声明顺序：
        /// 外文姓名 性别码 性别 号码 国籍码 国籍 中文姓名 出生日期 有效期起 有效期止
        /// 证照版本 签发机关 证件类型 旧预留 15位号码 新预留。
        /// </summary>
        [DllImport("sdtapi.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int Routon_ReadAllForeignBaseInfosNewPhoto(
            [Out] byte[] enName, [Out] byte[] genderCode, [Out] byte[] gender, [Out] byte[] code,
            [Out] byte[] nationCode, [Out] byte[] nation, [Out] byte[] cnName, [Out] byte[] birthday,
            [Out] byte[] beginDate, [Out] byte[] endDate, [Out] byte[] cardVersion, [Out] byte[] agency,
            [Out] byte[] cardType, [Out] byte[] oldFutureItem, [Out] byte[] code15,
            [Out] byte[] newFutureItem, string directory);

        /// <summary>
        /// 读港澳台居住证全部字段+照片。参数顺序＝原程序声明顺序：
        /// 姓名 性别 预留1 出生日期 地址 证件号码 签发机关 有效期起 有效期止
        /// 通行证号 签发次数 预留2 证件类型 预留3。
        /// </summary>
        [DllImport("sdtapi.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int Routon_ReadAllGATBaseInfosPhoto(
            [Out] byte[] name, [Out] byte[] sex, [Out] byte[] futureItem1, [Out] byte[] birthday,
            [Out] byte[] address, [Out] byte[] cardNo, [Out] byte[] agency,
            [Out] byte[] beginDate, [Out] byte[] endDate, [Out] byte[] passId, [Out] byte[] signCnt,
            [Out] byte[] futureItem2, [Out] byte[] cardType, [Out] byte[] futureItem3, string directory);
    }
}
