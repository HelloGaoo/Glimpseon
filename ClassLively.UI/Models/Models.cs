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
/// 配置项模型 对应cfg (QConfig)
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
/// 壁纸信息模型
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

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

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

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

/// <summary>
/// 软件下载条目
/// </summary>
public class SoftwareItemModel
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
}
