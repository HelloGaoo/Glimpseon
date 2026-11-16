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

// RecycleBinWidget
using System.Globalization;
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
/// 回收站
/// 200x200: 项目数与体积 + 打开/清空
/// 清空需二次确认
/// </summary>
public sealed class RecycleBinWidget : WidgetCardBase
{
    private readonly TextBlock _title;
    private readonly TextBlock _count = new()
    {
        FontSize = 22,
        FontWeight = WidgetWeight.Display,
        FontFamily = WidgetFonts.Num,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
    };
    private readonly TextBlock _unit = new()
    {
        Text = "项",
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 3),
    };
    private readonly TextBlock _size = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
    };
    private readonly TextBlock _glyph = AppIcons.Create(AppIcons.Delete, 52);
    private readonly BinButton _open;
    private readonly BinButton _clear;
    private readonly DispatcherTimer _revert;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _notice;

    private RecycleSnapshot _snap = new(0, 0);

    public RecycleBinWidget(ComponentDefinition definition) : base(definition)
    {
        _title = CardChrome.Title("回收站");

        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _glyph.VerticalAlignment = VerticalAlignment.Center;
        _glyph.Opacity = 0.6;

        var countRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { _count, _unit },
        };

        var head = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
        };
        var titleBox = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _title, _size },
        };
        head.Children.Add(titleBox);
        Grid.SetColumn(countRow, 1);
        head.Children.Add(countRow);

        Func<bool> drag = () => IsDraggable;
        _open = new BinButton("打开", null, false, drag);
        _clear = new BinButton("清空", "再点确认", true, drag);
        var actions = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,*"),
            ColumnSpacing = WidgetSpace.GapSm,
        };
        Grid.SetColumn(_open, 0);
        Grid.SetColumn(_clear, 1);
        actions.Children.Add(_open);
        actions.Children.Add(_clear);

        _revert = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _revert.Tick += (_, _) =>
        {
            _revert.Stop();
            _clear.SetPending(false);
            Refresh();
        };
        _notice = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _notice.Tick += (_, _) =>
        {
            _notice.Stop();
            Refresh();
        };

        _open.Clicked += RecycleBinService.Open;
        _clear.Clicked += () =>
        {
            if (_snap.Count <= 0)
            {
                return;
            }
            if (!_clear.Pending)
            {
                Log.Info("[BIN] 清空首点, 等待第二击确认");
                _clear.SetPending(true);
                _size.Text = "再点一次确认清空";
                _revert.Stop();
                _revert.Start();
                return;
            }
            _revert.Stop();
            _clear.SetPending(false);
            Log.Info("[BIN] 第二击确认, 执行清空");
            if (RecycleBinService.Empty())
            {
                Refresh();
            }
            else
            {
                _snap = RecycleBinService.Query();
                _size.Text = "清空失败，请重试";
                _size.Foreground = CardInk.Danger();
                _notice.Stop();
                _notice.Start();
            }
        };

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
            Margin = new Thickness(10),
        };
        root.Children.Add(head);
        Grid.SetRow(_glyph, 1);
        root.Children.Add(_glyph);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        Card.Child = root;
        Content = Card;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += (_, _) => Refresh();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        Refresh();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _refreshTimer.Start();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer.Stop();
        _revert.Stop();
        _notice.Stop();
        _clear.SetPending(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        _snap = RecycleBinService.Query();
        if (_snap.Count <= 0)
        {
            _count.Text = "空的";
            _count.FontSize = WidgetType.Body;
            _count.FontWeight = WidgetWeight.Body;
            _unit.IsVisible = false;
            _size.Text = "";
            _revert.Stop();
            _clear.SetPending(false);
            _clear.SetEnabled(false);
        }
        else
        {
            _count.Text = _snap.Count.ToString("N0", CultureInfo.CurrentCulture);
            _count.FontSize = 22;
            _count.FontWeight = WidgetWeight.Display;
            _unit.IsVisible = true;
            _size.Text = _clear.Pending ? "再点一次确认清空" : $"占用 {DataUsageService.FormatBytes(_snap.Bytes)}";
            _clear.SetEnabled(true);
        }
        ApplyPalette();
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _count.Foreground = _snap.Count > 0 ? CardInk.Fg(p) : CardInk.Muted(p);
        _unit.Foreground = CardInk.Muted(p);
        _size.Foreground = CardInk.Faint(p);
        _glyph.Foreground = CardInk.Muted(p);
        _title.Foreground = CardInk.Fg(p);
        _open.Apply(p, CardIsDark);
        _clear.Apply(p, CardIsDark);
    }

    /// <summary>回收站按键</summary>
    private sealed class BinButton : Border
    {
        private readonly TextBlock _label;
        private readonly Func<bool> _drag;
        private readonly string _idleLabel;
        private readonly string? _confirmLabel;
        private readonly bool _danger;
        private WidgetSurfacePalette _p = WidgetSurfacePalette.Resolve(false);
        private bool _dark;
        private bool _hover;
        private bool _pressed;
        private bool _enabled = true;

        public bool Pending { get; private set; }
        public event Action? Clicked;

        public BinButton(string idle, string? confirm, bool danger, Func<bool> drag)
        {
            _idleLabel = idle;
            _confirmLabel = confirm;
            _danger = danger;
            _drag = drag;
            Height = 26;
            CornerRadius = new CornerRadius(WidgetSpace.RadiusInner);
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Cursor = new Cursor(StandardCursorType.Hand);

            _label = new TextBlock
            {
                Text = idle,
                FontSize = WidgetType.Label,
                FontWeight = WidgetWeight.Emphasis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                IsHitTestVisible = false,
            };
            Child = _label;

            PointerCaptureLost += (_, _) =>
            {
                _pressed = false;
                ApplyColors();
            };
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            IsHitTestVisible = enabled;
            if (!enabled)
            {
                Pending = false;
                _label.Text = _idleLabel;
            }
            ApplyColors();
        }

        public void SetPending(bool pending)
        {
            if (_confirmLabel is null)
            {
                return;
            }
            Pending = pending;
            _label.Text = pending ? _confirmLabel : _idleLabel;
            ApplyColors();
        }

        public void Apply(WidgetSurfacePalette p, bool dark)
        {
            _p = p;
            _dark = dark;
            ApplyColors();
        }

        private void ApplyColors()
        {
            var tone = _danger ? CardInk.Danger().Color : CardAccent.Value;
            if (!_enabled)
            {
                Background = new SolidColorBrush(_p.Text, _dark ? 0.05 : 0.04);
                _label.Foreground = CardInk.Muted(_p);
            }
            else if (Pending)
            {
                Background = new SolidColorBrush(tone, 0.14);
                _label.Foreground = new SolidColorBrush(tone);
            }
            else
            {
                Background = new SolidColorBrush(_p.Text, _hover
                    ? _dark ? 0.12 : 0.09
                    : _dark ? 0.07 : 0.05);
                _label.Foreground = _hover ? CardInk.Fg(_p) : CardInk.Muted(_p);
            }
            Opacity = !_enabled ? 0.40 : _pressed ? 0.75 : 1;
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            _hover = true;
            ApplyColors();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hover = false;
            if (!_pressed)
            {
                ApplyColors();
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            // 编辑态交给卡片拖动
            if (_drag() || !_enabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }
            _pressed = true;
            e.Pointer.Capture(this);
            ApplyColors();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_pressed)
            {
                return;
            }
            _pressed = false;
            e.Pointer.Capture(null);
            var local = e.GetPosition(this);
            var inside = local.X >= 0 && local.Y >= 0 &&
                         local.X <= Bounds.Width && local.Y <= Bounds.Height;
            ApplyColors();
            if (inside)
            {
                Clicked?.Invoke();
            }
            e.Handled = true;
        }
    }
}
