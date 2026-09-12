# Glimpseon 1.0.0 开发者文档

> \[!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

Glimpseon 是一款基于 PyQt6 Fluent Widgets 的 Windows 桌面组件信息看板

本目录是面向开发者 / 维护者的技术文档。建议按下列顺序阅读：

## 文档索引

| 文档                             | 内容                       |
| ------------------------------ | ------------------------ |
| [架构总览](architecture.md)        | 整体结构、模块职责                |
| [快速开始](getting-started.md)     | 环境配置、运行打包                |
| [目录结构](directory-structure.md) | 源码与资源目录详解                |
| [核心模块](core-modules.md)        | `core/` 下各模块实现           |
| [UI 模块](ui-modules.md)         | `ui/` 下各界面实现             |
| [服务模块](services.md)            | `services/` 下数据获取服务      |
| [原生扩展](native-extension.md)    | `glimpseon_native/` C++  |
| [配置系统](configuration.md)       | `QConfig` 配置项全表与机制       |
| [启动流程](launch-flow.md)         | 从启动器到主窗口的完整时序            |
| [组件系统](component-system.md)    | 网格布局、组件定义与编辑模式           |

## 分节目录

**[架构总览](architecture.md)**

[1. 技术栈](architecture.md#1-技术栈) · [2. 分层结构](architecture.md#2-分层结构) · [3. 设计原则](architecture.md#3-设计原则) · [4. 数据处理（以壁纸为例）](architecture.md#4-数据处理以壁纸为例) · [5. 全局对象](architecture.md#5-全局对象) · [6. 外部联动](architecture.md#6-外部联动) · [7. 数据目录](architecture.md#7-数据目录)

**[快速开始](getting-started.md)**

[1. 环境要求](getting-started.md#1-环境要求) · [2. 获取代码](getting-started.md#2-获取代码) · [3. 安装依赖](getting-started.md#3-安装依赖) · [4. 运行](getting-started.md#4-运行) · [方式 A：通过启动器](getting-started.md#方式-a通过启动器) · [方式 B：直接运行主程序](getting-started.md#方式-b直接运行主程序) · [5. 首次运行](getting-started.md#5-首次运行) · [6. 打包（PyInstaller）](getting-started.md#6-打包pyinstaller) · [7. 调试技巧](getting-started.md#7-调试技巧) · [8. 常见问题](getting-started.md#8-常见问题)

**[目录结构](directory-structure.md)**

[1. 仓库根目录](directory-structure.md#1-仓库根目录) · [2. 应用版本目录 `app-1.0.0/`](directory-structure.md#2-应用版本目录-app-100) · [3. `core/` 核心模块](directory-structure.md#3-core-核心模块) · [4. `ui/` 界面模块](directory-structure.md#4-ui-界面模块) · [5. `services/` 数据服务](directory-structure.md#5-services-数据服务) · [6. `resource/` 静态资源](directory-structure.md#6-resource-静态资源) · [7. `locale/` 国际化](directory-structure.md#7-locale-国际化) · [8. `glimpseon_native/` 原生扩展](directory-structure.md#8-glimpseon_native-原生扩展) · [9. 运行期数据目录 `data/`](directory-structure.md#9-运行期数据目录-data) · [10. 命名约定](directory-structure.md#10-命名约定)

**[核心模块](core-modules.md)**

[1. paths.py — 路径推导](core-modules.md#1-pathspy--路径推导) · [1.1 路径推导逻辑](core-modules.md#11-路径推导逻辑) · [1.2 关键函数](core-modules.md#12-关键函数) · [1.3 注意](core-modules.md#13-注意) · [2. constants.py — 常量与 QSS 加载](core-modules.md#2-constantspy--常量与-qss-加载) · [2.1 主要常量](core-modules.md#21-主要常量) · [2.2 QSS 缓存机制](core-modules.md#22-qss-缓存机制) · [3. config.py — 配置管理](core-modules.md#3-configpy--配置管理) · [3.1 核心类](core-modules.md#31-核心类) · [3.2 配置分组（节）](core-modules.md#32-配置分组节) · [3.3 自动保存机制](core-modules.md#33-自动保存机制) · [3.4 API](core-modules.md#34-api) · [4. logger.py — 日志系统](core-modules.md#4-loggerpy--日志系统) · [4.1 日志格式](core-modules.md#41-日志格式) · [4.2 关键类与函数](core-modules.md#42-关键类与函数) · [4.3 异常钩子（init\_exhook 安装）](core-modules.md#43-异常钩子init_exhook-安装) · [4.4 子模块日志器约定](core-modules.md#44-子模块日志器约定) · [4.5 系统上下文](core-modules.md#45-系统上下文) · [5. utils.py — 工具函数](core-modules.md#5-utilspy--工具函数) · [5.1 单实例管理](core-modules.md#51-单实例管理) · [5.2 字体](core-modules.md#52-字体) · [5.3 缓存](core-modules.md#53-缓存) · [5.4 资源解包](core-modules.md#54-资源解包) · [5.5 自启动](core-modules.md#55-自启动) · [5.6 翻译系统](core-modules.md#56-翻译系统) · [5.7 精确时间](core-modules.md#57-精确时间) · [5.8 Fluent 图标命名空间](core-modules.md#58-fluent-图标命名空间) · [6. component.py — 组件系统](core-modules.md#6-componentpy--组件系统) · [6.1 数据类](core-modules.md#61-数据类) · [6.2 服务类](core-modules.md#62-服务类) · [6.3 内置组件](core-modules.md#63-内置组件) · [7. notification.py — 通知系统](core-modules.md#7-notificationpy--通知系统) · [7.1 通知类型](core-modules.md#71-通知类型) · [7.2 弹窗基类](core-modules.md#72-弹窗基类) · [7.3 弹窗实现](core-modules.md#73-弹窗实现) · [7.4 管理器](core-modules.md#74-管理器) · [8. downloader.py — 文件下载](core-modules.md#8-downloaderpy--文件下载) · [8.1 main](core-modules.md#81-main) · [8.2 安装方法约定](core-modules.md#82-安装方法约定) · [8.3 外部工具](core-modules.md#83-外部工具) · [9. linkage.py — 外部联动](core-modules.md#9-linkagepy--外部联动) · [9.1 核心类型](core-modules.md#91-核心类型) · [9.2 机制](core-modules.md#92-机制) · [10. timetable.py — 课表配置](core-modules.md#10-timetablepy--课表配置) · [11. updater.py — 软件更新](core-modules.md#11-updaterpy--软件更新) · [11.1 端点](core-modules.md#111-端点) · [11.2 流程函数](core-modules.md#112-流程函数) · [12. record.py — 版本记录](core-modules.md#12-recordpy--版本记录) · [12.1 数据结构](core-modules.md#121-数据结构) · [12.2 函数](core-modules.md#122-函数)

**[UI 模块](ui-modules.md)**

[1. common.py — 公共基类](ui-modules.md#1-commonpy--公共基类) · [2. home.py — 主界面](ui-modules.md#2-homepy--主界面) · [2.1 HomeInterface](ui-modules.md#21-homeinterface) · [2.2 辅助控件](ui-modules.md#22-辅助控件) · [3. component.py — 组件实现库](ui-modules.md#3-componentpy--组件实现库) · [3.1 核心基类与管理](ui-modules.md#31-核心基类与管理) · [3.2 编辑模式约定](ui-modules.md#32-编辑模式约定) · [3.3 内置组件清单](ui-modules.md#33-内置组件清单) · [3.4 媒体组件](ui-modules.md#34-媒体组件) · [3.5 手写画板（擦除）](ui-modules.md#35-手写画板擦除) · [3.6 HTML 渲染组件](ui-modules.md#36-html-渲染组件) · [3.7 公用图标提取](ui-modules.md#37-公用图标提取) · [4. wallpaper.py — 壁纸管理](ui-modules.md#4-wallpaperpy--壁纸管理) · [4.1 主要类](ui-modules.md#41-主要类) · [4.2 关键能力](ui-modules.md#42-关键能力) · [5. notification.py — 通知管理](ui-modules.md#5-notificationpy--通知管理) · [6. timetable.py — 课程表](ui-modules.md#6-timetablepy--课程表) · [7. download.py — 软件下载](ui-modules.md#7-downloadpy--软件下载) · [7.1 结构](ui-modules.md#71-结构) · [7.2 数据与渲染](ui-modules.md#72-数据与渲染) · [7.3 页面内交互](ui-modules.md#73-页面内交互) · [7.4 下载流程](ui-modules.md#74-下载流程) · [7.5 线程](ui-modules.md#75-线程) · [7.6 新增可下载软件](ui-modules.md#76-新增可下载软件) · [8. settings.py — 设置窗口](ui-modules.md#8-settingspy--设置窗口) · [8.1 自定义设置卡片](ui-modules.md#81-自定义设置卡片) · [8.2 设置子页](ui-modules.md#82-设置子页) · [8.3 SettingsWindow](ui-modules.md#83-settingswindow) · [9. about.py — 关于](ui-modules.md#9-aboutpy--关于) · [10. debug.py — 调试面板](ui-modules.md#10-debugpy--调试面板) · [11. 跨界面约定](ui-modules.md#11-跨界面约定) · [11.1 主题切换链](ui-modules.md#111-主题切换链) · [11.2 国际化](ui-modules.md#112-国际化) · [11.3 QSS 映射表](ui-modules.md#113-qss-映射表) · [11.4 整页 HTML 界面（QWebChannel）](ui-modules.md#114-整页-html-界面qwebchannel)

**[服务模块](services.md)**

[1. weather.py — 天气服务](services.md#1-weatherpy--天气服务) · [1.1 数据源](services.md#11-数据源) · [1.2 主要类](services.md#12-主要类) · [1.3 天气代码体系](services.md#13-天气代码体系) · [1.4 刷新与缓存](services.md#14-刷新与缓存) · [2. poetry.py — 一言服务](services.md#2-poetrypy--一言服务) · [2.1 配置](services.md#21-配置) · [2.2 PoetryService](services.md#22-poetryservice) · [3. news.py — 新闻服务](services.md#3-newspy--新闻服务) · [3.1 数据源](services.md#31-数据源) · [3.2 NewsService](services.md#32-newsservice) · [4. media.py — 媒体服务](services.md#4-mediapy--媒体服务) · [4.1 数据模型](services.md#41-数据模型) · [4.2 媒体源](services.md#42-媒体源) · [4.3 模块路由](services.md#43-模块路由) · [4.4 与 UI 协作](services.md#44-与-ui-协作) · [5. history.py — 历史上的今天服务](services.md#5-historypy--历史上的今天服务) · [5.1 数据源](services.md#51-数据源) · [5.2 HistoryService](services.md#52-historyservice) · [6. word.py — 每日单词服务](services.md#6-wordpy--每日单词服务) · [6.1 数据源](services.md#61-数据源) · [6.2 WordService](services.md#62-wordservice) · [7. sentence.py — 每日英语服务](services.md#7-sentencepy--每日英语服务) · [7.1 数据源](services.md#71-数据源) · [7.2 SentenceService](services.md#72-sentenceservice) · [8. 跨服务约定](services.md#8-跨服务约定) · [8.1 缓存](services.md#81-缓存) · [8.2 日志](services.md#82-日志) · [8.3 预加载](services.md#83-预加载) · [8.4 错误处理](services.md#84-错误处理)

**[原生扩展](native-extension.md)**

[1. 构建](native-extension.md#1-构建) · [1.1 构建步骤](native-extension.md#11-构建步骤) · [1.2 依赖](native-extension.md#12-依赖) · [1.3 注意](native-extension.md#13-注意) · [2. 导出 API（PYBIND11_MODULE）](native-extension.md#2-导出-apipybind11_module) · [3. wallpaper.cpp — 桌面壁纸](native-extension.md#3-wallpapercpp--桌面壁纸) · [4. image.cpp — Direct2D 高斯模糊](native-extension.md#4-imagecpp--direct2d-高斯模糊) · [4.1 管线](native-extension.md#41-管线) · [4.2 设备初始化（ensure\_init）](native-extension.md#42-设备初始化ensure_init) · [4.3 关键参数](native-extension.md#43-关键参数) · [4.4 Python 入口](native-extension.md#44-python-入口) · [5. hook.cpp — 全局输入钩子](native-extension.md#5-hookcpp--全局输入钩子) · [5.1 用途](native-extension.md#51-用途) · [5.2 实现](native-extension.md#52-实现) · [5.3 生命周期](native-extension.md#53-生命周期) · [6. sys.cpp — 系统工具](native-extension.md#6-syscpp--系统工具) · [6.1 空闲检测](native-extension.md#61-空闲检测) · [6.2 单例互斥锁](native-extension.md#62-单例互斥锁) · [6.3 字体安装](native-extension.md#63-字体安装) · [6.4 图标提取](native-extension.md#64-图标提取)

**[配置系统](configuration.md)**

[1. 机制](configuration.md#1-机制) · [1.1 配置项类型](configuration.md#11-配置项类型) · [1.2 声明形式](configuration.md#12-声明形式) · [1.3 自动保存](configuration.md#13-自动保存) · [1.4 全局对象](configuration.md#14-全局对象) · [1.5 主题变更信号](configuration.md#15-主题变更信号) · [2. 配置项全表](configuration.md#2-配置项全表) · [MainWindow](configuration.md#mainwindow) · [Log](configuration.md#log) · [Wallpaper](configuration.md#wallpaper) · [Appearance](configuration.md#appearance) · [Time](configuration.md#time) · [Poetry](configuration.md#poetry) · [Weather](configuration.md#weather) · [Countdown](configuration.md#countdown) · [School](configuration.md#school) · [QuickLaunch](configuration.md#quicklaunch) · [Media](configuration.md#media) · [Linkage（ClassIsland）](configuration.md#linkageclassisland) · [ClassWidgets](configuration.md#classwidgets) · [Timetable](configuration.md#timetable) · [PreciseTime](configuration.md#precisetime) · [Grid](configuration.md#grid) · [Download](configuration.md#download) · [Other](configuration.md#other) · [3. 默认配置](configuration.md#3-默认配置) · [4. 使用示例](configuration.md#4-使用示例) · [5. 序列化器](configuration.md#5-序列化器)

**[启动流程](launch-flow.md)**

[1. 阶段 0：启动器（Glimpseon.py）](launch-flow.md#1-阶段-0启动器glimpseonpy) · [2. 阶段 1：主程序初始化（GlimpseonMain.py 顶部）](launch-flow.md#2-阶段-1主程序初始化glimpseonmainpy-顶部) · [3. 阶段 2：`__main__` 入口](launch-flow.md#3-阶段-2__main__-入口) · [3.1 QApplication 与线程池](launch-flow.md#31-qapplication-与线程池) · [3.2 向导（首次运行）](launch-flow.md#32-向导首次运行) · [3.3 闪屏显示](launch-flow.md#33-闪屏显示) · [3.4 后台初始化任务](launch-flow.md#34-后台初始化任务) · [3.5 单实例检查](launch-flow.md#35-单实例检查) · [3.6 字体初始化](launch-flow.md#36-字体初始化) · [3.7 日志配置](launch-flow.md#37-日志配置) · [4. 阶段 3：主窗口创建](launch-flow.md#4-阶段-3主窗口创建) · [4.1 MainWindow\.__init__](launch-flow.md#41-mainwindow__init__) · [4.2 导航注册（\_initNavigation）](launch-flow.md#42-导航注册_initnavigation) · [5. 阶段 4：预加载（Preloader）](launch-flow.md#5-阶段-4预加载preloader) · [5.1 \_load\_wp（壁纸）](launch-flow.md#51-_load_wp壁纸) · [5.2 \_load\_wt（天气）](launch-flow.md#52-_load_wt天气) · [5.3 \_load\_po（一言）](launch-flow.md#53-_load_po一言) · [5.4 主线程槽](launch-flow.md#54-主线程槽) · [5.5 自动更新检查](launch-flow.md#55-自动更新检查) · [5.6 等待预加载](launch-flow.md#56-等待预加载) · [6. 阶段 5：收尾](launch-flow.md#6-阶段-5收尾) · [7. 启动耗时埋点](launch-flow.md#7-启动耗时埋点) · [8. 关键约束](launch-flow.md#8-关键约束)

**[组件系统](component-system.md)**

[1. 数据模型（core/component.py）](component-system.md#1-数据模型corecomponentpy) · [1.1 ResizeMode](component-system.md#11-resizemode) · [1.2 ComponentDefinition](component-system.md#12-componentdefinition) · [1.3 GridSettings / GridMetrics](component-system.md#13-gridsettings--gridmetrics) · [1.4 PageMeta / PageManager](component-system.md#14-pagemeta--pagemanager) · [2. 网格布局算法（GridLayoutService）](component-system.md#2-网格布局算法gridlayoutservice) · [2.1 calculate\_grid\_metrics](component-system.md#21-calculate_grid_metrics) · [2.2 坐标换算](component-system.md#22-坐标换算) · [2.3 碰撞检测](component-system.md#23-碰撞检测) · [3. 组件注册（ComponentRegistry）](component-system.md#3-组件注册componentregistry) · [3.1 内置组件](component-system.md#31-内置组件) · [4. UI 实现层（ui/component.py）](component-system.md#4-ui-实现层uicomponentpy) · [4.1 类层次](component-system.md#41-类层次) · [4.2 DraggableWidget 编辑能力](component-system.md#42-draggablewidget-编辑能力) · [4.3 ComponentManager](component-system.md#43-componentmanager) · [4.4 ComponentConfigDialog](component-system.md#44-componentconfigdialog) · [4.5 组件库窗口](component-system.md#45-组件库窗口) · [5. 编辑模式交互](component-system.md#5-编辑模式交互) · [5.1 进入/退出](component-system.md#51-进入退出) · [5.2 拖拽与缩放](component-system.md#52-拖拽与缩放) · [5.3 统一卡片背景（DraggableContainer）](component-system.md#53-统一卡片背景draggablecontainer) · [5.4 多页与页面状态](component-system.md#54-多页与页面状态) · [6. 手写画板（WritingPadComponent）](component-system.md#6-手写画板writingpadcomponent) · [6.1 分层结构与渲染](component-system.md#61-分层结构与渲染) · [6.2 定时器分工](component-system.md#62-定时器分工) · [6.3 定时器循环而非事件驱动](component-system.md#63-定时器循环而非事件驱动) · [6.4 撤回与重建](component-system.md#64-撤回与重建) · [7. 媒体组件](component-system.md#7-媒体组件) · [7.1 双定时器](component-system.md#71-双定时器) · [7.2 线程化抓取](component-system.md#72-线程化抓取) · [7.3 封面来源优先级](component-system.md#73-封面来源优先级) · [7.4 切歌竞态保护](component-system.md#74-切歌竞态保护) · [7.5 缓存与播放控制](component-system.md#75-缓存与播放控制) · [8. 持久化与加载流程](component-system.md#8-持久化与加载流程) · [9. 扩展新组件](component-system.md#9-扩展新组件) · [9.1 需要理解的组件配置](component-system.md#91-需要理解的组件配置) · [9.2 组件数据结构](component-system.md#92-组件数据结构) · [9.3 新增步骤](component-system.md#93-新增步骤) · [9.4 验证](component-system.md#94-验证) · [9.5 常见坑](component-system.md#95-常见坑)

<br />

## 项目入口

- 顶层启动器：[Glimpseon.py](https://github.com/HelloGaoo/Glimpseon/blob/main/Glimpseon.py)
- 主程序：[GlimpseonMain.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/GlimpseonMain.py)

## 版本

版本号与构建日期来自 `app-1.0.0/record.json`，由 `core/paths.py` 读取为 `VERSION` / `BUILD_DATE`。

## 鸣谢

本项目开发者对所有开源项目及其代码贡献者表示感谢。完整名单见 [`resource/credits.json`](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/resource/credits.json)。

（可能出现纰漏，以实际为准）

### 参考项目

本软件在开发过程中参考了以下开源项目的架构、算法或实现：

| 项目                                                                                                | 许可证                             | 版权信息 / 说明                             |
| :------------------------------------------------------------------------------------------------ | :------------------------------ | :------------------------------------ |
| [`Alan-CRL/Inkeys`](https://github.com/Alan-CRL/Inkeys)                                           | GNU General Public License v3.0 | Copyright © 2023-2025 AlanCRL（陈润林）工作室 |
| [`HelloGaoo/SeevvoDownloader`](https://github.com/HelloGaoo/SeevvoDownloader)                     | GNU General Public License v3.0 | Copyright © 2026 HelloGaoo,WHYOS      |
| [`ClassIsland/ClassIsland`](https://github.com/ClassIsland/ClassIsland)                           | GNU General Public License v3.0 | 参见项目文档                                |
| [`Class-Widgets/Class-Widgets`](https://github.com/Class-Widgets/Class-Widgets)                   | GNU General Public License v3.0 | Copyright © 2025 RinLit               |
| [`Kxnrl/NetEase-Cloud-Music-DiscordRPC`](https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC) | MIT License                     | Copyright (c) 2018 Kyle               |

### 第三方开源组件

| 组件名称                                                                                                            | 许可证                       | 版权信息 / 说明                         |
| :-------------------------------------------------------------------------------------------------------------- | :------------------------ | :-------------------------------- |
| [`pybind/pybind11`](https://github.com/pybind/pybind11)                                                         | BSD-3-Clause              | C++ （`glimpseon_native`）Python 绑定 |
| [`riverbankcomputing/PyQt6`](https://www.riverbankcomputing.com/software/pyqt/)                                 | GPL-3.0 / Commercial      | UI 框架                             |
| [`zhiyiYo/PyQt-Fluent-Widgets`](https://github.com/zhiyiYo/PyQt-Fluent-Widgets)                                 | GPL-3.0                   | UI 组件库                            |
| [`zhiyiYo/PyQt-Frameless-Window`](https://github.com/zhiyiYo/PyQt-Frameless-Window)                             | GPL-3.0                   | 无边框窗口                             |
| [`pytorch/pytorch`](https://github.com/pytorch/pytorch)                                                         | BSD-3-Clause              | EasyOCR 推理后端                      |
| [`pytorch/vision`](https://github.com/pytorch/vision)                                                           | BSD-3-Clause              | torchvision，EasyOCR 依赖            |
| [`JaidedAI/EasyOCR`](https://github.com/JaidedAI/EasyOCR)                                                       | Apache License 2.0        | OCR 识别                            |
| [`madmub/pytesseract`](https://github.com/madmub/pytesseract)                                                   | Apache License 2.0        | Tesseract OCR 封装                  |
| [`opencv/opencv-python`](https://github.com/opencv/opencv-python)                                               | Apache License 2.0        | 图像处理（opencv-python-headless）      |
| [`python-pillow/Pillow`](https://github.com/python-pillow/Pillow)                                               | HPND                      | 图像处理                              |
| [`scikit-image/scikit-image`](https://github.com/scikit-image/scikit-image)                                     | BSD-3-Clause              | 图像处理                              |
| [`numpy/numpy`](https://github.com/numpy/numpy)                                                                 | BSD-3-Clause              | 数值计算                              |
| [`scipy/scipy`](https://github.com/scipy/scipy)                                                                 | BSD-3-Clause              | 科学计算                              |
| [`networkx/networkx`](https://github.com/networkx/networkx)                                                     | BSD-3-Clause              | 图计算                               |
| [`sympy/sympy`](https://github.com/sympy/sympy)                                                                 | BSD-3-Clause              | 符号计算                              |
| [`shapely/shapely`](https://github.com/shapely/shapely)                                                         | BSD-3-Clause              | 几何计算                              |
| [`psf/requests`](https://github.com/psf/requests)                                                               | Apache License 2.0        | HTTP 请求                           |
| [`urllib3/urllib3`](https://github.com/urllib3/urllib3)                                                         | MIT License               | HTTP 底层                           |
| [`certifi/python-certifi`](https://github.com/certifi/python-certifi)                                           | MPL-2.0                   | CA 证书                             |
| [`Ousret/charset_normalizer`](https://github.com/Ousret/charset_normalizer)                                     | MIT License               | 字符编码检测                            |
| [`idna`](https://github.com/kjd/idna)                                                                           | BSD-3-Clause              | 国际化域名                             |
| [`pyinstaller/pyinstaller`](https://github.com/pyinstaller/pyinstaller)                                         | GPL-2.0+                  | 打包                                |
| [`pyinstaller/pyinstaller-hooks-contrib`](https://github.com/pyinstaller/pyinstaller-hooks-contrib)             | Apache License 2.0        | PyInstaller hooks                 |
| [`ronaldoussoren/altgraph`](https://github.com/ronaldoussoren/altgraph)                                         | MIT License               | PyInstaller 依赖                    |
| [`erocarrera/pefile`](https://github.com/erocarrera/pefile)                                                     | MIT License               | PE 文件解析                           |
| [`enthought/pywin32-ctypes`](https://github.com/enthought/pywin32-ctypes)                                       | PSF-2.0                   | PyInstaller 依赖                    |
| [`mhammond/pywin32`](https://github.com/mhammond/pywin32)                                                       | PSF-2.0                   | Windows API                       |
| [`enthought/comtypes`](https://github.com/enthought/comtypes)                                                   | MIT License               | COM 接口                            |
| [`yinkaisheng/Python-UIAutomation-for-Windows`](https://github.com/yinkaisheng/Python-UIAutomation-for-Windows) | MIT License               | UI 自动化                            |
| [`pywinrt/python-winsdk`](https://github.com/pywinrt/python-winsdk)                                             | MIT License               | Windows SDK 绑定                    |
| [`AndreMiras/pycaw`](https://github.com/AndreMiras/pycaw)                                                       | MIT License               | 音频会话控制                            |
| [`ABUCKY0/py-now-playing`](https://github.com/ABUCKY0/py-now-playing)                                           | GPL-3.0                   | 媒体播放状态                            |
| [`srounet/Pymem`](https://github.com/srounet/Pymem)                                                             | MIT License               | 进程内存读写                            |
| [`giampaolo/psutil`](https://github.com/giampaolo/psutil)                                                       | BSD-3-Clause              | 系统进程信息                            |
| [`miurahr/py7zr`](https://github.com/miurahr/py7zr)                                                             | LGPLv2+                   | 7z 压缩                             |
| [`miurahr/pyppmd`](https://github.com/miurahr/pyppmd)                                                           | LGPLv2+                   | PPMD 压缩                           |
| [`miurahr/pybcj`](https://github.com/miurahr/pybcj)                                                             | LGPLv2+                   | BCJ 过滤器                           |
| [`miurahr/inflate64`](https://github.com/miurahr/inflate64)                                                     | LGPLv2+                   | inflate64 解压                      |
| [`miurahr/multivolume`](https://github.com/miurahr/multivolume)                                                 | LGPLv2+                   | 多卷归档                              |
| [`Legrandin/pycryptodome`](https://github.com/Legrandin/pycryptodome)                                           | Unlicense / BSD-2-Clause  | 加密                                |
| [`fonttools/pyclipper`](https://github.com/fonttools/pyclipper)                                                 | Custom (based on Clipper) | 多边形裁剪                             |
| [`MeirKrihavi/python-bidi`](https://github.com/MeirKrihavi/python-bidi)                                         | LGPL                      | 双向文本                              |
| [`pallets/jinja`](https://github.com/pallets/jinja)                                                             | BSD-3-Clause              | 模板引擎                              |
| [`pallets/markupsafe`](https://github.com/pallets/markupsafe)                                                   | BSD-3-Clause              | HTML 转义                           |
| [`yaml/pyyaml`](https://github.com/yaml/pyyaml)                                                                 | MIT License               | YAML 解析                           |
| [`CNlyl/cnlunar`](https://github.com/CNlyl/cnlunar)                                                             | MIT License               | 农历计算                              |
| [`fengsp/color-thief-py`](https://github.com/fengsp/color-thief-py)                                             | BSD-3-Clause              | 主色提取                              |
| [`albertosottile/darkdetect`](https://github.com/albertosottile/darkdetect)                                     | BSD-3-Clause              | 系统暗色检测                            |
| [`foutaise/texttable`](https://github.com/foutaise/texttable)                                                   | MIT License               | 文本表格                              |
| [`imageio/imageio`](https://github.com/imageio/imageio)                                                         | BSD-2-Clause              | 图像 IO                             |
| [`cgohlke/tifffile`](https://github.com/cgohlke/tifffile)                                                       | BSD-3-Clause              | TIFF 文件                           |
| [`scikit-build/ninja`](https://github.com/scikit-build/ninja)                                                   | Apache License 2.0        | 构建系统                              |
| [`scientific-python/lazy_loader`](https://github.com/scientific-python/lazy_loader)                             | BSD-3-Clause              | 懒加载                               |
| [`pycontribs/filelock`](https://github.com/pycontribs/filelock)                                                 | BSD-3-Clause              | 文件锁                               |
| [`fsspec/filesystem_spec`](https://github.com/fsspec/filesystem_spec)                                           | BSD-3-Clause              | 文件系统抽象                            |
| [`mpmath/mpmath`](https://github.com/mpmath/mpmath)                                                             | BSD-3-Clause              | 任意精度运算                            |
| [`python/typing_extensions`](https://github.com/python/typing_extensions)                                       | PSF-2.0                   | 类型扩展                              |
| [`google/brotli`](https://github.com/google/brotli)                                                             | MIT License               | Brotli 压缩                         |
| [`rgommers/backports.zstd`](https://github.com/rgommers/backports.zstd)                                         | BSD / GPLv2+              | Zstandard 压缩                      |
| [`pypa/packaging`](https://github.com/pypa/packaging)                                                           | Apache-2.0 / BSD          | 打包工具                              |

> \[!NOTE]
> 以上许可证信息仅为摘要，各组件的具体权利义务以其随附的许可证文本为准。各组件的商标与名称归其各自所有者所有，本软件不主张对任何第三方组件的所有权。

