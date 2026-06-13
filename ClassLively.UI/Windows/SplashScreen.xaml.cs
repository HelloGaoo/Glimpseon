using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI.Text;
using ClassLively_UI.Controls;

namespace ClassLively_UI.Windows;

/// <summary>
/// 启动画面
///   0% → 清理临时文件(10%) → 加载翻译(15%) → 初始化字体(30%) → 配置日志(40%) → 加载配置(55%) → 创建主窗口(70%) → 预加载资源(75%) → 完成(95-100%)
/// </summary>
public sealed partial class SplashScreen : Window
{
    private TextBlock _statusText = null!;
    private ProgressBar _progressBar = null!;

    public event Action? OnCloseRequested;

    public SplashScreen(string appName = "ClassLively", string version = "1.0")
    {
        InitializeComponent();
        Title = $"{appName} 启动中...";
        this.AppWindow.Resize(new SizeInt32(360, 160));
        ExtendsContentIntoTitleBar = true;
        Closed += (s, e) => OnCloseRequested?.Invoke();

        // 居中显示
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    /// <summary>更新进度</summary>
    /// <param name="value">0-100</param>
    /// <param name="status">状态文字</param>
    public void SetProgress(int value, string status = "")
    {
        if (value < 0) value = 0;
        if (value > 100) value = 100;
        _progressBar.Value = value;
        if (!string.IsNullOrEmpty(status))
            _statusText.Text = status;
    }

    /// <summary>完成启动并关闭</summary>
    public void Complete()
    {
        SetProgress(100, "启动完成");
        // 延迟关闭
        _ = Task.Delay(300).ContinueWith(_ =>
        {
            this.DispatcherQueue.TryEnqueue(() => Close());
        });
    }

    private void InitializeComponent()
    {
        Content = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Children =
            {
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 12,
                    Children =
                    {
                        // Logo + 应用名行
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            Children =
                            {
                                new Image
                                {
                                    Source = new BitmapImage(
                                        new Uri("ms-appx:///Assets/Square44x44Logo.png")),
                                    Width = 48,
                                    Height = 48,
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                new StackPanel
                                {
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Spacing = 2,
                                    Children =
                                    {
                                        new TextBlock
                                        {
                                            Text = "ClassLively",
                                            FontSize = 20,
                                            FontWeight = new FontWeight { Weight = 700 },
                                            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                                        },
                                        new TextBlock
                                        {
                                            Text = "v1.0",
                                            FontSize = 12,
                                            Foreground = new SolidColorBrush(ColorHelper.FromRgba(255,255,255,120))
                                        }
                                    }
                                }
                            }
                        },
                        // 状态文字
                        (_statusText = new TextBlock
                        {
                            Text = "正在初始化...",
                            FontSize = 13,
                            Foreground = new SolidColorBrush(ColorHelper.FromRgba(255,255,255,180)),
                            HorizontalAlignment = HorizontalAlignment.Center
                        }),
                        // 进度条
                        (_progressBar = new ProgressBar
                        {
                            Width = 280,
                            Height = 4,
                            Minimum = 0,
                            Maximum = 100,
                            Value = 0,
                            HorizontalAlignment = HorizontalAlignment.Center
                        })
                    }
                }
            }
        };
    }
}
