using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace EyeGuardian.Services
{
    /// <summary>
    /// 运行时多语言。切换方式：替换合并进 App 资源里的 Lang.*.xaml 字典，
    /// XAML 全部用 DynamicResource 引用 key，因此换字典即整套界面文字跟着变，无需重建窗口。
    /// </summary>
    public static class Loc
    {
        public const string Auto = "auto";
        public const string Zh = "zh-CN";
        public const string En = "en-US";

        private static ResourceDictionary _dict;

        /// <summary>当前实际生效的语言。</summary>
        public static string Culture { get; private set; } = Zh;

        /// <summary>系统语言（只区分中文 / 英文，其他语言回退英文）。</summary>
        public static string SystemCulture()
        {
            try
            {
                var c = CultureInfo.InstalledUICulture ?? CultureInfo.CurrentUICulture;
                if (c != null && c.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
                    return Zh;
                return En;
            }
            catch { return Zh; }
        }

        /// <summary>把设置里的值（auto / zh-CN / en-US）解析成实际语言。</summary>
        public static string Resolve(string setting)
        {
            if (string.IsNullOrEmpty(setting) || setting == Auto) return SystemCulture();
            return setting.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? En : Zh;
        }

        public static void Apply(string culture)
        {
            Culture = culture == En ? En : Zh;

            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/Lang.{Culture}.xaml", UriKind.Absolute)
            };

            var merged = Application.Current?.Resources?.MergedDictionaries;
            if (merged != null)
            {
                if (_dict != null) merged.Remove(_dict);
                merged.Add(dict);
            }
            _dict = dict;

            try
            {
                var ci = new CultureInfo(Culture);
                CultureInfo.DefaultThreadCurrentCulture = ci;
                CultureInfo.DefaultThreadCurrentUICulture = ci;
            }
            catch { }
        }

        /// <summary>取当前语言的文案；找不到时回退 key 本身（便于发现漏翻）。</summary>
        public static string T(string key)
        {
            try { return Application.Current?.TryFindResource(key) as string ?? key; }
            catch { return key; }
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }

        /// <summary>中文界面用「30 分钟」，英文用「30 min」。</summary>
        public static string Minutes(int value) => $"{value} {T("unit.min")}";
    }
}
