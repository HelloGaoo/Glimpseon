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

// 媒体服务

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Glimpseon.Core.Win32;

namespace Glimpseon.Core.Services;

public sealed class MediaInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string TitleArtist { get; set; } = "";
    public byte[]? ThumbnailData { get; set; }
    public string PlaybackStatus { get; set; } = "stopped";
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public bool IsPlaying { get; set; }
    public string AppName { get; set; } = "";
    public string SongId { get; set; } = "";

    public bool IsValid() => !string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Artist) || !string.IsNullOrEmpty(SongId);
}

public sealed record LyricLine(long TimeMs, string Text);

public sealed class Lyrics
{
    public IReadOnlyList<LyricLine> Lines { get; init; } = Array.Empty<LyricLine>();
    public string RawLrc { get; init; } = "";
    public long? SongId { get; init; }

    public bool IsEmpty() => Lines.Count == 0;

    // 返回时刻所在行与行索引
    public (LyricLine? Line, int Index) GetLineAtTime(long timeMs)
    {
        if (IsEmpty())
        {
            return (null, -1);
        }
        for (var i = Lines.Count - 1; i >= 0; i--)
        {
            if (Lines[i].TimeMs <= timeMs)
            {
                return (Lines[i], i);
            }
        }
        return (Lines[0], 0);
    }
}

public static class LrcParser
{
    private static readonly Regex TimeTag = new(@"\[(\d{1,2}):(\d{1,2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

    // 解析 LRC 文本 返回按时间排序的行
    public static List<LyricLine> ParseLrc(string lrcText)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in lrcText.Split('\n'))
        {
            var text = TimeTag.Replace(raw, "").Trim();
            if (text.Length == 0)
            {
                continue;
            }
            foreach (Match m in TimeTag.Matches(raw))
            {
                try
                {
                    var mins = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    var secs = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    var msStr = m.Groups[3].Value;
                    var ms = msStr.Length > 0 ? int.Parse(msStr, CultureInfo.InvariantCulture) : 0;
                    if (msStr.Length == 2) ms *= 10;
                    else if (msStr.Length == 1) ms *= 100;
                    lines.Add(new LyricLine(mins * 60000 + secs * 1000 + ms, text));
                }
                catch (FormatException)
                {
                    Log.Debug($"parse_lrc: 无效时间标签 {m.Value}");
                }
            }
        }
        lines.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return lines;
    }
}

public interface IMediaSource : IDisposable
{
    string Name { get; }
    bool Available { get; }
    Task<MediaInfo?> ReadAsync();
    Task<Lyrics?> LyricsAsync(MediaInfo media);
    Task<byte[]?> CoverAsync(MediaInfo media);
    Task<long> DurationAsync(MediaInfo media);
    Task<bool> ControlAsync(string action);
}

public abstract class MediaSourceBase : IMediaSource
{
    public abstract string Name { get; }
    public abstract bool Available { get; }
    public abstract Task<MediaInfo?> ReadAsync();
    public abstract Task<Lyrics?> LyricsAsync(MediaInfo media);
    public abstract Task<byte[]?> CoverAsync(MediaInfo media);
    public abstract Task<long> DurationAsync(MediaInfo media);
    public abstract Task<bool> ControlAsync(string action);

    protected abstract void CloseResources();

    public void Dispose()
    {
        try
        {
            CloseResources();
        }
        catch (Exception e)
        {
            Log.Warning($"关闭媒体源 [{Name}] 失败: {e.Message}");
        }
        GC.SuppressFinalize(this);
    }
}

// LRU 缓存
internal class LruCache<TValue>(int capacity = 50)
{
    private readonly Dictionary<string, TValue> _cache = new();
    private readonly List<string> _order = new();

    public bool TryGet(string key, out TValue value) => _cache.TryGetValue(key, out value!);

    public void Set(string key, TValue value)
    {
        if (_cache.Count >= capacity && !_cache.ContainsKey(key))
        {
            _cache.Remove(_order[0]);
            _order.RemoveAt(0);
        }
        _cache[key] = value;
        _order.Remove(key);
        _order.Add(key);
    }
}

internal sealed record SongDetail(long SongId, string Name, List<string> Artists, string AlbumName, string CoverUrl, long Duration);

// 网易云音乐源

public sealed class NeteaseCloudMusicSource : MediaSourceBase
{
    public override string Name => "NeteaseCloudMusic";

    private static readonly Dictionary<string, (nint Current, nint SongArray)> V2Offsets = new()
    {
        ["2.7.1.1669"] = (0x8C8AF8, 0x8E9044),
        ["2.10.3.3613"] = (0xA39550, 0xAE8F80),
        ["2.10.5.3929"] = (0xA47548, 0xAF6FC8),
        ["2.10.6.3993"] = (0xA65568, 0xB15654),
        ["2.10.7.4239"] = (0xA66568, 0xB16974),
        ["2.10.8.4337"] = (0xA74570, 0xB24F28),
        ["2.10.10.4509"] = (0xA77580, 0xB282CC),
        ["2.10.10.4689"] = (0xA79580, 0xB2AD10),
        ["2.10.11.4930"] = (0xA7A580, 0xB2BCB0),
        ["2.10.12.5241"] = (0xA7A580, 0xB2BCB0),
        ["2.10.13.6067"] = (0xA7A590, 0xB2BCD0),
    };

    private static readonly byte[] V3SchedulePattern =
    [
        0x66, 0x0F, 0x2E, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x7A, 0x00, 0x75, 0x00, 0x66, 0x0F, 0x2E, 0x15,
    ];
    private static readonly byte[] V3PlayerPattern =
    [
        0x48, 0x8D, 0x0D, 0x00, 0x00, 0x00, 0x00, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8D, 0x0D, 0x00, 0x00, 0x00, 0x00, 0xE8,
        0x00, 0x00, 0x00, 0x00, 0x90, 0x48, 0x8D, 0x0D, 0x00, 0x00, 0x00, 0x00, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8D, 0x05,
        0x00, 0x00, 0x00, 0x00, 0x48, 0x8D, 0xA5, 0x00, 0x00, 0x00, 0x00, 0x5F, 0x5D, 0xC3, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0x48,
        0x89, 0x4C, 0x24, 0x00, 0x55, 0x57, 0x48, 0x81, 0xEC, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8D, 0x6C, 0x24, 0x00, 0x48, 0x8D,
        0x7C, 0x24,
    ];

