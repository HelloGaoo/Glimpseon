using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Text;
using ClassLively_UI.Services;
using ClassLively_UI.Models;

namespace ClassLively_UI.Controls;

/// <summary>
/// 媒体信息组件
/// </summary>
public sealed partial class MediaWidget : UserControl
{
    //  UI 元素引用
    private Image _coverImage = null!;
    private Border _coverBorder = null!;
    private TextBlock _titleText = null!;
    private TextBlock _artistText = null!;
    private TextBlock _lyricsText = null!;
    private ProgressBar _progressBar = null!;
    private TextBlock _timeLabel = null!;
    private TextBlock _durationLabel = null!;
    private StackPanel _infoPanel = null!;

    //  状态 
    private string? _lastTitleArtist;     // 上次歌曲标识
    private int _durationMs;             // 当前歌曲时长
    private int _positionMs;             // 当前播放位置
    private bool _isPlaying;
    private readonly Dictionary<string, object> _cache = new(); // LRU 缓存
    private const int MaxCacheSize = 50;

    // 定时器
    private readonly DispatcherTimer _progressTimer;   // 进度条更新定时器

    // 媒体监听服务
    private readonly MediaMonitorService _monitor = MediaMonitorService.Instance;

    // 封面淡入动画
    private Storyboard? _coverFadeStoryboard;

    //  配置参数
    public int CoverSize { get; set; } = 72;
    public double TextSize { get; set; } = 13;
    public int UpdateIntervalSec { get; set; } = 5;

    /// <summary>API 服务注入</summary>
    public IApiService? Api { get; set; }

    public MediaWidget()
    {
        InitializeComponent();
        ApplyDefaultStyle();

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progressTimer.Tick += OnProgressTick;
    }

    /// <summary>启动轮询</summary>
    public void Start()
    {
        _monitor.SetApi(Api);
        _monitor.StartPolling(OnMediaFetched, UpdateIntervalSec * 1000);
        _progressTimer.Start();
    }

    /// <summary>停止轮询</summary>
    public void Stop()
    {
        _monitor.StopPolling();
        _progressTimer.Stop();
    }

    //  数据回调 — 对应原版 _on_fetched

    /// <summary>处理媒体信息结果</summary>
    private void OnMediaFetched(MediaInfoModel? media)
    {
        if (media == null || !media.IsValid())
        {
            ShowNoMedia();
            return;
        }

        var titleArtist = $"{media.Title} - {media.Artist}";
        var isNewSong = titleArtist != _lastTitleArtist;

        // 更新基础信息
        _titleText.Text = media.Title ?? "";
        _artistText.Text = media.Artist ?? "";
        _isPlaying = media.IsPlaying;
        _positionMs = media.PositionMs;
        _durationMs = media.DurationMs;
        UpdateProgressDisplay();

        if (isNewSong)
        {
            _lastTitleArtist = titleArtist;

            // 新歌 → 获取详情（封面+歌词）
            _ = FetchSongDetailAsync(media.Title, media.Artist);

            // 如有缩略图就加载
            if (!string.IsNullOrEmpty(media.ThumbnailBase64))
            {
                var thumbBytes = DecodeBase64(media.ThumbnailBase64);
                if (thumbBytes != null)
                    LoadCoverFromBytes(thumbBytes);
            }
        }
    }

    /// <summary>获取歌曲详情（封面+歌词</summary>
    private async Task FetchSongDetailAsync(string? title, string? artist)
    {
        if (string.IsNullOrEmpty(title)) return;

        var cacheKey = $"{title} - {artist}";

        // LRU 缓存命中检查
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            // 移到末尾
            _cache.Remove(cacheKey);
            _cache[cacheKey] = cached;
            ApplySongDetail(cached as Dictionary<string, object?>);
            return;
        }

