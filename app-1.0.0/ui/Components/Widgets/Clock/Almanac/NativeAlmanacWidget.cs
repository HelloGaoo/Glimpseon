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

// NativeAlmanacWidget
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
/// 黄历
/// 左列: 农历 label + 大号农历日期(按宽自适应) + 干支两行 + 节气(accent)
/// 右列: 宜/忌小徽标 + 词条全量换行(字号随可用空间收敛, 实在放不下才整词截断)
/// 竖虚线 + 行间横虚线; 右上角 mono 日期; 30 分钟刷新; 窄卡减信息
/// </summary>
public sealed class NativeAlmanacWidget : WidgetCardBase
{
    private static Color T(string key, bool dark) => dark ? key switch
    {
        "fg" => NativeTheme.Rgba(255, 255, 255, 0.87),
        "muted" => NativeTheme.Rgba(255, 255, 255, 0.62),
        "faint" => NativeTheme.Rgba(255, 255, 255, 0.38),
        "term_c" => NativeTheme.Rgba(255, 255, 255, 0.80),
        "yi" => NativeTheme.Parse("#e0554b"),
        "ji" => NativeTheme.Parse("#6f6f6f"),
        "div" => NativeTheme.Rgba(255, 255, 255, 0.18),
        "accent" => NativeTheme.Parse("#e08a80"),
        _ => Colors.White,
    } : key switch
    {
        "fg" => NativeTheme.Rgba(40, 40, 40, 0.88),
        "muted" => NativeTheme.Rgba(40, 40, 40, 0.62),
        "faint" => NativeTheme.Rgba(40, 40, 40, 0.40),
        "term_c" => NativeTheme.Rgba(40, 40, 40, 0.80),
        "yi" => NativeTheme.Parse("#cf3838"),
        "ji" => NativeTheme.Parse("#3c3c3c"),
        "div" => NativeTheme.Rgba(0, 0, 0, 0.15),
        "accent" => NativeTheme.Parse("#b8483d"),
        _ => Colors.Black,
    };

    private readonly TextBlock _sdate = new();
    private readonly TextBlock _label = new();
    private readonly TextBlock _lunar = new();
    private readonly TextBlock _gzYear = new();
    private readonly TextBlock _gzRest = new();
    private readonly TextBlock _term = new();
    private readonly TextBlock _good = new();
    private readonly TextBlock _bad = new();
    private Border _yiBadge = null!;
    private Border _jiBadge = null!;
    private readonly DashedRule _vdiv = new() { Vertical = true };
    private readonly DashedRule _hdiv = new();
    private readonly Grid _leftCol = new() { MaxWidth = 150 };
    private readonly Grid _main = new();
    private readonly StackPanel _rightCol = new() { VerticalAlignment = VerticalAlignment.Center };
    private string[]? _goodTerms;
    private string[]? _badTerms;

