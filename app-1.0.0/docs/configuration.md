# 配置系统

> [!NOTE]
> 编写者：HelloGaoo 最后修改：2026/10/05

## 1 机制

- 落盘文件 > data/config/config.json 两层结构 组>项 与代码声明同构
- ConfigItem<T> 构造即注册进 Config 静态注册表 ItemCount 即全表项数
- 枚举经自定义序列化器落字符串 (ThemeMode>light/dark/auto Language>zh_CN/zh_TW/en_US/Auto)
- 写入校验 Clamp > 数值 min/max 截断 枚举/白名单 options 越界回默认值并 Warning
- Value setter > 值未变短路 > 变更即 Log.Info > ValueChanged 广播 > Config.Save 全量重写 (锁串行)
- Load > 逐项 LoadFrom 单项解析失败回默认值不中断 > 载入后立即 Save 补齐缺项
- RestartRequired=true 的项变更后由设置页触发 RequestRestart (语言 DPI 日志级别 GPU)
- 自检 > SelfCheckRoundTrip 以当前值/默认值/全部选项/布尔取反/数值边界做序列化往返 DebugMode 启动时 SelfCheckAll 输出失败清单

## 2 配置项全表 (core/AppConfig.cs)

### MainWindow

| 项 | 类型 | 默认 | 说明 |
|---|---|---|---|
| ThemeMode | 枚举 | auto | light/dark/auto |
| ThemeColor | string | #30c361 | 主色 hex |
| DpiScale | string | Auto | Auto/1/1.25/1.5/1.75/2 重启生效 |
| Language | 枚举 | Auto | zh_CN/zh_TW/en_US 重启生效 |

### Log

| 项 | 类型 | 默认 | 约束 |
|---|---|---|---|
| LogLevel | 枚举 | Info | Debug/Info/Warning/Error 重启生效 |
| DisableLog | bool | false | 重启生效 |
| MaxCount | int | 50 | 10-500 |
| MaxDays | int | 30 | 30-365 |

### Other

| 项 | 类型 | 默认 | 说明 |
|---|---|---|---|
| CloseAction | string | minimize | minimize/close |
| AllowMultipleInstances | bool | false | 跳过单实例锁 |
| DebugMode | bool | false | 调试态 |
| EnableGpuAcceleration | bool | true | AngleEgl>Software 重启生效 |
| AutoStart | bool | false | 对齐注册表 Run |
| AutoOpenOnIdle | bool | false | 空闲自动打开 |
| IdleMinutes | int | 5 | 1-60 |
| AutoOpenMaximize | bool | false | 自动打开时最大化 |
| AutoCheckUpdate | bool | true | 启动检查更新 |
| AutoUpdate | bool | false | 自动下载部署 含备份 |
| UpdateChannel | string | stable | stable/beta |
| MinimizeNotificationCount | int | 0 | 最小化提示计数 |
| ScrollBannerBgHeight | int | 80 | 40-300 |
| ScrollBannerMouseThrough | bool | true | 横幅鼠标穿透 |

### Wallpaper

| 项 | 类型 | 默认 | 约束 |
|---|---|---|---|
| SaveLimit | int | 50 | 10-100 |
| AutoGetInterval | string | 30m | never/10m/30m/1h/3h/6h/12h/1d/3d/5d/7d |
| AutoSyncToDesktop | bool | true | 写系统桌面 |
| WallpaperApi | string | wp.upx8.com | 五站点白名单 |
| Brightness | int | 0 | -100-0 |

### Appearance / Time

| 项 | 类型 | 默认 | 约束 |
|---|---|---|---|
| Appearance/BackgroundBlurRadius | int | 0 | 0-30 |
| Time/ShowClock | bool | true | |
| Time/ShowClockSeconds | bool | true | |
| Time/ShowLunarCalendar | bool | true | |
| Time/ClockColor | string | #FFFFFF | |
| Time/ClockSize | int | 80 | 40-120 |
| Time/DateSize | int | 16 | 10-40 |
| Time/TimeOffset | int | 0 | -9999-9999 秒 |
| Time/AutoTimeOffsetEnabled | bool | false | 自动时移 |
| Time/AutoTimeOffsetIncrement | int | 1 | -9999-9999 |

### Poetry / Weather

| 项 | 类型 | 默认 | 约束 |
|---|---|---|---|
| Poetry/ShowPoetry | bool | true | |
| Poetry/ApiUrl | string | https://v1.hitokoto.cn/ | 可换源 |
| Poetry/UpdateInterval | string | 10m | never/5m/10m/30m/1h/3h/6h/12h/1d |
| Poetry/Size int | | 16 | 12-50 |
| Poetry/TextColor | string | #FFFFFF | |
| Weather/ShowWeather | bool | true | |
| Weather/Size | int | 24 | 5-50 |
| Weather/TextColor | string | #FFFFFF | |
| Weather/IconSize | int | 64 | 32-200 |
| Weather/UpdateInterval | string | 5m | never/5m/15m/30m/1h/3h/6h/12h/24h |
| Weather/City | string | 北京市 | 经 RegionDatabase 回填坐标 |
| Weather/Source | string | city | city/coords |
| Weather/Latitude | double | 39.9042 | |
| Weather/Longitude | double | 116.4074 | |
| Weather/Unit | string | c | c/f |
| Weather/AlertExcluded | string | 空 | 排除的预警类型 |

