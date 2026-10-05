# UI 模块

> [!NOTE]
> 编写者：HelloGaoo 最后修改：2026/10/05

## 1 GlimpseonMain.cs — 入口与启动编排

- Main > Paths.EnsureDataDirs > Config.Load > BuildAvaloniaApp 按 Config.EnableGpuAcceleration 选 [AngleEgl, Software] / [Software] > StartWithClassicDesktopLifetime(args)"]
    E --> F
    F --> G{"IsRestartPending"}
    G -->|"是"| H["RestartSelf ProcessPath 重新拉起"]
    G -->|"否"| I["返回退出码"]
```

- RunStartupAsync 全序见 launch-flow.md 阶段 2 本文件要点:

- PreloadWallpaperAsync > 城市>坐标回填 (RegionDatabase 与 Config.Longitude/Latitude 差异才写) > 壁纸三级预取 缓存 path > Assets 默认壁纸 > data/wallpaper 最新历史 (流程见 launch-flow.md 预加载)
  - RegisterCacheRefreshers 三条 Entry (机制见 core-modules.md 5.5):
    - weather > Enabled=ShowWeather 无 Apply 组件自读缓存
    - poetry > Enabled=ShowPoetry 文本序列化存储
    - wallpaper > AutoEnabled=interval!=never SkipSave=true (Fetch 内部已落缓存) Apply 回 UI + AutoSyncToDesktop 时后台 SetDesktop
  - 配置失效联动 > City / WeatherUpdateInterval / ShowWeather > weather > PoetryApiUrl > poetry > WallpaperApi / AutoGetInterval > wallpaper

## 2 App.axaml(.cs)

- 资源引入 FluentAvalonia 与 Fluent 主题 > OnFrameworkInitializationCompleted 调 GlimpseonMain.RunStartupAsync > 静态 App.Lifetime 供更新流程 Shutdown

## 3 MainWindow — 导航壳

- 构造序 > LoadWindowIcon (Constants.AppIcon + VersionText) > InitTranslation (navigation.\* 键) > InitNavigation > InitTray (托盘) > InitIdleDetection > InitTimeSync > InitThemeMonitor (系统主题跟随) > DebugMode 订阅 (NavItemDebug 显隐) > InitSettingsWatch
- InitNavigation > new 七个页面 > FAFrame (IsNavigationStackEnabled=false + PassThroughNavigationPageFactory) > NavigateFromObject(HomeView)
- PageFor(tag) > wallpaper/notification/timetable/download/about/debug 其余归 HomeView
- OnNavSelectionChanged > Dispatcher.UIThread.Post 剥离输入手势后切页 (同步拆页会卡渲染) NavigationFailed 全栈 Log
- 公开方法 > SwitchToHome (重置选中) / RunNavTestAsync (--navtest 顺序遍历验证切页) / SwitchToDebug / RestoreFromTray / MinimizeToDesktop / OpenSettingsWindow

## 4 HomeView — 主界面

分层 (axaml 自上而下):

```
Root (Panel)
  BackgroundImage   壁纸层 Stretch=Fill
  DimOverlay        亮度遮罩
  PagesContainer    Canvas (内部 _pagesStack Canvas ClipToBounds)
  GridOverlay       网格辅助线 编辑态可见
  BottomBar         底部操作条
```

构造序:

```
PageManager(Paths.DataConfig)
ComponentRegistry.RegisterBatch(BuiltinComponentDefinitions.All)
ComponentManager(this)
GridSettings { ShortSideCells=Config.GridShortSideCells GapRatio=0.12 InsetPercent=Config.GridInsetPercent }
GridOverlay + InitPages + InitBottomBar + LoadComponents + ApplyPageVisibility
订阅 > BackgroundBlurRadius/WallpaperBrightness > ApplyBackgroundEffects
      GridShortSideCells/GridInsetPercent > UpdateGridMetrics
