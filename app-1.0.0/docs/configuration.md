# 配置系统

> [!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/config.py)

Glimpseon 使用 qfluentwidgets 的 `QConfig` 体系。所有配置项以类属性形式声明在 `Config(QConfig)` 中，存储于 `data/config/config.json`。

***

## 1. 机制

### 1.1 配置项类型

| 类型                        | 说明                                  |
| ------------------------- | ----------------------------------- |
| `ConfigItem`              | 通用项，带 `validator`                   |
| `OptionsConfigItem`       | 枚举项，`OptionsValidator` 限定取值         |
| `RangeConfigItem`         | 数值范围项，`RangeValidator(min, max)`    |
| `ColorConfigItem`         | 颜色项（`QColor`）                       |
| `ConfigItem + serializer` | 自定义序列化（如 `CountdownListSerializer`） |

### 1.2 声明形式

```python
themeMode = OptionsConfigItem(
    "MainWindow", "ThemeMode", Theme.AUTO,
    OptionsValidator([Theme.LIGHT, Theme.DARK, Theme.AUTO]),
    ThemeSerializer()
)
```

参数依次为：`group`、`name`、`default`、`validator`、`serializer`，可选 `restart=True`（变更需重启）。

### 1.3 自动保存

导入期遍历所有 `ConfigItem`，连接 `valueChanged → _on_config_changed → save_cfg()`。因此：

```python
cfg.clockSize.value = 100   # 修改即触发保存
```

### 1.4 全局对象

- `cfg`：`Config` 单例
- `save_cfg()`：立即持久化
- `default_cfg()`：返回完整默认字典（用于向导 / 重置）

### 1.5 主题变更信号

`cfg.themeChanged` 在 `setTheme` 时触发，`MainWindow._initThemeConnections` 将其广播到各界面的 `_onThemeChanged`。

***

## 2. 配置项全表

下表列出全部配置项（节 / 键 / 默认值 / 范围或选项 / 备注）。

### MainWindow

| 键            | 默认        | 范围 / 选项                                               | 备注      |
| ------------ | --------- | ----------------------------------------------------- | ------- |
| `ThemeMode`  | `AUTO`    | `LIGHT/DARK/AUTO`                                     | <br />  |
| `ThemeColor` | `#30c361` | 颜色                                                    | <br />  |
| `DpiScale`   | `Auto`    | `1/1.25/1.5/1.75/2/Auto`                              | restart |
| `Language`   | `AUTO`    | `CHINESE_SIMPLIFIED/CHINESE_TRADITIONAL/ENGLISH/AUTO` | restart |

### Log

| 键            | 默认      | 范围                         | 备注              |
| ------------ | ------- | -------------------------- | --------------- |
| `LogLevel`   | `INFO`  | `DEBUG/INFO/WARNING/ERROR` | restart         |
| `DisableLog` | `False` | bool                       | restart         |
| `MaxCount`   | `50`    | 10\~500                    | DebugMode 下强制 3 |
| `MaxDays`    | `7`     | 30\~365                    | DebugMode 下强制 1 |

### Wallpaper

| 键                   | 默认            | 范围 / 选项                                                                        | 备注     |
| ------------------- | ------------- | ------------------------------------------------------------------------------ | ------ |
| `SaveLimit`         | `50`          | 10\~100                                                                        | 历史保留数量 |
| `AutoGetInterval`   | `30m`         | `never/10m/30m/1h/3h/6h/12h/1d/3d/5d/7d`                                       | <br /> |
| `AutoSyncToDesktop` | `True`        | bool                                                                           | <br /> |
| `WallpaperApi`      | `wp.upx8.com` | `wp.upx8.com/api.ltyuanfang.cn/imlcd.cn_bg_high/imlcd.cn_bg_mc/imlcd.cn_bg_gq` | <br /> |
| `Brightness`        | `0`           | -100\~0                                                                        | 负值变暗   |

### Appearance

| 键                      | 默认  | 范围    | 备注     |
| ---------------------- | --- | ----- | ------ |
| `BackgroundBlurRadius` | `0` | 0\~30 | 壁纸模糊半径 |

### Time

| 键                         | 默认        | 范围          | 备注     |
| ------------------------- | --------- | ----------- | ------ |
| `ShowClock`               | `True`    | bool        | <br /> |
| `ShowClockSeconds`        | `True`    | bool        | <br /> |
| `ShowLunarCalendar`       | `True`    | bool        | <br /> |
| `ClockColor`              | `#FFFFFF` | 颜色          | <br /> |
| `ClockSize`               | `80`      | 40\~120     | <br /> |
| `DateSize`                | `16`      | 10\~40      | <br /> |
| `TimeOffset`              | `0`       | -9999\~9999 | 秒      |
| `AutoTimeOffsetEnabled`   | `False`   | bool        | <br /> |
| `AutoTimeOffsetIncrement` | `1`       | -9999\~9999 | <br /> |

