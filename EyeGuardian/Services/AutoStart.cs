using System;
using Microsoft.Win32;

namespace EyeGuardian.Services
{
    /// <summary>通过 HKCU\...\Run 实现开机自启，无需管理员权限。</summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "EyeSprite";

        public static string ExePath
        {
            get
            {
                var p = Environment.ProcessPath;
                return string.IsNullOrEmpty(p) ? "" : p;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                var v = key?.GetValue(ValueName) as string;
                return !string.IsNullOrEmpty(v);
            }
            catch { return false; }
        }

        public static void Set(bool enabled)
        {
            try
            {
                // Run 键在某些精简系统上可能不存在，直接 CreateSubKey 更稳
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return;
                if (enabled)
                {
                    var exe = ExePath;
                    if (!string.IsNullOrEmpty(exe))
                        key.SetValue(ValueName, "\"" + exe + "\" --minimized");
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
        }
    }
}
