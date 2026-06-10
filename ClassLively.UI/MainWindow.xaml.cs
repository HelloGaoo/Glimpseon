using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClassLively_UI;

/// <summary>
/// ClassLively 主窗口 — NavigationView 导航
/// 原 PyQt6: ClassLively.py > MainWindow
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        NavFrame.Navigate(typeof(Pages.HomePage));
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var tag = item.Tag as string;
        Type? pageType = tag switch
        {
            "home" => typeof(Pages.HomePage),
            "wallpaper" => typeof(Pages.WallpaperPage),
            "download" => typeof(Pages.DownloadPage),
            "settings" => typeof(Pages.SettingsPage),
            "update" => typeof(Pages.UpdatePage),
            "about" => typeof(Pages.AboutPage),
            "debug" => typeof(Pages.DebugPage),
            _ => null
        };

        if (pageType != null)
            NavFrame.Navigate(pageType);
    }
}
