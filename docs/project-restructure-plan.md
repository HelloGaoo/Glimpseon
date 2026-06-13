# ClassLively 项目结构重构方案

## 核心思路

**C# 显示，Python 干活。**

Python 是核心引擎（配置、服务、业务逻辑全在 Python），C# 只负责 UI 展示和用户交互。两者通过 API 通信。

---

## 一、当前问题

### 1. 仓库卫生问题（最先解决）

| 文件 | 问题 |
|------|------|
| `classlively_native/build/` | CMake 构建中间产物提交到了仓库 |
| `classlively_native.pyd`（根目录） | 编译产物散落 |
| `pymem-1.14.0-py3-none-any.whl` | 本地 wheel 包 |
| `update.7z` | 临时文件 |
| `ClassLively.UI/crash_log.txt` | 运行时崩溃日志 |
| `ClassLively.UI/config/` | 运行时生成的配置文件 |

### 2. Python 侧 PyQt6 残留（重点清理）

**`core/` 目录**：

| 文件 | PyQt6 残留 |
|------|-----------|
| `core/config.py` | 用 `qfluentwidgets.QConfig` / `ConfigItem` / `OptionsConfigItem` / `RangeConfigItem` / `ColorConfigItem` / `BoolValidator` 管理配置 |
| `core/utils.py` | 包含 `TranslatableWidget`、`LanguageCode`、`get_translation_manager` 等 PyQt6 工具类（但这个文件还有很多其他功能，不能粗暴删除，要逐个清理） |
| `core/constants.py` | `load_qss()` 函数加载 PyQt6 样式 |

**`services/` 目录**：

| 文件 | PyQt6 残留 |
|------|-----------|
| `services/weather.py` | `RegionSelectorDialog` 是 PyQt6 对话框，混在服务层 |

**`core/api_server.py`**：

- AI 生成的，代码组织混乱
- 还在导入 `ui.wallpaper.WallpaperInterface`，依赖了 PyQt6 UI 层

### 3. C# 前端问题

#### 3.1 编译错误（必须修复）

| 文件 | 问题 |
|------|------|
| `Helpers/ThemeHelper.cs` | 第8行 `class ThemeHelper` 后缺少左花括号 `{`，直接写了 `enum ThemeMode` |
| `Pages/HomePage.xaml.cs` | 第706行 `catch` 块没有对应的 `try`，`LoadCountdownAsync()` 中 try-catch 结构断裂 |
| `Helpers/PythonProcessManager.cs` | 第94行 `Debug.LogWarning()` 不存在，应为 `Debug.WriteLine()` |

#### 3.2 组件文件分散

当前 C# 组件拆成了 3 个文件：`DraggableControl.cs`、`MediaWidget.cs`、`QuickLaunchDock.cs`。
旧 PyQt6 的 `component.py` 把 DraggableWidget + MediaWidget + QuickLaunchDock 都放一个文件里，更紧凑好找。应该把组件逻辑集中到页面里或最少的共享文件，避免再拆出多个小模块。

#### 3.3 ApiService 实例管理混乱

- `App.Api` 是全局共享实例，但多个页面仍 `new ApiService()`（HomePage、WallpaperPage、SettingsPage、DownloadPage、UpdatePage、DebugPage）
- 断路器状态不共享

#### 3.4 硬编码字符串

- 后端地址 `http://127.0.0.1:19856` 在 `App.xaml.cs`、`ApiService.cs`、`MediaMonitorService.cs`、`PythonProcessManager.cs` 中重复出现

#### 3.5 模型类散落在各处

| 类 | 当前位置 |
|---|---------|
| `HistoryItem`, `HistoryRecord` | `WallpaperPage.xaml.cs` 末尾 |
| `SoftwareCategoryGroup` | `DownloadPage.xaml.cs` 中 |
| `BlurredPathResponse` | `ApiService.cs` 末尾 |
| `QuickLaunchItem` | `QuickLaunchDock.cs` 中 |

这些都应该归到 `Models/Models.cs` 里，所有模型放一个文件。

#### 3.6 重复的工具方法

