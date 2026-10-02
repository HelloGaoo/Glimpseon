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

// 通知页
using Avalonia.Controls;
using Avalonia.Layout;
using Glimpseon.Core;

namespace Glimpseon.UI.Views;

public class NotificationView : UserControl
{
    private readonly TextBox _edit = new()
    {
        Watermark = "输入公告内容",
        AcceptsReturn = true,
        MinHeight = 120,
        MinWidth = 480,
    };

    public NotificationView(MainWindow mainWindow)
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.notification")));
        stack.Children.Add(_edit);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var sendButton = new Button { Content = AppUtils.Tr("notification.send") };
        sendButton.Click += OnSendClicked;
        buttonRow.Children.Add(sendButton);
        stack.Children.Add(buttonRow);

        Content = Common.MakePageScroll(stack);
    }

    private void OnSendClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var text = _edit.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        Log.Info($"[通知] 发送请求已记录 长度={text.Length} ");
    }
}
