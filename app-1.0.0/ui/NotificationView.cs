// Glimpseon
// Copyright (C) 2026 HelloGaoo
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// 通知页

using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;

namespace Glimpseon.UI;

public class NotificationView : UserControl
{
    private static readonly string[] TtsVoices =
    [
        "zh-CN-XiaoxiaoNeural",
        "zh-CN-XiaoyiNeural",
        "zh-CN-YunxiNeural",
        "zh-CN-YunjianNeural",
        "zh-CN-YunyangNeural",
        "zh-CN-YunxiaNeural",
    ];

    private sealed class QueueItem
    {
        public NotificationRequest Request = new();
        public string Uid = Guid.NewGuid().ToString("N");
        public bool Scheduled;
        public DateTime? FireTime;
    }

    private readonly NotificationManager _manager = new();
    private readonly List<QueueItem> _queue = [];
    private readonly object _queueLock = new();
    private bool _isShowing;
    private string _showingUid = "";
    private DispatcherTimer? _scheduleTimer;

    // 控件引用
    private TextBox _contentEdit = null!;
    private TextBlock _charCountLabel = null!;
    private TextBlock _queueCountLabel = null!;
    private StackPanel _queueRows = null!;
    private Border _previewBorder = null!;
    private TextBlock _previewText = null!;
    private ComboBox _typeCombo = null!;
    private ComboBox _ttsVoiceCombo = null!;
    private NumericUpDown _ttsRateSpin = null!;
    private NumericUpDown _ttsVolumeSpin = null!;
    private ColorPicker _bgColorPicker = null!;
    private ColorPicker _fgColorPicker = null!;
    private NumericUpDown _fontSizeSpin = null!;
    private ComboBox _fontWeightCombo = null!;
    private NumericUpDown _speedSpin = null!;
    private NumericUpDown _durationSpin = null!;
    private NumericUpDown _bannerBgHeightSpin = null!;
    private ToggleSwitch _mouseThroughSwitch = null!;
    private CheckBox _syncBoardCheck = null!;
    private ListBox _queueTable = null!;
    private Border _previewCard = null!;

    // 通知配置区统一尺寸
    private const double CtrlH = 32;                  // 输入控件h
    private const double LabelW = 76;                 // 字段标签w
    private const double CtrlMinW = 120;              // 输入控件w
    private const double CtrlGap = 10;                // 行列间距
    private const double PreviewMinColumnWidth = 560; // 右栏窄就让给配置区