- `GetComponentDisplayName()` 在 `HomePage.xaml.cs` 和 `DraggableControl.cs` 中重复
- `ColorHelper` 在 `DraggableControl.cs` 中定义，`ComponentSettingDialog.cs` 通过别名引用
- `SelectComboBoxByTag/Content` 在 `WallpaperPage.xaml.cs` 和 `SettingsPage.xaml.cs` 中重复

#### 3.7 Helpers 太散

- `ThemeHelper.cs` 只有 45 行 3 个方法，而且可能没被调用
- `NativeBindings.cs`（91行）和 `TrayIconHelper.cs`（208行）都是 Win32 P/Invoke，分两个文件没必要
- `ColorHelper` 定义在 `DraggableControl.cs` 底部，但整个项目都在用

#### 3.8 配置读写混乱

- `AppSettings` 读本地 JSON，`ApiService` 读写后端配置
- HomePage 和 WallpaperPage 各自手写"先 API 后 fallback 本地"的 try-catch，至少重复 5 处
- 配置键名不统一：`show_clock_seconds`（下划线）vs `autoStart`（驼峰）

### 4. 可以立即删除的

- `resource/qss/` — PyQt6 样式，C# 不用

### 5. 暂时保留的（等 C# 迁移成熟后清理）

- `ClassLively.py` — 不只是入口，还有很多功能逻辑，C# 迁移完成前作为参照
- `old.ui/` 目录（原 `ui/`）— C# 迁移完成前作为参照

---

## 二、要做什么

### 第一步：仓库卫生（最先做）

`.gitignore` 添加：
```gitignore
# Native 构建
native/build/
*.pyd
*.pyc

# 运行时产物
crash_log.txt
update.7z
config/component_positions.json
config/window_state.json

# 本地 wheel
*.whl
```

删除已提交的构建产物：
- `native/build/`（原 `classlively_native/build/`）
- 根目录 `classlively_native.pyd`
- `pymem-1.14.0-py3-none-any.whl`
- `update.7z`
- `ClassLively.UI/crash_log.txt`

### 第二步：目录和文件重命名

- `ClassLively.UI/` → `ui/`（C# WinUI3 前端，直接叫 `ui/` 更直观）
- `ui/`（旧 PyQt6）→ `old.ui/`（标注为旧代码，C# 迁移参照用）
- `classlively_native/` → `native/`（简洁明了）
- `core/api_server.py` → `core/api.py`（简洁明了）

注意：重命名后需要同步修改：
- `.csproj` / `.sln` 文件中的路径引用
- `PythonProcessManager.cs` 中 `GetDefaultWorkingDirectory()` 的工作目录查找逻辑
- C# 命名空间可保持 `ClassLively_UI` 不变，只改目录名
- Python 中所有 `from core.api_server` 导入改为 `from core.api`
- `classlively_native` 的 Python 导入改为 `native`

### 第三步：删除 `resource/qss/`

PyQt6 样式文件，C# 不用，可以直接删。

### 第四步：修复 C# 编译错误

- `Helpers/ThemeHelper.cs`：补全缺失的左花括号
- `Pages/HomePage.xaml.cs`：修复 `LoadCountdownAsync()` 的 try-catch 结构
- `Helpers/PythonProcessManager.cs`：`Debug.LogWarning()` → `Debug.WriteLine()`

### 第五步：C# 组件文件整理

以页面为中心，尽量减少单独组件模块。组件逻辑优先放到各页面的 `*.xaml.cs` 里，只有跨页面复用的支持逻辑才提取为单个共享文件。

- 不再把每个 UI 组件拆成多个小文件，避免过度分散
- 仅在必要时保留一个 `Dialogs/ComponentSettingDialog.cs`，保持页面与弹窗逻辑集中
- 农历算法、颜色工具和少量通用方法可以合并到现有 `Helpers/` 文件而非单独新建模块

### 第六步：C# 合并散落的 Helpers

- `NativeBindings.cs`（91行）+ `TrayIconHelper.cs`（208行）+ `ThemeHelper.cs`（45行）→ 合并为 `Helpers/NativeHelper.cs`
- `AppSettings.cs` 作为统一配置入口，承担本地 JSON 读写和 API fallback
- `PythonProcessManager.cs` 保留后端进程管理，其他通用小工具尽量不再拆成额外文件

### 第七步：C# ApiService 实例统一

