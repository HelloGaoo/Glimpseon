# 核心模块（core/）

> [!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

`core/` 所有路径、常量、配置、日志、工具函数集中于此，被 `ui/`、`services/`、主程序复用。

***

## 1. paths.py — 路径推导

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/paths.py)

**职责**：在启动时确定软件文件目录，适配编写态 / 编译态

### 1.1 路径推导逻辑

| 路径             | 推导规则                                                                                                                         |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `PACKAGE_ROOT` | 环境变量 `Glimpseon_PackageRoot` → `sys.frozen` 时为 `sys.executable` 目录 → 否则 `__file__` 上三级（`paths.py` 位于 `app-*/core/`，三级父目录即包根） |
| `APP_DIR`      | 环境变量 `Glimpseon_AppDir` → 否则扫描 `app-*` 目录，选 `record.json` 中 `current==1 && !partial` 的 → 兜底为 `PACKAGE_ROOT`                  |
| `MEIPASS_DIR`  | 仅 `sys.frozen` 时取 `sys._MEIPASS`，否则 `None`                                                                                   |
| `DATA_ROOT`    | `PACKAGE_ROOT/data`，其下细分 `config/log/cache/temp/profile/user/icon/wallpaper/classphotos/notes`                               |

### 1.2 关键函数

- `ensure_data_dirs()`：创建全部数据子目录
- `get_resource_path(relative_path)`：**资源查找三级回退** `APP_DIR → MEIPASS_DIR → APP_DIR`
- `VERSION` / `BUILD_DATE`：从 `APP_DIR/record.json` 读取，失败回退 `1.0.0` / `""`。

### 1.3 注意

`constants.py` 通过 `from core.paths import ...` 复用并重导出大部分路径常量，业务代码统一从 `core.constants` 引入。

***

## 2. constants.py — 常量与 QSS 加载

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/constants.py)

**职责**：集中常量、QSS 加载与缓存。

### 2.1 主要常量

| 常量                  | 值 / 说明                                         |
| ------------------- | ---------------------------------------------- |
| `APP_NAME`          | `"Glimpseon"`                                  |
| `APP_ICON`          | `"resource/icons/CY.png"`                      |
| `TIMETABLE_SOURCES` | `["Glimpseon", "ClassIsland", "ClassWidgets"]` |
| `NEWS_ICONS`        | 新闻源 logo 路径映射                                  |
| `RESOURCE_*`        | 资源子目录别名                                        |
| `get_resPath`       | `get_resource_path` 的别名                        |

### 2.2 QSS 缓存机制

```python
_qss_cache = {}  # key: (theme, qss_filename) -> content

def load_qss(qss_filename) -> str
def clear_qss_cache()
```

- `load_qss` 根据 `isDarkTheme()` 选择 `resource/qss/{light|dark}/<filename>`。
- 切主题后调 `clear_qss_cache()` 清空缓存！！不然就卡死/样式卡两种中间。
- 文件以 `utf-8-sig` 读取（兼容 BOM）。

***

## 3. config.py — 配置管理

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/config.py)

**职责**：基于 qfluentwidgets `QConfig` 声明全部用户配置项。

### 3.1 核心类

- `Config(QConfig)`：所有配置项以类属性形式声明，文件固定为 `DATA_CONFIG/config.json`。
- `cfg`：全局单例，导入期 `qconfig.load()` 加载。
- 序列化器：`ThemeSerializer` / `LanguageSerializer` / `LogLevelSerializer` / `CountdownListSerializer`。

### 3.2 配置分组（节）

