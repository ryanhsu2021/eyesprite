using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using EyeGuardian.Services;
using WinForms = System.Windows.Forms;

namespace EyeGuardian.Services
{
    /// <summary>系统托盘图标 + 右键菜单。图标是随进度变化的环形图，一眼看出还剩多久。</summary>
    public sealed class TrayService : IDisposable
    {
        private readonly WinForms.NotifyIcon _icon;
        private readonly WinForms.ToolStripMenuItem _open;
        private readonly WinForms.ToolStripMenuItem _breakNow;
        private readonly WinForms.ToolStripMenuItem _pause;
        private readonly WinForms.ToolStripMenuItem _resume;
        private readonly WinForms.ToolStripMenuItem _stop;
        private readonly WinForms.ToolStripMenuItem _start;
        private readonly WinForms.ToolStripMenuItem _exit;
        private Icon _current;

        // 缓存上一帧的状态，只有真正变化才重绘图标/改文本。
        // 之前每 300ms 重画一次 32×32 图标，GDI+ 对象和高频 shell 调用让静置 CPU 到了 1% 以上。
        private GuardState _lastState = (GuardState)(-1);
        private int _lastPercent = -1;
        private long _lastKey = long.MinValue;

        public event Action ShowRequested;
        public event Action BreakRequested;
        public event Action PauseRequested;
        public event Action ResumeRequested;
        public event Action StopRequested;
        public event Action StartRequested;
        public event Action ExitRequested;

        public TrayService()
        {
            var font = new Font("Microsoft YaHei UI", 9f);
            var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false, Font = font };

            _open = new WinForms.ToolStripMenuItem("", null, (s, e) => ShowRequested?.Invoke())
            { Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };
            _breakNow = new WinForms.ToolStripMenuItem("", null, (s, e) => BreakRequested?.Invoke());
            _pause = new WinForms.ToolStripMenuItem("", null, (s, e) => PauseRequested?.Invoke());
            _resume = new WinForms.ToolStripMenuItem("", null, (s, e) => ResumeRequested?.Invoke());
            _stop = new WinForms.ToolStripMenuItem("", null, (s, e) => StopRequested?.Invoke());
            _start = new WinForms.ToolStripMenuItem("", null, (s, e) => StartRequested?.Invoke());
            _exit = new WinForms.ToolStripMenuItem("", null, (s, e) => ExitRequested?.Invoke());

            menu.Items.Add(_open);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(_breakNow);
            menu.Items.Add(_pause);
            menu.Items.Add(_resume);
            menu.Items.Add(_start);
            menu.Items.Add(_stop);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(_exit);

            _current = CreateIcon(GuardState.Stopped, 0);
            _icon = new WinForms.NotifyIcon
            {
                Icon = _current,
                Text = Loc.T("app.name"),
                Visible = true,
                ContextMenuStrip = menu
            };
            _icon.DoubleClick += (s, e) => ShowRequested?.Invoke();

            ApplyLanguage();
        }

        public void ApplyLanguage()
        {
            try
            {
                _open.Text = Loc.T("tr.open");
                _breakNow.Text = Loc.T("tr.breakNow");
                _pause.Text = Loc.T("tr.pause");
                _resume.Text = Loc.T("tr.resume");
                _stop.Text = Loc.T("tr.stop");
                _start.Text = Loc.T("tr.start");
                _exit.Text = Loc.T("tr.exit");
                _icon.Text = Loc.T("app.name");
                _lastKey = long.MinValue;   // 换语言后强制下一帧重刷文本
            }
            catch (Exception ex) { Log.Write("tray lang: " + ex.Message); }
        }

