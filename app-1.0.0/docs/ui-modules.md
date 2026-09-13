# UI 模块（ui/）

> \[!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

`ui/` 基于 P6FW/HTML 用户可见范围即是这层


***

## 1. common.py — 公共基类

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/common.py)

- `BaseScrollAreaInterface(ScrollArea)`：滚动界面基类，统一滚动条与边距。
- `show_text_file(title, ...)`：以对话框形式展示文本文件（协议 / 许可证）。
- `create_html_view(parent=None, mouse_transparent=True)`：创建透明背景的 HTML 渲染视图（QWebEngineView 封装，依赖 PyQt6-WebEngine，入口处需提前导入并设置 `AA_ShareOpenGLContexts`）。

***

## 2. home.py — 主界面

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/home.py) · QSS：`home.qss`

### 2.1 HomeInterface

`HomeInterface(QWidget, TranslatableWidget)` 是应用主画布，承载壁纸背景与所有桌面组件。

**核心职责**：

- 渲染壁纸背景（含模糊 / 亮度效果）。
- 管理组件网格布局（编辑模式拖拽 / 缩放）。
- 聚合时钟、天气、一言、倒计时、媒体、快捷启动、学校信息等组件。
- 提供页面指示器（多页切换）与编辑模式遮罩。

**关键信号**：

- `weather_updated(dict)` — 天气数据更新（由 Preloader 触发）
- `poetry_updated(str)` — 一言更新
- `wallpaperChanged` — 壁纸变更（间接触发）

**关键属性**：`_cached_weather`、`_cached_poetry`、`current_weather_code`、`isEditMode`。

### 2.2 辅助控件

| 类                     | 作用          |
| --------------------- | ----------- |
| `GuideLineOverlay`    | 编辑模式参考线覆盖层  |
| `PageIndicator`       | 多页小圆点指示器    |
| `_GridOverlay`        | 网格背景显示      |
| `CountdownEditDialog` | 倒计时编辑对话框    |
| `AppEditDialog`       | 快捷启动应用编辑对话框 |

***

## 3. component.py — 组件实现库

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/component.py) · QSS：`component.qss` / `home.qss`

### 3.1 核心基类与管理

| 类                                       | 作用                                                                             |
| --------------------------------------- | ------------------------------------------------------------------------------ |
| `DraggableWidget(QWidget)`              | 可拖拽组件基类（移动、缩放手柄、选中框、编辑/删除按钮）                                                   |
| `DraggableContainer(DraggableWidget)`   | dpi缩放：`_dpi` / `_base_size` / `_scaled_px` / `_scale_layouts`），所有具体组件的父类 |
| `ComponentManager`                      | 组件实例生命周期 / 布局 / 持久化管理                                                          |
| `ComponentConfigDialog(MessageBoxBase)` | 组件配置弹窗（独立配置，parent 到 MainWindow）                                               |
| `ComponentCard(CardWidget)`             | 组件库中的卡片项                                                                       |
| `CategoryPage(ScrollArea)`              | 组件库分类页                                                                         |
| `ComponentLibraryWindow(FluentWindow)`  | 组件库窗口，加载 `component.qss`                                           |

### 3.2 编辑模式约定