| 节              | 代表配置项                                                                                                                                                                                                                                                 |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MainWindow`   | `themeMode`、`themeColor`、`dpiScale`、`language`                                                                                                                                                                                                        |
| `Log`          | `logLevel`、`disableLog`、`logMaxCount`、`logMaxDays`                                                                                                                                                                                                    |
| `Wallpaper`    | `wallpaperSaveLimit`、`autoGetInterval`、`autoSyncToDesktop`、`wallpaperApi`、`Brightness`                                                                                                                                                                |
| `Appearance`   | `backgroundBlurRadius`                                                                                                                                                                                                                                |
| `Time`         | `showClock`、`showClockSeconds`、`showLunarCalendar`、`clockColor`、`clockSize`、`dateSize`、`timeOffset`、`autoTimeOffset*`                                                                                                                                 |
| `Poetry`       | `showPoetry`、`poetryApiUrl`、`poetryUpdateInterval`、`poetrySize`、`poetryTextColor`                                                                                                                                                                     |
| `Weather`      | `showWeather`、`weatherSize`、`weatherTextColor`、`weatherIconSize`、`weatherUpdateInterval`、`city`、`cityCode`、`latitude`、`longitude`                                                                                                                     |
| `Countdown`    | `showCountdown`、`countdownDisplayMode`、颜色/字号、`countdownCarouselInterval`、`countdownList`                                                                                                                                                              |
| `School`       | `school`、`schoolClass`、`showSchoolInfo`、颜色/字号                                                                                                                                                                                                         |
| `QuickLaunch`  | `showQuickLaunch`、`quickLaunchApps`、`quickLaunchIconSize`、`quickLaunchIconSpacing`、`showLabels`、`offsetY`                                                                                                                                             |
| `Media`        | `showMediaInfo`、`showMediaCover`、`showMediaLyrics`、`mediaUpdateInterval`、`mediaTextSize`、`mediaCoverSize`、`mediaLyricsSize`、`mediaLyricsAdvance`、`mediaUseCustomBg`、`mediaBgOpacity`、`mediaBorderRadius`、颜色配置                                         |
| `Linkage`      | `linkageEnabled`、`linkageDataPath`、`linkagePollInterval`、`linkageSyncTimeConfig`                                                                                                                                                                      |
| `ClassWidgets` | `classWidgetsEnabled`、`classWidgetsDataPath`、`classWidgetsPollInterval`                                                                                                                                                                               |
| `PreciseTime`  | `usePreciseTime`、`timeServer`、`lastSyncTime`                                                                                                                                                                                                          |
| `Grid`         | `gridShortSideCells`、`gridInsetPercent`、`componentCardOpacity`、`componentCardRadius`                                                                                                                                                                  |
| `Other`        | `closeAction`、`allowMultipleInstances`、`debugMode`、`enableGpuAcceleration`、`autoStart`、`autoOpenOnIdle`、`idleMinutes`、`autoOpenMaximize`、`autoCheckUpdate`、`autoUpdate`、`minimizeNotificationCount`、`scrollBannerBgHeight`、`scrollBannerMouseThrough` |
| `Download`     | `downloadSource`、`downloadItemsPerPage`                                                                                                                                                                                                               |

> [!NOTE]
> 完整配置项与默认值见 [配置系统](configuration.md)。

### 3.3 自动保存机制

```python
for attr_name in dir(cfg):
    attr = getattr(cfg, attr_name)
    if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
        attr.valueChanged.connect(_on_config_changed)  # → save_cfg()
