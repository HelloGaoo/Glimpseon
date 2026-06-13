using System.Text.Json.Serialization;

namespace ClassLively_UI.Models;

/// <summary>
/// API 响应包装
/// </summary>
public class ApiResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

/// <summary>
/// 配置项
/// </summary>
public class ConfigItemModel
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";
}

/// <summary>
/// 壁纸信息
/// </summary>
public class WallpaperInfoModel
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

/// <summary>
/// 系统空闲信息
/// </summary>
public class IdleInfoModel
{
    [JsonPropertyName("ms")]
    public int Ms { get; set; }
}

/// <summary>
/// 天气信息
/// </summary>
public class WeatherInfoModel
{
    [JsonPropertyName("temp")]
    public string? Temp { get; set; }

    public string? Temperature => Temp;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    public string? Condition => Description;

    [JsonPropertyName("city")]
    public string? City { get; set; }
}

/// <summary>
/// 一言/诗词信息
/// </summary>
public class PoetryModel
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    public string? Content => Text;

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

/// <summary>
/// 软件下载条目
/// </summary>
public partial class SoftwareItemModel
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>批量模式下是否选中</summary>
    public bool IsSelected { get; set; }
}

/// <summary>
/// 媒体播放信息
/// </summary>
public class MediaInfoModel
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("cover_path")]
    public string? CoverPath { get; set; }

    [JsonPropertyName("lyrics")]
    public string? Lyrics { get; set; }

    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; set; }

    /// <summary>封面二进制</summary>
    [JsonIgnore]
    public byte[]? ThumbnailData { get; set; }

    /// <summary>缩略图 base64（来自 Python 后端）</summary>
    [JsonPropertyName("thumbnail_base64")]
    public string? ThumbnailBase64 { get; set; }

    /// <summary>当前播放位置（毫秒）</summary>
    [JsonPropertyName("position_ms")]
    public int PositionMs { get; set; }

    /// <summary>总时长ms— 优先使用此字段   Duration 用来兼容旧api</summary>
    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; }

    /// <summary>来源应用名称</summary>
    [JsonPropertyName("app_name")]
    public string? AppName { get; set; }

    /// <summary>歌曲ID</summary>
    [JsonPropertyName("song_id")]
    public string? SongId { get; set; }

    /// <summary>是否包含有效数据</summary>
    public bool IsValid() => !string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Artist) || !string.IsNullOrEmpty(SongId);
}