### Poetry

| 键                      | 默认                                  | 范围 / 选项                            | 备注     |
| ---------------------- | ----------------------------------- | ---------------------------------- | ------ |
| `ShowPoetry`           | `True`                              | bool                               | <br /> |
| `PoetryApiUrl`         | `https://v1.hitokoto.cn/` | 字符串                                | <br /> |
| `PoetryUpdateInterval` | `10m`                               | `never/5m/10m/30m/1h/3h/6h/12h/1d` | <br /> |
| `PoetrySize`           | `16`                                | 12\~50                             | <br /> |
| `PoetryTextColor`      | `#FFFFFF`                           | 颜色                                 | <br /> |

### Weather

| 键                  | 默认         | 范围 / 选项                             | 备注     |
| ------------------ | ---------- | ----------------------------------- | ------ |
| `ShowWeather`      | `True`     | bool                                | <br /> |
| `WeatherSize`      | `24`       | 5\~50                               | 温度字号   |
| `WeatherTextColor` | `#FFFFFF`  | 颜色                                  | <br /> |
| `WeatherIconSize`  | `64`       | 32\~200                             | <br /> |
| `UpdateInterval`   | `5m`       | `never/5m/15m/30m/1h/3h/6h/12h/24h` | <br /> |
| `City`             | `""`       | 字符串                                 | <br /> |
| `CityCode`         | `""`       | 字符串                                 | <br /> |
| `Latitude`         | `39.9042`  | float                               | <br /> |
| `Longitude`        | `116.4074` | float                               | <br /> |

### Countdown

| 键                  | 默认             | 范围 / 选项                 | 备注     |
| ------------------ | -------------- | ----------------------- | ------ |
| `ShowCountdown`    | `True`         | bool                    | <br /> |
| `DisplayMode`      | `simultaneous` | `simultaneous/carousel` | <br /> |
| `TextColor`        | `#FF0000`      | 颜色                      | <br /> |
| `TextSize`         | `35`           | 12\~120                 | <br /> |
| `ConnectorColor`   | `#FFFFFF`      | 颜色                      | <br /> |
| `ConnectorSize`    | `35`           | 12\~60                  | <br /> |
| `CarouselInterval` | `5`            | 1\~60                   | 秒      |
| `CountdownList`    | `[]`           | list                    | 自定义序列化 |

### School

| 键                     | 默认        | 范围     | 备注     |
| --------------------- | --------- | ------ | ------ |
| `School`              | `""`      | 字符串    | <br /> |
| `Class`               | `""`      | 字符串    | <br /> |
| `ShowSchoolInfo`      | `False`   | bool   | <br /> |
| `SchoolInfoTextColor` | `#FFFFFF` | 颜色     | <br /> |
| `SchoolInfoTextSize`  | `34`      | 12\~60 | <br /> |

### QuickLaunch

| 键                 | 默认     | 范围     | 备注     |
| ----------------- | ------ | ------ | ------ |
| `ShowQuickLaunch` | `True` | bool   | <br /> |
| `QuickLaunchApps` | `[]`   | list   | <br /> |
| `IconSize`        | `64`   | 32\~96 | <br /> |
| `IconSpacing`     | `12`   | 4\~40  | <br /> |
| `ShowLabels`      | `True` | bool   | <br /> |
| `OffsetY`         | `60`   | 0\~120 | <br /> |

### Media

| 键                   | 默认          | 范围       | 备注     |
| ------------------- | ----------- | -------- | ------ |
| `ShowMediaInfo`     | `True`      | bool     | <br /> |
| `ShowMediaCover`    | `True`      | bool     | <br /> |
| `ShowMediaLyrics`   | `True`      | bool     | <br /> |
| `UpdateInterval`    | `1`         | 1\~5     | 秒      |
| `TextSize`          | `14`        | 10\~28   | <br /> |
| `CoverSize`         | `56`        | 32\~128  | <br /> |
| `LyricsSize`        | `12`        | 8\~24    | <br /> |
| `LyricsLines`       | `3`         | 1\~7     | <br /> |
| `Width`             | `360`       | 200\~800 | <br /> |
| `Height`            | `160`       | 100\~300 | <br /> |
| `LyricsAdvance`     | `300`       | 0\~2000  | ms     |
| `UseCustomBg`       | `False`     | bool     | <br /> |
| `BgOpacity`         | `60`        | 0\~100   | <br /> |
| `BorderRadius`      | `12`        | 0\~30    | <br /> |
| `TitleColor`        | `#FFFFFF`   | 颜色       | <br /> |
| `ArtistColor`       | `#FFFFFF99` | 颜色       | <br /> |
| `TimeColor`         | `#FFFFFF80` | 颜色       | <br /> |
| `LyricsColor`       | `#FFFFFFB3` | 颜色       | <br /> |
| `CoverBorderRadius` | `10`        | 0\~20    | <br /> |
| `CoverBorderColor`  | `#FFFFFF20` | 颜色       | <br /> |

