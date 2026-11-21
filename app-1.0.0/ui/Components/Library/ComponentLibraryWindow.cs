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

// ComponentLibraryWindow
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;

namespace Glimpseon.UI;

public sealed partial class ComponentLibraryWindow : GlimpseonWindow
{
    private const string AllCategory = ComponentRegistry.AllCategory;
    private static readonly IReadOnlyDictionary<string, int> NoCounts = new Dictionary<string, int>();

    private readonly ComponentRegistry _registry;
    private readonly Func<IReadOnlyDictionary<string, int>>? _placedCounts;
    private readonly Dictionary<string, ComponentCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _previewing = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComponentPreviewRenderer _previews = new();
    private CancellationTokenSource? _previewCts;
    private IReadOnlyDictionary<string, int> _counts = NoCounts;
    private ComponentDefinition? _selected;
    private string _selectedCategory = AllCategory;
    private ThemeVariant? _theme;
    private int _detailVersion;
    private bool _ready;
    private bool _opened;
    private Bitmap? _windowBitmap;

    public Action<string>? DefinitionActivated;

    public ComponentLibraryWindow(ComponentRegistry registry,
        Func<IReadOnlyDictionary<string, int>>? placedCounts = null)
    {
        _registry = registry;
        _placedCounts = placedCounts;

        InitializeComponent();

        Title = AppUtils.Tr("component_library.title");
        TitleLabel.Text = Title;
        VersionText.Text = Paths.Version;
        SearchBox.PlaceholderText = AppUtils.Tr("component_library.search_placeholder");
        AddButton.Content = AppUtils.Tr("component_library.add");
        DetailHint.Text = AppUtils.Tr("component_library.drag_hint");
        DetailEmptyText.Text = AppUtils.Tr("component_library.select_hint");
        MetaCategoryLabel.Text = AppUtils.Tr("component_library.category");
        MetaSizeLabel.Text = AppUtils.Tr("component_library.size");
        MetaIdLabel.Text = AppUtils.Tr("component_library.component_id");
        LoadWindowIcon();

        BuildNav();
        BuildCards();
        _ready = true;
        _theme = ActualThemeVariant;
        ApplyFilter();
        RefreshPlaced();

        SearchBox.TextChanged += (_, _) =>
        {
            if (_ready)
            {
                ApplyFilter();
            }
        };
        AddButton.Click += (_, _) =>
        {
            if (_selected is not null)
            {
                Activate(_selected);
            }
        };
        Opened += (_, _) =>
        {
            _opened = true;
            RefreshPlaced();
            _ = FillPreviewsAsync();
        };
        Activated += (_, _) =>
        {
            if (_ready)
            {
                RefreshPlaced();
            }
        };
        Closed += (_, _) =>
        {
            _opened = false;
            CancelPreviews();
            _previews.Dispose();
            _windowBitmap?.Dispose();
        };
        ActualThemeVariantChanged += (_, _) => OnThemeChanged();
    }

    public void RefreshPlaced()
    {
        _counts = GetPlacedCounts();
        foreach (var (id, card) in _cards)
        {
            card.SetPlaced(_counts.GetValueOrDefault(id));
        }
        UpdateDetailPlaced();
    }

    private void LoadWindowIcon()
    {
        try
        {
            var path = Paths.GetResourcePath(Constants.AppIcon);
            if (!File.Exists(path))
            {
                return;
            }
            _windowBitmap = new Bitmap(path);
            Icon = _windowBitmap;
            WindowIcon.Source = _windowBitmap;
        }
        catch (Exception e)
        {
            Log.Warning($"[组件库] 窗口图标加载失败: {e.Message}");
        }
    }

    private void BuildNav()
    {
        var allItem = CreateNavItem(AllCategory, AppUtils.Tr("component_library.all_components"),
            AppIcons.Library, _registry.Count);
        Nav.MenuItems.Add(allItem);
        var counts = _registry.GetCategoryCounts();
        foreach (var category in _registry.GetCategoryInfos())
        {
            Nav.MenuItems.Add(CreateNavItem(category.Key, CategoryText(category),
                CategoryGlyph(category.Icon), counts.GetValueOrDefault(category.Key)));
        }
        Nav.SelectedItem = allItem;
    }