    private const string ApiBase = "https://music163.xuanmou.com.cn";

    private Native.ProcessMemory? _pm;
    private int _pid;
    private string _version = "";
    private bool _isV3;
    private nint _dllBase;
    private int _dllSize;
    private byte[]? _dllData;
    private nint _schedulePtr;
    private nint _playerPtr;
    private bool _first = true;
    private string _lastId = "";
    private double _lastTime;
    private bool _procFound;
    private string? _lastReadTa;
    private double _apiLastTime;
    private readonly LruCache<object> _cache = new();
    private readonly SemaphoreSlim _apiGate = new(1, 1);

    public override bool Available => true;

    public override Task<MediaInfo?> ReadAsync()
    {
        var data = ReadMemory();
        if (data is null)
        {
            Log.Debug("网易云音乐: 内存读取失败");
            return Task.FromResult<MediaInfo?>(null);
        }
        var (title, artist) = ParseWindowTitle();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
        {
            Log.Debug("网易云音乐: 窗口标题解析失败");
        }
        else
        {
            Log.Debug($"网易云音乐 窗口标题已解析 {title} - {artist}");
        }
        var ta = title.Length > 0 ? $"{title} - {artist}" : "";
        if (ta.Length > 0 && ta != _lastReadTa)
        {
            Log.Debug($"网易云已读取 {ta} 进度 {(long)(data.PlaybackTime * 1000)} ms 播放中={data.IsPlaying}");
            _lastReadTa = ta;
        }
        var info = new MediaInfo
        {
            Title = title,
            Artist = artist,
            TitleArtist = artist.Length > 0 ? $"{title} - {artist}" : title,
            PositionMs = (long)(data.PlaybackTime * 1000),
            IsPlaying = data.IsPlaying,
            PlaybackStatus = data.IsPlaying ? "playing" : "paused",
            AppName = Name,
            SongId = data.SongId,
        };
        return Task.FromResult<MediaInfo?>(info);
    }

    public override async Task<Lyrics?> LyricsAsync(MediaInfo media)
    {
        if (media is null || media.SongId.Length == 0)
        {
            return null;
        }
        var key = $"lyric_{media.SongId}";
        if (_cache.TryGet(key, out var cached))
        {
            return (Lyrics)cached;
        }
        var data = await ApiGetAsync("/lyric", new Dictionary<string, string> { ["id"] = media.SongId });
        if (data is { } lyricRoot)
        {
            var lrc = lyricRoot.TryGetProperty("lrc", out var l) && l.TryGetProperty("lyric", out var ll)
                ? ll.GetString() ?? ""
                : "";
            if (lrc.Length == 0 && lyricRoot.TryGetProperty("tlyric", out var tl) && tl.TryGetProperty("lyric", out var tll))
            {
                lrc = tll.GetString() ?? "";
            }
            if (lrc.Length > 0)
            {
                var lines = LrcParser.ParseLrc(lrc);
                if (lines.Count > 0)
                {
                    var ly = new Lyrics { Lines = lines, RawLrc = lrc, SongId = long.TryParse(media.SongId, out var sid) ? sid : null };
                    Log.Info($"网易云歌词已获取 song_id={media.SongId} {lines.Count}行");
                    _cache.Set(key, ly);
                    return ly;
                }
            }
        }
        Log.Debug($"网易云歌词未命中 song_id={media.SongId}");
        return null;
    }

    public override async Task<byte[]?> CoverAsync(MediaInfo media)
    {
        if (media?.ThumbnailData is { Length: > 0 })
        {
            return media.ThumbnailData;
        }
        if (media is null || media.SongId.Length == 0)
        {
            return null;
        }
        var key = $"cover_{media.SongId}";
        if (_cache.TryGet(key, out var cached))
        {
            return (byte[])cached;
        }
        var detail = await GetDetailAsync(media.SongId);
        if (detail is not null && detail.CoverUrl.Length > 0)
        {
            var bytes = await DownloadAsync(detail.CoverUrl);
            if (bytes is not null)
            {
                Log.Debug($"网易云封面已下载 song_id={media.SongId} {bytes.Length}字节");
                _cache.Set(key, bytes);
                return bytes;
            }
        }
        Log.Debug($"网易云封面未命中 song_id={media.SongId}");
        return null;
    }

    public override async Task<long> DurationAsync(MediaInfo media)
    {
        if (media is { DurationMs: > 0 })
        {
            return media.DurationMs;
        }
        if (media is { SongId.Length: > 0 })
        {
            var detail = await GetDetailAsync(media.SongId);
            if (detail is not null)
            {
                Log.Debug($"网易云时长补全 song_id={media.SongId} {detail.Duration} ms");
                return detail.Duration;
            }
        }
        Log.Debug("网易云时长补全失败");
        return 0;
    }

    public override Task<bool> ControlAsync(string action)
    {
        Log.Debug($"网易云音乐源不支持控制命令 {action}");
        return Task.FromResult(false);
    }

    protected override void CloseResources()
    {
        _pm?.Dispose();
        _pm = null;
        _apiGate.Dispose();
    }

    // 内存读取

    private record ReadResult(string SongId, double PlaybackTime, bool IsPlaying);

