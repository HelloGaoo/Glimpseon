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
using Avalonia.Controls;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;

namespace Glimpseon.UI.Views;

public class WallpaperView : UserControl
{
    public WallpaperView(MainWindow mainWindow)
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.wallpaper")));
        stack.Children.Add(Common.MakeBody(AppUtils.Tr("wallpaper.default_source")));

        var currentCard = new FAInfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = FAInfoBarSeverity.Informational,
            Title = AppUtils.Tr("wallpaper.history"),
            Message = "000",
        };
        stack.Children.Add(currentCard);

        Content = Common.MakePageScroll(stack);
    }
}
