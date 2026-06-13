using ClassLively_UI.Models;

namespace ClassLively_UI.Services;

/// <summary>
/// Python API定义
/// </summary>
public interface IApiService
{
    //  配置
    Task<Dictionary<string, object>?> GetConfigAsync();
    Task<object?> GetConfigAsync(string key);
    Task<bool> SetConfigAsync(string key, object value);
    Task<List<ConfigItemModel>> ListConfigItemsAsync();

    //  壁纸
    Task<WallpaperInfoModel?> GetCurrentWallpaperAsync();
    Task<string?> GetCurrentWallpaperPathAsync();
    Task<byte[]?> GetBlurredWallpaperAsync(string path);
    Task<WallpaperInfoModel?> FetchWallpaperAsync(string? source = null);
    Task<bool> SetDesktopWallpaperAsync(string path);
    Task<List<WallpaperInfoModel>> GetHistoryAsync(int page = 1, int perPage = 20);

    //  天气 
    Task<WeatherInfoModel?> GetWeatherAsync(string? city = null);

    //  一言 
    Task<PoetryModel?> GetPoetryAsync();

    //  系统 
    Task<IdleInfoModel> GetIdleMsAsync();

    //  媒体
    Task<MediaInfoModel> GetMediaInfoAsync();
    Task<Dictionary<string, object?>?> GetMediaDetailAsync(string title, string artist);

    //  下载 
    Task<List<SoftwareItemModel>> ListSoftwareAsync(string? category = null);
    Task<bool> StartDownloadAsync(string name);

    //  健康 
    Task<bool> HealthCheckAsync();
}
