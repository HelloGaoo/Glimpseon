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

// AiChatWidget
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Media.Imaging;
using IOPath = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Threading;
using Glimpseon.Core.Services;
using Markdown.Avalonia;
using Avalonia.Input.Platform;
using System.Runtime.InteropServices;
using AvColor = Avalonia.Media.Color;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Utilities;
using Avalonia.VisualTree;


namespace Glimpseon.UI;

/// <summary>AI 问答 气泡会话持久化</summary>
public sealed class AiChatWidget : WidgetCardBase
{
    private sealed record ChatMsg(string Role, string Text, string Reasoning = "", long ReasoningMs = 0);

    /// <summary>历史会话</summary>
    private sealed class ChatSession
    {
        public string Id = "";
        public string Title = "";
        public long Ts;
        public List<ChatMsg> Msgs = new();
    }

    private const int MaxHistory = 40;
    private const int ContextTurns = 8;

    private readonly ScrollViewer _scroll = new() { Padding = new Thickness(WidgetSpace.Gap, WidgetSpace.Gap, WidgetSpace.Gap, WidgetSpace.Gap) };
    private readonly StackPanel _flow = new() { Spacing = WidgetSpace.Gap };
    private readonly TextBox _input = new()
    {
        FontSize = WidgetType.Body,
        Padding = new Thickness(2, WidgetSpace.GapXs),
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        MaxHeight = 132,
    };
    private readonly StackPanel _welcome = new()
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = WidgetSpace.Gap,
        IsVisible = false,
    };
    private readonly Border _composer = new()
    {
        CornerRadius = new CornerRadius(WidgetSpace.Radius),
        Padding = new Thickness(12, 8, 8, 8),
        Margin = new Thickness(12, 4, 12, 8),
    };
    private readonly Button _sendBtn = new()
    {
        Width = 32,
        Height = 32,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(16),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Content = FAIcons.IconGlyph("E724", 14),
    };
    private readonly List<ChatSession> _sessions = new();
    private string _currentId = "";
    private string? _dataFile;
    private bool _busy;
    private bool _cursorOn;
    private string _lastLive = "";
    private CancellationTokenSource? _streamCts;
    private Grid? _drawer;
    private Border? _drawerPanel;
    private StackPanel? _drawerList;
    private TranslateTransform? _drawerTranslate;

    // 历史会话制 恒有 current
    private ChatSession Current => _sessions.FirstOrDefault(s => s.Id == _currentId) ?? _sessions[0];
    private List<ChatMsg> _history => Current.Msgs;
    private TextBlock? _liveThink;
    private TextBlock? _liveBody;
    private Grid? _liveThinkBar;
    private TextBlock? _liveHead;
    private DispatcherTimer? _pulseTimer;
    private DispatcherTimer? _blinkTimer;

    public AiChatWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        ReadBgOverride(config);

        var title = new TextBlock
        {
            Text = AppUtils.Tr("ai_chat.title"),
            FontSize = WidgetType.Title,
            FontWeight = WidgetWeight.Title,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // header 键组
        var clearBtn = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E74D", 14),
            Opacity = 0.6,
        };
        ToolTip.SetTip(clearBtn, AppUtils.Tr("ai_chat.clear_tip"));
        clearBtn.Click += (_, _) =>
        {
            if (_busy)
            {
                return;
            }
            _ = ConfirmThenClear();
        };

        var newChatBtn = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E109", 14),
            Opacity = 0.6,
        };
        ToolTip.SetTip(newChatBtn, AppUtils.Tr("ai_chat.new_chat_tip"));
        newChatBtn.Click += (_, _) => StartNewChat();

        // 左滑抽屉 覆盖整卡
        var drawerList = new StackPanel();
        _drawerList = drawerList;
        var drawerScroll = new ScrollViewer { Content = drawerList, Padding = new Thickness(6, 0, 6, 10) };
        var drawerTitle = new TextBlock
        {
            Text = AppUtils.Tr("ai_chat.history_tip"),
            FontSize = WidgetType.Title,
            FontWeight = WidgetWeight.Title,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var drawerClose = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E711", 13),
            Opacity = 0.6,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        drawerClose.Click += (_, _) => HideDrawer();
        var drawerHead = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Margin = new Thickness(12, WidgetSpace.Gap, 8, WidgetSpace.GapXs),
        };
        drawerHead.Children.Add(drawerTitle);
        drawerHead.Children.Add(drawerClose);
        Grid.SetColumn(drawerClose, 1);

        var newChatPill = new Button
        {
            Height = 36,
            Margin = new Thickness(12, 4, 12, 8),
            CornerRadius = new CornerRadius(18),
            Content = AppUtils.Tr("ai_chat.new_chat_tip"),
            FontSize = WidgetType.Label,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        newChatPill.Click += (_, _) =>
        {
            StartNewChat();
            HideDrawer();
        };

        var drawerGrid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto,*") };
        drawerGrid.Children.Add(drawerHead);
        drawerGrid.Children.Add(newChatPill);
        drawerGrid.Children.Add(drawerScroll);
        Grid.SetRow(newChatPill, 1);
        Grid.SetRow(drawerScroll, 2);

        _drawerPanel = new Border { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, Child = drawerGrid };
        // X -280→0 Transitions 整体赋值
        _drawerTranslate = new TranslateTransform { X = -280 };
        _drawerTranslate.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            },
        };
        _drawerPanel.RenderTransform = _drawerTranslate;
        var scrim = new Border { Background = new SolidColorBrush(AvColor.FromArgb(110, 0, 0, 0)) };
        scrim.PointerReleased += (_, e) =>
        {
            HideDrawer();
            e.Handled = true;
        };
        _drawer = new Grid { IsVisible = false };
        _drawer.Children.Add(scrim);
        _drawer.Children.Add(_drawerPanel);

        var historyBtn = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E81C", 14),
            Opacity = 0.6,
        };
        ToolTip.SetTip(historyBtn, AppUtils.Tr("ai_chat.history_tip"));
        historyBtn.Click += (_, _) => ShowDrawer();

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.GapXs, VerticalAlignment = VerticalAlignment.Center };
        btnRow.Children.Add(newChatBtn);
        btnRow.Children.Add(historyBtn);
        btnRow.Children.Add(clearBtn);

        var header = new Grid { Margin = new Thickness(12, 8, 12, 4) };
        header.Children.Add(title);
        header.Children.Add(btnRow);

        _scroll.Content = _flow;
        Grid.SetRow(_scroll, 1);

        // 空态欢迎页
        var logo = FAIcons.IconGlyph("E99A", 34);
        logo.Foreground = new SolidColorBrush(AvColor.Parse(Config.ThemeColor.Value));
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        var hi = new TextBlock
        {
            Text = AppUtils.Tr("ai_chat.welcome"),
            FontSize = WidgetType.Headline,
            FontWeight = WidgetWeight.Title,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _welcome.Children.Add(logo);
        _welcome.Children.Add(hi);
        var chatHost = new Grid();
        chatHost.Children.Add(_scroll);
        chatHost.Children.Add(_welcome);
        Grid.SetRow(chatHost, 1);

        _sendBtn.Click += (_, _) =>
        {
            // busy 即停生
            if (_busy)
            {
                _streamCts?.Cancel();
            }
            else
            {
                _ = SendAsync();
            }
        };
        if (_sendBtn.Content is FAFontIcon sendIcon)
        {
            sendIcon.Foreground = Brushes.White;
        }

        // 剥 PART_BorderElement 模板内框
        _input.TemplateApplied += (_, _) => KillInputChrome();

        var bottomRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        bottomRow.Children.Add(_sendBtn);
        Grid.SetColumn(_sendBtn, 1);
        var composerGrid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto"), RowSpacing = WidgetSpace.GapXs };
        composerGrid.Children.Add(_input);
        composerGrid.Children.Add(bottomRow);
        Grid.SetRow(bottomRow, 1);
        _composer.Child = composerGrid;
        Grid.SetRow(_composer, 2);

        _input.PlaceholderText = AppUtils.Tr("ai_chat.watermark");
        _input.KeyDown += (_, e) =>
        {
            // Enter 发送 Shift 换行
            if (e.Key == Key.Enter && e.KeyModifiers != KeyModifiers.Shift)
            {
                e.Handled = true;
                _ = SendAsync();
            }
        };

        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto,Auto") };
        root.Children.Add(header);
        root.Children.Add(chatHost);
        root.Children.Add(_composer);

        // 模型名+AI 声明
        var footer = new TextBlock
        {
            FontSize = WidgetType.Micro,
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, WidgetSpace.GapSm),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        // 抽屉置顶 跨 4 行
        root.Children.Add(_drawer);
        Grid.SetRowSpan(_drawer, 4);
        void UpdateModelLabel()
        {
            footer.Text = $"{AiService.Resolve().Model} · {AppUtils.Tr("ai_chat.ai_note")}";
        }
        UpdateModelLabel();
        Config.AiProvider.ValueChanged += _ => UpdateModelLabel();
        Config.AiDefaultModel.ValueChanged += _ => UpdateModelLabel();
        Config.AiZhipuModel.ValueChanged += _ => UpdateModelLabel();
        Config.AiDeepseekModel.ValueChanged += _ => UpdateModelLabel();
        Config.AiCustomModel.ValueChanged += _ => UpdateModelLabel();

        StyleComposer();
        Card.Child = root;
        Content = Card;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_dataFile is null && !string.IsNullOrEmpty(ComponentId))
        {
            _dataFile = IOPath.Combine(Paths.DataUser, "aichat", $"{ComponentId}.json");
            try
            {
                if (File.Exists(_dataFile) && JsonNode.Parse(File.ReadAllText(_dataFile)) is JsonNode root)
                {
                    LoadSessions(root);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AIChat] 读取记录失败: {ex.Message}");
            }
            if (_sessions.Count == 0)
            {
                CreateSession();
            }
            Rebuild();
        }
    }

    // 会话档 兼容旧数组
    private void LoadSessions(JsonNode root)
    {
        if (root is JsonArray arr)
        {
            var s = new ChatSession { Id = Guid.NewGuid().ToString("N"), Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds() };
            foreach (var n in arr)
            {
                if (n is JsonObject o)
                {
                    s.Msgs.Add(new ChatMsg(
                    (string?)o["role"] ?? "user",
                    (string?)o["text"] ?? "",
                    (string?)o["reasoning"] ?? "",
                    o["reasoning_ms"] is JsonValue jv && jv.TryGetValue<long>(out var msv) ? msv : 0));
                }
            }
            _sessions.Add(s);
            _currentId = s.Id;
            return;
        }
        if (root is JsonObject obj)
        {
            _currentId = (string?)obj["current"] ?? "";
            if (obj["sessions"] is JsonArray list)
            {
                foreach (var n in list)
                {
                    if (n is not JsonObject so)
                    {
                        continue;
                    }
                    var s = new ChatSession
                    {
                        Id = (string?)so["id"] ?? Guid.NewGuid().ToString("N"),
                        Title = (string?)so["title"] ?? "",
                        Ts = so["ts"] is JsonValue tv && tv.TryGetValue<long>(out var tsv) ? tsv : 0,
                    };
                    if (so["msgs"] is JsonArray ms)
                    {
                        foreach (var m in ms)
                        {
                            if (m is JsonObject o)
                            {
                                s.Msgs.Add(new ChatMsg(
                                (string?)o["role"] ?? "user",
                                (string?)o["text"] ?? "",
                                (string?)o["reasoning"] ?? "",
                                o["reasoning_ms"] is JsonValue jv && jv.TryGetValue<long>(out var msv) ? msv : 0));
                            }
                        }
                    }
                    _sessions.Add(s);
                }
            }
            if (_sessions.All(s => s.Id != _currentId))
            {
                _currentId = _sessions.FirstOrDefault()?.Id ?? "";
            }
        }
    }

    private void CreateSession()
    {
        var s = new ChatSession { Id = Guid.NewGuid().ToString("N"), Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds() };
        _sessions.Insert(0, s);
        _currentId = s.Id;
        // 上限 20 淘汰最旧
        while (_sessions.Count > 20)
        {
            var oldest = _sessions.LastOrDefault(x => x.Id != _currentId);
            if (oldest is null)
            {
                break;
            }
            _sessions.Remove(oldest);
        }
    }

    // 新会话 空会话不重复开
    private void StartNewChat()
    {
        if (_busy || Current.Msgs.Count == 0)
        {
            return;
        }
        CreateSession();
        Save();
        Rebuild();
    }

    // 主题重绘 busy 跳过
    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        StyleComposer();
        if (_busy)
        {
            return;
        }
        if (_flow.Children.Count > 0)
        {
            Rebuild();
        }
    }

    // busy 切停生方块
    private void SetSendBusy(bool busy)
    {
        var icon = FAIcons.IconGlyph(busy ? "E15B" : "E110", busy ? 12 : 14);
        icon.Foreground = Brushes.White;
        _sendBtn.Content = icon;
    }

    // 剥 PART_BorderElement 内框下划线
    private void KillInputChrome()
    {
        if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(_input).FirstOrDefault(v => v is Border { Name: "PART_BorderElement" }) is Border border)
        {
            border.BorderThickness = new Thickness(0);
            border.Background = Brushes.Transparent;
            border.BorderBrush = null;
        }
    }

    // composer 主题化
    private void StyleComposer()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var dark = CardIsDark;
        var accent = AvColor.Parse(Config.ThemeColor.Value);
        _composer.Background = new SolidColorBrush(p.BandB);
        var channel = dark ? (byte)255 : (byte)0;
        _composer.BorderBrush = new SolidColorBrush(AvColor.FromArgb(dark ? (byte)15 : (byte)26, channel, channel, channel));
        _composer.BorderThickness = new Thickness(1);
        _input.Foreground = new SolidColorBrush(p.Text);
        _input.CaretBrush = new SolidColorBrush(accent);
        _sendBtn.Background = new SolidColorBrush(accent);
        if (_drawerPanel is { } panel)
        {
            panel.Background = new SolidColorBrush(p.BandB);
        }
    }

    private Control BuildMsg(ChatMsg m, bool isLast = false, bool showActions = true)
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var textCol = p.Text;
        var userBg = p.Blend(AvColor.Parse(Config.ThemeColor.Value), 0.22);
        if (m.Role != "user")
        {
            // 助手通栏 思考折叠+md+操作行
            var stack = new StackPanel { Spacing = 2 };
            if (!string.IsNullOrWhiteSpace(m.Reasoning))
            {
                stack.Children.Add(BuildThinkBlock(AppUtils.Tr("ai_chat.thought", ("s", m.ReasoningMs / 1000.0)), m.Reasoning, expanded: false));
            }
            stack.Children.Add(MarkdownLook.Create(m.Text, compact: true));
            if (showActions)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Opacity = 0.55 };
                var copyBtn = MiniAction("E898", "ai_chat.copy_tip");
                copyBtn.Click += (_, _) => _ = CopyTextAsync(m.Text);
                actions.Children.Add(copyBtn);
                if (isLast)
                {
                    var regenBtn = MiniAction("E72C", "ai_chat.regen_tip");
                    regenBtn.Click += (_, _) => _ = RegenerateAsync();
                    actions.Children.Add(regenBtn);
                }
                stack.Children.Add(actions);
            }
            return stack;
        }
        var border = new Border
        {
            Background = new SolidColorBrush(userBg),
            CornerRadius = new CornerRadius(12, 4, 12, 12),
            Padding = new Thickness(12, 8),
            MaxWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock { Text = m.Text, TextWrapping = TextWrapping.Wrap, FontSize = WidgetType.Body, Foreground = new SolidColorBrush(textCol) },
        };
        if (!showActions)
        {
            return border;
        }
        // 用户泡下 复制/编辑
        var wrap = new StackPanel { Spacing = 1 };
        wrap.Children.Add(border);
        var uActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Opacity = WidgetInk.DimOpacity, HorizontalAlignment = HorizontalAlignment.Right };
        var uCopy = MiniAction("E898", "ai_chat.copy_tip");
        uCopy.Click += (_, _) => _ = CopyTextAsync(m.Text);
        uActions.Children.Add(uCopy);
        var uEdit = MiniAction("E104", "ai_chat.edit_tip");
        uEdit.Click += (_, _) => EditUserMsg(m);
        uActions.Children.Add(uEdit);
        wrap.Children.Add(uActions);
        return wrap;
    }

    // 确认弹窗
    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        return owner is not null && await Common.Confirm(owner, title, content);
    }

    // 清空当前会话
    private async Task ConfirmThenClear()
    {
        if (!await ConfirmAsync(AppUtils.Tr("ai_chat.confirm_clear_title"), AppUtils.Tr("ai_chat.confirm_clear_content")))
        {
            return;
        }
        _history.Clear();
        Rebuild();
        Save();
    }

    // 编辑回填+截断分支 record 值相等 按引用定位
    private void EditUserMsg(ChatMsg m)
    {
        if (_busy)
        {
            return;
        }
        var idx = -1;
        for (var i = 0; i < _history.Count; i++)
        {
            if (ReferenceEquals(_history[i], m))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
        {
            return;
        }
        _input.Text = m.Text;
        for (var i = _history.Count - 1; i >= idx; i--)
        {
            _history.RemoveAt(i);
        }
        Save();
        Rebuild();
        _input.Focus();
    }

    // 思考块 官方规格 竖线+缩进
    private Control BuildThinkBlock(string title, string reasoning, bool expanded)
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var accent = AvColor.Parse(Config.ThemeColor.Value);
        var sub = new SolidColorBrush(p.Sub);
        var titleTb = new TextBlock { Text = title, FontSize = WidgetType.Label, Foreground = sub, VerticalAlignment = VerticalAlignment.Center };
        var chev = FAIcons.IconGlyph("E76C", 10);
        chev.VerticalAlignment = VerticalAlignment.Center;
        var icon = FAIcons.IconGlyph("E99A", 13);
        icon.Foreground = new SolidColorBrush(accent);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        headRow.Children.Add(icon);
        headRow.Children.Add(titleTb);
        headRow.Children.Add(chev);
        var head = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(0, 2),
            Child = headRow,
        };

        var body = new TextBlock
        {
            Text = reasoning,
            FontSize = WidgetType.Body,
            LineHeight = WidgetType.LineHeight(WidgetType.Body),
            TextWrapping = TextWrapping.Wrap,
            Foreground = sub,
            Margin = new Thickness(14, 0, 0, 0),
        };
        var line = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(p.Line),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(7, 0, 0, 0),
        };
        var content = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("8,*"),
            Margin = new Thickness(0, 2, 0, 0),
            IsVisible = expanded,
        };
        content.Children.Add(line);
        content.Children.Add(body);
        Grid.SetColumn(body, 1);

        var open = expanded;
        head.PointerEntered += (_, _) => titleTb.Foreground = new SolidColorBrush(p.Text);
        head.PointerExited += (_, _) => titleTb.Foreground = sub;
        head.PointerReleased += (_, e) =>
        {
            open = !open;
            content.IsVisible = open;
            chev.Glyph = char.ConvertFromUtf32(open ? 0xE70D : 0xE76C);
            e.Handled = true;
        };

        var root = new StackPanel();
        root.Children.Add(head);
        root.Children.Add(content);
        return root;
    }

    // 消息操作小按钮
    private Button MiniAction(string hex, string tipKey)
    {
        var btn = new Button
        {
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = FAIcons.IconGlyph(hex, 12),
        };
        ToolTip.SetTip(btn, AppUtils.Tr(tipKey));
        return btn;
    }

    private async Task CopyTextAsync(string text)
    {
        try
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip is not null)
            {
                await clip.SetTextAsync(text);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[AIChat] 复制失败: {e.Message}");
        }
    }

    private void Rebuild()
    {
        _welcome.IsVisible = _history.Count == 0 && !_busy;
        _flow.Children.Clear();
        for (var i = 0; i < _history.Count; i++)
        {
            var m = _history[i];
            _flow.Children.Add(BuildMsg(m, isLast: i == _history.Count - 1, showActions: !_busy));
        }
        if (_busy)
        {
            _flow.Children.Add(BuildMsg(new ChatMsg("assistant", AppUtils.Tr("ai_chat.thinking")), showActions: false));
        }
        _scroll.ScrollToEnd();
    }

    // live 行 流式渲染
    private void ShowLiveRow()
    {
        _welcome.IsVisible = false;
        _flow.Children.Clear();
        for (var i = 0; i < _history.Count; i++)
        {
            _flow.Children.Add(BuildMsg(_history[i], showActions: false));
        }
        SetSendBusy(true);
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var thinkIcon = FAIcons.IconGlyph("E99A", 13);
        thinkIcon.Foreground = new SolidColorBrush(AvColor.Parse(Config.ThemeColor.Value));
        thinkIcon.VerticalAlignment = VerticalAlignment.Center;
        _liveHead = new TextBlock
        {
            Text = AppUtils.Tr("ai_chat.thinking"),
            FontSize = WidgetType.Label,
            Foreground = new SolidColorBrush(p.Sub),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.GapSm };
        headRow.Children.Add(thinkIcon);
        headRow.Children.Add(_liveHead);
        _liveThink = new TextBlock
        {
            Text = "",
            FontSize = WidgetType.Body,
            LineHeight = WidgetType.LineHeight(WidgetType.Body),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(p.Sub),
        };
        var thinkScroll = new ScrollViewer
        {
            MaxHeight = 120,
            Margin = new Thickness(14, 0, 0, 0),
            Content = _liveThink,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var thinkLine = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(p.Line),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(7, 0, 0, 0),
        };
        _liveThinkBar = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("8,*"),
            IsVisible = false,
        };
        _liveThinkBar.Children.Add(thinkLine);
        _liveThinkBar.Children.Add(thinkScroll);
        Grid.SetColumn(thinkScroll, 1);
        _liveBody = new TextBlock
        {
            Text = "",
            FontSize = WidgetType.Body,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(p.Text),
            IsVisible = false,
        };
        var live = new StackPanel { Spacing = WidgetSpace.GapXs };
        live.Children.Add(headRow);
        live.Children.Add(_liveThinkBar);
        live.Children.Add(_liveBody);
        _flow.Children.Add(live);
        _lastLive = "";
        _scroll.ScrollToEnd();

        // 思考头呼吸动画
        _pulseTimer?.Stop();
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        var up = false;
        _pulseTimer.Tick += (_, _) =>
        {
            up = !up;
            if (_liveHead is { IsVisible: true } head)
            {
                head.Opacity = up ? 0.35 : 0.7;
            }
        };
        _pulseTimer.Start();

        // 光标闪烁
        _blinkTimer?.Stop();
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _blinkTimer.Tick += (_, _) =>
        {
            _cursorOn = !_cursorOn;
            if (_liveBody is { IsVisible: true } body)
            {
                body.Text = _lastLive + (_cursorOn ? "▍" : "");
            }
        };
        _blinkTimer.Start();
    }

    // delta 增量 正文折叠思考区
    private void AppendLive(string? reasoning, string? content)
    {
        if (reasoning is { Length: > 0 } && _liveThink is { } think)
        {
            think.Text = string.IsNullOrEmpty(think.Text) ? reasoning.TrimStart() : think.Text + reasoning;
            if (_liveThinkBar is { } bar)
            {
                bar.IsVisible = true;
            }
            FollowScroll();
        }
        if (content is { Length: > 0 } && _liveBody is { } body)
        {
            if (_pulseTimer is { } pt)
            {
                pt.Stop();
            }
            if (_liveThinkBar is { } bar2)
            {
                bar2.IsVisible = false;
            }
            if (_liveHead is { } head)
            {
                head.IsVisible = false;
            }
            _lastLive += content;
            body.IsVisible = true;
            body.Text = _lastLive + (_cursorOn ? "▍" : "");
            FollowScroll();
        }
    }

    // 贴底跟随
    private void FollowScroll()
    {
        if (_scroll.Extent.Height - _scroll.Offset.Y - _scroll.Viewport.Height < 40)
        {
            _scroll.ScrollToEnd();
        }
    }

    private void StopLiveTimers()
    {
        _pulseTimer?.Stop();
        _pulseTimer = null;
        _blinkTimer?.Stop();
        _blinkTimer = null;
    }

    private void Save()
    {
        try
        {
            if (_dataFile is null)
            {
                return;
            }
            // 自动标题+时间戳
            var cur = Current;
            var first = cur.Msgs.FirstOrDefault(m => m.Role == "user");
            cur.Title = first is null ? "" : (first.Text.Length > 16 ? first.Text[..16] + "…" : first.Text);
            cur.Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Directory.CreateDirectory(IOPath.GetDirectoryName(_dataFile)!);
            var list = new JsonArray();
            foreach (var s in _sessions)
            {
                var msgs = new JsonArray();
                foreach (var m in s.Msgs)
                {
                    msgs.Add(new JsonObject { ["role"] = m.Role, ["text"] = m.Text, ["reasoning"] = m.Reasoning, ["reasoning_ms"] = m.ReasoningMs });
                }
                list.Add(new JsonObject { ["id"] = s.Id, ["title"] = s.Title, ["ts"] = s.Ts, ["msgs"] = msgs });
            }
            File.WriteAllText(_dataFile, new JsonObject { ["current"] = _currentId, ["sessions"] = list }.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Warning($"[AIChat] 保存记录失败: {e.Message}");
        }
    }

    // 抽屉开合
    private void ShowDrawer()
    {
        if (_drawer is null || _drawerList is null || _drawerTranslate is null)
        {
            return;
        }
        FillHistoryList(_drawerList);
        _drawer.IsVisible = true;
        _drawerTranslate.X = 0;
    }

    private void HideDrawer()
    {
        if (_drawer is null || _drawerTranslate is null)
        {
            return;
        }
        _drawerTranslate.X = -280;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_drawerTranslate is { X: < 0 } && _drawer is not null)
            {
                _drawer.IsVisible = false;
            }
        };
        t.Start();
    }

    // 历史列表 日期分组
    private void FillHistoryList(StackPanel list)
    {
        list.Children.Clear();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ordered = _sessions.Where(s => s.Msgs.Count > 0).OrderByDescending(s => s.Ts).ToList();
        if (ordered.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr("ai_chat.empty_history"),
                FontSize = WidgetType.Label,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16),
            });
            return;
        }
        string? lastGroup = null;
        foreach (var s in ordered)
        {
            var session = s;
            var group = GroupLabel(session.Ts);
            if (group != lastGroup)
            {
                list.Children.Add(new TextBlock
                {
                    Text = group,
                    FontSize = WidgetType.Micro,
                    Opacity = 0.5,
                    Margin = new Thickness(8, 12, 0, 4),
                });
                lastGroup = group;
            }
            var titleTb = new TextBlock
            {
                Text = string.IsNullOrEmpty(session.Title) ? AppUtils.Tr("ai_chat.new_chat_tip") : session.Title,
                FontSize = WidgetType.Body,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var delBtn = MiniAction("E107", "ai_chat.delete_tip");
            delBtn.Opacity = 0.4;
            delBtn.Click += async (_, _) =>
            {
                if (!await ConfirmAsync(AppUtils.Tr("ai_chat.confirm_del_title"), AppUtils.Tr("ai_chat.confirm_del_content")))
                {
                    return;
                }
                _sessions.Remove(session);
                if (session.Id == _currentId)
                {
                    if (_sessions.Count == 0)
                    {
                        CreateSession();
                    }
                    else
                    {
                        _currentId = _sessions.OrderByDescending(x => x.Ts).First().Id;
                    }
                }
                Save();
                Rebuild();
                FillHistoryList(list);
            };
            var rowGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
            rowGrid.Children.Add(titleTb);
            rowGrid.Children.Add(delBtn);
            Grid.SetColumn(delBtn, 1);
            var row = new Border
            {
                Background = session.Id == _currentId ? new SolidColorBrush(p.BandB) : Brushes.Transparent,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = rowGrid,
            };
            row.PointerReleased += (_, e) =>
            {
                if (_busy)
                {
                    return;
                }
                _currentId = session.Id;
                Save();
                Rebuild();
                HideDrawer();
                e.Handled = true;
            };
            list.Children.Add(row);
        }
    }

    // 日期分组名
    private static string GroupLabel(long ts)
    {
        var d = ts > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.Date : DateTime.Today;
        var today = DateTime.Today;
        if (d == today)
        {
            return AppUtils.Tr("ai_chat.group_today");
        }
        if (d == today.AddDays(-1))
        {
            return AppUtils.Tr("ai_chat.group_yesterday");
        }
        if (d >= today.AddDays(-30))
        {
            return AppUtils.Tr("ai_chat.group_30");
        }
        return AppUtils.Tr("ai_chat.group_older");
    }

    private async Task SendAsync()
    {
        var text = _input.Text?.Trim() ?? "";
        if (_busy || text.Length == 0)
        {
            return;
        }
        if (!AiService.Ready)
        {
            _history.Add(new ChatMsg("assistant", AppUtils.Tr("ai_chat.not_ready")));
            Rebuild();
            Save();
            return;
        }
        _history.Add(new ChatMsg("user", text));
        TrimHistory();
        Save();
        _input.Clear();
        await RunCompletionAsync();
    }

    // 重新生成末条回复
    private async Task RegenerateAsync()
    {
        if (_busy || _history.Count == 0 || _history[^1].Role != "assistant")
        {
            return;
        }
        _history.RemoveAt(_history.Count - 1);
        if (_history.Count == 0 || _history[^1].Role != "user")
        {
            Save();
            Rebuild();
            return;
        }
        Save();
        await RunCompletionAsync();
    }

    // 流式补全 live 渲染
    private async Task RunCompletionAsync()
    {
        _busy = true;
        ShowLiveRow();
        var prompt = _history[^1].Role == "user" ? _history[^1].Text : "";
        if (prompt.Length == 0)
        {
            _busy = false;
            SetSendBusy(false);
            Rebuild();
            return;
        }

        // 近 8 轮上下文
        var recent = _history.Take(Math.Max(0, _history.Count - 1)).TakeLast(ContextTurns)
            .Select(m => (m.Role, m.Text)).ToList();
        var ui = Dispatcher.UIThread;
        _streamCts = new CancellationTokenSource();
        var r = await AiService.StreamChatAsync(prompt,
            new AiStreamHandlers(
                OnReasoning: d => ui.Post(() => AppendLive(d, null)),
                OnContent: d => ui.Post(() => AppendLive(null, d))),
            history: recent,
            ct: _streamCts.Token);
        _streamCts.Dispose();
        _streamCts = null;
        string reply;
        if (!r.Ok && r.Error == "已停止")
        {
            // 停生留 partial
            var partial = r.Content.Trim();
            reply = partial.Length > 0 ? partial + AppUtils.Tr("ai_chat.stopped") : AppUtils.Tr("ai_chat.stopped");
        }
        else
        {
            reply = r.Ok ? r.Content : AppUtils.Tr("ai_chat.err") + r.Error;
        }
        _history.Add(new ChatMsg("assistant", reply, r.Reasoning, r.LatencyMs));
        TrimHistory();
        Save();
        StopLiveTimers();
        _busy = false;
        SetSendBusy(false);
        Rebuild();
        _input.Focus();
    }

    private void TrimHistory()
    {
        while (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }
    }
}
