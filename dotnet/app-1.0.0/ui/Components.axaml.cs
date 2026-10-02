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

// 组件系统
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using Glimpseon.UI.Views;

namespace Glimpseon.UI;

public class DraggableContainer : ContentControl
{
    private bool _draggable;
    private bool _pressed;
    private Point _pressPoint;
    private Point _startPosition;
    private double _dpi = 100;

    public string ComponentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Style { get; set; } = "";

    public event Action<string>? Selected;

    public bool IsDraggable
    {
        get => _draggable;
        set
        {
            _draggable = value;
            if (value)
            {
                ZIndex = 100;
            }
        }
    }

    public double DpiScale
    {
        get => _dpi;
        set
        {
            _dpi = Math.Clamp(value, 1, 300);
            RenderTransform = new ScaleTransform(_dpi / 100.0, _dpi / 100.0);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_draggable || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _pressed = true;
        _pressPoint = e.GetPosition(Parent as Visual);
        _startPosition = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
        e.Pointer.Capture(this);
        Selected?.Invoke(ComponentId);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pressed)
        {
            return;
        }
        var pos = e.GetPosition(Parent as Visual);
        var dx = pos.X - _pressPoint.X;
        var dy = pos.Y - _pressPoint.Y;
        SetCanvasPosition(_startPosition.X + dx, _startPosition.Y + dy);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pressed = false;
        e.Pointer.Capture(null);
    }

    public void SetCanvasPosition(double x, double y)
    {
        if (Parent is not Panel parent)
        {
            return;
        }
        var maxX = Math.Max(0, parent.Bounds.Width - Width);
        var maxY = Math.Max(0, parent.Bounds.Height - Height);
        Canvas.SetLeft(this, Math.Clamp(x, 0, maxX));
        Canvas.SetTop(this, Math.Clamp(y, 0, maxY));
    }

    public (double X, double Y) GetPositionPercent()
    {
        if (Parent is not Panel parent || parent.Bounds.Width <= 0 || parent.Bounds.Height <= 0)
        {
            return (0.5, 0.5);
        }
        var availableW = parent.Bounds.Width - Width;
        var availableH = parent.Bounds.Height - Height;
        if (availableW <= 0 || availableH <= 0)
        {
            return (0.5, 0.5);
        }
        return (Math.Clamp(Canvas.GetLeft(this) / availableW, 0, 1), Math.Clamp(Canvas.GetTop(this) / availableH, 0, 1));
    }

    public void SetPositionPercent(double x, double y)
    {
        if (Parent is not Panel parent)
        {
            return;
        }
        var availableW = Math.Max(1, parent.Bounds.Width - Width);
        var availableH = Math.Max(1, parent.Bounds.Height - Height);
        SetCanvasPosition(x * availableW, y * availableH);
    }

    public void OnParentResize() => SetPositionPercent(GetPositionPercent().X, GetPositionPercent().Y);
}

public sealed class PlaceholderWidget : DraggableContainer
{
    public PlaceholderWidget(ComponentDefinition definition, JsonElement? config)
    {
        Width = definition.DefaultWidthCells * 110.0;
        Height = definition.DefaultHeightCells * 110.0;

        var card = new Border
        {
            CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value)),
            Background = new SolidColorBrush(Colors.White, 0.55),
            BorderBrush = new SolidColorBrush(Colors.Black, 0.06),
            BorderThickness = new Thickness(1),
        };
        BindOpacity();

        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
        };
        content.Children.Add(new FASymbolIcon
        {
            Symbol = FASymbol.Home,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = definition.DisplayName,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = "M6",
            FontSize = 10,
            Opacity = 0.4,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        Content = new Panel { Children = { card, content } };
        _card = card;
    }

    private readonly Border _card;

    private void BindOpacity()
    {
        void Apply()
        {
            _card.Background = new SolidColorBrush(Colors.White, Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1));
        }
        Apply();
        Config.ComponentCardOpacity.ValueChanged += _ => Apply();
        Config.ComponentCardRadius.ValueChanged += _ => _card.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));
    }
}

public sealed class ComponentData
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Style { get; set; } = "";
    public double PosX { get; set; } = 0.5;
    public double PosY { get; set; } = 0.5;
    public double Width { get; set; }
    public double Height { get; set; }
    public double Scale { get; set; } = 100;
    public bool Enabled { get; set; } = true;
    public int PageIndex { get; set; }
    public JsonElement? Config { get; set; }
}

