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

// 通知模块

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Glimpseon.Core.Win32;

namespace Glimpseon.Core;

public static class NotifType
{
    public const string Scroll = "scroll";
    public const string Corner = "corner";
    public const string Fullscreen = "fullscreen";
}

// 公告存储

public static class Announcements
{
    public const int Max = 50;
    private static string FilePath => Path.Combine(Paths.DataUser, "announcements.json");

    // 公告变更信号
    public static event Action? Changed;

    private static void NotifyChanged() => Changed?.Invoke();

    public static List<Announcement> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonNode.Parse(File.ReadAllText(FilePath));
                if (data is JsonArray arr)
                {
                    return Clean(arr);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"读取公告失败: {e.Message}");
        }
        return [];
    }

    public static void Save(List<Announcement> items)
    {
        try
        {
            Directory.CreateDirectory(Paths.DataUser);
            var arr = new JsonArray();
            foreach (var item in items.Take(Max))
            {
                arr.Add(new JsonObject { ["text"] = item.Text, ["created"] = item.Created });
            }
            File.WriteAllText(FilePath, arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Warning($"保存公告失败: {e.Message}");
        }
        NotifyChanged();
    }

    // 清外部数据 text<=200 created<=16 最多 Max 条 
    public static List<Announcement> Clean(IEnumerable<JsonNode?> raw)
    {
        var items = new List<Announcement>();
        foreach (var node in raw)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }
            var text = (obj["text"]?.GetValue<string>() ?? "").Trim();
            if (text.Length == 0)
            {
                continue;
            }
            var created = (obj["created"]?.GetValue<string>() ?? "").Trim();
            items.Add(new Announcement(text[..Math.Min(200, text.Length)], created[..Math.Min(16, created.Length)]));
            if (items.Count >= Max)
            {
                break;
            }
        }
        return items;
    }

    // 头部插入一条 同分钟同内容去重
    public static bool Append(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            return false;
        }
        text = text[..Math.Min(200, text.Length)];
        var items = Clean(new JsonArray(Load().Select(a => (JsonNode?)new JsonObject { ["text"] = a.Text, ["created"] = a.Created }).ToArray()));
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        if (items.Count > 0 && items[0].Text == text && items[0].Created == created)
        {
            Log.Debug("[Announce] 去重跳过");
            return false;
        }
        items.Insert(0, new Announcement(text, created));
        Save(items);
        Log.Info($"[Announce] 公告已写入 共{items.Count}条: {text[..Math.Min(30, text.Length)]}");
        return true;
    }
}

public sealed record Announcement(string Text, string Created);

//  通知请求

public sealed record NotificationRequest
{
    public string Type { get; init; } = "";
    public string Content { get; init; } = "";
    public int Speed { get; init; } = 5;
    public int Duration { get; init; } = 5;
    public string BgColor { get; init; } = "#000000";
    public int BgAlpha { get; init; } = 180;
    public string TextColor { get; init; } = "#ffffff";
    public int FontSize { get; init; } = 24;
    // 0=Normal 1=Bold 2=Black
    public int FontWeight { get; init; } = 1;
    public string TtsVoice { get; init; } = "zh-CN-XiaoxiaoNeural";
    public int TtsRate { get; init; } = 100;
    public int TtsVolume { get; init; } = 100;
}

public static class NotifPalette
{
    public static (string Bg, string Fg) FollowTheme()
    {
        var dark = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        return dark ? ("#1F1F1F", "#FFFFFF") : ("#FFFFFF", "#1F1F1F");
    }
}

//  弹窗基类

public abstract class NotifPopupWindow : Window
{
    protected readonly string Text;
    protected readonly string BgColor;
    protected readonly int BgAlpha;
    protected readonly string TextColor;
    protected readonly int FontSize;
    protected readonly int FontWeight;
    public Action? Finished { get; set; }

    protected NotifPopupWindow(NotificationRequest request, bool mouseThrough)
    {
        Text = request.Content;
        BgColor = request.BgColor;
        BgAlpha = request.BgAlpha;
        TextColor = request.TextColor;
        FontSize = request.FontSize;
        FontWeight = request.FontWeight;

        SystemDecorations = global::Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        FontFamily = Glimpseon.UI.Common.AppFontFamily;

        var weight = MapFontWeight();
        CreateContent(weight);

        Opened += (_, _) =>
        {
            var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (handle != nint.Zero)
            {
                Native.ForceTopmost(handle);
                if (mouseThrough)
                {
                    Native.SetMouseThrough(handle, true);
                }
            }
            OnOpenedCustom();
        };
        Closed += (_, _) =>
        {
            OnClosedCustom();
            Finished?.Invoke();
        };
    }

