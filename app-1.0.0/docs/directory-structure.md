# 目录结构

> [!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

本文档说明仓库及运行期目录组织，路径相对仓库根书写。

## 1. 仓库根目录

```
Glimpseon/
├── Glimpseon.py            # 顶层启动器（版本选择 + 子进程拉起）
├── README.md
├── LICENSE                 # GPL-3.0
├── requirements.txt
├── pymem-1.14.0-py3-none-any.whl
├── update.7z               # 更新包示例
├── .gitignore
└── app-1.0.0/              # 应用版本目录（启动器扫描 app-* 前缀）
```

## 2. 应用版本目录 `app-1.0.0/`

```
app-1.0.0/
├── GlimpseonMain.py        # 主程序入口（Splash + Wizard + MainWindow + Preloader）
├── record.json             # 版本记录（version / build_date / current / partial）
├── Glimpseon_native.pyd    # 预编译原生扩展（cp311-win_amd64）
│
├── core/                   # 核心业务层
├── ui/                     # 界面层（PyQt6 Fluent Widgets）
├── services/               # 数据服务层（天气/一言/新闻/历史/单词/英语/媒体）
├── resource/               # 静态资源
├── font/                   # 内嵌字体（HarmonyOS Sans）
├── locale/                 # 国际化语言包
├── glimpseon_native/       # C++ 原生扩展源码 + 构建
├── Tools/                  # 外部工具（7z.exe / aria2c.exe）
└── docs/                   # 本开发者文档
```

## 3. `core/` 核心模块

| 文件                                                                               | 职责                                                             |
| -------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| [paths.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/paths.py)               | 路径推导（PackageRoot/AppDir/MEIPASS/DATA\_\*）+ `get_resource_path` |
| [constants.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/constants.py)       | 全局常量、QSS 加载与缓存、资源路径别名                                          |
| [config.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/config.py)             | `QConfig` 子类 `Config`，全部配置项 + 自动保存                             |
| [logger.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/logger.py)             | `CustomLogger`、系统上下文、堆栈链、文件轮转、异常钩子                             |
| [utils.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/utils.py)               | 单实例、字体、缓存、自启、NTP、翻译、`FUI` 图标枚举                                 |
| [component.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/component.py)       | 组件定义、网格布局、页面管理、注册表                                             |
| [notification.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/notification.py) | 通知管理器 + 滚动/角落/全屏弹窗 + TTS                                       |
| [downloader.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/downloader.py)     | 多源下载、7z 解压、静默安装、优先级控制                                          |
| [linkage.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/linkage.py)           | ClassIsland / ClassWidgets 联动                                  |
| [timetable.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/timetable.py)       | 课表配置类与读写                                                       |
| [updater.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/updater.py)           | GitHub 更新检查 / 下载 / 部署                                          |
| [record.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/record.py)             | `record.json` 版本记录管理                                           |

## 4. `ui/` 界面模块

| 文件                                                                                                                                | 主要类                                                     |
| --------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| [home.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/home.py)                                                                    | `HomeInterface` 主界面（壁纸背景 + 时钟/天气/一言/倒计时/媒体/快捷启动/学校信息组件） |
| [wallpaper.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/wallpaper.py)                                                          | `WallpaperInterface` 壁纸管理（获取/保存/设桌面/历史/自动同步）            |
| [notification.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/notification.py)                                                    | `NotificationPage` 通知编辑/预览/队列/定时                        |
| [timetable.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/timetable.py)                                                          | `TimetablePage` 课程表编辑                                   |
| [download.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/download.py)                                                            | `DownloadInterface` 软件下载（HTML） |
| [settings.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/settings.py)                                                            | `SettingsWindow` 多分组设置                                  |
| [about.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/about.py)                                                                  | `AboutInterface` 关于/更新检查/鸣谢                             |
| [debug.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/debug.py)                                                                  | `DebugPanel` 调试面板                                       |
| [component.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/component.py)                                                          | 组件编辑窗口 / 组件库 / 选择框                                      |
| [common.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/common.py)                                                                | UI 公共工具（文本查看器等）                                         |
| __[init](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/__init__.py)__[.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/__init__.py) | 统一导出各界面                                                 |

## 5. `services/` 数据服务

| 文件                                                                         | 服务                                                  |
| -------------------------------------------------------------------------- | --------------------------------------------------- |
| [weather.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/weather.py) | 天气（含 `RegionDatabase` 城市库 + `RegionSelectorDialog`） |
| [poetry.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/poetry.py)   | 一言 / 诗句                                             |
| [news.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/news.py)       | 央视 / 百度 / 微博 / 头条 / 腾讯新闻                            |
| [history.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/history.py) | 历史上的今天                                             |
| [word.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/word.py)       | 每日单词                                               |
| [sentence.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/sentence.py) | 每日英语                                               |
| [media.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/media.py)     | 媒体信息（多播放器兼容、歌词、封面）                                  |

## 6. `resource/` 静态资源

```
resource/
├── fluent/{light,dark}/   # Fluent 图标 SVG（24/32 regular）
├── icons/
│   ├── default_icon/      # Directory.ico / exe.ico
│   ├── news/              # 新闻源 logo
│   ├── software_icon/     # 软件下载中心图标（.ico）
│   ├── weather/           # 天气图标（含 alerts/reminders）
│   ├── CY.png             # 应用图标
│   └── changelog.md       # 更新日志
├── qss/{light,dark}/      # 各界面 QSS（app/about/component/debug/
│                          #   home/notification/setting/
│                          #   setting_dialog/timetable/wallpaper）
├── city.db                # 城市经纬度库（weather 服务使用）
├── credits.json           # 第三方依赖鸣谢
├── software_list.py       # 软件下载分类清单（SOFTWARE_CATEGORIES）
└── url_dir.py             # 软件直链 URL 表
```

## 7. `locale/` 国际化

```
locale/
├── zh_CN.json   # 简体中文
├── zh_TW.json   # 繁体中文
└── en_US.json   # 英文
```

键值扁平化，通过 `tr("key")` 查找；支持插值如 `tr("dialog.instance_running", app=APP_NAME)`。

> [!IMPORTANT]
> zh\_TW\.json是繁体中文的意思，TW代表台湾省（中华人民共和国省级行政区），台湾是中华人民共和国的神圣领土的一部分。完成统一祖国的大业是包括台湾同胞在内的全中国人民的神圣职责。台湾自古以来就是中国领土不可分割的一部分

## 8. `glimpseon_native/` 原生扩展

```
glimpseon_native/
├── CMakeLists.txt          # pybind11 构建配置
├── src/
│   ├── wallpaper.cpp       # 设置/获取系统壁纸
│   ├── image.cpp           # Direct2D 高斯模糊
│   ├── image_blur.h
│   ├── hook.cpp/.h         # 低级键盘/鼠标钩子
│   ├── sys.cpp/.h          # 空闲检测/互斥锁/字体安装/图标提取
│   └── ...
├── build/                  # CMake 构建产物
└── classlively_native.pyd  # 旧名产物
```

详见 [原生扩展](native-extension.md)。

## 9. 运行期数据目录 `data/`

由 [core/paths.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/paths.py) 的 `ensure_data_dirs()` 创建，位于 `PACKAGE_ROOT/data/`。子目录见 [架构总览 - 数据目录](architecture.md#7-数据目录)。

## 10. 命名约定

- 版本目录：`app-{major}.{minor}.{patch}`，启动器按字典序 + `current` 标记选择。
- QSS 文件：与界面同名（如 `home.qss`、`wallpaper.qss`），分 `light`/`dark`。
- 图标：Fluent 命名 `ic_fluent_{name}_{size}_regular.svg`。
- 日志器：层级命名 `Glimpseon.core.{module}`，避免与主 `Glimpseon` logger 冲突。

