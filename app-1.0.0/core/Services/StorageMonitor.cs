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

// 储存监控服务 

using System.Globalization;
using Avalonia.Threading;

namespace Glimpseon.Core.Services;

public static class StorageMonitor
{
    private const int CheckIntervalMinutes = 30;
    private const int StartupDelayMinutes = 3;
    private const int RepeatNotifyHours = 1;       // 同一盘低空间状态重复提醒间隔
    private const int NotifyDurationSeconds = 30;  // 弹窗自动关闭时间
    private const double MinDataVolumeBytes = 1073741824.0; // 引导分区特征容量上限 1GB

    // 各盘上次提醒时间 低空间状态每小时重弹 空间恢复后移除重置
    private static readonly Dictionary<string, DateTime> Notified = new(StringComparer.OrdinalIgnoreCase);
    private static DispatcherTimer? _timer;

    public static void Start()
    {
        if (_timer is not null)
        {
            return;
        }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(CheckIntervalMinutes) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        // 启动后延迟首次检查 避开开机高峰
        _ = Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes)).ContinueWith(
            _ => Dispatcher.UIThread.Post(Check));

        Config.StorageFullNotify.ValueChanged += v => Dispatcher.UIThread.Post(() =>
        {
            Notified.Clear();
            if (v)
            {
                Check();
            }
        });
        Config.StorageFullThreshold.ValueChanged += _ => Notified.Clear();
        Log.Info($"[储存] 监控已启动 间隔{CheckIntervalMinutes}分钟");
    }

    /// <summary>
    /// 扫描本地固定磁盘 剩余空间低于阈值时右下角弹自绘弹窗
    /// 同一盘低空间状态每小时提醒一次 空间恢复后自动重置
    /// 引导分区与隐藏分区不计入
    /// </summary>
    public static void Check()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            if (!Config.StorageFullNotify.Value)
            {
                return;
            }
            var threshold = Config.StorageFullThreshold.Value;
            var details = new List<string>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.TotalSize <= 0)
                    {
                        continue;
                    }
                    // 无有效盘符的卷 隐藏分区
                    var letter = drive.Name.Length > 0 ? drive.Name.TrimEnd(':', '\\', '/') : "";
                    if (letter.Length != 1)
                    {
                        continue;
                    }
                    // 小容量 FAT32 卷 引导分区(ESP)
                    if (string.Equals(drive.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase)
                        && drive.TotalSize < MinDataVolumeBytes)
                    {
                        continue;
                    }
                    var freePercent = drive.AvailableFreeSpace * 100.0 / drive.TotalSize;
                    if (freePercent >= threshold)
                    {
                        // 空间已恢复 清记录 下次低于阈值重新提醒
                        Notified.Remove(letter);
                        continue;
                    }
                    if (Notified.TryGetValue(letter, out var last)
                        && DateTime.Now - last < TimeSpan.FromHours(RepeatNotifyHours))
                    {
                        continue;
                    }
                    Notified[letter] = DateTime.Now;
                    details.Add(AppUtils.Tr("settings.storage_full_drive",
                        ("drive", letter),
                        ("free", FormatGb(drive.AvailableFreeSpace)),
                        ("percent", threshold)));
                }
                catch (Exception e)
                {
                    Log.Debug($"[储存] {drive.Name} 检测跳过: {e.Message}");
                }
            }
            if (details.Count == 0)
            {
                return;
            }
            var content = AppUtils.Tr("settings.storage_full_toast",
                ("detail", string.Join("，", details)));
            var (bg, fg) = NotifPalette.FollowTheme();
            new NotificationManager().HandleNotification(new NotificationRequest
            {
                Type = NotifType.Corner,
                Content = content,
                Duration = NotifyDurationSeconds,
                BgColor = bg,
                TextColor = fg,
                // 磁盘提醒静默 不走TTS
                TtsVoice = "done",
            });
            Log.Info($"[储存] 空间不足提醒 阈值{threshold}% 内容: {content}");
        }
        catch (Exception e)
        {
            Log.Warning($"[储存] 检测失败: {e.Message}");
        }
    }

    private static string FormatGb(long bytes)
    {
        return (bytes / 1073741824.0).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
    }
}