### Countdown / School / QuickLaunch

| 项 | 类型 | 默认 | 约束 |
|---|---|---|---|
| Countdown/ShowCountdown | bool | true | |
| Countdown/DisplayMode | string | simultaneous | simultaneous/carousel |
| Countdown/TextColor | string | #FF0000 | |
| Countdown/TextSize | int | 35 | 12-120 |
| Countdown/ConnectorColor | string | #FFFFFF | |
| Countdown/ConnectorSize | int | 35 | 12-60 |
| Countdown/CarouselInterval | int | 5 | 1-60 |
| Countdown/CountdownList | JsonElement | [] | 数组 |
| School/School | string | 空 | |
| School/Class | string | 空 | |
| School/ShowSchoolInfo | bool | false | |
| School/SchoolInfoTextColor | string | #FFFFFF | |
| School/SchoolInfoTextSize | int | 34 | 12-60 |
| QuickLaunch/ShowQuickLaunch | bool | true | |
| QuickLaunch/QuickLaunchApps | JsonElement | [] | |
| QuickLaunch/IconSize | int | 64 | 32-96 |
| QuickLaunch/IconSpacing | int | 12 | 4-40 |
| QuickLaunch/ShowLabels | bool | true | |
| QuickLaunch/OffsetY | int | 60 | 0-120 |

### Media

| 项 | 类型 | 默认 | 约束 |
|---|---|---|---|
| ShowMediaInfo / ShowMediaCover / ShowMediaLyrics | bool | true | 三开关 |
| UpdateInterval | int | 1 | 1-5 秒 |
| TextSize / CoverSize / LyricsSize / LyricsLines | int | 14/56/12/3 | 10-28 / 32-128 / 8-24 / 1-7 |
| Width / Height | int | 360/160 | 200-800 / 100-300 |
| LyricsAdvance | int | 300 | 0-2000ms |
| UseCustomBg / BgOpacity / BorderRadius | | false/60/12 | 0-100 / 0-30 |
| TitleColor / ArtistColor / TimeColor / LyricsColor | string | 白系 | |
| CoverBorderRadius / CoverBorderColor | | 10 / #FFFFFF20 | 0-20 |

### Linkage / ClassWidgets / Timetable / PreciseTime

| 项 | 类型 | 默认 | 约束 |
|---|---|---|---|
| Linkage/Enabled | bool | false | ClassIsland 联动 |
| Linkage/DataPath | string | 空 | 手动指定数据目录 |
| Linkage/PollInterval | int | 5 | 1-30 秒 |
| Linkage/SyncTimeConfig | bool | false | 课表时间配置同步 |
| ClassWidgets/Enabled | bool | false | |
| ClassWidgets/DataPath | string | 空 | |
| ClassWidgets/PollInterval | int | 5 | 1-30 秒 |
| Timetable/ProfileSource | string | Glimpseon | Glimpseon/classisland/classwidgets |
| PreciseTime/UsePreciseTime | bool | true | NTP 校准 |
| PreciseTime/TimeServer | string | ntp.aliyun.com | |
| PreciseTime/LastSyncTime | string | 空 | 上次同步记录 |

### Grid / Download / Notification

| 项 | 类型 | 默认 | 约束 |
|---|---|---|---|
| Grid/ShortSideCells | int | 6 | 6-96 |
| Grid/InsetPercent | int | 5 | 0-30 |
| Grid/ComponentCardOpacity | int | 55 | 0-100 |
| Grid/ComponentCardRadius | int | 16 | 0-29 |
| Download/Source | string | hk | original/hk/cloudflare/edgeone/geekertao |
| Download/ItemsPerPage | int | 8 | 4-40 |
| Notification/SyncToBoard | bool | false | 公告同步到看板 |

## 3 使用示例

```csharp
// 声明 (AppConfig.cs 内)
public static readonly ConfigItem<int> LogMaxCount = new("Log", "MaxCount", 50, min: 10, max: 500);

// 读取
var n = Config.LogMaxCount.Value;

// 写入 > 自动截断/校验/落盘/广播
Config.LogMaxCount.Value = 100;

// 订阅
Config.City.ValueChanged += _ => AppUtils.CacheRefresher.Invalidate("weather");
```

## 4 其他持久化文件

| 文件 | 写入方 | 说明 |
|---|---|---|
| data/config/Setup_Wizard.json | WizardWindow | completed 标记 |
| data/config/home_layout.json | PageManager | 页面与组件实例 |
| data/user/announcements.json | Announcements | 通知公告 |
| data/wallpaper/history.json | WallpaperService | 壁纸历史 |
| app-x.y.z/record.json | Record/Updater | 版本元数据 |
