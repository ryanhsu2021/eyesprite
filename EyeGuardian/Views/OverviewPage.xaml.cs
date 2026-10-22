using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EyeGuardian.Models;
using EyeGuardian.Services;

namespace EyeGuardian.Views
{
    public partial class OverviewPage : UserControl
    {
        private const double RingSize = 188;
        private const double RingInset = 5;

        // 画刷只取一次（资源字典里的都是共享实例，不再每帧 new）
        private Brush _accent;
        private Brush _deep;
        private Brush _warn;
        private Brush _muted;
        private Brush _accentSoft;
        private Brush _secondary;

        // 圆环几何复用：只改 ArcSegment 的终点/大小，避免每帧新建 PathGeometry
        private readonly PathFigure _arcFigure = new PathFigure { IsClosed = false, IsFilled = false };
        private readonly ArcSegment _arc = new ArcSegment
        {
            SweepDirection = SweepDirection.Clockwise,
            IsStroked = true
        };
        private readonly PathGeometry _ringGeometry;
        private bool _ringAttached;

        // 上一帧渲染签名：内容没变就整帧跳过
        private GuardState _lastState = (GuardState)(-1);
        private BreakKind _lastKind;
        private bool _lastBreak;
        private bool _lastIdle;
        private int _lastShort = -1, _lastLong = -1, _lastBreakSec = -1;
        private int _lastShortCount = -1, _lastLongCount = -1, _lastFocusMin = -1;

        public OverviewPage()
        {
            InitializeComponent();

            _arcFigure.Segments.Add(_arc);
            _ringGeometry = new PathGeometry(new[] { _arcFigure });

            _accent = (Brush)FindResource("AccentBrush");
            _deep = (Brush)FindResource("AccentDeepBrush");
            _warn = (Brush)FindResource("WarnBrush");
            _muted = (Brush)FindResource("TextMutedBrush");
            _accentSoft = (Brush)FindResource("AccentSoftBrush");
            _secondary = (Brush)FindResource("TextSecondaryBrush");

            RefreshTexts();
        }

        /// <summary>语言或设置变化后刷新的静态文案（不进每帧循环）。</summary>
        public void RefreshTexts()
        {
            ShortUnit.Text = Loc.T("ov.times");
            LongUnit.Text = Loc.T("ov.times");
            FocusUnit.Text = Loc.T("ov.minutes");
            PreviewHint.Text = Loc.T("ov.previewHint");
            PreviewBtn.Content = Loc.T("ov.preview");
            BreakNowBtn.Content = Loc.T("ov.breakNow");
            PauseBtn.Content = Loc.T("ov.pause");
            ResumeBtn.Content = Loc.T("ov.resume");
            StartBtn.Content = Loc.T("ov.start");
            StopBtn.Content = Loc.T("ov.stop");

            var cfg = App.Instance?.Settings;
            Footnote.Text = cfg == null || !cfg.IdleDetection
                ? Loc.T("ov.idleOff")
                : Loc.F("ov.idleOn", Math.Max(1, cfg.IdleThresholdSeconds / 60), Math.Max(1, cfg.AwayResetMinutes));

            _lastState = (GuardState)(-1);   // 文案变了，下一帧必须重绘
        }