public sealed class ComponentManager
{
    private const int MaxComponents = 100;

    private readonly HomeView _home;
    public Dictionary<string, DraggableContainer> Components { get; } = new();
    private readonly Dictionary<string, ComponentData> _componentData = new();

    public ComponentManager(HomeView home)
    {
        _home = home;
    }

    private PageManager? PageManager => _home.PageManager;

    public void LoadComponents()
    {
        var pm = PageManager;
        if (pm is null)
        {
            Log.Warning("[ComponentManager] PageManager 未初始化");
            return;
        }
        var total = 0;
        for (var pageIndex = 0; pageIndex < pm.Count; pageIndex++)
        {
            var meta = pm.GetPage(pageIndex);
            if (meta is not { Type: "info" })
            {
                continue;
            }
            foreach (var compElement in meta.Components)
            {
                if (total >= MaxComponents)
                {
                    break;
                }
                try
                {
                    var obj = compElement.ValueKind == JsonValueKind.Object ? compElement : default;
                    var id = obj.ValueKind != default && obj.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var type = obj.ValueKind != default && obj.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                    var style = obj.ValueKind != default && obj.TryGetProperty("style", out var styleEl) ? styleEl.GetString() : null;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(style))
                    {
                        continue;
                    }
                    var data = new ComponentData
                    {
                        Id = id!,
                        Type = type!,
                        Style = style!,
                        PageIndex = pageIndex,
                        PosX = obj.TryGetProperty("position", out var pos) && pos.TryGetProperty("x", out var px) ? px.GetDouble() : 0.5,
                        PosY = obj.TryGetProperty("position", out var pos2) && pos2.TryGetProperty("y", out var py) ? py.GetDouble() : 0.5,
                        Width = obj.TryGetProperty("size", out var size) && size.TryGetProperty("w", out var w) ? w.GetDouble() : 220,
                        Height = obj.TryGetProperty("size", out var size2) && size2.TryGetProperty("h", out var h) ? h.GetDouble() : 220,
                        Scale = obj.TryGetProperty("scale", out var scale) ? scale.GetDouble() : 100,
                        Enabled = obj.TryGetProperty("enabled", out var en) ? (!en.ValueKind.Equals(JsonValueKind.False)) : true,
                        Config = obj.TryGetProperty("config", out var cfg) ? cfg.Clone() : null,
                    };
                    var instance = CreateWidget(data);
                    if (instance is null)
                    {
                        continue;
                    }
                    instance.IsVisible = false;
                    Components[id!] = instance;
                    _componentData[id!] = data;
                    total++;
                    Log.Info($"加载组件: {id} ({type}/{style}) page={pageIndex}");
                }
                catch (Exception e)
                {
                    Log.Error($"创建组件失败: {e.Message}");
                }
            }
        }
        Log.Info($"[ComponentManager] 共加载{total}个组件");
    }

    private DraggableContainer? CreateWidget(ComponentData data)
    {
        var definition = _home.ComponentRegistry.GetDefinition($"{data.Type}_{data.Style}");
        if (definition is null)
        {
            Log.Warning($"组件定义未注册: {data.Type}_{data.Style}");
            return null;
        }
        var widget = new PlaceholderWidget(definition, data.Config)
        {
            ComponentId = data.Id,
            Type = data.Type,
            Style = data.Style,
            DpiScale = data.Scale,
        };
        var parent = _home.GetInfoPagePanel(data.PageIndex);
        if (parent is null)
        {
            Log.Warning($"组件宿主页不存在: page={data.PageIndex}");
            return null;
        }
        parent.Children.Add(widget);
        Canvas.SetLeft(widget, data.PosX);
        Canvas.SetTop(widget, data.PosY);
        return widget;
    }

    public void SaveComponents()
    {
        var pm = PageManager;
        if (pm is null)
        {
            return;
        }
        var pageComponents = new Dictionary<int, List<JsonElement>>();
        foreach (var (compId, instance) in Components)
        {
            if (!_componentData.TryGetValue(compId, out var stored))
            {
                continue;
            }
            var (x, y) = instance.GetPositionPercent();
            stored.PosX = x;
            stored.PosY = y;
            stored.Width = instance.Width;
            stored.Height = instance.Height;
            stored.Scale = instance.DpiScale;
            var obj = new JsonObject
            {
                ["id"] = stored.Id,
                ["type"] = stored.Type,
                ["style"] = stored.Style,
                ["position"] = new JsonObject { ["x"] = x, ["y"] = y },
                ["size"] = new JsonObject { ["w"] = instance.Width, ["h"] = instance.Height },
                ["scale"] = stored.Scale,
                ["enabled"] = stored.Enabled,
                ["page_index"] = stored.PageIndex,
                ["config"] = stored.Config is { } cfg ? JsonNode.Parse(cfg.GetRawText()) : new JsonObject(),
            };
            var list = pageComponents.TryGetValue(stored.PageIndex, out var l) ? l : pageComponents[stored.PageIndex] = new List<JsonElement>();
            list.Add(JsonSerializer.Deserialize<JsonElement>(obj.ToJsonString()));
        }

        for (var i = 0; i < pm.Count; i++)
        {
            var meta = pm.GetPage(i);
            if (meta is { Type: "info" })
            {
                meta.Components = pageComponents.TryGetValue(i, out var list) ? list : new List<JsonElement>();
            }
        }
        Log.Debug($"[CM] 保存明细: {Components.Count}个组件");
        pm.Save();
        Log.Info("[ComponentManager] 组件已保存");
    }

    public string? AddComponent(string compType, string compStyle, int pageIndex)
    {
        if (Components.Count >= MaxComponents)
        {
            Log.Warning($"组件数量上限: {MaxComponents}");
            return null;
        }
        var existingIds = Components.Keys.ToHashSet();
        var counter = 1;
        while (existingIds.Contains($"comp_{compType}_{counter}"))
        {
            counter++;
        }
        var compId = $"comp_{compType}_{counter}";
        var data = new ComponentData
        {
            Id = compId,
            Type = compType,
            Style = compStyle,
            PageIndex = pageIndex,
        };
        var instance = CreateWidget(data);
        if (instance is null)
        {
            return null;
        }
        instance.IsVisible = true;
        instance.SetPositionPercent(0.5, 0.5);
        Components[compId] = instance;
        _componentData[compId] = data;
        SaveComponents();
        Log.Info($"添加组件: {compId} ({compType}/{compStyle}) page={pageIndex}");
        return compId;
    }

    public void RemoveComponent(string compId)
    {
        if (!Components.TryGetValue(compId, out var instance))
        {
            return;
        }
        (instance.Parent as Panel)?.Children.Remove(instance);
        Components.Remove(compId);
        _componentData.Remove(compId);
        SaveComponents();
        Log.Info($"删除组件: {compId}");
    }

    public List<DraggableContainer> GetAllContainers() => Components.Values.ToList();

    public int GetComponentPage(string compId) => _componentData.TryGetValue(compId, out var d) ? d.PageIndex : 0;

    public void SetComponentPage(string compId, int pageIndex)
    {
        if (!_componentData.TryGetValue(compId, out var data))
        {
            return;
        }
        data.PageIndex = pageIndex;
        if (Components.TryGetValue(compId, out var instance))
        {
            var newParent = _home.GetInfoPagePanel(pageIndex);
            if (instance.Parent is Panel old && newParent is not null)
            {
                old.Children.Remove(instance);
                newParent.Children.Add(instance);
            }
        }
        Log.Info($"[CM] 组件 {compId} 归属页改为 {pageIndex}");
        SaveComponents();
    }

    public void ShiftPagesAfterDelete(int deletedIndex, int fallbackIndex)
    {
        var moved = 0;
        foreach (var stored in _componentData.Values)
        {
            if (stored.PageIndex == deletedIndex)
            {
                stored.PageIndex = fallbackIndex;
                moved++;
            }
            else if (stored.PageIndex > deletedIndex)
            {
                stored.PageIndex--;
                moved++;
            }
        }
        if (moved > 0)
        {
            Log.Info($"[CM] 删除页面 {deletedIndex} 后迁移{moved}个组件至页 {fallbackIndex}");
        }
        SaveComponents();
    }
}

