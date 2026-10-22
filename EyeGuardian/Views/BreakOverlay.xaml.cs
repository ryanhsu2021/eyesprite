using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EyeGuardian.Services;
using WinForms = System.Windows.Forms;

namespace EyeGuardian.Views
{
    public partial class BreakOverlay : Window
    {
        private readonly bool _isPrimary;
        private readonly bool _forceBreak;
        private readonly bool _preview;
        private bool _allowClose;

        // 休息页每 300ms 刷一次：静态文案只写一次，圆环几何复用
        private bool _staticReady;
        private int _lastSecond = -1;
        private int _lastMinute = -1;
        private readonly PathFigure _arcFigure = new PathFigure { IsClosed = false, IsFilled = false };
        private readonly ArcSegment _arc = new ArcSegment { SweepDirection = SweepDirection.Clockwise, IsStroked = true };
        private PathGeometry _ringGeometry;
        private bool _ringAttached;

        public event Action SkipRequested;

        public BreakOverlay(bool isPrimary, bool forceBreak, bool preview = false)
        {
            InitializeComponent();
            _isPrimary = isPrimary;
            _forceBreak = forceBreak;
            _preview = preview;

            if (!isPrimary)
            {
                MainPanel.Visibility = Visibility.Collapsed;
                PreviewBadge.Visibility = Visibility.Collapsed;
                SecondaryText.Visibility = Visibility.Visible;
            }

            if (_preview)
            {
                PreviewBadge.Visibility = isPrimary ? Visibility.Visible : Visibility.Collapsed;
                SkipBtn.Content = Loc.T("br.leavePreview");
            }
            else
            {
                SkipBtn.Content = Loc.T("br.skip");
            }

            _arcFigure.Segments.Add(_arc);
            _ringGeometry = new PathGeometry(new[] { _arcFigure });

            SkipBtn.Visibility = _forceBreak && !_preview ? Visibility.Collapsed : Visibility.Visible;
            BrandText.Text = Loc.T("br.brand");

            if (_isPrimary)
            {
                try { ((Storyboard)FindResource("Breath")).Begin(this); } catch { }
            }

            PreviewKeyDown += OnKeyDown;
            MouseRightButtonUp += OnRightClick;
            SizeChanged += (s, e) => ApplyScale();
        }

        /// <summary>按屏幕尺寸等比放大内容，避免高分辨率屏上休息页显得太小。</summary>
        private void ApplyScale()
        {
            if (!_isPrimary || ActualWidth < 200 || ActualHeight < 200) return;
            double k = Math.Min(ActualWidth / 1800.0, ActualHeight / 1000.0);
            k = Math.Clamp(k, 1.0, 1.7);
            MainPanel.LayoutTransform = new ScaleTransform(k, k);
        }

        public void ShowOnScreen(WinForms.Screen screen)
        {
            var bounds = screen.Bounds;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            Show();

            // 用 Win32 按物理像素精确铺满目标显示器（多显示器 + 不同缩放最稳）
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
                bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                NativeMethods.SWP_SHOWWINDOW);

            if (_isPrimary)
            {
                Activate();
                Focus();
            }
        }

        public void UpdateView(GuardSnapshot s)
        {
            var now = DateTime.Now;
            if (now.Minute != _lastMinute)
            {
                _lastMinute = now.Minute;
                var clock = now.ToString("HH:mm");
                if (ClockText.Text != clock) ClockText.Text = clock;
            }

            int second = (int)Math.Round(Math.Max(0, s.BreakRemaining.TotalSeconds));
            if (second == _lastSecond) return;   // 同一秒内不重复排版
            _lastSecond = second;

            if (_isPrimary)
            {
                if (!_staticReady)
                {
                    _staticReady = true;
                    var tip = App.Instance?.Engine?.CurrentTip() ?? "";
                    if (s.Kind == BreakKind.Long)
                    {
                        KindText.Text = Loc.T("br.longTitle");
                        TipText.Text = string.IsNullOrEmpty(tip) ? Loc.T("br.longTip") : tip;
                        UnitText.Text = Loc.T("br.longRemaining");
                    }
                    else
                    {
                        KindText.Text = Loc.T("br.shortTitle");
                        TipText.Text = tip;
                        UnitText.Text = Loc.T("br.remaining");
                    }
                }

                var text = Fmt(s.BreakRemaining);
                if (CountText.Text != text) CountText.Text = text;
                SetRing(1 - s.BreakProgress);
            }
            else
            {
                var text = $"{Loc.T("st.breaking")} · {Fmt(s.BreakRemaining)}";
                if (SecondaryText.Text != text) SecondaryText.Text = text;
            }
        }

        private void SetRing(double progress)
        {
            const double size = 380;
            const double margin = 26;
            double r = (size - margin * 2) / 2;
            double cx = size / 2, cy = size / 2;

            double sweep = Math.Clamp(progress, 0, 1) * 360.0;
            if (sweep < 0.4) { if (Ring.Data != null) Ring.Data = null; _ringAttached = false; return; }
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

        /// <summary>统一 mm:ss，不再出现「秒」单位。</summary>
        private static string Fmt(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int total = (int)Math.Ceiling(t.TotalSeconds);
            return $"{total / 60:00}:{total % 60:00}";
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_forceBreak && !_preview) return;
            if (e.Key == Key.Escape)
            {
                SkipRequested?.Invoke();
                e.Handled = true;
            }
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            if (_forceBreak && !_preview) return;
            SkipRequested?.Invoke();
        }

        private void Skip_Click(object sender, RoutedEventArgs e) => SkipRequested?.Invoke();

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        public void CloseForced()
        {
            _allowClose = true;
            try { Close(); } catch { }
        }
    }
}
