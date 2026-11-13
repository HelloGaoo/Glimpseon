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

// NativeAnnouncementWidget
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

/// <summary>
/// 公告栏
/// 顶栏(标题 20px w700 + 计数胶囊 + ＋添加 + 两步确认清空) + 列表(点标记/正文 18px/时间 13px/
/// 24h 内最新条 accent 左边线+新徽标/悬停显编辑删除) + 内联输入(Enter 提交 Esc 取消)
/// 持久化 Announcements 服务
/// </summary>
public sealed class NativeAnnouncementWidget : WidgetCardBase
{
    private readonly TextBlock _title = new();
    private readonly TextBlock _countChip = new();
    private readonly Border _countBorder = new();
    private readonly Border _addBtn = new();
    private readonly Border _clearBtn = new();
    private readonly TextBlock _clearText = new();
    private readonly CardEmptyState _emptyView = new(
        FAIcons.IconGlyph("E789"),
        AppUtils.Tr("announcement.empty"),
        AppUtils.Tr("announcement.empty_hint"));
    private readonly StackPanel _list = new();
    private List<Announcement> _items = new();
    private string _lastSerialized = "";
    private bool _clearArmed;
    private readonly DispatcherTimer _clearTimer;
    private bool _suppressExternal;

    public NativeAnnouncementWidget(ComponentDefinition definition) : base(definition)
    {
        _clearTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        _clearTimer.Tick += (_, _) =>
        {
            _clearTimer.Stop();
            _clearArmed = false;
            _clearText.Text = AppUtils.Tr("announcement.clear");
            _clearBtn.Background = new SolidColorBrush(CardTheme.T("chip", IsDarkNow));
        };

        BuildLayout();
        _items = Announcements.Load();
        ActualThemeVariantChanged += (_, _) => Render();
        Announcements.Changed += OnExternalChanged;
        Render();
        Log.Debug($"[AN] 公告栏组件初始化(原生) id={ComponentId}");
    }

    private bool IsDarkNow => CardIsDark;

