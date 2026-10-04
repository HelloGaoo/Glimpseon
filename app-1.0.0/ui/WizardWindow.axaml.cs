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

// 向导窗口
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

public partial class WizardWindow : GlimpseonWindow
{
    private const string WizardConfigPathName = "Setup_Wizard.json";
    private static string WizardConfigPath => Path.Combine(Paths.DataConfig, WizardConfigPathName);

    private string _themeColor = Config.ThemeColor.Value;

    public static bool IsNeeded()
    {
        if (!File.Exists(WizardConfigPath))
        {
            return true;
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(WizardConfigPath));
            return !(doc.RootElement.TryGetProperty("completed", out var c) && c.GetInt32() == 1);
        }
        catch (Exception e)
        {
            Log.Warning($"向导标记读取失败 path={WizardConfigPath} err={e.Message}");
            return true;
        }
    }

    private static void WriteWizardFile(int completed)
    {
        Paths.EnsureDataDirs();
        Directory.CreateDirectory(Path.GetDirectoryName(WizardConfigPath)!);
        File.WriteAllText(WizardConfigPath, $"{{\"completed\": {completed}}}");
        Log.Info($"向导配置写入 {WizardConfigPath} completed={completed}");
    }

    public WizardWindow()
    {
        InitializeComponent();
        Title = AppUtils.Tr("wizard.title");
        LoadLocalizedText();
        LoadIcon();
        InitThemeCombo();
        InitColorPicker();
        ApplyThemeFromConfig();
    }

    private void LoadLocalizedText()
    {
        NextButtonText.Text = AppUtils.Tr("wizard.next");
        AgreeButtonText.Text = AppUtils.Tr("wizard.finish");
        AgreementTitle.Text = AppUtils.Tr("wizard.agreement_title");
        AgreementText.Text = AppUtils.Tr("wizard.agreement_text");
        SettingsTitle.Text = AppUtils.Tr("wizard.settings_title");
        SettingsText.Text = AppUtils.Tr("wizard.settings_text");
        AppearanceTitle.Text = AppUtils.Tr("wizard.appearance_title");
        AppearanceText.Text = AppUtils.Tr("wizard.appearance_text");
        SchoolInfoTitle.Text = AppUtils.Tr("wizard.school_info_title");
        SchoolInfoText.Text = AppUtils.Tr("wizard.school_info_text");
        OpenSourceCheckBox.Content = AppUtils.Tr("wizard.open_source_license");
        UserAgreementCheckBox.Content = AppUtils.Tr("wizard.user_agreement");
        PrivacyCheckBox.Content = AppUtils.Tr("wizard.privacy_policy");
        CityEdit.Text = Config.City.Value is "点击选择" or "Click to select" ? "" : Config.City.Value;
        SchoolEdit.Text = Config.School.Value;
        ClassEdit.Text = Config.SchoolClass.Value;
        AutoStartSwitch.IsChecked = Config.AutoStart.Value;
        AutoOpenOnIdleSwitch.IsChecked = Config.AutoOpenOnIdle.Value;
        AutoOpenMaximizeSwitch.IsChecked = Config.AutoOpenMaximize.Value;
    }

    private void LoadIcon()
    {
        var iconPath = Paths.GetResourcePath(Constants.AppIcon);
        if (File.Exists(iconPath))
        {
            try
            {
                IconImage.Source = new Bitmap(iconPath);
            }
            catch (Exception e)
            {
                Log.Warning($"向导图标加载失败: {e.Message}");
            }
        }
    }

    private void InitThemeCombo()
    {
        ThemeCombo.Items.Add(AppUtils.Tr("wizard.theme_light"));
        ThemeCombo.Items.Add(AppUtils.Tr("wizard.theme_dark"));
        ThemeCombo.Items.Add(AppUtils.Tr("wizard.theme_system"));
        ThemeCombo.SelectedIndex = Config.ThemeMode.Value switch
        {
            ThemeMode.Light => 0,
            ThemeMode.Dark => 1,
            _ => 2,
        };
    }

    private void InitColorPicker()
    {
        try
        {
            ThemeColorPicker.Color = Color.Parse(Config.ThemeColor.Value);
        }
        catch (Exception e)
        {

            Log.Debug($"[向导] 颜色解析失败 用默认: {e.Message}");
            ThemeColorPicker.Color = Color.Parse("#30c361");
        }
        ThemeColorPicker.ColorChanged += (_, e) =>
        {
            _themeColor = e.NewColor.ToString();
            Config.ThemeColor.Value = _themeColor;
        };
    }

    private void ApplyThemeFromConfig()
    {
        var app = Avalonia.Application.Current;
        if (app is null)
        {
            return;
        }
        app.RequestedThemeVariant = Config.ThemeMode.Value switch
        {
            ThemeMode.Light => Avalonia.Styling.ThemeVariant.Light,
            ThemeMode.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    private void OnNextClicked(object sender, RoutedEventArgs e) => Pages.SelectedIndex = 1;

    private void OnAgreementChanged(object? sender, RoutedEventArgs e) =>
        AgreeButton.IsEnabled = OpenSourceCheckBox.IsChecked == true && UserAgreementCheckBox.IsChecked == true && PrivacyCheckBox.IsChecked == true;

    private void OnOpenLicenseClicked(object sender, RoutedEventArgs e)
    {
        var licensePath = Path.Combine(Paths.PackageRoot, "LICENSE");
        var readmePath = Path.Combine(Paths.PackageRoot, "README.md");
        var target = File.Exists(licensePath) ? licensePath : readmePath;
        Common.ShowTextFile(AppUtils.Tr("wizard.open_source_license"), AppUtils.Tr("wizard.open_source_license"), target, this);
    }

    private void OnAgreeClicked(object sender, RoutedEventArgs e)
    {
        Pages.SelectedIndex = 2;
    }

    private void OnFinishPage3Clicked(object sender, RoutedEventArgs e)
    {
        Config.AutoStart.Value = AutoStartSwitch.IsChecked == true;
        Config.AutoOpenOnIdle.Value = AutoOpenOnIdleSwitch.IsChecked == true;
        Config.AutoOpenMaximize.Value = AutoOpenMaximizeSwitch.IsChecked == true;
        if (DesktopShortcutSwitch.IsChecked == true)
        {
            CreateDesktopShortcut();
        }
        Pages.SelectedIndex = 3;
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedIndex < 0)
        {
            return;
        }
        Config.ThemeMode.Value = ThemeCombo.SelectedIndex switch
        {
            0 => ThemeMode.Light,
            1 => ThemeMode.Dark,
            _ => ThemeMode.Auto,
        };
        ApplyThemeFromConfig();
    }

    private void OnNextPage4Clicked(object sender, RoutedEventArgs e) => Pages.SelectedIndex = 4;

    private void OnFinishClicked(object sender, RoutedEventArgs e)
    {
        Config.City.Value = string.IsNullOrWhiteSpace(CityEdit.Text) ? Config.City.Value : CityEdit.Text.Trim();
        Config.School.Value = SchoolEdit.Text?.Trim() ?? "";
        Config.SchoolClass.Value = ClassEdit.Text?.Trim() ?? "";

        // 城市坐标查询
        var city = Config.City.Value;
        (double? lon, double? lat) = new RegionDatabase().GetCoordinates(city);
        if (lon is not null && lat is not null)
        {
            Config.Longitude.Value = lon.Value;
            Config.Latitude.Value = lat.Value;
            Log.Info($"向导 城市={city} 经纬度=({lon} {lat})");
        }
        else
        {
            Log.Warning($"向导 未获取到城市坐标 天气功能可能受限: city={city}");
        }

        Log.Info($"向导完成 城市={Config.City.Value} 学校={Config.School.Value} 班级={Config.SchoolClass.Value}");
        WriteWizardFile(1);
        Close();
    }

    private void CreateDesktopShortcut()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var exePath = Environment.ProcessPath ?? "";
            var linkPath = Path.Combine(desktop, "Glimpseon.lnk");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{linkPath}'); $s.TargetPath = '{exePath}'; $s.WorkingDirectory = '{Paths.PackageRoot}'; $s.Save()\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            System.Diagnostics.Process.Start(psi);
            Log.Debug($"向导 桌面快捷方式已创建: {linkPath} -> {exePath}");
        }
        catch (Exception e)
        {
            Log.Warning($"向导 创建桌面快捷方式失败: {e.Message}");
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
    }
}
