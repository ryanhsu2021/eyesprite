using System.Text.Json.Serialization;

namespace EyeGuardian.Models
{
    /// <summary>
    /// 一条休息提示语。
    /// Text 为空表示沿用内置目录条目（文案随语言切换）；非空表示用户改写过，固定使用该文本。
    /// </summary>
    public class TipItem
    {
        /// <summary>内置条目 id（如 "blink"），自定义条目为 "custom:xxxxxxxx"。</summary>
        public string Id { get; set; } = "";

        /// <summary>用户改写的文案；为空时使用内置文案。</summary>
        public string Text { get; set; } = "";

        /// <summary>是否勾选启用。</summary>
        public bool Enabled { get; set; } = true;

        [JsonIgnore]
        public bool IsCustom => Id != null && Id.StartsWith("custom:", System.StringComparison.Ordinal);
    }
}
