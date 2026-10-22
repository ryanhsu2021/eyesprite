using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EyeGuardian.Models;
using EyeGuardian.Services;

namespace EyeGuardian.Views
{
    public partial class SettingsPage : UserControl
    {
        private bool _loading = true;

        public SettingsPage()
        {
            InitializeComponent();
            LoadFromSettings();
            _loading = false;
        }

        private AppSettings S => App.Instance.Settings;

        /// <summary>开发期截图用：滚到底部查看语言与提示语区块。</summary>
        public void ScrollToBottom()
        {
            try { PageScroll.ScrollToEnd(); TipScroll.ScrollToEnd(); } catch { }
        }

        private void LoadFromSettings()
        {
            var s = S;

            ForceSwitch.IsChecked = s.ForceBreak;
            IdleSwitch.IsChecked = s.IdleDetection;
            AlertSwitch.IsChecked = s.AlertBeforeLongBreak;
            SoundSwitch.IsChecked = s.SoundEnabled;
            StartupSwitch.IsChecked = AutoStart.IsEnabled();
            s.RunAtStartup = StartupSwitch.IsChecked == true;
            MinimizedSwitch.IsChecked = s.StartMinimized;

            string lang = string.IsNullOrEmpty(s.Language) ? Loc.Auto : s.Language;
            LangAuto.IsChecked = lang == Loc.Auto;
            LangZh.IsChecked = lang == Loc.Zh;
            LangEn.IsChecked = lang == Loc.En;

            ReloadTexts();
        }

        /// <summary>语言切换或设置变化后刷新纯文本部分（不改动用户正在编辑的内容）。</summary>
        public void ReloadTexts()
        {
            var s = S;
            ShortGapText.Text = Loc.Minutes(s.ShortGapMinutes);
            ShortDurationText.Text = Loc.Minutes(s.ShortDurationMinutes);
            LongGapText.Text = Loc.Minutes(s.LongGapMinutes);
            LongDurationText.Text = Loc.Minutes(s.LongDurationMinutes);
            IdleThresholdText.Text = Loc.Minutes(Math.Max(1, s.IdleThresholdSeconds / 60));
            AwayResetText.Text = Loc.Minutes(s.AwayResetMinutes);
            SyncIdlePanel();
            BuildTipRows();
        }

        /// <summary>空闲检测关闭时收起原理说明与参数，界面保持干净。</summary>
        private void SyncIdlePanel()
        {
            IdlePanel.Visibility = S.IdleDetection ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Step_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            var parts = tag.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int delta)) return;

            var s = S;
            bool rhythm = false;
            switch (parts[0])
            {
                case "ShortGap":
                    s.ShortGapMinutes = Math.Clamp(s.ShortGapMinutes + delta, 5, 240);
                    ShortGapText.Text = Loc.Minutes(s.ShortGapMinutes);
                    rhythm = true;
                    break;
                case "ShortDuration":
                    s.ShortDurationMinutes = Math.Clamp(s.ShortDurationMinutes + delta, 1, 60);
                    ShortDurationText.Text = Loc.Minutes(s.ShortDurationMinutes);
                    rhythm = true;
                    break;
                case "LongGap":
                    s.LongGapMinutes = Math.Clamp(s.LongGapMinutes + delta, 15, 480);
                    LongGapText.Text = Loc.Minutes(s.LongGapMinutes);
                    rhythm = true;
                    break;
                case "LongDuration":
                    s.LongDurationMinutes = Math.Clamp(s.LongDurationMinutes + delta, 5, 60);
                    LongDurationText.Text = Loc.Minutes(s.LongDurationMinutes);
                    rhythm = true;
                    break;
                case "IdleThreshold":
                    s.IdleThresholdSeconds = Math.Clamp(s.IdleThresholdSeconds + delta, 60, 600);
                    IdleThresholdText.Text = Loc.Minutes(s.IdleThresholdSeconds / 60);
                    break;
                case "AwayReset":
                    s.AwayResetMinutes = Math.Clamp(s.AwayResetMinutes + delta, 1, 30);
                    AwayResetText.Text = Loc.Minutes(s.AwayResetMinutes);
                    break;
            }