### Linkage（ClassIsland）

| 键                | 默认      | 范围    | 备注     |
| ---------------- | ------- | ----- | ------ |
| `Enabled`        | `False` | bool  | <br /> |
| `DataPath`       | `""`    | 字符串   | <br /> |
| `PollInterval`   | `5`     | 1\~30 | 秒      |
| `SyncTimeConfig` | `False` | bool  | <br /> |

### ClassWidgets

| 键              | 默认      | 范围    | 备注     |
| -------------- | ------- | ----- | ------ |
| `Enabled`      | `False` | bool  | <br /> |
| `DataPath`     | `""`    | 字符串   | <br /> |
| `PollInterval` | `5`     | 1\~30 | 秒      |

### Timetable

| 键               | 默认          | 选项                                   | 备注   |
| --------------- | ----------- | ------------------------------------ | ---- |
| `ProfileSource` | `Glimpseon` | `Glimpseon/classisland/classwidgets` | 课表来源 |

### PreciseTime

| 键                | 默认               | 备注     |
| ---------------- | ---------------- | ------ |
| `UsePreciseTime` | `False`          | bool   |
| `TimeServer`     | `ntp.aliyun.com` | <br /> |
| `LastSyncTime`   | `""`             | <br /> |

### Grid

| 键                      | 默认   | 范围     | 备注    |
| ---------------------- | ---- | ------ | ----- |
| `ShortSideCells`       | `6`  | 6\~96  | 短边格子数 |
| `InsetPercent`         | `5`  | 0\~30  | 边距百分比 |
| `ComponentCardOpacity` | `55` | 0\~100 | 卡片透明度 |
| `ComponentCardRadius`  | `16` | 0\~29  | 卡片圆角  |

### Download

| 键              | 默认   | 选项                                         | 备注                                                  |
| -------------- | ---- | ------------------------------------------ | --------------------------------------------------- |
| `Source`       | `hk` | `original/hk/cloudflare/edgeone/geekertao` | 下载镜像源 |
| `ItemsPerPage` | `8`  | 4\~40                                      | 弃用                          |

### Other

| 键                           | 默认         | 范围 / 选项          | 备注               |
| --------------------------- | ---------- | ---------------- | ---------------- |
| `CloseAction`               | `minimize` | `minimize/close` | 关闭按钮行为           |
| `AllowMultipleInstances`    | `False`    | bool             | <br />           |
| `DebugMode`                 | `False`    | bool             | 显示调试面板 + 跳过单例    |
| `EnableGpuAcceleration`     | `True`     | bool             | restart，OpenGLES |
| `AutoStart`                 | `False`    | bool             | <br />           |
| `AutoOpenOnIdle`            | `False`    | bool             | <br />           |
| `IdleMinutes`               | `5`        | 1\~60            | <br />           |
| `AutoOpenMaximize`          | `False`    | bool             | <br />           |
| `AutoCheckUpdate`           | `True`     | bool             | <br />           |
| `AutoUpdate`                | `False`    | bool             | <br />           |
| `MinimizeNotificationCount` | `0`        | int              | <br />           |
| `ScrollBannerBgHeight`      | `80`       | 40\~300          | <br />           |
| `ScrollBannerMouseThrough`  | `True`     | bool             | <br />           |

***

## 3. 默认配置

`default_cfg()` 返回默认字典（部分项的默认值在函数中略有差异）。该函数用于：

- 首次运行向导初始化
- 配置重置

***

## 4. 使用示例

```python
from core.config import cfg, save_cfg

# 读取
theme = cfg.themeMode.value
size = cfg.clockSize.value        # int

# 写入（自动保存）
cfg.clockSize.value = 100

# 监听变更
cfg.themeMode.valueChanged.connect(on_theme_changed)

# 手动保存（一般不用 除非初期个别雷霆代码）
save_cfg()
```

***

## 5. 序列化器

| 序列化器                      | 作用                                    |
| ------------------------- | ------------------------------------- |
| `ThemeSerializer`         | `Theme` ↔ 字符串                         |
| `LanguageSerializer`      | `Language` ↔ `zh_CN/zh_TW/en_US/Auto` |
| `LogLevelSerializer`      | `LogLevel` ↔ 字符串                      |
| `CountdownListSerializer` | 倒计时列表 ↔ JSON list                     |

