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

// ClassAlbumWidget
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

/// <summary>
/// 班级相册
/// 图片源 data/classphotos/album_<id>/ 5s 自动轮播 支持拖放图片
/// </summary>
public sealed class ClassAlbumWidget : WidgetCardBase
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.UniformToFill,
    };
    private readonly List<string> _photos = new();
    private int _index;
    private readonly DispatcherTimer _autoTimer;
    private readonly string _photosDir;

    public ClassAlbumWidget(ComponentDefinition definition, bool vertical) : base(definition)
    {
        Card.Child = _image;
        Content = Card;
        ClipToBounds = true;


        var id = string.IsNullOrEmpty(definition.Id) ? "default" : definition.Id;
        _photosDir = IOPath.Combine(Paths.DataRoot, "classphotos", $"album_{id}");
        LoadPhotos();

        // 5s 自动轮播
        _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoTimer.Tick += (_, _) => FlipNext();
        _autoTimer.Start();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        ApplyCardStyle();
    }

    private void LoadPhotos()
    {
        _photos.Clear();
        try
        {
            Directory.CreateDirectory(_photosDir);
            var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            foreach (var f in Directory.EnumerateFiles(_photosDir))
            {
                if (exts.Contains(IOPath.GetExtension(f).ToLowerInvariant()))
                {
                    _photos.Add(f);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[ALBUM] 读取相册失败: {e.Message}");
        }
        ShowCurrent();
    }

    private void FlipNext()
    {
        if (_photos.Count == 0)
        {
            return;
        }
        _index = (_index + 1) % _photos.Count;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        try
        {
            _image.Source = _photos.Count > 0 ? new Bitmap(_photos[_index]) : null;
        }
        catch (Exception e)
        {
            Log.Debug($"[相册] 图片解码失败 留空: {e.Message}");
            _image.Source = null;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            foreach (var item in e.DataTransfer.Items)
            {
                if (item.TryGetFile() is not { } file)
                {
                    continue;
                }
                var ext = IOPath.GetExtension(file.Path.LocalPath).ToLowerInvariant();
                if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(ext))
                {
                    var dest = IOPath.Combine(_photosDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
                    File.Copy(file.Path.LocalPath, dest, true);
                    _photos.Add(dest);
                }
            }
            ShowCurrent();
        }
        catch (Exception ex)
        {
            Log.Warning($"[ALBUM] 拖放失败: {ex.Message}");
        }
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        Card.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));
        Card.ClipToBounds = true;
    }
}
