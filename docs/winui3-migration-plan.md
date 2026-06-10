# ClassLively: PyQt6 → C# WinUI 3 迁移方案

> **版本**: v1.0 | **日期**: 2026-06-09 | **状态**: 待审查

---

## 一、可行性结论

### 完全可行，但有条件约束

| 维度 | 评估 | 说明 |
|------|------|------|
| **技术可行性** | **可行** | WinUI 3 + .NET 8 可覆盖全部现有功能 |
| **性能提升** | **显著** | GPU 硬件渲染替代 CPU 软绘制，预计动画帧率 15fps→60fps |
| **代码复用** | **高** | `core/`、`services/`、`classlively_native/` 全部保留，仅重写 `ui/` 层 |
| **开发模式** | **AI 编码 + 用户审查** | 用户不碰 C# 代码，只看效果和审查 |
| **风险等级** | **中** | 3 个高风险模块需重点设计（见下方） |

### 核心架构：混合进程模型

```
┌──────────────────────────────────────────────────────┐
│  进程 A: ClassLively.UI.exe (C# WinUI 3)            │
│  ┌──────────┐ ┌──────────┐ ┌────────────────────┐   │
│  │ MainWindow│ │ Pages    │ │ Custom Controls    │   │
│  │ (导航框架) │ │ (7个页面) │ │ (Dock/Media/拖拽)  │   │
│  └─────┬─────┘ └────┬─────┘ └────────┬───────────┘   │
│        │            │                │               │
│        └────────────┼────────────────┘               │
│                     ▼                                │
│           HttpClient / 命名管道                       │
│                     ▲                                │
├─────────────────────┼────────────────────────────────┤
│                     │                                │
│  进程 B: python ClassLively.py (FastAPI 后台)        │
│  ┌──────────┐ ┌────┴─────┐ ┌──────────────────┐     │
│  │ core/     │ │services/ │ │classlively_native│     │
│  │ config    │ │ weather  │ │ (C++ 加速模块)    │     │
│  │ downloader│ │ poetry   │ │ blur/wallpaper/  │     │
│  │ updater   │ │ media    │ │ hook/idle/...    │     │
│  └──────────┘ └──────────┘ └──────────────────┘     │
└──────────────────────────────────────────────────────┘
```

**为什么选双进程而非单进程**：
- Python 业务逻辑（~8000 行）已稳定运行，无重写必要
- `classlively_native` pybind11 模块无法直接被 C# 调用（需 P/Invoke 重写或代理）
- 渐进式迁移：C# UI 可独立开发调试，Python 后台保持不变
- 风险隔离：UI 崩溃不影响后台服务

---

## 二、现有 UI 层完整清单

### 2.1 文件清单

| # | 文件 | 行数 | 职责 | 迁移复杂度 |
|---|------|------|------|-----------|
| 1 | [ui/home.py](ui/home.py) | ~1412 | 主界面：时钟/天气/一言/倒计时/学校/快捷启动/媒体 + 模糊背景 | **高** |
| 2 | [ui/component.py](ui/component.py) | ~755 | 自定义组件库：DraggableWidget, MediaWidget, QuickLaunchDock 等 | **高** |
| 3 | [ui/component_settings.py](ui/component_settings.py) | ~401 | 7 个组件设置对话框 | 中 |
| 4 | [ui/wallpaper.py](ui/wallpaper.py) | ~512 | 壁纸界面：获取/预览/历史/模糊/设桌面 | 中 |
| 5 | [ui/settings.py](ui/settings.py) | ~250 | 全局设置：通用/外观/日志/数据管理 | 低 |
| 6 | [ui/debug.py](ui/debug.py) | ~546 | 调试面板：系统监控/网络诊断/API测试/元素检查 | 中 |
| 7 | [ui/download.py](ui/download.py) | ~328 | 软件下载：单/批量下载+进度环 | 低 |
| 8 | [ui/update.py](ui/update.py) | ~186 | 更新界面：版本检查/日志/自动更新 | 低 |
| 9 | [ui/about.py](ui/about.py) | ~126 | 关于界面：信息/链接/鸣谢 | 低 |
| 10 | [ui/common.py](ui/common.py) | ~24 | 公共基类：BaseScrollAreaInterface | 低 |
| 11 | [ClassLively.py](ClassLively.py) | ~1828 | 主入口：SplashScreen/WizardWindow/MainWindow/启动流程 | **高** |

**总计**: ~6368 行 UI 代码（含 ClassLively.py 的 UI 部分）

### 2.2 页面导航结构

```
MainWindow (FluentWindow)
├── HomeInterface          ← 主界面 (首页)
├── WallpaperInterface     ← 壁纸管理
├── DownloadInterface      ← 软件下载
├── SettingInterface       ← 设置 (底部)
├── UpdateInterface        ← 更新 (底部)
├── AboutInterface         ← 关于 (底部)
└── DebugPanel             ← 调试 (底部, 条件可见)

独立窗口:
├── SplashScreen          ← 启动画面
├── WizardWindow           ← 首次启动向导 (5页)
├── ComponentSettingDialog ×7 ← 组件设置弹窗
└── _TechDialog            ← 鸣谢弹窗
```