    protected abstract void CreateContent(FontWeight weight);

    protected virtual void OnOpenedCustom()
    {
    }

    protected virtual void OnClosedCustom()
    {
    }

    // 全屏背景色板
    protected static Brush MakeBgBrush(string colorHex, int alpha)
    {
        try
        {
            var color = Color.Parse(colorHex);
            return new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), color.R, color.G, color.B));
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Debug($"[通知] 颜色解析失败 用默认: {e.Message}");
            return new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), 0, 0, 0));
        }
    }

    // 主屏工作区
    protected PixelRect WorkingArea()
    {
        var screen = Screens?.Primary;
        return screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1040);
    }

    // 字重映射
    protected global::Avalonia.Media.FontWeight MapFontWeight() => FontWeight switch
    {
        0 => global::Avalonia.Media.FontWeight.Normal,
        2 => global::Avalonia.Media.FontWeight.Black,
        _ => global::Avalonia.Media.FontWeight.Bold,
    };

    protected TextBlock MakeLabel(bool wrap = false)
    {
        return new TextBlock
        {
            Text = Text,
            Foreground = new SolidColorBrush(ParseColor(TextColor)),
            FontSize = FontSize,
            FontWeight = MapFontWeight(),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
    }

    protected static Color ParseColor(string hex)
    {
        try
        {
            return Color.Parse(hex);
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Debug($"[通知] 颜色解析失败 用白色: {e.Message}");
            return Colors.White;
        }
    }
}

//  滚动横幅 

public sealed class ScrollBannerWindow : NotifPopupWindow
{
    private readonly int _pixelsPerSecond;
    private readonly int _durationSeconds;
    private readonly int _bgHeight;
    private readonly bool _mouseThrough;
    private Canvas _canvas = null!;
    private TextBlock _label = null!;
    private DispatcherTimer? _timer;
    private Stopwatch _stopwatch = null!;
    private double _scrollOffset;
    private double _lastTime;
    private bool _mustFinish;
    private int _completedLoops;

    public ScrollBannerWindow(NotificationRequest request, int pixelsPerSecond, int bgHeight, bool mouseThrough)
        : base(request, mouseThrough)
    {
        _pixelsPerSecond = pixelsPerSecond;
        _durationSeconds = request.Duration;
        _bgHeight = bgHeight;
        _mouseThrough = mouseThrough;
    }

    protected override void CreateContent(FontWeight weight)
    {
        var bg = new Border { Background = MakeBgBrush(BgColor, BgAlpha) };
        _canvas = new Canvas { ClipToBounds = true };
        _label = new TextBlock
        {
            Text = Text,
            Foreground = new SolidColorBrush(ParseColor(TextColor)),
            FontSize = FontSize,
            FontWeight = weight,
        };
        _canvas.Children.Add(_label);
        Content = new Panel { Children = { bg, _canvas } };
    }

    protected override void OnOpenedCustom()
    {
        var wa = WorkingArea();
        Position = new PixelPoint(wa.X, wa.Y + 60);
        Width = wa.Width;
        Height = _bgHeight;
        MinWidth = wa.Width;
        MinHeight = _bgHeight;
        MaxWidth = wa.Width;
        MaxHeight = _bgHeight;

        _label.Measure(Size.Infinity);
        var labelW = _label.DesiredSize.Width;
        var labelH = _label.DesiredSize.Height;

        _scrollOffset = wa.Width;
        var vOffset = Math.Max(0, (int)(_bgHeight - labelH) / 2);
        Canvas.SetLeft(_label, _scrollOffset);
        Canvas.SetTop(_label, vOffset);

        // 滚动16ms
        _stopwatch = Stopwatch.StartNew();
        _lastTime = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => ScrollStep();
        _timer.Start();
        Log.Debug($"滚动动画已启动 速度 {_pixelsPerSecond}px/s时长 {_durationSeconds}s");

        _ = Task.Delay(TimeSpan.FromSeconds(_durationSeconds)).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _mustFinish = true;
                if (_completedLoops >= 1)
                {
                    _timer?.Stop();
                    Close();
                }
            });
        });
    }

    private void ScrollStep()
    {
        var now = _stopwatch.Elapsed.TotalSeconds;
        var dt = now - _lastTime;
        _lastTime = now;
        if (dt <= 0)
        {
            return;
        }

        _scrollOffset -= _pixelsPerSecond * dt;
        var labelW = _label.DesiredSize.Width;
        if (_scrollOffset + labelW <= 0)
        {
            _completedLoops++;
            if (_mustFinish)
            {
                _timer?.Stop();
                Close();
                return;
            }
            _scrollOffset = Width;
        }
        Canvas.SetLeft(_label, (int)_scrollOffset);
    }

    protected override void OnClosedCustom()
    {
        _timer?.Stop();
        Log.Debug($"滚动横幅关闭 {_completedLoops}轮");
    }
}

