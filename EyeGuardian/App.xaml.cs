using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EyeGuardian.Models;
using EyeGuardian.Services;
using EyeGuardian.Views;
using WinForms = System.Windows.Forms;

namespace EyeGuardian
{
    public partial class App : Application
    {
        public static App Instance => (App)Current;

        public AppSettings Settings { get; private set; }
        public StatsData Stats { get; private set; }
        public GuardEngine Engine { get; private set; }
        public TrayService Tray { get; private set; }
        public MainWindow MainWin { get; private set; }

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly List<BreakOverlay> _overlays = new List<BreakOverlay>();
        private readonly List<BreakOverlay> _preview = new List<BreakOverlay>();
        private DateTime _previewEnd;
        private Mutex _mutex;
        private int _focusSinceSave;
        private bool _exiting;
        private bool _captureMode;

        private void OnStartup(object sender, StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                Log.Write("unhandled: " + args.Exception);
                args.Handled = true;
            };

            bool startMin = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            Settings = SettingsStore.Load();
            Loc.Apply(Loc.Resolve(Settings.Language));

            // 诊断：把空闲检测的三个信号写进 probe.txt 后退出（不启动界面、不占单实例锁）
            if (e.Args.Any(a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase)))
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(Paths.Dir, "probe.txt"), IdleDetector.Report()); }
                catch (Exception ex) { Log.Write("probe: " + ex.Message); }
                Shutdown();
                return;
            }

            _mutex = new Mutex(true, "EyeSprite.SingleInstance.v1", out bool created);
            if (!created)
            {
                MessageBox.Show(Loc.T("msg.multiInstance"), Loc.T("app.name"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            Stats = StatsStore.Load();

            // 注册表为准，回写设置（用户可能手动清掉了启动项）
            Settings.RunAtStartup = AutoStart.IsEnabled();

            Engine = new GuardEngine(Settings);
            Engine.BreakStarted += OnBreakStarted;
            Engine.BreakEnded += OnBreakEnded;
            Engine.LongBreakSoon += OnLongBreakSoon;
            Engine.AwaySettled += OnAwaySettled;

            Tray = new TrayService();
            Tray.ShowRequested += ShowMainWindow;
            Tray.BreakRequested += () => Engine.StartBreakManually(BreakKind.Short);
            Tray.PauseRequested += () => PauseProtection(TimeSpan.FromMinutes(20));
            Tray.ResumeRequested += () => Engine.Resume();
            Tray.StopRequested += () => Engine.Stop();
            Tray.StartRequested += () => Engine.Start();
            Tray.ExitRequested += ExitApp;

            MainWin = new MainWindow();

            Engine.Start();

            _timer.Tick += Timer_Tick;
            _timer.Start();

            if (!Settings.StartMinimized && !startMin)
                ShowMainWindow();

            if (!Settings.FirstRunBalloonShown)
            {
                Tray.Balloon(Loc.T("tr.balloonTitle"), Loc.T("tr.balloonText"));
                Settings.FirstRunBalloonShown = true;
                SettingsStore.Save(Settings);
            }

            if (e.Args.Any(a => a.Equals("--shot", StringComparison.OrdinalIgnoreCase)))
                CaptureSequence();
            else if (e.Args.Any(a => a.Equals("--shot-en", StringComparison.OrdinalIgnoreCase)))
                CaptureSequence(english: true);
        }

        /// <summary>
        /// 关机 / 注销：先落盘再放行。
        /// 主窗平时是「关闭即隐藏」，若不先强制关闭，系统会当成应用阻止关机。
        /// </summary>
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            try { SettingsStore.Save(Settings); StatsStore.Save(Stats); } catch { }
            try { MainWin?.ForceClose(); } catch { }
            base.OnSessionEnding(e);
        }

        // ---------- 语言 ----------

        /// <summary>按设置重新装载语言资源，并刷新非 XAML 绑定的文案。</summary>
        public void ApplyLanguage()
        {
            Loc.Apply(Loc.Resolve(Settings.Language));
            Engine?.InvalidateTip();      // 提示语文案随语言变化
            Tray?.ApplyLanguage();
            MainWin?.RefreshLanguage();
        }

        // ---------- 对外操作（界面按钮调用） ----------

        public void PauseProtection(TimeSpan duration)
        {
            Engine.Pause(duration);
            Stats.AddPause();
        }

        public void StartBreak(BreakKind kind) => Engine.StartBreakManually(kind);

        public void ShowMainWindow()
        {
            if (MainWin == null) return;
            MainWin.Show();
            if (MainWin.WindowState == WindowState.Minimized) MainWin.WindowState = WindowState.Normal;
            MainWin.Activate();
            MainWin.Topmost = true;
            MainWin.Topmost = false;
            MainWin.Focus();
        }

        /// <summary>立即按当前状态刷新一次主窗（从托盘恢复、切回概览时用）。</summary>
        public void RefreshUi()
        {
            if (MainWin == null || !MainWin.IsVisible) return;
            MainWin.UpdateView(Engine.Snapshot(), Stats.Today());
        }

        public void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            _timer.Stop();
            try { StatsStore.Save(Stats); SettingsStore.Save(Settings); } catch { }
            CloseBreakWindows();
            ClosePreview();
            try { Engine?.Dispose(); } catch { }
            Tray?.Dispose();
            MainWin?.ForceClose();
            Shutdown();
        }

        // ---------- 计时循环 ----------

        private void Timer_Tick(object sender, EventArgs e)
        {
            Engine.Tick();
            var snap = Engine.Snapshot();

            Tray?.Update(snap);
            // 主窗收在托盘时跳过界面刷新：文本重排与几何生成全是白费
            if (MainWin != null && MainWin.IsVisible) MainWin.UpdateView(snap, Stats.Today());

            for (int i = 0; i < _overlays.Count; i++)
                _overlays[i].UpdateView(snap);

            UpdatePreview();

            int focus = Engine.TakeFocusSeconds();
            if (focus > 0 && !_captureMode)
            {
                Stats.AddFocus(focus);
                _focusSinceSave += focus;
            }
            if (_focusSinceSave >= 300)
            {
                _focusSinceSave = 0;
                StatsStore.Save(Stats);
            }
        }

        // ---------- 休息窗口 ----------

        private void OnBreakStarted()
        {
            ClosePreview();
            CloseBreakWindows();
            try
            {
                foreach (var screen in WinForms.Screen.AllScreens)
                {
                    var overlay = new BreakOverlay(screen.Primary, Settings.ForceBreak);
                    overlay.SkipRequested += () => Engine.SkipBreak();
                    _overlays.Add(overlay);
                    overlay.ShowOnScreen(screen);
                }
                if (Settings.SoundEnabled)
                {
                    try { SystemSounds.Asterisk.Play(); } catch { }
                }
            }
            catch (Exception ex) { Log.Write("break window: " + ex.Message); }
        }

        private void OnBreakEnded(bool completed)
        {
            CloseBreakWindows();
            if (!_captureMode)
            {
                if (completed)
                {
                    if (Engine.Kind == BreakKind.Short) Stats.AddShort();
                    else Stats.AddLong();
                }
                else
                {
                    Stats.AddSkipped();
                }
                StatsStore.Save(Stats);
            }
            MainWin?.RefreshStats();
        }

        private void CloseBreakWindows()
        {
            for (int i = _overlays.Count - 1; i >= 0; i--)
            {
                try { _overlays[i].CloseForced(); } catch { }
            }
            _overlays.Clear();
        }

        private void OnLongBreakSoon()
        {
            Tray?.Balloon(Loc.T("tr.longSoonTitle"), Loc.T("tr.longSoonText"));
        }

        /// <summary>离开够久回到电脑前：视为已经休息过，计时从头开始。</summary>
        private void OnAwaySettled(TimeSpan away)
        {
            if (_captureMode) return;
            int minutes = Math.Max(1, (int)Math.Round(away.TotalMinutes));
            Tray?.Balloon(Loc.T("tr.awayTitle"), Loc.F("tr.awayText", minutes));
        }

        // ---------- 预览休息画面（不计时、不计统计） ----------

        private static readonly TimeSpan PreviewLength = TimeSpan.FromSeconds(10);

        public void PreviewBreak()
        {
            if (Engine.InBreak || _preview.Count > 0) return;
            try
            {
                _previewEnd = DateTime.Now + PreviewLength;
                foreach (var screen in WinForms.Screen.AllScreens)
                {
                    var overlay = new BreakOverlay(screen.Primary, Settings.ForceBreak, preview: true);
                    overlay.SkipRequested += ClosePreview;
                    _preview.Add(overlay);
                    overlay.ShowOnScreen(screen);
                }
            }
            catch (Exception ex) { Log.Write("preview: " + ex.Message); }
        }

        private void UpdatePreview()
        {
            if (_preview.Count == 0) return;

            var remaining = _previewEnd - DateTime.Now;
            if (remaining <= TimeSpan.Zero) { ClosePreview(); return; }

            var snap = new GuardSnapshot
            {
                State = GuardState.Protecting,
                InBreak = true,
                Kind = BreakKind.Short,
                BreakRemaining = remaining,
                BreakDuration = PreviewLength,
                BreakProgress = 1 - remaining.TotalSeconds / PreviewLength.TotalSeconds
            };
            for (int i = 0; i < _preview.Count; i++) _preview[i].UpdateView(snap);
        }

        private void ClosePreview()
        {
            for (int i = _preview.Count - 1; i >= 0; i--)
            {
                try { _preview[i].CloseForced(); } catch { }
            }
            _preview.Clear();
        }

        // ---------- 开发期自检：把各界面渲染成 PNG ----------

        private async void CaptureSequence(bool english = false)
        {
            _captureMode = true;
            var savedLang = Settings.Language;
            try
            {
                // 截图期间忽略空闲检测，否则会被判成 Idle 状态（不改动用户设置）
                Engine.SuppressIdle = true;
                ShowMainWindow();   // 启动最小化时主窗没显示过，ActualWidth 为 0 会截出空图
                await Task.Delay(800);
                string prefix = english ? "shot-en-" : "shot-";

                if (english)
                {
                    Settings.Language = Loc.En;
                    ApplyLanguage();
                    SelectNavSafe(0);
                    await Task.Delay(600);
                }

                Capture(MainWin, prefix + "overview.png");

                MainWin.SelectNav(1);
                await Task.Delay(350);
                Capture(MainWin, prefix + "stats.png");

                MainWin.SelectNav(2);
                await Task.Delay(350);
                Capture(MainWin, prefix + "settings.png");

                MainWin.ScrollSettingsToBottom();
                await Task.Delay(350);
                Capture(MainWin, prefix + "settings2.png");

                if (english)
                {
                    MainWin.SelectNav(0);
                    await Task.Delay(250);
                    PreviewBreak();
                    await Task.Delay(900);
                    if (_preview.Count > 0) Capture(_preview[0], prefix + "break.png");
                    await Task.Delay(300);
                    ClosePreview();
                }
                else
                {
                    MainWin.SelectNav(0);
                    await Task.Delay(250);
                    Engine.StartBreakManually(BreakKind.Short);
                    await Task.Delay(900);
                    if (_overlays.Count > 0) Capture(_overlays[0], prefix + "break.png");
                    await Task.Delay(300);
                    Engine.SkipBreak();
                }
            }
            catch (Exception ex) { Log.Write("capture: " + ex.Message); }
            finally
            {
                // 还原语言/空闲设置，避免截图模式污染用户设置
                Settings.Language = savedLang;
                Engine.SuppressIdle = false;
                try { Loc.Apply(Loc.Resolve(savedLang)); MainWin?.RefreshLanguage(); } catch { }
                await Task.Delay(400);
                ExitApp();
            }
        }

        private void SelectNavSafe(int index)
        {
            try { MainWin.SelectNav(index); } catch { }
        }

        private static void Capture(Window w, string fileName)
        {
            try
            {
                if (w == null) return;
                w.UpdateLayout();
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
                int width = (int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX);
                int height = (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY);
                if (width <= 0 || height <= 0) return;

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(w);

                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(System.IO.Path.Combine(Paths.Dir, fileName));
                enc.Save(fs);
            }
            catch (Exception ex) { Log.Write("capture " + fileName + ": " + ex.Message); }
        }
    }
}