所有页面统一使用 `App.Api` 全局实例，不再 `new ApiService()`。

### 第八步：C# 提取硬编码常量

在 `Helpers/NativeHelper.cs` 或 `App.xaml.cs` 里加一个常量：
```csharp
public const string ApiBaseUrl = "http://127.0.0.1:19856";
```
所有引用 `http://127.0.0.1:19856` 的地方改为这个常量。不用单独建文件。

### 第九步：C# 模型类归位到 `Models/Models.cs`

把散落在各处的模型类都移到 `Models/Models.cs` 里，所有模型放一个文件：
- `WallpaperPage.xaml.cs` 末尾的 `HistoryItem`、`HistoryRecord`
- `DownloadPage.xaml.cs` 中的 `SoftwareCategoryGroup`
- `ApiService.cs` 末尾的 `BlurredPathResponse`
- `QuickLaunchDock.cs` 中的 `QuickLaunchItem`

### 第十步：C# 统一配置读写

在 `Helpers/AppSettings.cs` 里增加"API 优先 + 本地 fallback"的统一方法：
```csharp
public static async Task<T> GetAsync<T>(string key, T defaultValue, IApiService? api = null)
public static async Task SetAsync<T>(string key, T value, IApiService? api = null)
```
消除各页面重复的 try-catch fallback 逻辑。

### 第十一步：清理 Python PyQt6 依赖

**`core/config.py`**：
- 用 `dataclass` + JSON 替换 `qfluentwidgets.QConfig`
- 用回调列表替代 `valueChanged` 信号
- 去掉 `Theme`、`QLocale` 等 PyQt6 类型依赖
- 保留所有配置项定义，只换底层实现

**`core/utils.py`**：
- 移除 `TranslatableWidget`、`LanguageCode`、`get_translation_manager`
- 保留其他所有功能

**`core/constants.py`**：
- 移除 `load_qss()`

**`services/weather.py`**：
- 移除 `RegionSelectorDialog` 和 `qfluentwidgets` 导入
- 保留 `WeatherService` 和 `RegionDatabase`

**`core/api.py`**：
- 不再导入 `ui.wallpaper`，壁纸逻辑改为调用 `services/`
- 整理代码结构

### 第十二步：精简 `requirements.txt`

移除 PyQt6 相关：
- `PyQt6`, `PyQt6-Qt6`, `PyQt6_sip`, `PyQt6-Fluent-Widgets`, `PyQt6-Frameless-Window`

移除未使用：
- `easyocr`, `pytesseract`, `opencv-python-headless`
- `torch`, `torchvision`
- `scikit-image`, `scipy`, `shapely`
- `pycaw`
- `pyinstaller`

保留：
- `fastapi`, `uvicorn` — API 服务
- `requests` — HTTP 客户端
- `psutil`, `pymem` — 媒体检测
- `winsdk` — GSMTC
- `pillow` — 图片处理
- `py7zr` — 更新解压
- `cnlunar` — 农历
- `pywin32`, `uiautomation` — 系统交互

### 等后续 C# 迁移成熟后

- 删除 `ClassLively.py`
- 删除 `old.ui/` 目录

---

## 三、重构后的结构

