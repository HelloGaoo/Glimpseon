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

// MediaPlayerWidget
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Media.Imaging;
using IOPath = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Threading;
using Glimpseon.Core.Services;
using Markdown.Avalonia;
using Avalonia.Input.Platform;
using System.Runtime.InteropServices;
using AvColor = Avalonia.Media.Color;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Utilities;
using Avalonia.VisualTree;


namespace Glimpseon.UI;

public sealed class MediaPlayerWidget : WidgetCardBase
{
    private sealed record MediaDetail(Lyrics? Lyrics, byte[]? Cover, long Duration);

    private readonly Border _coverHost = new();
    private readonly StackPanel _appPanel = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = WidgetSpace.GapXs,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _appDot = new()
    {
        Width = 6,
        Height = 6,
        CornerRadius = new CornerRadius(3),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _appText = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _artistText = new();
    private readonly TextBlock _lyricsText = new();
    private readonly TextBlock _timeText = new();
    private readonly MediaProgressBar _wave = new();
    private readonly StackPanel _centerBlock = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = WidgetSpace.GapXs,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Grid _bottomRow;
    private readonly MediaTransportButton _btnPrev;
    private readonly MediaTransportButton _btnPlay;
    private readonly MediaTransportButton _btnNext;

    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _progTimer;

    private MediaInfo? _media;
    private Lyrics? _lyrics;
    private string _lastTa = "";
    private bool _hasThumb;
    private bool _playing;
    private long _position;
    private long _duration;
    private bool _polling;
    private bool _controlBusy;
    private int _detailVersion;
    private long _stateHoldUntil;
    private readonly Dictionary<string, MediaDetail> _infoCache = new();

    public MediaPlayerWidget(ComponentDefinition definition) : base(definition)
    {
        // 左封面 + 右信息: 来源/标题/歌手/歌词/波形/控制键
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = 16,
        };

        _coverHost.CornerRadius = new CornerRadius(10);
        _coverHost.BorderThickness = new Thickness(1);
        _coverHost.ClipToBounds = true;
        _coverHost.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_coverHost, 0);
        root.Children.Add(_coverHost);