DragDrop 允许 + Pointer 事件 Tunnel 挂接 (Pressed/Moved/Released/Wheel)
```

- SetWallpaper(path) > Bitmap 加载 (旧图 Dispose) > ApplyBackgroundEffects > Blur>0 时 BlurEffect / DimOverlay.Opacity = clamp(-Brightness/100 0 1)
- 页面模型 > info 页 = Canvas (DraggableContainer 宿主 SizeChanged 重放 ApplyPercent) / nav 页 = NavigationPage
- LayoutPages > 页宽高=容器尺寸 Canvas.SetLeft(page i*w) \_pagesStack 平移 -currentPage*w > GoToPage 动画切页
- ApplyPageVisibility > 只显示当前页组件 且 enabled

## 5 Components.axaml.cs — 组件实现库 (约 9000+ 行)

- 网格算法 组件定义 注册表 实例化链路 编辑交互 组件清单与私有持久化 见 component-system.md 此处不重复
- 本文件要点:
  - DraggableContainer 坐标唯一真源是百分比 (0-1) 像素拖拽反算百分比落库 布局未完成 (Bounds=0) 时跳过等 SizeChanged 重放
  - DpiScale (1-300) 经 ScaleTransform 矢量缩放 VisualWidth/Height = Width\*Dpi/100
  - SelectionAdorner 命中测试只认右下手柄区 其余穿透 (可直接拖动卡片)
- 控件类清单 (WidgetCardBase 衍生 ApplyTheme/BindOpacity 自动生效):

| 区块   | 类                                                                                                                                                                |
| ---- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 框架   | DraggableContainer PlaceholderWidget SelectionAdorner ComponentLibraryWindow (预览骨架卡/实况卡) NavigationPage (items 跳转 LaunchItem) StyleWidgetFactory (typeId > 控件路由) |
| 时钟   | DigitalClockWidget (中文星期表)                                                                                                                                       |
| 一言   | PoetryWidget (RefreshFromCache/FormatPoetry)                                                                                                                     |
| 天气   | WeatherWidgetBase > WeatherCurrentWidget / WeatherHourlyWidget / WeatherWeeklyWidget                                                                             |
| 倒计时  | CountdownEventWidget / CountdownDaysWidget                                                                                                                       |
| 学校   | SchoolInfoWidget + TimetableScheduleProvider (GetTodaySchedule/LatestProfileName/BuildTimelineNodes 按 ProfileSource 分发本地档案或联动桥)                                  |
| 新闻   | NewsWidget (ItemCount=4 单条点击 OpenNews)                                                                                                                           |
| 课表联动 | TimetablePreviewWidget (行进度条) / TimetableNowLessonWidget (预备态 RenderPrepare + 常态 RenderNormal TeacherDisplay 缩写)                                                 |
| 媒体   | MediaPlayerWidget (进度/歌词/封面/控制)                                                                                                                                  |
| 工具系  | 快捷启动 (图标经 Native 提取落 data/icon) 书写板 便签 计算器 性能 网速 分贝仪 作业板 相册 公告栏等                                                                                                 |

## 6 WallpaperView — 壁纸页

- 当前壁纸预览 站点切换 (Config.WallpaperApi) 手动换一张 桌面同步开关 历史网格 删除/清理 (SyncCleanup)
- NotifyWallpaperFetched(path, source) 供预加载与刷新器回调
- 下载走 WallpaperService.FetchAsync 见 services.md 2

## 7 TimetableView — 课程表页

- 档案下拉 (Timetable.ListProfiles) + 添加 (NextProfileName)/重命名/删除/导入/导出/打开目录
- 表格编辑 > AddPeriod/RemovePeriod (courses 自动重排) 时间段 科目 (预设+自定义) 默认时长
- ProfileSource 非 Glimpseon 或联动开启 > 表格只读 数据来自 Linkage 桥

## 8 DownloadView — 软件下载页

- 数据源 DownloadCatalog.Categories > 分类卡片 (图标 名称 简介 官网/下载按钮)
- 下载 > new Downloader(progressCallback) > InstallSoftwareAsync 反射分发 (见 core-modules.md 8.5)

## 9 NotificationView — 通知页

- 左配置右预览 > 类型 (scroll/corner/fullscreen) 内容 速度 时长 颜色 TTS 参数 > 组装 NotificationRequest (字段见 core-modules.md 11.1)
- 分发逻辑 NotificationManager.HandleNotification 见 core-modules.md 11.4 > ScrollBannerMouseThrough 控制横幅穿透
- 公告列表经 Announcements (data/user/announcements.json) Changed 事件刷新

## 10 SettingsWindow — 设置窗口

- FluentWindow 多子页 覆盖主窗口/日志/其他/壁纸/外观/时间/一言/天气/倒计时/学校/快捷启动/媒体/联动/ClassWidgets/课表/网格/下载
- 控件绑定 > CardSpin/CardSwitch/CardCombo 直连 Config.Value 值变更即落盘并广播 ValueChanged
- 网格子页 > ShortSideCells/InsetPercent/ComponentCardOpacity/ComponentCardRadius 实时预览 (经 HomeView 订阅重建)
- RestartRequired 项保存后提示 RequestRestart (项清单见 configuration.md)

## 11 WizardWindow — 首次运行向导

- IsNeeded > data/config/Setup\_Wizard.json 不存在或 completed!=1
- Pages 五步 > 使用协议 (GPL-3.0 用户协议 隐私政策 OnAgreementChanged 控下一步) > 基本设置 (自启动 空闲自动打开 最大化 桌面快捷方式) > 外观 (主题 主色) > 完成页 > OnFinishClicked 写 completed=1

## 12 SplashWindow — 启动闪屏

- 构造 (版本号 图标路径) CenterOnScreen > UpdateStatus (locale splash.\*) SetProgress (DispatcherTimer 平滑推进 0-100) > 主流程驱动 关闭时机见 launch-flow.md

## 13 AboutView — 关于与更新

- 版本信息 (Paths.Version/BuildDate) 开源致谢 协议入口
- 更新公开入口 > CheckUpdateManual / CheckUpdateAuto > 下载 解压 备份 部署 脚本 全链路见 Versioning.md

## 14 Common.cs — 公共能力

- Common.Alert 宿主 Window 异步对话框 (单实例提示/确认场景)
- ParseAccentColor/ApplyAccentColor > 主题色解析写 FluentAvalonia 资源键
- AppFontFamily 全局字体 (通知窗口也引用)
- DPI 工具 / 自动打开 (空闲检测 GetIdleMilliseconds + 最大化) 逻辑挂靠点

## 15 DebugView — 调试页

- DebugMode 导航可见 > 配置自检结果 (SelfCheckAll) 日志开关 运行态探针

## 16 跨界面约定

- 主题链 > ThemeMode/ThemeColor ValueChanged > ApplyAccentColor > BindOpacity 刷新卡片
- 国际化 > 文案一律 Tr() 切语言 RestartRequired
- DPI > Config.DpiScale 主界面缩放 Auto 跟随系统 > 组件尺寸乘 DpiScale 落库
- 导航切页统一 Dispatcher.UIThread.Post 避免输入管线内同步重排