        try
        {
            if (Api == null) return;
            var detail = await Api.GetMediaDetailAsync(title, artist);

            if (detail != null)
            {
                // 存入缓存
                _cache[cacheKey] = detail;
                if (_cache.Count > MaxCacheSize)
                {
                    foreach (var key in _cache.Keys)
                    {
                        _cache.Remove(key);
                        break;
                    }
                }

                ApplySongDetail(detail);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaWidget] 获取歌曲详情失败: {ex.Message}");
        }
    }

    /// <summary>应用歌曲详情到UI（封面+歌词）</summary>
#pragma warning disable CS8604 // detail 字典来自 JSON 反序列化
    private void ApplySongDetail(Dictionary<string, object?>? detail)
    {
        if (detail == null) return;

        // 加载封面（base64）
        if (detail.TryGetValue("cover_base64", out var coverB64Obj) && coverB64Obj is string coverB64 && !string.IsNullOrEmpty(coverB64))
        {
            var coverBytes = DecodeBase64(coverB64);
            if (coverBytes != null)
                LoadCoverFromBytes(coverBytes);
        }

        // 加载歌词
        if (detail.TryGetValue("lyrics", out var lyricsObj) && lyricsObj is string lrcText && !string.IsNullOrEmpty(lrcText))
        {
            // 当前时间对应的歌词行
            var lines = lrcText.Split('\n', StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('['))
                {
                    _lyricsText.Text = line.Trim();
                    break;
                }
            }
        }
    }
