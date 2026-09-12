# 启动流程

> [!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

本文档梳理从用户启动到主窗口就绪的完整时序，涵盖启动器、闪屏、向导、主窗口、预加载各阶段。

[主程序源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/GlimpseonMain.py) · [启动器源码](https://github.com/HelloGaoo/Glimpseon/blob/main/Glimpseon.py)

***

## 1. 阶段 0：启动器（Glimpseon.py）

[启动器](https://github.com/HelloGaoo/Glimpseon/blob/main/Glimpseon.py) 负责版本选择与子进程拉起。

```
find_app()
  ├─ 扫描根目录所有 app-* 子目录
  ├─ 读取各 app-*/record.json
  │    └─ 跳过 partial=true 的目录
  ├─ 解析版本号 tuple (major, minor, patch)
  ├─ 排序：current(降序) → 版本号(降序)
  ├─ 选中首个，注入环境变量：
  │    Glimpseon_PackageRoot = 根目录
  │    Glimpseon_AppDir      = 选中 app 目录
  └─ subprocess.run([python, GlimpseonMain.py], env)
```

> [!NOTE]
> 参考自 ClassIsland 的启动器设计。

***

## 2. 阶段 1：主程序初始化（GlimpseonMain.py 顶部）

导入期即执行的初始化（模块导入顺序敏感）：

1. **路径推导**：[core/paths.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/paths.py) 读取环境变量，计算 `PACKAGE_ROOT` / `APP_DIR` / `DATA_*`，`ensure_data_dirs()` 建目录。
2. **配置加载**：[core/config.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/config.py) `qconfig.load(CONFIG_PATH, cfg)`，连接所有 `valueChanged → save_cfg`。
3. **日志器**：[core/logger.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/logger.py) 必须先 `logging.setLoggerClass(CustomLogger)` 再创建 `Glimpseon` logger，否则 `precise_time` / `caller_info` 字段缺失。

***

## 3. 阶段 2：`__main__` 入口

[GlimpseonMain.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/GlimpseonMain.py) 的 `__main__` 起。

### 3.1 QApplication 与线程池

```
auto_start_launch = auto_start_launch()       # 检测自启动
setHighDpiScaleFactorRoundingPolicy(PassThrough)
if cfg.enableGpuAcceleration:
    setAttribute(AA_UseOpenGLES)
app = QApplication(sys.argv)
init_exhook()                                  # 安装异常钩子
atexit.register(release_single_instance)
executor = ThreadPoolExecutor(max_workers=2)
_extract_future = executor.submit(extract_files)   # 后台释放 Tools/（另一下载器项目遗留，当前基本未用到。。完全用不到 占地方。）
```

### 3.2 向导（首次运行）

```
if check_wizard_needed():                      # Setup_Wizard.json completed != 1
    create_wizard_file()
    wizard = WizardWindow()                    # 5 页向导
    wizard.exec()
```

向导 5 页：欢迎 → 协议（开源协议/用户协议/隐私）→ 基本设置（自启/空闲/桌面快捷方式）→ 外观（主题/颜色）→ 学校信息（城市/学校/班级）。完成后写 `completed: 1`。

### 3.3 闪屏显示

```
splash = SplashScreen(APP_NAME, VERSION, icon_path)
splash.show(); splash.setProgress(0)
```

`SplashScreen` 无边框置顶，进度条带定时器动画（`_advance_progress`）。

### 3.4 后台初始化任务

```
def _background_init():
    cleanup_temp_directory()        # 清理 data/temp
executor.submit(_background_init)
```

### 3.5 单实例检查

```
if not verify_single_instance():
    # 弹出「已有实例运行」对话框后 sys.exit(0)
```

`verify_single_instance` 在 `allowMultipleInstances` 或 `debugMode` 为真时放行。

### 3.6 字体初始化

```
_extract_future.result()            # 等待 Tools 释放
initialize_fonts(app, install_to_system=True)  # 装 HarmonyOS Sans 到系统
```

### 3.7 日志配置

```
logger.update_cfg(disable_log, log_level, max_count, max_days)
# DebugMode 下强制最小值
```

随后打印`logger.info` 记录所有配置项。

***

## 4. 阶段 3：主窗口创建

### 4.1 MainWindow\.__init__

```
window = MainWindow()
  ├─ setTheme(cfg.themeMode.value)
  ├─ _initTranslation()           # 安装 FluentTranslator 与 翻译
  ├─ _initNavigation()            # 注册各子界面（见下）
  ├─ resize(1050, 750) / moveToCenter / _loadWindowPosition
  ├─ initSystemTray()             # 系统托盘
  ├─ sync_autostart_cfg()         # 同步注册表自启
  ├─ _initIdleDetection()         # 空闲检测 / 全局钩子
  ├─ _initThemeConnections()      # 主题信号广播
  └─ _initSystemThemeMonitor()    # 轮询系统主题（auto）
```

### 4.2 导航注册（\_initNavigation）

| 顺序    | 界面                   | 图标                    | 导航文本            |
| ----- | -------------------- | --------------------- | --------------- |
| 1     | `HomeInterface`      | `FUI.HOME`            | 主界面             |
| 2     | `WallpaperInterface` | `FUI.PHOTO`           | 壁纸              |
| 3     | `NotificationPage`   | `FUI.MESSAGE`         | 通知              |
| 4     | `TimetablePage`      | `FUI.EDUCATION`       | 课程表             |
| 5     | `DownloadInterface`  | `FUI.DOWNLOAD`        | 软件下载            |
| 6（底部） | `AboutInterface`     | `FUI.INFO`            | 关于              |
| 7（底部） | `DebugPanel`         | `FUI.DEVELOPER_TOOLS` | 调试（仅 debugMode） |

`NotificationManager` 在通知页之后创建，连接 `send_notification → handle_notification`。

下载页数据通过 `QTimer.singleShot(0, _populateDownload)` 异步填充：`addSection()` / `addSoftware()` 只入队数据，`_onDataPopulated()` → `_requestRender()` 时才生成整页 HTML（`_build_html()` → `setHtml`）。此时页面尚未显示，渲染被挂起（`_pendingLayout`），等首次 `showEvent` 再执行。

***

## 5. 阶段 4：预加载（Preloader）

```
loader = Preloader(window)
loader.sig_wp.connect(_upd_wp)      # 壁纸
loader.sig_wt.connect(_update_weather_display)  # 天气
loader.sig_po.connect(_upd_po)      # 一言
loader.start()
```

`Preloader(QThread)` 顺序执行（可被 `cancel()` 中断）：

### 5.1 \_load\_wp（壁纸）

```
1. 若已有 current_pixmap → 跳过
2. get_cached_content("wallpaper", ignore_expiry=True)  # 过期也用旧的
   └─ 命中 → sig_wp.emit(path, src, url)
3. requests.get(API, stream=True)
   └─ 200 → 落盘 wp_HHMMSS.jpg → _manageWallpaperLimit → save_cache → emit
4. 失败 → 默认壁纸 resource/wallpaper/default.jpg
5. 再失败 → data/wallpaper/ 下最新 wallpaper_*.jpg
```

### 5.2 \_load\_wt（天气）

```
1. cfg.showWeather 为假 → 跳过
2. get_cached_content("weather") 命中 → sig_wt.emit(data)
3. RegionDatabase().get_coordinates(city) → 更新经纬度
4. WeatherService().fetch_all() → save_cache → emit
```

### 5.3 \_load\_po（一言）

```
1. cfg.showPoetry 为假 → 跳过
2. get_cached_content("poetry") 命中 → sig_po.emit(text)
3. PoetryService.get_poetry() → save_cache → emit
```

### 5.4 主线程槽

- `_upd_wp`：设 `current_pixmap` → `_updateMainWindowBackground` → `_applyEffects`（模糊）→ `infoCard.updateInfo` → `historyManager.add` → `wallpaperChanged.emit()`。
- `_update_weather_display`：更新 `_cached_weather` → `weather_updated.emit`。
- `_upd_po`：更新 `_cached_poetry` → `poetry_updated.emit`。

### 5.5 自动更新检查

```
if cfg.autoCheckUpdate.value:
    window.aboutInterface.checkUpdateAuto()
```

### 5.6 等待预加载

```
while loader.isRunning():
    allow_ui_update()
    if 超时: loader.cancel(); loader.wait(); break
```

***

## 6. 阶段 5：收尾

```
splash.setProgress(95)
allow_ui_update()
splash.setProgress(100)
splash.waitForProgress()    # 等进度条动画到 100
allow_ui_update()
splash.close()
window.showMaximized()
tray_icon.show()
sys.exit(app.exec())
```

> [!NOTE]
> **顺序说明**：当前实现是先 `splash.close()` 再 `window.showMaximized()`。`close` 前的 `allow_ui_update()` 让事件循环处理完闪屏末帧与待绘制事件，避免主窗口显示瞬间的白屏。

***

## 7. 启动耗时埋点

主程序在关键节点（Splash 显示、语言配置、字体初始化、后台等待、创建主窗口、翻译系统初始化、`_initNavigation`、各 Interface、预加载、进度条 100% 等待、总启动耗时）用 `time.time()` 计时并 `logger.info`，日志键名以「耗时」类字段写入 `data/log/`。

***

## 8. 关键约束

| 约束                             | 原因                                            |
| ------------------------------ | --------------------------------------------- |
| 组件加载须在 splash 期间同步完成           | `QTimer.singleShot(0)` 会导致主窗口显示后才加载，产生延迟      |
| 主 logger 在 CustomLogger 类设置后创建 | `CustomLogger._log` 覆盖发生在类设置时，后建才有 `precise_time`/`caller_info` 字段 |
| 子模块 logger 用层级命名               | 与主 `Glimpseon` logger 区分                      |
| splash 先 show 后 close，close 前 `allow_ui_update` | 事件循环处理完闪屏末帧，主窗口显示无白屏 |
| 预加载用 QThread + 信号              | 预加载在子线程执行，经 `pyqtSignal` 回主线程刷新 UI            |
| 预加载超时强制取消                      | 网络异常时启动不被挂起                                   |