    public NativeAlmanacWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        SizeChanged += (_, _) => FitText();
        ActualThemeVariantChanged += (_, _) => Rebuild();
        // 原版 30 分钟定期刷新
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        timer.Tick += (_, _) => Rebuild();
        timer.Start();
        Rebuild();
    }

    private bool IsDarkNow => CardIsDark;

    private static Border MakeBadge(string glyph, string key, bool dark)
    {
        return new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(T(key, dark)),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = WidgetType.Label,
                FontWeight = WidgetWeight.Emphasis,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private void BuildLayout()
    {
        var dark = IsDarkNow;
        _sdate.FontFamily = new FontFamily(NativeTheme.MonoFont);
        _sdate.FontSize = WidgetType.Micro;
        _sdate.LetterSpacing = 1.5;
        _sdate.VerticalAlignment = VerticalAlignment.Top;
        _sdate.HorizontalAlignment = HorizontalAlignment.Right;
        _sdate.Margin = new Thickness(0, 12, 16, 0);

        _label.Text = "农历";
        _label.FontSize = WidgetType.Micro;
        _label.LetterSpacing = 2;
        _label.Margin = new Thickness(2, 0, 0, WidgetSpace.GapXs);
        _lunar.FontWeight = WidgetWeight.Display;
        _lunar.LetterSpacing = 1;
        _lunar.Margin = new Thickness(0, 0, 0, 9);
        _gzYear.FontSize = WidgetType.Body;
        _gzYear.LetterSpacing = 1;
        _gzYear.Margin = new Thickness(0, 0, 0, WidgetSpace.GapXs);
        _gzRest.FontSize = WidgetType.Body;
        _gzRest.LetterSpacing = 1;
        _term.FontSize = WidgetType.Label;
        _term.LetterSpacing = 1.5;
        _term.Margin = new Thickness(0, WidgetSpace.Gap, 0, 0);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_label);
        left.Children.Add(_lunar);
        left.Children.Add(_gzYear);
        left.Children.Add(_gzRest);
        left.Children.Add(_term);
        _leftCol.Children.Add(left);

        _good.TextWrapping = TextWrapping.Wrap;
        _bad.TextWrapping = TextWrapping.Wrap;

        _yiBadge = MakeBadge("宜", "yi", dark);
        _jiBadge = MakeBadge("忌", "ji", dark);

        var rowYi = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*") };
        rowYi.ColumnSpacing = WidgetSpace.Gap;
        Grid.SetColumn(_yiBadge, 0);
        Grid.SetColumn(_good, 1);
        rowYi.Children.Add(_yiBadge);
        rowYi.Children.Add(_good);

        var rowJi = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*") };
        rowJi.ColumnSpacing = WidgetSpace.Gap;
        Grid.SetColumn(_jiBadge, 0);
        Grid.SetColumn(_bad, 1);
        rowJi.Children.Add(_jiBadge);
        rowJi.Children.Add(_bad);

        _hdiv.Margin = new Thickness(0, WidgetSpace.Gap);

        _rightCol.Children.Add(rowYi);
        _rightCol.Children.Add(_hdiv);
        _rightCol.Children.Add(rowJi);

        _main.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,12,*");
        _main.Margin = new Thickness(WidgetSpace.Pad);
        Grid.SetColumn(_leftCol, 0);
        Grid.SetColumn(_vdiv, 1);
        _vdiv.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(_rightCol, 2);
        _rightCol.Margin = new Thickness(12, 0, 0, 0);
        _main.Children.Add(_leftCol);
        _main.Children.Add(_vdiv);
        _main.Children.Add(_rightCol);

        var root = new Grid();
        root.Children.Add(_main);
        root.Children.Add(_sdate);
        Card.Child = root;
        Content = Card;
    }

    // 不再按固定个数截断; 词条全量给出, 空间由字号与整词截断兜底
    // 词内插 word joiner(U+2060), 防止 "进人口" 在折行时被拆成 "进人 / 口"
    private static string TermsText(string[]? terms) =>
        terms is { Length: > 0 } ? JoinTerms(terms) : "--";

    private static string JoinTerms(IEnumerable<string> terms) =>
        string.Join(" ", terms.Select(Glue));

    private static string Glue(string term) =>
        term.Length <= 1 ? term : string.Join("\u2060", term.Select(c => c.ToString()));

    private void Rebuild()
    {
        var d = AlmanacService.GetToday();
        var lunar = "--";
        var gzYear = "--";
        var gzRest = "--";
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        if (d is not null)
        {
            lunar = $"{d.LunarMonth}{d.LunarDay}".Trim();
            if (lunar.Length == 0)
            {
                lunar = "--";
            }
            // 干支压成两行: 丙午马年 / 己亥月 丙戌日
            gzYear = string.IsNullOrEmpty(d.YearGz) ? "--" : $"{d.YearGz}{d.Zodiac}年";
            gzRest = $"{d.MonthGz}月 {d.DayGz}日".Trim();
            if (gzRest.Length == 0)
            {
                gzRest = "--";
            }
            _goodTerms = d.Good;
            _badTerms = d.Bad;
            if (!string.IsNullOrEmpty(d.Date) && DateTime.TryParse(d.Date, out var pd))
            {
                date = pd.ToString("yyyy-MM-dd");
            }
        }
        _sdate.Text = NativeTheme.DotDate(date);
        _lunar.Text = lunar;
        _gzYear.Text = gzYear;
        _gzRest.Text = gzRest;
        _term.Text = string.IsNullOrEmpty(d?.SolarTerm) ? "" : d!.SolarTerm;
        ApplyPalette();
        FitText();
        InvalidateVisual();
    }

    private void ApplyPalette()
    {
        var dark = IsDarkNow;
        _sdate.Foreground = new SolidColorBrush(T("faint", dark));
        _label.Foreground = new SolidColorBrush(T("muted", dark));
        _lunar.Foreground = new SolidColorBrush(T("fg", dark));
        _gzYear.Foreground = new SolidColorBrush(T("muted", dark));
        _gzRest.Foreground = new SolidColorBrush(T("muted", dark));
        _term.Foreground = new SolidColorBrush(T("accent", dark));
        _good.Foreground = new SolidColorBrush(T("term_c", dark));
        _bad.Foreground = new SolidColorBrush(T("term_c", dark));
        _yiBadge.Background = new SolidColorBrush(T("yi", dark));
        _jiBadge.Background = new SolidColorBrush(T("ji", dark));
        var div = new SolidColorBrush(T("div", dark));
        _vdiv.Stroke = div;
        _hdiv.Stroke = div;
    }

    private void FitText()
    {
        var compact = Bounds.Width < 300;
        var tiny = Bounds.Height < 150;
        _label.IsVisible = !compact;
        _term.IsVisible = !string.IsNullOrEmpty(_term.Text) && !compact && !tiny;
        _main.Margin = compact ? new Thickness(WidgetSpace.PadCompact) : new Thickness(WidgetSpace.Pad);

        // 左列: 农历日期/干支按可用宽收敛, 长农历日不再截断
        var maxLeft = Math.Clamp(Bounds.Width - 36 - 10 - 14 - 120, 96, 150);
        if (double.IsNaN(maxLeft))
        {
            maxLeft = 148;
        }
        _lunar.FontSize = CardText.Fit(_lunar.Text, Common.AppFontFamily, WidgetWeight.Display, maxLeft, compact ? 26 : 30, 18, 30);
        var wide = Math.Max(
            CardText.Width(_gzYear.Text, Common.AppFontFamily, FontWeight.Normal, WidgetType.Body),
            CardText.Width(_gzRest.Text, Common.AppFontFamily, FontWeight.Normal, WidgetType.Body));
        var gzSize = wide > maxLeft ? Math.Max(WidgetType.Micro, Math.Round(WidgetType.Body * maxLeft / wide, 1)) : WidgetType.Body;
        _gzYear.FontSize = gzSize;
        _gzRest.FontSize = gzSize;

        // 右列: 词条全量换行; 字号从 13.5 收敛到 10.5, 仍放不下才整词截断
        var rightW = _rightCol.Bounds.Width;
        if (double.IsNaN(rightW) || rightW < 40)
        {
            rightW = Math.Max(60, Bounds.Width - _main.Margin.Left - _main.Margin.Right - maxLeft - 10 - 14);
        }
        var rightH = _rightCol.Bounds.Height;
        if (double.IsNaN(rightH) || rightH < 20)
        {
            rightH = Math.Max(60, Bounds.Height - _main.Margin.Top - _main.Margin.Bottom);
        }
        var rowBudget = Math.Max(24, (rightH - 21) / 2);
        var need = 0.0;
        var size = WidgetType.Body;
        foreach (var s in new[] { WidgetType.Body, WidgetType.Label, WidgetType.Micro })
        {
            size = s;
            need = Math.Max(RowNeed(_goodTerms, rightW, s), RowNeed(_badTerms, rightW, s));
            if (need <= rowBudget)
            {
                break;
            }
        }
        var lineHeight = size * WidgetType.BodyLineHeight;
        var allowLines = Math.Max(1, (int)Math.Floor(rowBudget / lineHeight));
        _good.Text = need <= rowBudget ? TermsText(_goodTerms) : FitTerms(_goodTerms, rightW, size, allowLines);
        _bad.Text = need <= rowBudget ? TermsText(_badTerms) : FitTerms(_badTerms, rightW, size, allowLines);
        _good.FontSize = size;
        _good.LineHeight = Math.Round(lineHeight, 1);
        _bad.FontSize = size;
        _bad.LineHeight = Math.Round(lineHeight, 1);
    }

    // 词条列在当前字号下的换行高度
    private static double RowNeed(string[]? terms, double colW, double size)
    {
        var lines = Lines(TermsText(terms), colW, size);
        return Math.Max(1, lines) * size * WidgetType.BodyLineHeight;
    }

    private static int Lines(string text, double colW, double size) =>
        Math.Max(1, (int)Math.Ceiling(CardText.Width(text, Common.AppFontFamily, FontWeight.Normal, size) / Math.Max(1, colW)));

    // 整词截断: 从尾部成词去掉, 不出现半词
    private static string FitTerms(string[]? terms, double colW, double size, int maxLines)
    {
        if (terms is not { Length: > 0 })
        {
            return "--";
        }
        var count = terms.Length;
        while (count > 1 && Lines(JoinTerms(terms[..count]), colW, size) > maxLines)
        {
            count--;
        }
        var text = JoinTerms(terms[..count]);
        return count == terms.Length ? text : text + " …";
    }
}
