using System;
using System.Collections.Generic;
using System.Linq;

namespace EyeGuardian.Models
{
    public class DayStats
    {
        public string Date { get; set; } = "";
        public int ShortBreaks { get; set; }
        public int LongBreaks { get; set; }
        public int Skipped { get; set; }
        public int Pauses { get; set; }
        public int FocusSeconds { get; set; }
    }

    public class StatsData
    {
        public List<DayStats> Days { get; set; } = new List<DayStats>();

        private static string Key(DateTime d) => d.ToString("yyyy-MM-dd");

        public DayStats Today() => For(DateTime.Today);

        public DayStats For(DateTime day)
        {
            var k = Key(day);
            var item = Days.FirstOrDefault(x => x.Date == k);
            if (item == null)
            {
                item = new DayStats { Date = k };
                Days.Add(item);
                Prune();
            }
            return item;
        }

        public DayStats On(DateTime day) => Days.FirstOrDefault(x => x.Date == Key(day)) ?? new DayStats { Date = Key(day) };

        public void AddShort() => Today().ShortBreaks++;
        public void AddLong() => Today().LongBreaks++;
        public void AddSkipped() => Today().Skipped++;
        public void AddPause() => Today().Pauses++;
        public void AddFocus(int seconds) => Today().FocusSeconds += seconds;

        public long TotalShort => Days.Sum(d => (long)d.ShortBreaks);
        public long TotalLong => Days.Sum(d => (long)d.LongBreaks);
        public long TotalFocusSeconds => Days.Sum(d => (long)d.FocusSeconds);

        /// <summary>连续达标天数（当天至少完成 1 次休息才算）</summary>
        public int Streak()
        {
            int streak = 0;
            for (int i = 0; i < 400; i++)
            {
                var d = On(DateTime.Today.AddDays(-i));
                if (d.ShortBreaks + d.LongBreaks > 0) streak++;
                else if (i > 0) break;
            }
            return streak;
        }

        private void Prune()
        {
            if (Days.Count > 400)
            {
                Days = Days.OrderByDescending(d => d.Date).Take(400).ToList();
            }
        }
    }
}