    private ReadResult? ReadMemory()
    {
        try
        {
            var pidAlive = false;
            if (_pm is not null)
            {
                try
                {
                    var proc = Process.GetProcessById(_pid);
                    pidAlive = !proc.HasExited;
                }
                catch (Exception e)
                {

                    Glimpseon.Core.Log.Debug($"[MEDIA] 进程状态检查失败: {e.Message}");
                    pidAlive = false;
                }
            }

            if (!pidAlive)
            {
                _pm?.Dispose();
                _pm = null;
                _first = true;
                if (!FindProcess(out _pid, out _version))
                {
                    if (_procFound)
                    {
                        Log.Info("网易云音乐: 丢失 cloudmusic.exe 进程");
                        _procFound = false;
                    }
                    return null;
                }
                if (!_procFound)
                {
                    Log.Info($"网易云 cloudmusic.exe pid={_pid} 版本={(_version.Length > 0 ? _version : "未知")}");
                    _procFound = true;
                }
            }

            _isV3 = _version.StartsWith('3');
            if (!_isV3 && _version.Length > 0 && !V2Offsets.ContainsKey(_version))
            {
                Log.Warning($"网易云音乐: 不支持的版本 {_version}");
                return null;
            }

            _pm ??= new Native.ProcessMemory();
            if (!_pm.Open(_pid))
            {
                Log.Warning("网易云音乐: OpenProcess 失败");
                return null;
            }

            if (_first)
            {
                (_dllBase, _dllSize) = GetDllInfo();
                if (_isV3)
                {
                    _dllData = _pm.ReadBytes(_dllBase, _dllSize);
                    (_schedulePtr, _playerPtr) = ScanV3();
                    if (_schedulePtr == 0)
                    {
                        Log.Warning($"网易云音乐: V3 AOB扫描失败 (版本 {_version})");
                        return null;
                    }
                }
                else if (_dllBase == 0)
                {
                    return null;
                }
                _first = false;
            }

            double playbackTime;
            string songId;
            if (_isV3)
            {
                playbackTime = _pm.ReadF64(_schedulePtr);
                songId = ReadV3Id();
            }
            else
            {
                var offsets = V2Offsets[_version];
                playbackTime = _pm.ReadF64(_dllBase + offsets.Current);
                var arr = _pm.ReadU64(_dllBase + offsets.SongArray);
                var raw = _pm.ReadBytes((nint)arr, 0x14);
                songId = raw is null ? "" : Encoding.Unicode.GetString(raw).Split('_')[0];
            }

            if (!Regex.IsMatch(songId, @"\d+"))
            {
                return null;
            }

            var isPlaying = songId != _lastId || Math.Abs(playbackTime - _lastTime) >= 0.01;
            _lastId = songId;
            _lastTime = playbackTime;

            return new ReadResult(songId, playbackTime, isPlaying);
        }
        catch (Exception e)
        {
            Log.Error($"内存读取失败: {e.Message}");
            return null;
        }
    }

