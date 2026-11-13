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

// NativeHomeworkBoardWidget
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
/// 作业板
/// 顶栏(进度 d/n + 72x3 进度条 + ＋添加科目 + 两步确认清除已完成) + 双栏科目卡
/// (色点/标题双击改名/计数/两步确认删除/条目圆形勾选/划线/内联增改) 400ms 防抖持久化
/// </summary>
public sealed class NativeHomeworkBoardWidget : WidgetCardBase
{

    private static readonly Color[] Palette =
    {
        NativeTheme.Parse("#f59e0b"), NativeTheme.Parse("#3b82f6"), NativeTheme.Parse("#10b981"), NativeTheme.Parse("#ef4444"),
        NativeTheme.Parse("#8b5cf6"), NativeTheme.Parse("#ec4899"), NativeTheme.Parse("#06b6d4"), NativeTheme.Parse("#84cc16"),
    };

    private readonly TextBlock _progText = new();
    private readonly ProgressBar _progBar = new();
    private readonly Border _addSubBtn = new();
    private readonly Border _clearDoneBtn = new();
    private readonly TextBlock _clearDoneText = new();
    private readonly CardEmptyState _emptyView = new(
        FAIcons.Glyph(AppIcons.Book),
        AppUtils.Tr("homework.empty_title"),
        AppUtils.Tr("homework.empty"));
    // Grid 栏: 科目少时按星号均分整列高度 免留白
    private readonly Grid _col0 = new();
    private readonly Grid _col1 = new();
    private JsonArray _sections = new();
    private string? _dataFile;
    private readonly DispatcherTimer _saveTimer;

