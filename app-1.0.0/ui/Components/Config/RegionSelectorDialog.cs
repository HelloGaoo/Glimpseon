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

// RegionSelectorDialog
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
/// 地区选择器
/// 搜索框 + 地区列表(city.db) 实时搜索 双击确认 打开时定位当前城市 用应用内 FAContentDialog 承载
/// </summary>
public static class RegionSelectorDialog
{
    public static async Task<string?> ShowAsync(Window owner, string current)
    {
        var db = new RegionDatabase();
        var search = new TextBox
        {
            PlaceholderText = AppUtils.Tr("weather_service.region_placeholder"),
            MinWidth = 460,
        };
        // 固定高度: 让 ListBox 内部滚动(而不是外层对话框整页滚动)
        var list = new ListBox
        {
            MinWidth = 460,
            SelectionMode = SelectionMode.Single,
            // 列表自身可滚 撑满 root 的 * 行
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        void Refresh(string? kw)
        {
            list.ItemsSource = db.Search(string.IsNullOrWhiteSpace(kw) ? null : kw);
        }
        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Refresh(search.Text);
            }
        };
        Refresh(null);

        // 固定内容高度(含标题/按钮后整窗 <=600) 列表占满 * 行并在其内部滚动
        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.Gap,
            MinWidth = 460,
            Height = 440,
        };
        Grid.SetRow(search, 0);
        Grid.SetRow(list, 1);
        root.Children.Add(search);
        root.Children.Add(list);

        var dlg = new FAContentDialog
        {
            Title = AppUtils.Tr("weather_service.select_region"),
            Content = root,
            PrimaryButtonText = AppUtils.Tr("common.confirm"),
            CloseButtonText = AppUtils.Tr("common.cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        };

        // 双击 = 确认(
        list.DoubleTapped += (_, e) =>
        {
            if (list.SelectedItem is string s && s.Length > 0)
            {
                e.Handled = true;
                dlg.Hide(FAContentDialogResult.Primary);
            }
        };

        // 定位当前城市(
        if (!string.IsNullOrEmpty(current) && list.ItemsSource is IEnumerable<string> items)
        {
            var idx = 0;
            var found = -1;
            foreach (var it in items)
            {
                if (it == current)
                {
                    found = idx;
                    break;
                }
                idx++;
            }
            if (found >= 0)
            {
                list.SelectedIndex = found;
                list.ScrollIntoView(found);
            }
        }

        var result = await dlg.ShowAsync(owner);
        return result == FAContentDialogResult.Primary && list.SelectedItem is string sel && sel.Length > 0
            ? sel
            : null;
    }
}