```

导入期遍历所有 `ConfigItem`，连接 `valueChanged` 到 `save_cfg`，因此修改 `cfg.xxx.value` 即自动持久化。

带 `restart=True` 的项（如 `language`、`dpiScale`、`enableGpuAcceleration`）变更后需重启生效。

### 3.4 API

- `cfg`：全局配置单例
- `save_cfg()`：立即保存
- `default_cfg()`：返回完整默认配置字典（用于重置 / 向导）

***

## 4. logger.py — 日志系统

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/logger.py)

**职责**：日志器

### 4.1 日志格式

```
%(precise_time)s|%(levelname)s|%(caller_info)s|%(module)s:%(lineno)d|%(message)s
```

- `precise_time`：NTP 校准后的精确时间（`precise_now()`）。
- `caller_info`：调用方信息。
- 这些字段由 `CustomLogger._log` 注入 `extra`，主 `Glimpseon` logger 在 `setLoggerClass(CustomLogger)` 之后创建。

### 4.2 关键类与函数

- `CustomLogger(logging.Logger)`：重写 `_log`，注入 `precise_time` / `caller_info`。
- `Logger`：封装类，提供 `update_cfg(disable_log, log_level, max_count, max_days)`、文件轮转（`LOG_MAX_BYTES=1MB`）、控制台输出。
- `logger`：全局实例（`Glimpseon`）。
- `init_exhook()`：一次性安装全部异常/信号钩子。

### 4.3 异常钩子（init\_exhook 安装）

| 钩子                                    | 覆盖范围                      |
| ------------------------------------- | ------------------------- |
| `_install_faulthandler`               | C 段错误 / 崩溃栈写入文件           |
| `_install_sys_excepthook`             | 主线程未捕获异常                  |
| `_install_threading_excepthook`       | 子线程异常                     |
| `_install_qt_message_handler`         | Qt `qWarning`/`qCritical` |
| `_install_asyncio_exception_handler`  | asyncio 异常                |
| `_install_multiprocessing_handler`    | 子进程异常                     |
| `_install_concurrent_futures_handler` | Future 异常                 |
| `_install_signal_handlers`            | SIGTERM 等信号               |
| `_install_atexit_handler`             | 退出钩子                      |

### 4.4 子模块日志器约定

使用层级命名 `Glimpseon.core.{module}`（如 `Glimpseon.core.config`），与主 `Glimpseon` logger 区分。建议在函数内懒创建。

### 4.5 系统上下文

`_get_system_context()` 收集时间、PID、Python/系统版本、内存/CPU/磁盘（psutil，可选）、线程数与线程名，写入崩溃日志头部。

***

## 5. utils.py — 工具函数

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/utils.py)

**职责**：杂七杂八的函数。

### 5.1 单实例管理

- `SingleInstanceManager`：基于 `Glimpseon_native.acquire_mutex` 的命名互斥锁，Mutex 名 `Glimpseon_SingleInstance_Mutex_{GUID}`。
- `verify_single_instance()`：`allowMultipleInstances` 或 `debugMode` 为真时直接放行。

### 5.2 字体

- `initialize_fonts(app, install_to_system=True)`：检测系统是否已装 HarmonyOS Sans，未装则调 `Glimpseon_native.install_font` 安装；并 `setFontFamilies([...])`。
- `resolve_font_family()`：从 `HARMONYOS_FONT_FAMILIES + FONT_FAMILY_CANDIDATES` 中选首个系统可用字体。
- 字体回退链（`apply_fonts` 注入 QFont 替换规则 + 全局 QSS）：`HarmonyOS Sans → Microsoft YaHei UI → Microsoft YaHei → PingFang SC → Source Han Sans SC → Segoe UI → sans-serif`。
- `FONT_FAMILY` 常量（`core/constants.py`）供 UI 组件层统一引用，与回退链一致。

### 5.3 缓存

文件缓存位于 `DATA_CACHE`，每个缓存项含 `content` + `expiry`（按 `interval_str` 解析）。

- `save_cache(name, content, interval_str)`
- `load_cache(name, ignore_expiry=False)` → `{content, expiry}`
- `get_cached_content(name, ignore_expiry=False)` → 直接返回 content
- `clear_cache(name)` / `clear_all_cache()`
- `parse_interval("30m")` → 秒数

### 5.4 资源解包

- `extract_files()`：从 `_MEIPASS` 提取 `Tools/`（7z、aria2c）等外部工具到 `PACKAGE_ROOT`

### 5.5 自启动

- `set_autostart(enabled, delay_seconds=5)`：写注册表 `HKCU\...\Run`，带延迟启动参数。
- `sync_autostart_cfg()`：同步配置与注册表状态。
- `auto_start_launch()`：检测本次是否由自启动触发。
- `check_autostart()`：读注册表判断是否已启用。

### 5.6 翻译系统

- `LanguageCode(Enum)`：`ZH_CN / ZH_TW / EN_US`。
- `TranslationManager(QObject)`：加载 `locale/{lang}.json`，`set_language(code)`，带 `language_changed` 信号。
- `tr(key, **kwargs)`：全局翻译查找，支持 `str.format` 插值。
- `TranslatableWidget`：可翻译控件基类，提供 `retranslate` 钩子。

### 5.7 精确时间

- `TimeSyncService`：NTP 客户端（默认 `ntp.aliyun.com`），后台同步。
- `precise_now()` → `datetime`，`precise_time_str()` → 字符串。
- `_check_auto_time_offset()`：根据 `autoTimeOffsetEnabled` / `autoTimeOffsetIncrement` 自动偏移。

### 5.8 Fluent 图标命名空间

- `_FluentUIIconInstance(FluentIconBase)`：按主题查 `resource/fluent/{light|dark}/ic_fluent_{name}_{32|24}_regular.svg`。
- `_FluentUIIconNamespace` / `FUI`：枚举式访问，如 `FUI.HOME`、`FUI.RIGHT_ARROW`、`FUI.SETTING`。

***

## 6. component.py — 组件系统

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/component.py)

**职责**：组件定义、网格布局计算、注册表、页面管理。详见 [组件系统](component-system.md)。

### 6.1 数据类

- `ResizeMode`：`FIXED / HORIZONTAL / VERTICAL / FREE`。
- `ComponentDefinition`：组件元数据（id、分类、图标、最小/默认格子数、resize\_mode、component\_class、default\_config）。
- `GridSettings`：`short_side_cells`、`gap_ratio`、`inset_percent`。
- `GridMetrics`：网格计算结果（行列数、格子尺寸、间隙、边距）。
- `PageMeta`：页面元信息。

### 6.2 服务类

- `GridLayoutService`：根据画布尺寸 + `GridSettings` 计算 `GridMetrics`，提供格子↔像素换算。
- `ComponentRegistry(QObject)`：组件注册表，`register` / `register_batch` / `unregister` / `get_definitions_by_category` / `load_from_json`，发 `definitions_changed` 信号。
- `PageManager`：页面增删改查与持久化。

### 6.3 内置组件

`BUILTIN_COMPONENT_DEFINITIONS` 预定义若干组件（数字时钟、月历等），通过 `ComponentRegistry` 注册。

***

## 7. notification.py — 通知系统

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/notification.py)

**职责**：多种通知弹窗 + 队列管理 + TTS/音频。

### 7.1 通知类型

`NotifType`：`SCROLL`（滚动横幅）/ `CORNER`（右下角）/ `FULLSCREEN`（全屏）。

### 7.2 弹窗基类

- `_BasePopup(QFrame)`：无边框 + 置顶 + Tool + 不抢焦点 + 可选鼠标穿透；`show()` 时调 `SetWindowPos(HWND_TOPMOST)` 并按需设置 `WS_EX_TRANSPARENT`。

### 7.3 弹窗实现

- `ScrollBanner`：滚动横幅，高度可配（`scrollBannerBgHeight`），可鼠标穿透。
- `FullScreenPopup`：全屏通知。

### 7.4 管理器

`NotificationManager(QObject)`：接收 `handle_notification` 槽，维护通知队列与窗口生命周期，发 `notification_finished` 信号。支持 TTS 与音频播放。

***

## 8. downloader.py — 文件下载

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/downloader.py)

**职责**：多源下载、7z/zip 解压、静默安装、进程优先级控制。

### 8.1 main

- `DOWNLOAD_SOURCES`：`original`（GitHub 直连）/ `hk` / `cloudflare` / `edgeone` / `geekertao` 
- `set_priority_pid(pid, level)`：通过 `SetPriorityClass` 调进程优先级（默认 `below_normal`，避免下载抢占）。
- `SEVEN_ZIP_PASSWORD`：加密 7z 包统一密码。
- `Downloader` 类：封装下载 + 解压 + 静默安装（COM `Dispatch`）流程。
- `cleanup_temp_directory(temp_dir, logger)`：清理临时目录。

### 8.2 安装方法约定

每个可下载软件对应一个按软件名命名的方法（去掉空格与方括号）：
这还是老项目复制过来的
```python
def _install_<软件名>(self, software_name, cache_file,
                      progress_callback=None, download_complete_callback=None)