    private void BuildLayout()
    {
        _title.Text = AppUtils.Tr("announcement.title");
        _title.FontSize = WidgetType.Title;
        _title.FontWeight = WidgetWeight.Title;
        _title.VerticalAlignment = VerticalAlignment.Center;

        _countBorder.Child = _countChip;
        _countChip.FontSize = WidgetType.Label;
        _countChip.FontWeight = WidgetWeight.Title;
        _countBorder.Padding = new Thickness(WidgetSpace.Gap, 2);
        _countBorder.CornerRadius = new CornerRadius(WidgetSpace.RadiusPill);
        _countBorder.VerticalAlignment = VerticalAlignment.Center;

        _addBtn.Child = new TextBlock { Text = "+ " + AppUtils.Tr("announcement.add"), FontSize = WidgetType.Body, FontWeight = WidgetWeight.Title };
        _addBtn.Padding = new Thickness(WidgetSpace.Gap, WidgetSpace.GapXs);
        _addBtn.CornerRadius = new CornerRadius(WidgetSpace.RadiusInner);
        _addBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _addBtn.VerticalAlignment = VerticalAlignment.Center;
        _addBtn.PointerEntered += (_, _) =>
        {
            var a = NativeTheme.Accent();
            _addBtn.Background = new SolidColorBrush(Color.FromArgb(33, a.R, a.G, a.B));
            ((TextBlock)_addBtn.Child!).Foreground = new SolidColorBrush(a);
        };
        _addBtn.PointerExited += (_, _) =>
        {
            _addBtn.Background = new SolidColorBrush(CardTheme.T("chip", IsDarkNow));
            ((TextBlock)_addBtn.Child!).Foreground = new SolidColorBrush(CardTheme.T("sub", IsDarkNow));
        };
        _addBtn.PointerReleased += (_, _) => StartAdd();

        _clearBtn.Child = _clearText;
        _clearText.Text = AppUtils.Tr("announcement.clear");
        _clearText.FontSize = WidgetType.Body;
        _clearText.FontWeight = WidgetWeight.Title;
        _clearBtn.Padding = new Thickness(WidgetSpace.Gap, WidgetSpace.GapXs);
        _clearBtn.CornerRadius = new CornerRadius(WidgetSpace.RadiusInner);
        _clearBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _clearBtn.VerticalAlignment = VerticalAlignment.Center;
        _clearBtn.PointerReleased += (_, _) => OnClearClicked();

        var topGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto,Auto,Auto") };
        topGrid.ColumnSpacing = WidgetSpace.Gap;
        var anHistoryBtn = CardTheme.TagButton(AppUtils.Tr("announcement.history"), WidgetType.Label, IsDarkNow);
        anHistoryBtn.PointerReleased += (_, _) => ComponentExpander.Show(this, AppUtils.Tr("announcement.history"),
            DailyLogPanel.ForAnnouncements());
        Grid.SetColumn(_title, 0);
        Grid.SetColumn(_countBorder, 1);
        Grid.SetColumn(anHistoryBtn, 3);
        Grid.SetColumn(_addBtn, 4);
        Grid.SetColumn(_clearBtn, 5);
        topGrid.Children.Add(_title);
        topGrid.Children.Add(_countBorder);
        topGrid.Children.Add(anHistoryBtn);
        topGrid.Children.Add(_addBtn);
        topGrid.Children.Add(_clearBtn);

        var scroll = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var body = new Grid();
        body.Children.Add(scroll);
        body.Children.Add(_emptyView);

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            Margin = new Thickness(WidgetSpace.PadCompact, WidgetSpace.GapXs, WidgetSpace.PadCompact, WidgetSpace.PadCompact),
        };
        Grid.SetRow(topGrid, 0);
        Grid.SetRow(body, 1);
        root.Children.Add(topGrid);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;
    }

    private static string NowStr() => DateTime.Now.ToString("yyyy-MM-dd HH:mm");

    // 原版 fmtTime: 当年 MM-DD HH:mm 跨年完整
    private static string FmtTime(string? created)
    {
        if (string.IsNullOrEmpty(created))
        {
            return "";
        }
        var m = System.Text.RegularExpressions.Regex.Match(created, @"^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2})$");
        if (!m.Success)
        {
            return created;
        }
        var now = DateTime.Now;
        var hm = m.Groups[4].Value + ":" + m.Groups[5].Value;
        if (m.Groups[1].Value != now.Year.ToString())
        {
            return $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value} {hm}";
        }
        return $"{m.Groups[2].Value}-{m.Groups[3].Value} {hm}";
    }

    // 原版 isFresh: 24h 内
    private static bool IsFresh(string? created)
    {
        var m = System.Text.RegularExpressions.Regex.Match(created ?? "", @"^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2})$");
        if (!m.Success)
        {
            return false;
        }
        var d = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), 0);
        return DateTime.Now - d < TimeSpan.FromHours(24);
    }

    private void Commit()
    {
        _suppressExternal = true;
        _lastSerialized = System.Text.Json.JsonSerializer.Serialize(
            _items.Select(a => new { text = a.Text, created = a.Created }));
        Announcements.Save(_items);
        _suppressExternal = false;
    }

    private void OnExternalChanged()
    {
        if (_suppressExternal)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                Announcements.Load().Select(a => new { text = a.Text, created = a.Created }));
            if (json == _lastSerialized)
            {
                return;
            }
            _items = Announcements.Load();
            Render();
        });
    }

    private void OnClearClicked()
    {
        if (_items.Count == 0)
        {
            return;
        }
        if (!_clearArmed)
        {
            _clearArmed = true;
            _clearText.Text = AppUtils.Tr("announcement.confirm_clear");
            _clearBtn.Background = new SolidColorBrush(Color.FromArgb(230, 229, 72, 77));
            _clearText.Foreground = Brushes.White;
            _clearTimer.Stop();
            _clearTimer.Start();
            return;
        }
        _clearArmed = false;
        _clearTimer.Stop();
        _clearText.Text = AppUtils.Tr("announcement.clear");
        _items.Clear();
        Commit();
        Render();
    }

    private TextBox MakeInlineInput(string? initial, string placeholder, double fontSize)
    {
        var input = new TextBox
        {
            Text = initial ?? "",
            PlaceholderText = placeholder,
            FontSize = fontSize,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(CardTheme.T("chip", IsDarkNow)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8),
            AcceptsReturn = false,
        };
        input.Foreground = new SolidColorBrush(CardTheme.T("ink", IsDarkNow));
        return input;
    }

    private void StartAdd()
    {
        _addBtn.IsVisible = false;
        var underline = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = new SolidColorBrush(NativeTheme.Accent()),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = MakeInlineInput(null, AppUtils.Tr("announcement.item_ph"), WidgetType.Body),
        };
        var input = (TextBox)underline.Child!;
        var finished = false;
        var finish = (bool save) =>
        {
            if (finished)
            {
                return;
            }
            finished = true;
            _addBtn.IsVisible = true;
            var v = input.Text?.Trim() ?? "";
            if (save && v.Length > 0)
            {
                _items.Insert(0, new Announcement(v, NowStr()));
                Commit();
            }
            Render();
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                finish(true);
            }
            else if (e.Key == Key.Escape)
            {
                finish(false);
            }
        };
        input.LostFocus += (_, _) => finish(true);
        _list.Children.Insert(0, underline);
        input.Focus();
    }

    private void StartEdit(int index, Border? oldHost)
    {
        var it = _items[index];
        var finished = false;
        var underline = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = new SolidColorBrush(NativeTheme.Accent()),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = MakeInlineInput(it.Text, AppUtils.Tr("announcement.item_ph"), WidgetType.Body),
        };
        var input = (TextBox)underline.Child!;
        var finish = (bool save) =>
        {
            if (finished)
            {
                return;
            }
            finished = true;
            var v = input.Text?.Trim() ?? "";
            if (save && v.Length > 0 && v != it.Text)
            {
                _items[index] = it with { Text = v };
                Commit();
            }
            Render();
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                finish(true);
            }
            else if (e.Key == Key.Escape)
            {
                finish(false);
            }
        };
        input.LostFocus += (_, _) => finish(true);
        if (oldHost is not null && _list.Children.Contains(oldHost))
        {
            var pos = _list.Children.IndexOf(oldHost);
            _list.Children[pos] = underline;
        }
        else
        {
            _list.Children.Insert(0, underline);
        }
        input.Focus();
        input.SelectAll();
    }

    private void Render()
    {
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        var accent22 = Color.FromArgb(33, accent.R, accent.G, accent.B);
        _title.Foreground = new SolidColorBrush(CardTheme.T("ink", dark));
        _countChip.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _countBorder.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        _countChip.Text = _items.Count > 0 ? _items.Count.ToString() : "";
        _countBorder.IsVisible = _items.Count > 0;
        _addBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        ((TextBlock)_addBtn.Child!).Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        if (!_clearArmed)
        {
            _clearBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
            _clearText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        }
        _clearBtn.IsVisible = _items.Count > 0;
        _emptyView.Apply(WidgetSurfacePalette.Resolve(dark));
        _emptyView.IsVisible = _items.Count == 0;

        _list.Children.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var idx = i;
            var fresh = i == 0 && IsFresh(it.Created);

            var text = new TextBlock
            {
                Text = it.Text,
                FontSize = WidgetType.Body,
                LineHeight = WidgetType.LineHeight(WidgetType.Body),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = WidgetType.LineHeight(WidgetType.Body) * 3,
                ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            text.Foreground = new SolidColorBrush(CardTheme.T("ink", dark));
            var textHost = new Border { Child = text };
            text.DoubleTapped += (_, _) => StartEdit(idx, textHost);

            var time = new TextBlock
            {
                Text = FmtTime(it.Created),
                FontSize = WidgetType.Label,
                VerticalAlignment = VerticalAlignment.Center,
            };
            time.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));

            var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.Gap, Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0) };
            meta.Children.Add(time);
            if (fresh)
            {
                meta.Children.Add(new Border
                {
                    Background = new SolidColorBrush(accent22),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = AppUtils.Tr("announcement.new"),
                        FontSize = WidgetType.Micro,
                        FontWeight = WidgetWeight.Title,
                        Foreground = new SolidColorBrush(accent),
                    },
                });
            }
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 0, 0), Opacity = WidgetInk.DimOpacity };
            var editBtn = CardTheme.IconButton("✎", 24, false, () => IsDarkNow);
            editBtn.PointerReleased += (_, _) => StartEdit(idx, textHost);
            var delBtn = CardTheme.IconButton("×", 24, false, () => IsDarkNow);
            delBtn.PointerReleased += (_, _) =>
            {
                _items.RemoveAt(idx);
                Commit();
                Render();
            };
            // 悬停整卡时按钮全显
            btns.Children.Add(editBtn);
            btns.Children.Add(delBtn);
            meta.Children.Add(btns);

            var body = new StackPanel();
            body.Children.Add(textHost);
            body.Children.Add(meta);

            var dot = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(accent),
                Margin = new Thickness(0, WidgetSpace.Gap, 0, 0),
            };

            var itemGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*") };
            itemGrid.ColumnSpacing = WidgetSpace.Gap;
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(body, 1);
            itemGrid.Children.Add(dot);
            itemGrid.Children.Add(body);

            // 平面列表: 透明底 最新条 accent 左缘 条目间 1px 分隔线
            var itemBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = fresh ? CardAccent.Brush() : new SolidColorBrush(CardTheme.T("line", dark)),
                BorderThickness = fresh ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(fresh ? WidgetSpace.Gap : 0, WidgetSpace.Gap, 0, WidgetSpace.Gap),
                Margin = new Thickness(0, 0, 0, 0),
                Child = itemGrid,
            };
            itemBorder.PointerEntered += (_, _) => btns.Opacity = 1;
            itemBorder.PointerExited += (_, _) => btns.Opacity = WidgetInk.DimOpacity;
            _list.Children.Add(itemBorder);
        }
        InvalidateVisual();
    }
}