    public NotificationView(MainWindow mainWindow)
    {
        // 标题行 弹性内容区
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 16,
        };
        var title = Common.MakeTitle(AppUtils.Tr("notification.title"));
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        // 左右两栏
        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1*,1.4*"),
            ColumnSpacing = 20,
        };

        var left = BuildLeftPanel();
        var right = BuildRightPanel();
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        split.Children.Add(left);
        split.Children.Add(right);
        Grid.SetRow(split, 1);
        root.Children.Add(split);

        var scroll = Common.MakePageScroll(root);
        scroll.EffectiveViewportChanged += (_, e) =>
        {
            var fill = Math.Max(0, e.EffectiveViewport.Height - 48);
            if (Math.Abs(root.MinHeight - fill) > 1)
            {
                root.MinHeight = fill;
            }
        };
        Content = scroll;

        _scheduleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _scheduleTimer.Tick += (_, _) => CheckScheduled();

        _manager.NotificationFinished = () => Dispatcher.UIThread.Post(OnNotificationShown);

        AttachedToLogicalTree += (_, _) => _scheduleTimer?.Start();
        DetachedFromLogicalTree += (_, _) => _scheduleTimer?.Stop();

        UpdatePreview();
        Log.Debug($"[通知页] 就绪 队列=0");
    }

    // 左栏

    private Control BuildLeftPanel()
    {
        // 上(内容,弹性) 下(队列,自适应)
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 16,
        };

        // 内容卡
        var contentCard = Common.MakeCard();
        var contentStack = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(24, 20, 24, 20),
        };
        var contentLabel = new TextBlock
        {
            Text = AppUtils.Tr("notification.content_label"),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
        };
        Grid.SetRow(contentLabel, 0);
        contentStack.Children.Add(contentLabel);
        _contentEdit = new TextBox
        {
            Watermark = AppUtils.Tr("notification.content_placeholder"),
            AcceptsReturn = true,
            MinHeight = 140,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        _contentEdit.TextChanged += (_, _) =>
        {
            _charCountLabel.Text = (_contentEdit.Text?.Length ?? 0).ToString();
            UpdatePreview();
        };
        Grid.SetRow(_contentEdit, 1);
        contentStack.Children.Add(_contentEdit);
        _charCountLabel = new TextBlock
        {
            Text = "0",
            FontSize = 12,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(_charCountLabel, 2);
        contentStack.Children.Add(_charCountLabel);
        contentCard.Child = contentStack;
        Grid.SetRow(contentCard, 0);
        panel.Children.Add(contentCard);

        // 队列卡
        var queueCard = Common.MakeCard();
        var queueStack = new StackPanel { Spacing = 10, Margin = new Thickness(24, 20, 24, 20) };
        var header = new DockPanel { LastChildFill = false };
        _queueCountLabel = new TextBlock { Text = "0", FontSize = 13, Opacity = 0.7 };
        DockPanel.SetDock(_queueCountLabel, Dock.Right);
        header.Children.Add(_queueCountLabel);
        header.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("notification.queue_label"),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
        });
        queueStack.Children.Add(header);

        // 表头 #/类型/内容/状态
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("34,70,*,70"),
            Margin = new Thickness(0, 4, 0, 4),
        };
        AddHeaderCell(headerGrid, "#", 0);
        AddHeaderCell(headerGrid, AppUtils.Tr("notification.type_label"), 1);
        AddHeaderCell(headerGrid, AppUtils.Tr("notification.content_label"), 2);
        AddHeaderCell(headerGrid, AppUtils.Tr("notification.queue_status"), 3);
        queueStack.Children.Add(headerGrid);

        _queueTable = new ListBox
        {
            MinHeight = 180,
            MaxHeight = 320,
        };
        _queueTable.SelectionChanged += (_, _) => { /* 选中行由 SelectedIndex 提供 */ };
        queueStack.Children.Add(_queueTable);

        queueCard.Child = queueStack;
        Grid.SetRow(queueCard, 1);
        panel.Children.Add(queueCard);
        return panel;
    }

    private static void AddHeaderCell(Grid grid, string text, int col)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.7,
            Margin = new Thickness(4, 0, 4, 0),
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    // 右栏

    private Control BuildRightPanel()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 16,
        };

        // 预览卡
        _previewCard = Common.MakeCard();
        var previewStack = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            Margin = new Thickness(24, 20, 24, 20),
        };
        var previewLabel = new TextBlock
        {
            Text = AppUtils.Tr("notification.preview_label"),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
        };
        Grid.SetRow(previewLabel, 0);
        previewStack.Children.Add(previewLabel);
        _previewText = new TextBlock
        {
            Text = AppUtils.Tr("notification.preview_hint"),
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _previewBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            MinHeight = 110,
            Child = _previewText,
        };
        Grid.SetRow(_previewBorder, 1);
        previewStack.Children.Add(_previewBorder);
        _previewCard.Child = previewStack;
        Grid.SetRow(_previewCard, 0);
        panel.Children.Add(_previewCard);

        // 配置卡
        var configCard = Common.MakeCard();
        var config = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"),
            ColumnSpacing = CtrlGap,
            RowSpacing = CtrlGap,
            Margin = new Thickness(16, 16, 16, 16),
        };
        int NewRow()
        {
            config.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return config.RowDefinitions.Count - 1;
        }

        void AddFull(Control c)
        {
            Grid.SetRow(c, NewRow());
            Grid.SetColumn(c, 0);
            Grid.SetColumnSpan(c, 4);
            config.Children.Add(c);
        }

        // 标签 控件
        void AddWide(string label, Control control)
        {
            var r = NewRow();
            var lb = MakeFieldLabel(label);
            Grid.SetRow(lb, r);
            Grid.SetColumn(lb, 0);
            config.Children.Add(lb);
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);
            Grid.SetColumnSpan(control, 3);
            config.Children.Add(control);
        }

        void AddPair(string leftLabel, Control left, string rightLabel, Control right)
        {
            var r = NewRow();
            var lb0 = MakeFieldLabel(leftLabel);
            Grid.SetRow(lb0, r);
            Grid.SetColumn(lb0, 0);
            config.Children.Add(lb0);
            Grid.SetRow(left, r);
            Grid.SetColumn(left, 1);
            config.Children.Add(left);

            var lb1 = MakeFieldLabel(rightLabel);
            Grid.SetRow(lb1, r);
            Grid.SetColumn(lb1, 2);
            config.Children.Add(lb1);
            Grid.SetRow(right, r);
            Grid.SetColumn(right, 3);
            config.Children.Add(right);
        }

        AddFull(new TextBlock
        {
            Text = AppUtils.Tr("notification.config_label"),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
        });

        // 类型
        _typeCombo = Ctrl(new ComboBox { SelectedIndex = 0 });
        _typeCombo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("notification.type_scroll"), Tag = NotifType.Scroll });
        _typeCombo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("notification.type_corner"), Tag = NotifType.Corner });
        _typeCombo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("notification.type_fullscreen"), Tag = NotifType.Fullscreen });
        _typeCombo.SelectedIndex = 0;
        AddWide(AppUtils.Tr("notification.type_label"), _typeCombo);

        // TTS 语音/语速/音量
        _ttsVoiceCombo = Ctrl(new ComboBox());
        _ttsVoiceCombo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("notification.tts_no_speak"), Tag = "done" });
        foreach (var voice in TtsVoices)
        {
            _ttsVoiceCombo.Items.Add(new ComboBoxItem { Content = voice, Tag = voice });
        }
        _ttsVoiceCombo.SelectedIndex = 1;
        AddWide(AppUtils.Tr("notification.tts_voice_label"), _ttsVoiceCombo);

        _ttsRateSpin = Ctrl(new NumericUpDown { Minimum = 50, Maximum = 200, Value = 100, Increment = 5 });
        _ttsVolumeSpin = Ctrl(new NumericUpDown { Minimum = 0, Maximum = 200, Value = 100, Increment = 5 });
        AddPair(AppUtils.Tr("notification.tts_rate_label"), _ttsRateSpin,
                AppUtils.Tr("notification.tts_volume_label"), _ttsVolumeSpin);

        // 背景色/文字色
        _bgColorPicker = Ctrl(new ColorPicker { Color = Color.FromArgb(180, 0, 0, 0) });
        _fgColorPicker = Ctrl(new ColorPicker { Color = Colors.White });
        _bgColorPicker.ColorChanged += (_, _) => UpdatePreview();
        _fgColorPicker.ColorChanged += (_, _) => UpdatePreview();
        AddPair(AppUtils.Tr("notification.bg_color"), _bgColorPicker,
                AppUtils.Tr("notification.text_color"), _fgColorPicker);

        // 字号/字重
        _fontSizeSpin = Ctrl(new NumericUpDown { Minimum = 12, Maximum = 72, Value = 24 });
        _fontSizeSpin.ValueChanged += (_, _) => UpdatePreview();
        _fontWeightCombo = Ctrl(new ComboBox { SelectedIndex = 1 });
        _fontWeightCombo.Items.Add(new ComboBoxItem { Content = "Normal", Tag = 0 });
        _fontWeightCombo.Items.Add(new ComboBoxItem { Content = "Bold", Tag = 1 });
        _fontWeightCombo.Items.Add(new ComboBoxItem { Content = "Black", Tag = 2 });
        _fontWeightCombo.SelectedIndex = 1;
        _fontWeightCombo.SelectionChanged += (_, _) => UpdatePreview();
        AddPair(AppUtils.Tr("notification.font_size"), _fontSizeSpin,
                AppUtils.Tr("notification.font_weight"), _fontWeightCombo);

        AddFull(new Separator { Margin = new Thickness(0, 2, 0, 2) });

        // 速度/时长
        _speedSpin = Ctrl(new NumericUpDown { Minimum = 1, Maximum = 20, Value = 5 });
        _durationSpin = Ctrl(new NumericUpDown { Minimum = 1, Maximum = 60, Value = 10 });
        AddPair(AppUtils.Tr("notification.settings_speed"), _speedSpin,
                AppUtils.Tr("notification.settings_duration"), _durationSpin);

        // 横幅背景高/鼠标穿透 (写配置)
        _bannerBgHeightSpin = Ctrl(new NumericUpDown
        {
            Minimum = 40,
            Maximum = 300,
            Value = Config.ScrollBannerBgHeight.Value,
        });
        _bannerBgHeightSpin.ValueChanged += (_, _) =>
        {
            if (_bannerBgHeightSpin.Value.HasValue)
            {
                Config.ScrollBannerBgHeight.Value = (int)_bannerBgHeightSpin.Value.Value;
            }
        };
        _mouseThroughSwitch = new ToggleSwitch
        {
            IsChecked = Config.ScrollBannerMouseThrough.Value,
            OffContent = AppUtils.Tr("common.off"),
            OnContent = AppUtils.Tr("common.on"),
            Height = CtrlH,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = CtrlMinW,
        };
        _mouseThroughSwitch.IsCheckedChanged += (_, _) =>
            Config.ScrollBannerMouseThrough.Value = _mouseThroughSwitch.IsChecked == true;
        AddPair(AppUtils.Tr("notification.banner_bg_height"), _bannerBgHeightSpin,
                AppUtils.Tr("notification.banner_mouse_through"), _mouseThroughSwitch);

        AddFull(new Separator { Margin = new Thickness(0, 2, 0, 2) });

        // 发送按钮行
        var addQueueButton = new Button
        {
            Content = AppUtils.Tr("notification.add_queue"),
            Height = CtrlH,
            Padding = new Thickness(16, 0, 16, 0),
            Background = new SolidColorBrush(Color.Parse("#30c361")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(4),
        };
        addQueueButton.Click += (_, _) => OnAddToQueue();

        var scheduleButton = new Button
        {
            Content = AppUtils.Tr("notification.schedule"),
            Height = CtrlH,
            Padding = new Thickness(16, 0, 16, 0),
        };
        scheduleButton.Click += (_, _) => OnScheduleClicked();

        _syncBoardCheck = new CheckBox
        {
            Content = AppUtils.Tr("notification.sync_to_board"),
            IsChecked = Config.NotificationSyncBoard.Value,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = CtrlH,
        };
        _syncBoardCheck.IsCheckedChanged += (_, _) =>
            Config.NotificationSyncBoard.Value = _syncBoardCheck.IsChecked == true;

        var actionRow = new WrapPanel { Orientation = Orientation.Horizontal };
        addQueueButton.Margin = new Thickness(0, 0, 8, 6);
        scheduleButton.Margin = new Thickness(0, 0, 8, 6);
        _syncBoardCheck.Margin = new Thickness(0, 0, 8, 6);
        actionRow.Children.Add(addQueueButton);
        actionRow.Children.Add(scheduleButton);
        actionRow.Children.Add(_syncBoardCheck);
        AddFull(actionRow);

        AddFull(new Separator { Margin = new Thickness(0, 2, 0, 2) });

        // 队列操作
        AddFull(new TextBlock
        {
            Text = AppUtils.Tr("notification.queue_ops"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.8,
        });

        var nextButton = new Button { Content = AppUtils.Tr("notification.next_display"), Height = CtrlH, Padding = new Thickness(12, 0, 12, 0) };
        nextButton.Click += (_, _) => OnNextDisplay();
        var upButton = new Button { Content = AppUtils.Tr("common.up"), Height = CtrlH, Padding = new Thickness(12, 0, 12, 0) };
        upButton.Click += (_, _) => MoveQueue(-1);
        var downButton = new Button { Content = AppUtils.Tr("common.down"), Height = CtrlH, Padding = new Thickness(12, 0, 12, 0) };
        downButton.Click += (_, _) => MoveQueue(1);
        var delButton = new Button { Content = AppUtils.Tr("notification.delete_task"), Height = CtrlH, Padding = new Thickness(12, 0, 12, 0) };
        delButton.Click += (_, _) => OnDeleteTask();
        var showCfgButton = new Button { Content = AppUtils.Tr("notification.edit_config"), Height = CtrlH, Padding = new Thickness(12, 0, 12, 0) };
        showCfgButton.Click += (_, _) => OnShowConfig();

        var btnRow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { nextButton, upButton, downButton, delButton, showCfgButton })
        {
            b.Margin = new Thickness(0, 0, 8, 6);
            btnRow.Children.Add(b);
        }
        AddFull(btnRow);

        configCard.Child = config;
        Grid.SetRow(configCard, 1);
        panel.Children.Add(configCard);

        // 右栏宽度不足 隐藏预览区  整栏空间让给配置区
        configCard.SizeChanged += (_, _) => UpdatePreviewVisibility(configCard.Bounds.Width);
        return panel;
    }

    /// <summary>字段标签: 统一宽度/高度</summary>
    private static TextBlock MakeFieldLabel(string text) => new()
    {
        Text = text,
        Width = LabelW,
        FontSize = 13,
        Height = CtrlH,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };

    /// <summary>统一输入控件尺寸: 同高 + 拉伸到列宽(列用 * 均分 → 同排等宽)</summary>
    private static T Ctrl<T>(T control) where T : Control
    {
        control.Height = CtrlH;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.MinWidth = CtrlMinW;
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    /// <summary>右栏窄于阈值时隐藏预览区</summary>
    private void UpdatePreviewVisibility(double rightColumnWidth)
    {
        if (rightColumnWidth <= 1)
        {
            return;
        }
        var show = rightColumnWidth >= PreviewMinColumnWidth;
        if (_previewCard.IsVisible == show)
        {
            return;
        }
        _previewCard.IsVisible = show;
        Log.Debug($"[通知页] 右栏宽度 {rightColumnWidth:F0} → 预览区{(show ? "显示" : "隐藏")}");
    }

    // 预览

    private void UpdatePreview()
    {
        if (_previewText is null || _fontSizeSpin is null)
        {
            return;
        }
        var text = (_contentEdit.Text ?? "").Trim();
        _previewText.Text = text.Length > 0 ? text : AppUtils.Tr("notification.preview_hint");
        _previewText.FontSize = (double)(_fontSizeSpin.Value ?? 24);
        _previewText.FontWeight = (_fontWeightCombo?.SelectedIndex ?? 1) switch
        {
            0 => FontWeight.Normal,
            2 => FontWeight.Black,
            _ => FontWeight.Bold,
        };
        _previewText.Foreground = new SolidColorBrush(_fgColorPicker?.Color ?? Colors.White);
        if (_previewBorder is not null && _bgColorPicker is not null)
        {
            _previewBorder.Background = new SolidColorBrush(_bgColorPicker.Color);
        }
    }

    // 数据构建

    private NotificationRequest BuildNotifData()
    {
        return new NotificationRequest
        {
            Type = (_typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? NotifType.Scroll,
            Content = (_contentEdit.Text ?? "").Trim(),
            Speed = (int)(_speedSpin.Value ?? 5),
            Duration = (int)(_durationSpin.Value ?? 10),
            BgColor = _bgColorPicker.Color.ToString(),
            BgAlpha = _bgColorPicker.Color.A,
            TextColor = _fgColorPicker.Color.ToString(),
            FontSize = (int)(_fontSizeSpin.Value ?? 24),
            FontWeight = _fontWeightCombo.SelectedIndex,
            TtsVoice = (_ttsVoiceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "done",
            TtsRate = (int)(_ttsRateSpin.Value ?? 100),
            TtsVolume = (int)(_ttsVolumeSpin.Value ?? 100),
        };
    }

    // 队列管理

    private void OnAddToQueue()
    {
        var content = (_contentEdit.Text ?? "").Trim();
        if (content.Length == 0)
        {
            ShowTip(FAInfoBarSeverity.Warning, AppUtils.Tr("notification.empty_content"));
            return;
        }
        var item = new QueueItem { Request = BuildNotifData() };
        lock (_queueLock)
        {
            _queue.Add(item);
        }
        Log.Info($"通知入队: {content[..Math.Min(30, content.Length)]} 队列长度 {CountQueue()}");
        RefreshQueueTable();
        ShowTip(FAInfoBarSeverity.Success, AppUtils.Tr("notification.added_to_queue"));
        if (!_isShowing)
        {
            SendNext();
        }
    }

    private int CountQueue()
    {
        lock (_queueLock)
        {
            return _queue.Count;
        }
    }

    // 从队列取第一个发送
    private void SendNext()
    {
        QueueItem? next = null;
        lock (_queueLock)
        {
            if (_queue.Count == 0)
            {
                _isShowing = false;
                _showingUid = "";
                RefreshQueueTable();
                return;
            }
            // 右下角通知即时弹不占队列
            if (_queue[0].Request.Type == NotifType.Corner)
            {
                var corner = _queue[0];
                _queue.RemoveAt(0);
                next = corner;
            }
            else
            {
                _isShowing = true;
                _showingUid = _queue[0].Uid;
            }
        }
        if (next is not null)
        {
            RefreshQueueTable();
            _manager.HandleNotification(next.Request);
            SendNext();
            return;
        }
        RefreshQueueTable();
        var head = PeekHead();
        if (head is not null)
        {
            Log.Info($"队列发送通知: {head.Request.Content[..Math.Min(30, head.Request.Content.Length)]}");
            _manager.HandleNotification(head.Request);
        }
    }

    private QueueItem? PeekHead()
    {
        lock (_queueLock)
        {
            return _queue.Count > 0 ? _queue[0] : null;
        }
    }

    // 通知显示完毕 出队发下一条
    private void OnNotificationShown()
    {
        lock (_queueLock)
        {
            for (var i = 0; i < _queue.Count; i++)
            {
                if (_queue[i].Uid == _showingUid)
                {
                    _queue.RemoveAt(i);
                    break;
                }
            }
            _isShowing = false;
            _showingUid = "";
        }
        Log.Info($"[通知] 显示完毕 剩余队列 {CountQueue()}");
        RefreshQueueTable();
        if (CountQueue() > 0)
        {
            SendNext();
        }
    }

    // 刷新队列表
    private void RefreshQueueTable()
    {
        List<QueueItem> snapshot;
        string showingUid;
        lock (_queueLock)
        {
            snapshot = _queue.ToList();
            showingUid = _showingUid;
        }
        Dispatcher.UIThread.Post(() =>
        {
            _queueTable.Items.Clear();
            for (var i = 0; i < snapshot.Count; i++)
            {
                var item = snapshot[i];
                string statusText;
                IBrush statusBrush;
                if (item.Uid == showingUid)
                {
                    statusText = AppUtils.Tr("notification.status_showing");
                    statusBrush = Brushes.Green;
                }
                else if (item.Scheduled)
                {
                    statusText = AppUtils.Tr("notification.status_scheduled");
                    statusBrush = new SolidColorBrush(Color.Parse("#f0ad4e"));
                }
                else
                {
                    statusText = AppUtils.Tr("notification.status_queued");
                    statusBrush = Brushes.Gray;
                }
                var content = item.Request.Content;
                if (item.Scheduled && item.FireTime.HasValue)
                {
                    content += $"  ({item.FireTime.Value:yyyy-MM-dd HH:mm})";
                }
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("34,70,*,70"),
                    Margin = new Thickness(4, 2, 4, 2),
                };
                AddRowCell(row, (i + 1).ToString(), 0, Brushes.Gray);
                AddRowCell(row, TypeLabel(item.Request.Type), 1, Brushes.Gray);
                AddRowCell(row, content, 2, Brushes.Gray);
                AddRowCell(row, statusText, 3, statusBrush);
                _queueTable.Items.Add(row);
            }
            _queueCountLabel.Text = snapshot.Count.ToString();
        });
    }

    private static string TypeLabel(string type) => type switch
    {
        NotifType.Scroll => AppUtils.Tr("notification.type_scroll"),
        NotifType.Corner => AppUtils.Tr("notification.type_corner"),
        NotifType.Fullscreen => AppUtils.Tr("notification.type_fullscreen"),
        _ => type,
    };

    private static void AddRowCell(Grid grid, string text, int col, IBrush brush)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    // 上下移动
    private void MoveQueue(int delta)
    {
        var row = _queueTable.SelectedIndex;
        lock (_queueLock)
        {
            var target = row + delta;
            if (row < 0 || row >= _queue.Count || target < 0 || target >= _queue.Count)
            {
                return;
            }
            (_queue[row], _queue[target]) = (_queue[target], _queue[row]);
        }
        RefreshQueueTable();
        _queueTable.SelectedIndex = row + delta;
    }

    // 下一显示
    private void OnNextDisplay()
    {
        var row = _queueTable.SelectedIndex;
        QueueItem? item = null;
        lock (_queueLock)
        {
            if (row < 0 || row >= _queue.Count)
            {
                ShowTip(FAInfoBarSeverity.Warning, AppUtils.Tr("notification.select_task_first"));
                return;
            }
            item = _queue[row];
            _queue.RemoveAt(row);
            _queue.Insert(0, item);
        }
        Log.Info($"[通知] 手动触发显示 原位置 {row} -> 队首");
        RefreshQueueTable();
        _queueTable.SelectedIndex = 0;
        if (!_isShowing)
        {
            SendNext();
        }
        else
        {
            ShowTip(FAInfoBarSeverity.Informational, AppUtils.Tr("notification.will_display_next"));
        }
    }

    // 删除任务
    private async void OnDeleteTask()
    {
        var row = _queueTable.SelectedIndex;
        if (row < 0)
        {
            ShowTip(FAInfoBarSeverity.Warning, AppUtils.Tr("notification.select_task_first"));
            return;
        }
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return;
        }
        var ok = await Common.Confirm(owner, AppUtils.Tr("common.confirm_delete"),
            AppUtils.Tr("notification.confirm_delete_task"));
        if (!ok)
        {
            return;
        }
        lock (_queueLock)
        {
            if (row < _queue.Count && _queue[row].Uid == _showingUid)
            {
                ShowTip(FAInfoBarSeverity.Warning, AppUtils.Tr("notification.cannot_delete_showing"));
                return;
            }
            if (row < _queue.Count)
            {
                _queue.RemoveAt(row);
            }
        }
        RefreshQueueTable();
    }

    // 定时发送
    private async void OnScheduleClicked()
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return;
        }
        var datePicker = new DatePicker { SelectedDate = DateTimeOffset.Now, Width = 320 };
        var timePicker = new TimePicker { SelectedTime = DateTimeOffset.Now.AddMinutes(1).TimeOfDay, Width = 320 };
        var stack = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = AppUtils.Tr("notification.date_label"), FontWeight = FontWeight.SemiBold });
        stack.Children.Add(datePicker);
        stack.Children.Add(new TextBlock { Text = AppUtils.Tr("notification.time_label"), FontWeight = FontWeight.SemiBold });
        stack.Children.Add(timePicker);
        var confirmButton = new Button { Content = AppUtils.Tr("common.confirm"), Height = 34, HorizontalAlignment = HorizontalAlignment.Right };
        stack.Children.Add(confirmButton);

        var dialog = new Window
        {
            Title = AppUtils.Tr("notification.schedule_title"),
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = stack,
        };
        var confirmed = false;
        confirmButton.Click += (_, _) => { confirmed = true; dialog.Close(); };
        await dialog.ShowDialog(owner);
        if (!confirmed)
        {
            Log.Debug("[通知页] 定时发送对话框取消");
            return;
        }

        var content = (_contentEdit.Text ?? "").Trim();
        if (content.Length == 0)
        {
            ShowTip(FAInfoBarSeverity.Warning, AppUtils.Tr("notification.empty_content"));
            return;
        }

        var selDate = datePicker.SelectedDate?.DateTime ?? DateTime.Now;
        var selTime = timePicker.SelectedTime ?? TimeSpan.Zero;
        var fireTime = selDate.Date + selTime;
        if (fireTime <= DateTime.Now)
        {
            ShowTip(FAInfoBarSeverity.Error, AppUtils.Tr("notification.invalid_schedule_time"));
            return;
        }

        var item = new QueueItem
        {
            Request = BuildNotifData(),
            Scheduled = true,
            FireTime = fireTime,
        };
        lock (_queueLock)
        {
            _queue.Add(item);
        }
        RefreshQueueTable();
        ShowTip(FAInfoBarSeverity.Success,
            AppUtils.Tr("notification.schedule_set", ("time", fireTime.ToString("yyyy-MM-dd HH:mm:ss"))));
        Log.Info($"[通知] 定时任务已设置 {fireTime:yyyy-MM-dd HH:mm:ss}");
    }

    // 10 秒检查到期定时任务
    private void CheckScheduled()
    {
        List<QueueItem> due = [];
        lock (_queueLock)
        {
            var now = DateTime.Now;
            for (var i = _queue.Count - 1; i >= 0; i--)
            {
                var item = _queue[i];
                if (item.Scheduled && item.FireTime.HasValue && item.FireTime.Value <= now)
                {
                    item.Scheduled = false;
                    item.FireTime = null;
                    _queue.RemoveAt(i);
                    _queue.Insert(0, item);
                    due.Add(item);
                }
            }
        }
        if (due.Count > 0)
        {
            Log.Info($"[通知] 到期定时任务{due.Count}项 已移至队首");
            RefreshQueueTable();
            if (!_isShowing)
            {
                SendNext();
            }
        }
    }

    // 消息条
    private void ShowTip(FAInfoBarSeverity severity, string content)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Content 是 ScrollViewer(非 UserControl) 不能按类型拦截 否则所有反馈静默失效
            if (this.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault() is not { } root)
            {
                return;
            }
            var bar = new FAInfoBar
            {
                Severity = severity,
                Message = content,
                IsOpen = true,
                IsClosable = true,
                Margin = new Thickness(0, 0, 0, 4),
            };
            bar.Closed += (_, _) => root.Children.Remove(bar);
            root.Children.Insert(1, bar);
            _ = Task.Delay(2500).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() => bar.IsOpen = false), TaskScheduler.Default);
        });
    }

    // 编辑选中任务配置

    private async void OnShowConfig()
    {
        var row = _queueTable.SelectedIndex;
        QueueItem? item;
        lock (_queueLock)
        {
            item = row >= 0 && row < _queue.Count ? _queue[row] : null;
        }
        if (item is null)
        {
            ShowTip(FAInfoBarSeverity.Warning, AppUtils.Tr("notification.select_task_first"));
            return;
        }
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return;
        }
        var updated = await EditRequestDialogAsync(owner, item.Request);
        if (updated is null)
        {
            return;
        }
        lock (_queueLock)
        {
            if (row < _queue.Count)
            {
                _queue[row].Request = updated;
            }
        }
        RefreshQueueTable();
        ShowTip(FAInfoBarSeverity.Success, AppUtils.Tr("notification.config_updated"));
        Log.Info($"[通知] 任务配置已更新 行={row}");
    }

    // 编辑弹窗 返回 null 表示取消
    private static async Task<NotificationRequest?> EditRequestDialogAsync(Window owner, NotificationRequest request)
    {
        var typeCombo = new ComboBox { MinWidth = 150, SelectedIndex = 0 };
        typeCombo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("notification.type_scroll"), Tag = NotifType.Scroll });
        typeCombo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("notification.type_corner"), Tag = NotifType.Corner });
        typeCombo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("notification.type_fullscreen"), Tag = NotifType.Fullscreen });
        for (var i = 0; i < typeCombo.Items.Count; i++)
        {
            if ((typeCombo.Items[i] as ComboBoxItem)?.Tag as string == request.Type)
            {
                typeCombo.SelectedIndex = i;
            }
        }

        var contentEdit = new TextBox
        {
            Text = request.Content,
            AcceptsReturn = true,
            MinHeight = 90,
            TextWrapping = TextWrapping.Wrap,
        };

        var bgPicker = new ColorPicker { Color = Color.Parse(request.BgColor), Width = 160 };
        var fgPicker = new ColorPicker { Color = Color.Parse(request.TextColor), Width = 160 };

        var sizeSpin = new NumericUpDown { Minimum = 10, Maximum = 100, Value = request.FontSize, Width = 140 };
        var weightCombo = new ComboBox { MinWidth = 140, SelectedIndex = request.FontWeight is >= 0 and <= 2 ? request.FontWeight : 1 };
        weightCombo.Items.Add(new ComboBoxItem { Content = "Normal", Tag = 0 });
        weightCombo.Items.Add(new ComboBoxItem { Content = "Bold", Tag = 1 });
        weightCombo.Items.Add(new ComboBoxItem { Content = "Black", Tag = 2 });
        weightCombo.SelectedIndex = request.FontWeight is >= 0 and <= 2 ? request.FontWeight : 1;

        var speedSpin = new NumericUpDown { Minimum = 1, Maximum = 20, Value = request.Speed, Width = 140 };
        var durSpin = new NumericUpDown { Minimum = 1, Maximum = 60, Value = request.Duration, Width = 140 };

        var voiceCombo = new ComboBox { MinWidth = 150 };
        voiceCombo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("notification.tts_no_speak"), Tag = "done" });
        foreach (var voice in TtsVoices)
        {
            voiceCombo.Items.Add(new ComboBoxItem { Content = voice, Tag = voice });
        }
        for (var i = 0; i < voiceCombo.Items.Count; i++)
        {
            if ((voiceCombo.Items[i] as ComboBoxItem)?.Tag as string == request.TtsVoice)
            {
                voiceCombo.SelectedIndex = i;
            }
        }

        var rateSpin = new NumericUpDown { Minimum = 50, Maximum = 200, Value = request.TtsRate, Width = 140, Increment = 5 };
        var volumeSpin = new NumericUpDown { Minimum = 0, Maximum = 200, Value = request.TtsVolume, Width = 140, Increment = 5 };

        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.content_label"), contentEdit));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.type_label"), typeCombo));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.bg_color"), bgPicker));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.text_color"), fgPicker));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.font_size"), sizeSpin));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.font_weight"), weightCombo));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.settings_speed"), speedSpin));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.settings_duration"), durSpin));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.tts_voice_label"), voiceCombo));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.tts_rate_label"), rateSpin));
        stack.Children.Add(MakeLabeledControlStatic(AppUtils.Tr("notification.tts_volume_label"), volumeSpin));

        var confirmed = false;
        Window? dialog = null;
        var confirmButton = new Button
        {
            Content = AppUtils.Tr("common.confirm"),
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.Parse("#30c361")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(4),
        };
        confirmButton.Click += (_, _) => { confirmed = true; dialog?.Close(); };
        var cancelButton = new Button { Content = AppUtils.Tr("dialog.cancel"), Height = 34, HorizontalAlignment = HorizontalAlignment.Right };
        cancelButton.Click += (_, _) => dialog?.Close();
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(cancelButton);
        btnRow.Children.Add(confirmButton);
        stack.Children.Add(btnRow);

        dialog = new Window
        {
            Title = AppUtils.Tr("notification.config_detail_title"),
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = stack, MaxHeight = 560 },
        };
        await dialog.ShowDialog(owner);
        if (!confirmed)
        {
            return null;
        }

        var bg = bgPicker.Color;
        return request with
        {
            Type = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? NotifType.Scroll,
            Content = contentEdit.Text?.Trim() ?? "",
            BgColor = bg.ToString(),
            BgAlpha = bg.A,
            TextColor = fgPicker.Color.ToString(),
            FontSize = (int)(sizeSpin.Value ?? 24),
            FontWeight = weightCombo.SelectedIndex,
            Speed = (int)(speedSpin.Value ?? 5),
            Duration = (int)(durSpin.Value ?? 10),
            TtsVoice = (voiceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "done",
            TtsRate = (int)(rateSpin.Value ?? 100),
            TtsVolume = (int)(volumeSpin.Value ?? 100),
        };
    }

    private static StackPanel MakeLabeledControlStatic(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 95,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        });
        row.Children.Add(control);
        return row;
    }
}