- 编辑/删除按钮、选中框、缩放柄的尺寸与配色见 `DraggableWidget` 。
- 按钮使用全局 `componentCardOpacity` / `componentCardRadius`。
- 组件移动事件必须触发按钮重新定位。
- 编辑模式显示 `_GridOverlay` 网格 + `GuideLineOverlay` 参考线。
- 缩放：详见 [component-system.md 5.2](component-system.md#52-拖拽与缩放)）。

### 3.3 内置组件清单

| 分类       | 组件类                                                     | 说明                                                             |
| -------- | ------------------------------------------------------- | -------------------------------------------------------------- |
| Clock    | `DigitalClockComponent`                                 | 数字时钟（秒/农历）                                                     |
| Clock    | `SquareClock1Component`                                 | 方形钟表I（SVG）                                                     |
| Clock    | `SquareClock2Component`                                 | 方形钟表II（SVG）                                                    |
| Clock    | `CalendarMonthComponent`                                | 月历（`_DayCell`）                                                 |
| Clock    | `MiniCalendarComponent`                                 | 简约月历（HTML）                                                     |
| Clock    | `CountdownEventComponent`                               | 事件倒计时                                                          |
| Clock    | `TimerCountdownComponent`                               | 计时器（`TimeColumnWidget` / `TimerTimeDisplayWidget`）             |
| Weather  | `WeatherIconTempComponent`                              | 图标 + 温度                                                        |
| Weather  | `WeatherHourlyComponent`                                | 逐小时预报                                                          |
| Weather  | `WeatherWeeklyComponent`                                | 每周预报                                                           |
| Info     | `PoetryOneLineComponent`                                | 一言                                                             |
| Info     | `NewsBaidu/Weibo/Jinritoutiao/Tenxunwang/CCTVComponent` | 新闻（继承 `NewsComponent`）                                         |
| Info     | `HistoryTodayComponent`                                 | 历史上的今天                                                         |
| Info     | `DailyWordComponent`                                    | 每日单词                                                           |
| Info     | `DailySentenceComponent`                                | 每日英语                                                           |
| School   | `SchoolInfoComponent`                                   | 学校班级信息                                                         |
| School   | `TimetablePreviewComponent`                             | 课表预览（`_TimetableRow`）                                          |
| School   | `TimetableNowLessonComponent`                           | 当前课程                                                           |
| School   | `TimetableTimelineComponent`                            | 课程时间轴（HTML）                                                    |
| School   | `ClassAlbumHorizontal/VerticalComponent`                | 班级相册（继承 `ClassAlbumBaseComponent`）                             |
| School   | `HomeworkBoardComponent`                                | 作业板（HTML）                        |
| Media    | `MediaPlayerComponent`                                  | 媒体播放信息                                                         |
| Launcher | `QuickLaunchDockComponent` / `QuickLaunchDock`          | 快捷启动栏                                                          |
| Launcher | `QuickLaunchGridComponent`                              | 快捷启动II                                                         |
| Tools    | `CalculatorComponent`                                   | 计算器                                                            |
| Tools    | `WritingPadComponent`                                   | 手写画板（`_WritingOverlay` / `_PenSettingsPopup` / `_OverToolBtn`） |
| Tools    | `StickyNoteComponent`                                   | 便签                                                             |
| —        | `NavigationPage`                                        | 导航页（`NavItemCell`）                                             |

### 3.4 媒体组件

单一 `MediaPlayerComponent`（`DraggableContainer` 子类）：标题/艺术家/封面/进度/歌词/播放控制一体。双定时器、抓取、切歌竞态保护等实现见 [component-system.md 7](component-system.md#7-媒体组件)。

### 3.5 手写画板（擦除）

`WritingPadComponent` 基于 `_WritingOverlay`（见 [component-system.md 6](component-system.md#6-手写画板writingpadcomponent)。

### 3.6 HTML 渲染组件

html组件通过 `create_html_view()`（[ui/common.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/common.py)）借用 QWebEngineView 渲染 html/svg/js，类内不直接引用 QtWebEngine：

| 组件                           | 渲染方式            | 说明     |
| ---------------------------- | --------------- | ------ |
| `DailySentenceComponent`     | HTML + CSS      | 每日英语   |
| `DailyWordComponent`         | HTML + CSS      | 每日单词   |
| `SquareClock1Component`      | SVG             | 方形钟表I  |
| `SquareClock2Component`      | SVG             | 方形钟表II |
| `MiniCalendarComponent`      | HTML + CSS      | 简约月历   |
| `TimetableTimelineComponent` | HTML + CSS + JS | 课程时间轴  |
| `HomeworkBoardComponent`     | HTML + CSS + JS + QWebChannel | 作业板 |

**约定**：

- 视图由 `create_html_view()` 创建；QtWebEngineWidgets 必须在 QApplication 创建前于入口导入。
- 字体 `FONT_FAMILY`（`core/constants.py`），引号 / 水印字母 / 等宽日期等保留衬线 / 等宽字体。
- 方形钟表I/II走时由页面内 `requestAnimationFrame` 循环驱动。
- 方形钟表II：已走过的秒刻度渐回浅色。
- 整页 HTML另见 [11.4](#114-整页-html-界面qwebchannel)。

### 3.7 公用图标提取

快捷启动相关组件需要显示添加的软件的图标，于 [ui/component.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/component.py) 中创建公用 `extract_app_icon` 函数，可直接导入使用。

<br />

***

## 4. wallpaper.py — 壁纸管理

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/wallpaper.py) · QSS：`wallpaper.qss`

### 4.1 主要类

| 类                                                    | 作用                                      |
| ---------------------------------------------------- | --------------------------------------- |
| `WallpaperInterface(ScrollArea, TranslatableWidget)` | 壁纸主界面                                   |
| `WallpaperRecord`                                    | 单条壁纸记录（路径/来源/URL/时间）                    |
| `WallpaperHistory`                                   | 历史记录管理（持久化 + 数量限制 `wallpaperSaveLimit`） |
| `WallpaperInfoCard(CardWidget)`                      | 当前壁纸信息卡                                 |
| `WallpaperPreviewDialog(MessageBoxBase)`             | 壁纸预览                                    |
| `WallpaperThumbnailCard(CardWidget)`                 | 缩略图卡片                                   |
| `WallpaperHistoryWidget(QWidget)`                    | 历史缩略图列表                                 |
| `_ShrinkableWidget(QWidget)`                         | 可收缩容器                                   |

### 4.2 关键能力

- 多 API 源获取壁纸（`_getApiUrl` 按 `cfg.wallpaperApi`）。
- 模糊 / 亮度效果（`_applyEffects`，调用 `Glimpseon_native`）。
- 设为桌面壁纸（`Glimpseon_native` wallpaper 接口）。
- 自动同步到桌面（`autoSyncToDesktop`）。
- 历史记录与数量管理（`_manageWallpaperLimit`）。
- `wallpaperChanged` 信号通知主界面更新背景。

***

## 5. notification.py — 通知管理

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/notification.py) · QSS：`notification.qss`

| 类                                                  | 作用              |
| -------------------------------------------------- | --------------- |
| `NotificationPage(ScrollArea, TranslatableWidget)` | 通知编辑/预览/队列/定时发送 |
| `_PreviewWidget(QWidget)`                          | 通知预览            |
| `_ConfigEditDialog(Dialog)`                        | 通知配置编辑          |

**信号**：`send_notification` → 连接到 `NotificationManager.handle_notification`；`notification_finished` 回调 `_on_notification_shown`。

***

## 6. timetable.py — 课程表

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/timetable.py) · QSS：`timetable.qss`

`TimetablePage(ScrollArea, TranslatableWidget)` 承载课表编辑与课表来源切换：

- **档案管理**：多课表档案（`TimetableProfile`），新建 / 删除 / 重命名 / 导入 / 导出 / 打开目录，存于 `data/profile/`。
- **时间安排**：表格编辑时段（上课 / 下课），起止时间经时间选择器双向同步，默认单节时长可配。
- **课程编辑**：按时段填课程（科目按钮 / 单元格编辑）；删除时段后课程索引自动重排。
- **课表来源**：`cfg.profileSource` 切换 `Glimpseon / classisland / classwidgets`；外部源模式下编辑禁用，改为展示联动数据表（`_refreshLinkageTables`），找到进程往上找data/（`_onAutoDetect`）。
- **对外接口**：`get_today_schedule()` / `get_schedule_by_weekday(weekday)` 供课表预览、当前课程、时间轴组件取课。

***

## 7. download.py — 软件下载

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/download.py)