```
ClassLively/
├── ui/                             ← C# WinUI3 前端（原 ClassLively.UI/）
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── Assets/
│   ├── Pages/
│   │   ├── HomePage.xaml.cs         ← 对应旧 home.py：主页 + 编辑面板 + 编辑弹窗
│   │   ├── WallpaperPage.xaml.cs    ← 对应旧 wallpaper.py
│   │   ├── DownloadPage.xaml.cs     ← 对应旧 download.py
│   │   ├── SettingsPage.xaml.cs     ← 对应旧 settings.py
│   │   ├── UpdatePage.xaml.cs       ← 对应旧 update.py
│   │   ├── AboutPage.xaml.cs        ← 对应旧 about.py
│   │   └── DebugPage.xaml.cs        ← 对应旧 debug.py
│   ├── Services/
│   │   ├── IApiService.cs
│   │   ├── ApiService.cs
│   │   └── MediaMonitorService.cs
│   ├── Helpers/
│   │   ├── AppSettings.cs          ← 统一配置读写、API fallback
│   │   ├── NativeHelper.cs         ← Win32/主题/API 常量
│   │   └── PythonProcessManager.cs ← 后端进程管理
│   ├── Models/
│   │   └── Models.cs               ← 归位：所有模型类放这里
│   └── Dialogs/
│       └── ComponentSettingDialog.cs  ← 可选的组件设置弹窗，仅一个单文件模块
│
├── native/                          ← C++ Native 扩展（原 classlively_native/）
│   ├── src/
│   └── CMakeLists.txt
│
├── core/                            ← Python 核心
│   ├── api.py                       ← 原 api_server.py，整理清理
│   ├── config.py                    ← 重写：纯 Python 配置管理
│   ├── constants.py                 ← 清理：移除 load_qss()
│   ├── downloader.py
│   ├── logger.py
│   ├── updater.py
│   └── utils.py                     ← 清理：移除 PyQt6 工具类
│
├── services/                        ← Python 核心（干活模块）
│   ├── media.py
│   ├── weather.py                   ← 清理：移除 PyQt6 对话框
│   └── poetry.py
│
├── data/
├── resource/
│   └── icons/                       ← qss/ 已删除
├── font/
├── locale/
├── Tools/
├── docs/
│
├── ClassLively.py                   ← 暂保留（C# 迁移参照）
├── old.ui/                          ← 暂保留（原 ui/，C# 迁移参照）
├── version.py
└── requirements.txt
```

### C# 前端结构解释

```
ui/
│
├── App / MainWindow          ← 入口层：应用启动 + 主窗口壳
│
├── Pages/                    ← 视图层：7个页面，每个页面一个文件
│   ├── HomePage              ← 对应旧 home.py
│   ├── WallpaperPage         ← 对应旧 wallpaper.py
│   └── ...
│
├── Dialogs/                  ← 弹窗层：最少的单文件支持
│   └── ComponentSettingDialog ← 组件设置弹窗（对应旧 component_settings.py）
│
├── Windows/                  ← 窗口层：独立窗口（非页面内嵌）
│   ├── SetupWizard           ← 首次启动向导
│   └── SplashScreen          ← 启动画面
│
├── Services/                 ← 服务层：跟后端通信
│   ├── IApiService           ← 接口（方便测试/mock）
│   ├── ApiService            ← 实现：HTTP + 断路器 + 重试
│   └── MediaMonitorService   ← WebSocket 媒体监听
│
├── Helpers/                  ← 工具层：仅保留最少支持文件
│   ├── AppSettings           ← 配置读写（JSON + API fallback）
│   ├── NativeHelper          ← Win32/主题/颜色/通用工具
│   └── PythonProcessManager  ← Python 后端进程管理
│
├── Models/                   ← 数据层：纯数据结构，无逻辑
│   └── Models.cs             ← 所有 DTO/Model 放一个文件
│
└── Assets/                   ← 资源层：图片等静态文件
```

**依赖方向**：

```
Pages → Models
  ↓
Services → Helpers
  ↓
  API (Python 后端)
```

- Pages 直接管理 UI，用 Services 获取数据
- Services 只管跟 Python API 通信，不管 UI 怎么展示
- Helpers 是最底层的纯工具，谁都能调用
- Models 是纯数据结构，谁都能引用

**跟旧 PyQt6 的区别**：旧代码里 `home.py` 既管 UI 又直接调 `requests` 获取天气、直接读注册表，现在这些"干活"的事全交给 Python API，C# 的 Pages 只通过 Services 拿数据然后显示。

---

## 三点五、每个文件的职责说明

### C# 前端 `ui/`

