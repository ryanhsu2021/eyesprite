using System;
using System.Collections.Generic;
using EyeGuardian.Models;

namespace EyeGuardian.Services
{
    public enum GuardState { Protecting, Paused, Stopped }

    public enum BreakKind { Short, Long }

    public sealed class GuardSnapshot
    {
        public GuardState State { get; set; }
        public bool InBreak { get; set; }
        public BreakKind Kind { get; set; }
        public TimeSpan ToShort { get; set; }
        public TimeSpan ToLong { get; set; }
        public TimeSpan BreakRemaining { get; set; }
        public TimeSpan BreakDuration { get; set; }
        /// <summary>距下次短休的进度 0~1（已过去占比）</summary>
        public double ShortProgress { get; set; }
        /// <summary>当前休息的进度 0~1</summary>
        public double BreakProgress { get; set; }
        public bool Idle { get; set; }
        public TimeSpan PauseRemaining { get; set; }
        public bool PauseIndefinite { get; set; }
    }

    /// <summary>
    /// 护眼计时核心。与上游 EyesGuard 的关键差异：全部用「绝对时间点」推算剩余量，
    /// 不靠每秒减一，所以不会漂移，睡眠唤醒后也不会错乱。
    /// </summary>
    public sealed class GuardEngine : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly IIdleSource _idle;

        private DateTime _shortDue;
        private DateTime _longDue;
        private DateTime _breakStart;
        private DateTime _breakEndAt;
        private DateTime _lastTick = DateTime.Now;
        private DateTime _pausedAt;

        private TimeSpan _shortGapAtStart = TimeSpan.FromMinutes(20);
        private TimeSpan _breakDuration = TimeSpan.FromSeconds(20);

        private TimeSpan _pauseRemaining;
        private bool _pauseIndefinite;
        private bool _alertedLong;
        private double _focusAccum;

        /// <summary>本次离开的起点；null 表示人在电脑前。</summary>
        private DateTime? _awaySince;
        /// <summary>本次离开累计时长（秒）。</summary>
        private double _awaySeconds;
        /// <summary>判定离开的瞬时原因，供界面做说明。</summary>
        private IdleProbe _probe;
        private string _currentTip = "";
        private int _lastTipIndex = -1;
        // 提示语列表缓存：之前每 300ms 重建一次 List + 全部文案查找
        private List<string> _tipCache;
        private string _minuteTip = "";
        private int _minuteTipAt = -1;

        public GuardState State { get; private set; } = GuardState.Stopped;
        public bool InBreak { get; private set; }
        public BreakKind Kind { get; private set; } = BreakKind.Short;
        public bool IsIdle { get; private set; }

        public event Action BreakStarted;
        public event Action<bool> BreakEnded;   // true = 完整休息结束，false = 被跳过
        public event Action LongBreakSoon;
        public event Action StateChanged;
        /// <summary>人回来了，且离开时间够长被结算成一次休息（参数为离开时长）。</summary>
        public event Action<TimeSpan> AwaySettled;

        public GuardEngine(AppSettings settings, IIdleSource idle = null)
        {
            _settings = settings;
            _idle = idle ?? new IdleDetector();
        }

        public TimeSpan PauseRemaining => _pauseIndefinite ? TimeSpan.Zero : _pauseRemaining;
        public bool PauseIndefinite => _pauseIndefinite;

        /// <summary>强制忽略空闲状态（截图/自检用，不影响用户设置）。</summary>
        public bool SuppressIdle { get; set; }

        public void Start()
        {
            var now = DateTime.Now;
            State = GuardState.Protecting;
            _lastTick = now;
            ClearAway();
            ResetShort(now);
            ResetLong(now);
            _alertedLong = false;
            StateChanged?.Invoke();
        }

        public void Stop()
        {
            if (InBreak) ForceEndBreak();
            State = GuardState.Stopped;
            _pauseIndefinite = false;
            _pauseRemaining = TimeSpan.Zero;
            ClearAway();
            StateChanged?.Invoke();
        }

        public void Pause(TimeSpan duration)
        {
            if (State != GuardState.Protecting || InBreak) return;
            _pausedAt = DateTime.Now;
            _pauseIndefinite = duration <= TimeSpan.Zero;
            _pauseRemaining = duration;
            State = GuardState.Paused;
            StateChanged?.Invoke();
        }