        public void Update(GuardSnapshot snap)
        {
            try
            {
                double progress = snap.InBreak ? snap.BreakProgress : snap.ShortProgress;
                int percent = (int)Math.Round(Math.Clamp(progress, 0, 1) * 100);

                // 圆环按 1% 粒度重绘：30 分钟周期下约 18 秒一次，肉眼无差别
                if (snap.State != _lastState || percent != _lastPercent)
                {
                    var newIcon = CreateIcon(snap.State, progress);
                    _icon.Icon = newIcon;
                    if (_current != null) _current.Dispose();
                    _current = newIcon;
                    _lastState = snap.State;
                    _lastPercent = percent;
                }

                // 文本与菜单只在「显示内容真的会变」时才更新（正常情况每秒一次）
                TimeSpan shown = snap.InBreak ? snap.BreakRemaining
                    : snap.State == GuardState.Protecting ? snap.ToShort
                    : snap.State == GuardState.Paused ? snap.PauseRemaining
                    : TimeSpan.Zero;

                long key = ((long)(int)snap.State << 40)
                           | ((long)(int)Math.Max(0, shown.TotalSeconds) << 8)
                           | (snap.InBreak ? 1L : 0L) << 2
                           | (snap.Idle ? 1L : 0L) << 1
                           | (snap.PauseIndefinite ? 1L : 0L);
                if (key == _lastKey) return;
                _lastKey = key;

                string stateText;
                if (snap.State == GuardState.Paused) stateText = Loc.T("st.paused");
                else if (snap.State == GuardState.Stopped) stateText = Loc.T("st.stopped");
                else if (snap.InBreak) stateText = Loc.T("st.breaking");
                else if (snap.Idle) stateText = Loc.T("st.idle");
                else stateText = Loc.T("st.protecting");

                string time = snap.InBreak || snap.State == GuardState.Paused
                    ? Format(shown)
                    : snap.State == GuardState.Protecting ? Format(snap.ToShort) : "--:--";

                string text = $"{Loc.T("app.name")} · {stateText} {time}";
                if (text.Length > 120) text = text.Substring(0, 120);
                if (_icon.Text != text) _icon.Text = text;

                bool protecting = snap.State == GuardState.Protecting;
                _breakNow.Enabled = !snap.InBreak && snap.State != GuardState.Stopped;
                _pause.Enabled = protecting && !snap.InBreak;
                _resume.Enabled = snap.State == GuardState.Paused;
                _stop.Enabled = snap.State != GuardState.Stopped;
                _start.Visible = snap.State == GuardState.Stopped;
                _stop.Visible = snap.State != GuardState.Stopped;

                if (snap.State == GuardState.Paused)
                    _pause.Text = snap.PauseIndefinite ? Loc.T("tr.pausedManual") : Loc.F("tr.pausedFor", Format(snap.PauseRemaining));
                else
                    _pause.Text = Loc.T("tr.pause");
            }
            catch (Exception ex) { Log.Write("tray update: " + ex.Message); }
        }

        public void Balloon(string title, string message)
        {
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = message;
                _icon.ShowBalloonTip(6000);
            }
            catch { }
        }

        private static string Format(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes:00}:{t.Seconds:00}";
        }

        private static Icon CreateIcon(GuardState state, double progress)
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var rect = new Rectangle(4, 4, 23, 23);

                var baseColor = state switch
                {
                    GuardState.Protecting => Color.FromArgb(90, 0x17, 0xA8, 0x79),
                    GuardState.Paused => Color.FromArgb(90, 0xEA, 0x9A, 0x1E),
                    _ => Color.FromArgb(80, 0x9A, 0xA8, 0xA3)
                };
                using (var bg = new Pen(baseColor, 5f)) g.DrawEllipse(bg, rect);

                var fgColor = state switch
                {
                    GuardState.Protecting => Color.FromArgb(255, 0x17, 0xA8, 0x79),
                    GuardState.Paused => Color.FromArgb(255, 0xE0, 0x90, 0x12),
                    _ => Color.FromArgb(255, 0x8A, 0x9A, 0x94)
                };
                float sweep = (float)(360 * Math.Clamp(progress, 0.02, 1.0));
                using var fg = new Pen(fgColor, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(fg, rect, -90f, sweep);
            }

            IntPtr h = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(h);
                return (Icon)tmp.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(h);
            }
        }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
                _current?.Dispose();
            }
            catch { }
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr handle);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOACTIVATE = 0x0010;
    }
}