    private static FANavigationViewItem CreateNavItem(string key, string label, string glyph, int count)
    {
        var icon = new FAFontIconSource
        {
            FontFamily = FAIcons.SymbolsFont,
            Glyph = glyph,
            FontSize = 20,
        };
        var item = new FANavigationViewItem
        {
            Content = label,
            Tag = key,
            IconSource = icon,
        };
        if (count > 0)
        {
            item.InfoBadge = new FAInfoBadge { Value = count };
        }
        return item;
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (!_ready || e.SelectedItem is not FANavigationViewItem { Tag: string key })
        {
            return;
        }
        _selectedCategory = key;
        ApplyFilter();
        CardScroll.Offset = new Vector(0, 0);
    }

    private void BuildCards()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in _registry.GetCategoryInfos())
        {
            labels[category.Key] = CategoryText(category);
        }
        foreach (var definition in _registry.All)
        {
            var label = labels.GetValueOrDefault(definition.Category, definition.Category);
            _cards[definition.Id] = new ComponentCard(definition, label, OnCardSelected, Activate);
        }
    }

    private void ApplyFilter()
    {
        if (!_ready)
        {
            return;
        }
        var definitions = _registry.Search(SearchBox.Text, _selectedCategory);
        CardPanel.Children.Clear();
        foreach (var definition in definitions)
        {
            if (!_cards.TryGetValue(definition.Id, out var card))
            {
                continue;
            }
            CardPanel.Children.Add(card);
            if (_opened && !card.HasPreview)
            {
                var target = card;
                Dispatcher.UIThread.Post(() => _ = FillCardPreviewAsync(target), DispatcherPriority.Background);
            }
        }
        UpdateHeader(definitions.Count);
        EmptyState.IsVisible = definitions.Count == 0;
        EmptyText.Text = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? AppUtils.Tr("component_library.no_components")
            : AppUtils.Tr("component_library.no_result");
        if (_selected is not null && definitions.All(d => d.Id != _selected.Id))
        {
            ShowDetail(null);
        }
    }

    private void UpdateHeader(int count)
    {
        CategoryTitle.Text = _selectedCategory == AllCategory
            ? AppUtils.Tr("component_library.all_components")
            : CategoryLabel(_selectedCategory);
        CategorySubtitle.Text = AppUtils.Tr("component_library.result_count", ("count", count));
    }

    private void OnCardSelected(ComponentCard card) => ShowDetail(card.Definition);

    private void Activate(ComponentDefinition definition)
    {
        DefinitionActivated?.Invoke(definition.Id);
        RefreshPlaced();
    }

    private async Task FillPreviewsAsync()
    {
        CancelPreviews();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        foreach (var card in _cards.Values.ToList())
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }
            if (TopLevel.GetTopLevel(card) is null || card.HasPreview)
            {
                continue;
            }
            try
            {
                await Task.Delay(12, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (cts.IsCancellationRequested)
            {
                return;
            }
            await FillCardPreviewAsync(card);
        }
    }

    private async Task FillCardPreviewAsync(ComponentCard card)
    {
        if (!_opened || card.HasPreview || !_previewing.Add(card.Definition.Id))
        {
            return;
        }
        try
        {
            var bitmap = await _previews.GetAsync(card.Definition);
            if (bitmap is not null)
            {
                card.SetPreview(bitmap);
            }
        }
        finally
        {
            _previewing.Remove(card.Definition.Id);
        }
    }

    private void CancelPreviews()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
    }

    private void OnThemeChanged()
    {
        if (!_ready || ActualThemeVariant == _theme)
        {
            return;
        }
        _theme = ActualThemeVariant;
        DetailPreviewImage.Source = null;
        foreach (var card in _cards.Values)
        {
            card.ClearPreview();
        }
        _previewing.Clear();
        _previews.Clear();
        if (_selected is not null)
        {
            ShowDetail(_selected);
        }
        if (_opened)
        {
            _ = FillPreviewsAsync();
        }
    }

    private async void ShowDetail(ComponentDefinition? definition)
    {
        _selected = definition;
        var version = ++_detailVersion;
        var hasDetail = definition is not null;
        ContentGrid.ColumnDefinitions[1].Width = hasDetail ? new GridLength(300) : new GridLength(0);
        ContentGrid.ColumnSpacing = hasDetail ? 20 : 0;
        DetailColumn.IsVisible = hasDetail;
        foreach (var card in _cards.Values)
        {
            card.SetSelected(ReferenceEquals(card.Definition, definition));
        }
        DetailBody.IsVisible = definition is not null;
        DetailEmpty.IsVisible = definition is null;
        DetailPreviewImage.Source = null;
        if (definition is null)
        {
            return;
        }

        DetailName.Text = definition.DisplayName;
        DetailDesc.Text = definition.Description;
        DetailDesc.IsVisible = !string.IsNullOrWhiteSpace(definition.Description);
        MetaCategoryValue.Text = CategoryLabel(definition.Category);
        MetaSizeValue.Text = $"{definition.DefaultWidth:0} × {definition.DefaultHeight:0}";
        MetaIdValue.Text = definition.Id;
        var aspect = definition.DefaultWidth / Math.Max(1.0, definition.DefaultHeight);
        DetailPreviewHost.Height = aspect < 0.8 ? 300 : aspect < 1.25 ? 220 : 176;
        UpdateDetailPlaced();
        var bitmap = await _previews.GetAsync(definition);
        if (version == _detailVersion && ReferenceEquals(_selected, definition))
        {
            DetailPreviewImage.Source = bitmap;
        }
    }

    private void UpdateDetailPlaced()
    {
        if (_selected is null)
        {
            return;
        }
        var count = _counts.GetValueOrDefault(_selected.Id);
        DetailPlaced.Text = count > 0
            ? AppUtils.Tr("component_library.placed_count", ("count", count))
            : AppUtils.Tr("component_library.no_placed");
    }

    private IReadOnlyDictionary<string, int> GetPlacedCounts()
    {
        try
        {
            return _placedCounts?.Invoke() ?? NoCounts;
        }
        catch (Exception e)
        {
            Log.Warning($"[组件库] 已放置统计失败: {e.Message}");
            return NoCounts;
        }
    }

    private static string CategoryLabel(string key)
    {
        var category = ComponentCategories.All.FirstOrDefault(c =>
            string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        return category is null ? key : CategoryText(category);
    }

    private static string CategoryText(ComponentCategoryInfo category)
    {
        var text = AppUtils.Tr(category.LocaleKey);
        return text == category.LocaleKey ? category.Key : text;
    }

    private static string CategoryGlyph(string icon) => icon switch
    {
        "Clock" => AppIcons.Clock,
        "Sun" => AppIcons.Sun,
        "Info" => AppIcons.Info,
        "Music" => AppIcons.Music,
        "Grid" => AppIcons.Grid,
        "Education" => AppIcons.Education,
        "Wrench" => AppIcons.Wrench,
        "Settings" => AppIcons.Settings,
        "Books" => AppIcons.Books,
        _ => AppIcons.Grid,
    };
}

