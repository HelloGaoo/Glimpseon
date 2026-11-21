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

// ComponentCard
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Glimpseon.Core;

namespace Glimpseon.UI;

public sealed partial class ComponentCard : UserControl
{
    private readonly ComponentDefinition _definition;
    private readonly Action<ComponentCard> _onSelected;
    private readonly Action<ComponentDefinition> _onActivated;
    private Point _pressPoint;
    private Point _touchPressPoint;
    private PointerPressedEventArgs? _pressedArgs;
    private bool _pressed;
    private bool _touchPressed;
    private bool _dragging;
    private bool _previewSet;

    public ComponentCard(
        ComponentDefinition definition,
        string categoryLabel,
        Action<ComponentCard> onSelected,
        Action<ComponentDefinition> onActivated)
    {
        _definition = definition;
        _onSelected = onSelected;
        _onActivated = onActivated;

        InitializeComponent();

        Cursor = new Cursor(StandardCursorType.Hand);
        NameText.Text = definition.DisplayName;
        MetaText.Text = $"{categoryLabel} · {definition.DefaultWidth:0} × {definition.DefaultHeight:0}";
        ToolTip.SetTip(this, string.IsNullOrWhiteSpace(definition.Description)
            ? definition.DisplayName
            : definition.Description);

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, _) => OnPointerReleased();
        PointerCaptureLost += (_, _) =>
        {
            ResetDrag();
            _touchPressed = false;
        };
        DoubleTapped += (_, _) => _onActivated(_definition);
    }

    public ComponentDefinition Definition => _definition;

    public bool HasPreview => _previewSet;

    public void SetPreview(Bitmap? bitmap)
    {
        _previewSet = true;
        PreviewImage.Source = bitmap;
    }

    public void ClearPreview()
    {
        _previewSet = false;
        PreviewImage.Source = null;
    }

    public void SetPlaced(int count)
    {
        var placed = Math.Max(0, count);
        PlacedBadge.IsVisible = placed > 0;
        PlacedText.Text = placed > 1
            ? AppUtils.Tr("component_library.placed_count", ("count", placed))
            : AppUtils.Tr("component_library.placed");
    }

    public void SetSelected(bool selected) => CardRoot.Classes.Set("selected", selected);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        // 触屏选中
        if (e.Pointer.Type != PointerType.Mouse)
        {
            _touchPressed = true;
            _touchPressPoint = e.GetPosition(this);
            return;
        }
        _pressPoint = e.GetPosition(this);
        _pressed = true;
        _dragging = false;
        _pressedArgs = e;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_touchPressed)
        {
            var touchPos = e.GetPosition(this);
            if (Math.Abs(touchPos.X - _touchPressPoint.X) > 8 ||
                Math.Abs(touchPos.Y - _touchPressPoint.Y) > 8)
            {
                _touchPressed = false;
            }
            return;
        }
        if (!_pressed || _dragging || _pressedArgs is null)
        {
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetDrag();
            return;
        }
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _pressPoint.X) < 8 && Math.Abs(pos.Y - _pressPoint.Y) < 8)
        {
            return;
        }
        _dragging = true;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(_definition.Id));
        try
        {
            await DragDrop.DoDragDropAsync(_pressedArgs, transfer, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Log.Warning($"[组件库] 拖拽失败 {_definition.Id}: {ex.Message}");
        }
        finally
        {
            ResetDrag();
        }
    }

    private void OnPointerReleased()
    {
        var clicked = _touchPressed || (_pressed && !_dragging);
        _touchPressed = false;
        ResetDrag();
        if (clicked)
        {
            _onSelected(this);
        }
    }

    private void ResetDrag()
    {
        _pressed = false;
        _dragging = false;
        _pressedArgs = null;
    }
}
