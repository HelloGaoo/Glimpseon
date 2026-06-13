using System.Net.Http.Json;
using System.Text.Json;
using ClassLively_UI.Models;

namespace ClassLively_UI.Services;

/// <summary>
/// Python FastAPI
///  http://127.0.0.1:19856
/// </summary>
public class ApiService : IApiService, IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string BaseUrl = "http://127.0.0.1:19856";

    public ApiService()
    {
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    //  配置 

    public async Task<Dictionary<string, object>?> GetConfigAsync()
    {
        var resp = await _http.GetFromJsonAsync<ApiResponse<Dictionary<string, object>>>("/api/config", _jsonOptions);
        return resp?.Data;
    }

    public async Task<object?> GetConfigAsync(string key)
    {
        var all = await GetConfigAsync();
        if (all != null && all.TryGetValue(key, out var value))
            return value;
        return null;
    }

    public async Task<bool> SetConfigAsync(string key, object value)
    {
        try
        {
            var result = await _http.PostAsJsonAsync($"/api/config/{key}", new { value = value }, _jsonOptions);
            return result.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<ConfigItemModel>> ListConfigItemsAsync()
    {
        var resp = await _http.GetFromJsonAsync<ApiResponse<List<ConfigItemModel>>>("/api/config/items", _jsonOptions);
        return resp?.Data ?? new();
    }

    //  壁纸 

    public async Task<WallpaperInfoModel?> GetCurrentWallpaperAsync()
    {
        var resp = await _http.GetFromJsonAsync<ApiResponse<WallpaperInfoModel>>("/api/wallpaper/current", _jsonOptions);
        return resp?.Data;
    }

    /// <summary>获取当前壁纸路径</summary>
    public async Task<string?> GetCurrentWallpaperPathAsync()
    {
        var resp = await _http.GetFromJsonAsync<ApiResponse<Dictionary<string, object>>>("/api/wallpaper/current", _jsonOptions);
        var path = resp?.Data?["path"]?.ToString();
        return path;
    }

    /// <summary>获取模糊后的壁纸字节</summary>
    public async Task<byte[]?> GetBlurredWallpaperAsync(string path)
    {
        try
        {
            var resp = await _http.GetAsync($"/api/wallpaper/blurred?path={Uri.EscapeDataString(path)}");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    public async Task<WallpaperInfoModel?> FetchWallpaperAsync(string? source = null)
    {
        var url = string.IsNullOrEmpty(source) ? "/api/wallpaper/fetch" : $"/api/wallpaper/fetch?source={source}";
        var resp = await _http.PostAsync(url, null);
        if (!resp.IsSuccessStatusCode) return null;
        var apiResp = await resp.Content.ReadFromJsonAsync<ApiResponse<WallpaperInfoModel>>(_jsonOptions);
        return apiResp?.Data;
    }

    public async Task<bool> SetDesktopWallpaperAsync(string path)
    {
        try
        {
            var result = await _http.PostAsJsonAsync("/api/wallpaper/set-desktop", new { path }, _jsonOptions);
            return result.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<WallpaperInfoModel>> GetHistoryAsync(int page = 1, int perPage = 20)
    {
        var resp = await _http.GetFromJsonAsync<ApiResponse<List<WallpaperInfoModel>>>($"/api/wallpaper/history?page={page}&per_page={perPage}", _jsonOptions);
        return resp?.Data ?? new();
    }

    //  天气 

    public async Task<WeatherInfoModel?> GetWeatherAsync(string? city = null)
    {
        var url = string.IsNullOrEmpty(city) ? "/api/weather" : $"/api/weather?city={Uri.EscapeDataString(city)}";
        var resp = await _http.GetFromJsonAsync<ApiResponse<WeatherInfoModel>>(url, _jsonOptions);
        return resp?.Data;
    }

    //  一言 

    public async Task<PoetryModel?> GetPoetryAsync()
    {
        var resp = await _http.GetFromJsonAsync<ApiResponse<PoetryModel>>("/api/poetry", _jsonOptions);
        return resp?.Data;
    }

    //  系统 

    public async Task<IdleInfoModel> GetIdleMsAsync()
    {
        var resp = await _http.GetFromJsonAsync<ApiResponse<IdleInfoModel>>("/api/system/idle-ms", _jsonOptions);
        return resp?.Data ?? new IdleInfoModel { Ms = -1 };
    }

    //  媒体 

    public async Task<MediaInfoModel> GetMediaInfoAsync()
    {
        var resp = await _http.GetFromJsonAsync<ApiResponse<MediaInfoModel>>("/api/media/info", _jsonOptions);
        return resp?.Data ?? new MediaInfoModel();
    }

    public async Task<Dictionary<string, object?>?> GetMediaDetailAsync(string title, string artist)
    {
        try
        {
            var url = $"/api/media/detail?title={Uri.EscapeDataString(title)}&artist={Uri.EscapeDataString(artist ?? "")}";
            var resp = await _http.GetFromJsonAsync<ApiResponse<Dictionary<string, object?>>>(url, _jsonOptions);
            return resp?.Data;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiService] 获取媒体详情失败: {ex.Message}");
            return null;
        }
    }

    //  下载 

    public async Task<List<SoftwareItemModel>> ListSoftwareAsync(string? category = null)
    {
        var url = string.IsNullOrEmpty(category) ? "/api/software/list" : $"/api/software/list?category={category}";
        var resp = await _http.GetFromJsonAsync<ApiResponse<List<SoftwareItemModel>>>(url, _jsonOptions);
        return resp?.Data ?? new();
    }

    public async Task<bool> StartDownloadAsync(string name)
    {
        try
        {
            var result = await _http.PostAsync($"/api/software/download/{Uri.EscapeDataString(name)}", null);
            return result.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // 连通性检测

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/api/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void Dispose() => _http.Dispose();
}
