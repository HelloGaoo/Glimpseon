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

// 课程表页
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;

namespace Glimpseon.UI.Views;

public class TimetableView : UserControl
{
    public TimetableView(MainWindow mainWindow)
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.timetable")));
        stack.Children.Add(Common.MakeBody($"档案目录 {Paths.DataProfile}"));
        stack.Children.Add(new FAInfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = FAInfoBarSeverity.Informational,
            Title = AppUtils.Tr("timetable.profile"),
            Message = "...",
        });

        Content = Common.MakePageScroll(stack);
    }
}
