using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;

namespace ClassLively_UI.Controls;

/// <summary>
/// 快捷启动栏
///   MAX_SCALE=1.45, BASE_SCALE=1.0, MAGNIFY_RANGE=100px
///   ANIM_SPEED=0.22, BOUNCE_H=14px, BOUNCE_DUR=800ms
///   FPS=60
/// </summary>
public sealed partial class QuickLaunchDock : UserControl

    //  常量
    private const double MaxScale = 1.45;
    private const double BaseScale = 1.0;
    private const double MagnifyRange = 100;
    private const double AnimSpeed = 0.22;
    private const double BounceH = 14;
    private const int BounceDur = 800;
    private const int MaxApps = 12;

    //  状态
    private readonly List<QuickLaunchItem> _items = new();
    private int _iconSize = 48;
    private int _iconGap = 12;
    private bool _showLabels = true;

    // 悬停状态
    private int _hoverIdx = -1;
    private readonly List<double> _scales = new();       // 当前缩放值
    private readonly List<double> _targetScales = new();  // 目标缩放值
    private DispatcherTimer? _animTimer;                  // 动画定时器

    // 弹跳状态
    private int _bounceIdx = -1;
    private bool _bounceActive;

    // 拖拽状态
    private int _draggingIdx = -1;
    private Point? _dragStartPos;
    private bool _isInternalDrag;
    private int _dropTargetIdx = -1;
    private Point? _dragCurrentPos;

    //  UI 元素
    private Border _dockBorder = null!;
    private StackPanel _iconsPanel = null!;

    public event Action<string>? AppLaunched;  // (appName)

    public QuickLaunchDock()
    {
        InitializeComponent();
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // ~60fps
        _animTimer.Tick += OnAnimTick;
        this.Unloaded += OnUnloaded;
    }

    /// <summary>页面卸载时清理动画定时器</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _animTimer?.Stop();
    }

    /// <summary>设置应用列表</summary>
    public void SetApps(List<Dictionary<string, string>> apps, int animateIdx = -1)
    {
        _iconsPanel.Children.Clear();
        _items.Clear();
        _scales.Clear();
        _targetScales.Clear();

        if (apps == null || apps.Count == 0)
        {
            // 空状态
            var hintLabel = new TextBlock
            {
                Text = "暂无快捷方式\n拖拽 .exe / .lnk 到此处添加",
                FontSize = 12,
                Foreground = new SolidColorBrush(ColorHelper.FromRgba(255, 255, 255, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            _iconsPanel.Children.Add(hintLabel);
            Visibility = Visibility.Visible;
            return;
        }

        Visibility = Visibility.Visible;

        for (var i = 0; i < apps.Count && i < MaxApps; i++)
        {
            var app = apps[i];
            var item = CreateIconItem(app, i);
            _items.Add(item);
            _iconsPanel.Children.Add(item.Container);
            _scales.Add(BaseScale);
            _targetScales.Add(BaseScale);
        }

        UpdateDockSize();
    }

    /// <summary>更新图标大小</summary>
    public void UpdateIconSize(int size)
    {
        _iconSize = size;
        foreach (var item in _items)
            UpdateItemIconSize(item);
        UpdateDockSize();
    }

    //  创建单个图标项
    private QuickLaunchItem CreateIconItem(Dictionary<string, string> app, int index)
    {
        var name = app.GetValueOrDefault("name", "App");
        var path = app.GetValueOrDefault("path", "");
        var iconFile = app.GetValueOrDefault("icon", "exe.ico");
        var type = app.GetValueOrDefault("type", "app");

        var container = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Background = new SolidColorBrush(ColorHelper.Transparent),
            Tag = index,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = new Point(0.5, 1.0),  // 从底部中心缩放
            RenderTransform = new ScaleTransform { ScaleX = BaseScale, ScaleY = BaseScale }
        };

        // 图标图片
        var img = new Image
        {
            Width = _iconSize,
            Height = _iconSize,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        LoadIconImage(img, path, iconFile);

        var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(img);

        // 悬停时候的名称标签
        if (_showLabels)
        {
            var label = new TextBlock
            {
                Text = name.Length > 50 ? name[..50] + "..." : name,
                FontSize = 12,
                Foreground = new SolidColorBrush(ColorHelper.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0,  // 默认隐藏
                IsHitTestVisible = false,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(label);
            container.PointerEntered += (_, _) => label.Opacity = 1.0;
            container.PointerExited += (_, _) => label.Opacity = 0.0;
        }

        container.Child = stack;

        // 绑定事件
        container.PointerPressed += OnIconPointerPressed;
        container.PointerMoved += OnIconPointerMoved;
        container.PointerReleased += OnIconPointerReleased;
        container.PointerCaptureLost += OnIconPointerCaptureLost;  // 窗口切换时清理拖拽状态
        container.PointerEntered += OnIconPointerEntered;
        container.PointerExited += OnIconPointerExited;
        container.PointerCanceled += OnIconPointerExited;

        return new QuickLaunchItem
        {
            Container = container,
            Image = img,
            Name = name,
            Path = path,
            Type = type,
            ScaleTransform = (ScaleTransform)container.RenderTransform!,
            Index = index
        };
    }

    private static async void LoadIconImage(Image img, string appPath, string iconFile)
    {
        try
        {
            // 从 exe/lnk 提取图标
            if (!string.IsNullOrEmpty(appPath) &&
                (appPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                 appPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
            {
                var bmp = await ExtractIconAsync(appPath);
                if (bmp != null)
                {
                    img.Source = bmp;
                    return;
                }
            }

            // 回退：自定义图标或占位符
            img.Source = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.png"));
        }
        catch
        {
            img.Source = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.png"));
        }
    }

    /// <summary>从 exe 文件提取图标todo没做完</summary>
    private static async Task<BitmapSource?> ExtractIconAsync(string filePath)
    {
        try
        {
            // WinUI 3 中提取文件图标需要 P/Invoke 或 StorageItemThumbnail
            // 这里先用占位图
            await Task.CompletedTask;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateItemIconSize(QuickLaunchItem item)
    {
        item.Image.Width = _iconSize;
        item.Image.Height = _iconSize;
    }

    //  悬停放大动画
    private static double Smoothstep(double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return t * t * (3 - 2 * t);
    }

    private void CalcTargetScales(double mouseX)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_hoverIdx >= 0 && Math.Abs(i - _hoverIdx) <= 2)
            {
                // 计算图标中心X坐标
                var cx = GetIconCenterX(i);
                var d = Math.Abs(mouseX - cx);
                if (d < MagnifyRange)
                {
                    var t = Smoothstep(1.0 - d / MagnifyRange);
                    _targetScales[i] = BaseScale + (MaxScale - BaseScale) * t;
                }
                else
                {
                    _targetScales[i] = BaseScale;
                }
            }
            else
            {
                _targetScales[i] = BaseScale;
            }
        }
        EnsureAnimTimer();
    }

    private double GetIconCenterX(int idx)
    {
        if (idx < 0 || idx >= _items.Count) return 0;
        var item = _items[idx];
        var transform = item.Container.TransformToVisual(this);
        var pos = transform.TransformPoint(new Point(0, 0));
        return pos.X + item.Container.ActualWidth / 2;
    }

    private void EnsureAnimTimer()
    {
        if (!_animTimer!.IsEnabled)
            _animTimer.Start();
    }

    private void OnAnimTick(object sender, object e)
    {
        var changed = false;
        for (var i = 0; i < _scales.Count; i++)
        {
            var cur = _scales[i];
            var tgt = _targetScales[i];
            var diff = tgt - cur;
            if (Math.Abs(diff) > 0.005)
            {
                // LERP 插值
                var sp = AnimSpeed * 1.0; // 60fps 基准速度
                if (Math.Abs(diff) < 0.02)
                    _scales[i] = tgt;
                else
                    _scales[i] += diff * Math.Min(sp, 1.0);
                changed = true;
            }
        }

        // 应用缩放到每个图标容器
        for (var i = 0; i < _items.Count; i++)
        {
            if (i < _scales.Count)
            {
                _items[i].ScaleTransform.ScaleX = _scales[i];
                _items[i].ScaleTransform.ScaleY = _scales[i];
            }
        }

        if (!changed)
            _animTimer!.Stop();
    }

    //  点击弹跳动画
    //  0%→0px, 14%→-14px, 28%→0px, 44%→-7px,
    //  58%→0px, 72%→-3.08px, 86%→0px, 100%→0px

    private void StartBounce(int idx)
    {
        if (idx < 0 || idx >= _items.Count) return;
        _bounceIdx = idx;
        _bounceActive = true;

        var item = _items[idx];
        var bounceAnim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(BounceDur),
            EnableDependentAnimation = true
        };

        var bh = BounceH;
        var kf0 = new EasingDoubleKeyFrame(); kf0.Value = 0; kf0.KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero); bounceAnim.KeyFrames.Add(kf0);
        var kf1 = new EasingDoubleKeyFrame(); kf1.Value = -bh; kf1.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BounceDur * 0.14)); bounceAnim.KeyFrames.Add(kf1);
        var kf2 = new EasingDoubleKeyFrame(); kf2.Value = 0; kf2.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BounceDur * 0.28)); bounceAnim.KeyFrames.Add(kf2);
        var kf3 = new EasingDoubleKeyFrame(); kf3.Value = -bh * 0.50; kf3.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BounceDur * 0.44)); bounceAnim.KeyFrames.Add(kf3);
        var kf4 = new EasingDoubleKeyFrame(); kf4.Value = 0; kf4.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BounceDur * 0.58)); bounceAnim.KeyFrames.Add(kf4);
        var kf5 = new EasingDoubleKeyFrame(); kf5.Value = -bh * 0.22; kf5.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BounceDur * 0.72)); bounceAnim.KeyFrames.Add(kf5);
        var kf6 = new EasingDoubleKeyFrame(); kf6.Value = 0; kf6.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BounceDur * 0.86)); bounceAnim.KeyFrames.Add(kf6);
        var kf7 = new EasingDoubleKeyFrame(); kf7.Value = 0; kf7.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BounceDur)); bounceAnim.KeyFrames.Add(kf7);

        var tt = new TranslateTransform();
        item.Container.RenderTransformOrigin = new Point(0.5, 1.0);
        Storyboard.SetTarget(bounceAnim, tt);
        Storyboard.SetTargetProperty(bounceAnim, "Y");

        var sb = new Storyboard();
        sb.Children.Add(bounceAnim);
        sb.Completed += (_, _) =>
        {
            _bounceActive = false;
            _bounceIdx = -1;
            // 恢复 ScaleTransform
            item.Container.RenderTransform = item.ScaleTransform;
        };

        // 组合 Scale + Translate
        var tg = new TransformGroup();
        tg.Children.Add(item.ScaleTransform);
        tg.Children.Add(tt);
        item.Container.RenderTransform = tg;

        sb.Begin();
    }

    //  鼠标事件
    private void OnIconPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is int idx)
        {
            var pt = e.GetCurrentPoint(this);
            _dragStartPos = pt.Position;
            _draggingIdx = idx;
            border.CapturePointer(e.Pointer);
        }
    }

    private void OnIconPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not int idx) return;
        var pt = e.GetCurrentPoint(this);

        // 判断是否触发拖拽（距离 > 10px）
        if (_draggingIdx == idx && _dragStartPos.HasValue && !_isInternalDrag)
        {
            var dx = pt.Position.X - _dragStartPos.Value.X;
            var dy = pt.Position.Y - _dragStartPos.Value.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > 10)
            {
                StartInternalDrag(idx);
            }
        }

        // 更新拖拽位置
        if (_isInternalDrag && _draggingIdx == idx)
        {
            _dragCurrentPos = pt.Position;
            UpdateDropTarget(pt.Position);
        }

        // 更新悬停缩放
        CalcTargetScales(pt.Position.X);
    }

    private void OnIconPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not int idx) return;

        if (_isInternalDrag && _draggingIdx == idx)
        {
            FinishDragReorder();
        }
        else if (_draggingIdx == idx && _dragStartPos.HasValue)
        {
            // 普通点击 → 启动应用
            LaunchApp(idx);
        }

        ResetDragState();
    }

    private void OnIconPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is int idx)
        {
            _hoverIdx = idx;
            var pt = e.GetCurrentPoint(this);
            CalcTargetScales(pt.Position.X);
        }
    }

    private void OnIconPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is int idx && _hoverIdx == idx)
        {
            _hoverIdx = -1;
            for (var i = 0; i < _targetScales.Count; i++)
                _targetScales[i] = BaseScale;
            EnsureAnimTimer();
        }
    }

    /// <summary>窗口切换时清理拖拽状态</summary>
    private void OnIconPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ResetDragState();
        if (_items.Count > 0 && _draggingIdx >= 0 && _draggingIdx < _items.Count)
            _items[_draggingIdx].Container.Opacity = 1.0;
    }

    private void DockPanel_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hoverIdx = -1;
        for (var i = 0; i < _targetScales.Count; i++)
            _targetScales[i] = BaseScale;
        EnsureAnimTimer();
    }

    private void ResetDragState()
    {
        _draggingIdx = -1;
        _dragStartPos = null;
        _isInternalDrag = false;
        _dropTargetIdx = -1;
        _dragCurrentPos = null;
    }

    //  拖拽排序

    private void StartInternalDrag(int idx)
    {
        _isInternalDrag = true;
        _dropTargetIdx = idx;
        _dragCurrentPos = _dragStartPos;

        // 被拖拽项半透明
        if (idx < _items.Count)
            _items[idx].Container.Opacity = 0.5;
    }

    private void UpdateDropTarget(Point pos)
    {
        // 基于鼠标X位置判断插入点todo没做完
        var newTarget = -1;
        for (var i = 0; i < _items.Count; i++)
        {
            if (i == _draggingIdx) continue;
            var item = _items[i];
            var transform = item.Container.TransformToVisual(this);
            var itemPos = transform.TransformPoint(new Point(0, 0));
            if (pos.X < itemPos.X + item.Container.ActualWidth / 2)
            {
                newTarget = i;
                break;
            }
        }
        if (newTarget == -1) newTarget = _items.Count;

        if (newTarget != _dropTargetIdx)
        {
            _dropTargetIdx = newTarget;
            // 视觉反馈todo
        }
    }

    private void FinishDragReorder()
    {
        if (_draggingIdx < 0 || _draggingIdx >= _items.Count) return;

        // 恢复透明度
        _items[_draggingIdx].Container.Opacity = 1.0;

        // 执行重排
        AppReordered?.Invoke(_draggingIdx, _dropTargetIdx);
    }

    /// <summary>外部处理重排后刷新列表</summary>
    public void RefreshOrder(List<Dictionary<string, string>> apps)
    {
        SetApps(apps);
    }

    public event Action<int, int>? AppReordered; // (fromIdx, toIdx)

    //  应用启动

    private void LaunchApp(int idx)
    {
        if (idx < 0 || idx >= _items.Count) return;
        var item = _items[idx];

        StartBounce(idx);
        AppLaunched?.Invoke(item.Name);

        _ = Task.Run(() =>
        {
            try
            {
                switch (item.Type)
                {
                    case "url":
                        _ = Launcher.LaunchUriAsync(new Uri(item.Path));
                        break;
                    case "folder":
                        _ = Launcher.LaunchFolderPathAsync(item.Path);
                        break;
                    default:
                        Process.Start(new ProcessStartInfo { FileName = item.Path, UseShellExecute = true });
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QuickLaunch] 启动失败: {item.Name} - {ex.Message}");
            }
        });
    }

    //  右键菜单

    private void ShowContextMenu(int idx, Point pos)
    {
        if (idx < 0 || idx >= _items.Count) return;
        var item = _items[idx];

        var menu = new MenuFlyout();

        var openItem = new MenuFlyoutItem { Text = $"打开 ({item.Name})" };
        openItem.Tapped += (_, _) => LaunchApp(idx);
        menu.Items.Add(openItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        var editItem = new MenuFlyoutItem { Text = "编辑" };
        editItem.Tapped += (_, _) => EditApp(idx);
        menu.Items.Add(editItem);
        var deleteItem = new MenuFlyoutItem { Text = "删除" };
        deleteItem.Tapped += (_, _) => DeleteApp(idx);
        menu.Items.Add(deleteItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        var pathText = item.Type == "url" ? $"网址: {item.Path}" : $"路径: {item.Path}";
        var infoItem = new MenuFlyoutItem { Text = pathText };
        infoItem.IsEnabled = false;
        menu.Items.Add(infoItem);

        menu.ShowAt(this, pos);
    }

    private void EditApp(int idx)
    {
        // todo实现编辑对话框
        Debug.WriteLine($"[QuickLaunch] 编辑应用: {_items[idx]?.Name}");
    }

    private void DeleteApp(int idx)
    {
        if (idx < 0 || idx >= _items.Count) return;
        AppDeleted?.Invoke(idx);
    }

    public event Action<int>? AppDeleted; // (idx)

    //  外部拖拽添加

    private void DockPanel_DragEnter(object sender, DragEventArgs e)
    {
        // 接受文件和URL拖放
        if (e.DataView.Contains(StandardDataFormats.StorageItems) ||
            e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void DockPanel_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var storageItem in items)
                {
                    if (storageItem is StorageFolder)
                    {
                        AppAdded?.Invoke(("folder", storageItem.Path, storageItem.Name));
                    }
                    else
                    {
                        var ext = System.IO.Path.GetExtension(storageItem.Name).ToLowerInvariant();
                        if (ext is ".exe" or ".lnk")
                            AppAdded?.Invoke(("app", storageItem.Path, storageItem.Name));
                    }
                }
            }
            else if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var text = await e.DataView.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text) &&
                    (text.StartsWith("http://") || text.StartsWith("https://") || text.StartsWith("www.")))
                {
                    AppAdded?.Invoke(("url", text.Trim(), text.Trim()[..Math.Min(30, text.Trim().Length)]));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[QuickLaunch] 拖放添加失败: {ex.Message}");
        }
    }

    public event Action<(string Type, string Path, string Name)>? AppAdded;

    //  布局计算

    private void UpdateDockSize()
    {
        if (_items.Count == 0)
        {
            Width = 0;
            Height = 0;
            return;
        }

        var w = _items.Count * _iconSize + (_items.Count - 1) * _iconGap + 40; // PAD_X*2=40
        var h = _iconSize + 20; // PAD_Y_TOP + PAD_Y_BOTTOM + label

        // 预留溢出空间
        w += (int)(_iconSize * (MaxScale - BaseScale)) * 2;
        h += (int)(BounceH + 10) + (_showLabels ? 28 : 0);

        Width = w;
        Height = h;
    }

    //  XAML 初始化

    private void InitializeComponent()
    {
        // 外层 Dock 容器
        _dockBorder = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 6, 6, 6),
            Background = CreateDockBackground(),
            BorderBrush = new SolidColorBrush(ColorHelper.FromRgba(255, 255, 255, 20)),
            BorderThickness = new Thickness(1),
            AllowDrop = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _dockBorder.DragEnter += DockPanel_DragEnter;
        _dockBorder.Drop += DockPanel_Drop;
        _dockBorder.PointerExited += DockPanel_PointerExited;
        _dockBorder.RightTapped += OnDockRightTapped;

        // 图标水平排列面板
        _iconsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = _iconGap,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _dockBorder.Child = _iconsPanel;
        Content = _dockBorder;
    }

    private void OnDockRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var pt = e.GetPosition(this);
        // 查找点击了哪个图标
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var transform = item.Container.TransformToVisual(this);
            var pos = transform.TransformPoint(new Point(0, 0));
            var rect = new Rect(pos.X, pos.Y, item.Container.ActualWidth, item.Container.ActualHeight);
            if (rect.Contains(pt))
            {
                ShowContextMenu(i, pt);
                return;
            }
        }
    }

    /// <summary>创建 Dock 背景</summary>
    private static Brush CreateDockBackground()
    {
        // 半透明深色背景
        return new SolidColorBrush(ColorHelper.FromRgba(30, 30, 32, 165));
    }
}

//  辅助数据类

internal sealed class QuickLaunchItem
{
    public Border Container { get; set; } = null!;
    public Image Image { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Type { get; set; } = "app";
    public ScaleTransform ScaleTransform { get; set; } = null!;
    public int Index { get; set; }
}
