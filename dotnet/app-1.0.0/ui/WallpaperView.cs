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
// 壁纸页
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

public class WallpaperView : UserControl
{
    private readonly MainWindow _mainWindow;
    private readonly Image _previewImage = new()
    {
        Stretch = Stretch.UniformToFill,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock _info = new()
    {
        FontSize = 12,
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 8, 4, 0),
    };
    private readonly WrapPanel _historyPanel = new() { Orientation = Orientation.Horizontal };
    private string? _currentPath;

    public WallpaperView(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        var stack = new StackPanel { Spacing = 10 };

        stack.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.wallpaper")));

        // 预览大图 圆角裁切
        var previewBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Height = 380,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = _previewImage,
        };
        stack.Children.Add(previewBorder);
        stack.Children.Add(_info);

        // 操作行
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var fetchButton = new Button { Content = "获取壁纸" };
        fetchButton.Click += async (_, _) => await FetchAndApplyAsync();
        var setDesktopButton = new Button { Content = "设为桌面" };
        setDesktopButton.Click += (_, _) => SetDesktop();
        var saveButton = new Button { Content = "另存为" };
        saveButton.Click += async (_, _) => await SaveWallpaperAsAsync();
        var openDirButton = new Button { Content = "打开目录" };
        openDirButton.Click += (_, _) => OpenWallpaperDir();
        buttonRow.Children.Add(fetchButton);
        buttonRow.Children.Add(setDesktopButton);
        buttonRow.Children.Add(saveButton);
        buttonRow.Children.Add(openDirButton);
        stack.Children.Add(buttonRow);

        // 历史记录 缩略图墙
        stack.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("wallpaper.history"),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 14, 0, 4),
        });
        stack.Children.Add(_historyPanel);

        Content = Common.MakePageScroll(stack);

        // 初始显示缓存或默认壁纸
        var cached = WallpaperService.LoadCachePath() ?? MaybeDefaultWallpaper();
        if (cached is not null)
        {
            UseWallpaper(cached, AppUtils.Tr("wallpaper.source_cache"), addToHistory: false);
        }
        RefreshHistoryCards();
    }

    private static string? MaybeDefaultWallpaper()
    {
        var def = Paths.GetResourcePath(Constants.ResourceDefaultWallpaper);
        if (File.Exists(def))
        {
            return def;
        }
        if (Directory.Exists(Paths.WallpaperDir))
        {
            return Directory.EnumerateFiles(Paths.WallpaperDir)
                .Where(f => Path.GetFileName(f).StartsWith("wallpaper_") && f.EndsWith(".jpg"))
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }
        return null;
    }

    private async Task FetchAndApplyAsync()
    {
        var path = await WallpaperService.FetchAsync();
        if (path is null)
        {
            _info.Text = "获取壁纸失败";
            return;
        }
        UseWallpaper(path, "");
        if (Config.AutoSyncToDesktop.Value)
        {
            SetDesktop();
        }
        RefreshHistoryCards();
    }


    // 刷新展示
    public void NotifyWallpaperFetched(string path, string source)
    {
        try
        {
            UseWallpaper(path, source, addToHistory: false);
            RefreshHistoryCards();
        }
        catch (Exception e)
        {
            Log.Warning($"[壁纸页] 同步更新失败: {e.Message}");
        }
    }

    // 应用壁纸
    private void UseWallpaper(string path, string source, bool addToHistory = true)
    {
        if (!File.Exists(path))
        {
            Log.Warning($"选择的壁纸文件不存在: {path}");
            return;
        }
        try
        {
            _previewImage.Source = new Bitmap(path);
            _mainWindow.HomeView.SetWallpaper(path);
            _currentPath = path;
            if (addToHistory)
            {
                WallpaperService.History.Add(path, source, "");
            }
            UpdateInfo(path, source);
            Log.Info($"已应用壁纸: {path}");
        }
        catch (Exception e)
        {
            Log.Warning($"壁纸解码失败 保持不变: {path} {e.Message}");
        }
    }

    private void UpdateInfo(string path, string source)
    {
        var record = WallpaperService.History.Records.FirstOrDefault(r => r.Path == path);
        var sizeKb = 0;
        try
        {
            sizeKb = (int)(new FileInfo(path).Length / 1024);
        }
        catch (Exception e)
        {

            Log.Debug($"[壁纸] 大小读取失败 按 0: {e.Message}");
            // 读取失败 按 0
        }
        _info.Text = $"{path}  ({sizeKb}KB)\n{source} {record?.Resolution ?? ""}".Trim();
    }

    private void SetDesktop()
    {
        if (_currentPath is null)
        {
            return;
        }
        if (WallpaperService.SetDesktop(_currentPath))
        {
            Log.Info($"已应用壁纸: {_currentPath}");
        }
    }

    private async Task SaveWallpaperAsAsync()
    {
        if (_currentPath is null || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存壁纸",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_currentPath),
            DefaultExtension = "jpg",
        });
        if (file is null)
        {
            Log.Debug("用户取消另存壁纸");
            return;
        }
        try
        {
            await using var source = File.OpenRead(_currentPath);
            await using var target = await file.OpenWriteAsync();
            await source.CopyToAsync(target);
            Log.Info($"壁纸已保存到: {file.Name}");
        }
        catch (Exception e)
        {
            Log.Error($"保存壁纸失败: {e.Message}");
        }
    }

    private void OpenWallpaperDir()
    {
        try
        {
            Directory.CreateDirectory(Paths.WallpaperDir);
            Process.Start(new ProcessStartInfo { FileName = Paths.WallpaperDir, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Warning($"打开壁纸目录失败: {e.Message}");
        }
    }

    // 历史缩略图墙 点击应用
    private void RefreshHistoryCards()
    {
        _historyPanel.Children.Clear();
        foreach (var record in WallpaperService.History.Records)
        {
            _historyPanel.Children.Add(BuildHistoryCard(record));
        }
    }

    private Control BuildHistoryCard(WallpaperRecord record)
    {
        var thumb = new Image
        {
            Width = 208,
            Height = 117,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        try
        {
            using var stream = File.OpenRead(record.Path);
            thumb.Source = Bitmap.DecodeToWidth(stream, 416);
        }
        catch (Exception e)
        {

            Log.Debug($"[壁纸] 缩略图解码失败 留空: {e.Message}");
            // 缩略图解码失败 留空
        }
        var caption = new TextBlock
        {
            Text = $"{record.AddedTime}\n{record.Source}  {record.Resolution}",
            FontSize = 11,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 4, 8, 6),
        };
        var button = new Button
        {
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Border
            {
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Child = new StackPanel
                {
                    Children = { thumb, caption },
                },
            },
        };
        button.Click += (_, _) =>
        {
            UseWallpaper(record.Path, record.Source);
            if (Config.AutoSyncToDesktop.Value)
            {
                SetDesktop();
            }
        };
        return button;
    }
}
