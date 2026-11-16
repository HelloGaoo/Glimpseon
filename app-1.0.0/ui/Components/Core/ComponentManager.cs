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

// ComponentManager
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Glimpseon.Core;
using System.IO;


namespace Glimpseon.UI;

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
        // 存档条目数
        var rawCount = 0;
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
                    // style 可为空
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type))
                    {
                        continue;
                    }
                    rawCount++;
                    var data = new ComponentData
                    {
                        Id = id!,
                        Type = type!,
                        Style = style ?? "",
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
                    Log.Error($"创建组件失败: {e}");
                }
            }
        }
        Log.Info($"[ComponentManager] 共加载{total}个组件");
        MigrateCompactLayout(rawCount);
    }

    // 紧凑布局迁移范围
    private static readonly HashSet<string> LayoutMigrationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "system_disk", "system_battery", "system_link", "system_device", "system_volume",
        "system_power", "system_recycle", "clock_flip", "clock_round", "clock_zones",
        "clock_dayprogress", "weather_air", "weather_sun",
        "tools_search", "tools_text", "info_exchange",
        "info_picday",
    };

    // 组件定义键
    private static string DefinitionKey(ComponentData data) =>
        string.IsNullOrEmpty(data.Style) ? data.Type : $"{data.Type}_{data.Style}";

    private void MigrateCompactLayout(int rawCount)
    {
        var flag = Path.Combine(Paths.DataConfig, "component_grid_v5.flag");
        if (File.Exists(flag))
        {
            return;
        }
        try
        {
            var legacy = new List<DraggableContainer>();
            foreach (var (id, widget) in Components)
            {
                if (!_componentData.TryGetValue(id, out var data) ||
                    !LayoutMigrationKeys.Contains(DefinitionKey(data)))
                {
                    legacy.Add(widget);
                }
            }
            var scale400 = MostCommonScale(legacy, 400);
            var scale200 = MostCommonScale(legacy, 200);

            // 下架/重复条目检测
            var changed = rawCount > Components.Count;
            foreach (var (id, widget) in Components)
            {
                if (!_componentData.TryGetValue(id, out var data))
                {
                    continue;
                }
                var defId = DefinitionKey(data);
                if (!LayoutMigrationKeys.Contains(defId))
                {
                    continue;
                }
                var definition = _home.ComponentRegistry.GetDefinition(defId);
                if (definition is null)
                {
                    continue;
                }
                var targetW = definition.DefaultWidth;
                var targetH = definition.DefaultHeight;
                // 同步存档尺寸
                if (Math.Abs(data.Width - targetW) > 0.5 || Math.Abs(data.Height - targetH) > 0.5)
                {
                    data.Width = targetW;
                    data.Height = targetH;
                    changed = true;
                }
                var targetScale = targetW >= 350 ? scale400 : scale200;
                if (targetScale > 0 && Math.Abs(widget.DpiScale - targetScale) > 0.5)
                {
                    widget.DpiScale = targetScale;
                    data.Scale = targetScale;
                    changed = true;
                }
            }
            if (changed)
            {
                // 只保存现存组件
                SaveComponents();
            }
            Directory.CreateDirectory(Paths.DataConfig);
            File.WriteAllText(flag, DateTime.Now.ToString("O"));
            Log.Info($"[CM] 紧凑布局迁移完成 400→{scale400} 200→{scale200}");
        }
        catch (Exception e)
        {
            Log.Warning($"[CM] 紧凑布局迁移失败: {e.Message}");
        }
    }

    // 同宽众数缩放
    private static double MostCommonScale(IEnumerable<DraggableContainer> components, double width)
    {
        var scales = components
            .Where(c => Math.Abs(c.Width - width) < 1)
            .Select(c => Math.Round(c.DpiScale))
            .Where(s => s > 0)
            .ToList();
        return scales.Count == 0 ? 100 : scales.GroupBy(s => s).OrderByDescending(g => g.Count()).First().Key;
    }

    private DraggableContainer? CreateWidget(ComponentData data)
    {
        // 兼容旧存档
        var defId = DefinitionKey(data);
        var definition = _home.ComponentRegistry.GetDefinition(defId)
            ?? _home.ComponentRegistry.GetDefinition(data.Type);
        if (definition is null)
        {
            Log.Warning($"组件定义未注册: {defId}");
            return null;
        }
        // 工厂优先
        var widget = StyleWidgetFactory.Create(defId, definition, data.Config)
            ?? new PlaceholderWidget(definition, data.Config);
        widget.ComponentId = data.Id;
        widget.Type = data.Type;
        widget.Style = data.Style;
        widget.DpiScale = data.Scale;
        var parent = _home.GetInfoPagePanel(data.PageIndex);
        if (parent is null)
        {
            Log.Warning($"组件宿主页不存在: page={data.PageIndex}");
            return null;
        }
        parent.Children.Add(widget);
        // 百分比坐标
        widget.SetPositionPercent(data.PosX, data.PosY);
        // 布局完成后生效
        widget.ApplyPercent();
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

    // 当前页瀑布流
    public void ArrangeCurrentPage(int pageIndex)
    {
        if (_home.GetInfoPagePanel(pageIndex) is not { } parent || parent.Bounds.Width <= 1)
        {
            return;
        }
        var hostW = parent.Bounds.Width;
        var hostH = parent.Bounds.Height;
        var list = Components
            .Where(kv => _componentData.TryGetValue(kv.Key, out var d) && d.PageIndex == pageIndex && d.Enabled)
            .Select(kv => kv.Value)
            .OrderBy(c =>
            {
                var t = Canvas.GetTop(c);
                return double.IsNaN(t) ? 0 : t;
            })
            .ThenBy(c =>
            {
                var l = Canvas.GetLeft(c);
                return double.IsNaN(l) ? 0 : l;
            })
            .ToList();
        if (list.Count == 0)
        {
            return;
        }

        const double inset = 16, gap = 16, bottomReserve = 96;
        var availableH = Math.Max(200, hostH - inset * 2 - bottomReserve);
        var maxVisualW = list.Max(c => Math.Max(1, c.VisualWidth));

        // 枚举列数取最优
        var bestCols = FallbackColumns(hostW, inset, gap);
        var bestColumnW = (hostW - inset * 2 - gap * (bestCols - 1)) / bestCols;
        var bestShrink = 1.0;
        var bestScore = 0.0;
        for (var cols = 2; cols <= 8; cols++)
        {
            var cw = (hostW - inset * 2 - gap * (cols - 1)) / cols;
            if (cw < 120)
            {
                break;
            }
            var hs = new double[cols];
            for (var i = 0; i < cols; i++)
            {
                hs[i] = inset;
            }
            foreach (var c in list)
            {
                var fit = c.VisualWidth > cw ? cw / c.VisualWidth : 1.0;
                var h = c.VisualHeight * fit;
                var column = 0;
                for (var i = 1; i < cols; i++)
                {
                    if (hs[i] < hs[column])
                    {
                        column = i;
                    }
                }
                hs[column] += h + gap;
            }
            var totalH = hs.Max() - gap;
            var shrink = totalH > 0 ? Math.Min(1.0, availableH / totalH) : 1.0;
            var widthScale = Math.Min(1.0, cw / maxVisualW);
            var score = shrink * widthScale;
            if (score > bestScore)
            {
                bestScore = score;
                bestCols = cols;
                bestColumnW = cw;
                bestShrink = shrink;
            }
        }

        foreach (var c in list)
        {
            var fit = c.VisualWidth > bestColumnW ? bestColumnW / c.VisualWidth : 1.0;
            c.DpiScale = Math.Max(1, Math.Round(c.DpiScale * fit * bestShrink));
        }

        var columns = bestCols;
        var columnW = bestColumnW;
        var heights = new double[columns];
        for (var i = 0; i < columns; i++)
        {
            heights[i] = inset;
        }
        foreach (var c in list)
        {
            var column = 0;
            for (var i = 1; i < columns; i++)
            {
                if (heights[i] < heights[column])
                {
                    column = i;
                }
            }
            var x = inset + column * (columnW + gap) + Math.Max(0, (columnW - c.VisualWidth) / 2);
            var y = heights[column];
            c.SetCanvasPosition(x, y);
            heights[column] = y + c.VisualHeight + gap;
        }

        SaveComponents();
        Log.Info($"[CM] 布局整理 页={pageIndex} 组件={list.Count} 列={columns} 缩放={bestShrink:F2}");

        static int FallbackColumns(double hostW, double inset, double gap)
        {
            var cols = Math.Clamp((int)Math.Round(hostW / 380.0), 2, 6);
            var columnW = (hostW - inset * 2 - gap * (cols - 1)) / cols;
            return columnW < 120 ? 2 : cols;
        }
    }

    // 组件配置
    public JsonObject GetComponentConfig(string compId)
    {
        if (_componentData.TryGetValue(compId, out var d) && d.Config is { ValueKind: JsonValueKind.Object } cfg)
        {
            return JsonNode.Parse(cfg.GetRawText())?.AsObject() ?? new JsonObject();
        }
        return new JsonObject();
    }

    public string GetDefinitionKey(string compId) =>
        _componentData.TryGetValue(compId, out var d) ? DefinitionKey(d) : "";

    // 写配置并重建
    public void UpdateComponentConfig(string compId, JsonObject config)
    {
        if (!_componentData.TryGetValue(compId, out var data) || !Components.TryGetValue(compId, out var old))
        {
            return;
        }
        data.Config = JsonSerializer.Deserialize<JsonElement>(config.ToJsonString());
        var (px, py) = old.GetPositionPercent();
        var scale = old.DpiScale;
        // 先建后删
        var fresh = CreateWidget(data);
        if (fresh is null)
        {
            Log.Warning($"[CM] 配置重建失败: {compId}");
            return;
        }
        (old.Parent as Panel)?.Children.Remove(old);
        Components[compId] = fresh;
        fresh.SetPositionPercent(px, py);
        fresh.ApplyPercent();
        fresh.DpiScale = scale;
        _home.AfterComponentRebuilt(fresh);
        SaveComponents();
        Log.Info($"[CM] 组件配置已更新: {compId}");
    }

    // 换肤重建
    public void RebuildAllForAppearance()
    {
        var rebuilt = 0;
        foreach (var compId in Components.Keys.ToList())
        {
            if (!_componentData.TryGetValue(compId, out var data) || !Components.TryGetValue(compId, out var old))
            {
                continue;
            }
            try
            {
                var (px, py) = old.GetPositionPercent();
                var scale = old.DpiScale;
                var visible = old.IsVisible;
                // 先建后删
                var fresh = CreateWidget(data);
                if (fresh is null)
                {
                    Log.Warning($"[CM] 外观重建跳过 {compId}: 工厂返回空");
                    continue;
                }
                (old.Parent as Panel)?.Children.Remove(old);
                Components[compId] = fresh;
                fresh.SetPositionPercent(px, py);
                fresh.ApplyPercent();
                fresh.DpiScale = scale;
                fresh.IsVisible = visible;
                _home.AfterComponentRebuilt(fresh);
                rebuilt++;
            }
            catch (Exception e)
            {
                Log.Error($"[CM] 外观重建失败 {compId}: {e.GetType().Name}: {e.Message}");
            }
        }
        Log.Info($"[CM] 换肤重建 {rebuilt} 个组件");
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
        var defId = string.IsNullOrEmpty(compStyle) ? compType : $"{compType}_{compStyle}";
        var definition = _home.ComponentRegistry.GetDefinition(defId);
        var data = new ComponentData
        {
            Id = compId,
            Type = compType,
            Style = compStyle,
            PageIndex = pageIndex,
            // 对齐默认配置
            Config = definition?.DefaultConfig,
        };
        var instance = CreateWidget(data);
        if (instance is null)
        {
            return null;
        }
        instance.IsVisible = true;
        // 同宽众数缩放
        instance.DpiScale = MostCommonScale(Components.Values, instance.Width);
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

    public bool IsComponentEnabled(string compId) =>
        !_componentData.TryGetValue(compId, out var d) || d.Enabled;

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
}