---

## 三、分阶段迁移计划

### 阶段 0：基础设施（预估 3-5 天）

#### 0.1 创建 C# 项目结构

```
ClassLively.UI/
├── ClassLively.UI.csproj          # WinUI 3 项目文件 (.NET 8, x64, Unpackaged)
├── App.xaml / App.xaml.cs         # 应用入口
├── MainWindow.xaml /.cs           # 主窗口 (NavigationView 导航)
├── Assets/                        # 图标/图片资源
├── Models/                        # 数据模型 (ViewModels)
│   ├── AppConfig.cs              # 配置项模型 (对应 Python cfg)
│   ├── WallpaperInfo.cs          # 壁纸信息
│   ├── SoftwareItem.cs           # 软件条目
│   └── MediaInfo.cs              # 媒体信息
├── Services/                      # API 客户端层
│   ├── ApiService.cs             # HTTP 客户端 (调 Python FastAPI)
│   ├── IApiService.cs            # 接口定义 (便于测试 Mock)
│   └── ConfigService.cs          # 配置同步服务
├── Controls/                      # 自定义控件
│   ├── BlurBackgroundControl.cs  # 模糊背景控件
│   ├── DraggableControl.cs       # 可拖拽基类
│   └── QuickLaunchDock.cs        # 快捷启动栏
├── Pages/                         # 页面
│   ├── HomePage.xaml/.cs         # 主界面
│   ├── WallpaperPage.xaml/.cs    # 壁纸页
│   ├── SettingsPage.xaml/.cs     # 设置页
│   ├── DownloadPage.xaml/.cs     # 下载页
│   ├── UpdatePage.xaml/.cs       # 更新页
│   ├── AboutPage.xaml/.cs        # 关于页
│   └── DebugPage.xaml/.cs        # 调试页
├── Dialogs/                       # 弹窗
│   ├── WizardDialog.xaml/.cs     # 向导窗口
│   └── ComponentSettingDialog.xaml/.cs
├── Helpers/                       # 工具类
│   ├── ThemeHelper.cs            # 主题管理
│   ├── TrayIconHelper.cs         # 系统托盘 (Win32 API)
│   └── WindowHelper.cs           # 窗口管理
└── Resources/
    ├── Strings/                  # 本地化字符串
    │   ├── zh-CN.resw
    │   ├── zh-TW.resw
    │   └── en-US.resw
    └── Styles/                   # XAML 样式资源
        ├── DarkTheme.xaml
        └── LightTheme.xaml
```

#### 0.2 Python FastAPI 后台包装

在 `core/` 下新增 `api_server.py`，将现有业务逻辑包装为 REST API：

```python
# core/api_server.py (新增文件，不影响任何现有代码)
from fastapi import FastAPI, HTTPException
import uvicorn

app = FastAPI(title="ClassLively Backend")

# === 配置 API ===
@app.get("/api/config")
def get_config(): return cfg.to_dict()

@app.post("/api/config/{key}")
def set_config(key: str, value): ...

@app.get("/api/config/items")
def list_config_items(): ...

# === 壁纸 API ===
@app.get("/api/wallpaper/current")
def get_current_wallpaper(): ...

@app.post("/api/wallpaper/fetch")
def fetch_wallpaper(source: str = None): ...

@app.post("/api/wallpaper/set-desktop")
def set_desktop_wallpaper(path: str): ...

@app.get("/api/wallpaper/history")
def get_history(page: int = 1, per_page: int = 20): ...

# === 天气 API ===
@app.get("/api/weather")
def get_weather(city: str = None): ...

# === 一言 API ===
@app.get("/api/poetry")
def get_poetry(): ...

# === 下载 API ===
@app.get("/api/software/list")
def list_software(category: str = None): ...

@app.post("/api/software/download/{name}")
def download_software(name: str): ...

@app.get("/api/software/download/status")
def download_status(): ...

# === 系统 API ===
@app.get("/api/system/idle-ms")
def idle_milliseconds(): ...
    from classlively_native import idle_get_milliseconds
    return {"ms": idle_get_milliseconds()}

# === 媒体 API ===
@app.get("/api/media/info")
def media_info(): ...
@app.get("/api/media/lyrics")
def media_lyrics(): ...

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=19856)
```

**硬性约束检查**：

| 约束 | 评估 |
|------|------|
| **全局兼容性** | 新增独立文件，不修改任何现有 `.py`。FastAPI 在子线程运行，与 PyQt6 不冲突 |
| **禁止短视实现** | 使用标准 FastAPI 路由，非临时脚本。接口设计考虑未来 gRPC 升级 |
| **技术债** | 无。纯增量添加 |
| **回归风险** | 无。现有功能完全不受影响 |
| **拒绝局部工作** | API 设计覆盖全部核心功能点（配置/壁纸/天气/媒体/下载/系统），非单功能 demo |

