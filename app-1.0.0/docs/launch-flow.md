# 启动流程

> [!NOTE]
> 编写者：HelloGaoo 最后修改：2026/10/05

从双击 Glimpseon.exe 到主窗口显示的完整时序

## 阶段 0 启动器 (Glimpseon.cs Launcher.Main)

```mermaid
flowchart TD
    A["枚举 PackageRoot/app-* 目录"] --> B{"record.json 存在 且 partial!=true 且解析成功"}
    B -->|"否"| B1["跳过该目录"]
    B -->|"是"| C["按 current 降序 > 版本号降序 取首个"]
    C --> D["启动 app-*/GlimpseonMain.exe"]
    D --> E["注入 env Glimpseon_PackageRoot / Glimpseon_AppDir"]
    E --> F["WaitForExit 转发退出码"]
```

## 阶段 1 主程序入口 (GlimpseonMain.Main)

```mermaid
flowchart TD
    A["Paths.EnsureDataDirs 建 data 子目录"] --> B["Config.Load 读 config.json 缺项补默认并落盘"]
    B --> C["BuildAvaloniaApp(gpu) AngleEgl > Software 渲染链"]
    C --> D["StartWithClassicDesktopLifetime"]
    D --> E{"App 退出后 Config.RestartPending"}
    E -->|"真"| F["AppUtils.RestartSelf 原进程重启"]
    E -->|"假"| G["返回退出码"]
```

## 阶段 2 RunStartupAsync (App.OnFrameworkInitializationCompleted 调用)

```mermaid
flowchart TD
    S1["1 Log.InitExceptionHooks 未捕获异常全量落盘 > 原生 MessageBox > 退出码 1"] --> S2["2 挂 ProcessExit 释放单实例互斥体 + MediaServices.Close"]
    S2 --> S3["3 打印运行环境 APP_DIR 版本 构建日期 系统 运行时 包根"]
    S3 --> S4["4 Log.Configure DebugMode 时 3 份 / 1 天"]
    S4 --> S5["5 InitTranslation + ApplyLanguageFromConfig > ApplyAccentColor 解析 ThemeColor"]
    S5 --> D1{"6 DebugMode"}
    D1 -->|"是"| S6["Config.SelfCheckAll 全量配置项序列化往返自检"]
    D1 -->|"否"| D2{"7 VerifySingleInstance Mutex Glimpseon_SingleInstance_Mutex_{A7F3...}"}
    S6 --> D2
    D2 -->|"失败"| F1["透明全屏提示 Common.Alert > Shutdown"]
    D2 -->|"成功"| D3{"8 WizardWindow.IsNeeded Setup_Wizard.json 缺失或 completed!=1"}
    D3 -->|"是"| W1["Show 向导 await WaitForWindowCloseAsync"]
    D3 -->|"否"| S9["9 SplashWindow 显示版本号与图标"]
    W1 --> S9
    S9 --> P1["10 字体初始化 InitializeFonts HarmonyOS Sans > 后台 Downloader.CleanupTempDirectory"]
    P1 --> P2["11 LogConfigSummary 打印全部分组配置摘要"]
    P2 --> P3["12 new MainWindow 创建导航壳"]
    P3 --> P4["13 预加载 + RegisterCacheRefreshers + CacheRefresher.Start > 0.6s 超时转后台"]
    P4 --> P5["14 AutoCheckUpdate > window.AboutView.CheckUpdateAuto"]
    P5 --> D4{"15 --navtest 参数"}
    D4 -->|"有"| N1["RunNavTestAsync 导航自测"]
    D4 -->|"无"| S16["16 window.Show 最大化 SwitchToHome > SyncAutostartCfg 同步注册表自启动"]
    N1 --> S16
    S16 --> S17["17 lifetime.MainWindow=window ShutdownMode=OnMainWindowClose > 关闭 Splash 记录总耗时"]
    S17 -.->|"任一步异常"| X["Log.Critical + Native.ShowMessageBox > Shutdown(1)"]
```

## 预加载 (PreloadWallpaperAsync)

```mermaid
flowchart TD
    A{"City 有效"} -->|"是"| B["RegionDatabase.GetCoordinates 回填 Longitude/Latitude 变化才写"]
    A -->|"否"| C["后台任务按优先级取图"]
    B --> C
    C --> D{"WallpaperService.LoadCachePath 缓存命中"}
    D -->|"是"| F["UI 线程 SetWallpaper"]
    D -->|"否"| E{"Assets/wallpaper/default.jpg 存在"}
    E -->|"是"| F
    E -->|"否"| G["data/wallpaper 最近修改的 wallpaper_*.jpg"]
    G --> F
    F --> H["WallpaperView.NotifyWallpaperFetched source=cache"]
```

## 缓存刷新器 (RegisterCacheRefresher)

| Name | 间隔来源 | 开关 | Apply |
|---|---|---|---|
| weather | Weather/UpdateInterval | ShowWeather | 天气数据写缓存 |
| poetry | Poetry/UpdateInterval | ShowPoetry | 一言文本写缓存 |
| wallpaper | Wallpaper/AutoGetInterval | AutoGetInterval != never | SetWallpaper + 可选 SetDesktop (SkipSave 不写内容缓存) |

配置联动 City / UpdateInterval / ShowWeather / PoetryApiUrl / WallpaperApi / AutoGetInterval 任一 ValueChanged > CacheRefresher.Invalidate 对应项

## 阶段 3 退出与重启

```mermaid
flowchart TD
    A{"CloseAction"} -->|"minimize"| B["关闭按钮最小化到托盘区"]
    A -->|"close"| C["直接退出"]
    D["设置页重启项变更 Language DpiScale LogLevel DisableLog EnableGpuAcceleration"] --> E["RequestRestart > MarkRestartPending + Shutdown"]
    E --> F["Main 尾部 RestartSelf 拉起"]
    G["更新部署完成"] --> H["生成 update_ready.bat > Shutdown > 下次启动器选择新目录 详见 Versioning.md"]
```