//  全屏弹窗

public sealed class FullscreenPopupWindow : NotifPopupWindow
{
    private readonly int _durationSeconds;

    public FullscreenPopupWindow(NotificationRequest request) : base(request, mouseThrough: false)
    {
        _durationSeconds = request.Duration;
    }

    protected override void CreateContent(FontWeight weight)
    {
        var bg = new Border { Background = MakeBgBrush(BgColor, BgAlpha) };
        var label = MakeLabel(wrap: true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        Content = new Panel { Children = { bg, label } };
    }

    protected override void OnOpenedCustom()
    {
        var wa = WorkingArea();
        Position = new PixelPoint(wa.X, wa.Y);
        Width = wa.Width;
        Height = wa.Height;
        MinWidth = wa.Width;
        MinHeight = wa.Height;
        MaxWidth = wa.Width;
        MaxHeight = wa.Height;

        _ = Task.Delay(TimeSpan.FromSeconds(_durationSeconds)).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(Close);
        });
    }
}

//  右下角弹窗

public sealed class CornerPopupWindow : NotifPopupWindow
{
    // 纵向堆叠
    private static int _activeCount;

    private const int AnimMs = 260;
    private const int MarginPx = 16;

    private readonly int _durationSeconds;
    private Border _card = null!;
    private TranslateTransform _translate = null!;
    private DispatcherTimer? _closeTimer;
    private DispatcherTimer? _animTimer;
    private bool _closing;

    public CornerPopupWindow(NotificationRequest request) : base(request, mouseThrough: false)
    {
        _durationSeconds = Math.Clamp(request.Duration, 3, 60);
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    protected override void CreateContent(FontWeight weight)
    {
        var title = new TextBlock
        {
            Text = Constants.AppName,
            Foreground = new SolidColorBrush(ParseColor(TextColor)),
            FontSize = 12,
            FontWeight = global::Avalonia.Media.FontWeight.Normal,
            Opacity = 0.55,
        };
        var body = new TextBlock
        {
            Text = Text,
            Foreground = new SolidColorBrush(ParseColor(TextColor)),
            FontSize = Math.Clamp(FontSize, 14, 40),
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
        };
        _card = new Border
        {
            Background = MakeBgBrush(BgColor, Math.Min(BgAlpha + 30, 255)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 13, 18, 15),
            Child = new StackPanel { Spacing = 4, Children = { title, body } },
        };
        _translate = new TranslateTransform();
        _card.RenderTransform = _translate;
        _card.Opacity = 0;
        Content = _card;
        // 点击任意处关闭
        PointerReleased += (_, _) => BeginClose();
    }

    protected override void OnOpenedCustom()
    {
        _translate.Y = 48;
        Dispatcher.UIThread.Post(() =>
        {
            var wa = WorkingArea();
            var step = (int)Math.Ceiling(Bounds.Height) + 12;
            var offset = _activeCount * step;
            _activeCount++;
            Position = new PixelPoint(
                wa.Right - (int)Math.Ceiling(Bounds.Width) - MarginPx,
                wa.Bottom - (int)Math.Ceiling(Bounds.Height) - MarginPx - offset);
            PlayTo(1, 0, null);
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_durationSeconds) };
            _closeTimer.Tick += (_, _) => BeginClose();
            _closeTimer.Start();
        });
    }

    protected override void OnClosedCustom()
    {
        _closeTimer?.Stop();
        _animTimer?.Stop();
        _activeCount = Math.Max(0, _activeCount - 1);
    }

    private void BeginClose()
    {
        if (_closing)
        {
            return;
        }
        _closing = true;
        _closeTimer?.Stop();
        PlayTo(0, 48, Close);
    }

    // 滑入滑出 opacity+Y 同步插值
    private void PlayTo(double toOpacity, double toY, Action? done)
    {
        _animTimer?.Stop();
        var fromOpacity = _card.Opacity;
        var fromY = _translate.Y;
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, watch.Elapsed.TotalMilliseconds / AnimMs);
            var e = 1 - Math.Pow(1 - t, 3);
            _card.Opacity = fromOpacity + (toOpacity - fromOpacity) * e;
            _translate.Y = fromY + (toY - fromY) * e;
            if (t >= 1)
            {
                timer.Stop();
                done?.Invoke();
            }
        };
        _animTimer = timer;
        timer.Start();
    }
}

