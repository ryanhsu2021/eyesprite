using System;
using System.IO;
using System.Text.Json;
using EyeGuardian.Models;

namespace EyeGuardian.Services
{
    public static class Paths
    {
        public static string Dir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EyeSprite");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string Settings => Path.Combine(Dir, "settings.json");
        public static string Stats => Path.Combine(Dir, "stats.json");
        public static string Log => Path.Combine(Dir, "app.log");
    }

    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { WriteIndented = true };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(Paths.Settings))
                {
                    var json = File.ReadAllText(Paths.Settings);
                    var s = JsonSerializer.Deserialize<AppSettings>(json, Opt);
                    if (s != null)
                    {
                        TipCatalog.Sync(s);
                        s.ShortGapMinutes = Math.Clamp(s.ShortGapMinutes, 1, 240);
                        s.ShortDurationMinutes = Math.Clamp(s.ShortDurationMinutes, 1, 60);
                        s.LongGapMinutes = Math.Clamp(s.LongGapMinutes, 5, 480);
                        s.LongDurationMinutes = Math.Clamp(s.LongDurationMinutes, 1, 60);
                        s.IdleThresholdSeconds = Math.Clamp(s.IdleThresholdSeconds, 60, 600);
                        s.AwayResetMinutes = Math.Clamp(s.AwayResetMinutes, 1, 30);

                        // v1：空闲检测改为默认关闭，老配置里的「开启」统一收回，由用户自己决定
                        if (s.SettingsVersion < 1)
                        {
                            s.IdleDetection = false;
                            s.SettingsVersion = 1;
                            Save(s);
                        }
                        return s;
                    }
                }
            }
            catch (Exception ex) { Log.Write("settings load: " + ex.Message); }
            var fresh = new AppSettings();
            TipCatalog.Sync(fresh);
            return fresh;
        }

        public static void Save(AppSettings s)
        {
            try { File.WriteAllText(Paths.Settings, JsonSerializer.Serialize(s, Opt)); }
            catch (Exception ex) { Log.Write("settings save: " + ex.Message); }
        }
    }

    public static class StatsStore
    {
        private static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { WriteIndented = false };

        public static StatsData Load()
        {
            try
            {
                if (File.Exists(Paths.Stats))
                {
                    var data = JsonSerializer.Deserialize<StatsData>(File.ReadAllText(Paths.Stats), Opt);
                    if (data != null) { data.Days ??= new System.Collections.Generic.List<DayStats>(); return data; }
                }
            }
            catch (Exception ex) { Log.Write("stats load: " + ex.Message); }
            return new StatsData();
        }

        public static void Save(StatsData d)
        {
            try { File.WriteAllText(Paths.Stats, JsonSerializer.Serialize(d, Opt)); }
            catch (Exception ex) { Log.Write("stats save: " + ex.Message); }
        }
    }

    public static class Log
    {
        public static void Write(string msg)
        {
            try { File.AppendAllText(Paths.Log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}"); }
            catch { }
        }
    }
}