        public void Resume()
        {
            if (State != GuardState.Paused) return;
            var now = DateTime.Now;
            var span = now - _pausedAt;
            if (span > TimeSpan.Zero) { _shortDue += span; _longDue += span; }
            _lastTick = now;
            _pauseRemaining = TimeSpan.Zero;
            _pauseIndefinite = false;
            State = GuardState.Protecting;
            StateChanged?.Invoke();
        }

        public void StartBreakManually(BreakKind kind = BreakKind.Short)
        {
            if (InBreak) return;
            if (State == GuardState.Stopped) Start();
            BeginBreak(kind);
        }

        public void SkipBreak()
        {
            if (!InBreak) return;
            EndBreak(false);
        }

        /// <summary>主循环，由 UI 定时器每 ~300ms 调用一次。</summary>
        public void Tick()
        {
            var now = DateTime.Now;
            var delta = now - _lastTick;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            // 睡眠/休眠唤醒：超过 2 分钟的跳变不计入工作时间
            if (delta > TimeSpan.FromMinutes(2)) delta = TimeSpan.Zero;
            _lastTick = now;

            if (State == GuardState.Stopped)
            {
                IsIdle = false;
                return;
            }

            if (State == GuardState.Paused)
            {
                if (!_pauseIndefinite)
                {
                    _pauseRemaining -= delta;
                    if (_pauseRemaining <= TimeSpan.Zero) { Resume(); return; }
                }
                ClearAway();
                return;
            }

            if (InBreak)
            {
                ClearAway();
                if (now >= _breakEndAt) EndBreak(true);
                return;
            }

            IsIdle = _settings.IdleDetection && !SuppressIdle && _idle.IsAway(_settings.IdleThresholdSeconds, out _probe);
            if (IsIdle)
            {
                // 人不在：两个倒计时同步后移，用眼时长也不累计
                if (_awaySince == null) _awaySince = now;
                _awaySeconds += delta.TotalSeconds;
                _shortDue += delta;
                _longDue += delta;
                return;
            }

            // 刚回到电脑前：按离开时长结算
            if (_awaySince != null) SettleAway(now);

            _focusAccum += delta.TotalSeconds;

            if (now >= _longDue) { BeginBreak(BreakKind.Long); return; }
            if (now >= _shortDue) { BeginBreak(BreakKind.Short); return; }

            var toLong = _longDue - now;
            if (_settings.AlertBeforeLongBreak)
            {
                if (!_alertedLong && toLong <= TimeSpan.FromSeconds(60) && toLong > TimeSpan.Zero)
                {
                    _alertedLong = true;
                    LongBreakSoon?.Invoke();
                }
                else if (toLong > TimeSpan.FromSeconds(70))
                {
                    _alertedLong = false;
                }
            }
        }

        /// <summary>取出累计的工作秒数（用于统计「护眼时长 / 用眼时长」）。</summary>
        public int TakeFocusSeconds()
        {
            if (_focusAccum < 1) return 0;
            int v = (int)_focusAccum;
            _focusAccum -= v;
            return v;
        }

        public GuardSnapshot Snapshot()
        {
            var now = DateTime.Now;
            var toShort = _shortDue - now;
            var toLong = _longDue - now;

            double shortProgress = 0;
            if (!InBreak && State == GuardState.Protecting)
            {
                var total = Math.Max(1, _shortGapAtStart.TotalSeconds);
                shortProgress = Math.Clamp(1.0 - toShort.TotalSeconds / total, 0, 1);
            }
            else if (InBreak || State != GuardState.Stopped)
            {
                shortProgress = 0;
            }

            double breakProgress = 0;
            if (InBreak && _breakDuration.TotalSeconds > 0)
                breakProgress = Math.Clamp((now - _breakStart).TotalSeconds / _breakDuration.TotalSeconds, 0, 1);

            return new GuardSnapshot
            {
                State = State,
                InBreak = InBreak,
                Kind = Kind,
                ToShort = toShort > TimeSpan.Zero ? toShort : TimeSpan.Zero,
                ToLong = toLong > TimeSpan.Zero ? toLong : TimeSpan.Zero,
                BreakRemaining = InBreak && _breakEndAt > now ? _breakEndAt - now : TimeSpan.Zero,
                BreakDuration = _breakDuration,
                ShortProgress = shortProgress,
                BreakProgress = breakProgress,
                Idle = IsIdle,
                PauseRemaining = PauseRemaining,
                PauseIndefinite = _pauseIndefinite
            };
        }

