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

// 启动窗口
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Glimpseon.Core;

namespace Glimpseon.UI;

public partial class SplashWindow : Window
{
    private double _currentProgress;
    private double _targetProgress;
    private DispatcherTimer? _animTimer;

    public SplashWindow(string version, string iconPath)
    {
        InitializeComponent();
        VersionLabel.Text = version;
        StatusLabel.Text = AppUtils.Tr("splash.initializing");
        LoadIcon(iconPath);
        CenterOnScreen();
    }

    private void LoadIcon(string iconPath)
    {
        try
        {
            if (File.Exists(iconPath))
            {
                IconImage.Source = new Bitmap(iconPath);
            }
            else
            {
                Log.Warning($"启动窗口图标不存在: {iconPath}");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"启动窗口图标加载失败: {e.Message}");
        }
    }

    public void CenterOnScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }
        var rect = screen.WorkingArea;
        var scaling = screen.Scaling;
        Position = new Avalonia.PixelPoint(
            (int)((rect.X + rect.Width / 2 - Width * scaling / 2)),
            (int)((rect.Y + rect.Height / 2 - Height * scaling / 2)));
    }

    public void UpdateStatus(string status)
    {
        StatusLabel.Text = status;
        Log.Debug($"启动窗口状态更新: {status}");
    }

    public void SetProgress(double value)
    {
        _targetProgress = Math.Clamp(value, 0, 100);
        if (_animTimer is null)
        {
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
            _animTimer.Tick += AdvanceProgress;
        }
        if (!_animTimer.IsEnabled)
        {
            _animTimer.Start();
        }
    }

    private void AdvanceProgress(object? sender, EventArgs e)
    {
        if (_currentProgress < _targetProgress)
        {
            var step = Math.Max(1, (_targetProgress - _currentProgress) / 6);
            _currentProgress = Math.Min(_currentProgress + step, _targetProgress);
            ProgressBar.Value = _currentProgress;
        }
        else
        {
            _animTimer?.Stop();
        }
    }
}
