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

// QuickLaunchGridWidget
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

/// <summary>快捷启动 真实图标 + 常用应用 + 编辑态</summary>
public sealed class QuickLaunchGridWidget : WidgetCardBase
{
    private const int MaxApps = 8;
    private const double RootPadX = WidgetSpace.PadCompact;
    private const double RootPadY = WidgetSpace.Gap;

    private readonly WrapPanel _tiles = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly ScrollViewer _scroll;
    private readonly Grid _tileArea = new();
    private readonly Border _empty = new();
    private readonly StackPanel _adder = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = WidgetSpace.GapSm,
        Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly TextBlock _editLabel = new()
    {
        FontSize = WidgetType.Label,
        FontFamily = FAIcons.SymbolsFont,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly Border _editButton;
    private readonly Grid _footer = new();
    private readonly List<QuickLaunchItem> _apps = new();
    private readonly List<Border> _tileBorders = new();

    private string? _storeFile;
    private bool _loaded;
    private bool _editing;
    private bool _hover;
    private bool _ready;

    public QuickLaunchGridWidget(ComponentDefinition definition) : base(definition)
    {
        _scroll = new ScrollViewer
        {
            Content = _tiles,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        _empty.HorizontalAlignment = HorizontalAlignment.Stretch;
        _empty.VerticalAlignment = VerticalAlignment.Stretch;
        _tileArea.Children.Add(_scroll);
        _tileArea.Children.Add(_empty);

        _editButton = new Border
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0),
            Child = _editLabel,
            Cursor = new Cursor(StandardCursorType.Hand),
            ZIndex = 20,
        };
        ToolTip.SetTip(_editButton, Tr("quick_launch.edit"));
        _editButton.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
            RoutingStrategies.Bubble);
        _editButton.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            _editing = !_editing;
            _hover = _editing;
            RebuildCells();
        };

        _footer.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto");
        Grid.SetColumn(_adder, 0);
        _footer.Children.Add(_adder);
        _footer.IsVisible = false;

        var root = new Grid
        {
            Margin = new Thickness(RootPadX, RootPadY, RootPadX, RootPadY),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
        };
        Grid.SetRow(_tileArea, 0);
        Grid.SetRow(_footer, 1);
        root.Children.Add(_tileArea);
        root.Children.Add(_footer);
        Card.Child = root;
        Content = Card;

        PointerEntered += (_, _) =>
        {
            _hover = true;
            UpdateEditChrome(WidgetSurfacePalette.Resolve(CardIsDark));
        };
        PointerExited += (_, _) =>
        {
            _hover = false;
            UpdateEditChrome(WidgetSurfacePalette.Resolve(CardIsDark));
        };
        SizeChanged += (_, _) => UpdateMetrics();
        Config.ThemeColor.ValueChanged += _ =>
        {
            if (_ready)
            {
                RebuildCells();
            }
        };

        _ready = true;
        RebuildCells();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_loaded || string.IsNullOrEmpty(ComponentId))
        {
            return;
        }
        _loaded = true;
        _storeFile = IOPath.Combine(Paths.DataUser, $"qlgrid_{ComponentId}.json");
        try
        {
            if (File.Exists(_storeFile))
            {
                LoadStore();
            }
            else
            {
                _apps.AddRange(DefaultApps());
                Save();
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[QLG] 读取配置失败: {ex.Message}");
            _apps.Clear();
            _apps.AddRange(DefaultApps());
        }
        RebuildCells();
    }

    private void LoadStore()
    {
        if (_storeFile is null
            || JsonNode.Parse(File.ReadAllText(_storeFile)) is not JsonObject obj
            || obj["apps"] is not JsonArray arr)
        {
            return;
        }
        _apps.Clear();
        foreach (var n in arr)
        {
            if (n is not JsonObject o)
            {
                continue;
            }
            var path = (string?)o["path"] ?? "";
            if (path.Length == 0)
            {
                continue;
            }
            _apps.Add(new QuickLaunchItem
            {
                Name = (string?)o["name"] ?? "",
                NameKey = (string?)o["name_key"] ?? "",
                Path = path,
            });
        }
    }

    private static List<QuickLaunchItem> DefaultApps()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length == 0)
        {
            windows = @"C:\Windows";
        }
        return new List<QuickLaunchItem>
        {
            new()
            {
                NameKey = "custom_action.builtin_explorer",
                Path = IOPath.Combine(windows, "explorer.exe"),
            },
            new()
            {
                NameKey = "custom_action.builtin_downloads",
                Path = IOPath.Combine(home, "Downloads"),
            },
            new()
            {
                NameKey = "custom_action.builtin_documents",
                Path = IOPath.Combine(home, "Documents"),
            },
            new()
            {
                NameKey = "custom_action.builtin_notepad",
                Path = IOPath.Combine(windows, "notepad.exe"),
            },
        };
    }

    private void Save()
    {
        try
        {
            if (_storeFile is null)
            {
                return;
            }
            Directory.CreateDirectory(Paths.DataUser);
            var arr = new JsonArray();
            foreach (var a in _apps)
            {
                arr.Add(new JsonObject
                {
                    ["name"] = a.Name,
                    ["name_key"] = a.NameKey,
                    ["path"] = a.Path,
                });
            }
            File.WriteAllText(_storeFile, new JsonObject { ["apps"] = arr }.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Warning($"[QLG] 保存失败: {e.Message}");
        }
    }

    private void RebuildCells()
    {
        if (!_ready)
        {
            return;
        }
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _tiles.Children.Clear();
        _tileBorders.Clear();
        var iconSize = _apps.Count > 4 ? 32 : 48;
        foreach (var app in _apps)
        {
            _tiles.Children.Add(BuildTile(app, p, iconSize));
        }

        _scroll.IsVisible = _apps.Count > 0;
        _empty.IsVisible = _apps.Count == 0;
        if (_apps.Count == 0)
        {
            _editing = false;
            _empty.Child = BuildEmptyState(p);
        }

        _adder.Children.Clear();
        _adder.IsVisible = _editing && _apps.Count > 0 && _apps.Count < MaxApps;
        if (_adder.IsVisible)
        {
            _adder.Children.Add(MakeMiniButton(Tr("quick_launch_ii.add_app"), true, PickApp, p));
            _adder.Children.Add(MakeMiniButton(Tr("quick_launch_ii.add_folder"), false, PickFolder, p));
            _adder.Children.Add(new TextBlock
            {
                Text = $"{_apps.Count}/{MaxApps}",
                FontSize = WidgetType.Micro,
                Foreground = CardInk.Faint(p),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(WidgetSpace.GapXs, 0, 0, 0),
            });
        }

        AttachEditButton();
        UpdateEditChrome(p);
        UpdateMetrics();
    }

    private void AttachEditButton()
    {
        _tileArea.Children.Remove(_editButton);
        _footer.Children.Remove(_editButton);
        if (_apps.Count == 0)
        {
            _footer.IsVisible = false;
            return;
        }
        if (_editing)
        {
            Grid.SetColumn(_editButton, 1);
            _editButton.HorizontalAlignment = HorizontalAlignment.Right;
            _editButton.VerticalAlignment = VerticalAlignment.Center;
            _editButton.Margin = new Thickness(0);
            _footer.Children.Add(_editButton);
        }
        else
        {
            _editButton.HorizontalAlignment = HorizontalAlignment.Right;
            _editButton.VerticalAlignment = _apps.Count is > 4 and < 8
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top;
            _editButton.Margin = _apps.Count is > 4 and < 8
                ? new Thickness(0, 0, 0, 2)
                : new Thickness(0, 2, 0, 0);
            _tileArea.Children.Add(_editButton);
        }
        _footer.IsVisible = _editing;
    }

    private Border BuildTile(QuickLaunchItem app, WidgetSurfacePalette p, double iconSize)
    {
        var icon = new Border
        {
            Width = iconSize,
            Height = iconSize,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = BuildFallbackIcon(app.DisplayName, iconSize, p),
        };
        var bitmap = FileIcon.For(app.Path);
        if (bitmap is not null)
        {
            icon.Child = new Image
            {
                Source = bitmap,
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform,
            };
        }

        var labelSize = _apps.Count > 4 ? WidgetType.Micro : WidgetType.Label;
        var label = new TextBlock
        {
            Text = app.DisplayName,
            FontSize = labelSize,
            Foreground = CardInk.Fg(p),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 96,
            MaxLines = 2,
            LineHeight = WidgetType.LineHeight(labelSize, WidgetType.TightLineHeight),
        };
        var content = new StackPanel
        {
            Spacing = WidgetSpace.GapXs,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
        };
        content.Children.Add(icon);
        content.Children.Add(label);

        var tile = new Grid();
        tile.Children.Add(content);
        var missing = !File.Exists(app.Path) && !Directory.Exists(app.Path);
        if (_editing)
        {
            var removeLabel = new TextBlock
            {
                Text = "×",
                FontSize = WidgetType.Body,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var remove = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(p.Card, 0.96),
                BorderBrush = new SolidColorBrush(p.Line, 0.7),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = removeLabel,
                Cursor = new Cursor(StandardCursorType.Hand),
                ZIndex = 5,
            };
            ToolTip.SetTip(remove, Tr("quick_launch.delete"));
            remove.PointerEntered += (_, _) =>
            {
                remove.Background = CardInk.Danger();
                removeLabel.Foreground = Brushes.White;
            };
            remove.PointerExited += (_, _) =>
            {
                remove.Background = new SolidColorBrush(p.Card, 0.96);
                removeLabel.Foreground = CardInk.Muted(p);
            };
            remove.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
                RoutingStrategies.Bubble);
            remove.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                _apps.Remove(app);
                Save();
                RebuildCells();
            };
            removeLabel.Foreground = CardInk.Muted(p);
            tile.Children.Add(remove);
        }

        var normalBg = Brushes.Transparent;
        var hoverBg = new SolidColorBrush(p.Text, 0.055);
        var tileBorder = new Border
        {
            Margin = new Thickness(2, 2),
            Padding = new Thickness(3, 3),
            CornerRadius = new CornerRadius(10),
            Background = normalBg,
            Child = tile,
            Cursor = new Cursor(_editing ? StandardCursorType.Arrow : StandardCursorType.Hand),
        };
        if (missing)
        {
            tileBorder.Opacity = WidgetInk.DimOpacity;
        }
        ToolTip.SetTip(tileBorder, missing
            ? Tr("quick_launch.path_not_exist", ("path", app.Path))
            : app.Path);
        tileBorder.PointerEntered += (_, _) => tileBorder.Background = hoverBg;
        tileBorder.PointerExited += (_, _) => tileBorder.Background = normalBg;
        tileBorder.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
            RoutingStrategies.Bubble);
        tileBorder.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            if (_editing || missing)
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warning($"[QLG] 启动失败: {ex.Message}");
            }
        };
        _tileBorders.Add(tileBorder);
        return tileBorder;
    }

    private Control BuildFallbackIcon(string name, double size, WidgetSurfacePalette p)
    {
        var letter = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.24),
            Background = CardAccent.Brush(0.16),
            Child = new TextBlock
            {
                Text = letter,
                FontSize = size * 0.42,
                FontWeight = FontWeight.SemiBold,
                Foreground = CardAccent.Brush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private Control BuildEmptyState(WidgetSurfacePalette p)
    {
        var empty = new CardEmptyState(
            FAIcons.Glyph(AppIcons.Add),
            Tr("quick_launch_ii.empty_title"),
            Tr("quick_launch_ii.empty_hint"));
        empty.AddAction(MakeMiniButton(Tr("quick_launch_ii.add_app"), true, PickApp, p));
        empty.AddAction(MakeMiniButton(Tr("quick_launch_ii.add_folder"), false, PickFolder, p));
        empty.Apply(p);
        return empty;
    }

    private Border MakeMiniButton(string text, bool accent, Action onClick, WidgetSurfacePalette p)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = WidgetType.Label,
            FontWeight = WidgetWeight.Emphasis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = accent ? CardAccent.Brush() : CardInk.Muted(p),
        };
        var background = accent ? CardAccent.Brush(0.12) : new SolidColorBrush(p.Text, 0.055);
        var hover = accent ? CardAccent.Brush(0.20) : new SolidColorBrush(p.Text, 0.10);
        var button = new Border
        {
            Height = 25,
            Padding = new Thickness(9, 0),
            CornerRadius = new CornerRadius(8),
            Background = background,
            Child = label,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.PointerEntered += (_, _) => button.Background = hover;
        button.PointerExited += (_, _) => button.Background = background;
        button.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
            RoutingStrategies.Bubble);
        button.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return button;
    }

    private void UpdateEditChrome(WidgetSurfacePalette p)
    {
        _editLabel.Text = _editing ? AppIcons.Check : AppIcons.Edit;
        _editLabel.FontSize = _editing ? 11 : 11.5;
        _editLabel.Foreground = _editing ? CardAccent.Brush() : CardInk.Muted(p);
        _editButton.Background = _editing
            ? CardAccent.Brush(0.14)
            : new SolidColorBrush(p.Text, 0.055);
        _editButton.Opacity = _editing ? 1 : _hover ? 0.9 : 0.38;
        ToolTip.SetTip(_editButton, Tr(_editing ? "custom_action.done" : "quick_launch.edit"));
    }

    private void UpdateMetrics()
    {
        if (!_ready || Bounds.Width <= 1)
        {
            return;
        }
        var avail = Math.Max(90, Bounds.Width - RootPadX * 2 - 2);
        var columns = Math.Clamp((int)(avail / 88.0), 2, 6);
        _tiles.ItemWidth = Math.Clamp(avail / columns, 68, 110);
        _tiles.ItemHeight = _apps.Count > 4 ? 72 : 92;
        _tiles.VerticalAlignment = VerticalAlignment.Center;
        _scroll.VerticalAlignment = _apps.Count > 4 ? VerticalAlignment.Stretch : VerticalAlignment.Center;
    }

    private async void PickApp()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = new List<Avalonia.Platform.Storage.FilePickerFileType>
                    {
                        new("程序或快捷方式") { Patterns = new[] { "*.exe", "*.lnk" } },
                        new("所有文件") { Patterns = new[] { "*" } },
                    },
                });
            if (files is { Count: > 0 })
            {
                var path = files[0].Path.LocalPath;
                AddApp(new QuickLaunchItem
                {
                    Name = IOPath.GetFileNameWithoutExtension(path),
                    Path = path,
                });
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[QLG] 选择失败: {e.Message}");
        }
    }

    private async void PickFolder()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    AllowMultiple = false,
                });
            if (folders is { Count: > 0 })
            {
                var path = folders[0].Path.LocalPath;
                AddApp(new QuickLaunchItem
                {
                    Name = new DirectoryInfo(path).Name,
                    Path = path,
                });
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[QLG] 选择文件夹失败: {e.Message}");
        }
    }

    private void AddApp(QuickLaunchItem item)
    {
        if (_apps.Count >= MaxApps)
        {
            Log.Warning("[QLG] 已达到 8 个上限");
            return;
        }
        if (_apps.Any(a => string.Equals(a.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        _apps.Add(item);
        Save();
        RebuildCells();
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        if (!_ready)
        {
            return;
        }
        RebuildCells();
    }

    private static string Tr(string key) => AppUtils.Tr(key);
    private static string Tr(string key, params (string Name, object? Value)[] args) => AppUtils.Tr(key, args);
}
