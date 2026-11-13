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

// TimerCountdownWidget
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

/// <summary>倒计时 / 秒表: 分段标签 + 大读数 + 直接输入 + 计次</summary>
public sealed class TimerCountdownWidget : WidgetCardBase
{
    private static readonly int[] PresetMinutes = { 1, 3, 5, 10, 15, 30, 60 };
    private const int DefaultDuration = 5 * 60;
    private const int MaxDuration = 23 * 3600 + 59 * 60 + 59;

    private readonly TimerPill _tabCountdown;
    private readonly TimerPill _tabStopwatch;
    private readonly Border _modeBar;
    private readonly Grid _pageHost = new();

    // 倒计时
    private readonly TextBlock _cdTime = new()
    {
        Text = "05:00",
        FontSize = 58,
        FontWeight = WidgetWeight.Display,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Cursor = new Cursor(StandardCursorType.Hand),
        LetterSpacing = WidgetType.DisplaySpacing(58),
    };
    private readonly TextBox _cdEdit = new()
    {
        FontSize = 58,
        FontWeight = WidgetWeight.Display,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(8, 0, 8, 0),
        MinWidth = 120,
        MaxLength = 8,
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _cdHint = new()
    {
        FontSize = WidgetType.Micro,
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _cdEnds = new()
    {
        FontSize = WidgetType.Label,
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Grid _cdMeta = new()
    {
        MinHeight = 14,
        Margin = new Thickness(0, 2, 0, 0),
    };
    private readonly CapsuleBar _cdProgress = new()
    {
        Height = 6,
        Segments = 20,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Margin = new Thickness(0, WidgetSpace.Gap, 0, 0),
        IsVisible = false,
    };
    private readonly Grid _presetGrid = new()
    {
        VerticalAlignment = VerticalAlignment.Bottom,
    };
    private readonly List<TimerPill> _presetPills = new();
    private readonly TimerPill _customPill;
    private readonly TimerPill _cdMain;
    private readonly TimerPill _cdReset;
    private readonly StackPanel _cdButtons;
    private readonly Grid _cdReadout;
    private readonly Grid _cdPage;
    private readonly Grid _root;
    private readonly StackPanel _swButtons;
    private bool _portrait;
    private int _presetColumns;

    private int _cdDuration = DefaultDuration;
    private int _cdRemaining = DefaultDuration;
    private DateTime _cdEndAt;
    private bool _cdRunning;
    private bool _cdPaused;
    private bool _cdDone;
    private bool _cdEditing;
    private int _lastCdSecond = -1;

    // 秒表
    private readonly TextBlock _swTime = new()
    {
        Text = "00:00",
        FontSize = 54,
        FontWeight = WidgetWeight.Display,
        VerticalAlignment = VerticalAlignment.Bottom,
        LetterSpacing = WidgetType.DisplaySpacing(54),
    };
    private readonly TextBlock _swTenths = new()
    {
        Text = ".0",
        FontSize = 22,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(2, 0, 0, WidgetSpace.GapXs),
    };
    private readonly StackPanel _swReadout = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _lapStack = new() { Spacing = 0 };
    private readonly TextBlock _lapEmpty = new()
    {
        FontSize = WidgetType.Micro,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };
    private readonly Grid _lapHost = new();
    private readonly ScrollViewer _lapScroll;
    private readonly List<TimeSpan> _laps = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly TimerPill _swMain;
    private readonly TimerPill _swSecond;
    private readonly Grid _swPage;

    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _countdownMode = true;
    private bool _ready;
    private TopLevel? _topLevel;

    public TimerCountdownWidget(ComponentDefinition definition) : base(definition)
    {
        _tabCountdown = new TimerPill(Tr("timer_countdown.countdown"), TimerPill.PillKind.Tab, 26, WidgetType.Label, 13);
        _tabStopwatch = new TimerPill(Tr("timer_countdown.stopwatch"), TimerPill.PillKind.Tab, 26, WidgetType.Label, 13);
        _tabCountdown.DarkProvider = () => CardIsDark;
        _tabStopwatch.DarkProvider = () => CardIsDark;
        _tabCountdown.Clicked += () => SwitchMode(true);
        _tabStopwatch.Clicked += () => SwitchMode(false);

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        tabs.Children.Add(_tabCountdown);
        tabs.Children.Add(_tabStopwatch);
        _modeBar = new Border
        {
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(13),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = tabs,
        };

        // ── 倒计时页 ──
        _cdTime.PointerPressed += (_, e) => e.Handled = true;
        _cdTime.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            BeginCountdownEdit();
        };
        _cdEdit.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
            RoutingStrategies.Bubble);
        _cdEdit.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitCountdownEdit(true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CommitCountdownEdit(false);
            }
        };
        _cdEdit.LostFocus += (_, _) =>
        {
            if (_cdEditing)
            {
                CommitCountdownEdit(true, leaving: true);
            }
        };
        _cdReadout = new Grid
        {
            MinHeight = 64,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _cdReadout.Children.Add(_cdTime);
        _cdReadout.Children.Add(_cdEdit);
        _cdMeta.Children.Add(_cdEnds);
        _cdMeta.Children.Add(_cdHint);

        foreach (var minutes in PresetMinutes)
        {
            var pill = new TimerPill(PresetLabel(minutes), TimerPill.PillKind.Secondary, 25, WidgetType.Label, 12)
            {
                DarkProvider = () => CardIsDark,
            };
            var value = minutes * 60;
            pill.Clicked += () =>
            {
                if (!_cdRunning && !_cdPaused && !_cdDone)
                {
                    SetCountdownDuration(value);
                }
            };
            _presetPills.Add(pill);
            _presetGrid.Children.Add(pill);
        }

        _customPill = new TimerPill(Tr("timer_countdown.custom"), TimerPill.PillKind.Secondary, 25, WidgetType.Label, 12)
        {
            DarkProvider = () => CardIsDark,
        };
        _customPill.Clicked += () =>
        {
            if (!_cdRunning && !_cdPaused && !_cdDone)
            {
                BeginCountdownEdit();
            }
        };
        _presetGrid.Children.Add(_customPill);
        ConfigurePresetGrid(false);

        _cdMain = new TimerPill(Tr("timer_countdown.start"), TimerPill.PillKind.Primary, 34, WidgetType.Title, 17)
        {
            DarkProvider = () => CardIsDark,
        };
        _cdMain.Clicked += OnCountdownMain;
        _cdReset = new TimerPill(Tr("timer_countdown.reset"), TimerPill.PillKind.Secondary, 34, WidgetType.Label, 17)
        {
            DarkProvider = () => CardIsDark,
            IsVisible = false,
        };
        _cdReset.Clicked += OnCountdownReset;
        _cdButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapSm,
            Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
        };
        _cdButtons.Children.Add(_cdMain);
        _cdButtons.Children.Add(_cdReset);

        _cdPage = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto,Auto,Auto,Auto"),
            RowSpacing = 2,
            ColumnSpacing = 12,
        };
        _cdPage.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        _cdPage.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));
        Grid.SetRow(_cdReadout, 0);
        Grid.SetRow(_cdProgress, 1);
        Grid.SetRow(_cdMeta, 2);
        Grid.SetRow(_cdButtons, 3);
        Grid.SetRow(_presetGrid, 0);
        Grid.SetColumn(_presetGrid, 1);
        Grid.SetRowSpan(_presetGrid, 4);
        _cdPage.Children.Add(_cdReadout);
        _cdPage.Children.Add(_cdProgress);
        _cdPage.Children.Add(_cdMeta);
        _cdPage.Children.Add(_presetGrid);
        _cdPage.Children.Add(_cdButtons);

        // ── 秒表页 ──
        _swReadout.Children.Add(_swTime);
        _swReadout.Children.Add(_swTenths);
        _lapScroll = new ScrollViewer
        {
            Content = _lapStack,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
        };
        _lapHost.Children.Add(_lapScroll);
        _lapHost.Children.Add(_lapEmpty);
        _swMain = new TimerPill(Tr("timer_countdown.start"), TimerPill.PillKind.Primary, 34, WidgetType.Title, 17)
        {
            DarkProvider = () => CardIsDark,
        };
        _swMain.Clicked += OnStopwatchMain;
        _swSecond = new TimerPill(Tr("timer_countdown.lap"), TimerPill.PillKind.Secondary, 34, WidgetType.Label, 17)
        {
            DarkProvider = () => CardIsDark,
            IsVisible = false,
        };
        _swSecond.Clicked += OnStopwatchSecond;
        _swButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapSm,
            Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
        };
        _swButtons.Children.Add(_swMain);
        _swButtons.Children.Add(_swSecond);
        _swPage = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto,0"),
            RowSpacing = 2,
            ColumnSpacing = 12,
        };
        _swPage.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        _swPage.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));
        Grid.SetRow(_swReadout, 0);
        Grid.SetColumn(_swReadout, 0);
        Grid.SetRow(_swButtons, 1);
        Grid.SetColumn(_swButtons, 0);
        Grid.SetRow(_lapHost, 0);
        Grid.SetColumn(_lapHost, 1);
        Grid.SetRowSpan(_lapHost, 2);
        _swPage.Children.Add(_swReadout);
        _swPage.Children.Add(_lapHost);
        _swPage.Children.Add(_swButtons);

        _pageHost.Children.Add(_cdPage);
        _pageHost.Children.Add(_swPage);

        _root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapSm,
        };
        Grid.SetRow(_modeBar, 0);
        Grid.SetRow(_pageHost, 1);
        _root.Children.Add(_modeBar);
        _root.Children.Add(_pageHost);
        Card.Child = _root;
        Content = Card;

        _ticker.Tick += (_, _) => Tick();
        SizeChanged += (_, _) => Relayout();
        Config.ThemeColor.ValueChanged += _ =>
        {
            if (_ready)
            {
                ApplyCardStyle();
            }
        };

        _ready = true;
        ApplyCardStyle();
        UpdateCountdownChrome();
        UpdateStopwatchDisplay();
        UpdateStopwatchChrome();
        RebuildLaps();
        SwitchMode(true);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureTicker();
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _ticker.Stop();
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
            _topLevel = null;
        }
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_cdEditing)
        {
            return;
        }
        if (IsWithin(_cdEdit, e.Source) || IsWithin(_cdMain, e.Source) || IsWithin(_cdReset, e.Source))
        {
            return;
        }
        CommitCountdownEdit(true);
    }

    private static bool IsWithin(Visual ancestor, object? source)
    {
        var v = source as Visual;
        while (v is not null)
        {
            if (ReferenceEquals(v, ancestor))
            {
                return true;
            }
            v = v.GetVisualParent();
        }
        return false;
    }

    private static string PresetLabel(int minutes) => minutes >= 60
        ? Tr("timer_countdown.preset_hour")
        : Tr("timer_countdown.preset_min", ("n", minutes));

    private void SwitchMode(bool countdown)
    {
        if (_cdEditing)
        {
            CommitCountdownEdit(false);
        }
        _countdownMode = countdown;
        _cdPage.IsVisible = countdown;
        _swPage.IsVisible = !countdown;
        UpdateTabs();
        Relayout();
    }

    private void UpdateTabs()
    {
        _tabCountdown.Active = _countdownMode;
        _tabStopwatch.Active = !_countdownMode;
        _tabCountdown.ApplyTheme();
        _tabStopwatch.ApplyTheme();
    }

    private void OnCountdownMain()
    {
        if (_cdEditing)
        {
            CommitCountdownEdit(true);
            return;
        }
        if (_cdDone)
        {
            ResetCountdown();
            return;
        }
        if (_cdRunning)
        {
            PauseCountdown();
            return;
        }
        if (_cdPaused)
        {
            _cdRunning = true;
            _cdPaused = false;
            _cdEndAt = DateTime.Now.AddSeconds(_cdRemaining);
        }
        else
        {
            _cdRemaining = _cdDuration;
            _cdDone = false;
            _cdRunning = true;
            _cdPaused = false;
            _cdEndAt = DateTime.Now.AddSeconds(_cdRemaining);
        }
        _lastCdSecond = -1;
        _cdProgress.Value = _cdRemaining / (double)Math.Max(1, _cdDuration);
        EnsureTicker();
        UpdateCountdownDisplay();
        UpdateCountdownChrome();
    }

    private void PauseCountdown()
    {
        _cdRemaining = CountdownLeft();
        _cdRunning = false;
        _cdPaused = true;
        _cdProgress.Value = _cdRemaining / (double)Math.Max(1, _cdDuration);
        if (!_stopwatch.IsRunning)
        {
            _ticker.Stop();
        }
        UpdateCountdownChrome();
    }

    private void OnCountdownReset()
    {
        if (_cdEditing)
        {
            CommitCountdownEdit(false);
            return;
        }
        ResetCountdown();
    }

    private void ResetCountdown()
    {
        _cdRunning = false;
        _cdPaused = false;
        _cdDone = false;
        _cdRemaining = _cdDuration;
        _lastCdSecond = -1;
        _cdEditing = false;
        _cdEdit.IsVisible = false;
        _cdTime.IsVisible = true;
        _cdHint.IsVisible = false;
        _cdProgress.Value = _cdRemaining / (double)Math.Max(1, _cdDuration);
        if (!_stopwatch.IsRunning)
        {
            _ticker.Stop();
        }
        UpdateCountdownDisplay();
        UpdateCountdownChrome();
    }

    private void SetCountdownDuration(int seconds)
    {
        _cdDuration = Math.Clamp(seconds, 1, MaxDuration);
        _cdRemaining = _cdDuration;
        _cdDone = false;
        _cdRunning = false;
        _cdPaused = false;
        _lastCdSecond = -1;
        _cdProgress.Value = 1;
        UpdateCountdownDisplay();
        UpdateCountdownChrome();
    }

    private int CountdownLeft() => _cdRunning
        ? Math.Max(0, (int)Math.Ceiling((_cdEndAt - DateTime.Now).TotalSeconds))
        : _cdRemaining;

    private void Tick()
    {
        if (_cdRunning)
        {
            var left = CountdownLeft();
            if (left <= 0)
            {
                CompleteCountdown();
            }
            else
            {
                _cdRemaining = left;
                if (left != _lastCdSecond)
                {
                    _lastCdSecond = left;
                    UpdateCountdownDisplay();
                }
                _cdProgress.Value = left / (double)Math.Max(1, _cdDuration);
            }
        }
        if (_stopwatch.IsRunning)
        {
            UpdateStopwatchDisplay();
        }
        if (!_cdRunning && !_stopwatch.IsRunning)
        {
            _ticker.Stop();
        }
    }

    private void CompleteCountdown()
    {
        _cdRunning = false;
        _cdPaused = false;
        _cdDone = true;
        _cdRemaining = 0;
        _cdProgress.Value = 0;
        _lastCdSecond = 0;
        Log.Info("[TIMER] 倒计时结束");
        if (!_stopwatch.IsRunning)
        {
            _ticker.Stop();
        }
        UpdateCountdownDisplay();
        UpdateCountdownChrome();
    }

    private void UpdateCountdownDisplay()
    {
        if (_cdEditing)
        {
            return;
        }
        _cdTime.Text = _cdDone ? Tr("timer_countdown.time_up") : FormatClock(_cdRemaining);
        Relayout();
    }

    private void UpdateCountdownChrome()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var active = _cdRunning || _cdPaused;
        _cdMain.Text = _cdEditing
            ? Tr("timer_countdown.confirm")
            : _cdDone
                ? Tr("timer_countdown.ok")
                : _cdRunning
                    ? Tr("timer_countdown.pause")
                    : _cdPaused
                        ? Tr("timer_countdown.resume")
                        : Tr("timer_countdown.start");
        _cdReset.Text = _cdEditing ? Tr("timer_countdown.cancel") : Tr("timer_countdown.reset");
        _cdReset.IsVisible = active || _cdEditing;
        _cdProgress.IsVisible = active;
        _cdEnds.IsVisible = !_cdDone;
        _presetGrid.Opacity = active || _cdEditing ? 0.5 : 1;
        _cdTime.Foreground = _cdDone ? CardAccent.Brush() : CardInk.Fg(p);
        _cdTime.Cursor = new Cursor(active || _cdDone ? StandardCursorType.Arrow : StandardCursorType.Hand);
        if (_cdEditing)
        {
            _cdEnds.Text = Tr("timer_countdown.edit_confirm_hint");
        }
        else if (_cdRunning)
        {
            _cdEnds.Text = $"{_cdEndAt:HH:mm} {Tr("timer_countdown.ends_at")}";
        }
        else if (_cdPaused)
        {
            _cdEnds.Text = Tr("timer_countdown.paused");
        }
        else
        {
            _cdEnds.Text = Tr("timer_countdown.edit_time");
        }
        foreach (var pill in _presetPills)
        {
            var minutes = PresetMinutes[_presetPills.IndexOf(pill)];
            pill.Active = minutes * 60 == _cdDuration;
            pill.ApplyTheme();
        }
        _customPill.ApplyTheme();
        if (!_cdEditing)
        {
            _cdTime.Text = _cdDone ? Tr("timer_countdown.time_up") : FormatClock(_cdRemaining);
        }
        Relayout();
    }

    private void BeginCountdownEdit()
    {
        if (_cdRunning || _cdPaused || _cdDone || _cdEditing)
        {
            return;
        }
        _cdEditing = true;
        _cdHint.IsVisible = false;
        _cdEdit.Text = FormatClock(_cdDuration);
        _cdEdit.IsVisible = true;
        _cdTime.IsVisible = false;
        UpdateCountdownChrome();
        _cdEdit.Focus();
        _cdEdit.SelectAll();
    }

    private void CommitCountdownEdit(bool apply, bool leaving = false)
    {
        if (!_cdEditing)
        {
            return;
        }
        if (!apply)
        {
            EndCountdownEdit();
            return;
        }
        var value = ParseDuration(_cdEdit.Text, out var tooLong);
        if (value is null)
        {
            if (leaving)
            {
                EndCountdownEdit();
                return;
            }
            _cdHint.Text = tooLong
                ? Tr("timer_countdown.max_hint")
                : Tr("timer_countdown.edit_hint");
            _cdHint.IsVisible = true;
            _cdEdit.Focus();
            _cdEdit.SelectAll();
            return;
        }
        EndCountdownEdit();
        SetCountdownDuration(value.Value);
    }

    private void EndCountdownEdit()
    {
        _cdEditing = false;
        _cdEdit.IsVisible = false;
        _cdTime.IsVisible = true;
        _cdHint.IsVisible = false;
        UpdateCountdownChrome();
    }

    private static int? ParseDuration(string? text, out bool tooLong)
    {
        tooLong = false;
        var value = (text ?? "").Trim().Replace('：', ':').Replace('.', ':');
        if (value.Length == 0)
        {
            return null;
        }
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3 || parts.Any(p => p.Length == 0 || !p.All(char.IsDigit)))
        {
            return null;
        }
        var nums = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], out nums[i]))
            {
                return null;
            }
        }
        if (parts.Length > 1 && nums.Skip(1).Any(n => n >= 60))
        {
            return null;
        }
        var seconds = parts.Length switch
        {
            1 => nums[0] * 60,
            2 => nums[0] * 60 + nums[1],
            _ => nums[0] * 3600 + nums[1] * 60 + nums[2],
        };
        if (seconds <= 0)
        {
            return null;
        }
        if (seconds > MaxDuration)
        {
            tooLong = true;
            return null;
        }
        return (int)seconds;
    }

    private void OnStopwatchMain()
    {
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
        }
        else
        {
            _stopwatch.Start();
        }
        EnsureTicker();
        UpdateStopwatchDisplay();
        UpdateStopwatchChrome();
    }

    private void OnStopwatchSecond()
    {
        if (_stopwatch.IsRunning)
        {
            _laps.Add(_stopwatch.Elapsed);
            RebuildLaps();
        }
        else
        {
            ResetStopwatch();
        }
    }

    private void ResetStopwatch()
    {
        _stopwatch.Reset();
        _laps.Clear();
        if (!_cdRunning)
        {
            _ticker.Stop();
        }
        UpdateStopwatchDisplay();
        UpdateStopwatchChrome();
        RebuildLaps();
    }

    private void UpdateStopwatchDisplay()
    {
        var elapsed = _stopwatch.Elapsed;
        var (main, tenths) = FormatStopwatch(elapsed);
        _swTime.Text = main;
        _swTenths.Text = tenths;
        Relayout();
    }

    private void UpdateStopwatchChrome()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _swMain.Text = _stopwatch.IsRunning
            ? Tr("timer_countdown.pause")
            : _stopwatch.Elapsed > TimeSpan.Zero
                ? Tr("timer_countdown.resume")
                : Tr("timer_countdown.start");
        _swSecond.Text = _stopwatch.IsRunning
            ? Tr("timer_countdown.lap")
            : Tr("timer_countdown.reset");
        _swSecond.IsVisible = _stopwatch.IsRunning || _stopwatch.Elapsed > TimeSpan.Zero;
        _swTime.Foreground = CardInk.Fg(p);
        _swTenths.Foreground = CardInk.Muted(p);
        _lapEmpty.Foreground = CardInk.Faint(p);
        // 按钮显隐会改变主按钮的剩余宽度，必须立即重排，避免先拉满再缩回
        Relayout();
    }

    private void RebuildLaps()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _lapStack.Children.Clear();
        _lapEmpty.IsVisible = _laps.Count == 0;
        if (_laps.Count == 0)
        {
            return;
        }

        for (var i = _laps.Count - 1; i >= 0; i--)
        {
            _lapStack.Children.Add(MakeLapRow(
                Tr("timer_countdown.lap_n", ("n", i + 1)),
                FormatStopwatch(_laps[i]),
                current: i == _laps.Count - 1,
                color: i == _laps.Count - 1 ? CardAccent.Brush() : null, p, out _));
        }
    }

    private Border MakeLapRow(string label, (string Main, string Tenths) time, bool current,
        IBrush? color, WidgetSurfacePalette p, out TextBlock? timeLabel)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = WidgetType.Label,
            FontWeight = current ? WidgetWeight.Emphasis : WidgetWeight.Body,
            Foreground = current ? CardInk.Fg(p) : CardInk.Muted(p),
            VerticalAlignment = VerticalAlignment.Center,
        };
        timeLabel = new TextBlock
        {
            Text = time.Main + time.Tenths,
            FontSize = WidgetType.Label,
            FontWeight = current ? WidgetWeight.Emphasis : WidgetWeight.Body,
            Foreground = color ?? (current ? CardInk.Fg(p) : CardInk.Muted(p)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto"),
        };
        Grid.SetColumn(timeLabel, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(timeLabel);
        return new Border
        {
            Padding = new Thickness(0, 3),
            BorderBrush = new SolidColorBrush(p.Line, 0.32),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row,
        };
    }

    private void EnsureTicker()
    {
        if (_cdRunning || _stopwatch.IsRunning)
        {
            if (!_ticker.IsEnabled)
            {
                _ticker.Start();
            }
        }
    }

    private static string FormatClock(int seconds)
    {
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }

    private static (string Main, string Tenths) FormatStopwatch(TimeSpan elapsed)
    {
        var totalSeconds = (long)Math.Floor(elapsed.TotalSeconds);
        var h = totalSeconds / 3600;
        var m = totalSeconds % 3600 / 60;
        var s = totalSeconds % 60;
        var main = h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
        return (main, $".{elapsed.Milliseconds / 100}");
    }

    private void Relayout()
    {
        if (!_ready || Bounds.Width <= 1)
        {
            return;
        }
        var portrait = Bounds.Height >= Bounds.Width * 1.15;
        var padX = portrait ? 14 : 16;
        var width = Math.Max(80, Bounds.Width - padX * 2);
        var rightWidth = portrait ? 0 : Math.Clamp(width * 0.37, 132, 156);
        var leftWidth = Math.Max(96, width - (portrait ? 0 : rightWidth + 12));
        var lapWidth = portrait ? 0 : Math.Clamp(width * 0.45, 148, 190);
        var swLeftWidth = Math.Max(96, width - (portrait ? 0 : lapWidth + 12));
        if (_portrait != portrait)
        {
            _portrait = portrait;
            _root.Margin = new Thickness(padX, 14, padX, 14);
            _cdPage.RowSpacing = portrait ? 4 : 2;
            _swPage.RowSpacing = portrait ? 4 : 2;
            ConfigurePresetGrid(portrait);
            PlaceCountdownPage(portrait);
            PlaceStopwatchPage(portrait);
        }
        _cdPage.ColumnDefinitions[1].Width = new GridLength(portrait ? 0 : rightWidth);
        _cdPage.ColumnSpacing = portrait ? 0 : 12;
        _cdPage.RowDefinitions[4].Height = portrait ? GridLength.Auto : new GridLength(0);
        _swPage.ColumnDefinitions[1].Width = new GridLength(lapWidth);
        _swPage.ColumnSpacing = portrait ? 0 : 12;
        _swPage.RowDefinitions[0].Height = portrait ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _swPage.RowDefinitions[1].Height = portrait ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        _swPage.RowDefinitions[2].Height = portrait ? GridLength.Auto : new GridLength(0);

        var cdPrefer = _cdDone
            ? 42
            : Math.Clamp(leftWidth * (portrait ? 0.36 : 0.3), 42, 58);
        _cdTime.FontSize = CardText.Fit(_cdTime.Text, _cdTime.FontFamily, WidgetWeight.Display,
            leftWidth, cdPrefer, 26, 70);
        _cdEdit.FontSize = _cdTime.FontSize;
        _cdEdit.Width = leftWidth;
        var swPrefer = portrait ? Math.Clamp(width * 0.32, 38, 54) : 58;
        _swTime.FontSize = CardText.Fit(_swTime.Text, _swTime.FontFamily, WidgetWeight.Display,
            swLeftWidth - 30, swPrefer, 26, 62);
        _swTenths.FontSize = Math.Max(14, _swTime.FontSize * 0.42);

        SetPillHeight(_tabCountdown, portrait ? 30 : 26);
        SetPillHeight(_tabStopwatch, portrait ? 30 : 26);
        SetPillHeight(_cdMain, portrait ? 40 : 34);
        SetPillHeight(_cdReset, portrait ? 40 : 34);
        SetPillHeight(_swMain, portrait ? 40 : 34);
        SetPillHeight(_swSecond, portrait ? 40 : 34);
        foreach (var pill in _presetPills)
        {
            SetPillHeight(pill, portrait ? 34 : 30);
        }
        SetPillHeight(_customPill, portrait ? 34 : 30);

        _modeBar.HorizontalAlignment = portrait ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        var tabWidth = (width - 6) / 2;
        _tabCountdown.Width = portrait ? tabWidth : double.NaN;
        _tabStopwatch.Width = portrait ? tabWidth : double.NaN;
        FitButtonWidths(_cdMain, _cdReset, leftWidth);
        FitButtonWidths(_swMain, _swSecond, swLeftWidth);
        _presetGrid.IsVisible = true;
    }

    private void PlaceStopwatchPage(bool portrait)
    {
        Grid.SetColumn(_swReadout, 0);
        Grid.SetRow(_swReadout, 0);
        Grid.SetColumn(_swButtons, 0);
        Grid.SetRow(_swButtons, portrait ? 2 : 1);
        Grid.SetColumn(_lapHost, 0);
        Grid.SetRow(_lapHost, 1);
        foreach (var control in new Control[] { _swReadout, _swButtons, _lapHost })
        {
            Grid.SetColumnSpan(control, portrait ? 2 : 1);
            Grid.SetRowSpan(control, 1);
        }
        if (portrait)
        {
            return;
        }
        Grid.SetColumn(_lapHost, 1);
        Grid.SetRow(_lapHost, 0);
        Grid.SetRowSpan(_lapHost, 2);
    }

    private void PlaceCountdownPage(bool portrait)
    {
        Grid.SetColumn(_cdReadout, 0);
        Grid.SetRow(_cdReadout, 0);
        Grid.SetColumn(_cdProgress, 0);
        Grid.SetRow(_cdProgress, 1);
        Grid.SetColumn(_cdMeta, 0);
        Grid.SetRow(_cdMeta, 2);
        Grid.SetColumn(_cdButtons, 0);
        Grid.SetRow(_cdButtons, portrait ? 4 : 3);
        Grid.SetColumn(_presetGrid, portrait ? 0 : 1);
        Grid.SetRow(_presetGrid, portrait ? 3 : 0);
        foreach (var control in new Control[] { _cdReadout, _cdProgress, _cdMeta, _cdButtons })
        {
            Grid.SetColumnSpan(control, portrait ? 2 : 1);
            Grid.SetRowSpan(control, 1);
        }
        Grid.SetColumnSpan(_presetGrid, portrait ? 2 : 1);
        Grid.SetRowSpan(_presetGrid, portrait ? 1 : 4);
    }

    private static void SetPillHeight(TimerPill pill, double height)
    {
        if (Math.Abs(pill.Height - height) < 0.1)
        {
            return;
        }
        pill.Height = height;
        pill.CornerRadius = new CornerRadius(height / 2);
    }

    private static void FitButtonWidths(TimerPill main, TimerPill second, double width)
    {
        if (!second.IsVisible)
        {
            main.Width = width;
            second.Width = double.NaN;
            return;
        }
        main.Width = (width - 6) / 2;
        second.Width = width - main.Width - 6;
    }

    private void ConfigurePresetGrid(bool portrait)
    {
        const int columns = 2;
        const int rows = 4;
        if (_presetColumns == columns && _presetGrid.RowDefinitions.Count == rows)
        {
            _presetGrid.ColumnSpacing = portrait ? 6 : 4;
            _presetGrid.RowSpacing = portrait ? 6 : 4;
            return;
        }
        _presetColumns = columns;
        _presetGrid.ColumnDefinitions.Clear();
        _presetGrid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++)
        {
            _presetGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }
        for (var i = 0; i < rows; i++)
        {
            _presetGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        _presetGrid.ColumnSpacing = portrait ? 6 : 4;
        _presetGrid.RowSpacing = portrait ? 6 : 4;
        _presetGrid.VerticalAlignment = VerticalAlignment.Center;
        _customPill.IsVisible = true;
        var index = 0;
        foreach (var pill in _presetPills)
        {
            pill.Margin = default;
            Grid.SetColumn(pill, index % columns);
            Grid.SetRow(pill, index / columns);
            index++;
        }
        _customPill.Margin = default;
        Grid.SetColumn(_customPill, index % columns);
        Grid.SetRow(_customPill, index / columns);
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        if (!_ready)
        {
            return;
        }
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _modeBar.Background = new SolidColorBrush(p.Text, 0.045);
        _cdTime.Foreground = _cdDone ? CardAccent.Brush() : CardInk.Fg(p);
        _cdEdit.Foreground = CardInk.Fg(p);
        _cdEdit.Background = new SolidColorBrush(p.Text, 0.06);
        _cdEdit.BorderBrush = new SolidColorBrush(p.Text, 0.14);
        _cdEdit.SelectionBrush = CardAccent.Brush(0.28);
        _cdEdit.SelectionForegroundBrush = CardInk.Fg(p);
        _cdEdit.CaretBrush = CardAccent.Brush();
        _cdHint.Foreground = CardInk.Danger();
        _cdEnds.Foreground = CardInk.Muted(p);
        _lapEmpty.Foreground = CardInk.Faint(p);
        _cdProgress.DarkProvider = () => CardIsDark;
        _cdProgress.InvalidateVisual();
        foreach (var pill in _presetPills)
        {
            pill.ApplyTheme();
        }
        _customPill.ApplyTheme();
        _tabCountdown.ApplyTheme();
        _tabStopwatch.ApplyTheme();
        _cdMain.ApplyTheme();
        _cdReset.ApplyTheme();
        _swMain.ApplyTheme();
        _swSecond.ApplyTheme();
        _swTime.Foreground = CardInk.Fg(p);
        _swTenths.Foreground = CardInk.Muted(p);
        UpdateTabs();
        RebuildLaps();
        Relayout();
    }

    private static string Tr(string key) => AppUtils.Tr(key);
    private static string Tr(string key, params (string Name, object? Value)[] args) => AppUtils.Tr(key, args);
}
