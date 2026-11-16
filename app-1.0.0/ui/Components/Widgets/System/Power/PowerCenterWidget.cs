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

// PowerCenterWidget
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
/// 电源中心
/// 400x100: 锁屏/睡眠/重启/关机
/// 重启/关机需二次确认
/// </summary>
public sealed class PowerCenterWidget : WidgetCardBase
{
    private readonly PowerKey _lock;
    private readonly PowerKey _sleep;
    private readonly PowerKey _restart;
    private readonly PowerKey _shutdown;
    private readonly PowerKey[] _keys;
    private readonly TextBlock _title;
    private readonly DispatcherTimer _revert;

    public PowerCenterWidget(ComponentDefinition definition) : base(definition)
    {
        _title = CardChrome.Title("电源中心");
        Func<bool> drag = () => IsDraggable;

        _lock = new PowerKey(AppIcons.Lock, "锁屏", null, false, drag);
        _sleep = new PowerKey(AppIcons.Moon, "睡眠", null, false, drag);
        _restart = new PowerKey(AppIcons.Sync, "重启", "再点重启", false, drag);
        _shutdown = new PowerKey(AppIcons.Power, "关机", "再点关机", true, drag);
        _keys = new[] { _lock, _sleep, _restart, _shutdown };

        var head = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = WidgetSpace.Gap,
        };
        head.Children.Add(_title);

        var bar = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,*,*,*"),
            ColumnSpacing = WidgetSpace.GapSm,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        for (var i = 0; i < _keys.Length; i++)
        {
            Grid.SetColumn(_keys[i], i);
            bar.Children.Add(_keys[i]);
        }

        _lock.Clicked += PowerActionService.Lock;
        _sleep.Clicked += PowerActionService.Sleep;
        _restart.Clicked += () => Handle(_restart, PowerActionService.Restart);
        _shutdown.Clicked += () => Handle(_shutdown, PowerActionService.Shutdown);

        _revert = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _revert.Tick += (_, _) =>
        {
            _revert.Stop();
            ClearPending();
        };

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapXs,
            Margin = new Thickness(10),
        };
        root.Children.Add(head);
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);
        Card.Child = root;
        Content = Card;

        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        ApplyPalette();
    }

    private void Handle(PowerKey key, Action run)
    {
        if (key.ConfirmLabel is null)
        {
            run();
            return;
        }
        if (!key.Pending)
        {
            foreach (var other in _keys)
            {
                if (!ReferenceEquals(other, key))
                {
                    other.SetPending(false);
                }
            }
            key.SetPending(true);
            _revert.Stop();
            _revert.Start();
            return;
        }
        _revert.Stop();
        key.SetPending(false);
        run();
    }

    private void ClearPending()
    {
        foreach (var key in _keys)
        {
            key.SetPending(false);
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _revert.Stop();
        ClearPending();
        base.OnDetachedFromVisualTree(e);
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _title.Foreground = CardInk.Fg(p);
        foreach (var key in _keys)
        {
            key.Apply(p, CardAccent.Value, CardIsDark);
        }
    }

    /// <summary>电源键</summary>
    private sealed class PowerKey : Border
    {
        private readonly TextBlock _glyph;
        private readonly TextBlock _label;
        private readonly Func<bool> _drag;
        private readonly bool _danger;
        private readonly string _idleLabel;
        public string? ConfirmLabel { get; }
        private WidgetSurfacePalette _p = WidgetSurfacePalette.Resolve(false);
        private Color _accent = Colors.Gray;
        private bool _dark;
        private bool _hover;
        private bool _pressed;

        public bool Pending { get; private set; }
        public event Action? Clicked;

        public PowerKey(string glyph, string idle, string? confirm, bool danger, Func<bool> drag)
        {
            _drag = drag;
            _danger = danger;
            _idleLabel = idle;
            ConfirmLabel = confirm;
            Height = 50;
            CornerRadius = new CornerRadius(WidgetSpace.RadiusInner);
            BorderThickness = new Thickness(1);
            BorderBrush = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);

            _glyph = new TextBlock
            {
                Text = glyph,
                FontFamily = AppIcons.Family,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            _label = new TextBlock
            {
                Text = idle,
                FontSize = 11,
                FontWeight = WidgetWeight.Emphasis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                IsHitTestVisible = false,
            };
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _glyph, _label },
            };

            PointerCaptureLost += (_, _) =>
            {
                _pressed = false;
                ApplyColors();
            };
        }

        public void SetPending(bool pending)
        {
            if (ConfirmLabel is null)
            {
                return;
            }
            Pending = pending;
            _label.Text = pending ? ConfirmLabel : _idleLabel;
            ApplyColors();
        }

        public void Apply(WidgetSurfacePalette p, Color accent, bool dark)
        {
            _p = p;
            _accent = accent;
            _dark = dark;
            ApplyColors();
        }

        private void ApplyColors()
        {
            var tone = _danger ? CardInk.Danger().Color : _accent;
            if (Pending)
            {
                Background = new SolidColorBrush(tone, 0.13);
                BorderBrush = new SolidColorBrush(tone, 0.90);
                _glyph.Foreground = new SolidColorBrush(tone);
                _label.Foreground = new SolidColorBrush(tone);
            }
            else
            {
                Background = new SolidColorBrush(_p.Text, _hover
                    ? _dark ? 0.13 : 0.09
                    : _dark ? 0.075 : 0.055);
                BorderBrush = Brushes.Transparent;
                _glyph.Foreground = _hover ? CardInk.Fg(_p) : CardInk.Muted(_p);
                _label.Foreground = _hover ? CardInk.Fg(_p) : CardInk.Muted(_p);
            }
            Opacity = _pressed ? 0.75 : 1;
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
            // 编辑态让位拖动
            if (_drag() || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