#pragma warning restore CS8604

    /// <summary>base64 字符串 → byte[]</summary>
    private static byte[]? DecodeBase64(string b64)
    {
        try
        {
            return Convert.FromBase64String(b64);
        }
        catch
        {
            return null;
        }
    }

    //  封面图

    /// <summary>加载封面</summary>
    private async void LoadCoverFromBytes(byte[] data)
    {
        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new System.IO.MemoryStream(data))
            {
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            }

            // 淡入动画
            PlayCoverFadeInAnimation(() => _coverImage.Source = bitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaWidget] 加载封面失败: {ex.Message}");
            ShowDefaultCover();
        }
    }

    /// <summary>播放封面淡入动画 (300ms, OutCubic)</summary>
    private void PlayCoverFadeInAnimation(Action onComplete)
    {
        // 停止之前的动画
        _coverFadeStoryboard?.Stop();

        var anim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(anim, _coverImage);
        Storyboard.SetTargetProperty(anim, "Opacity");

        _coverFadeStoryboard = new Storyboard();
        _coverFadeStoryboard.Children.Add(anim);
        _coverFadeStoryboard.Completed += (_, _) => onComplete?.Invoke();
        _coverFadeStoryboard.Begin();

        // 没有完成回调就直接设置
        if (_coverFadeStoryboard == null)
            onComplete?.Invoke();
    }

    /// <summary>显示默认占位封面</summary>
    private void ShowDefaultCover()
    {
        _coverImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.png"));
        _coverImage.Opacity = 1.0;
    }

    //  进度条更新

    private void OnProgressTick(object sender, object e)
    {
        if (!_isPlaying || _durationMs <= 0) return;

        // 先做模拟 todo从python的core/原媒体处理转过来 
        _positionMs += 1000;
        if (_positionMs > _durationMs) _positionMs = _durationMs;
        UpdateProgressDisplay();
    }

    private void UpdateProgressDisplay()
    {
        if (_durationMs > 0)
        {
            _progressBar.Value = Math.Min(100, (_positionMs * 100.0 / _durationMs));
            _timeLabel.Text = FormatMs(_positionMs);
            _durationLabel.Text = FormatMs(_durationMs);
        }
        else
        {
            _progressBar.Value = 0;
            _timeLabel.Text = "0:00";
            _durationLabel.Text = "0:00";
        }
    }

    private static string FormatMs(int ms)
    {
        var totalSec = ms / 1000;
        var min = totalSec / 60;
        var sec = totalSec % 60;
        return $"{min}:{sec:D2}";
    }

    //  无媒体状态

    private void ShowNoMedia()
    {
        _titleText.Text = "未在播放";
        _artistText.Text = "";
        _lyricsText.Text = "";
        _progressBar.Value = 0;
        _timeLabel.Text = "0:00";
        _durationLabel.Text = "0:00";
        _isPlaying = false;
        _lastTitleArtist = null;
        ShowDefaultCover();
    }

    //  WCAG 对比度自适应
    //  公式: Y = 0.299*R + 0.587*G + 0.114*B
    //  阈值: 160 → 亮背景用黑字，暗背景用白字
    //  三级透明度: 标题100% / 艺术家66% / 时间80%

    /// <summary>根据背景色自适应文字颜色</summary>
    public void AdaptTextColor(byte r, byte g, byte b)
    {
        var brightness = (r * 299 + g * 587 + b * 114) / 1000.0;
        var isLightBg = brightness > 160;

        // 亮背景→黑字，暗背景→白字
        var baseColor = isLightBg ? ColorHelper.FromRgb(0, 0, 0) : ColorHelper.White;

        _titleText.Foreground = new SolidColorBrush(baseColor);                                          // 100%
        _artistText.Foreground = new SolidColorBrush(ColorHelper.FromRgba(baseColor.R, baseColor.G, baseColor.B, 102));  // 40% ≈ 0x66
        _timeLabel.Foreground = new SolidColorBrush(ColorHelper.FromRgba(baseColor.R, baseColor.G, baseColor.B, 204));   // 80% ≈ 0xCC
        _durationLabel.Foreground = _timeLabel.Foreground;
        _lyricsText.Foreground = new SolidColorBrush(ColorHelper.FromRgba(baseColor.R, baseColor.G, baseColor.B, 153));  // 60% ≈ 0x99
    }


    //  默认样式

    private void ApplyDefaultStyle()
    {
        // 默认深色主题配色
        AdaptTextColor(30, 30, 32); // 深色背景
    }

    //  XAML 初始化

    private void InitializeComponent()
    {
        // 主布局：左封面 右信息
        var mainLayout = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18,
            Padding = new Thickness(16, 14, 16, 12),
            VerticalAlignment = VerticalAlignment.Center
        };

        // ── 左侧：封面图 ──
        _coverBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Width = CoverSize,
            Height = CoverSize,
            BorderBrush = new SolidColorBrush(ColorHelper.FromRgba(255, 255, 255, 25)),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(ColorHelper.FromRgba(255, 255, 255, 25)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        _coverImage = new Image
        {
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        _coverBorder.Child = _coverImage;

        // ── 右侧：信息列 ──
        _infoPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 2, 0, 0)
        };

        _titleText = new TextBlock
        {
            Text = "未在播放",
            FontSize = TextSize + 2,
            FontWeight = new FontWeight { Weight = 600 },
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 250
        };

        _artistText = new TextBlock
        {
            Text = "",
            FontSize = TextSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 250,
            Opacity = 0.66
        };

        _lyricsText = new TextBlock
        {
            Text = "",
            FontSize = TextSize - 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 280,
            Opacity = 0.6,
            Margin = new Thickness(0, 4, 0, 0)
        };

        // ── 进度条区域 ──
        var progressContainer = new Border
        {
            Padding = new Thickness(0, 4, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            }
        };
        var progressStack = (StackPanel)progressContainer.Child;

        _timeLabel = new TextBlock
        {
            Text = "0:00",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8
        };

        _progressBar = new ProgressBar
        {
            MinWidth = 80,
            Height = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        _durationLabel = new TextBlock
        {
            Text = "0:00",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8
        };

        progressStack.Children.Add(_timeLabel);
        progressStack.Children.Add(_progressBar);
        progressStack.Children.Add(_durationLabel);

        // 组装右侧面板
        _infoPanel.Children.Add(_titleText);
        _infoPanel.Children.Add(_artistText);
        _infoPanel.Children.Add(_lyricsText);
        _infoPanel.Children.Add(progressContainer);

        // 组装主布局
        mainLayout.Children.Add(_coverBorder);
        mainLayout.Children.Add(_infoPanel);

        Content = mainLayout;
    }
}
