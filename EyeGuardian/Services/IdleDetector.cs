using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EyeGuardian.Services
{
    /// <summary>一次探测的结果：为什么判定「人不在」。</summary>
    public struct IdleProbe
    {
        /// <summary>距最后一次键鼠输入的时长。</summary>
        public TimeSpan IdleSpan;
        /// <summary>锁屏 / 安全桌面：立即视为离开。</summary>
        public bool Locked;
        /// <summary>有声音在播放：人不一定在，但通常在用电脑。</summary>
        public bool AudioActive;
        /// <summary>前台窗口铺满整块屏幕：多见于看视频 / 全屏阅读。</summary>
        public bool Fullscreen;
    }

    /// <summary>空闲信号源。抽成接口是为了让计时的「离开结算」逻辑可以脱离系统环境被验证。</summary>
    public interface IIdleSource
    {
        bool IsAway(int thresholdSeconds, out IdleProbe probe);
    }

    /// <summary>
    /// 空闲判断。三层信号叠加，避免「看视频被当成离开」这类误判：
    /// 1) GetLastInputInfo —— 系统最后一次键鼠输入的时间戳（零侵入，不装钩子）；
    /// 2) 音频活动 —— 默认播放设备的峰值电平（只听「有没有声音」，不解析内容）；
    /// 3) 前台全屏 —— 前台窗口是否铺满所在显示器。
    /// 锁屏单独判断，命中即视为离开，不用等阈值。
    /// </summary>
    public sealed class IdleDetector : IIdleSource, IDisposable
    {
        // ---------- Win32 ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // ---------- Core Audio（读峰值电平） ----------

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        private enum EDataFlow { Render = 0 }
        private enum ERole { Multimedia = 1 }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
         Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
         Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                                       [MarshalAs(UnmanagedType.Interface)] out IAudioMeterInformation meter);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
         Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
        private interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
            [PreserveSig] int GetMeteringChannelCount(out int count);
            [PreserveSig] int GetChannelsPeakValues(int channelCount, [Out] float[] peaks);
            [PreserveSig] int QueryHardwareSupport(out int mask);
        }

        private const int CLSCTX_ALL = 23;
        /// <summary>低于这个峰值视为静音（底噪通常在 0.0005 以下）。</summary>
        private const float PeakThreshold = 0.002f;
        /// <summary>声音间歇（换曲、停顿）不算离开，留 5 秒余量。</summary>
        private static readonly TimeSpan AudioHold = TimeSpan.FromSeconds(5);

        private IMMDeviceEnumerator _enumerator;
        private IAudioMeterInformation _meter;
        private DateTime _lastAudioAt = DateTime.MinValue;
        private int _probeCounter;

        // ---------- 对外 ----------

        /// <summary>距最后一次键鼠输入的时长；失败时返回 0（宁可多提醒，不漏提醒）。</summary>
        public TimeSpan Idle()
        {
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
                uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
                return TimeSpan.FromMilliseconds(elapsed);
            }
            catch { return TimeSpan.Zero; }
        }

        /// <summary>锁屏 / 安全桌面：前台窗口为空。</summary>
        public bool Locked()
        {
            try { return GetForegroundWindow() == IntPtr.Zero; }
            catch { return false; }
        }

        /// <summary>默认播放设备是否正在出声。</summary>
        public bool AudioActive()
        {
            try
            {
                var meter = EnsureMeter();
                if (meter == null) return false;

                if (meter.GetPeakValue(out float peak) != 0)
                {
                    Release(ref _meter);   // 设备切换 / 失效：先释放 RCW 再重建，否则长期运行会漏 COM 对象
                    return false;
                }

                if (peak > PeakThreshold)
                {
                    _lastAudioAt = DateTime.UtcNow;
                    return true;
                }
                return DateTime.UtcNow - _lastAudioAt < AudioHold;
            }
            catch
            {
                Release(ref _meter);
                return false;
            }
        }

        /// <summary>前台窗口是否铺满所在显示器（看视频、演示、全屏阅读）。</summary>
        public bool FullscreenForeground()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero || !IsWindowVisible(h)) return false;

                // 自己的窗口（休息遮罩）不算，否则会永久屏蔽空闲判定
                GetWindowThreadProcessId(h, out int pid);
                if (pid == Environment.ProcessId) return false;

                var cls = new StringBuilder(64);
                GetClassName(h, cls, cls.Capacity);
                string name = cls.ToString();
                if (name == "Progman" || name == "WorkerW" ||
                    name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd")
                    return false;   // 桌面 / 任务栏

                if (!GetWindowRect(h, out RECT wr)) return false;

                IntPtr mon = MonitorFromWindow(h, 2 /* MONITOR_DEFAULTTONEAREST */);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(mon, ref mi)) return false;

                const int tol = 2;
                return wr.Left <= mi.rcMonitor.Left + tol &&
                       wr.Top <= mi.rcMonitor.Top + tol &&
                       wr.Right >= mi.rcMonitor.Right - tol &&
                       wr.Bottom >= mi.rcMonitor.Bottom - tol;
            }
            catch { return false; }
        }

        /// <summary>
        /// 综合判断。只有输入空闲已经超过阈值时才去查音频和全屏，
        /// 平时每 300ms 只花一个 GetLastInputInfo 的开销。
        /// </summary>
        public bool IsAway(int thresholdSeconds, out IdleProbe probe)
        {
            probe = new IdleProbe { IdleSpan = Idle() };

            if (Locked())
            {
                probe.Locked = true;
                return true;
            }

            if (probe.IdleSpan.TotalSeconds < Math.Max(5, thresholdSeconds)) return false;

            probe.AudioActive = AudioActive();
            if (probe.AudioActive) return false;

            probe.Fullscreen = FullscreenForeground();
            return !probe.Fullscreen;
        }

        public bool IsAway(int thresholdSeconds) => IsAway(thresholdSeconds, out _);

        /// <summary>诊断用：把三个原始信号各测一遍，排查「为什么没判定离开」。</summary>
        public static string Report()
        {
            var d = new IdleDetector();
            var sb = new StringBuilder();
            sb.AppendLine("idle=" + d.Idle().TotalSeconds.ToString("F1") + "s");
            sb.AppendLine("locked=" + d.Locked());
            sb.AppendLine("audio=" + d.AudioActive());
            sb.AppendLine("fullscreen=" + d.FullscreenForeground());
            return sb.ToString();
        }

        // ---------- 内部 ----------

        /// <summary>释放 COM 引用（COM 对象不受 GC 及时回收，必须显式放）。</summary>
        private static void Release<T>(ref T com) where T : class
        {
            try
            {
                if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
            }
            catch { }
            com = null;
        }

        public void Dispose()
        {
            Release(ref _meter);
            Release(ref _enumerator);
        }

        private IAudioMeterInformation EnsureMeter()
        {
            if (_meter != null) return _meter;

            // 设备状态变化不频繁，每 10 次（约 3 秒）才重试一次，避免反复创建 COM 对象
            if (_enumerator != null && _probeCounter++ % 10 != 0) return null;

            try
            {
                _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) != 0
                    || device == null)
                    return null;

                var iid = typeof(IAudioMeterInformation).GUID;
                var hr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var meter);
                // 拿到电平表就不再需要 device，立即释放它
                if (Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);

                if (hr != 0 || meter == null) return null;

                _meter = meter;
                return _meter;
            }
            catch
            {
                Release(ref _meter);
                Release(ref _enumerator);
                return null;
            }
        }
    }
}
