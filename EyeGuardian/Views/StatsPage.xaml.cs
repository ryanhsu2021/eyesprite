using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EyeGuardian.Models;
using EyeGuardian.Services;

namespace EyeGuardian.Views
{
    public partial class StatsPage : UserControl
    {
        private static readonly SolidColorBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x17, 0xA8, 0x79));
        private static readonly SolidColorBrush LongBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xE3, 0xD1));
        private static readonly SolidColorBrush TrackBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xED));
        private static readonly SolidColorBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x93, 0xA3, 0x9C));
        private static readonly SolidColorBrush TextBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x24, 0x1E));

        public StatsPage()
        {
            InitializeComponent();
            Rebuild();
        }

        public void Rebuild()
        {
            var data = App.Instance?.Stats;
            if (data == null) return;

            var today = data.Today();
            SShort.Text = today.ShortBreaks.ToString();
            SLong.Text = today.LongBreaks.ToString();
            SFocus.Text = (today.FocusSeconds / 60).ToString();
            SSkip.Text = today.Skipped.ToString();
            StreakText.Text = Loc.F("sg.streak", data.Streak());

            int totalBreaks = (int)(data.TotalShort + data.TotalLong);
            double hours = data.TotalFocusSeconds / 3600.0;
            TotalText.Text = Loc.F("sg.totalText", totalBreaks, hours.ToString("0.0"));

            BuildBars(data);
        }

        private void BuildBars(StatsData data)
        {
            var days = new List<DayStats>();
            for (int i = 6; i >= 0; i--)
                days.Add(data.On(DateTime.Today.AddDays(-i)));

            int max = Math.Max(1, days.Max(d => d.ShortBreaks + d.LongBreaks));
            BarsHost.Children.Clear();

            CultureInfo culture;
            try { culture = new CultureInfo(Loc.Culture); } catch { culture = CultureInfo.CurrentCulture; }

            for (int i = 0; i < days.Count; i++)
            {
                var d = days[i];
                var date = DateTime.Today.AddDays(-(6 - i));
                bool isToday = i == days.Count - 1;
                int total = d.ShortBreaks + d.LongBreaks;

                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };

                col.Children.Add(new TextBlock
                {
                    Text = total.ToString(),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = total > 0 ? TextBrush : MutedBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                var track = new Grid { Height = 104, Width = 34, VerticalAlignment = VerticalAlignment.Bottom };
                track.Children.Add(new Border { Background = TrackBrush, CornerRadius = new CornerRadius(8) });

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                double scale = 104.0 / max;
                double hLong = d.LongBreaks * scale;
                double hShort = d.ShortBreaks * scale;

                if (hLong > 0)
                {
                    stack.Children.Add(new Border
                    {
                        Height = Math.Max(4, hLong),
                        Background = LongBrush,
                        CornerRadius = new CornerRadius(7, 7, d.ShortBreaks > 0 ? 0 : 7, d.ShortBreaks > 0 ? 0 : 7)
                    });
                }
                if (hShort > 0)
                {
                    stack.Children.Add(new Border
                    {
                        Height = Math.Max(4, hShort),
                        Background = AccentBrush,
                        CornerRadius = new CornerRadius(d.LongBreaks > 0 ? 0 : 7, d.LongBreaks > 0 ? 0 : 7, 7, 7)
                    });
                }
                if (total == 0)
                {
                    stack.Children.Add(new Border { Height = 3, Background = TrackBrush, CornerRadius = new CornerRadius(2) });
                }

                track.Children.Add(stack);
                col.Children.Add(track);

                col.Children.Add(new TextBlock
                {
                    Text = isToday ? Loc.T("sg.today") : date.ToString("ddd", culture),
                    FontSize = 10.5,
                    Margin = new Thickness(0, 7, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = isToday ? AccentBrush : MutedBrush
                });

                BarsHost.Children.Add(col);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(Loc.T("sg.clearConfirm"), Loc.T("sg.clearTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;

            App.Instance.Stats.Days.Clear();
            StatsStore.Save(App.Instance.Stats);
            Rebuild();
        }
    }
}
