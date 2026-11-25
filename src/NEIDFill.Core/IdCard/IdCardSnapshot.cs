using System;

namespace NEIDFill.Core.IdCard
{
    /// <summary>
    /// 单次读卡快照。字段取三类证件（100 居民/101 外国人永居/102 港澳台）的公共集，
    /// 语义映射与原程序上报表一致：外国人 Name=外文姓名、Folk=国籍；港澳台 Folk 为空。
    /// </summary>
    public sealed class IdCardSnapshot
    {
        public string Name = "";
        public string Sex = "";
        public string Folk = "";
        public string Birthday = "";
        public string Code = "";
        public string Address = "";
        public string Agency = "";
        public string ExpireStart = "";
        public string ExpireEnd = "";
    }
}
