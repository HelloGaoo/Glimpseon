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

// 软件下载页
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Glimpseon.Core;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core.Services;

namespace Glimpseon.UI.Views;

public class DownloadView : UserControl
{
    public DownloadView()
    {
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.download")));

        foreach (var category in DownloadCatalog.Categories)
        {
            stack.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr(category.NameKey),
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 4),
            });

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var software in category.Software)
            {
                wrap.Children.Add(BuildSoftwareCard(software));
            }
            stack.Children.Add(wrap);
        }

        Content = Common.MakePageScroll(stack);
    }

    private Control BuildSoftwareCard(SoftwareEntry software)
    {
        var icon = new Image { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
        try
        {
            var iconPath = DownloadCatalog.GetSoftwareIconPath(software.Icon);
            if (File.Exists(iconPath))
            {
                icon.Source = new Bitmap(iconPath);
            }
        }
        catch (Exception)
        {
            // 图标缺失 留空
        }

        var nameBlock = new TextBlock { Text = software.Name, FontWeight = FontWeight.SemiBold, FontSize = 14 };
        var descBlock = new TextBlock
        {
            Text = software.Description,
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 220,
        };

        var downloadButton = new FASymbolIcon
        {
            Symbol = FASymbol.Download,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Button
        {
            Content = downloadButton,
            Padding = new Thickness(10, 6, 10, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => OnDownloadClicked(software);

        var card = new Border
        {
            Width = 300,
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.3),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    icon,
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { nameBlock, descBlock },
                    },
                    button,
                },
            },
        };
        return card;
    }

    private void OnDownloadClicked(SoftwareEntry software)
    {
        Log.Info($"[下载] 请求 {software.Name} link={software.Link ?? "无"} ");
    }
}
