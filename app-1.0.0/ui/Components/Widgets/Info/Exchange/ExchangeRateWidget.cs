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

// ExchangeRateWidget
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

/// <summary>
/// 汇率牌价
/// 400x200: 两列币种
/// 100 CNY 基准
/// </summary>
public sealed class ExchangeRateWidget : WidgetCardBase
{
    private static readonly Dictionary<string, string> CurrencyNames = new()
    {
        ["USD"] = "美元",
        ["EUR"] = "欧元",
        ["JPY"] = "日元",
        ["HKD"] = "港币",
        ["GBP"] = "英镑",
        ["KRW"] = "韩元",
        ["AUD"] = "澳元",
        ["CAD"] = "加元",
        ["SGD"] = "新加坡元",
    };

    private readonly TextBlock _title;
    private readonly Border _refreshBtn = new()
    {
        Width = 22,
        Height = 22,
        CornerRadius = new CornerRadius(11),
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Cursor = new Cursor(StandardCursorType.Hand),
    };
    private readonly TextBlock _refreshIcon = AppIcons.Create(AppIcons.Refresh, 11);
    private readonly TextBlock _stamp = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };
    private readonly Grid _list = new()
    {
        ColumnDefinitions = ColumnDefinitions.Parse("*,*"),
        ColumnSpacing = WidgetSpace.PadCompact,
        RowSpacing = WidgetSpace.Gap,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock _loadingText = new()
    {
        Text = "正在获取汇率…",
        FontSize = WidgetType.Label,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };
    private readonly TextBlock _emptyText = new()
    {
        Text = "暂无汇率数据",
        FontSize = WidgetType.Label,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };
    private readonly List<string> _currencies = new();
    private readonly DispatcherTimer _flashTimer;
    private readonly DispatcherTimer _refreshTimer;
    private ExchangeSnapshot? _snapshot;
    private DateTimeOffset? _checkedAt;
    private DateTimeOffset? _sourceAt;
    private string? _flashText;
    private bool _loading;
    private bool _offline;

    public ExchangeRateWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _currencies.AddRange(ReadCurrencies(config));

        _title = CardChrome.Title("汇率牌价");
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            _flashText = null;
            UpdateStamp();
        };
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _refreshTimer.Tick += (_, _) =>
        {
            if (!_loading)
            {
                _ = LoadAsync(forceNetwork: false);
            }
        };
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        ApplyPalette();
        _ = LoadAsync(forceNetwork: false);
    }

    private static List<string> ReadCurrencies(JsonElement? config) =>
        SplitCurrencies(ReadString(config, "currencies", ComponentDefaults.Currencies));

    // 逗号分隔 6 个
    private static List<string> SplitCurrencies(string raw)
    {
        var list = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (list.Count >= 6)
            {
                break;
            }
            var code = part.ToUpperInvariant();
            if (code.Length > 0 && !list.Contains(code))
            {
                list.Add(code);
            }
        }
        return list;
    }

    private void BuildLayout()
    {
        _refreshIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _refreshIcon.VerticalAlignment = VerticalAlignment.Center;
        _refreshBtn.Child = _refreshIcon;
        ToolTip.SetTip(_refreshBtn, "立即刷新（跳过缓存）");
        _refreshBtn.PointerReleased += (_, e) =>
        {
            if (_loading || e.InitialPressMouseButton != MouseButton.Left || !_refreshBtn.IsPointerOver)
            {
                return;
            }
            _ = LoadAsync(forceNetwork: true);
        };

        var head = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
        };
        head.Children.Add(_title);
        Grid.SetColumn(_stamp, 1);
        head.Children.Add(_stamp);
        Grid.SetColumn(_refreshBtn, 2);
        head.Children.Add(_refreshBtn);

        var body = new Grid();
        body.Children.Add(_list);
        body.Children.Add(_loadingText);
        body.Children.Add(_emptyText);

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapXs,
            Margin = new Thickness(10),
        };
        root.Children.Add(head);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _title.Foreground = CardInk.Fg(p);
        _refreshIcon.Foreground = CardInk.Muted(p);
        _stamp.Foreground = CardInk.Faint(p);
        _loadingText.Foreground = CardInk.Faint(p);
        _emptyText.Foreground = CardInk.Faint(p);
        RefreshRows();
    }

    private void RefreshRows()
    {
        _list.Children.Clear();
        _list.RowDefinitions.Clear();
        _loadingText.IsVisible = _loading && _snapshot is null;
        _emptyText.IsVisible = !_loading && _snapshot is null;
        _refreshIcon.Opacity = _loading ? 0.45 : 1.0;

        if (_snapshot is not null)
        {
            var rows = (_currencies.Count + 1) / 2;
            for (var i = 0; i < Math.Max(1, rows); i++)
            {
                _list.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            }
            for (var i = 0; i < _currencies.Count; i++)
            {
                var cell = BuildCell(_currencies[i]);
                Grid.SetColumn(cell, i % 2);
                Grid.SetRow(cell, i / 2);
                _list.Children.Add(cell);
            }
        }

        UpdateStamp();
    }

    // 本地检查时间
    private void UpdateStamp()
    {
        ToolTip.SetTip(_refreshBtn, _loading ? "正在刷新…" : "立即刷新（跳过缓存）");
        if (_flashText is not null)
        {
            _stamp.Text = _flashText;
            return;
        }
        if (_loading)
        {
            _stamp.Text = "刷新中…";
            return;
        }
        if (_snapshot is null)
        {
            _stamp.Text = "100 CNY";
            ToolTip.SetTip(_stamp, "点击右上角刷新");
            return;
        }

        var local = (_checkedAt ?? DateTimeOffset.Now).ToLocalTime();
        _stamp.Text = _offline
            ? $"缓存 · {local:HH:mm:ss}"
            : $"100 CNY · {local:HH:mm:ss}";
        var source = _sourceAt is { } src
            ? src.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "未知";
        ToolTip.SetTip(_stamp,
            $"本地检查 {local:MM-dd HH:mm:ss}\n数据源更新 {source}（API 每日更新）");
    }

    private Control BuildCell(string code)
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var value = new TextBlock
        {
            FontSize = WidgetType.Body,
            FontWeight = WidgetWeight.Title,
            FontFamily = WidgetFonts.Num,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (_snapshot is not null && _snapshot.Rates.TryGetValue(code, out var rate))
        {
            var decimals = code is "JPY" or "KRW" ? 0 : 2;
            value.Text = (rate * 100).ToString(decimals == 0 ? "0" : "0.00", CultureInfo.InvariantCulture);
            value.Foreground = CardInk.Fg(p);
        }
        else
        {
            value.Text = "--";
            value.Foreground = CardInk.Faint(p);
        }

        var cell = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = WidgetSpace.GapXs,
            MinHeight = 20,
            VerticalAlignment = VerticalAlignment.Center,
        };
        cell.Children.Add(new TextBlock
        {
            Text = code,
            FontSize = WidgetType.Label,
            FontWeight = WidgetWeight.Emphasis,
            Foreground = CardInk.Fg(p),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock
        {
            Text = CurrencyNames.GetValueOrDefault(code, code),
            FontSize = WidgetType.Micro,
            FontWeight = WidgetWeight.Body,
            Foreground = CardInk.Faint(p),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        Grid.SetColumn(name, 1);
        cell.Children.Add(name);
        Grid.SetColumn(value, 2);
        cell.Children.Add(value);
        return cell;
    }

    private async Task LoadAsync(bool forceNetwork)
    {
        _loading = true;
        _flashText = null;
        RefreshRows();
        if (forceNetwork)
        {
            Log.Info("[FX] 手动刷新汇率（跳过缓存）");
        }

        ExchangeSnapshot? snap;
        var offline = false;
        try
        {
            var fetch = await ExchangeRateService.FetchAsync(useCache: !forceNetwork);
            snap = fetch?.Snapshot;
            offline = fetch?.Fallback ?? false;
        }
        catch (Exception e)
        {
            Log.Warning($"[FX] 刷新汇率失败: {e.Message}");
            snap = ExchangeRateService.FromCache(ignoreExpiry: true);
            offline = snap is not null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _loading = false;
            if (snap is not null)
            {
                _snapshot = snap;
                _sourceAt = snap.Updated;
                _checkedAt = DateTimeOffset.Now;
                _offline = offline;
                _flashText = offline
                    ? $"失败 · 缓存 {_checkedAt.Value:HH:mm:ss}"
                    : $"已刷新 {_checkedAt.Value:HH:mm:ss}";
                if (forceNetwork)
                {
                    Log.Info($"[FX] 手动刷新完成: {snap.Rates.Count} 个币种, 本地 {_checkedAt.Value:HH:mm:ss}");
                }
            }
            else
            {
                _flashText = "刷新失败";
                if (forceNetwork)
                {
                    Log.Warning("[FX] 手动刷新失败且无可用缓存");
                }
            }
            RefreshRows();
            _flashTimer.Stop();
            _flashTimer.Start();
        });
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _refreshTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _flashTimer.Stop();
        _refreshTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