#### 0.3 C# API 客户端

```csharp
// Services/ApiService.cs
public class ApiService : IApiService
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:19856") };

    public async Task<Dictionary<string, object>> GetConfigAsync() { ... }
    public async Task SetConfigAsync(string key, object value) { ... }
    public async Task<WallpaperInfo> GetCurrentWallpaperAsync() { ... }
    public async Task<WallpaperInfo> FetchWallpaperAsync(string source = null) { ... }
    // ... 其余 API 方法
}
```

### 阶段 1：主框架 + 简单页面（预估 5-7 天）

#### 1.1 MainWindow 导航框架

**对应**: [ClassLively.py L918-L1453](ClassLively.py#L918-L1453) 的 `MainWindow`

```xml
<!-- MainWindow.xaml -->
<Window>
    <Grid>
        <NavigationView x:Name="NavView"
                        PaneDisplayMode="Left"
                        IsBackButtonVisible="Collapsed"
                        SelectionChanged="NavView_SelectionChanged">
            <NavigationView.MenuItems>
                <NavigationViewItem Icon="Home" Content="主页" Tag="home" />
                <NavigationViewItem Icon="Pictures" Content="壁纸" Tag="wallpaper" />
                <NavigationViewItem Icon="Download" Content="软件下载" Tag="download" />
            </NavigationView.MenuItems>
            <NavigationView.FooterMenuItems>
                <NavigationViewItem Icon="Setting" Content="设置" Tag="settings" />
                <NavigationViewItem Icon="Sync" Content="更新" Tag="update" />
                <NavigationViewItem Icon="Info" Content="关于" Tag="about" />
            </NavigationView.FooterMenuItems>
            <Frame x:Name="ContentFrame"/>
        </NavigationView>
    </Grid>
</Window>
```

**PyQt6 → WinUI 3 映射**:

| PyQt6 (qfluentwidgets) | WinUI 3 | 备注 |
|------------------------|---------|------|
| `FluentWindow` | `Window` + `NavigationView` | WinUI 3 原生 NavigationView |
| `NavigationInterface.addSubInterface()` | `Frame.Navigate(typeof(Page))` | 页面导航 |
| `setStyleSheet()` 全局 QSS | `ThemeResources` + XAML `RequestedTheme` | 原生深浅主题切换 |
| `isMaximized()` / `showMaximized()` | `AppWindow.Presenter.IsMaximized` | 窗口状态 |
| `closeEvent()` | `Closed` event | 关闭事件 |

#### 1.2 设置页面（优先做，最简单验证链路）

**对应**: [ui/settings.py](ui/settings.py) (~250 行)

这是最简单的页面，适合第一个迁移验证完整链路（C# UI → API → Python → 返回）。

**WinUI 3 控件映射**:

| qfluentwidgets 控件 | WinUI 3 替代 |
|---------------------|-------------|
| `SettingCardGroup` | `StackPanel` + `Expander` 或 GroupCard (Toolkit) |
| `SwitchSettingCard` | `ToggleSwitch` + `TextBlock` |
| `ComboBoxSettingCard` | `ComboBox` + `TextBlock` |
| `SpinBoxSettingCard` | `NumberBox` |
| `LineEditSettingCard` | `TextBox` |
| `ButtonSettingCard` | `Button` |
| `CustomColorSettingCard` | `ColorPicker` (Toolkit) |

#### 1.3 关于页面

**对应**: [ui/about.py](ui/about.py) (~126 行)

静态内容展示，无复杂交互。

#### 1.4 更新页面

**对应**: [ui/update.py](ui/update.py) (~186 行)

标准检查-下载-安装流程。

### 阶段 2：核心页面（预估 7-10 天）

#### 2.1 壁纸页面

**对应**: [ui/wallpaper.py](ui/wallpaper.py) (~512 行)

**关键功能点**:
- 多 API 源获取壁纸按钮
- 当前壁纸预览 (Image 控件)
- "另存为"/"设为桌面" 按钮
- 设置组 (保存数量/间隔/API源/自动同步)
- 效果组 (模糊半径/亮度 Slider)
- 历史记录网格 (GridView + 缩略图)
- 分页加载

**模糊背景方案**:
- 方案 A（推荐）：调用 Python API → Python 调 `classlively_native.blur_image()` → 返回模糊后图片字节 → C# 显示
- 方案 B：C# 直接用 Win2D GaussianBlurEffect（需额外引入 Win2D NuGet）
- **选择方案 A**：复用已有 C++ 实现，零重复开发

#### 2.2 下载页面

**对应**: [ui/download.py](ui/download.py) (~328 行)

**关键差异**:
- Python 用 `ThreadPoolExecutor` 并发下载 → C# 用 `async/await` + `HttpClient`
- Python 用 `QMetaObject.invokeMethod` 跨线程更新 UI → C# `Progress<T>` + `IProgress<T>`（天然线程安全）

#### 2.3 主界面（HomePage）— 基础版

**对应**: [ui/home.py](ui/home.py) (~1412 行) — 先做基础布局，复杂组件放阶段 3

**第一阶段包含**:
- 模糊背景层 (`BlurBackgroundControl`)
- 时钟组件 (TextBlock + DispatcherTimer)
- 天气组件 (图标 + 温度 TextBlock)
- 一言组件 (TextBlock)
- 倒计时组件 (TextBlock)
- 学校信息组件 (TextBlock)
- 暗化遮罩层 (Rectangle + Opacity)

**暂不包含**（阶段 3）:
- QuickLaunchDock（自定义绘制 Dock 栏）
- MediaWidget（媒体信息 + 歌词）
- DraggableWidget 拖拽编辑模式

### 阶段 3：复杂自定义组件（预估 10-14 天）

#### 3.1 QuickLaunchDock 快捷启动栏

**对应**: [component.py L1320-L2129](component.py#L1320-L2129) (~809 行)

**当前实现**: 完全 QPainter 自定义绘制，120FPS 定时器驱动动画循环，悬停放大 + 点击弹跳 + 拖排排序

**WinUI 3 实现方案**:
```
推荐: XAML 控件组合 + Composition API 动画
├── ItemsRepeater              ← 图标列表容器
│   └── ItemTemplate:
│       └── Grid (可拖拽)
│           ├── Image          ← 应用图标
│           └── TextBlock      ← 名称标签(可选显示)
├── ImplicitAnimation          ← 悬停缩放 (替代 QTimer 循环)
├── ElementAnimation           ← 点击弹跳 (KeyFrame 替代 bezier 手写)
└── DragDrop                   ← 拖放排序 (原生支持)
```

**备选**: 如果 XAML 组合性能不足，用 Win2D CanvasControl 自行绘制（类似原 QPainter 方案）

**硬性约束检查**:

| 约束 | 评估 |
|------|------|
| **全局兼容性** | 独立控件，不影响其他页面。与主页面的数据通过 ViewModel 传递 |
| **禁止短视实现** | 使用 Composition API（Windows 动画标准），非硬编码关键帧 hack |
| **技术债** | 可能需要引用 `CommunityToolkit.WinUI.Animations` 和 `Microsoft.Graphics.Win2D` |
| **回归风险** | 仅影响快捷启动栏自身。建议单独分支开发 |
| **拒绝局部工作** | 支持完整的 12 图标上限/拖放排序/右键菜单/悬停动画/点击反馈 |

#### 3.2 MediaWidget 媒体信息组件

**对应**: [component.py L598-L1213](component.py#L598-L1213) (~615 行)

**当前实现**: 多线程 Worker + LRU 缓存 + 歌词滚动 + 封面淡入动画 + WCAG 对比度颜色自适应

**WinUI 3 实现方案**:
```
MVVM 架构:
├── MediaViewModel
│   ├── RefreshAsync()         ← 定时轮询 (DispatcherTimer)
│   ├── Cache (LRU)            ← ConcurrentDictionary 替代
│   └── ColorAdaptation()      ← 对比度计算 (纯算法，语言无关)
├── MediaUserControl (XAML)
│   ├── CoverImage (Image)     ← 封面 + FadeIn ThemeAnimation
│   ├── TitleText (TextBlock)  ← 标题
│   ├── ArtistText (TextBlock) ← 艺术家
│   ├── LyricsText (TextBlock) ← 歌词 (ScrollViewer 自动滚动)
│   └── ProgressBar            ← 播放进度
└── DataService (API 调用)
    └── GET /api/media/info    ← 复用 Python 后台
```

#### 3.3 DraggableWidget 拖拽系统

**对应**: [component.py L93-L431](component.py#L93-L431) (~338 行)

**当前实现**: 百分比定位 + 9 种锚点 + 鼠标拖拽 + 吸附对齐 + 编辑模式虚线边框

**WinUI 3 实现方案**:
```
Canvas 定位 + Manipulation gestures:
├── DraggableControl : UserControl
│   ├── PercentX / PercentY (double, 0-100)  ← 百分比坐标
│   ├── AnchorMode (enum)                    ← 锆点枚举
│   ├── IsEditMode (bool)                     ← 编辑模式开关
│   ├── ManipulationDelta event               ← 拖拽手势
│   ├── SnapToGrid()                          ← 吸附对齐
│   └── PositionChanged event                 ← 位置变更通知
└── PositionSaver                             ← JSON 持久化 (替换 component_positions.json)
```

### 阶段 4：系统集成（预估 5-7 天）

#### 4.1 系统托盘

**对应**: [ClassLively.py L1174-L1197](ClassLively.py#L1174-L1197)

**问题**: WinUI 3 **无原生系统托盘支持**

**方案**: 使用 Win32 API `Shell_NotifyIconW` via P/Invoke

```csharp
// Helpers/TrayIconHelper.cs
public static class TrayIconHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    public static void CreateTrayIcon(IntPtr hwnd, Icon icon, Action onClick, ContextMenu menu) { ... }
    public static void RemoveTrayIcon() { ... }
}
```

**依赖**: 仅需 `user32.dll` + `shell32.dll`（Windows 内置 DLL）

#### 4.2 空闲检测 + 自动打开

**对应**: [ClassLively.py L1240-L1290](ClassLively.py#L1240-L1290)

**方案**: C# 定时器 + 调用 Python API `/api/system/idle-ms`

```csharp
// MainWindow.xaml.cs
private DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(10) };
private async void CheckIdle(object sender, EventArgs e)
{
    var result = await _api.GetIdleMsAsync();
    if (result.Ms > _idleThresholdMs && !IsPlayingMedia())
        ShowWindow();
}
```

#### 4.3 单例互斥锁

**方案 A（推荐）**: C# 端用 `System.Threading.Mutex`（纯托管，无需 P/Invoke）

```csharp
static readonly Mutex _mutex = new Mutex(true, "ClassLively_SingleInstance_Mutex_{GUID}");
if (!_mutex.WaitOne(0)) { /* 已有实例 */ }
```

**方案 B**: 调用 Python API（如果需要跨进程协调，用方案 A 即可）

#### 4.4 C++ Native 功能对接

| C++ 函数 | 当前调用方式 | C# 替代方案 |
|----------|------------|------------|
| `blur_image()` | Python pybind11 | **通过 Python API 代理**（推荐，避免重复实现） |
| `set_wallpaper()` | Python pybind11 | **P/Invoke DllImport**（简单函数，直接调更高效） |
| `idle_get_ms()` | Python pybind11 | **通过 Python API 代理** |
| `install/uninstall_hook()` | Python pybind11 | **P/Invoke DllImport**（钩子必须在同进程） |
| `extract_icon()` | Python pybind11 | **P/Invoke DllImport** 或 **通过 API 代理** |
| `acquire/release_mutex()` | Python pybind11 | **C# System.Threading.Mutex**（无需调用） |
| `install_font()` | Python pybind11 | **P/Invoke DllImport** 或 **通过 API 代理** |

**决策规则**:
- 必须在同进程执行的（如全局钩子）→ C# P/Invoke 直接调用 `classlively_native.dll`
- 可以跨进程的（如模糊、空闲检测）→ 通过 Python API 代理（减少 C# 端 P/Invoke 复杂度）

#### 4.5 启动画面 + 向导

**SplashScreen**: `Window` + `ProgressBar` + `TextBlock`，比 PyQt6 简单得多（WinUI 3 原生支持透明窗口 + 无边框）

**WizardWindow**: Community Toolkit 的 `WizardControl` 或手写 `Frame.Navigate()` 分页

### 阶段 5：打磨优化（预估 3-5 天）

- 动画曲线调优（缓入/缓出/弹性效果）
- 高 DPI 适配测试（100%/150%/200%/250%）
- 深浅主题全场景验证
- 多显示器测试
- 内存泄漏检测
- 性能基准对比（vs PyQt6 版本）

---

## 四、控件/概念完整映射表

### 4.1 基础控件

| PyQt6 / qfluentwidgets | WinUI 3 | 命名空间 |
|------------------------|---------|---------|
| `QWidget` | `UserControl` / `Page` | `Microsoft.UI.Xaml.Controls` |
| `QLabel` | `TextBlock` | 同上 |
| `QPushButton` | `Button` | 同上 |
| `QLineEdit` | `TextBox` | 同上 |
| `QTextEdit` (只读) | `TextBlock` (Wrapping) | 同上 |
| `QCheckBox` | `CheckBox` | 同上 |
| `QRadioButton` | `RadioButton` | 同上 |
| `QComboBox` | `ComboBox` | 同上 |
| `QSpinBox` | `NumberBox` | 同上 |
| `QSlider` | `Slider` | 同上 |
| `QProgressBar` | `ProgressBar` | 同上 |
| `QGroupBox` | `Expander` / `GroupCard` | 同上 / Toolkit |
| `QScrollArea` | `ScrollViewer` | 同上 |
| `QFileDialog` | `FilePicker` (WinRT) | `Windows.Storage.Pickers` |
| `QMessageBox` | `ContentDialog` | 同上 |
| `QSystemTrayIcon` | `Shell_NotifyIconW` (P/Invoke) | Win32 API |
| `QMenu` / `RoundMenu` | `MenuFlyout` | `Microsoft.UI.Xaml.Controls` |
| `QStatusBar` | `CommandBar` (底部) | 同上 |
| `QTabWidget` | `TabView` | 同上 |
| `QToolTip` | `ToolTipService` | `Microsoft.UI.Xaml` |
| `QSplitter` | `GridSplitter` | Community Toolkit |
| `InfoBar` (qfluentwidgets) | `InfoBar` | Community Toolkit |
| `CardWidget` (qfluentwidgets) | `CardControl` / `SettingsCard` | Community Toolkit |
| `PrimaryPushButton` | `Button` (Style="Accent") | WinUI 3 |
| `TransparentPushButton` | `Button` (Style="Transparent") | WinUI 3 |
| `SwitchButton` | `ToggleSwitch` | WinUI 3 |
| `AvatarWidget` | `PersonPicture` | WinUI 3 |
| `SearchBar` | `AutoSuggestBox` | WinUI 3 |
| `ProgressBar` (圆形) | `ProgressRing` | WinUI 3 |
| `ListWidget` | `ListView` | WinUI 3 |
| `TableWidget` | `DataGrid` | Community Toolkit |
| `IconWidget` | `FontIcon` / `BitmapIcon` | WinUI 3 |
| `FlowLayout` | `ItemsRepeater` + `FlowLayout` | Toolkit |

### 4.2 布局系统

| PyQt6 | WinUI 3 | 说明 |
|-------|---------|------|
| `QVBoxLayout` | `StackPanel` Orientation="Vertical" | 垂直排列 |
| `QHBoxLayout` | `StackPanel` Orientation="Horizontal" | 水平排列 |
| `QGridLayout` | `Grid` (RowDefinition + ColumnDefinition) | 表格布局 |
| `QFormLayout` | `StackPanel` (TextBlock + Input 配对) | 表单布局 |
| `百分比定位` (DraggableWidget) | `Canvas` (绝对定位) + 百分比计算 | 需自行实现 |
| `setGeometry(x,y,w,h)` | `Canvas.Left/Top/Width/Height` | 绝对定位 |
| `setSizePolicy` | `HorizontalAlignment/VerticalAlignment` | 对齐方式 |

### 4.3 样式/主题

| PyQt6 QSS | WinUI 3 XAML | 示例 |
|-----------|-------------|------|
| `setStyleSheet("background: #1e1e1e")` | `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` | 背景色 |
| `color: white` | `Foreground="White"` | 文字色 |
| `border-radius: 8px` | `CornerRadius="8"` | 圆角 |
| `font-size: 14px` | `FontSize="14"` | 字号 |
| `:hover` | `PointerOver` VisualState | 悬停态 |
| `:pressed` | `Pressed` VisualState | 按下态 |
| `QSS 文件加载` | `ThemeDictionaries` + `ResourceDictionary` | 主题字典 |
| `isDarkTheme()` 判断 | `ActualTheme == Dark` | 主题判断 |
| `setTheme(DARK/LIGHT)` | `RequestedTheme = Dark/Light` | 切换主题 |
| 18 个 QSS 文件 | 2 个 ResourceDictionary (Dark/Light) + 页面级 Style | 样式组织 |

### 4.4 事件/信号

| PyQt6 | WinUI 3 | 说明 |
|-------|---------|------|
| `pyqtSignal` | `event` / `EventHandler<T>` | 事件声明 |
| `.connect(slot)` | `+= handler` | 订阅事件 |
| `.emit(args)` | `?.Invoke(this, args)` | 触发事件 |
| `pyqtSlot` | 普通 method（自动线程安全） | 线程槽 |
| `QTimer.singleShot` | `Dispatcher.RunAsync` | 延迟执行 |
| `QTimer(interval)` | `DispatcherTimer` | 定时器 |
| `cfg.xxx.valueChanged.connect(fn)` | `cfg.PropertyChanged += fn` | 配置变更监听 |
| `QMetaObject.invokeMethod(..., Queued)` | `DispatcherQueue.TryEnqueue(...)` | 跨线程 UI 更新 |

### 4.5 多线程

| PyQt6 | WinUI 3 | 说明 |
|-------|---------|------|
| `QThread` + `run()` | `Task.Run()` / `async Task` | 后台任务 |
| `QThreadPoolExecutor` | `Parallel.ForEachAsync()` | 并发池 |
| `worker.signal.emit(data)` | `IProgress<T>.Report(data)` | 进度上报 |
| `mutex.lock/unlock` | `lock {}` / `SemaphoreSlim` | 同步原语 |

### 4.6 图形/绘制

| PyQt6 | WinUI 3 | 说明 |
|-------|---------|------|
| `QPixmap` / `QImage` | `SoftwareBitmap` / `WriteableBitmap` | 图片对象 |
| `QPainter` | `Win2D CanvasDrawingSession` | 2D 绘制 |
| `QGraphicsBlurEffect` | `GaussianBlurEffect` (Win2D) | 高斯模糊 |
| `QPropertyAnimation(opacity)` | `FadeInThemeAnimation` / `ScalarAnimator` | 透明度动画 |
| `QPen` / `QBrush` | `CanvasStrokeStyle` / `Color` | 画笔/画刷 |
| `QPainterPath` | `CanvasGeometry` / `PathBuilder` | 路径绘制 |
| `render(pixmap)` | `RenderTargetBitmap` | 控件截图 |

### 4.7 国际化

| PyQt6 | WinUI 3 | 说明 |
|-------|---------|------|
| `tr("key")` | `Loader.GetString("key")` | 翻译函数 |
| `locale/*.json` | `Strings/*.resw` | 语言资源文件 |
| `TranslatableWidget` | `ResourceContext.SetLanguageQualifier()` | 语言切换 |
| `cfg.language` 变更 | `ApplicationLanguages.PrimaryLanguageOverride` | 语言设置 |

---

## 五、风险评估与应对

### 5.1 高风险项

| 风险 | 影响 | 概率 | 应对策略 |
|------|------|------|---------|
| **QuickLaunchDock 动画流畅度** | 核心交互体验 | 中 | 先用 XAML ImplicitAnimation 验证；若不够顺滑再降级到 Win2D |
| **系统托盘稳定性** | 托盘消失=用户找不到程序 | 低 | 使用成熟的开源库 `Hardcodet.NotifyIconWpf` 的 WinUI 3 移植版；或封装为独立 C++/CLI 组件 |
| **拖拽编辑模式精度** | 组件位置错乱 | 低 | 百分比坐标 + Resize 事件重算，与原 Python 方案一致 |
| **C++ DLL 跨语言调用** | blur_image 参数传递错误 | 低 | 先走 Python API 代理路径验证；稳定后再优化为 P/Invoke 直调 |
| **内存占用增长** | WinUI 3 基础开销 > PyQt6 | 低 | .NET 8 已大幅优化；Unpackaged 模式减少 MSIX 开销 |

### 5.2 中风险项

| 风险 | 影响 | 概率 | 应对策略 |
|------|------|------|---------|
| **QSS → XAML 样式还原度** | 视觉不一致 | 中 | 截图逐页对比；使用 Community Toolkit 补充缺失控件样式 |
| **配置系统双写一致性** | C# 改了 Python 不知道 | 低 | 所有配置写入统一走 Python API；C# 只读+展示 |
| **深浅主题切换闪烁** | 用户体验差 | 低 | WinUI 3 原生 RequestedTheme 是即时切换，不会像 QSS 重载那样闪烁 |
| **80+ 配置项迁移遗漏** | 功能缺失 | 中 | 写自动化对照脚本：扫描 Python ConfigItem → 检查 C# AppConfig 是否全覆盖 |

### 5.3 回归风险矩阵

| 受影响模块 | 风险来源 | 测试范围 |
|-----------|---------|---------|
| **壁纸功能** | C++ blur_image 调用链路变化 | 获取→预览→设桌面→模糊背景 全流程 |
| **空闲检测** | 从 ctypes/Python → C#/HTTP API | 空闲超时→自动打开→恢复 |
| **媒体信息** | 多线程架构重构 | 歌曲切换→封面→歌词→进度 全流程 |
| **下载功能** | ThreadPoolExecutor → async/await | 单下载→批量下载→失败重试 |
| **配置持久化** | 双进程配置同步 | 改设置→重启→验证保留 |
| **全局钩子** | P/Invoke 调用 | 安装→键盘/鼠标事件→翻页防误触→卸载 |
| **国际化** | tr() JSON → .resw | 中英繁三语言切换→每个页面文字检查 |
| **多实例** | Python ctypes → C# Mutex | 启动两个实例→第二个应退出 |

---

## 六、技术债声明

### 6.1 不可避免的技术债

| 债务 | 原因 | 最小化方案 |
|------|------|-----------|
| **双进程通信延迟** | HTTP 往返约 1-5ms | 非高频操作走 HTTP；高频（如媒体轮询）用 WebSocket 或命名管道 |
| **classlively_native 双份绑定** | Python pybind11 + C# P/Invoke 维护两套 | C# 端优先走 API 代理；仅必须同进程的才 P/Invoke |
| **QSS 样式无法自动转换** | CSS-like 与 XAML 语法差异大 | 手写 XAML Style；保留原 QSS 作为视觉参考 |
| **QuickLaunchDock 需重写绘制逻辑** | QPainter → Win2D API 完全不同 | 优先 XAML 组合方案；仅在性能不足时用 Win2D |

### 6.2 可避免的技术债（禁止产生）

- ~~硬编码颜色值~~ → 全部使用 ThemeResource 引用
- ~~复制粘贴配置项定义~~ → 从 Python cfg 自动生成 C# 模型
- ~~绕过 API 直接操作文件~~ → 所有数据访问统一走 ApiService
- ~~忽略空值/异常处理~~ → 全面使用 nullable + try-catch + 用户友好提示

---

## 七、不做的事（边界明确）

以下内容**不在本次迁移范围内**：

| 不做的事 | 原因 |
|---------|------|
| **修改 `core/` 任意文件** | 业务逻辑稳定，仅需新增 api_server.py |
| **修改 `services/` 任意文件** | 同上 |
| **修改 `classlively_native/` C++ 代码** | 已完成的 7 个模块不变 |
| **修改 `data/` 数据文件** | 软件列表/URL 目录等不动 |
| **Rust 媒体模块** | 你明确说不动媒体代码 |
| **QML 方案** | 已确定用 C# WinUI 3 |
| **Web/WebView 方案** | 原生 WinUI 3 性能更好 |
| **数据库引入** | 当前 JSON 配置足够，不引入 SQLite 等 |
| **云同步功能** | 不在需求范围内 |

---

## 八、成功标准

### 8.1 功能对等

- [ ] 全部 7 个页面功能完整迁移（主页/壁纸/下载/设置/更新/关于/调试）
- [ ] 全部 7 个可拖拽组件正常工作（时钟/天气/一言/倒计时/学校/快捷启动/媒体）
- [ ] 全部 7 个组件设置对话框可用
- [ ] 启动向导（5 页）完整
- [ ] 启动画面正常
- [ ] 系统托盘（显示/隐藏/右键菜单/双击还原）
- [ ] 空闲检测 + 自动打开
- [ ] 单例互斥锁
- [ ] 深浅主题切换（3 种模式：浅色/深色/跟随系统）
- [ ] 国际化（简中/繁中/English/Auto）
- [ ] 高斯模糊背景（调用 C++ native）
- [ ] 设为桌面壁纸（调用 C++ native）
- [ ] 窗口管理（最大化/最小化到托盘/关闭行为/位置持久化）

### 8.2 性能指标

| 指标 | PyQt6 现状 | WinUI 3 目标 |
|------|-----------|-------------|
| 冷启动时间 | 2-3 秒 | < 1.5 秒 |
| 页面切换帧率 | 15-30 fps | 60 fps |
| QuickLaunchDock 动画 | 120 FPS (CPU 绘制) | 60 FPS (GPU 合成) |
| 内存占用 (空闲) | ~150 MB | < 120 MB |
| 窗口 resize 延迟 | 可感知卡顿 | < 16ms (一帧内) |

### 8.3 体验改进（超越原版）

| 改进点 | 说明 |
|--------|------|
| **原生 Fluent Design** | 微软官方实现，非第三方模拟 |
| **GPU 渲染** | 全局 DirectX 硬件加速 |
| **主题切换零闪烁** | RequestedTheme 即时生效 vs QSS 全量重载 |
| **async/await** | 下载/网络操作不再阻塞 UI |
| **更好的 IDE 支持** | Visual Studio IntelliSense + XAML 热重载 |
| **打包体积** | 单文件发布（无 Python 运行时捆绑） |

---

## 九、执行顺序总结

```
阶段 0 (3-5天)  ── 基础设施
  ├─ 0.1 C# WinUI 3 项目创建 + 编译验证 ✅ (已完成原型验证)
  ├─ 0.2 Python FastAPI 后台包装 (新增 api_server.py)
  └─ 0.3 C# API 客户端 (ApiService.cs)

阶段 1 (5-7天)  ── 主框架 + 简单页面
  ├─ 1.1 MainWindow 导航框架
  ├─ 1.2 SettingsPage (最简单，先验证全链路)
  ├─ 1.3 AboutPage
  └─ 1.4 UpdatePage

阶段 2 (7-10天) ── 核心页面
  ├─ 2.1 WallpaperPage (含模糊背景)
  ├─ 2.2 DownloadPage
  └─ 2.3 HomePage 基础版 (不含复杂组件)

阶段 3 (10-14天) ── 复杂组件
  ├─ 3.1 QuickLaunchDock
  ├─ 3.2 MediaWidget
  └─ 3.3 DraggableWidget 拖拽系统

阶段 4 (5-7天)  ── 系统集成
  ├─ 4.1 系统托盘
  ├─ 4.2 空闲检测 + 自动打开
  ├─ 4.3 单例互斥锁
  ├─ 4.4 C++ Native 对接
  └─ 4.5 启动画面 + 向导

阶段 5 (3-5天)  ── 打磨优化
  ├─ 动画/DPI/主题/多显示器测试
  └─ 性能基准对比

总计: 约 33-48 天 (按每天有效工作时间计)
AI 编码效率预估: 实际可能压缩至 2-3 周
```

---

## 十、决策点（需用户确认）

在开始编码前，需确认以下决策：

1. **通信协议确认**: FastAPI HTTP (默认) 还是命名管道（更低延迟但更复杂）？
2. **QuickLaunchDock 实现路线**: XAML 控件组合（快但可能动画受限）还是 Win2D 自绘（慢但完全控制）？
3. **是否保留 PyQt6 版本作为 fallback**: 双版本并行维护还是一次性替换？
4. **打包方式**: 单 exe (自包含部署) 还是安装包 (MSIX installer)？
5. **是否先做一个最小可运行原型**（只有导航栏 + 设置页 + 调通 API），验证方案可行性后再全面推进？

---

*文档结束。以上方案基于对项目全部 12 个 UI 文件的完整分析编写，遵循所有硬性约束。*
