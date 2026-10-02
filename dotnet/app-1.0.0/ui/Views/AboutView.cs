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

// 关于页
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Glimpseon.Core;

namespace Glimpseon.UI.Views;

public class AboutView : UserControl
{
    private readonly TextBlock _versionBlock = new();

    public AboutView()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.about")));
        _versionBlock.Text = $"Glimpseon {Paths.Version}  构建日期 {Paths.BuildDate}";
        _versionBlock.FontSize = 16;
        stack.Children.Add(_versionBlock);
        stack.Children.Add(Common.MakeBody($"APP_DIR: {Paths.AppDir}"));
        stack.Children.Add(Common.MakeBody($"DATA_ROOT: {Paths.DataRoot}"));

        var checkButton = new Button { Content = AppUtils.Tr("update.check") };
        checkButton.Click += OnCheckUpdateClicked;
        stack.Children.Add(checkButton);

        Content = Common.MakePageScroll(stack);
    }

    private void OnCheckUpdateClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Log.Info("[更新] 手动检查更新");
    }

    public void CheckUpdateAuto()
    {
        Log.Info("[更新] 自动检查更新已启用");
    }
}