| 文件 | 对应旧 PyQt6 | 职责 |
|------|-------------|------|
| `App.xaml` | — | WinUI3 应用资源字典，定义全局样式 |
| `App.xaml.cs` | — | 应用入口：单实例检查、启动向导、启动画面、Python 后端进程管理、全局 ApiService 实例、健康监控、优雅关闭 |
| `MainWindow.xaml` | — | 主窗口布局：Mica 背景 + TitleBar + NavigationView 导航菜单 + 内容 Frame |
| `MainWindow.xaml.cs` | — | 主窗口逻辑：NavigationView 页面导航、窗口状态保存/恢复、系统托盘、空闲检测、关闭行为 |
| `Assets/` | — | 静态图片资源：应用图标、启动画面、商店 Logo 等 |
| `Dialogs/ComponentSettingDialog.cs` | `component_settings.py` | 组件设置弹窗：7种组件（时钟/天气/一言/倒计时/学校信息/媒体/快捷启动）的设置面板 |
| `Helpers/AppSettings.cs` | — | 本地配置读写：JSON 文件读写、内存缓存、线程安全。增强后增加"API 优先 + 本地 fallback"统一方法 |
| `Helpers/NativeHelper.cs` | — | Win32 原生工具（合并自 NativeBindings + TrayIconHelper + ThemeHelper）：P/Invoke 绑定、系统托盘、主题切换、API 地址常量 + 少量通用工具 |
| `Helpers/PythonProcessManager.cs` | — | Python 后端进程生命周期管理：启动/停止/重启/健康检查/端口检测/自动重启 |
| `Models/Models.cs` | — | 所有数据模型：ApiResponse\<T\>、ConfigItemModel、WallpaperInfoModel、IdleInfoModel、WeatherInfoModel、PoetryModel、SoftwareItemModel、MediaInfoModel、HistoryItem、HistoryRecord、SoftwareCategoryGroup、BlurredPathResponse、QuickLaunchItem |
| `Pages/HomePage.xaml.cs` | `home.py` | 主页：7个组件 UI 构建+数据加载+定时更新、EditPanel 侧边编辑面板、CountdownEditDialog/QuickLaunchEditDialog/AppEditDialog 弹窗、吸附对齐线、编辑模式、组件位置持久化、模糊背景 |
| `Pages/WallpaperPage.xaml.cs` | `wallpaper.py` | 壁纸管理：获取/另存为/手动选择/设为桌面、模糊/亮度调节、历史记录、自动获取定时器 |
| `Pages/DownloadPage.xaml.cs` | `download.py` | 软件下载：单个/批量模式、下载源切换、分类分组显示、批量下载进度 |
| `Pages/SettingsPage.xaml.cs` | `settings.py` | 设置：自启动/空闲/关闭行为、主题/主题色、日志级别/禁用/条数/天数、清缓存/重置/打开配置 |
| `Pages/UpdatePage.xaml.cs` | `update.py` | 更新：版本对比、GitHub 远程检查、更新状态指示灯、自动检查/自动更新开关 |
| `Pages/AboutPage.xaml.cs` | `about.py` | 关于：版本信息、远程更新日志、GitHub/B站链接、GPL-3.0 许可证弹窗、鸣谢列表 |
| `Pages/DebugPage.xaml.cs` | `debug.py` | 调试：系统信息、网络诊断、API 端点测试、日志查看器、GC 回收 |
| `Services/IApiService.cs` | — | API 接口定义：配置/壁纸/天气/一言/系统/媒体/下载/健康检查/断路器 |
| `Services/ApiService.cs` | — | API 实现：HttpClient 通信、自动重试、断路器模式、健康状态事件 |
| `Services/MediaMonitorService.cs` | — | 媒体播放监听：WebSocket 实时推送 + HTTP 轮询降级 |
| `Windows/SetupWizard.xaml.cs` | — | 首次启动设置向导：5页（欢迎/协议/基本设置/外观/学校信息） |
| `Windows/SplashScreen.xaml.cs` | — | 启动画面：进度条+状态文字 |

### C++ Native `native/`

| 文件 | 职责 |
|------|------|
| `src/hook.cpp` | 全局键盘钩子（快捷键拦截） |
| `src/image.cpp` | 图片高斯模糊（用于壁纸模糊效果） |
| `src/sys.cpp` | 系统空闲检测、互斥锁（单实例） |
| `src/wallpaper.cpp` | 设置桌面壁纸（调用 Windows API） |
| `CMakeLists.txt` | CMake 构建配置，编译为 `classlively_native.pyd` |

### Python 核心 `core/`

