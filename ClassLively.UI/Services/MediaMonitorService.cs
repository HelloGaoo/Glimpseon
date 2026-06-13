using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using ClassLively_UI.Models;

namespace ClassLively_UI.Services;

/// <summary>
/// 媒体监听服务 调用media.py
/// </summary>
public sealed class MediaMonitorService
{
    // ════════════════════════════════════════════════
    //  单例
    // ════════════════════════════════════════════════

    private static readonly Lazy<MediaMonitorService> _instance =
        new(() => new MediaMonitorService());

    public static MediaMonitorService Instance => _instance.Value;

    // ════════════════════════════════════════════════
    //  内部状态
    // ════════════════════════════════════════════════

    private IApiService? _api;
    private Microsoft.UI.Xaml.DispatcherTimer? _pollTimer;
    private Action<MediaInfoModel?>? _callback;
    private MediaInfoModel? _lastMedia;

    private MediaMonitorService() { }

    /// <summary>注入 API </summary>
    public void SetApi(IApiService api) => _api = api;

    // ════════════════════════════════════════════════
    //  轮询机制
    // ════════════════════════════════════════════════

    /// <summary>启动定时轮询 媒体变化时触发 callback</summary>
    public void StartPolling(Action<MediaInfoModel?> callback, int intervalMs = 5000)
    {
        if (_api == null)
        {
            Debug.WriteLine("[MediaMonitorService] 未注入 IApiService");
            return;
        }

        StopPolling();

        _callback = callback;
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _pollTimer.Tick += async (_, _) => await PollOnceAsync();
        _pollTimer.Start();

        // 首次立即查询
        _ = PollOnceAsync();

        Debug.WriteLine($"[MediaMonitorService] 轮询已启动, 间隔 {intervalMs}ms");
    }

    /// <summary>停止轮询</summary>
    public void StopPolling()
    {
        if (_pollTimer != null)
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= async (_, _) => await PollOnceAsync();
            _pollTimer = null;
        }
        _callback = null;
        _lastMedia = null;
        Debug.WriteLine("[MediaMonitorService] 轮询已停止");
    }

    // ════════════════════════════════════════════════
    //  内部辅助
    // ════════════════════════════════════════════════

    private async Task PollOnceAsync()
    {
        if (_api == null) return;

        try
        {
            var current = await _api.GetMediaInfoAsync();

            // 变化检测：比较 Title + Artist + SongId
            var currentKey = $"{current.Title}|{current.Artist}|{current.SongId}";
            var lastKey = _lastMedia != null ? $"{_lastMedia.Title}|{_lastMedia.Artist}|{_lastMedia.SongId}" : null;

            if (currentKey != lastKey)
            {
                _lastMedia = current;
                _callback?.Invoke(current);
            }
            else if (_lastMedia != null)
            {
                // 同一首歌但位置/状态可能变了，更新内部引用供外部使用
                _lastMedia.PositionMs = current.PositionMs;
                _lastMedia.IsPlaying = current.IsPlaying;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaMonitorService] 轮询异常: {ex.Message}");
        }
    }
}