    // 定位主进程
    private bool FindProcess(out int pid, out string version)
    {
        pid = 0;
        version = "";
        foreach (var proc in Process.GetProcessesByName("cloudmusic"))
        {
            try
            {
                var exe = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(exe))
                {
                    continue;
                }
                foreach (ProcessModule mod in proc.Modules)
                {
                    if (!mod.ModuleName.Equals("cloudmusic.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    _dllBase = mod.BaseAddress;
                    _dllSize = mod.ModuleMemorySize;
                    pid = proc.Id;
                    version = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "";
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Debug($"获取版本信息失败: {e.Message}");
            }
            finally
            {
                proc.Dispose();
            }
        }
        return false;
    }

    private (nint Base, int Size) GetDllInfo() => (_dllBase, _dllSize);

    private (nint Schedule, nint Player) ScanV3()
    {
        var addr = AobScan(V3SchedulePattern);
        if (addr == 0)
        {
            return (0, 0);
        }
        var schedule = addr + 4 + _pm!.ReadI32(addr + 4) + 4;

        addr = AobScan(V3PlayerPattern);
        if (addr == 0)
        {
            return (0, 0);
        }
        var player = addr + 3 + _pm.ReadI32(addr + 3) + 4;
        Log.Debug($"V3 AOB 命中 schedule_ptr=0x{schedule:X} player_ptr=0x{player:X}");
        return (schedule, player);
    }

    // AOB 扫描 0x00 通配
    private nint AobScan(byte[] pattern)
    {
        if (_dllData is null)
        {
            return 0;
        }
        var data = _dllData;
        var plen = pattern.Length;
        for (var i = 0; i <= data.Length - plen; i++)
        {
            var match = true;
            for (var j = 0; j < plen; j++)
            {
                if (pattern[j] != 0x00 && data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return _dllBase + i;
            }
        }
        return 0;
    }

    private string ReadV3Id()
    {
        if (_playerPtr == 0)
        {
            return "";
        }
        try
        {
            var info = _pm!.ReadU64(_playerPtr + 0x50);
            if (info == 0)
            {
                return "";
            }
            var ptr = (nint)(info + 0x10);
            var length = _pm.ReadU64(ptr + 0x10);
            if (length <= 0)
            {
                return "";
            }
            byte[] raw;
            if (length <= 15)
            {
                var shortRaw = _pm.ReadBytes(ptr, (int)length);
                if (shortRaw is null)
                {
                    return "";
                }
                raw = shortRaw;
            }
            else
            {
                var longRaw = _pm.ReadBytes((nint)_pm.ReadU64(ptr), (int)Math.Min(length, 128));
                if (longRaw is null)
                {
                    return "";
                }
                raw = longRaw;
            }
            var s = Encoding.UTF8.GetString(raw);
            var idx = s.IndexOf('_');
            return idx >= 0 ? s[..idx] : "";
        }
        catch (Exception e)
        {
            Log.Debug($"读取字符串失败: {e.Message}");
            return "";
        }
    }

    // 读主窗口标题
    private (string Title, string Artist) ParseWindowTitle()
    {
        try
        {
            var hwnd = Native.FindWindowByClass("OrpheusBrowserHost");
            if (hwnd == nint.Zero)
            {
                return ("", "");
            }
            var title = Native.GetWindowText(hwnd)?.Trim();
            if (string.IsNullOrEmpty(title) || title.Length <= 2 || !title.Contains(" - "))
            {
                return ("", "");
            }
            var idx = title.LastIndexOf(" - ", StringComparison.Ordinal);
            if (idx <= 0)
            {
                return ("", "");
            }
            var left = title[..idx].Trim();
            var right = title[(idx + 3)..].Trim();
            if (right.Length > 0 && left != "网易云音乐")
            {
                return (left, right);
            }
            return ("", "");
        }
        catch (Exception e)
        {
            Log.Debug($"读窗口标题失败: {e.Message}");
            return ("", "");
        }
    }

    // API

    private async Task ApiWaitAsync()
    {
        var elapsed = Environment.TickCount64 / 1000.0 - _apiLastTime;
        if (elapsed < 0.1)
        {
            await Task.Delay((int)((0.1 - elapsed) * 1000));
        }
        _apiLastTime = Environment.TickCount64 / 1000.0;
    }

    private async Task<JsonElement?> ApiGetAsync(string endpoint, Dictionary<string, string> parameters)
    {
        try
        {
            await _apiGate.WaitAsync();
            try
            {
                await ApiWaitAsync();
                var query = string.Join("&", parameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
                var url = $"{ApiBase}{endpoint}?{query}";
                using var response = await AppUtils.Http.GetAsync(url);
                if ((int)response.StatusCode != 200)
                {
                    Log.Debug($"网易云api状态码异常 {endpoint} http {(int)response.StatusCode}");
                    return null;
                }
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement.Clone();
                if (root.TryGetProperty("code", out var code) && code.GetInt32() != 200)
                {
                    Log.Debug($"网易云api业务码异常 {endpoint} code={code.GetInt32()}");
                    return null;
                }
                return root;
            }
            finally
            {
                _apiGate.Release();
            }
        }
        catch (Exception e)
        {
            Log.Debug($"api请求失败: {endpoint} - {e.Message}");
            return null;
        }
    }

    private async Task<SongDetail?> GetDetailAsync(string songId)
    {
        var key = $"detail_{songId}";
        if (_cache.TryGet(key, out var cached))
        {
            Log.Debug($"网易云歌曲详情命中缓存 song_id={songId}");
            return (SongDetail)cached;
        }
        var data = await ApiGetAsync("/song/detail", new Dictionary<string, string> { ["ids"] = songId });
        if (data is { } root && root.TryGetProperty("songs", out var songs) && songs.GetArrayLength() > 0)
        {
            var s = songs[0];
            var al = s.TryGetProperty("al", out var a) ? a : default;
            var detail = new SongDetail(
                s.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0,
                s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                s.TryGetProperty("ar", out var ar) ? ar.EnumerateArray().Select(x => x.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "").ToList() : new List<string>(),
                al.ValueKind != JsonValueKind.Undefined && al.TryGetProperty("name", out var aln) ? aln.GetString() ?? "" : "",
                al.ValueKind != JsonValueKind.Undefined && al.TryGetProperty("picUrl", out var pic) ? pic.GetString() ?? "" : "",
                s.TryGetProperty("dt", out var dt) ? dt.GetInt64() : 0);
            _cache.Set(key, detail);
            Log.Debug($"网易云详情已获取 song_id={songId} {detail.Name}");
            return detail;
        }
        Log.Debug($"网易云歌曲详情获取失败 song_id={songId}");
        return null;
    }

    private static async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            using var response = await AppUtils.Http.GetAsync(url);
            if ((int)response.StatusCode == 200 &&
                (response.Content.Headers.ContentType?.ToString().Contains("image") ?? false))
            {
                var data = await response.Content.ReadAsByteArrayAsync();
                if (data.Length > 1024 && data.Length < 10 * 1024 * 1024)
                {
                    return data;
                }
            }
            Log.Debug("网易云封面下载未命中");
        }
        catch (Exception e)
        {
            Log.Debug($"获取封面失败: {e.Message}");
        }
        return null;
    }
}

// 酷狗音乐源

public sealed class KugouMusicSource : MediaSourceBase
{
    public override string Name => "KugouMusic";

    private double _songStartTime;
    private string _lastTitleArtist = "";
    private string? _lastReadTa;
    private int _lastPosTier = -1;
    private readonly Dictionary<string, long> _durationCache = new();
    private readonly Dictionary<string, Lyrics> _lyricCache = new();
    private readonly Dictionary<string, byte[]> _coverCache = new();

    public override bool Available => true;

    public override async Task<MediaInfo?> ReadAsync()
    {
        try
        {
            var (title, artist) = ParseWindowTitle();
            if (title.Length == 0)
            {
                return null;
            }

            var ta = artist.Length > 0 ? $"{title} - {artist}" : title;
            var now = Environment.TickCount64 / 1000.0;

            if (ta != _lastTitleArtist)
            {
                Log.Info($"酷狗 曲目切换 {ta}");
                _lastTitleArtist = ta;
                _songStartTime = now;
            }

            // 酷狗无法获取真实播放进度/暂停 恒为播放中
            var isPlaying = true;

            // 网络取时长
            var durMs = await DurationFromTitle(title, artist);

            var elapsed = now - _songStartTime;
            var positionMs = (long)Math.Max(0, elapsed) * 1000;
            if (durMs > 0 && positionMs > durMs)
            {
                positionMs = durMs;
            }

            var tier = durMs > 0 ? (int)(positionMs * 10 / durMs) : -1;
            if (ta != _lastReadTa || tier != _lastPosTier)
            {
                Log.Debug($"酷狗读取 {ta} 进度 {positionMs}/{durMs} ms 档 {tier}/10");
                _lastReadTa = ta;
                _lastPosTier = tier;
            }

            return new MediaInfo
            {
                Title = title,
                Artist = artist,
                TitleArtist = ta,
                PositionMs = positionMs,
                DurationMs = durMs,
                IsPlaying = isPlaying,
                PlaybackStatus = "playing",
                AppName = "Kugou",
            };
        }
        catch (Exception e)
        {
            Log.Debug($"酷狗读取失败: {e.Message}");
            return null;
        }
    }

    public override async Task<Lyrics?> LyricsAsync(MediaInfo media)
    {
        if (media is null)
        {
            return null;
        }
        var key = $"{media.Title} - {media.Artist}";
        if (_lyricCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        try
        {
            var dur = media.DurationMs > 0 ? media.DurationMs : await DurationFromTitle(media.Title, media.Artist);
            var keyword = $"{media.Artist} - {media.Title}";
            var searchUrl = $"http://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword={Uri.EscapeDataString(keyword)}&duration={dur}&hash=";
            using var resp = await AppUtils.Http.GetAsync(searchUrl);
            if ((int)resp.StatusCode != 200)
            {
                Log.Debug($"酷狗歌词搜索失败 http {(int)resp.StatusCode} {key}");
                return null;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var candidates = doc.RootElement.TryGetProperty("candidates", out var c) ? c : default;
            if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            {
                Log.Debug($"酷狗歌词搜索无候选 {key}");
                return null;
            }
            var first = candidates[0];
            var lyricId = first.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
            var accesskey = first.TryGetProperty("accesskey", out var ak) ? ak.GetString() : null;
            if (lyricId == 0 || string.IsNullOrEmpty(accesskey))
            {
                return null;
            }
            var dlUrl = $"http://lyrics.kugou.com/download?ver=1&client=pc&id={lyricId}&accesskey={accesskey}&fmt=lrc&charset=utf8";
            using var dlResp = await AppUtils.Http.GetAsync(dlUrl);
            if ((int)dlResp.StatusCode != 200)
            {
                Log.Debug($"酷狗歌词下载失败 http {(int)dlResp.StatusCode} {key}");
                return null;
            }
            using var dlDoc = JsonDocument.Parse(await dlResp.Content.ReadAsStringAsync());
            var contentB64 = dlDoc.RootElement.TryGetProperty("content", out var ct) ? ct.GetString() : null;
            if (string.IsNullOrEmpty(contentB64))
            {
                return null;
            }
            var lrcText = Encoding.UTF8.GetString(Convert.FromBase64String(contentB64)).Trim();
            if (lrcText.Length < 10)
            {
                return null;
            }
            var lines = LrcParser.ParseLrc(lrcText);
            if (lines.Count == 0)
            {
                Log.Debug($"酷狗歌词解析为空 {key}");
                return null;
            }
            var ly = new Lyrics { Lines = lines, RawLrc = lrcText, SongId = 0 };
            Log.Info($"酷狗歌词已获取 {key} {lines.Count}行");
            _lyricCache[key] = ly;
            return ly;
        }
        catch (Exception e)
        {
            Log.Debug($"酷狗歌词获取失败: {e.Message}");
            return null;
        }
    }

    public override async Task<byte[]?> CoverAsync(MediaInfo media)
    {
        if (media is null)
        {
            Log.Debug("酷狗封面 无媒体信息");
            return null;
        }
        var key = $"{media.Title} - {media.Artist}";
        if (_coverCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        try
        {
            var lists = await SearchAsync(media.Title, media.Artist);
            if (lists.ValueKind == JsonValueKind.Array && lists.GetArrayLength() > 0)
            {
                var coverUrl = (lists[0].TryGetProperty("Image", out var img) ? img.GetString() ?? "" : "").Replace("/{size}", "");
                if (coverUrl.Length > 0)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, coverUrl);
                    req.Headers.Referrer = new Uri("http://www.kugou.com/");
                    using var cr = await AppUtils.Http.SendAsync(req);
                    if ((int)cr.StatusCode == 200)
                    {
                        var bytes = await cr.Content.ReadAsByteArrayAsync();
                        if (bytes.Length > 1024 && bytes.Length < 10 * 1024 * 1024)
                        {
                            Log.Debug($"酷狗封面已下载 {key} {bytes.Length}字节");
                            _coverCache[key] = bytes;
                            return bytes;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Debug($"酷狗封面获取失败: {e.Message}");
        }
        return null;
    }

    public override async Task<long> DurationAsync(MediaInfo media)
    {
        if (media is { DurationMs: > 0 })
        {
            return media.DurationMs;
        }
        if (media is not null)
        {
            var d = await DurationFromTitle(media.Title, media.Artist);
            Log.Debug($"酷狗时长补全 {media.Title} - {media.Artist} -> {d} ms");
            return d;
        }
        return 0;
    }

    public override Task<bool> ControlAsync(string action)
    {
        Log.Debug($"酷狗源不支持控制命令 {action}");
        return Task.FromResult(false);
    }

    protected override void CloseResources()
    {
        // 无持外资源
    }

    public async Task<long> DurationFromTitle(string title, string artist)
    {
        var cacheKey = $"{title} - {artist}";
        if (_durationCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }
        try
        {
            var lists = await SearchAsync(title, artist);
            if (lists.ValueKind == JsonValueKind.Array && lists.GetArrayLength() > 0 &&
                lists[0].TryGetProperty("Duration", out var durEl))
            {
                var durMs = durEl.GetInt64() * 1000;
                Log.Debug($"酷狗时长命中 {cacheKey} -> {durMs} ms");
                _durationCache[cacheKey] = durMs;
                return durMs;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"获取酷狗时长失败: {e.Message}");
        }
        return 0;
    }

    private static async Task<JsonElement> SearchAsync(string title, string artist)
    {
        var keyword = $"{title} {artist}";
        var url = $"http://songsearch.kugou.com/song_search_v2?keyword={Uri.EscapeDataString(keyword)}&platform=WebFilter&format=json&page=1&pagesize=1";
        try
        {
            using var resp = await AppUtils.Http.GetAsync(url);
            if ((int)resp.StatusCode == 200)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                if (root.TryGetProperty("error_code", out var ec) && ec.GetInt64() == 0 &&
                    root.TryGetProperty("data", out var data) && data.TryGetProperty("lists", out var lists))
                {
                    Log.Debug($"酷狗搜索 {keyword} 命中 {lists.GetArrayLength()}条");
                    return lists.Clone();
                }
                Log.Debug($"酷狗搜索业务码异常 {keyword}");
            }
        }
        catch (Exception e)
        {
            Log.Debug($"酷狗搜索失败 {keyword}: {e.Message}");
        }
        return default;
    }

    private (string Title, string Artist) ParseWindowTitle()
    {
        try
        {
            string? found = null;
            Native.EnumTopWindows((h, _) =>
            {
                var ln = Native.GetWindowText(h);
                if (ln is { Length: > 0 and < 300 } t && t.Contains(" - 酷狗音乐") && !t.Contains("桌面歌词"))
                {
                    found = t;
                    return false;
                }
                return true;
            });
            return found is null ? ("", "") : FixKugouTitle(found);
        }
        catch (Exception e)
        {
            Log.Debug($"枚举窗口失败: {e.Message}");
            return ("", "");
        }
    }

    private static (string Title, string Artist) FixKugouTitle(string raw)
    {
        raw = raw.Replace(" - 酷狗音乐", "");
        if (raw.Length == 0 || !raw.Contains('-'))
        {
            return (raw, "");
        }
        var dash = raw.IndexOf('-', StringComparison.Ordinal);
        var left = raw[..dash].Trim();
        var right = dash + 1 < raw.Length ? raw[(dash + 1)..].Trim() : "";
        if (left.Contains("酷狗"))
        {
            var idx = left.IndexOf("酷狗", StringComparison.Ordinal);
            left = (left[(idx + 2)..].Trim() + left[..idx]).Trim();
        }
        if (left.Contains(" - "))
        {
            var sub = left.LastIndexOf(" - ", StringComparison.Ordinal);
            if (sub > 0)
            {
                return (left[(sub + 3)..].Trim(), left[..sub].Trim());
            }
        }
        return (left, right);
    }
}

// SMTC 源

public sealed class GsmTcSource : MediaSourceBase
{
    public override string Name => "GSMTC";

    public override bool Available => OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= 17763;

    public override async Task<MediaInfo?> ReadAsync()
    {
        try
        {
            var manager = await GetManagerAsync();
            var session = manager?.GetCurrentSession();
            if (session is null)
            {
                return null;
            }
            return await ReadSessionAsync(session);
        }
        catch (Exception e)
        {
            Log.Error($"GSMTC读取失败: {e.Message}");
            return null;
        }
    }

    internal static async Task<MediaInfo?> ReadSessionAsync(Windows.Media.Control.GlobalSystemMediaTransportControlsSession session)
    {
        var info = new MediaInfo();
        try
        {
            var app = session.SourceAppUserModelId;
            info.AppName = !string.IsNullOrEmpty(app) && app.Contains('.') ? app.Split('.')[^1] : app ?? "";
        }
        catch (Exception e)
        {
            Log.Warning($"GSMTC: 获取应用名失败 {e.Message}");
        }

        try
        {
            var pb = session.GetPlaybackInfo();
            if (pb is not null && pb.PlaybackStatus is { } status)
            {
                info.PlaybackStatus = status switch
                {
                    Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => "closed",
                    Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => "opened",
                    Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => "changing",
                    Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "stopped",
                    Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "playing",
                    Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "paused",
                    _ => "unknown",
                };
                info.IsPlaying = status == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"GSMTC: 获取播放状态失败 {e.Message}");
        }

        try
        {
            var tl = session.GetTimelineProperties();
            info.PositionMs = Math.Max(0, (long)tl.Position.TotalMilliseconds);
            info.DurationMs = Math.Max(0, (long)tl.EndTime.TotalMilliseconds);
        }
        catch (Exception e)
        {
            Log.Warning($"GSMTC: 获取时间线失败 {e.Message}");
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props is not null)
            {
                info.Title = props.Title ?? "";
                info.Artist = props.Artist ?? "";
                info.Album = props.AlbumTitle ?? "";
                info.TitleArtist = info.Artist.Length > 0 ? $"{info.Title} - {info.Artist}" : info.Title;

                if (props.Thumbnail is not null)
                {
                    try
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        if (stream.Size > 0 && stream.Size < 10 * 1024 * 1024)
                        {
                            using var net = stream.AsStreamForRead();
                            using var ms = new MemoryStream();
                            await net.CopyToAsync(ms);
                            info.ThumbnailData = ms.ToArray();
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"GSMTC: 读取缩略图失败: {e.GetType().Name}: {e.Message}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"GSMTC: 获取媒体属性失败 {e.Message}");
        }
        return info;
    }

    public override Task<Lyrics?> LyricsAsync(MediaInfo media)
    {
        Log.Debug("GSMTC 源不提供歌词补全");
        return Task.FromResult<Lyrics?>(null);
    }

    public override Task<byte[]?> CoverAsync(MediaInfo media)
    {
        Log.Debug("GSMTC 源不提供封面补全");
        return Task.FromResult<byte[]?>(null);
    }

    public override Task<long> DurationAsync(MediaInfo media)
    {
        Log.Debug("GSMTC 源不提供时长补全");
        return Task.FromResult(0L);
    }

    // 发送play/pause/next/prev
    public override async Task<bool> ControlAsync(string action)
    {
        if (!Available)
        {
            Log.Debug($"GSMTC 源不可用 忽略 {action}");
            return false;
        }
        try
        {
            var manager = await GetManagerAsync();
            var session = manager?.GetCurrentSession();
            if (session is null)
            {
                Log.Debug($"GSMTC 控制命令 {action} 无媒体会话");
                return false;
            }
            Log.Info($"GSMTC: 发送控制命令 {action} -> {session.SourceAppUserModelId ?? "未知会话"}");
            return action switch
            {
                "play" => await session.TryPlayAsync(),
                "pause" => await session.TryPauseAsync(),
                "next" => await session.TrySkipNextAsync(),
                "prev" => await session.TrySkipPreviousAsync(),
                _ => false,
            };
        }
        catch (Exception e)
        {
            Log.Warning($"GSMTC: 控制命令失败 {action}: {e.Message}");
            return false;
        }
    }

    public Task<bool> NextTrackAsync() => ControlAsync("next");

    public Task<bool> PrevTrackAsync() => ControlAsync("prev");

    protected override void CloseResources()
    {
        // 管理器为进程级单例 不显式释放
    }

    internal static async Task<Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager?> GetManagerAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        return await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }
}

// QQ音乐源 + UIA 读进度

public sealed class QQMusicSource : MediaSourceBase
{
    public override string Name => "QQMusic";

    public override bool Available => OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= 17763;

    private nint _qqHwnd;
    private bool _uiaReady;
    private readonly Dictionary<string, long> _durationCache = new();
    private readonly Dictionary<string, Lyrics> _lyricCache = new();
    private static readonly Regex TimeRe = new(@"^(\d{1,2}):(\d{2})$", RegexOptions.Compiled);

    public override async Task<MediaInfo?> ReadAsync()
    {
        try
        {
            var manager = await GsmTcSource.GetManagerAsync();
            if (manager is null)
            {
                return null;
            }
            foreach (var session in manager.GetSessions())
            {
                var appId = session.SourceAppUserModelId ?? "";
                if (!appId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var props = await session.TryGetMediaPropertiesAsync();
                if (props is null)
                {
                    continue;
                }
                var title = props.Title ?? "";
                var artist = props.Artist ?? "";
                var album = props.AlbumTitle ?? "";

                var pb = session.GetPlaybackInfo();
                // 原版 status_val in (1, 4) 即 Opened/Playing
                var status = pb?.PlaybackStatus;
                var isPlaying = status is Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened
                    or Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                var ta = artist.Length > 0 ? $"{title} - {artist}" : title;

                var (uiaPosMs, uiaDurMs) = await Task.Run(ReadUiaProgress);

                var tl = session.GetTimelineProperties();
                var durMs = 0L;
                if (uiaDurMs > 0)
                {
                    durMs = uiaDurMs;
                }
                else if (tl.EndTime > TimeSpan.Zero)
                {
                    durMs = (long)tl.EndTime.TotalMilliseconds;
                }
                if (durMs <= 0)
                {
                    durMs = await GetDurationAsync(title, artist);
                }

                // UIA 读进度失败时回退 SMTC 时间线位置
                var positionMs = uiaPosMs >= 0 ? uiaPosMs : Math.Max(0, (long)tl.Position.TotalMilliseconds);
                if (durMs > 0 && positionMs > durMs)
                {
                    positionMs = durMs;
                }

                var info = new MediaInfo
                {
                    Title = title,
                    Artist = artist,
                    Album = album,
                    TitleArtist = ta,
                    PositionMs = positionMs,
                    DurationMs = durMs,
                    IsPlaying = isPlaying,
                    PlaybackStatus = isPlaying ? "playing" : "paused",
                    AppName = "QQMusic",
                };

                if (props.Thumbnail is not null)
                {
                    try
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        if (stream.Size > 0 && stream.Size < 10 * 1024 * 1024)
                        {
                            using var net = stream.AsStreamForRead();
                            using var ms = new MemoryStream();
                            await net.CopyToAsync(ms);
                            info.ThumbnailData = ms.ToArray();
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Debug($"QQMusic: 获取封面失败: {e.Message}");
                    }
                }
                Log.Debug($"QQ音乐已读取 {ta} 进度 {positionMs}/{durMs} ms 播放中={isPlaying}");
                return info;
            }
            return null;
        }
        catch (Exception e)
        {
            Log.Debug($"QQ音乐读取失败: {e.Message}");
            return null;
        }
    }

    public override async Task<Lyrics?> LyricsAsync(MediaInfo media)
    {
        if (media is null)
        {
            return null;
        }
        var key = $"{media.Title} - {media.Artist}";
        if (_lyricCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        try
        {
            var keyword = $"{media.Artist} - {media.Title}";
            var url = $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_y.fcg?songmid=&g_tk=5381&format=json&incharset=utf8&outcharset=utf-8&nobase64=0&keyword={Uri.EscapeDataString(keyword)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://y.qq.com/");
            using var resp = await AppUtils.Http.SendAsync(req);
            if ((int)resp.StatusCode != 200)
            {
                Log.Debug($"QQ音乐歌词搜索失败 http {(int)resp.StatusCode} {key}");
                return null;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var lyricStr = doc.RootElement.TryGetProperty("lyric", out var l) ? l.GetString() : null;
            if (string.IsNullOrEmpty(lyricStr))
            {
                Log.Debug($"QQ音乐歌词内容为空 {key}");
                return null;
            }
            var lines = LrcParser.ParseLrc(lyricStr);
            if (lines.Count > 0)
            {
                var ly = new Lyrics { Lines = lines, RawLrc = lyricStr, SongId = 0 };
                Log.Info($"QQ音乐歌词已获取 {key} {lines.Count}行");
                _lyricCache[key] = ly;
                return ly;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"解析QQ音乐歌词失败: {e.Message}");
        }
        return null;
    }

    public override Task<byte[]?> CoverAsync(MediaInfo media)
    {
        Log.Debug("QQ音乐源不提供封面补全");
        return Task.FromResult<byte[]?>(null);
    }

    public override Task<long> DurationAsync(MediaInfo media)
    {
        if (media is { DurationMs: > 0 })
        {
            return Task.FromResult(media.DurationMs);
        }
        return media is not null ? GetDurationAsync(media.Title, media.Artist) : Task.FromResult(0L);
    }

    public override Task<bool> ControlAsync(string action)
    {
        Log.Debug($"QQ音乐源不支持控制命令 {action}");
        return Task.FromResult(false);
    }

    protected override void CloseResources()
    {
        // 无持外资源
    }

    // UIA 遍历找 mm:ss 文本控件 按屏幕 x 排序取进度/时长
    private (long PosMs, long DurMs) ReadUiaProgress()
    {
        try
        {
            if (!_uiaReady || _qqHwnd == nint.Zero)
            {
                var hwnd = FindQqHwnd();
                if (hwnd == nint.Zero)
                {
                    return (-1, 0);
                }
                _qqHwnd = hwnd;
                _uiaReady = true;
                Log.Debug($"QQ音乐 UIA 已绑定主窗口 hwnd=0x{hwnd:X}");
            }

            var results = new List<(string Text, int Left)>();
            using var automation = new FlaUI.UIA3.UIA3Automation();
            var win = automation.FromHandle(_qqHwnd);
            Scan(win, results, 0);

            if (results.Count >= 2)
            {
                results.Sort((a, b) => a.Left.CompareTo(b.Left));
                var m1 = TimeRe.Match(results[0].Text);
                var m2 = TimeRe.Match(results[1].Text);
                if (m1.Success && m2.Success)
                {
                    var posS = int.Parse(m1.Groups[1].Value) * 60 + int.Parse(m1.Groups[2].Value);
                    var durS = int.Parse(m2.Groups[1].Value) * 60 + int.Parse(m2.Groups[2].Value);
                    return (posS * 1000L, durS * 1000L);
                }
            }
            else if (results.Count == 1)
            {
                var m = TimeRe.Match(results[0].Text);
                if (m.Success)
                {
                    var posS = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
                    return (posS * 1000L, 0);
                }
            }
            return (-1, 0);
        }
        catch (Exception e)
        {
            Log.Debug($"UIA获取播放进度失败: {e.Message}");
            _uiaReady = false;
            return (-1, 0);
        }
    }

    private static void Scan(FlaUI.Core.AutomationElements.AutomationElement ctrl, List<(string, int)> results, int depth)
    {
        try
        {
            var name = ctrl.Properties.Name.Value ?? "";
            var trimmed = name.Trim();
            if (TimeRe.IsMatch(trimmed) && ctrl.Properties.ControlType.Value == FlaUI.Core.Definitions.ControlType.Text)
            {
                var rect = ctrl.Properties.BoundingRectangle.Value;
                results.Add((trimmed, rect.Left));
            }
            if (depth < 25)
            {
                foreach (var child in ctrl.FindAllChildren())
                {
                    Scan(child, results, depth + 1);
                }
            }
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Debug($"[MEDIA] UIA 控件遍历失败跳过: {e.Message}");
            // 控件遍历失败跳过
        }
    }

    private static nint FindQqHwnd()
    {
        nint best = nint.Zero;
        var bestArea = 0L;
        Native.EnumTopWindows((h, _) =>
        {
            var title = Native.GetWindowText(h) ?? "";
            var cn = Native.GetWindowClassName(h).ToLowerInvariant();
            var tl = title.ToLowerInvariant();
            if (tl.Contains("qq") || (cn.Contains("txgui") && tl.Contains("qq")))
            {
                if (Native.TryGetWindowRect(h, out var rc))
                {
                    var area = (long)(rc.Right - rc.Left) * (rc.Bottom - rc.Top);
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = h;
                    }
                }
            }
            return true;
        });
        if (best != nint.Zero && Native.TryGetWindowRect(best, out var r) &&
            (r.Right - r.Left) > 500 && (r.Bottom - r.Top) > 300)
        {
            Log.Debug($"QQ音乐 找到主窗口 hwnd=0x{best:X}");
            return best;
        }
        return nint.Zero;
    }

    private async Task<long> GetDurationAsync(string title, string artist)
    {
        var cacheKey = $"{title} - {artist}";
        if (_durationCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }
        try
        {
            var keyword = $"{artist} {title}";
            var url = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?cr=1&new_json=1&format=json&aggr=1&lossless=0&n=1&w={Uri.EscapeDataString(keyword)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://y.qq.com/");
            using var resp = await AppUtils.Http.SendAsync(req);
            if ((int)resp.StatusCode != 200)
            {
                Log.Debug($"QQ音乐时长搜索失败 http {(int)resp.StatusCode} {cacheKey}");
                return 0;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("song", out var song) &&
                song.TryGetProperty("list", out var list) && list.GetArrayLength() > 0 &&
                list[0].TryGetProperty("interval", out var interval))
            {
                var durMs = interval.GetInt64() * 1000;
                Log.Debug($"QQ音乐时长命中 {cacheKey} -> {durMs} ms");
                _durationCache[cacheKey] = durMs;
                return durMs;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"获取QQ音乐时长失败: {e.Message}");
        }
        return 0;
    }
}

// 分配媒体源

public static class MediaServices
{
    private static readonly NeteaseCloudMusicSource Netease = new();
    private static readonly QQMusicSource QQ = new();
    private static readonly KugouMusicSource Kugou = new();
    private static readonly GsmTcSource GsmTc = new();
    private static readonly IMediaSource[] Services = [Netease, QQ, Kugou, GsmTc];

    private static bool _invalidWarned;

    public static async Task<MediaInfo?> GetMediaInfoAsync()
    {
        foreach (var source in Services)
        {
            if (!source.Available)
            {
                continue;
            }
            try
            {
                var info = await source.ReadAsync();
                if (info is not null && info.IsValid())
                {
                    _invalidWarned = false;
                    return info;
                }
                if (info is not null && !_invalidWarned)
                {
                    Log.Warning($"媒体源 [{source.Name}] 无效信息 title='{info.Title}' artist='{info.Artist}' song_id='{info.SongId}'");
                    _invalidWarned = true;
                }
            }
            catch (Exception e)
            {
                Log.Error($"媒体源 [{source.Name}] 读取异常: {e.Message}");
            }
        }
        return null;
    }

    // 按 app_name 切源
    public static IMediaSource GetService(string appName)
    {
        var al = (appName ?? "").ToLowerInvariant();
        if (al.Contains("kugou"))
        {
            return Kugou;
        }
        if (al.Contains("qqmusic") || al is "qq音乐" or "qq音乐播放器")
        {
            return QQ;
        }
        if (al.Contains("netease") || al.Contains("cloudmusic"))
        {
            return Netease;
        }
        foreach (var source in Services)
        {
            if (source.Name.ToLowerInvariant() == al)
            {
                return source;
            }
        }
        Log.Debug($"get_service: '{appName}' 未匹配 用 GSMTC");
        return GsmTc;
    }

    // 发送控制命令
    public static Task<bool> MediaControlAsync(string action) => GsmTc.ControlAsync(action);

    public static Task<bool> MediaNextAsync() => GsmTc.NextTrackAsync();

    public static Task<bool> MediaPrevAsync() => GsmTc.PrevTrackAsync();

    public static void Close()
    {
        foreach (var source in Services)
        {
            source.Dispose();
        }
    }
}