`DownloadInterface(QWidget)` 整页内嵌html，两者经 `QWebChannel` 通信。（用pfw做实在是太卡了）

### 7.1 结构

| 组成 | 说明 |
| --- | --- |
| `DownloadInterface(QWidget)` | 外壳 `QWebEngineView` |
| `_HTML_TEMPLATE`（`string.Template`） | 整页模板|
| `DownloadBridge(QObject)` | `QWebChannel` 桥，注册名 `bridge`，方法 `@pyqtSlot` 声明（见 7.4） |
| `get_cached_icon_data()` | 图标转 base64 data URI 内嵌（`file://` 取不到） |

### 7.2 数据与渲染

- 数据源：`SOFTWARE_CATEGORIES`（`resource/software_list.py`）、图标 `get_software_icon_path()`、下载链接 `resource/url_dir.py` 的 `url_dir`。
- 对外api：`addSection()` / `addSoftware()` / `_onDataPopulated()`。
- `_onDataPopulated()` → `_requestRender()`：可见即渲染，隐藏则挂起到首次 `showEvent`。
- 调色板由 `isDarkTheme()` 选；主题色取 `QColor(cfg.themeColor.value).name()`（取值可能是 `str` 或 `QColor`）。
- `cfg.themeChanged` / `cfg.themeColor.valueChanged` 触发重渲染。

