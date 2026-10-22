using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EyeGuardian.Models;
using EyeGuardian.Services;

namespace EyeGuardian.Views
{
    public partial class MainWindow : Window
    {
        // 只有概览是首屏，统计/设置按需创建：少建两棵可视化树，启动更快、常驻内存更小
        private readonly OverviewPage _overview = new OverviewPage();
        private StatsPage _stats;
        private SettingsPage _settings;

        private StatsPage StatsView => _stats ??= new StatsPage();
        private SettingsPage SettingsView => _settings ??= new SettingsPage();
        private bool _forceClose;

        // 状态圆点的画刷只建一次并冻结：之前每 300ms new 一个 SolidColorBrush
        private static readonly Brush DotPaused = Frozen(Color.FromRgb(0xF6, 0xC9, 0x6B));
        private static readonly Brush DotStopped = Frozen(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
        private static readonly Brush DotBreak = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush DotIdle = Frozen(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        private static readonly Brush DotProtecting = Frozen(Color.FromRgb(0x8F, 0xE8, 0xC4));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public MainWindow()
        {
            InitializeComponent();
            Host.Content = _overview;

            // 版本号从程序集读，避免和 csproj 脱节
            try
            {
                var v = typeof(MainWindow).Assembly.GetName().Version;
                VersionText.Text = "v" + (v == null ? "0.0" : v.ToString(3));
            }
            catch { }

            IsVisibleChanged += (s, e) => { if (IsVisible) App.Instance?.RefreshUi(); };
        }

        public void UpdateView(GuardSnapshot snap, DayStats today)
        {
            // 窗口收在托盘时不做任何界面更新：文本重排、几何生成都白费
            if (!IsVisible) return;

            if (ReferenceEquals(Host.Content, _overview)) _overview.Update(snap, today);

            string status;
            Brush dot;
            if (snap.State == GuardState.Paused) { status = Loc.T("st.paused"); dot = DotPaused; }
            else if (snap.State == GuardState.Stopped) { status = Loc.T("st.stopped"); dot = DotStopped; }
            else if (snap.InBreak) { status = Loc.T("st.breaking"); dot = DotBreak; }
            else if (snap.Idle) { status = Loc.T("st.idle"); dot = DotIdle; }
            else { status = Loc.T("st.protecting"); dot = DotProtecting; }

            SetText(SideStatus, status);
            if (!ReferenceEquals(StateDot.Fill, dot)) StateDot.Fill = dot;
            SetText(SideNextShort, Loc.T("ov.toNextShort") + " " + Fmt(snap.ToShort));
            SetText(SideNextLong, Loc.T("ov.toNextLong") + " " + Fmt(snap.ToLong));
            SetText(SideTodayBreaks, Loc.F("side.breaks", today.ShortBreaks, today.LongBreaks));
            SetText(SideTodayFocus, Loc.F("side.focus", today.FocusSeconds / 60));
        }

        /// <summary>只在内容真的变了才赋值，避免无谓的布局失效。</summary>
        private static void SetText(TextBlock block, string text)
        {
            if (block.Text != text) block.Text = text;
        }

        public void RefreshStats()
        {
            _stats?.Rebuild();
        }

        public void SelectNav(int index)
        {
            Nav.SelectedIndex = index;
        }

        public void ScrollSettingsToBottom()
        {
            _settings?.ScrollToBottom();
        }

        /// <summary>语言切换后刷新非 XAML 绑定部分。</summary>
        public void RefreshLanguage()
        {
            _stats?.Rebuild();
            _settings?.ReloadTexts();
            _overview.RefreshTexts();
        }

        private static string Fmt(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
            return $"{t.Minutes:00}:{t.Seconds:00}";
        }

        private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Host == null) return;
            switch (Nav.SelectedIndex)
            {
                case 1:
                    Host.Content = StatsView;
                    StatsView.Rebuild();
                    break;
                case 2:
                    Host.Content = SettingsView;
                    break;
                default:
                    Host.Content = _overview;
                    // 切回概览时立刻按当前状态渲染一次（隐藏/其他页时不刷新的补偿）
                    _overview.RefreshTexts();
                    App.Instance?.RefreshUi();
                    break;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Min_Click(object sender, RoutedEventArgs e) => Hide();

        private void Close_Click(object sender, RoutedEventArgs e) => Hide();

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_forceClose)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }
    }
}