//  管理器

public class NotificationManager
{
    private readonly List<Window> _activeWindows = [];
    private string _ttsVoice = "zh-CN-XiaoxiaoNeural";
    private int _ttsRate = 100;
    private int _ttsVolume = 100;

    public Action? NotificationFinished { get; set; }

    // 分发通知
    public void HandleNotification(NotificationRequest data)
    {
        _ttsVoice = data.TtsVoice;
        _ttsRate = data.TtsRate;
        _ttsVolume = data.TtsVolume;

        if (string.IsNullOrEmpty(data.Content))
        {
            Log.Warning("通知内容为空");
            return;
        }

        Log.Info($"显示通知: type={data.Type} 时长={data.Duration}s 内容: {data.Content[..Math.Min(40, data.Content.Length)]}");
        SpeakText(data.Content);

        switch (data.Type)
        {
            case NotifType.Corner:
                ShowCorner(data);
                break;
            case NotifType.Scroll:
                ShowScroll(data);
                break;
            case NotifType.Fullscreen:
                ShowFullscreen(data);
                break;
            default:
                Log.Warning($"未知通知类型: {data.Type}");
                break;
        }
    }

    private void ShowScroll(NotificationRequest data)
    {
        // 速度档位映射
        var pxSpeed = 30 + (data.Speed - 1) * 20;
        Dispatcher.UIThread.Post(() =>
        {
            var banner = new ScrollBannerWindow(data, pxSpeed, Config.ScrollBannerBgHeight.Value, Config.ScrollBannerMouseThrough.Value);
            Log.Info($"横幅速度 {pxSpeed}px/s 时长 {data.Duration}s 背景 {data.BgColor}/{data.BgAlpha} 文字 {data.TextColor}/{data.FontSize}px/字重{data.FontWeight}");
            Present(banner);
        });
    }