### 7.3 页面内交互

所有元素都是html，样式在 `_HTML_TEMPLATE` 里。复选状态只存 `CARDS[name].checked`。

### 7.4 下载流程

单个：`bridge.download(name)` → 确认框 → 工作线程 `_findCacheFile()` / `_get_url()`（按 `cfg.downloadSource` 拼前缀）→ `downloader._install_<名称>()`（契约见 [core-modules.md 8](core-modules.md)）→ 进度回调。

批量：`bridge.startBatch(names_json)` → `ThreadPoolExecutor` → `_sigBatchDone` 清空勾选。

| 方向 | 成员 |
| --- | --- |
| JS → Python | `download` / `openLink` / `setMode` / `setSource` / `startBatch` / `confirmResult` |
| Python → JS | `window.glimpseon.uiStart` / `uiProgress` / `uiError` / `uiBatchDone` / `toast` / `confirm`、`window.relayout()` |

### 7.5 线程

工作线程没有事件循环，`QTimer.singleShot()` 在那儿不触发，`runJavaScript()` 只有主线程能调。UI 更新统一用 `_sigProgress(str,int)` / `_sigError(str,str)` / `_sigComplete(str)` / `_sigBatchDone()`，在 `__init__` 里 connect 到主线程槽。

### 7.6 新增可下载软件

1. `resource/software_list.py` 的 `SOFTWARE_CATEGORIES` 加软件条目：分类项为 `{"name_key"（i18n 键）, "software": [...]}`，软件项含 `name` / `description` / `icon` / `link`。
2. `resource/url_dir.py` 的 `url_dir` 加下载记录：`{"filename", "url"}`（直链）或 `{"filename", "github_path"}`（GitHub Releases，经镜像源拼前缀），可带 `hash` 校验。
3. `core/downloader.py` 实现 `_install_<软件名>()` 安装方法（契约见 [core-modules.md 8.2](core-modules.md#82-安装方法约定)）；图标 `.ico` 放 `resource/icons/software_icon/`，文件名与条目 `icon` 一致。

> \[!NOTE]
> QSS已飞 毕业快乐

***

## 8. settings.py — 设置窗口

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/settings.py) · QSS：`setting.qss`

### 8.1 自定义设置卡片

| 类                                             | 作用     |
| --------------------------------------------- | ------ |
| `LineEditSettingCard` / `TextLineSettingCard` | 行编辑    |
| `SpinBoxSettingCard`                          | 数值     |
| `SyncStatusSettingCard`                       | 同步状态显示 |
| `AutoOffsetSettingCard`                       | 自动时间偏移 |
| `ButtonSettingCard` / `DualButtonSettingCard` | 按钮触发   |

### 8.2 设置子页

`SettingsSubPage(ScrollArea)` 为基类，子页：

