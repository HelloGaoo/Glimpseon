using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Streams;
using ClassLively_UI.Controls;
using ClassLively_UI.Services;
using ClassLively_UI.Models;
using ClassLively_UI.Helpers;
using ClassLively_UI.Dialogs;

using Path = System.IO.Path;

namespace ClassLively_UI.Pages;

public sealed partial class HomePage : Page, ISnapProvider
{
    private readonly IApiService _api;
    private static readonly HttpClient _httpClient = new();

    // ── 定时器 ──
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _poetryTimer;
    private DispatcherTimer _weatherTimer = null!;
    private DispatcherTimer _countdownRefreshTimer = null!;
    private DispatcherTimer _countdownCarouselTimer = null!;

    // ── 组件位置 ──
    private readonly List<DraggableControl> _draggableWidgets = new();
    private bool _isEditMode;
    private const double SnapThreshold = 8;
    private const int SnapMargin = 20;

    // ── 辅助线元素缓存 ──
    private readonly List<Line> _guideLines = new();

    // ── 程序化组件引用 ──
    private QuickLaunchDock? _quickLaunchDock;
    private MediaWidget? _mediaWidget;

    // ── 倒计时轮播状态 ──
    private int _countdownCarouselIndex;

    // ── 组件 UI 元素引用 ──
    private TextBlock? _clockTimeText;
    private TextBlock? _clockDateText;
    private TextBlock? _weatherTempText;
    private Image? _weatherIconImage;
    private TextBlock? _schoolClassText;
    private TextBlock? _schoolNameText;
    private TextBlock? _poetryText;
    private TextBlock? _poetrySourceText;
    private TextBlock? _countdownText;

    public HomePage()
    {
        InitializeComponent();
        _api = new ApiService();

        InitAllComponents();
        InitTimers();

        this.SizeChanged += OnPageSizeChanged;
        this.Unloaded += OnPageUnloaded;
        _ = InitializeAsync();
    }

    //  组件初始化