public sealed class ComponentLibraryWindow : Window
{
    private const string DragFormat = "application/x-Glimpseon-component";

    private readonly ComponentRegistry _registry;
    private readonly ListBox _categoryList = new();
    private readonly ListBox _definitionList = new();
    private Point _pressPoint;
    private string? _draggingDefinitionId;
    private bool _dragStarted;
    private PointerPressedEventArgs? _pressedArgs;

    public ComponentLibraryWindow(ComponentRegistry registry)
    {
        _registry = registry;
        Title = AppUtils.Tr("component_library.title");
        Width = 520;
        Height = 460;
        MinWidth = 420;
        MinHeight = 360;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        _categoryList.Margin = new Thickness(8);
        _categoryList.SelectionChanged += OnCategoryChanged;
        _definitionList.Margin = new Thickness(8);
        _definitionList.PointerPressed += OnListPointerPressed;
        _definitionList.PointerMoved += OnListPointerMoved;

        Grid.SetColumn(_categoryList, 0);
        Grid.SetColumn(_definitionList, 1);
        root.Children.Add(_categoryList);
        root.Children.Add(_definitionList);
        Content = root;

        foreach (var category in _registry.GetCategories())
        {
            _categoryList.Items.Add(category);
        }
        if (_categoryList.ItemCount > 0)
        {
            _categoryList.SelectedIndex = 0;
        }
    }

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_categoryList.SelectedItem is not string category)
        {
            return;
        }
        _definitionList.Items.Clear();
        foreach (var definition in _registry.GetDefinitionsByCategory(category))
        {
            _definitionList.Items.Add(new TextBlock { Text = definition.DisplayName, Margin = new Thickness(4, 6, 4, 6) });
        }
    }

    private ComponentDefinition? GetSelectedDefinition()
    {
        if (_definitionList.SelectedItem is TextBlock { Text: { } name })
        {
            return _registry.GetDefinitionsByCategory(_categoryList.SelectedItem as string ?? "")
                .FirstOrDefault(d => d.DisplayName == name);
        }
        return null;
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressPoint = e.GetPosition(this);
        _dragStarted = false;
        _pressedArgs = e;
        _draggingDefinitionId = GetSelectedDefinition()?.Id;
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStarted || _draggingDefinitionId is null || _pressedArgs is null)
        {
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var pos = e.GetPosition(this);
        const double dragThreshold = 8;
        if (Math.Abs(pos.X - _pressPoint.X) < dragThreshold ||
            Math.Abs(pos.Y - _pressPoint.Y) < dragThreshold)
        {
            return;
        }
        _dragStarted = true;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(_draggingDefinitionId));
        await DragDrop.DoDragDropAsync(_pressedArgs, transfer, DragDropEffects.Copy);
        _draggingDefinitionId = null;
        _pressedArgs = null;
    }
}