            Persist(restartRhythm: rhythm);
        }

        private void Switch_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = S;

            if (sender == ForceSwitch) s.ForceBreak = ForceSwitch.IsChecked == true;
            else if (sender == IdleSwitch)
            {
                s.IdleDetection = IdleSwitch.IsChecked == true;
                SyncIdlePanel();
            }
            else if (sender == AlertSwitch) s.AlertBeforeLongBreak = AlertSwitch.IsChecked == true;
            else if (sender == SoundSwitch) s.SoundEnabled = SoundSwitch.IsChecked == true;
            else if (sender == MinimizedSwitch) s.StartMinimized = MinimizedSwitch.IsChecked == true;
            else if (sender == StartupSwitch)
            {
                s.RunAtStartup = StartupSwitch.IsChecked == true;
                AutoStart.Set(s.RunAtStartup);
            }

            Persist(restartRhythm: false);
        }

        private void Lang_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (sender is not RadioButton rb || rb.Tag is not string tag) return;

            S.Language = tag;
            SettingsStore.Save(S);
            App.Instance.ApplyLanguage();
        }

        // ---------- 休息提示语 ----------

        private bool _suppressCheck;
        private bool _rebuilding;

        /// <summary>按当前设置重建提示语列表（勾选 + 可直接编辑的文案 + 自定义条目）。</summary>
        private void BuildTipRows()
        {
            if (_rebuilding) return;
            _rebuilding = true;
            try
            {
                if (S.TipItems == null) S.TipItems = TipCatalog.All();

                TipList.Children.Clear();
                foreach (var item in S.TipItems.ToList())
                    TipList.Children.Add(CreateTipRow(item));

                UpdateTipCount();
            }
            finally { _rebuilding = false; }
        }

        private void UpdateTipCount()
        {
            var list = S.TipItems ?? new List<TipItem>();
            int on = list.Count(x => x.Enabled);
            TipCount.Text = Loc.F("se.tipsEnabled", on, list.Count);
            TipCount.Foreground = on == 0
                ? (Brush)FindResource("DangerBrush")
                : (Brush)FindResource("AccentDeepBrush");
        }

        private UIElement CreateTipRow(TipItem item)
        {
            var row = new Grid { Margin = new Thickness(3, 1, 3, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 勾选框：先设状态再挂事件，避免构建时误触发保存
            var chk = new CheckBox
            {
                Style = (Style)FindResource("TipCheck"),
                IsChecked = item.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 3, 0)
            };
            chk.Checked += (s, e) => ToggleTip(item, true, chk);
            chk.Unchecked += (s, e) => ToggleTip(item, false, chk);
            Grid.SetColumn(chk, 0);
            row.Children.Add(chk);

            // 文案：点击即可编辑，失焦保存
            var box = new TextBox
            {
                Style = (Style)FindResource("TipInput"),
                Text = TipCatalog.TextOf(item),
                Tag = item,
                VerticalAlignment = VerticalAlignment.Center
            };
            box.LostFocus += (s, e) => CommitTipText(item, box);
            box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0)
            };

            // 改写过的内置条目：一键还原成内置文案
            if (!item.IsCustom && !string.IsNullOrWhiteSpace(item.Text))
            {
                var undo = new Button
                {
                    Style = (Style)FindResource("TipIconBtn"),
                    Content = "\uE7A7",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    ToolTip = Loc.T("se.tipsReset")
                };
                undo.Click += (s, e) =>
                {
                    item.Text = "";
                    box.Text = TipCatalog.TextOf(item);
                    SettingsStore.Save(S);
                    App.Instance.Engine.InvalidateTip();
                    BuildTipRows();
                };
                actions.Children.Add(undo);
            }

            // 自定义条目：允许删除
            if (item.IsCustom)
            {
                var del = new Button
                {
                    Style = (Style)FindResource("TipIconBtn"),
                    Content = "\uE711",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    ToolTip = Loc.T("se.tipsDelete")
                };
                del.Click += (s, e) =>
                {
                    S.TipItems.Remove(item);
                    SettingsStore.Save(S);
                    App.Instance.Engine.InvalidateTip();
                    BuildTipRows();
                };
                actions.Children.Add(del);
            }

            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);

            return row;
        }

        private void ToggleTip(TipItem item, bool enabled, CheckBox source)
        {
            if (_suppressCheck) return;

            if (!enabled && (S.TipItems?.Count(x => x.Enabled) ?? 0) <= 1)
            {
                // 至少留一条，否则休息时没有内容可展示
                _suppressCheck = true;
                source.IsChecked = true;
                _suppressCheck = false;
                MessageBox.Show(Loc.T("se.tipsEmpty"), Loc.T("se.title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            item.Enabled = enabled;
            SettingsStore.Save(S);
            App.Instance.Engine.InvalidateTip();
            UpdateTipCount();
        }

        private void CommitTipText(TipItem item, TextBox box)
        {
            if (item == null || _rebuilding) return;

            string text = (box.Text ?? "").Trim();

            if (item.IsCustom)
            {
                if (text.Length == 0)
                {
                    // 空的自定义条目没意义，直接丢掉
                    S.TipItems.Remove(item);
                    SettingsStore.Save(S);
                    App.Instance.Engine.InvalidateTip();
                    BuildTipRows();
                    return;
                }
                item.Text = text;
            }
            else
            {
                // 与内置文案一致就视为「没改」，跟着语言走
                item.Text = text == TipCatalog.BuiltinText(item.Id) ? "" : text;
            }

            SettingsStore.Save(S);
            App.Instance.Engine.InvalidateTip();

            // 改写过 / 还原后需要刷新行内按钮，因此整体重建
            BuildTipRows();
        }

        private void AddTip_Click(object sender, RoutedEventArgs e)
        {
            if (S.TipItems == null) S.TipItems = TipCatalog.All();

            var item = TipCatalog.NewCustom();
            S.TipItems.Add(item);
            SettingsStore.Save(S);
            App.Instance.Engine.InvalidateTip();
            BuildTipRows();

            // 聚焦到新建的那一行
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var last = TipList.Children.OfType<Grid>().LastOrDefault();
                var box = last?.Children.OfType<TextBox>().FirstOrDefault();
                if (box == null) return;
                box.Focus();
                box.CaretIndex = box.Text.Length;
                TipScroll.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ResetTips_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(Loc.T("se.tipsReset") + "?", Loc.T("se.title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) return;

            S.TipItems = TipCatalog.All();
            S.TipsVersion = TipCatalog.Version;
            SettingsStore.Save(S);
            App.Instance.Engine.InvalidateTip();
            BuildTipRows();
            SaveHint.Text = Loc.T("se.tipsRestored");
        }

        private void Persist(bool restartRhythm)
        {
            SettingsStore.Save(S);
            SaveHint.Text = Loc.F("se.savedAt", DateTime.Now.ToString("HH:mm"));

            var engine = App.Instance.Engine;
            if (restartRhythm && engine.State == GuardState.Protecting && !engine.InBreak)
                engine.Start();
        }
    }
}