```

- 由 ui/ 按 `_install_` + 去掉特定字后的名称查找并调用（`hasattr` → `getattr`）。
- `cache_file`：`resource/url_dir.py` 中匹配到的那条记录（含 `url` / `github_path` / `hash`）。
- `progress_callback(software_name, percent)`：线程回调，UI 更新经 `pyqtSignal` 回主线程（见 [ui-modules.md 7.5](ui-modules.md#75-线程)）。

### 8.3 外部工具

使用 `Tools/7z.exe`、`Tools/aria2c.exe`（由 `extract_files()` 释放）。

***

## 9. linkage.py — 外部联动

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/linkage.py)

**职责**：与 ClassIsland / ClassWidgets 集成，同步课表与课程时间状态。

### 9.1 核心类型

- `TimeState(IntEnum)`：`NONE / PREPARE_ON_CLASS / ON_CLASS / BREAKING / AFTER_SCHOOL`，带 `display_name`。
- `LessonInfo`：单节课信息（科目、教师、首字母、起止时间、序号），支持 `from_subject_data` / `to_dict`。

### 9.2 机制

- 读取外部应用 `Profiles/Default.json` 与 `Settings.json`。
- 轮询（`linkagePollInterval` / `classWidgetsPollInterval`），动态检测路径变化与自动重连。
- `linkageSyncTimeConfig` 控制是否同步时间配置。
- 使用 `precise_now()` 计算当前课程时间状态。

***

## 10. timetable.py — 课表配置

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/timetable.py)

- `TimetableProfile`：单份课表档案（名称、默认上课/下课时长、`periods` 时段列表、`courses` 各时段课程），`to_dict()` / `from_dict()` / `save()` / `load()` 读写 json
- 模块函数：`get_profile_path(name)`、`list_profiles()`、`next_profile_name()`、`ensure_default_profile()`、`rename_profile(old, new)`、`delete_profile(name)`。
- 课表 JSON 存于 `DATA_PROFILE`（`data/profile/`），与 `cfg.profileSource`（`Glimpseon / classisland / classwidgets`，见 [配置系统](configuration.md)）共同决定课表来源。

***

## 11. updater.py — 软件更新

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/updater.py)

**职责**：基于 GitHub Releases 的检查 / 下载 / 部署 / 清理。

### 11.1 端点

- `GITHUB_API`：`https://api.github.com/repos/HelloGaoo/Glimpseon/releases/latest`
- `CHANGELOG_URL`：经 `gh-proxy` 代理拉取 `changelog.md`。

