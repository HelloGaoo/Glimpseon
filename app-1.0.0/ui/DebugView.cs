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

// 调试页
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Glimpseon.Core;

namespace Glimpseon.UI;

public class DebugView : UserControl
{
    private readonly TextBlock _info = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new Avalonia.Media.FontFamily("Consolas"),
        FontSize = 12,
    };

    public DebugView()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.debug")));
        stack.Children.Add(_info);
        var refresh = new Button { Content = AppUtils.Tr("debug.refresh") };
        refresh.Click += (_, _) => RefreshInfo();
        stack.Children.Add(refresh);
        var crashBtn = new Button { Content = AppUtils.Tr("debug.crash_test") };
        crashBtn.Click += (_, _) => new Thread(() =>
            throw new InvalidOperationException("CrashTest 模拟崩溃")).Start();
        stack.Children.Add(crashBtn);
        Content = Common.MakePageScroll(stack);
        RefreshInfo();
    }

    private void RefreshInfo()
    {
        var proc = Process.GetCurrentProcess();
        _info.Text = string.Join('\n',
            $"时间: {AppUtils.PreciseNow():yyyy-MM-dd HH:mm:ss}",
            $"PID: {proc.Id}",
            $"内存: {proc.WorkingSet64 / 1024 / 1024}MB",
            $"线程: {proc.Threads.Count}",
            $"NTP 偏移: {AppUtils.NtpOffset.TotalSeconds:+0.000;-0.000}s",
            $"PACKAGE_ROOT: {Paths.PackageRoot}",
            $"APP_DIR: {Paths.AppDir}",
            $"DATA_ROOT: {Paths.DataRoot}");
    }
}