    public NativeHomeworkBoardWidget(ComponentDefinition definition) : base(definition)
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Render();
        AttachedToVisualTree += (_, _) =>
        {
            if (_dataFile is null && !string.IsNullOrEmpty(ComponentId))
            {
                _dataFile = IOPath.Combine(Paths.DataUser, $"homework_{ComponentId}.json");
                _sections = LoadFrom(_dataFile);
                Render();
            }
        };
        Log.Debug("[HW] 作业板组件初始化(原生)");
    }

    private bool IsDarkNow => CardIsDark;

    private static JsonArray LoadFrom(string file)
    {
        try
        {
            if (File.Exists(file)
                && JsonNode.Parse(File.ReadAllText(file)) is JsonObject obj
                && obj["sections"] is JsonArray arr)
            {
                Log.Debug($"[HW] 作业板读取: {arr.Count}个科目");
                return (JsonArray)arr.DeepClone();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[HW] 读取作业板失败: {e.Message}");
        }
        return new JsonArray();
    }

    private void Save()
    {
        try
        {
            if (_dataFile is null)
            {
                return;
            }
            Directory.CreateDirectory(Paths.DataUser);
            var payload = new JsonObject { ["sections"] = (_sections ?? new JsonArray()).DeepClone() };
            File.WriteAllText(_dataFile, payload.ToJsonString());
            DailyLog.Record(DailyLog.HomeworkPrefix + ComponentId, payload);
            Log.Debug($"[HW] 作业板已保存: {(_sections?.Count ?? 0)}个科目");
        }
        catch (Exception e)
        {
            Log.Warning($"[HW] 保存作业板失败: {e.Message}");
        }
    }


    private void Commit()
    {
        var clean = new JsonArray();
        foreach (var secNode in _sections!)
        {
            if (secNode is not JsonObject sec)
            {
                continue;
            }
            var title = ((string?)sec["title"] ?? "").Trim();
            if (title.Length > 30)
            {
                title = title[..30];
            }
            var items = new JsonArray();
            if (sec["items"] is JsonArray itemArr)
            {
                foreach (var itNode in itemArr)
                {
                    if (itNode is not JsonObject it)
                    {
                        continue;
                    }
                    var text = ((string?)it["text"] ?? "").Trim();
                    if (text.Length > 120)
                    {
                        text = text[..120];
                    }
                    if (text.Length > 0)
                    {
                        items.Add(new JsonObject { ["text"] = text, ["done"] = it["done"]?.GetValue<bool>() == true });
                    }
                }
            }
            if (title.Length > 0 || items.Count > 0)
            {
                clean.Add(new JsonObject { ["title"] = title, ["items"] = items });
            }
        }
        _sections = clean;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private (int Total, int Done) Progress()
    {
        var total = 0;
        var done = 0;
        foreach (var sec in _sections!)
        {
            if (sec is not JsonObject s || s["items"] is not JsonArray arr)
            {
                continue;
            }
            foreach (var it in arr)
            {
                if (it is not JsonObject o)
                {
                    continue;
                }
                total++;
                if (o["done"]?.GetValue<bool>() == true)
                {
                    done++;
                }
            }
        }
        return (total, done);
    }

    private void BuildLayout()
    {
        _progText.FontSize = WidgetType.Label;
        _progText.FontWeight = WidgetWeight.Title;
        _progText.VerticalAlignment = VerticalAlignment.Center;

        _progBar.Height = 4;
        _progBar.CornerRadius = new CornerRadius(2);
        _progBar.Minimum = 0;
        _progBar.Maximum = 100;
        _progBar.Value = 0;
        _progBar.Margin = new Thickness(2, 0, 2, 6);

        _addSubBtn.Child = new TextBlock { Text = "+ " + AppUtils.Tr("homework.add_subject"), FontSize = WidgetType.Label, FontWeight = WidgetWeight.Title };
        _addSubBtn.Padding = new Thickness(WidgetSpace.Gap, 3);
        _addSubBtn.CornerRadius = new CornerRadius(WidgetSpace.RadiusInner);
        _addSubBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _addSubBtn.VerticalAlignment = VerticalAlignment.Center;
        _addSubBtn.PointerReleased += (_, _) =>
        {
            _sections.Add(new JsonObject { ["title"] = "", ["items"] = new JsonArray() });
            Render();
        };

        _clearDoneBtn.Child = _clearDoneText;
        _clearDoneText.Text = AppUtils.Tr("homework.clear_done");
        _clearDoneText.FontSize = WidgetType.Label;
        _clearDoneText.FontWeight = WidgetWeight.Title;
        _clearDoneBtn.Padding = new Thickness(WidgetSpace.Gap, 3);
        _clearDoneBtn.CornerRadius = new CornerRadius(WidgetSpace.RadiusInner);
        _clearDoneBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _clearDoneBtn.VerticalAlignment = VerticalAlignment.Center;
        _clearDoneBtn.PointerReleased += (_, _) =>
        {
            var changed = false;
            foreach (var sec in _sections!)
            {
                if (sec is JsonObject s && s["items"] is JsonArray arr)
                {
                    var keep = new JsonArray();
                    foreach (var it in arr)
                    {
                        if (it is JsonObject o && o["done"]?.GetValue<bool>() != true)
                        {
                            keep.Add(o.DeepClone());
                        }
                        else
                        {
                            changed = true;
                        }
                    }
                    s["items"] = keep;
                }
            }
            if (changed)
            {
                Commit();
                Render();
            }
        };

        var topGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto,Auto,Auto") };
        topGrid.ColumnSpacing = WidgetSpace.Gap;
        var progGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.Gap, VerticalAlignment = VerticalAlignment.Center };
        progGroup.Children.Add(_progText);
        var historyBtn = CardTheme.TagButton(AppUtils.Tr("homework.history"), WidgetType.Label, IsDarkNow);
        historyBtn.PointerReleased += (_, _) => ComponentExpander.Show(this, AppUtils.Tr("homework.history"),
            DailyLogPanel.ForHomework(ComponentId));
        Grid.SetColumn(progGroup, 0);
        Grid.SetColumn(historyBtn, 3);
        Grid.SetColumn(_addSubBtn, 4);
        Grid.SetColumn(_clearDoneBtn, 5);
        topGrid.Children.Add(progGroup);
        topGrid.Children.Add(historyBtn);
        topGrid.Children.Add(_addSubBtn);
        topGrid.Children.Add(_clearDoneBtn);
        topGrid.Margin = new Thickness(0, WidgetSpace.GapXs, 0, WidgetSpace.Gap);

        var colsGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,12,*") };
        Grid.SetColumn(_col0, 0);
        Grid.SetColumn(_col1, 2);
        colsGrid.Children.Add(_col0);
        colsGrid.Children.Add(_col1);

        var body = new Grid();
        var scroll = new ScrollViewer
        {
            Content = colsGrid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        body.Children.Add(scroll);
        body.Children.Add(_emptyView);

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*"),
            Margin = new Thickness(WidgetSpace.PadCompact, WidgetSpace.GapXs, WidgetSpace.PadCompact, WidgetSpace.Gap),
            RowSpacing = WidgetSpace.GapXs,
        };
        Grid.SetRow(topGrid, 0);
        Grid.SetRow(_progBar, 1);
        Grid.SetRow(body, 2);
        root.Children.Add(topGrid);
        root.Children.Add(_progBar);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;
    }

    private TextBox MakeInlineInput(string? initial, string placeholder, double fontSize, bool bold = false)
    {
        var input = new TextBox
        {
            Text = initial ?? "",
            PlaceholderText = placeholder,
            FontSize = fontSize,
            FontWeight = bold ? WidgetWeight.Title : WidgetWeight.Body,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(CardTheme.T("chip", IsDarkNow)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(7, 2.5),
            MinHeight = 0,
        };
        input.Foreground = new SolidColorBrush(CardTheme.T("ink", IsDarkNow));
        return input;
    }

    // host 可为 Panel(StackPanel) 或 Border(文本宿主) 统一替换为下划线输入行
    private static void ReplaceHost(Control host, Control newChild)
    {
        switch (host)
        {
            case Panel p:
                p.Children.Clear();
                p.Children.Add(newChild);
                break;
            case Border b:
                b.Child = newChild;
                break;
        }
    }

    private TextBox WrapInlineInput(string? initial, string placeholder, double fontSize, Control host, bool bold = false)
    {
        var underline = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            BorderBrush = new SolidColorBrush(NativeTheme.Accent()),
            Child = MakeInlineInput(initial, placeholder, fontSize, bold),
        };
        ReplaceHost(host, underline);
        var input = (TextBox)underline.Child!;
        input.Focus();
        if (!string.IsNullOrEmpty(initial))
        {
            input.SelectAll();
        }
        return input;
    }

    private void Render()
    {
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        _progText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _progText.Text = "";
        _progBar.Foreground = new SolidColorBrush(accent);
        _progBar.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        _addSubBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        ((TextBlock)_addSubBtn.Child!).Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _clearDoneBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        _clearDoneText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _emptyView.Apply(WidgetSurfacePalette.Resolve(dark));

        var (total, done) = Progress();
        _progText.Text = $"{done}/{total}";
        _progBar.Value = total > 0 ? done * 100.0 / total : 0;
        _clearDoneBtn.IsVisible = done > 0;
        _emptyView.IsVisible = _sections.Count == 0;

        _col0.Children.Clear();
        _col1.Children.Clear();
        _col0.RowDefinitions.Clear();
        _col1.RowDefinitions.Clear();

        // 单科目横跨整卡 免右半留白
        var single = _sections.Count == 1;
        Grid.SetColumnSpan(_col0, single ? 3 : 1);

        // 双栏平衡分配: 按估算高 落列
        var h0 = 0.0;
        var h1 = 0.0;
        for (var si = 0; si < _sections.Count; si++)
        {
            if (_sections[si] is not JsonObject sec)
            {
                continue;
            }
            var itemCount = sec["items"] is JsonArray arr ? arr.Count : 0;
            var est = 30 + itemCount * 22;
            var target = single || h0 <= h1 ? _col0 : _col1;
            if (target == _col0)
            {
                h0 += est;
            }
            else
            {
                h1 += est;
            }
            target.Children.Add(BuildSection(si, sec));
        }

        // 该栏只放一个科目 -> 撑满列高; 多个则按内容高 由 ScrollViewer 接管
        FitColumn(_col0);
        FitColumn(_col1);
        InvalidateVisual();
    }

    // 单科目列 星号均分; 多科目 定量高
    private static void FitColumn(Grid col)
    {
        col.RowDefinitions.Clear();
        if (col.Children.Count == 0)
        {
            return;
        }
        var stretch = col.Children.Count == 1;
        for (var i = 0; i < col.Children.Count; i++)
        {
            col.RowDefinitions.Add(new RowDefinition(stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
            Grid.SetRow(col.Children[i], i);
        }
    }

    private Border BuildSection(int si, JsonObject sec)
    {
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        var color = Palette[si % Palette.Length];
        var halo = Color.FromArgb(0x22, color.R, color.G, color.B);

        var titleText = new TextBlock
        {
            Text = (string?)sec["title"] ?? "",
            FontSize = WidgetType.Body,
            FontWeight = WidgetWeight.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleText.Foreground = new SolidColorBrush(CardTheme.T("ink", dark));
        var titleHost = new Border { Child = titleText, MinWidth = 30 };
        titleText.DoubleTapped += (_, _) =>
        {
            var finished = false;
            var input = WrapInlineInput((string?)sec["title"], AppUtils.Tr("homework.subject_ph"), WidgetType.Body, titleHost, bold: true);
            var finish = (bool save) =>
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                var v = input.Text?.Trim() ?? "";
                if (save && v.Length > 0 && v != (string?)sec["title"])
                {
                    sec["title"] = v;
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
        };

        var items = sec["items"] as JsonArray ?? new JsonArray();
        var doneCount = items.Count(o => o is JsonObject o2 && o2["done"]?.GetValue<bool>() == true);
        var countText = new TextBlock
        {
            Text = items.Count > 0 ? $"{doneCount}/{items.Count}" : "",
            FontSize = WidgetType.Micro,
            VerticalAlignment = VerticalAlignment.Center,
        };
        countText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));

        // 两步确认删除科目
        var delText = new TextBlock { Text = "×", FontSize = WidgetType.Micro, FontWeight = WidgetWeight.Title, TextAlignment = TextAlignment.Center };
        var delBtn = new Border
        {
            Child = delText,
            Padding = new Thickness(7, 1),
            CornerRadius = new CornerRadius(4),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        delText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        var armed = false;
        DispatcherTimer? armTimer = null;
        delBtn.PointerEntered += (_, _) => delBtn.Opacity = 1;
        delBtn.PointerExited += (_, _) => { if (!armed) { delBtn.Opacity = 0; } };
        delBtn.PointerReleased += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                delBtn.Opacity = 1;
                delText.Text = AppUtils.Tr("homework.confirm_del");
                delBtn.Background = new SolidColorBrush(Color.FromArgb(230, 229, 72, 77));
                delText.Foreground = Brushes.White;
                armTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
                armTimer.Tick += (_, _) =>
                {
                    armTimer?.Stop();
                    armed = false;
                    delText.Text = "×";
                    delBtn.Background = null;
                    delText.Foreground = new SolidColorBrush(CardTheme.T("sub", IsDarkNow));
                };
                armTimer.Start();
            }
            else
            {
                armTimer?.Stop();
                _sections.RemoveAt(si);
                Commit();
                Render();
            }
        };

        var head = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,Auto,*,Auto") };
        head.ColumnSpacing = 7;
        var dotHalo = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(halo),
            Child = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(dotHalo, 0);
        Grid.SetColumn(titleHost, 1);
        Grid.SetColumn(countText, 2);
        Grid.SetColumn(delBtn, 4);
        head.Children.Add(dotHalo);
        head.Children.Add(titleHost);
        head.Children.Add(countText);
        head.Children.Add(delBtn);
        head.Margin = new Thickness(1, 0, 1, 5);

        // 三段: 标题顶格 / 条目居中吸收余高 / 添加行贴底
        var itemsHost = new StackPanel
        {
            VerticalAlignment = _sections.Count == 1 ? VerticalAlignment.Center : VerticalAlignment.Top,
        };
        for (var ii = 0; ii < items.Count; ii++)
        {
            if (items[ii] is JsonObject it)
            {
                itemsHost.Children.Add(BuildItemRow(si, ii, it));
            }
        }

        // 添加条目行
        var addRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapSm,
            Margin = new Thickness(3, 3, 3, 3),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        addRow.Children.Add(new TextBlock { Text = "+", FontSize = WidgetType.Label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.55 });
        addRow.Children.Add(new TextBlock { Text = AppUtils.Tr("homework.add_item"), FontSize = WidgetType.Label, VerticalAlignment = VerticalAlignment.Center });
        addRow.PointerReleased += (_, _) =>
        {
            var finished = false;
            var input = WrapInlineInput(null, AppUtils.Tr("homework.add_item"), WidgetType.Body, addRow);
            var finish = (bool save) =>
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                var v = input.Text?.Trim() ?? "";
                if (save && v.Length > 0)
                {
                    ((sec["items"] ??= new JsonArray()) as JsonArray)!.Add(new JsonObject { ["text"] = v, ["done"] = false });
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
        };

        var body = new Grid { RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto") };
        Grid.SetRow(head, 0);
        Grid.SetRow(itemsHost, 1);
        Grid.SetRow(addRow, 2);
        body.Children.Add(head);
        body.Children.Add(itemsHost);
        body.Children.Add(addRow);

        // 平面分组: 色点+标题即分组 科目间 1px 分隔线
        var secBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(CardTheme.T("line", dark)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(0, WidgetSpace.Gap, 0, WidgetSpace.Gap),
            Margin = new Thickness(0, 0, 0, WidgetSpace.Gap),
            Child = body,
        };
        secBorder.PointerEntered += (_, _) => delBtn.Opacity = 1;
        secBorder.PointerExited += (_, _) => { if (!armed) { delBtn.Opacity = 0; } };
        return secBorder;
    }

    private Border BuildItemRow(int si, int ii, JsonObject it)
    {
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        var done = it["done"]?.GetValue<bool>() == true;
        var text = (string?)it["text"] ?? "";

        var row = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = WidgetSpace.Gap,
            Margin = new Thickness(0, 0, 0, 0),
        };

        // 勾选圆 16x16 rx4.4
        var circle = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4.4),
            BorderThickness = new Thickness(1.4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = WidgetType.Micro,
                FontWeight = WidgetWeight.Title,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White,
                IsVisible = done,
            },
        };
        circle.Background = done ? new SolidColorBrush(accent) : null;
        circle.BorderBrush = done ? new SolidColorBrush(accent) : new SolidColorBrush(CardTheme.T("dotbd", dark));
        circle.VerticalAlignment = VerticalAlignment.Center;
        circle.PointerReleased += (_, _) =>
        {
            it["done"] = !(it["done"]?.GetValue<bool>() ?? false);
            Commit();
            Render();
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = WidgetType.Body,
            LineHeight = WidgetType.LineHeight(WidgetType.Body),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        textBlock.Foreground = done
            ? new SolidColorBrush(CardTheme.T("sub", dark), 0.6)
            : new SolidColorBrush(CardTheme.T("ink", dark));
        if (done)
        {
            textBlock.TextDecorations = TextDecorations.Strikethrough;
        }
        var textHost = new Border { Child = textBlock };
        textBlock.DoubleTapped += (_, _) =>
        {
            var finished = false;
            var input = WrapInlineInput(text, AppUtils.Tr("homework.add_item"), WidgetType.Body, textHost);
            var finish = (bool save) =>
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                var v = input.Text?.Trim() ?? "";
                if (save && v.Length > 0 && v != text)
                {
                    it["text"] = v;
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
        };

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.GapXs, Opacity = 0 };
        var editBtn = CardTheme.IconButton("✎", 18, false, () => IsDarkNow);
        editBtn.PointerReleased += (_, _) =>
        {
            textBlock.RaiseEvent(new RoutedEventArgs(Control.DoubleTappedEvent));
        };
        var delBtn = CardTheme.IconButton("×", 18, false, () => IsDarkNow);
        delBtn.PointerReleased += (_, _) =>
        {
            if (_sections[si] is JsonObject s && s["items"] is JsonArray arr)
            {
                arr.RemoveAt(ii);
                Commit();
                Render();
            }
        };
        btns.Children.Add(editBtn);
        btns.Children.Add(delBtn);

        Grid.SetColumn(circle, 0);
        Grid.SetColumn(textHost, 1);
        Grid.SetColumn(btns, 2);
        row.Children.Add(circle);
        row.Children.Add(textHost);
        row.Children.Add(btns);

        var rowBorder = new Border
        {
            Child = row,
            Padding = new Thickness(WidgetSpace.GapXs, WidgetSpace.GapXs, WidgetSpace.GapSm, WidgetSpace.GapXs),
            CornerRadius = new CornerRadius(6),
        };
        rowBorder.PointerEntered += (_, _) =>
        {
            rowBorder.Background = new SolidColorBrush(CardTheme.T("hover", dark));
            btns.Opacity = 1;
        };
        rowBorder.PointerExited += (_, _) =>
        {
            rowBorder.Background = null;
            btns.Opacity = 0;
        };
        return rowBorder;
    }
}