### 11.2 流程函数

| 函数                                                     | 作用                                             |
| ------------------------------------------------------ | ---------------------------------------------- |
| `get_github_changelog(max_retries=3)`                  | 拉取更新日志                                         |
| `check_github_version(max_retries=3)`                  | 比较版本号                                          |
| `download_update(url, progress_callback, max_retries)` | 下载更新包到 `DATA_TEMP`                             |
| `extract_update(archive_path, target_version)`         | 解压为新 `app-{version}` 目录                        |
| `deploy_update(new_version_dir)`                       | 写新 `record.json`（`current=1`），旧版本 `deactivate` |
| `create_update_script(new_version_dir)`                | 生成切换脚本                                         |
| `cleanup_update_files()` / `cleanup_old_versions()`    | 清理临时与旧版本                                       |

依赖 `core.record` 的 `create_record` / `save_record` / `load_record` / `deactivate_version`。

***

## 12. record.py — 版本记录

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/record.py)

**职责**：管理各 `app-*` 目录的 `record.json`。

### 12.1 数据结构

```json
{
  "current": 1,
  "partial": false,
  "version": "1.0.0",
  "files": { "相对路径": {"hash": "sha256", "size": 123} },
  "variables": { "install_time": "ISO 时间" }
}
```

### 12.2 函数

- `scan_files(directory)`：递归计算所有文件 sha256 + size（跳过 `record.json` 自身）。
- `create_record(version, app_dir, current=1, partial=False)`
- `save_record(record, record_path)` / `load_record(record_path)`
- `deactivate_version(version_dir)`：置 `current=0`。

> [!NOTE]
> `partial=true` 表示升级不完整，启动器与 `_detect_app_dir` 均会跳过该版本。

