﻿// Glimpseon
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

// VolumeControlWidget
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;

namespace Glimpseon.UI;

/// <summary>
/// 音量控制
/// 400x100: 静音键 + 滑块
/// 1 秒轮询
/// </summary>
public sealed class VolumeControlWidget : WidgetCardBase
{
    private readonly TextBlock _title;
    private readonly TextBlock _percent = new()
    {
        FontSize = 20,
        FontWeight = WidgetWeight.Display,
        FontFamily = WidgetFonts.Num,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Slider _slider = new()
    {
        Minimum = 0,
        Maximum = 100,
        SmallChange = 1,
        LargeChange = 10,
        VerticalAlignment = VerticalAlignment.Center,
        MinHeight = 24,
    };
    private readonly Border _mute = new()
    {
        Width = 26,
        Height = 26,
        CornerRadius = new CornerRadius(13),
        Background = Brushes.Transparent,
        Cursor = new Cursor(StandardCursorType.Hand),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _muteGlyph = new()
    {
        Text = AppIcons.Volume,
        FontFamily = AppIcons.Family,
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };
    private readonly DispatcherTimer _timer;

    private bool _syncing;
    private bool _dragging;
    private bool _muted;
    private bool _muteHover;
    private bool _mutePressed;
    private double _lastVolume = 0.5;

    public VolumeControlWidget(ComponentDefinition definition) : base(definition)
    {
        _title = CardChrome.Title("音量控制");
        _percent.Text = "0%";

        var head = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
        };
        head.Children.Add(_mute);
        Grid.SetColumn(_title, 1);
        head.Children.Add(_title);
        Grid.SetColumn(_percent, 2);
        head.Children.Add(_percent);

        _mute.Child = _muteGlyph;
        _mute.PointerEntered += (_, _) =>
        {
            _muteHover = true;
            ApplyMuteVisual();
        };
        _mute.PointerExited += (_, _) =>
        {
            _muteHover = false;
            _mutePressed = false;
            ApplyMuteVisual();
        };
        _mute.PointerPressed += (_, e) =>
        {
            // 编辑态让位拖动
            if (IsDraggable || !e.GetCurrentPoint(_mute).Properties.IsLeftButtonPressed)
            {
                return;
            }
            _mutePressed = true;
            e.Pointer.Capture(_mute);
            ApplyMuteVisual();
            e.Handled = true;
        };
        _mute.PointerReleased += (_, e) =>
        {
            if (!_mutePressed)
            {
                return;
            }
            _mutePressed = false;
            e.Pointer.Capture(null);
            var local = e.GetPosition(_mute);
            if (local.X >= 0 && local.Y >= 0 && local.X <= _mute.Bounds.Width && local.Y <= _mute.Bounds.Height)
            {
                ToggleMute();
            }
            e.Handled = true;
        };
        _mute.PointerCaptureLost += (_, _) =>
        {
            _mutePressed = false;
            ApplyMuteVisual();
        };

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = 2,
            Margin = new Thickness(WidgetSpace.PadCompact),
        };
        root.Children.Add(head);
        Grid.SetRow(_slider, 1);
        root.Children.Add(_slider);
        Card.Child = root;
        Content = Card;

        // 拖动标记
        _slider.AddHandler(PointerPressedEvent, (_, _) => _dragging = true, RoutingStrategies.Tunnel);
        _slider.AddHandler(PointerReleasedEvent, (_, _) => _dragging = false, RoutingStrategies.Tunnel);
        _slider.PointerCaptureLost += (_, _) => _dragging = false;
        _slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                OnSliderChanged();
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => SyncFromSystem();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        SyncFromSystem(force: true);
        ApplyPalette();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
        SyncFromSystem(force: true);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void SyncFromSystem(bool force = false)
    {
        var volume = Math.Clamp(Audio.Volume, 0, 1);
        if (_dragging && !force)
        {
            return;
        }
        if (force || Math.Abs(_slider.Value - volume * 100) > 0.5)
        {
            _syncing = true;
            _slider.Value = volume * 100;
            _syncing = false;
        }
        if (volume > 0.001)
        {
            _lastVolume = volume;
        }
        ShowVolume(volume);
    }

    private void OnSliderChanged()
    {
        if (_syncing)
        {
            return;
        }
        var volume = Math.Clamp(_slider.Value, 0, 100) / 100.0;
        Audio.SetVolume(volume);
        if (volume > 0.001)
        {
            _lastVolume = volume;
        }
        ShowVolume(volume);
    }

    private void ToggleMute()
    {
        var current = Math.Clamp(Audio.Volume, 0, 1);
        var target = current > 0.001 ? 0 : _lastVolume > 0.001 ? _lastVolume : 0.5;
        _syncing = true;
        _slider.Value = target * 100;
        _syncing = false;
        Audio.SetVolume(target);
        ShowVolume(target);
    }

    private void ShowVolume(double volume)
    {
        var pct = (int)Math.Round(Math.Clamp(volume, 0, 1) * 100);
        _percent.Text = $"{pct}%";
        _muted = pct == 0;
        _muteGlyph.Text = _muted ? AppIcons.Mute : AppIcons.Volume;
        ApplyMuteVisual();
    }

    // 仅刷新静音键
    private void ApplyMuteVisual()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _mute.Background = _muteHover
            ? new SolidColorBrush(p.Text, CardIsDark ? 0.10 : 0.07)
            : Brushes.Transparent;
        _mute.Opacity = _mutePressed ? 0.7 : 1;
        _muteGlyph.Foreground = _muted ? CardAccent.Brush() : CardInk.Muted(p);
        _percent.Foreground = _muted ? CardInk.Faint(p) : CardInk.Fg(p);
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _title.Foreground = CardInk.Fg(p);

        var accent = CardAccent.Value;
        var track = new SolidColorBrush(p.Text, CardIsDark ? 0.18 : 0.14);
        _slider.Resources["SliderTrackValueFill"] = new SolidColorBrush(accent);
        _slider.Resources["SliderTrackValueFillPointerOver"] = new SolidColorBrush(accent);
        _slider.Resources["SliderTrackValueFillPressed"] = new SolidColorBrush(accent);
        _slider.Resources["SliderThumbBackground"] = new SolidColorBrush(accent);
        _slider.Resources["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(accent);
        _slider.Resources["SliderThumbBackgroundPressed"] = new SolidColorBrush(accent);
        _slider.Resources["SliderTrackFill"] = track;
        _slider.Resources["SliderTrackFillPointerOver"] = track;
        _slider.Resources["SliderTrackFillPressed"] = track;
        ApplyMuteVisual();
    }
}
