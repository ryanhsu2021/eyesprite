using System;
using System.Collections.Generic;

namespace EyeGuardian.Models
{
    /// <summary>全部可配置项，落盘为 %LocalAppData%\EyeSprite\settings.json</summary>
    public class AppSettings
    {
        // 计时（默认：每 30 分钟短休 1 分钟；每 60 分钟长休 5 分钟）
        public int ShortGapMinutes { get; set; } = 30;
        public int ShortDurationMinutes { get; set; } = 1;
        public int LongGapMinutes { get; set; } = 60;
        public int LongDurationMinutes { get; set; } = 5;

        // 行为
        public bool ForceBreak { get; set; } = false;
        /// <summary>空闲检测默认关闭，由用户主动开启（设置页会展开一段原理说明）。</summary>
        public bool IdleDetection { get; set; } = false;
        /// <summary>无键鼠输入多久算离开（秒）。</summary>
        public int IdleThresholdSeconds { get; set; } = 120;
        /// <summary>离开超过多少分钟算「已经休息过」，回来重新计时。</summary>
        public int AwayResetMinutes { get; set; } = 5;
        public bool AlertBeforeLongBreak { get; set; } = true;
        public bool SoundEnabled { get; set; } = true;
        public bool RunAtStartup { get; set; } = false;
        public bool StartMinimized { get; set; } = false;

        /// <summary>配置结构版本，用于升级迁移。</summary>
        public int SettingsVersion { get; set; } = 1;

        /// <summary>auto / zh-CN / en-US</summary>
        public string Language { get; set; } = Services.Loc.Auto;

        public bool FirstRunBalloonShown { get; set; } = false;

        /// <summary>休息提示语列表（勾选启用 / 改写 / 自定义），由 TipCatalog 负责初始填充。</summary>
        public List<TipItem> TipItems { get; set; }

        /// <summary>提示语目录版本，用于软件升级后补入新增的内置条目。</summary>
        public int TipsVersion { get; set; } = 0;

        /// <summary>旧版字段（每行一条），仅用于旧配置迁移，不再写回。</summary>
        public List<string> Tips { get; set; }

        public TimeSpan ShortGap => TimeSpan.FromMinutes(Math.Max(1, ShortGapMinutes));
        public TimeSpan ShortDuration => TimeSpan.FromMinutes(Math.Max(1, ShortDurationMinutes));
        public TimeSpan LongGap => TimeSpan.FromMinutes(Math.Max(5, LongGapMinutes));
        public TimeSpan LongDuration => TimeSpan.FromMinutes(Math.Max(1, LongDurationMinutes));
    }
}
