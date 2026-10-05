# 目录结构

> [!NOTE]
> 编写者：HelloGaoo 最后修改：2026/10/05

## 1 仓库根目录

```
Glimpseon.slnx     
Glimpseon.csproj              
Glimpseon.cs                
build.bat                 
LICENSE                    
app-1.0.0/              
doc/ChangeLogs/             
```

## 2 版本目录 app-1.0.0/

```
GlimpseonMain.csproj          
GlimpseonMain.cs           
record.json                  
app.manifest               
core/ ui/ locale/ Assets/   
GlimpseonMain.exe/.dll       
```

## 3 core/ 核心模块

| 文件                 | 职责                                                |
| ------------------ | ------------------------------------------------- |
| AppConfig.cs       | ConfigItem<T> 与 Config 注册表 全部配置项声明                |
| AppUtils.cs        | 翻译 单实例 JSON 缓存 CacheRefresher 自启动 NTP 字体 主题色 HTTP |
| Paths.cs           | 包根/版本目录/data 子目录推导 record.json 版本读取               |
| Constants.cs       | 应用名 图标路径 资源路径 课表来源常量                              |
| Record.cs          | record.json 生成 加载 停用 文件哈希清单                       |
| Logging.cs         | 日志 滚动 清理 全局异常钩子                                   |
| Updater.cs         | GitHub Releases 检查 下载 解压 部署 更新脚本                  |
| Downloader.cs      | 通用下载 API 镜像源 静默安装 临时目录清理                          |
| Timetable.cs       | 课表档案模型 与 档案目录管理                                   |
| Linkage.cs         | 联动状态机 桥接基类 ClassIsland/ClassWidgets 桥 进程定位        |
| Notifications.cs   | 通知类型 公告存储 请求模型                                    |
| ComponentModels.cs | 组件定义 网格度量 页面模型 组件注册表                              |
| Win32/Native.cs    | P/Invoke 封装                                       |

## 4 core/Services/ 服务模块

| 文件                 | 数据源                                        |
| ------------------ | ------------------------------------------ |
| Weather.cs         | 小米天气 weatherapi.market.xiaomi.com          |
| Wallpaper.cs       | 多壁纸站 (wp.upx8.com / ltyuanfang / imlcd)    |
| Poetry.cs          | 一言 v1.hitokoto.cn (URL 可配)                 |
| Word.cs            | uapis.cn 每日一词                              |
| Sentence.cs        | api.timelessq.com 每日英语                     |
| News.cs            | 央视 api.xcvts.cn + news.orz.ai 多平台热榜        |
| History.cs         | tmini.net 历史上的今天                           |
| Almanac.cs         | lunar-csharp 本地计算黄历 农历                     |
| Media.cs           | 网易云 (xuanmou 网关) / 酷狗 / QQ 音乐 + GSMTC 系统播放 |
| DownloadCatalog.cs | 教学软件下载目录静态数据                               |

## 5 ui/ 界面模块

| 文件                                                              | 职责                                |
| --------------------------------------------------------------- | --------------------------------- |
| App.axaml(.cs)                                                  | Application 定义 挂接 RunStartupAsync |
| MainWindow                                                      | 导航壳 FluentaSideBar 页面切换           |
| HomeView                                                        | 主界面 壁纸背景 页面容器 底部栏                 |
| Components.axaml.cs                                             | 组件控件实现 + ComponentManager (最大文件)  |
| WallpaperView / TimetableView / DownloadView / NotificationView | 功能页                               |
| SettingsWindow                                                  | 设置 FluentWindow 多子页               |
| WizardWindow / SplashWindow                                     | 首次向导 / 启动闪屏                       |
| AboutView                                                       | 版本信息 与 更新 UI                      |
| DebugView                                                       | 调试页 (DebugMode 时入导航)              |
| Common.cs                                                       | 公共对话框 主题色 工具                      |

## 6 locale/ 与 Assets/

- locale: zh\_CN.json zh\_TW\.json en\_US.json 扁平 key 分组命名 (splash.\* timetable.\* wizard.\*) AppUtils.InitTranslation 启动载入 Tr(key, args) 取词
- Assets:

| 路径                    | 内容                     | 消费方                                                      |
| --------------------- | ---------------------- | -------------------------------------------------------- |
| font/\*.ttf           | HarmonyOS Sans 系列 6 字重 | csproj 以 AvaloniaResource 内嵌 AppUtils.InitializeFonts 注册 |
| city.db               | 城市坐标 SQLite            | RegionDatabase 天气经纬度回填                                   |
| wallpaper/default.jpg | 默认壁纸                   | 启动预加载第三级取图                                               |
| icons/                | 内置图标                   | 界面与组件引用                                                  |

## 7 运行期 data/ 详表

| 目录                       | 内容                                                | 写入方                                 |
| ------------------------ | ------------------------------------------------- | ----------------------------------- |
| config                   | config.json Setup\_Wizard.json home\_layout.json  | Config / WizardWindow / PageManager |
| log                      | 滚动日志                                              | Logging                             |
| cache                    | {name}.json 服务缓存                                  | AppUtils.SaveCache                  |
| temp/update temp/extract | 更新包                                               | Updater                             |
| profile                  | 档案配置-N.json                                       | Timetable                           |
| user                     | announcements.json notes/ qlgrid\_\* homework\_\* | 通知与组件                               |
| wallpaper                | wallpaper\_\*.jpg history.json                    | WallpaperService                    |
| icon classphotos notes   | 图标缓存 班级相册 手写数据                                    | 各组件                                 |

## 8 命名约定

- 配置 key > 组/项 PascalCase JSON 落盘同构 (Weather/UpdateInterval)
- locale key > 小写分组下划线 (dialog.instance\_running)
- 课表档案 > 档案配置-{N}.json 课表来源常量 Glimpseon/classisland/classwidgets
- 日志 tag > \[模块名] 前缀 (\[PRELOAD] \[CM] \[PageManager])

