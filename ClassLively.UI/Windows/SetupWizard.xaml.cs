using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI.Text;
using ClassLively_UI.Helpers;
using ClassLively_UI.Controls;

namespace ClassLively_UI.Windows;

public sealed partial class SetupWizard : Window
{
    private const int TotalPages = 5;
    private int _currentPage = -1;
    private Button? _agreementNextBtn;
    private readonly Storyboard _fadeInStoryboard = new();
    private readonly DoubleAnimation _fadeInAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(300),
        From = 0,
        To = 1,
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    public event Action? OnCompleted;
    public event Action? OnSkipped;
    // todo：向导还有大部分没弄好
    // ── 第2页：协议复选框 ──
    private CheckBox? CbLicense, CbUser, CbPrivacy;

    // ── 第3页：基本设置开关 ──
    private ToggleSwitch? SwAutoStart, SwIdleOpen, SwIdleMax, SwShortcut;

    // ── 第4页：外观设置 ──
    private ComboBox? CboTheme;

    // ── 第5页：信息输入 ──
    private TextBox? TxtCity, TxtSchool, TxtClass;

    public SetupWizard()
    {
        InitializeComponent();
        Title = "ClassLively 设置向导";
        AppWindow.Resize(new SizeInt32(840, 620));
        ExtendsContentIntoTitleBar = true;

        RootGrid.RequestedTheme = ElementTheme.Dark;

        // 淡入动画
        Storyboard.SetTargetProperty(_fadeInAnimation, "Opacity");
        _fadeInStoryboard.Children.Add(_fadeInAnimation);

        Closed += (s, e) =>
        {
            if (_currentPage < TotalPages - 1)
                OnSkipped?.Invoke();
        };

        ShowPage(0);
    }

    /// <summary>检查是否显示向导</summary>
    public static bool IsWizardNeeded()
    {
        var wizardPath = Path.Combine(AppContext.BaseDirectory, "config", "Setup_Wizard.json");
        if (!File.Exists(wizardPath)) return true;
        try
        {
            var json = File.ReadAllText(wizardPath);
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            return data.GetProperty("completed").GetInt32() != 1;
        }
        catch { return true; }
    }

    //  页面切换