/// <summary>自适应卡片面板</summary>
public sealed class ComponentCardPanel : Panel
{
    public double MinItemWidth { get; set; } = 196;

    public double MaxItemWidth { get; set; } = 300;

    public double ColumnSpacing { get; set; } = 14;

    public double RowSpacing { get; set; } = 14;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var count = Children.Count;
        if (count == 0)
        {
            return new Size(width, 0);
        }

        var (columns, itemWidth, _) = Plan(width, count);
        var total = 0.0;
        for (var i = 0; i < count; i += columns)
        {
            var rowHeight = 0.0;
            for (var j = 0; j < columns && i + j < count; j++)
            {
                var child = Children[i + j];
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            total += rowHeight;
            if (i + columns < count)
            {
                total += RowSpacing;
            }
        }
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = double.IsFinite(finalSize.Width) ? finalSize.Width : 0;
        var count = Children.Count;
        if (count == 0)
        {
            return finalSize;
        }

        var (columns, itemWidth, offset) = Plan(width, count);
        var y = 0.0;
        for (var i = 0; i < count; i += columns)
        {
            var rowHeight = 0.0;
            for (var j = 0; j < columns && i + j < count; j++)
            {
                rowHeight = Math.Max(rowHeight, Children[i + j].DesiredSize.Height);
            }
            for (var j = 0; j < columns && i + j < count; j++)
            {
                var child = Children[i + j];
                child.Arrange(new Rect(offset + j * (itemWidth + ColumnSpacing), y, itemWidth, rowHeight));
            }
            y += rowHeight;
            if (i + columns < count)
            {
                y += RowSpacing;
            }
        }
        return finalSize;
    }

    private (int Columns, double ItemWidth, double Offset) Plan(double width, int count)
    {
        var columns = Math.Max(1,
            (int)Math.Floor((width + ColumnSpacing) / (MinItemWidth + ColumnSpacing)));
        columns = Math.Min(columns, count);
        var itemWidth = (width - (columns - 1) * ColumnSpacing) / columns;
        if (itemWidth > MaxItemWidth)
        {
            itemWidth = MaxItemWidth;
        }
        if (itemWidth < 1)
        {
            itemWidth = Math.Max(1, width);
        }
        var rowWidth = columns * itemWidth + (columns - 1) * ColumnSpacing;
        var offset = Math.Max(0, (width - rowWidth) / 2);
        return (columns, itemWidth, offset);
    }
}