public sealed class NavigationPage : Panel
{
    private readonly int _pageIndex;
    private readonly PageManager _pageManager;

    public NavigationPage(int pageIndex, PageManager pageManager)
    {
        _pageIndex = pageIndex;
        _pageManager = pageManager;
        RebuildItems();
    }

    public void RebuildItems()
    {
        Children.Clear();
        foreach (var itemElement in _pageManager.GetPageItems(_pageIndex))
        {
            try
            {
                var obj = itemElement.ValueKind == JsonValueKind.Object ? itemElement : default;
                var name = obj.ValueKind != default && obj.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var path = obj.ValueKind != default && obj.TryGetProperty("path", out var pathEl) ? pathEl.GetString() ?? "" : "";
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Width = 220,
                    Height = 72,
                    Padding = new Thickness(14, 10),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 4,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock { Text = path, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis },
                        },
                    },
                };
                cell.PointerReleased += (_, _) => LaunchItem(path);
                Children.Add(cell);
            }
            catch (Exception e)
            {
                Log.Warning($"[NavigationPage] 导航项解析失败: {e.Message}");
            }
        }
        InvalidateArrange();
    }

    private void LaunchItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            Log.Info($"[NavigationPage] 启动条目: {path}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            Log.Warning($"[NavigationPage] 条目启动失败: {path} {e.Message}");
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Math.Max(1, (int)(availableSize.Width / 240));
        var rows = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(220, 72));
        }
        rows = (Children.Count + columns - 1) / columns;
        return new Size(availableSize.Width, Math.Max(0, rows * 84));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, (int)(finalSize.Width / 240));
        for (var i = 0; i < Children.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            Children[i].Arrange(new Rect(20 + col * 240, 20 + row * 84, 220, 72));
        }
        return finalSize;
    }
}