    private void InitAllComponents()
    {
        // ── 时钟组件 ──
        _clockTimeText = new TextBlock { Text = "00:00:00", FontSize = 48, FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 700 }, Foreground = new SolidColorBrush(ColorHelper.White) };
        _clockDateText = new TextBlock { Text = "", FontSize = 14, Foreground = new SolidColorBrush(ColorHelper.FromRgba(170, 170, 170, 255)), Margin = new Thickness(0, 4, 0, 0) };
        var clockPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        clockPanel.Children.Add(_clockTimeText);
        clockPanel.Children.Add(_clockDateText);
        var clockHost = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(ColorHelper.FromRgba(0, 0, 0, 204)), Padding = new Thickness(24, 20, 24, 20) };
        clockHost.Child = clockPanel;
        var clockCtrl = CreateDraggable("clock", clockHost, 0.5, 0.25);

        // ── 天气组件 ──
        _weatherTempText = new TextBlock { Text = "--°C", FontSize = 28, FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 700 }, Foreground = new SolidColorBrush(ColorHelper.White), VerticalAlignment = VerticalAlignment.Center };
        _weatherIconImage = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        var weatherPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        weatherPanel.Children.Add(_weatherTempText);
        weatherPanel.Children.Add(_weatherIconImage);
        var weatherHost = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(ColorHelper.FromRgba(0, 0, 0, 204)), Padding = new Thickness(20, 16, 20, 16) };
        weatherHost.Child = weatherPanel;
        var weatherCtrl = CreateDraggable("weather", weatherHost, 0.9, 0.08);

        // ── 学校信息 ──
        _schoolClassText = new TextBlock { Text = "", FontSize = 15, FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 }, Foreground = new SolidColorBrush(ColorHelper.White) };
        _schoolNameText = new TextBlock { Text = "", FontSize = 13, Foreground = new SolidColorBrush(ColorHelper.FromRgba(170, 170, 170, 255)) };
        var schoolPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        schoolPanel.Children.Add(_schoolClassText);
        schoolPanel.Children.Add(_schoolNameText);
        var schoolHost = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(ColorHelper.FromRgba(0, 0, 0, 204)), Padding = new Thickness(20, 16, 20, 16) };
        schoolHost.Child = schoolPanel;
        var schoolCtrl = CreateDraggable("school_info", schoolHost, 0.08, 0.08);

        // ── 一言 ──    
        _poetryText = new TextBlock { Text = "正在获取一言...", FontSize = 15, Foreground = new SolidColorBrush(ColorHelper.FromRgba(221, 221, 221, 255)), TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        _poetrySourceText = new TextBlock { Text = "", FontSize = 11, Foreground = new SolidColorBrush(ColorHelper.FromRgba(119, 119, 119, 255)), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        var poetryPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        poetryPanel.Children.Add(_poetryText);
        poetryPanel.Children.Add(_poetrySourceText);
        var poetryHost = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(ColorHelper.FromRgba(0, 0, 0, 204)), Padding = new Thickness(24, 18, 24, 18) };
        poetryHost.Child = poetryPanel;
        var poetryCtrl = CreateDraggable("poetry", poetryHost, 0.5, 0.88);

        // ── 倒计时 ──
        _countdownText = new TextBlock { Text = "", FontSize = 16, FontWeight = new global::Windows.UI.Text.FontWeight { Weight = 600 }, Foreground = new SolidColorBrush(ColorHelper.White), TextWrapping = TextWrapping.Wrap };
        var countdownHost = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(ColorHelper.FromRgba(0, 0, 0, 204)), Padding = new Thickness(24, 18, 24, 18) };
        countdownHost.Child = _countdownText;
        var countdownCtrl = CreateDraggable("countdown", countdownHost, 0.5, 0.55);

        // ── 快捷启动栏 ──
        var dock = new QuickLaunchDock();
        dock.AppLaunched += name => Debug.WriteLine($"[HomePage] 启动: {name}");
        dock.AppReordered += (fromIdx, toIdx) => Debug.WriteLine($"[HomePage] 重排: {fromIdx} -> {toIdx}");
        dock.AppDeleted += idx => Debug.WriteLine($"[HomePage] 删除: {idx}");
        _quickLaunchDock = dock;
        var quickLaunchCtrl = CreateDraggable("quick_launch", dock, 0.5, 0.92);

        // ── 媒体信息 ──
        var media = new MediaWidget { Api = _api };
        _mediaWidget = media;
        var mediaCtrl = CreateDraggable("media", media, 0.12, 0.85);

        _draggableWidgets.AddRange(new[] { clockCtrl, weatherCtrl, schoolCtrl, poetryCtrl, countdownCtrl, quickLaunchCtrl, mediaCtrl });
    }

    private DraggableControl CreateDraggable(string componentId, UIElement content, double defaultX, double defaultY)
    {
        var ctrl = new DraggableControl(componentId);
        ctrl.SetContent(content);
        ctrl.SnapProvider = this;
        ctrl.PositionChanged += (_, _, _) => { };
        ctrl.SettingRequested += OpenComponentSetting;

        ComponentCanvas.Children.Add(ctrl);
        ctrl.SetPositionPercent(defaultX, defaultY);
        return ctrl;
    }

    //  定时器初始化

    private void InitTimers()
    {
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();

        _poetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _poetryTimer.Tick += async (_, _) => await FetchPoetryAsync();

        _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _weatherTimer.Tick += async (_, _) => await LoadWeatherAsync();

        _countdownRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownRefreshTimer.Tick += (_, _) => UpdateCountdown();

        _countdownCarouselTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _countdownCarouselTimer.Tick += (_, _) => UpdateCountdown();
    }

    //  页面卸载清理

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _clockTimer?.Stop();
        _poetryTimer?.Stop();
        _weatherTimer.Stop();
        _countdownRefreshTimer.Stop();
        _countdownCarouselTimer.Stop();

        _mediaWidget?.Stop();

        if (_quickLaunchDock != null)
            _quickLaunchDock.Visibility = Visibility.Collapsed;

        this.SizeChanged -= OnPageSizeChanged;
    }

    //  编辑模式

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleEditMode();
    }

    private void ToggleEditMode()
    {
        _isEditMode = !_isEditMode;

        if (_isEditMode)
        {
            EditButton.Content = "完成";
            SetDraggableEnabled(true);
            ShowGuideLineOverlay();
        }
        else
        {
            EditButton.Content = "编辑布局";
            SetDraggableEnabled(false);
            HideGuideLineOverlay();
            SaveComponentPositions();
        }
    }

    private void SetDraggableEnabled(bool enabled)
    {
        foreach (var widget in _draggableWidgets)
        {
            if (widget != null)
            {
                widget.SetDraggable(enabled);
                if (enabled)
                    widget.UpdateThemeColor(ColorHelper.FromRgb(48, 195, 97));
            }
        }
    }


    //  辅助线

    private void ShowGuideLineOverlay()
    {
        GuideLineOverlay.Visibility = Visibility.Visible;
    }

    private void HideGuideLineOverlay()
    {
        GuideLineOverlay.Visibility = Visibility.Collapsed;
        ClearGuideLinesInternal();
    }

    public void ShowGuideLines(List<(char Direction, double Position)> lines)
    {
        ClearGuideLinesInternal();

        foreach (var (dir, pos) in lines)
        {
            var line = new Line
            {
                Stroke = new SolidColorBrush(ColorHelper.FromRgba(48, 195, 97, 100)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            };

            if (dir == 'h')
            {
                line.X1 = 0; line.Y1 = pos;
                line.X2 = GuideLineOverlay.ActualWidth; line.Y2 = pos;
            }
            else
            {
                line.X1 = pos; line.Y1 = 0;
                line.X2 = pos; line.Y2 = GuideLineOverlay.ActualHeight;
            }

            _guideLines.Add(line);
            GuideLineOverlay.Children.Add(line);
        }
    }

    public void ClearGuideLines() => ClearGuideLinesInternal();

    private void ClearGuideLinesInternal()
    {
        foreach (var line in _guideLines)
            GuideLineOverlay.Children.Remove(line);
        _guideLines.Clear();
    }

    /// <summary>
    /// 吸附算法
    /// </summary>
    public (double X, double Y, List<(char Dir, double Pos)> Lines) ComputeSnap(
        double x, double y, double w, double h, object? draggingWidget)
    {
        var snappedX = x;
        var snappedY = y;
        var alignLines = new List<(char, double)>();

        var cw = ComponentCanvas.ActualWidth;
        var ch = ComponentCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return (snappedX, snappedY, alignLines);

        var dragPointsX = new[] { x, x + w / 2, x + w };
        var dragPointsY = new[] { y, y + h / 2, y + h };

        var bestDx = SnapThreshold + 1;
        var bestDy = SnapThreshold + 1;
        double? snapXVal = null;
        double? snapYVal = null;

        var vRefs = new[] { 0.0, SnapMargin, cw / 4, cw / 3, cw / 2, cw * 2 / 3, cw * 3 / 4, cw - SnapMargin, cw };
        foreach (var refPos in vRefs)
        {
            for (var i = 0; i < dragPointsX.Length; i++)
            {
                var dx = Math.Abs(dragPointsX[i] - refPos);
                if (dx <= SnapThreshold && dx < bestDx)
                {
                    bestDx = dx;
                    var offsets = new[] { 0.0, w / 2, w };
                    snapXVal = refPos - offsets[i];
                }
            }
        }

        var hRefs = new[] { 0.0, SnapMargin, ch / 4, ch / 3, ch / 2, ch * 2 / 3, ch * 3 / 4, ch - SnapMargin, ch };
        foreach (var refPos in hRefs)
        {
            for (var i = 0; i < dragPointsY.Length; i++)
            {
                var dy = Math.Abs(dragPointsY[i] - refPos);
                if (dy <= SnapThreshold && dy < bestDy)
                {
                    bestDy = dy;
                    var offsets = new[] { 0.0, h / 2, h };
                    snapYVal = refPos - offsets[i];
                }
            }
        }

        if (snapXVal.HasValue) snappedX = Math.Round(snapXVal.Value);
        if (snapYVal.HasValue) snappedY = Math.Round(snapYVal.Value);

        var finalPointsX = new[] { snappedX, snappedX + w / 2, snappedX + w };
        var finalPointsY = new[] { snappedY, snappedY + h / 2, snappedY + h };

        foreach (var refPos in vRefs)
        {
            foreach (var dp in finalPointsX)
                if (Math.Abs(dp - refPos) <= 1) { alignLines.Add(('v', refPos)); break; }
        }
        foreach (var refPos in hRefs)
        {
            foreach (var dp in finalPointsY)
                if (Math.Abs(dp - refPos) <= 1) { alignLines.Add(('h', refPos)); break; }
        }

        return (snappedX, snappedY, alignLines);
    }

    //  位置保存

    private static readonly string PositionsFilePath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "component_positions.json");

    private void SaveComponentPositions()
    {
        try
        {
            var positions = new Dictionary<string, object>();
            foreach (var widget in _draggableWidgets)
            {
                if (widget != null)
                {
                    var (px, py) = widget.GetPositionPercent();
                    positions[widget.ComponentId] = new { x = Math.Round(px, 4), y = Math.Round(py, 4) };
                }
            }

            var dir = Path.GetDirectoryName(PositionsFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(PositionsFilePath,
                JsonSerializer.Serialize(positions, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 保存组件位置失败: {ex.Message}");
        }
    }

    private void LoadComponentPositions()
    {
        try
        {
            if (!File.Exists(PositionsFilePath)) return;

            var json = File.ReadAllText(PositionsFilePath);
            var positions = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (positions == null) return;

            foreach (var widget in _draggableWidgets)
            {
                if (widget != null && positions.TryGetValue(widget.ComponentId, out var posEl))
                {
                    try
                    {
                        var x = posEl.GetProperty("x").GetDouble();
                        var y = posEl.GetProperty("y").GetDouble();
                        widget.SetPositionPercent(x, y);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 加载组件位置失败: {ex.Message}");
        }
    }


    //  窗口缩放

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            foreach (var widget in _draggableWidgets)
                widget?.OnParentResize();

            if (GuideLineOverlay.Visibility == Visibility.Visible)
                ClearGuideLinesInternal();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] SizeChanged 异常: {ex.Message}");
        }
    }

    //  组件设置弹窗

    private void OpenComponentSetting(string componentId)
    {
        var dialog = ComponentSettingDialog.Create(componentId, _api, this.XamlRoot);
        if (dialog != null)
            _ = dialog.ShowAsync();
    }

    private static string GetComponentDisplayName(string id) => id switch
    {
        "clock" => "时钟",
        "weather" => "天气",
        "poetry" => "一言",
        "countdown" => "倒计时",
        "school_info" => "学校信息",
        "quick_launch" => "快捷启动",
        "media" => "媒体信息",
        _ => id
    };

    //  初始化数据加载

    private async Task InitializeAsync()
    {
        bool backendOnline = false;
        try { backendOnline = await _api.HealthCheckAsync(); } catch { }

        try { await LoadBlurredBackground(); } catch { }
        try { await FetchPoetryAsync(); _poetryTimer!.Start(); } catch { }
        try { await LoadWeatherAsync(); _weatherTimer.Start(); } catch { }
        try { await LoadCountdownAsync(); _countdownRefreshTimer.Start(); _countdownCarouselTimer.Start(); } catch { }
        try { LoadSchoolInfo(); } catch { }
        try { await LoadOverlayOpacity(); } catch { }

        UpdateClock();
        _clockTimer!.Start();

        _mediaWidget?.Start();

        try { await LoadQuickLaunchAppsAsync(); } catch { }

        await Task.Delay(100);
        LoadComponentPositions();
    }

    //  时钟组件

    private static readonly string[] Weekdays = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
    private static readonly string[] LunarMonthNames = { "", "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "十一", "腊" };
    private static readonly string[] LunarDayNames =
    {
        "", "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
    };

    // 农历数据表1900-2100年
    // bit15~bit4: 12个月大小月 bit=1大月30天  bit=0小月29天
    // bit3: 是否闰月 bit2~bit0: 闰月位置 0=无闰月
    private static readonly int[] LunarData =
    {
        0x04bd8, 0x04ae0, 0x0a570, 0x054d5, 0x0d260, 0x0d950, 0x16554, 0x056a0, 0x09ad0, 0x055d2,
        0x04ae0, 0x0a5b6, 0x0a4d0, 0x0d250, 0x1d255, 0x0b540, 0x0d6a0, 0x0ada2, 0x095b0, 0x14977,
        0x04970, 0x0a4b0, 0x0b4b5, 0x06a50, 0x06d40, 0x1ab54, 0x02b60, 0x09570, 0x052f2, 0x04970,
        0x06566, 0x0d4a0, 0x0ea50, 0x06e95, 0x05ad0, 0x02b60, 0x186e3, 0x092e0, 0x1c8d7, 0x0c950,
        0x0d4a0, 0x1d8a6, 0x0b550, 0x056a0, 0x1a5b4, 0x025d0, 0x092d0, 0x0d2b2, 0x0a950, 0x0b557,
        0x06ca0, 0x0b550, 0x15355, 0x04da0, 0x0a5b0, 0x14573, 0x052b0, 0x0a9a8, 0x0e950, 0x06aa0,
        0x0aea6, 0x0ab50, 0x04b60, 0x0aae4, 0x0a570, 0x05260, 0x0f263, 0x0d950, 0x05b57, 0x056a0,
        0x096d0, 0x04dd5, 0x04ad0, 0x0a4d0, 0x0d4d4, 0x0d250, 0x0d558, 0x0b540, 0x0b6a0, 0x195a6,
        0x095b0, 0x049b0, 0x0a974, 0x0a4b0, 0x0b27a, 0x06a50, 0x06d40, 0x0af46, 0x0ab60, 0x09570,
        0x04af5, 0x04970, 0x064b0, 0x074a3, 0x0ea50, 0x06b58, 0x05ac0, 0x0ab60, 0x096d5, 0x092e0,
        0x0c960, 0x0d954, 0x0d4a0, 0x0da50, 0x07552, 0x056a0, 0x0abb7, 0x025d0, 0x092d0, 0x0cab5,
        0x0a950, 0x0b4a0, 0x0baa4, 0x0ad50, 0x055d9, 0x04ba0, 0x0a5b0, 0x15176, 0x052b0, 0x0a930,
        0x07954, 0x06aa0, 0x0ad50, 0x05b52, 0x04b60, 0x0a6e6, 0x0a4e0, 0x0d260, 0x0ea65, 0x0d530,
        0x05aa0, 0x076a3, 0x096d0, 0x04afb, 0x04ad0, 0x0a4d0, 0x1d0b6, 0x0d250, 0x0d520, 0x0dd45,
        0x0b5a0, 0x056d0, 0x055b2, 0x049b0, 0x0a577, 0x0a4b0, 0x0aa50, 0x1b255, 0x06d20, 0x0ada0,
        0x14b63, 0x09370, 0x049f8, 0x04970, 0x064b0, 0x168a6, 0x0ea50, 0x06b20, 0x1a6c4, 0x0aae0,
        0x0a2e0, 0x0d2e3, 0x0c960, 0x0d557, 0x0d4a0, 0x0da50, 0x05d55, 0x056a0, 0x0a6d0, 0x055d4,
        0x052d0, 0x0a9b8, 0x0a950, 0x0b4a0, 0x0b6a6, 0x0ad50, 0x055a0, 0x0aba4, 0x0a5b0, 0x052b0,
        0x0b273, 0x06930, 0x07337, 0x06aa0, 0x0ad50, 0x14b55, 0x04b60, 0x0a570, 0x054e4, 0x0d160,
        0x0e968, 0x0d520, 0x0daa0, 0x16aa6, 0x056d0, 0x04ae0, 0x0a9d4, 0x0a2d0, 0x0d150, 0x0f252,
        0x0d520
    };

    private const int LunarBaseYear = 1900;

    private void UpdateClock()
    {
        try
        {
            var now = DateTime.Now;
            bool showSeconds = AppSettings.GetBool("show_clock_seconds", true);
            _clockTimeText.Text = now.ToString(showSeconds ? "HH:mm:ss" : "HH:mm");

            var weekdayName = Weekdays[(int)now.DayOfWeek];
            var solarStr = $"{now.Year}年{now.Month}月{now.Day}日 {weekdayName}";

            bool showLunar = AppSettings.GetBool("show_lunar_calendar", false);
            if (showLunar)
            {
                try { solarStr += " " + GetLunarDate(now); }
                catch { }
            }

            _clockDateText.Text = solarStr;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] UpdateClock 异常: {ex.Message}");
        }
    }

    private static string GetLunarDate(DateTime date)
    {
        int offset = (date - new DateTime(LunarBaseYear, 1, 31)).Days;

        int lunarYear = LunarBaseYear;
        int yearDays;
        while ((yearDays = GetLunarYearDays(lunarYear)) > offset && offset > 0)
        {
            offset -= yearDays;
            lunarYear++;
        }

        if (offset < 0)
        {
            offset += yearDays;
            lunarYear--;
        }

        int lunarMonth = 1;
        int leapMonth = GetLeapMonth(lunarYear);
        bool isLeap = false;
        int monthDays;

        for (; lunarMonth < 13 && offset > 0; lunarMonth++)
        {
            if (leapMonth > 0 && lunarMonth == (leapMonth + 1) && !isLeap)
            {
                --lunarMonth;
                isLeap = true;
                monthDays = GetLeapMonthDays(lunarYear);
            }
            else
            {
                monthDays = GetLunarMonthDays(lunarYear, lunarMonth);
            }

            if (isLeap && lunarMonth == (leapMonth + 1)) isLeap = false;

            if (offset >= monthDays)
                offset -= monthDays;
            else
                break;
        }

        int lunarDay = offset + 1;

        if (lunarMonth == 13) { lunarMonth = 1; }

        return $"{LunarMonthNames[lunarMonth]}月{LunarDayNames[lunarDay]}";
    }

    private static int GetLunarYearDays(int year)
    {
        int sum = 348;
        var info = LunarData[year - LunarBaseYear];
        for (int i = 0x8000; i > 0x8; i >>= 1) sum += (info & i) != 0 ? 1 : 0;
        return sum + GetLeapMonthDays(year);
    }

    private static int GetLeapMonth(int year) => LunarData[year - LunarBaseYear] & 0xf;

    private static int GetLeapMonthDays(int year)
    {
        if (GetLeapMonth(year) != 0)
            return (LunarData[year - LunarBaseYear] & 0x10000) != 0 ? 30 : 29;
        return 0;
    }

    private static int GetLunarMonthDays(int year, int month)
    {
        return (LunarData[year - LunarBaseYear] & (0x10000 >> month)) != 0 ? 30 : 29;
    }

    //  一言/诗词组件

    private async Task FetchPoetryAsync()
    {
        // 后端 API
        try
        {
            var poetry = await _api.GetPoetryAsync();
            if (poetry != null && !string.IsNullOrEmpty(poetry.Text))
            {
                _poetryText.Text = poetry.Text;
                _poetrySourceText.Text = string.IsNullOrEmpty(poetry.Source) ? "ClassLively" : $"—— {poetry.Source}";
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 后端一言获取失败，尝试 fallback: {ex.Message}");
        }

        // Fallback: hitokoto.cn
        try
        {
            var response = await _httpClient.GetAsync("https://v1.hitokoto.cn/?c=d&c=h&c=i&c=k");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var text = root.TryGetProperty("hitokoto", out var hitokotoEl) ? hitokotoEl.GetString() ?? "" : "";
            var from = root.TryGetProperty("from", out var fromEl) ? fromEl.GetString() ?? "" : "";

            if (!string.IsNullOrEmpty(text))
            {
                _poetryText.Text = text;
                _poetrySourceText.Text = string.IsNullOrEmpty(from) ? "" : $"—— {from}";
            }
            else
            {
                _poetryText.Text = "一言获取失败";
                _poetrySourceText.Text = "";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 一言获取失败: {ex.Message}");
            _poetryText.Text = "一言获取失败";
            _poetrySourceText.Text = "";
        }
    }

    //  倒计时组件

    private List<Dictionary<string, string>>? _countdownList;

    private async Task LoadCountdownAsync()
    {
        // 后端 API
        try
        {
            var config = await _api.GetConfigAsync();
            if (config != null && config.TryGetValue("countdownList", out var rawList) && rawList is JsonElement arr)
            {
                if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                {
                    _countdownList = new List<Dictionary<string, string>>();
                    foreach (var item in arr.EnumerateArray())
                    {
                        var dict = new Dictionary<string, string>();
                        foreach (var prop in item.EnumerateObject())
                            dict[prop.Name] = prop.Value.GetString() ?? "";
                        _countdownList.Add(dict);
                    }
                    UpdateCountdown();
                    return;
                }
            }
        }

        _countdownText.Text = "";
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 倒计时获取失败: {ex.Message}");
            _countdownText.Text = "";
        }
    }

    private void UpdateCountdown()
    {
        try
        {
            if (_countdownList == null || _countdownList.Count == 0)
            {
                _countdownText.Text = "";
                return;
            }

            var displayMode = AppSettings.GetString("countdown_display_mode", "carousel");

            if (displayMode == "simultaneous")
            {
                var texts = _countdownList.Select(cd => FormatCountdown(cd)).Where(t => !string.IsNullOrEmpty(t)).ToList();
                _countdownText.Text = texts.Count > 0 ? string.Join("\n", texts) : "";
            }
            else
            {
                if (_countdownCarouselIndex >= _countdownList.Count)
                    _countdownCarouselIndex = 0;

                var text = FormatCountdown(_countdownList[_countdownCarouselIndex]);
                _countdownText.Text = text ?? "";

                _countdownCarouselIndex++;
                if (_countdownCarouselIndex >= _countdownList.Count)
                    _countdownCarouselIndex = 0;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 倒计时更新异常: {ex.Message}");
        }
    }

    private string? FormatCountdown(Dictionary<string, string>? cd)
    {
        if (cd == null) return null;

        var title = cd.GetValueOrDefault("title", "");
        var targetTimeStr = cd.GetValueOrDefault("target_time", "");

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(targetTimeStr))
            return null;

        if (!DateTime.TryParse(targetTimeStr, out var targetTime))
            return null;

        var now = DateTime.Now;
        var delta = targetTime - now;
        var totalSeconds = (int)Math.Floor(delta.TotalSeconds);

        string timeText;
        if (targetTime.Date == now.Date && totalSeconds < 0)
        {
            timeText = "就在今天";
        }
        else if (totalSeconds > 0)
        {
            var days = totalSeconds / 86400;
            var hours = (totalSeconds % 86400) / 3600;
            var minutes = (totalSeconds % 3600) / 60;
            var seconds = totalSeconds % 60;

            timeText = days switch
            {
                >= 3 => $"{days} 天",
                >= 1 => $"{days} 天 {hours} 小时",
                _ when hours >= 1 => $"{hours} 小时",
                _ when minutes >= 1 => $"{minutes} 分 {seconds} 秒",
                _ => $"{seconds} 秒"
            };
            timeText = $"距离{title}还有 {timeText}";
        }
        else
        {
            var pastDays = Math.Abs(totalSeconds) / 86400;
            timeText = $"{title} 已过去 {pastDays} 天";
        }

        return timeText;
    }

    //  天气组件

    private async Task LoadWeatherAsync()
    {
        // 后端 API
        try
        {
            var weather = await _api.GetWeatherAsync();
            if (weather != null)
            {
                _weatherTempText.Text = $"{weather.Temp}°C";
                if (!string.IsNullOrEmpty(weather.Icon))
                {
                    try
                    {
                        var bitmap = new BitmapImage(new Uri(weather.Icon));
                        _weatherIconImage.Source = bitmap;
                    }
                    catch { /* 图标加载失败则留空 */ }
                }
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 后端天气获取失败，尝试 fallback: {ex.Message}");
        }

        // Fallback: 本地配置
        try
        {
            var city = AppSettings.GetString("weather_city", "");
            if (string.IsNullOrEmpty(city))
            {
                _weatherTempText.Text = "--°C";
                return;
            }

            _weatherTempText.Text = "--°C";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 天气加载失败: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════
    //  学校信息组件
    // ════════════════════════════════════════════════

    private async void LoadSchoolInfo()
    {
        // 方法1：后端 API
        try
        {
            var config = await _api.GetConfigAsync();
            if (config != null)
            {
                var schoolName = config.TryGetValue("school", out var sVal) ? sVal?.ToString() : null;
                var schoolClass = config.TryGetValue("schoolClass", out var cVal) ? cVal?.ToString() : null;

                if (!string.IsNullOrEmpty(schoolName) || !string.IsNullOrEmpty(schoolClass))
                {
                    _schoolClassText.Text = schoolClass ?? "";
                    _schoolNameText.Text = string.IsNullOrEmpty(schoolName) ? "未设置学校" : schoolName;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 后端学校信息获取失败，尝试 fallback: {ex.Message}");
        }

        // Fallback: AppSettings
        try
        {
            var schoolName = AppSettings.GetString("school_name", "");
            var schoolClass = AppSettings.GetString("school_class", "");

            _schoolClassText.Text = schoolClass ?? "";
            _schoolNameText.Text = string.IsNullOrEmpty(schoolName) ? "未设置学校" : schoolName;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 学校信息加载失败: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════
    //  模糊背景
    // ════════════════════════════════════════════════

    private async Task LoadBlurredBackground()
    {
        try
        {
            var wallpaperPath = await _api.GetCurrentWallpaperPathAsync();
            if (!string.IsNullOrEmpty(wallpaperPath) && File.Exists(wallpaperPath))
            {
                var blurredBytes = await _api.GetBlurredWallpaperAsync(wallpaperPath);
                if (blurredBytes != null && blurredBytes.Length > 0)
                {
                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(blurredBytes))
                    {
                        await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                    }
                    BackgroundImage.Source = bitmap;
                }
                else
                {
                    BackgroundImage.Source = new BitmapImage(new Uri($"file:///{wallpaperPath.Replace('\\', '/')}"));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomePage] 加载模糊背景失败: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════
    //  暗化遮罩透明度
    // ════════════════════════════════════════════════

    private async Task LoadOverlayOpacity()
    {
        try
        {
            var opacityObj = await _api.GetConfigAsync("background_darken");
            if (opacityObj != null && double.TryParse(opacityObj.ToString(), out double opacity))
            {
                DarkOverlay.Opacity = Math.Clamp(opacity, 0.0, 1.0);
            }
        }
        catch { }
    }

    // ════════════════════════════════════════════════
    //  快捷启动应用列表
    // ════════════════════════════════════════════════

    private async Task LoadQuickLaunchAppsAsync()
    {
        try
        {
            if (_quickLaunchDock == null) return;
            var appsObj = await _api.GetConfigAsync("quickLaunchApps");
            if (appsObj is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
            {
                var apps = new List<Dictionary<string, string>>();
                foreach (var item in arr.EnumerateArray())
                {
                    var app = new Dictionary<string, string>();
                    foreach (var prop in item.EnumerateObject())
                        app[prop.Name] = prop.Value.GetString() ?? "";
                    apps.Add(app);
                }
                _quickLaunchDock.SetApps(apps);
            }
        }
        catch { }
    }
}