| 子页               | 内容                                                                             |
| ---------------- | ------------------------------------------------------------------------------ |
| `GeneralPage`    | 通用（关闭动作、多实例、自启、空闲、更新）                                                          |
| `TimePage`       | 时间（时钟、农历、偏移、NTP）                                                               |
| `AppearancePage` | 外观（主题、颜色、模糊、壁纸亮度）                                                              |
| `LogPage`        | 日志（级别、禁用、数量、天数）                                                                |
| `AdvancedPage`   | 高级（GPU、调试、下载源）                                                                 |
| `GridPage`       | 网格（格子数、边距、卡片透明度/圆角，含 `_GridPreviewWidget` / `_CornerRadiusPreviewWidget` 实时预览） |

### 8.3 SettingsWindow

`SettingsWindow(FluentWindow)`：独立 FluentWindow，承载上述子页。

***

## 9. about.py — 关于

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/about.py) · QSS：`about.qss`

- `AboutInterface(ScrollArea, TranslatableWidget)`：版本信息、链接卡片、更新检查（`checkUpdateAuto`）、鸣谢。
- `_TechDialog(MessageBoxBase)`：依赖库与许可证弹窗（数据来自 `resource/credits.json`）。

***

## 10. debug.py — 调试面板

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/debug.py) · QSS：`debug.qss`

`DebugPanel(BaseScrollAreaInterface, TranslatableWidget)`：系统监控、快捷操作、网络诊断、API 测试。仅在 `debugMode` 为真时显示于导航底部，`F12` 快速跳转。`_updateTheme` 响应主题。

***

## 11. 跨界面约定

### 11.1 主题切换链

`MainWindow._initThemeConnections()` 将 `cfg.themeChanged` 连接到各界面的 `_onThemeChanged`：

```
cfg.themeChanged → downloadInterface / wallpaper / notificationPage /
                   timetablePage / aboutInterface / _onDebugPanelThemeChanged
```

切换流程：`_onThemeModeChanged` → `clear_qss_cache()` → `setTheme()` → 若未触发则手动 `cfg.themeChanged.emit()` → 各界面重载 QSS。

主题色单独广播：`cfg.themeColor.valueChanged`

### 11.2 国际化

 `tr(key)`取文案（键在 `locale/*.json`）。多数界面继承 `TranslatableWidget`（`setup_translatable_ui()`；整页 HTML 界面在 `_build_html()` 时把需要的文案一并注入模板（语言切换走重启确认框 `_onLanguageConfigChanged`）。

### 11.3 QSS 映射表

| 界面           | QSS 文件                             |
| ------------ | ---------------------------------- |
| Home / 部分组件  | `home.qss`                         |
| 组件库 / 配置弹窗   | `component.qss`                    |
| Wallpaper    | `wallpaper.qss`                    |
| Notification | `notification.qss`                 |
| Timetable    | `timetable.qss`                    |
| Download     | - |
| Settings     | `setting.qss`                      |
| About        | `about.qss`                        |
| Debug        | `debug.qss`                        |
| 启动闪屏 / 向导    | `app.qss`                          |

### 11.4 整页 HTML 界面（QWebChannel）

[ui/download.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/download.py) 范例。

- 外壳：`QWidget` + 单个 `create_html_view(mouse_transparent=False)`，html创建标题。
- 模板放类属性 `_HTML_TEMPLATE`（`string.Template`，`$var` 占位），`_build_html()` 注入、`_render()` 调 `setHtml`。
- 注入三类：调色板（`isDarkTheme()` 选）、主题色 `QColor(cfg.themeColor.value).name()`、json（`</` 替换成 `<\/`）。
- 弹窗 / 提示也用html 搞不懂为啥html创建的卡片和按钮会比pfw的弹窗还高

| 方向 | 做法 |
| --- | --- |
| JS → Python | `QWebChannel` 注册 `bridge`，方法 `@pyqtSlot`；页面引 `qrc:///qtwebchannel/qwebchannel.js` |
| Python → JS | `runJavaScript("window.glimpseon.xxx(...)")`，参数 `json.dumps` 转义 |
| 确认框 | Python 下发 `confirm(id, title, content)`，JS 回 `confirmResult(id, ok)`，`{id: callback}` 承接 |
| 尺寸 | `showEvent` / `resizeEvent` 通知 js 重排（隐藏时视口宽度为 0） |