        public string CurrentTip()
        {
            // 休息中：用本次休息开始时选定的那条，整段休息稳定不跳
            if (InBreak && !string.IsNullOrEmpty(_currentTip)) return _currentTip;

            // 平时：按分钟在启用列表里轮换，每分钟换一条，也不会闪
            _tipCache ??= TipCatalog.ActiveTexts(_settings);
            if (_tipCache.Count == 0) return "";

            int minute = DateTime.Now.Minute;
            if (minute != _minuteTipAt || _minuteTip.Length == 0)
            {
                _minuteTip = _tipCache[minute % _tipCache.Count];
                _minuteTipAt = minute;
            }
            return _minuteTip;
        }

        /// <summary>提示语被改动后清掉缓存，下一帧就会用新文案。</summary>
        public void InvalidateTip()
        {
            _currentTip = "";
            _lastTipIndex = -1;
            _tipCache = null;
            _minuteTip = "";
            _minuteTipAt = -1;
        }

        /// <summary>每次休息开始只挑一次，避免界面 300ms 刷新时提示语来回跳。</summary>
        private void PickTip()
        {
            _tipCache ??= TipCatalog.ActiveTexts(_settings);
            var tips = _tipCache;
            if (tips.Count == 0) { _currentTip = ""; return; }
            if (tips.Count == 1) { _currentTip = tips[0]; return; }

            int idx = _lastTipIndex;
            for (int i = 0; i < 8 && idx == _lastTipIndex; i++)
                idx = Random.Shared.Next(tips.Count);

            _lastTipIndex = idx;
            _currentTip = tips[idx];
        }

        // ---------- 内部 ----------

        /// <summary>本次离开已持续多久（不在电脑前时才有意义）。</summary>
        public TimeSpan AwayFor => _awaySince == null ? TimeSpan.Zero : TimeSpan.FromSeconds(_awaySeconds);

        /// <summary>退出时释放 COM 等非托管资源。</summary>
        public void Dispose() => (_idle as IDisposable)?.Dispose();

        /// <summary>当前判定的信号明细，供界面解释「为什么算离开」。</summary>
        public IdleProbe Probe => _probe;

        /// <summary>
        /// 回到电脑前时结算：
        /// 离开够久 → 视为已经休息过，两个周期都重新开始（不是无穷顺延）；
        /// 只离开一小会 → 若回来马上就要休息，给 60 秒缓冲，避免刚坐下就被弹遮罩。
        /// </summary>
        private void SettleAway(DateTime now)
        {
            double seconds = _awaySeconds;
            _awaySince = null;
            _awaySeconds = 0;

            var resetAfter = TimeSpan.FromMinutes(Math.Max(1, _settings.AwayResetMinutes));
            if (TimeSpan.FromSeconds(seconds) >= resetAfter)
            {
                ResetShort(now);
                ResetLong(now);
                AwaySettled?.Invoke(TimeSpan.FromSeconds(seconds));
                return;
            }

            var grace = TimeSpan.FromSeconds(60);
            if (_shortDue - now < grace) _shortDue = now + grace;
            if (_longDue - now < grace) _longDue = now + grace;
        }

        private void ClearAway()
        {
            _awaySince = null;
            _awaySeconds = 0;
            IsIdle = false;
            _probe = default;
        }

        private void ResetShort(DateTime now)
        {
            _shortGapAtStart = _settings.ShortGap;
            _shortDue = now + _shortGapAtStart;
        }

        private void ResetLong(DateTime now)
        {
            _longDue = now + _settings.LongGap;
            _alertedLong = false;
        }

        private void BeginBreak(BreakKind kind)
        {
            Kind = kind;
            InBreak = true;
            PickTip();
            _breakStart = DateTime.Now;
            _breakDuration = kind == BreakKind.Short ? _settings.ShortDuration : _settings.LongDuration;
            _breakEndAt = _breakStart + _breakDuration;
            _alertedLong = false;
            BreakStarted?.Invoke();
        }

        private void EndBreak(bool completed)
        {
            var kind = Kind;
            InBreak = false;
            var now = DateTime.Now;
            ResetShort(now);
            if (kind == BreakKind.Long) ResetLong(now);
            BreakEnded?.Invoke(completed);
        }

        private void ForceEndBreak()
        {
            InBreak = false;
            BreakEnded?.Invoke(false);
        }
    }
}