        public void Update(GuardSnapshot s, DayStats today)
        {
            int toShort = (int)s.ToShort.TotalSeconds;
            int toLong = (int)s.ToLong.TotalSeconds;
            int breakSec = (int)s.BreakRemaining.TotalSeconds;
            int focusMin = today.FocusSeconds / 60;

            if (_lastState == s.State && _lastKind == s.Kind && _lastBreak == s.InBreak && _lastIdle == s.Idle
                && _lastShort == toShort && _lastLong == toLong && _lastBreakSec == breakSec
                && _lastShortCount == today.ShortBreaks && _lastLongCount == today.LongBreaks
                && _lastFocusMin == focusMin)
                return;

            _lastState = s.State;
            _lastKind = s.Kind;
            _lastBreak = s.InBreak;
            _lastIdle = s.Idle;
            _lastShort = toShort;
            _lastLong = toLong;
            _lastBreakSec = breakSec;
            _lastShortCount = today.ShortBreaks;
            _lastLongCount = today.LongBreaks;
            _lastFocusMin = focusMin;

            SetText(FocusTodayText, Loc.F("ov.focusToday", focusMin));
            SetText(TodayShort, today.ShortBreaks.ToString());
            SetText(TodayLong, today.LongBreaks.ToString());
            SetText(TodayFocus, focusMin.ToString());

            if (s.InBreak)
            {
                SetText(CountdownText, Fmt(s.BreakRemaining));
                SetText(CountdownLabel, Loc.T(s.Kind == BreakKind.Long ? "br.longRemaining" : "ov.breakRemaining"));
                SetRing(1 - s.BreakProgress, _accent);
                SetBadge(Loc.T("st.breaking"), _accent, _deep, _accentSoft);
            }
            else if (s.State == GuardState.Paused)
            {
                SetText(CountdownText, s.PauseIndefinite ? "--:--" : Fmt(s.PauseRemaining));
                SetText(CountdownLabel, Loc.T("ov.pauseRemaining"));
                SetRing(0, _warn);
                SetBadge(Loc.T("st.paused"), _warn, PausedTextBrush, PausedSoftBrush);
            }
            else if (s.State == GuardState.Stopped)
            {
                SetText(CountdownText, "--:--");
                SetText(CountdownLabel, Loc.T("ov.stoppedLabel"));
                SetRing(0, _muted);
                SetBadge(Loc.T("st.stopped"), _muted, _secondary, StoppedSoftBrush);
            }
            else
            {
                SetText(CountdownText, Fmt(s.ToShort));
                SetText(CountdownLabel, s.Idle ? Loc.T("ov.idleShifted") : Loc.T("ov.toNextShort"));
                SetRing(1 - s.ShortProgress, _accent);
                SetBadge(s.Idle ? Loc.T("st.idle") : Loc.T("st.protecting"), _accent, _deep, _accentSoft);
            }

            SetText(ShortCountdownText, s.State == GuardState.Stopped ? "--:--" : Fmt(s.ToShort));
            SetText(LongCountdownText, s.State == GuardState.Stopped ? "--:--" : Fmt(s.ToLong));

            bool canBreak = !s.InBreak && s.State != GuardState.Stopped;
            if (BreakNowBtn.IsEnabled != canBreak) BreakNowBtn.IsEnabled = canBreak;

            var pauseVis = s.State == GuardState.Protecting ? Visibility.Visible : Visibility.Collapsed;
            if (PauseBtn.Visibility != pauseVis) PauseBtn.Visibility = pauseVis;
            var resumeVis = s.State == GuardState.Paused ? Visibility.Visible : Visibility.Collapsed;
            if (ResumeBtn.Visibility != resumeVis) ResumeBtn.Visibility = resumeVis;
            var startVis = s.State == GuardState.Stopped ? Visibility.Visible : Visibility.Collapsed;
            if (StartBtn.Visibility != startVis) StartBtn.Visibility = startVis;
            var stopVis = s.State == GuardState.Stopped ? Visibility.Collapsed : Visibility.Visible;
            if (StopBtn.Visibility != stopVis) StopBtn.Visibility = stopVis;

            // 提示语随语言变化
            var tip = App.Instance?.Engine?.CurrentTip() ?? "";
            SetText(TipText, tip);
        }

        private static void SetText(TextBlock block, string text)
        {
            if (block.Text != text) block.Text = text;
        }

        // 暂停/停止态用的两种浅色，同样只建一次
        private static readonly Brush PausedTextBrush = Freeze(Color.FromRgb(0x9A, 0x64, 0x08));
        private static readonly Brush PausedSoftBrush = Freeze(Color.FromRgb(0xFD, 0xF4, 0xE4));
        private static readonly Brush StoppedSoftBrush = Freeze(Color.FromRgb(0xEF, 0xF3, 0xF1));

        private static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private void SetBadge(string text, Brush fg, Brush fgText, Brush bg)
        {
            SetText(StateText, text);
            if (!ReferenceEquals(StateText.Foreground, fgText)) StateText.Foreground = fgText;
            if (!ReferenceEquals(StateDot.Fill, fg)) StateDot.Fill = fg;
            if (!ReferenceEquals(StateBadge.Background, bg)) StateBadge.Background = bg;
        }

        private void SetRing(double progress, Brush stroke)
        {
            if (!ReferenceEquals(Ring.Stroke, stroke)) Ring.Stroke = stroke;

            double r = (RingSize - RingInset * 2) / 2;
            double cx = RingSize / 2, cy = RingSize / 2;

            double sweep = Math.Clamp(progress, 0, 1) * 360.0;
            if (sweep < 0.4)
            {
                if (_ringAttached) { Ring.Data = null; _ringAttached = false; }
                return;
            }
            if (sweep > 359.4) sweep = 359.4;

            double rad = (sweep - 90) * Math.PI / 180.0;
            _arc.Size = new Size(r, r);
            _arc.IsLargeArc = sweep > 180;
            _arc.Point = new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));

            if (!_ringAttached)
            {
                _arcFigure.StartPoint = new Point(cx, cy - r);
                Ring.Data = _ringGeometry;
                _ringAttached = true;
            }
        }

        private static string Fmt(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
            return $"{t.Minutes:00}:{t.Seconds:00}";
        }

        private void RingArea_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => App.Instance.PreviewBreak();

        private void Preview_Click(object sender, RoutedEventArgs e) => App.Instance.PreviewBreak();

        private void BreakNow_Click(object sender, RoutedEventArgs e) => App.Instance.StartBreak(BreakKind.Short);

        private void Pause_Click(object sender, RoutedEventArgs e) => App.Instance.PauseProtection(TimeSpan.FromMinutes(20));

        private void Resume_Click(object sender, RoutedEventArgs e) => App.Instance.Engine.Resume();

        private void Start_Click(object sender, RoutedEventArgs e) => App.Instance.Engine.Start();

        private void Stop_Click(object sender, RoutedEventArgs e) => App.Instance.Engine.Stop();
    }
}