    private void ShowFullscreen(NotificationRequest data)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var popup = new FullscreenPopupWindow(data);
            Log.Info($"全屏通知 {data.Duration}s 背景 {data.BgColor}/{data.BgAlpha} 文字 {data.TextColor}/{data.FontSize}px/字重{data.FontWeight}");
            Present(popup);
        });
    }

    private void Present(NotifPopupWindow popup)
    {
        // 弹窗关闭时回收并派发下一条
        popup.Finished = () =>
        {
            _activeWindows.Remove(popup);
            OnNotificationFinished();
        };
        _activeWindows.Add(popup);
        popup.Show();
    }


    private void ShowCorner(NotificationRequest data)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var popup = new CornerPopupWindow(data);
            Log.Info($"右下角弹窗 时长 {data.Duration}s 背景 {data.BgColor}/{data.BgAlpha} 文字 {data.TextColor}/{data.FontSize}px 内容: {data.Content[..Math.Min(40, data.Content.Length)]}");
            popup.Finished = () => _activeWindows.Remove(popup);
            _activeWindows.Add(popup);
            popup.Show();
        });
    }

    private void OnNotificationFinished()
    {
        NotificationFinished?.Invoke();
    }

    //  TTS

    private static void FormatTtsPercent(int value, out string formatted)
    {
        var delta = value - 100;
        formatted = (delta >= 0 ? "+" : "") + delta + "%";
    }

    private void SpeakText(string text)
    {
        if (_ttsVoice == "done")
        {
            Log.Info("TTS 已禁用");
            return;
        }
        FormatTtsPercent(_ttsRate, out var rate);
        FormatTtsPercent(_ttsVolume, out var volume);
        Log.Info($"TTS 合成 voice={_ttsVoice} rate={rate} volume={volume}");

        var voice = _ttsVoice;
        _ = Task.Run(async () =>
        {
            var tempDir = Path.GetTempPath();
            var textFile = Path.Combine(tempDir, $"tts_text_{Guid.NewGuid().ToString("N")[..8]}.txt");
            var tempFile = Path.Combine(tempDir, $"tts_{Guid.NewGuid().ToString("N")[..8]}.mp3");
            try
            {
                await File.WriteAllTextAsync(textFile, text);
                var args = $"--voice {voice} --rate {rate} --volume {volume} -f \"{textFile}\" --write-media \"{tempFile}\"";

                var venvPython = Path.Combine(Paths.DataRoot, "tts-venv", "Scripts", "python.exe");
                var (ok, err) = File.Exists(venvPython)
                    ? await RunProcessAsync(venvPython, $"-m edge_tts {args}")
                    : (false, "");
                if (!ok)
                {
                    (ok, err) = await RunProcessAsync("python", $"-m edge_tts {args}");
                }
                if (!ok)
                {
                    (ok, err) = await RunProcessAsync("edge-tts", args);
                }
                if (!ok || !File.Exists(tempFile))
                {
                    Log.Error($"TTS 生成失败: {err}");
                    TryDelete(tempFile);
                    return;
                }
                Log.Debug($"TTS 已合成 {text.Length}字符 {tempFile}");
                await Dispatcher.UIThread.InvokeAsync(() => PlayAudio(tempFile));
            }
            catch (Exception e)
            {
                Log.Error($"TTS 生成失败: {e.Message}");
                TryDelete(tempFile);
            }
            finally
            {
                TryDelete(textFile);
            }
        });
    }

    private static async Task<(bool Ok, string Error)> RunProcessAsync(string fileName, string arguments)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
            };
            proc.Start();
            var errTask = proc.StandardError.ReadToEndAsync();
            var outTask = proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var err = await errTask;
            await outTask;
            return (proc.ExitCode == 0, err);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    private Windows.Media.Playback.MediaPlayer? _player;
    private string _currentAudioFile = "";

    // 播放 TTS
    private void PlayAudio(string filePath)
    {
        try
        {
            StopPlayer();
            if (_currentAudioFile.Length > 0 && File.Exists(_currentAudioFile))
            {
                TryDelete(_currentAudioFile);
            }
            _currentAudioFile = filePath;

            _player = new Windows.Media.Playback.MediaPlayer();
            _player.MediaEnded += (_, _) => Dispatcher.UIThread.Post(OffAudio);
            _player.MediaFailed += (_, args) =>
            {
                Log.Error($"播放失败: {args.ErrorMessage}");
                Dispatcher.UIThread.Post(OffAudio);
            };
            _player.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(filePath));
            Log.Info($"播放 TTS {filePath}");
            _player.Play();
        }
        catch (Exception e)
        {
            Log.Error($"播放失败: {e.Message}");
            OffAudio();
        }
    }

    // 关闭播放 清理临时文件
    public void OffAudio()
    {
        StopPlayer();
        if (_currentAudioFile.Length > 0 && File.Exists(_currentAudioFile))
        {
            DeleteWithRetry(_currentAudioFile, 0);
            _currentAudioFile = "";
        }
        Log.Debug("音频播放已清理");
    }

    private void StopPlayer()
    {
        if (_player is not null)
        {
            try
            {
                _player.Pause();
                _player.Source = null;
            }
            catch (Exception e)
            {

                Glimpseon.Core.Log.Debug($"[通知] 资源释放失败: {e.Message}");
                // 释放失败忽略
            }
            _player = null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Debug($"[通知] 文件删除失败: {e.Message}");
            // 删除失败忽略
        }
    }

    private static void DeleteWithRetry(string path, int attempt)
    {
        try
        {
            File.Delete(path);
            Log.Debug($"已删除临时文件: {path}");
        }
        catch (Exception) when (attempt < 3)
        {
            _ = Task.Delay(300 * (attempt + 1)).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() => DeleteWithRetry(path, attempt + 1)));
        }
        catch (Exception)
        {
            Log.Warning($"临时文件删除失败 {path}");
        }
    }
}
