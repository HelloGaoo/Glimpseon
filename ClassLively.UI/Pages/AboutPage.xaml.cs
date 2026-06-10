using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace ClassLively_UI.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        LoadVersionInfo();
    }

    private void LoadVersionInfo()
    {
        var version = "1.0.0";
        var buildDate = "2025-06-10";

        VersionTextBlock.Text = $"版本：{version}";
        BuildDateTextBlock.Text = $"构建日期：{buildDate}";
        DetailVersionText.Text = version;
        DetailBuildDateText.Text = buildDate;
    }

    private async void GitHubVisit_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("https://github.com/HelloGaoo/ClassLively"));
    }

    private async void AuthorHomepage_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("https://space.bilibili.com/1498602348"));
    }

    private async void LicenseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "GNU General Public License v3.0",
            Content =
                "Copyright (C) 2024-2025 HelloGaoo\n\n" +
                "This program is free software: you can redistribute it and/or modify " +
                "it under the terms of the GNU General Public License as published by " +
                "the Free Software Foundation, either version 3 of the License, or " +
                "(at your option) any later version.\n\n" +
                "This program is distributed in the hope that it will be useful, " +
                "but WITHOUT ANY WARRANTY; without even the implied warranty of " +
                "MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the " +
                "GNU General Public License for more details.\n\n" +
                "You should have received a copy of the GNU General Public License " +
                "along with this program. If not, see <https://www.gnu.org/licenses/>.",
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void CreditsButton_Click(object sender, RoutedEventArgs e)
    {
        var credits = new List<string>
        {
            "Microsoft WinUI 3 - UI 框架",
            "Python / PyQt6 - 后端服务",
            "Microsoft.Xaml.Behaviors - XAML 行为",
            "Community contributors - 社区贡献者",
            "Open source community - 开源社区"
        };

        var contentPanel = new StackPanel { Spacing = 8 };
        foreach (var credit in credits)
        {
            var textBlock = new TextBlock
            {
                Text = "• " + credit,
                FontSize = 13,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap
            };
            contentPanel.Children.Add(textBlock);
        }

        var dialog = new ContentDialog
        {
            Title = "鸣谢",
            Content = contentPanel,
            CloseButtonText = "114514",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