    private void ShowPage(int index)
    {
        _currentPage = index;
        PageContainer.Children.Clear();

        var page = index switch
        {
            0 => BuildWelcomePage(),
            1 => BuildAgreementPage(),
            2 => BuildBasicSettingsPage(),
            3 => BuildAppearancePage(),
            4 => BuildSchoolInfoPage(),
            _ => BuildWelcomePage()
        };

        PageContainer.Children.Add(page);

        page.Opacity = 0;
        _fadeInStoryboard.Stop();
        Storyboard.SetTarget(_fadeInAnimation, page);
        _fadeInStoryboard.Begin();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < TotalPages - 1)
        {
            ShowPage(_currentPage + 1);
        }
        else
        {
            SaveAllSettings();
            MarkCompleted();
            OnCompleted?.Invoke();
            Close();
        }
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0)
            ShowPage(_currentPage - 1);
    }

    //  导航按钮

    private StackPanel CreateNavButtons(bool showPrev, bool showNext, string nextText = "下一步")
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };

        if (showPrev)
        {
            var prevBtn = new Button { Content = "上一步", Width = 100 };
            prevBtn.Click += Prev_Click;
            panel.Children.Add(prevBtn);
        }

        if (showNext)
        {
            var nextBtn = new Button
            {
                Content = nextText,
                Style = Application.Current.Resources["AccentButtonStyle"] as Style,
                Width = 120
            };
            nextBtn.Click += Next_Click;
            panel.Children.Add(nextBtn);
        }

        return panel;
    }

    private (StackPanel Panel, Button NextBtn) CreateNavButtonsWithNextRef(bool showPrev, string nextText)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };

        if (showPrev)
        {
            var prevBtn = new Button { Content = "上一步", Width = 100 };
            prevBtn.Click += Prev_Click;
            panel.Children.Add(prevBtn);
        }

        var nextBtn = new Button
        {
            Content = nextText,
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Width = 120,
            IsEnabled = false
        };
        nextBtn.Click += Next_Click;
        panel.Children.Add(nextBtn);

        return (panel, nextBtn);
    }

    //  第1页 — 欢迎

    private UIElement BuildWelcomePage()
    {
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 24,
            Children =
            {
                new Image
                {
                    Source = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.png")),
                    Width = 112,
                    Height = 112,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "ClassLively",
                    FontSize = 34,
                    FontWeight = new FontWeight { Weight = 700 },
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "114514",
                    FontSize = 15,
                    Foreground = new SolidColorBrush(ColorHelper.FromRgba(255, 255, 255, 160)),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                CreateNavButtons(showPrev: false, showNext: true, nextText: "开始使用")
            }
        };
    }

    //  第2页 — 软件使用协议

    private UIElement BuildAgreementPage()
    {
        // todo：应放超链接
        CbLicense = new CheckBox
        {
            Content = "我已阅读并同意 GPL-3.0 开源许可协议",
            FontSize = 14,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Margin = new Thickness(0, 6, 0, 6)
        };
        CbUser = new CheckBox
        {
            Content = "我已阅读并同意用户使用协议",
            FontSize = 14,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Margin = new Thickness(0, 6, 0, 6)
        };
        CbPrivacy = new CheckBox
        {
            Content = "我已阅读并同意隐私政策",
            FontSize = 14,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Margin = new Thickness(0, 6, 0, 6)
        };

        var (navPanel, nextBtn) = CreateNavButtonsWithNextRef(showPrev: true, nextText: "下一步");
        _agreementNextBtn = nextBtn;

        RoutedEventHandler updateNext = (_, _) =>
        {
            if (_agreementNextBtn != null)
                _agreementNextBtn.IsEnabled =
                    CbLicense?.IsChecked == true &&
                    CbUser?.IsChecked == true &&
                    CbPrivacy?.IsChecked == true;
        };

        foreach (var cb in new[] { CbLicense, CbUser, CbPrivacy })
        {
            cb!.Checked += updateNext;
            cb.Unchecked += updateNext;
        }

        return new StackPanel
        {
            Padding = new Thickness(60, 40, 60, 40),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = "软件使用协议",
                    FontSize = 30,
                    FontWeight = new FontWeight { Weight = 700 },
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                },
                new TextBlock
                {
                    Text = "请仔细阅读以下协议，同意所有选项继续",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(ColorHelper.FromRgba(255, 255, 255, 140)),
                    TextWrapping = TextWrapping.Wrap
                },
                CbLicense,
                CbUser,
                CbPrivacy,
                navPanel
            }
        };
    }

    //  第3页 — 基本设置

    private UIElement BuildBasicSettingsPage()
    {
        SwAutoStart = new ToggleSwitch { IsOn = true, Header = "开机自启动" };
        SwIdleOpen = new ToggleSwitch { IsOn = true, Header = "空闲时自动打开" };
        SwIdleMax = new ToggleSwitch { IsOn = false, Header = "自动打开时最大化" };
        SwShortcut = new ToggleSwitch { IsOn = false, Header = "创建桌面快捷方式" };

        return new StackPanel
        {
            Padding = new Thickness(60, 40, 60, 40),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "基本设置",
                    FontSize = 30,
                    FontWeight = new FontWeight { Weight = 700 },
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                },
                CreateSettingCard("开机自启动", SwAutoStart),
                CreateSettingCard("空闲时自动打开", SwIdleOpen),
                CreateSettingCard("自动打开时最大化", SwIdleMax),
                CreateSettingCard("创建桌面快捷方式", SwShortcut),
                new StackPanel { Height = 20 },
                CreateNavButtons(showPrev: true, showNext: true)
            }
        };
    }

    //  第4页 — 外观设置

    private UIElement BuildAppearancePage()
    {
        CboTheme = new ComboBox
        {
            Width = 400,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        CboTheme.Items.Add("跟随系统");
        CboTheme.Items.Add("浅色模式");
        CboTheme.Items.Add("深色模式");
        CboTheme.SelectedIndex = 0;

        return new StackPanel
        {
            Padding = new Thickness(60, 40, 60, 40),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "外观设置",
                    FontSize = 30,
                    FontWeight = new FontWeight { Weight = 700 },
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 120,
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing = 6,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "主题模式",
                                    FontSize = 16,
                                    FontWeight = new FontWeight { Weight = 600 },
                                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                                },
                                CboTheme
                            }
                        }
                    }
                },
                new StackPanel { Height = 20 },
                CreateNavButtons(showPrev: true, showNext: true)
            }
        };
    }

    //  第5页 — 信息

    private UIElement BuildSchoolInfoPage()
    {
        TxtCity = new TextBox { PlaceholderText = "请输入城市名称", Width = 400, MaxLength = 50 };
        TxtSchool = new TextBox { PlaceholderText = "请输入学校名称", Width = 400, MaxLength = 50 };
        TxtClass = new TextBox { PlaceholderText = "请输入班级", Width = 400, MaxLength = 50 };

        var countdownBtn = new Button
        {
            Content = "配置倒计时",
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 200
        };
        countdownBtn.Click += CountdownBtn_Click;

        return new StackPanel
        {
            Padding = new Thickness(60, 40, 60, 40),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "学校信息",
                    FontSize = 30,
                    FontWeight = new FontWeight { Weight = 700 },
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                },
                new TextBlock
                {
                    Text = "配置以下信息以启用学校相关功能（可跳过）",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(ColorHelper.FromRgba(255, 255, 255, 130))
                },
                CreateInputField("天气城市", TxtCity),
                CreateInputField("学校名称", TxtSchool),
                CreateInputField("班级", TxtClass),
                CreateInputField("倒计时配置", null, customControl: countdownBtn),
                new StackPanel { Height = 20 },
                CreateNavButtons(showPrev: true, showNext: true, nextText: "完成设置")
            }
        };
    }

    private void CountdownBtn_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 弹出倒计时编辑对话框
        Debug.WriteLine("[SetupWizard] 倒计时配置按钮点击");
    }

    //  辅助控件构建方法

    private static StackPanel CreateSettingCard(string label, ToggleSwitch sw)
    {
        sw.Margin = new Thickness(20, 0, 0, 0);
        sw.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(0, 10, 0, 10),
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = 15,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                },
                sw
            }
        };
    }

    private static StackPanel CreateInputField(string label, TextBox? textBox, UIElement? customControl = null)
    {
        var control = customControl ?? textBox;
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = 14,
                    FontWeight = new FontWeight { Weight = 600 },
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                },
                control!
            }
        };
    }

    //  保存配置

    private void SaveAllSettings()
    {
        // ── 基本设置 ──
        AppSettings.Set("auto_start", SwAutoStart?.IsOn ?? false);
        AppSettings.Set("idle_auto_open", SwIdleOpen?.IsOn ?? false);
        AppSettings.Set("idle_maximize", SwIdleMax?.IsOn ?? false);
        AppSettings.Set("desktop_shortcut", SwShortcut?.IsOn ?? false);

        // ── 外观设置 ──
        var themeIndex = CboTheme?.SelectedIndex ?? 0;
        var themeStr = themeIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "system"
        };
        AppSettings.Set("theme_mode", themeStr);

        // ── 学校信息 ──
        AppSettings.Set("weather_city", TxtCity?.Text?.Trim() ?? "");
        AppSettings.Set("school_name", TxtSchool?.Text?.Trim() ?? "");
        AppSettings.Set("class_name", TxtClass?.Text?.Trim() ?? "");

        _ = AppSettings.SaveAsync();

        Debug.WriteLine("[SetupWizard] 所有配置已保存到 settings.json");
    }

    private static void MarkCompleted()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "config");
        Directory.CreateDirectory(dir);
        var wizardPath = Path.Combine(dir, "Setup_Wizard.json");

        var data = new
        {
            completed = 1,
            completed_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        File.WriteAllText(wizardPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));

        Debug.WriteLine($"[SetupWizard] 向导已完成，标记已写入 {wizardPath}");
    }
}
