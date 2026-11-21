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

// DailyPhotoWidget
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

/// <summary>每日一图</summary>
public sealed class DailyPhotoWidget : WidgetCardBase
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.UniformToFill,
    };
    private readonly Border _frame;
    private readonly Border _shade;
    private readonly TextBlock _title = new()
    {
        FontSize = WidgetType.Title,
        FontWeight = WidgetWeight.Title,
        Foreground = new SolidColorBrush(Colors.White),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
    };
    private readonly TextBlock _copyright = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        Foreground = new SolidColorBrush(Colors.White, 0.70),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 2,
    };
    private readonly TextBlock _status = new()
    {
        FontSize = WidgetType.Label,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };
    private readonly DispatcherTimer _refreshTimer;
    private Bitmap? _bitmap;
    private bool _loading;
    private bool _hasPhoto;
    private string _loadedDate = "";

    public DailyPhotoWidget(ComponentDefinition definition) : base(definition)
    {
        _frame = new Border
        {
            ClipToBounds = true,
            Child = _image,
        };

        var shadeText = new StackPanel
        {
            Spacing = WidgetSpace.GapXs,
            Children = { _title, _copyright },
        };
        _shade = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(WidgetSpace.PadCompact, WidgetSpace.Pad, WidgetSpace.PadCompact, WidgetSpace.PadCompact),
            IsHitTestVisible = false,
            IsVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(115, 0, 0, 0), 0),
                    new GradientStop(Colors.Transparent, 1),
                },
            },
            Child = shadeText,
        };

        var root = new Grid();
        root.Children.Add(_frame);
        root.Children.Add(_shade);
        root.Children.Add(_status);
        Card.Child = root;
        Content = Card;

        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        ApplyPalette();
        if (PreviewSamples.Active && (HomeView.CurrentWallpaper ?? HomeView.GlassBackdrop) is { } sample)
        {
            _image.Source = sample;
            _hasPhoto = true;
            _title.Text = "每日一图";
            _copyright.Text = "示例图片";
            _shade.IsVisible = true;
            _status.IsVisible = false;
        }

        // 定时核对重试
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _refreshTimer.Tick += (_, _) => _ = LoadAsync();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (PreviewSamples.Active && _hasPhoto)
        {
            return;
        }
        _refreshTimer.Start();
        _ = LoadAsync();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task LoadAsync()
    {
        if (_loading || _loadedDate == DateTime.Now.ToString("yyyyMMdd"))
        {
            return;
        }
        _loading = true;
        if (!_hasPhoto)
        {
            ShowStatus("正在获取今日图片…");
        }

        DailyPhotoFetch? fetch = null;
        try
        {
            fetch = await DailyPhotoService.FetchAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"[PIC] 每日图片加载失败: {e.Message}");
        }
        var info = fetch?.Info ?? DailyPhotoService.FromCache();

        if (info is not null && TryShowPhoto(info))
        {
            _status.IsVisible = false;
            // 旧图保留重试
            _loadedDate = fetch is { Stale: false } ? DateTime.Now.ToString("yyyyMMdd") : "";
        }
        else if (!_hasPhoto)
        {
            ShowStatus("今日图片暂不可用");
        }
        _loading = false;
    }

    private bool TryShowPhoto(DailyPhotoInfo info)
    {
        try
        {
            var bitmap = new Bitmap(info.Path);
            _image.Source = bitmap;
            _bitmap?.Dispose();
            _bitmap = bitmap;
            _hasPhoto = true;
            _title.Text = info.Title;
            _copyright.Text = info.Copyright;
            _shade.IsVisible = true;
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"[PIC] 图片解码失败: {e.Message}");
            return false;
        }
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.IsVisible = true;
        if (!_hasPhoto)
        {
            _shade.IsVisible = false;
        }
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _frame.CornerRadius = new CornerRadius(EffectiveRadius);
        _shade.CornerRadius = new CornerRadius(EffectiveRadius);
        _status.Foreground = new SolidColorBrush(p.Text, CardInk.MutedOpacity);
    }
}