        var right = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 2,
            Margin = new Thickness(0, 1, 0, 1),
        };
        // 关键: 右侧信息必须放在第 2 列, 否则会和封面叠在同一格
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        _appText.FontSize = WidgetType.Micro;
        _appText.FontWeight = WidgetWeight.Emphasis;
        _appPanel.Children.Add(_appDot);
        _appPanel.Children.Add(_appText);
        Grid.SetRow(_appPanel, 0);
        right.Children.Add(_appPanel);

        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.TextWrapping = TextWrapping.Wrap;
        _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        _titleText.MaxLines = 2;
        Grid.SetRow(_titleText, 1);
        right.Children.Add(_titleText);

        _artistText.VerticalAlignment = VerticalAlignment.Center;
        _artistText.TextWrapping = TextWrapping.NoWrap;
        _artistText.TextTrimming = TextTrimming.CharacterEllipsis;
        _artistText.MaxLines = 1;
        Grid.SetRow(_artistText, 2);
        right.Children.Add(_artistText);

        _lyricsText.TextWrapping = TextWrapping.NoWrap;
        _lyricsText.TextTrimming = TextTrimming.CharacterEllipsis;
        _lyricsText.MaxLines = 1;
        _lyricsText.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            },
        };
        _centerBlock.Children.Add(_lyricsText);
        Grid.SetRow(_centerBlock, 3);
        right.Children.Add(_centerBlock);

        // 进度条独立一行 紧贴控制键上方 比伪波形更安静
        _wave.Margin = new Thickness(0, 3, 0, 2);
        Grid.SetRow(_wave, 4);
        right.Children.Add(_wave);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapSm,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _btnPrev = new MediaTransportButton(false);
        _btnPlay = new MediaTransportButton(true);
        _btnNext = new MediaTransportButton(false);
        btnRow.Children.Add(_btnPrev);
        btnRow.Children.Add(_btnPlay);
        btnRow.Children.Add(_btnNext);

        _timeText.TextAlignment = TextAlignment.Right;
        _timeText.HorizontalAlignment = HorizontalAlignment.Stretch;
        _timeText.VerticalAlignment = VerticalAlignment.Center;
        _timeText.TextTrimming = TextTrimming.CharacterEllipsis;
        _timeText.MaxLines = 1;
        _timeText.Margin = new Thickness(8, 0, 0, 0);

        _bottomRow = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(btnRow, 0);
        Grid.SetColumn(_timeText, 1);
        _bottomRow.Children.Add(btnRow);
        _bottomRow.Children.Add(_timeText);
        Grid.SetRow(_bottomRow, 5);
        right.Children.Add(_bottomRow);

        Card.Child = root;
        Content = Card;

        _btnPrev.Clicked += () => _ = ControlAsync("prev");
        _btnPlay.Clicked += () => _ = TogglePlayAsync();
        _btnNext.Clicked += () => _ = ControlAsync("next");

        // 轮询间隔 = cfg.mediaUpdateInterval 秒
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, Config.MediaUpdateInterval.Value)) };
        _pollTimer.Tick += (_, _) => _ = PollAsync();
        _progTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progTimer.Tick += (_, _) => UpdateProgress();

        _wave.DarkProvider = () => CardIsDark;
        Config.ShowMediaInfo.ValueChanged += _ => IsVisible = Config.ShowMediaInfo.Value;
        Config.ShowMediaCover.ValueChanged += _ =>
        {
            _coverHost.IsVisible = Config.ShowMediaCover.Value;
            ApplyStyle();
        };
        Config.ShowMediaLyrics.ValueChanged += _ =>
        {
            UpdateLyrics(_position);
            UpdateProgressUi();
        };
        Config.MediaCoverBorderRadius.ValueChanged += _ => ApplyStyle();
        Config.MediaCoverBorderColor.ValueChanged += _ => ApplyStyle();
        Config.MediaUseCustomBg.ValueChanged += _ =>
        {
            if (!Config.MediaUseCustomBg.Value)
            {
                ApplyCardStyle();
            }
            ApplyStyle();
        };
        Config.MediaBgOpacity.ValueChanged += _ => ApplyStyle();
        Config.MediaBorderRadius.ValueChanged += _ => ApplyStyle();
        Config.MediaUpdateInterval.ValueChanged += _ =>
            _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Config.MediaUpdateInterval.Value));
        Config.ThemeColor.ValueChanged += _ =>
        {
            _wave.InvalidateVisual();
            if (!_hasThumb)
            {
                SetDefaultCover();
            }
            ApplyStyle();
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyStyle();
            if (!_hasThumb)
            {
                SetDefaultCover();
            }
        };
        SizeChanged += (_, _) => ApplyStyle();
        ApplyStyle();
        UpdatePlayState();
        IsVisible = Config.ShowMediaInfo.Value;
        _pollTimer.Start();
        _progTimer.Start();
        _ = PollAsync();
    }

    // 默认封面: 强调色薄雾 + 唱片环 + 音符
    private void SetDefaultCover(bool muted = false)
    {
        var dark = CardIsDark;
        var p = WidgetSurfacePalette.Resolve(dark);
        var accent = CardAccent.Value;
        var tintA = muted ? (byte)16 : (byte)44;
        var tintB = muted ? (byte)3 : (byte)8;
        var ink = new SolidColorBrush(p.Text, muted ? 0.22 : 0.42);
        var ringInk = new SolidColorBrush(p.Text, muted ? 0.10 : 0.16);
        var cover = _coverHost.Width > 1 ? _coverHost.Width : 140;

        var grid = new Grid();
        var background = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(tintA, accent.R, accent.G, accent.B), 0),
                    new GradientStop(Color.FromArgb(tintB, accent.R, accent.G, accent.B), 1),
                },
            },
        };
        var ring = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = cover * 0.66,
            Height = cover * 0.66,
            Stroke = ringInk,
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var note = new TextBlock
        {
            Text = "♪",
            FontSize = cover * 0.40,
            Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        grid.Children.Add(background);
        grid.Children.Add(ring);
        grid.Children.Add(note);
        _coverHost.Background = null;
        _coverHost.Child = grid;
    }

    private async Task LoadCoverAsync(byte[]? data)
    {
        if (data is not { Length: > 0 })
        {
            return;
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Bitmap bitmap;
                try
                {
                    using var ms = new MemoryStream(data);
                    bitmap = new Bitmap(ms);
                }
                catch (Exception)
                {
                    Log.Warning($"[MP] 封面数据解码失败 ({data.Length}B)");
                    return;
                }
                _hasThumb = true;
                _coverHost.Transitions = null;
                _coverHost.Opacity = 0;
                _coverHost.Child = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                _coverHost.Transitions = new Transitions
                {
                    new DoubleTransition
                    {
                        Property = Visual.OpacityProperty,
                        Duration = TimeSpan.FromMilliseconds(300),
                        Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
                    },
                };
                _coverHost.Opacity = 1;
            });
        }
        catch (Exception e)
        {
            Log.Debug($"[MP] 应用封面失败 控件已卸载: {e.Message}");
        }
    }

    private static string DisplayAppName(string? app)
    {
        var raw = (app ?? "").Trim();
        var key = raw.ToLowerInvariant();
        if (key.Contains("cloudmusic") || key.Contains("netease"))
        {
            return AppUtils.Tr("media.source_netease");
        }
        if (key.Contains("qqmusic") || key is "qq音乐" or "qq音乐播放器")
        {
            return AppUtils.Tr("media.source_qq");
        }
        if (key.Contains("kugou") || key.Contains("酷狗"))
        {
            return AppUtils.Tr("media.source_kugou");
        }
        if (key is "gsmtc" or "smtc")
        {
            return AppUtils.Tr("media.source_system");
        }
        return raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
    }

    private static string FormatArtist(string? artist)
    {
        var raw = (artist ?? "").Trim();
        if (raw.Length == 0)
        {
            return "";
        }
        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? string.Join(" / ", parts) : raw;
    }

    private void ApplyStyle()
    {
        var dark = CardIsDark;
        var p = WidgetSurfacePalette.Resolve(dark);
        // 字号与封面按卡高等比 基准 200 高
        var h = Bounds.Height > 1 ? Bounds.Height : 200;
        var w = Bounds.Width > 1 ? Bounds.Width : 400;
        var k = Math.Clamp(h / 200.0, 0.62, 1.5);
        var compact = h < 150 || w < 340;
        var cover = Math.Clamp(Math.Min(h - (compact ? 24 : 34), w * (compact ? 0.28 : 0.32)), 56, 128);
        _coverHost.Width = cover;
        _coverHost.Height = cover;
        _coverHost.CornerRadius = new CornerRadius(Math.Clamp(Config.MediaCoverBorderRadius.Value, 8, 18));
        var coverBorder = ParseRgba(Config.MediaCoverBorderColor.Value);
        _coverHost.BorderBrush = coverBorder is { } cb
            ? new SolidColorBrush(cb)
            : new SolidColorBrush(p.Text, dark ? 0.10 : 0.08);
        _coverHost.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetY = 4,
            Blur = 16,
            Color = Color.FromArgb(dark ? (byte)85 : (byte)36, 0, 0, 0),
        });
        _coverHost.IsVisible = Config.ShowMediaCover.Value;

        var ink = p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        var faint = new SolidColorBrush(ink, CardInk.FaintOpacity);
        _appText.Foreground = muted;
        _appText.FontSize = Math.Clamp(Math.Round(WidgetType.Micro * k, 1), 9.5, WidgetType.Label);
        _appText.FontWeight = WidgetWeight.Emphasis;
        _titleText.Foreground = new SolidColorBrush(ink);
        var titleSize = Math.Clamp(Math.Round(WidgetType.Title * k, 1), WidgetType.Body, 22);
        _titleText.FontSize = titleSize;
        _titleText.LineHeight = WidgetType.LineHeight(titleSize, WidgetType.TightLineHeight);
        _titleText.FontWeight = WidgetWeight.Title;
        _titleText.MaxLines = compact ? 1 : 2;
        _titleText.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        _artistText.Foreground = muted;
        _artistText.FontSize = Math.Clamp(Math.Round(WidgetType.Label * k, 1), 10, WidgetType.Title);
        _lyricsText.Foreground = muted;
        _lyricsText.FontSize = Math.Clamp(Math.Round(WidgetType.Label * k, 1), 10, 15);
        _lyricsText.FontWeight = WidgetWeight.Body;
        _timeText.Foreground = new SolidColorBrush(ink, WidgetInk.DimOpacity);
        _timeText.FontSize = Math.Clamp(Math.Round(WidgetType.Micro * k, 1), 9.5, WidgetType.Label);
        _timeText.FontWeight = WidgetWeight.Emphasis;
        _wave.Height = 6;
        _wave.InvalidateVisual();

        var playSize = Math.Clamp(32 * k, 26, 44);
        var sideSize = Math.Clamp(28 * k, 22, 36);
        _btnPlay.Configure(playSize, playSize * 0.44, CardAccent.Value, OnAccentColor(CardAccent.Value));
        _btnPrev.Configure(sideSize, sideSize * 0.64, CardAccent.Value, ink);
        _btnNext.Configure(sideSize, sideSize * 0.64, CardAccent.Value, ink);

        // 自定义背景模式
        if (Config.MediaUseCustomBg.Value)
        {
            var c = dark ? Color.FromRgb(30, 30, 30) : Colors.White;
            Card.Background = new SolidColorBrush(c, Math.Clamp(Config.MediaBgOpacity.Value / 100.0, 0, 1));
            Card.CornerRadius = new CornerRadius(Config.MediaBorderRadius.Value);
            Card.BorderThickness = new Thickness(0);
        }
        else
        {
            Card.BorderThickness = new Thickness(1);
        }

        if (!_hasThumb)
        {
            SetDefaultCover();
        }
        UpdatePlayState();
    }

    private static Color OnAccentColor(Color accent)
    {
        var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        return luminance > 0.62 ? Color.FromRgb(22, 22, 22) : Colors.White;
    }

    private void UpdatePlayState()
    {
        // FluentIcon PLAY/PAUSE_BOLD 等价字形
        _btnPlay.Glyph = _playing ? "\uE769" : "\uE768";
        _btnPrev.Glyph = "\uE892";
        _btnNext.Glyph = "\uE893";
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _appDot.Background = _media is null
            ? new SolidColorBrush(p.Text, 0.25)
            : _playing ? CardAccent.Brush() : new SolidColorBrush(p.Text, 0.35);
        var has = _media is not null;
        _btnPrev.IsHitTestVisible = has;
        _btnPlay.IsHitTestVisible = has;
        _btnNext.IsHitTestVisible = has;
        _btnPrev.Opacity = has ? 1 : 0.35;
        _btnNext.Opacity = has ? 1 : 0.35;
        _btnPlay.Opacity = has ? 1 : 0.45;
    }

    // 媒体配色沿用 #RRGGBBAA 书写习惯, 与 Avalonia 的 #AARRGGBB 区分开
    private static Color? ParseRgba(string? hex)
    {
        var s = (hex ?? "").Trim();
        if (s.StartsWith('#'))
        {
            s = s[1..];
        }
        if (s.Length == 8)
        {
            try
            {
                var r = Convert.ToByte(s[..2], 16);
                var g = Convert.ToByte(s[2..4], 16);
                var b = Convert.ToByte(s[4..6], 16);
                var a = Convert.ToByte(s[6..8], 16);
                return Color.FromArgb(a, r, g, b);
            }
            catch
            {
                return null;
            }
        }
        return TryParseColor(hex);
    }

    private async Task PollAsync()
    {
        if (_polling)
        {
            return;
        }
        _polling = true;
        MediaInfo? m = null;
        try
        {
            // 网易云首次读取要扫 40MB dll, 放后台线程避免卡 UI
            m = await Task.Run(async () =>
            {
                var info = await MediaServices.GetMediaInfoAsync();
                return info is not null && info.IsValid() ? info : null;
            });
        }
        catch (Exception e)
        {
            Log.Error($"媒体信息获取异常: {e.Message}");
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => OnMedia(m));
        }
        finally
        {
            _polling = false;
        }
    }

    private void OnMedia(MediaInfo? m)
    {
        if (!Config.ShowMediaInfo.Value)
        {
            return;
        }
        if (m is null)
        {
            NoMedia();
            return;
        }
        _media = m;
        var newSong = m.TitleArtist != _lastTa;
        if (newSong)
        {
            DisplayNewSong(m);
        }
        else
        {
            SyncPosition(m.PositionMs, m.IsPlaying);
            // 刚点过播放/暂停时先保持本地乐观状态, 等系统会话追上再换图标
            if (Environment.TickCount64 >= _stateHoldUntil)
            {
                _playing = m.IsPlaying;
            }
        }
        if (m.DurationMs > 0)
        {
            _duration = m.DurationMs;
        }
        UpdatePlayState();
        UpdateProgressUi();
        if (_lyrics is not null)
        {
            UpdateLyrics(_position);
        }
        _coverHost.IsVisible = Config.ShowMediaCover.Value;
    }

    // 系统时间轴偶尔是旧快照: 小回跳直接忽略, 大跳变才认作拖动/切段
    private void SyncPosition(long reportedMs, bool playing)
    {
        var reported = Math.Max(0, reportedMs);
        if (!playing)
        {
            if (Math.Abs(reported - _position) > 2500)
            {
                _position = reported;
            }
            return;
        }
        if (reported > _position + 700 || reported < _position - 3000)
        {
            _position = reported;
        }
    }

    // 新歌
    private void DisplayNewSong(MediaInfo m)
    {
        _lastTa = m.TitleArtist;
        _detailVersion++;
        _stateHoldUntil = 0;
        _position = Math.Max(0, m.PositionMs);
        _playing = m.IsPlaying;
        _duration = Math.Max(0, m.DurationMs);
        _hasThumb = false;

        // 淡入复位 + 默认封面
        _coverHost.Transitions = null;
        _coverHost.Opacity = 1;
        SetDefaultCover();
        _lyrics = null;
        _lyricsText.Text = "";
        _lyricsText.Opacity = 1;
        _lyricsText.IsVisible = false;
        _centerBlock.IsVisible = true;
        _wave.Progress = 0;

        var appName = DisplayAppName(m.AppName);
        _appText.Text = appName;
        _appPanel.IsVisible = appName.Length > 0;

        var title = m.Title.Length > 0 ? m.Title : AppUtils.Tr("media.unknown_song");
        _titleText.Text = title;
        _artistText.Text = FormatArtist(m.Artist);
        _artistText.IsVisible = _artistText.Text.Length > 0;
        Log.Info($"[MP] 新曲 {appName} · {title} · {_artistText.Text} · song_id={m.SongId} · pos={_position}ms");

        UpdatePlayState();
        if (m.ThumbnailData is { Length: > 0 })
        {
            _ = LoadCoverAsync(m.ThumbnailData);
        }
        if (_infoCache.TryGetValue(m.TitleArtist, out var cached))
        {
            ApplyDetail(cached);
        }
        else
        {
            _ = FetchDetailAsync(m, _detailVersion);
        }
    }

    private void NoMedia()
    {
        if (_media is not null)
        {
            Log.Info("[MP] 无媒体播放 重置为空状态");
        }
        _appText.Text = "";
        _appPanel.IsVisible = false;
        _titleText.Text = AppUtils.Tr("media.not_playing");
        _artistText.Text = AppUtils.Tr("media.play_hint");
        _artistText.IsVisible = _artistText.Text.Length > 0;
        _lyricsText.Text = "";
        _lyricsText.IsVisible = false;
        _wave.Progress = 0;
        _wave.IsVisible = false;
        _timeText.Text = "";
        _timeText.IsVisible = false;
        _centerBlock.IsVisible = false;
        _media = null;
        _lyrics = null;
        _lastTa = "";
        _hasThumb = false;
        _stateHoldUntil = 0;
        _playing = false;
        _duration = 0;
        _position = 0;
        _detailVersion++;
        SetDefaultCover(muted: true);
        _coverHost.IsVisible = Config.ShowMediaCover.Value;
        UpdatePlayState();
    }

    // 进度秒推
    private void UpdateProgress()
    {
        if (_playing && _duration > 0)
        {
            _position = Math.Min(_position + 1000, _duration);
        }
        UpdateProgressUi();
        if (_lyrics is not null)
        {
            UpdateLyrics(_position);
        }
    }

    private void UpdateProgressUi()
    {
        var hasDuration = _duration > 0;
        var hasMedia = _media is not null;
        _wave.Progress = hasDuration ? Math.Min(1, _position / (double)_duration) : 0;
        _wave.IsVisible = hasMedia;
        _timeText.Text = hasMedia
            ? hasDuration ? $"{Fmt(_position)} / {Fmt(_duration)}" : Fmt(_position)
            : "";
        _timeText.IsVisible = hasMedia && _timeText.Text.Length > 0;
        _centerBlock.IsVisible = hasMedia;
    }

    private static string Fmt(long ms)
    {
        var total = Math.Max(0, ms / 1000);
        if (total >= 3600)
        {
            return $"{total / 3600}:{total % 3600 / 60:D2}:{total % 60:D2}";
        }
        return $"{total / 60}:{total % 60:D2}";
    }

    private void UpdateLyrics(long ms)
    {
        var text = "";
        if (Config.ShowMediaLyrics.Value && _lyrics is not null && !_lyrics.IsEmpty())
        {
            var advance = Config.MediaLyricsAdvance.Value;
            var (_, idx) = _lyrics.GetLineAtTime(ms + advance);
            if (idx >= 0 && idx < _lyrics.Lines.Count)
            {
                text = _lyrics.Lines[idx].Text;
            }
        }
        if (text != _lyricsText.Text)
        {
            _lyricsText.Opacity = 0;
            _lyricsText.Text = text;
            _lyricsText.IsVisible = text.Length > 0;
            Dispatcher.UIThread.Post(() => _lyricsText.Opacity = 1);
        }
        else
        {
            _lyricsText.IsVisible = text.Length > 0;
        }
        _centerBlock.IsVisible = _media is not null;
    }

    // 歌曲详情后台抓取
    private async Task FetchDetailAsync(MediaInfo m, int version)
    {
        MediaDetail result = new(null, null, 0);
        try
        {
            var svc = MediaServices.GetService(m.AppName);
            if (svc is not null)
            {
                var lyrics = Config.ShowMediaLyrics.Value ? await svc.LyricsAsync(m) : null;
                var cover = await svc.CoverAsync(m);
                var dur = await svc.DurationAsync(m);
                result = new MediaDetail(lyrics, cover is { Length: > 0 } ? cover : null, dur);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[MP] 获取歌曲信息失败: {e.Message}");
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version != _detailVersion)
            {
                return;
            }
            _infoCache[m.TitleArtist] = result;
            if (_infoCache.Count > 60)
            {
                _infoCache.Remove(_infoCache.Keys.First());
            }
            Log.Info($"[MP] 详情完成 cover={(result.Cover is not null)} lyrics={(result.Lyrics is not null)} dur={result.Duration}ms");
            if (_media is not null && _media.TitleArtist == m.TitleArtist)
            {
                ApplyDetail(result);
            }
        });
    }

    private void ApplyDetail(MediaDetail result)
    {
        if (result.Duration > 0)
        {
            _duration = result.Duration;
        }
        if (Config.ShowMediaCover.Value && !_hasThumb && result.Cover is not null)
        {
            _ = LoadCoverAsync(result.Cover);
        }
        else if (result.Cover is not null && !_hasThumb)
        {
            _ = LoadCoverAsync(result.Cover);
        }
        if (result.Lyrics is not null)
        {
            _lyrics = result.Lyrics;
            UpdateLyrics(_position);
        }
        UpdateProgressUi();
    }

    // 播放/暂停
    private async Task TogglePlayAsync()
    {
        if (_controlBusy)
        {
            return;
        }
        _controlBusy = true;
        var targetPlaying = !_playing;
        _playing = targetPlaying;
        _stateHoldUntil = Environment.TickCount64 + 1600;
        UpdatePlayState();
        try
        {
            var ok = await MediaServices.MediaControlAsync(targetPlaying ? "play" : "pause", _media?.AppName);
            if (!ok)
            {
                Log.Warning($"[MP] 控制命令 '{(targetPlaying ? "play" : "pause")}' 未确认");
                // 命令没被系统接受时立刻回退图标, 不要让乐观状态停住
                _playing = !targetPlaying;
                _stateHoldUntil = 0;
                UpdatePlayState();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[MP] 控制命令执行失败: {e.Message}");
        }
        finally
        {
            _controlBusy = false;
        }
        await Task.Delay(600);
        await PollAsync();
    }

    // 切上下曲
    private async Task ControlAsync(string action)
    {
        if (_controlBusy)
        {
            return;
        }
        _controlBusy = true;
        _coverHost.Transitions = null;
        _coverHost.Opacity = 1;
        try
        {
            var ok = action switch
            {
                "next" => await MediaServices.MediaNextAsync(_media?.AppName),
                "prev" => await MediaServices.MediaPrevAsync(_media?.AppName),
                _ => await MediaServices.MediaControlAsync(action, _media?.AppName),
            };
            if (!ok)
            {
                Log.Warning($"[MP] 控制命令 '{action}' 未确认");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[MP] 控制命令 '{action}' 执行失败: {e.Message}");
        }
        finally
        {
            _controlBusy = false;
        }
        await Task.Delay(600);
        await PollAsync();
    }
}