| 文件 | 职责 |
|------|------|
| `api.py` | FastAPI HTTP 服务（端口 19856）：所有 API 路由端点、WebSocket 连接管理、媒体推送线程。C# 前端通过这个文件与 Python 通信 |
| `config.py` | 配置持久化管理：用 dataclass + JSON 替代 PyQt6 QConfig，所有配置项定义、读写、变更回调。C# 通过 API 读写，Python 负责持久化 |
| `constants.py` | 全局常量：APP_NAME、BASE_DIR、API 端口等 |
| `downloader.py` | 文件下载器：带进度回调的下载工具 |
| `logger.py` | 日志系统：日志级别、格式、文件输出 |
| `updater.py` | 自动更新：检查新版本、下载更新包、解压替换 |
| `utils.py` | 工具集：单实例锁、字体初始化、文件提取、自启动管理、缓存读写 |

### Python 服务 `services/`

| 文件 | 职责 |
|------|------|
| `media.py` | 媒体检测：网易云、QQ 音乐、酷狗、GSMTC 四个 Reader + 调度器 + LRC 歌词解析。检测当前播放的歌曲、歌手、封面、歌词、进度 |
| `weather.py` | 天气服务：天气数据获取、城市数据库查询。城市选择由 C# 前端实现 |
| `poetry.py` | 一言服务：获取随机诗词/一言内容 |

### 数据 `data/`

| 文件 | 职责 |
|------|------|
| `software_list.py` | 软件下载列表定义：软件名、下载链接、分类、图标 |
| `url_dir.py` | URL 目录：各 API 端点地址 |
| `default_icon/` | 默认软件图标 |
| `software_icon/` | 各软件的图标 |
| `music_photo/` | 音乐相关图片资源 |
| `xiaomi_weather.db` | 小米天气城市数据库（SQLite） |

### 资源 `resource/`

| 文件 | 职责 |
|------|------|
| `icons/` | 天气图标等静态图标资源 |

### 其他

| 文件 | 职责 |
|------|------|
| `font/` | HarmonyOS Sans 字体文件 |
| `locale/` | 国际化翻译文件（zh_CN.json、zh_TW.json、en_US.json） |
| `Tools/` | 外部工具：7z.exe、aria2c.exe |
| `docs/` | 项目文档 |
| `ClassLively.py` | 旧 PyQt6 入口（暂保留，C# 迁移参照） |
| `old.ui/` | 旧 PyQt6 UI 层（暂保留，C# 迁移参照） |
| `version.py` | 版本号定义，前后端共享 |
| `requirements.txt` | Python 依赖包列表 |

---

## 四、架构

```
┌──────────────────────────────────┐
│       C# WinUI3 (显示)           │
│                                  │
│  Pages/（对应旧 PyQt6 各页面）   │
│  ├── HomePage     ← home.py     │
│  ├── WallpaperPage ← wallpaper.py│
│  ├── DownloadPage ← download.py  │
│  ├── SettingsPage ← settings.py  │
│  ├── UpdatePage   ← update.py    │
│  ├── AboutPage    ← about.py     │
│  └── DebugPage    ← debug.py     │
│                                  │
│  Dialogs/                        │
│  └── ComponentSettingDialog      │
│      ← component_settings.py     │
│                                  │
│  Helpers/                        │
│  ├── AppSettings   配置读写      │
│  ├── NativeHelper  Win32+主题    │
│  └── PythonProcessManager        │
│                                  │
│  Services/                       │
│  ├── ApiService    API 通信      │
│  └── MediaMonitorService 媒体监听│
└──────────┬───────────────────────┘
           │ HTTP REST + WebSocket
           │ :19856
┌──────────▼───────────────────────┐
│      Python FastAPI (干活)        │
│                                  │
│  core/                           │
│  ├── api.py        API 服务      │
│  ├── config.py     配置持久化    │
│  ├── utils.py      工具集        │
│  ├── constants.py  常量          │
│  ├── logger.py     日志          │
│  ├── downloader.py 下载          │
│  └── updater.py    更新          │
│                                  │
│  services/                       │
│  ├── media.py      媒体检测      │
│  ├── weather.py    天气服务      │
│  └── poetry.py     一言服务      │
│                                  │
│  data/                           │
│  ├── software_list.py            │
│  └── url_dir.py                  │
└──────────────────────────────────┘
```

**Python 零 PyQt6 依赖，纯后端服务；C# 纯 UI 显示。**
**Python 管配置持久化，C# 通过 API 读写。**
**C# 文件组织对应旧 PyQt6 UI 结构，方便迁移参照。**
