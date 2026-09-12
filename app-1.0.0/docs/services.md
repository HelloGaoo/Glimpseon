# 服务模块（services/）

> \[!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

`services/` 是数据获取层，从网络或系统获取外部数据。所有服务统一使用 `core.utils` 的文件缓存机制（`save_cache` / `get_cached_content`）减少请求。

[包导出](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/__init__.py)：`from .media import *`、`PoetryService`、`WeatherService`、`NewsService`、`HistoryService`、`WordService`、`SentenceService`。

***

## 1. weather.py — 天气服务

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/weather.py)

### 1.1 数据源

- API：`https://weatherapi.market.xiaomi.com/wtr-v3/weather/all`（小米天气接口）
- 鉴权：固定 `APPKEY=weather20151024`、`SIGN=zUFJoAR2ZVrDy1vF3D07`
- 入参：经纬度（`cfg.latitude` / `cfg.longitude`）

### 1.2 主要类

| 类                                      | 作用                                                                           |
| -------------------------------------- | ---------------------------------------------------------------------------- |
| `WeatherService`                       | 天气获取主服务，含天气代码映射表 `WEATHER_MAP` / `ICON_MAP` / `WEATHER_TEXT_MAP`             |
| `RegionDatabase`                       | 基于 SQLite 读取 `resource/city.db`，提供 `get_coordinates(city_name) → (lon, lat)` |
| `RegionSelectorDialog(MessageBoxBase)` | 城市选择对话框（搜索 + 列表）                                                             |

### 1.3 天气代码体系

- `WEATHER_MAP`：0\~20 基础天气代码 → 中文名 + SVG 文件名。
- `ICON_MAP`：扩展天气代码（含 21\~99）→ 图标 SVG 映射。
- `WEATHER_TEXT_MAP`：天气代码 → i18n 键（如 `weather.sunny`），通过 `tr()` 翻译。
- 图标资源位于 `resource/icons/weather/`，含 `alerts/`（蓝/橙/红/黄预警）与 `reminders/`（高低温/降雨提醒）。

### 1.4 刷新与缓存

- `fetch_all()`：一次拉取当前温度、天气代码、逐小时、每日预报。
- 刷新间隔由 `cfg.weatherUpdateInterval`（`never/5m/15m/30m/1h/3h/6h/12h/24h`）控制。
- 缓存键 `"weather"`，由 `Preloader._load_wt` 预加载。

***

## 2. poetry.py — 一言服务

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/poetry.py)

### 2.1 配置

- API：`cfg.poetryApiUrl`，默认 `https://v1.hitokoto.cn/`
- 回退文案：`tr("poetry.default")`
- 刷新间隔：`cfg.poetryUpdateInterval`

### 2.2 PoetryService

| 方法                         | 作用                                                                                                             |
| -------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `get_poetry(api_url=None)` | 请求 API，解析 JSON（提取 `hitokoto` 正文与 `from`/`from_who` 出处，拼为 `正文——出处`）；非 JSON（如 `?encode=text` 纯文本）按原文本返回；失败返回回退文案 |
| `get_poetry_with_cache()`  | 带缓存读取，缓存键 `"poetry"`                                                                                           |

***

## 3. news.py — 新闻服务

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/news.py)

### 3.1 数据源

| 平台                   | API                                                            |
| -------------------- | -------------------------------------------------------------- |
| 央视新闻                 | `https://api.xcvts.cn/api/hotlist/ysxw?type=json`              |
| 百度 / 微博 / 今日头条 / 腾讯网 | `https://news.orz.ai/api/v1/dailynews/`（`SUPPORTED_PLATFORMS`） |

### 3.2 NewsService

- `_create_session()`：带浏览器 UA 的 `requests.Session`。
- `fetch_cctv_news(use_cache=True)`：央视新闻列表，缓存键 `news_cctv`。
- 各平台热点抓取，缓存间隔统一 `30m`。
- 图标映射见 `core/constants.py` 的 `NEWS_ICONS`（`resource/icons/news/*.svg`）。

***

## 4. media.py — 媒体服务

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/media.py)

**职责**：从多个本地播放器获取正在播放的媒体信息（标题/艺术家/封面/进度/歌词）。

### 4.1 数据模型

| 类                     | 作用                                        |
| --------------------- | ----------------------------------------- |
| `MediaInfo`           | 媒体信息（标题、艺术家、专辑、时长、进度、封面等），`is_valid()` 校验 |
| `LyricLine`           | 单行歌词（时间戳 + 文本）                            |
| `Lyrics`              | 歌词集合                                      |
| `parse_lrc(lrc_text)` | LRC 文本解析为 `List[LyricLine]`               |

### 4.2 媒体源

四个媒体源相互独立：

| 源类                  | 名称                | 数据获取方式                                                               |
| ------------------- | ----------------- | -------------------------------------------------------------------- |
| `NeteaseCloudMusic` | NeteaseCloudMusic | 网易云内存读取（V2 偏移表 / V3 AOB 扫描）+ 窗口标题 + `music163.xuanmou.com.cn` 代理 API |
| `KugouMusic`        | KugouMusic        | 酷狗窗口标题模拟进度 + 酷狗搜索/歌词/封面 API（三个分离缓存）                                  |
| `QQMusic`           | QQMusic           | winsdk SMTC 会话 + UIA（uiautomation）读真实进度                              |
| `GsmTc`             | GSMTC             | Windows SMTC 通用源，支持播放控制                                              |

每个源实现统一接口：`read() / lyrics(media) / cover(media) / duration(media) / control(action) / close()`。

### 4.3 模块路由

模块函数只做路由，不承担聚合逻辑：

- `get_media_info()`：按序探测四个源，首个返回有效信息者胜出。
- `get_service(app_name)`：按应用名关键词分发（`kugou`/`qqmusic`/`netease`/`cloudmusic`），未知应用回退 `_gsmtc`。
- `get_netease()` / `get_gstmtc()`：获取特定源实例。
- `media_control(action)` / `media_next()` / `media_prev()` / `media_play_pause()`：向当前 SMTC 会话发送控制命令。
- `close()`：释放所有源资源（session/event loop），`_api_get` 支持 close 后惰性重建 session。

### 4.4 与 UI 协作

UI 端 `MediaPlayerComponent` 用 `threading.Thread`（daemon）+ pyqtSignal 在后台调用服务接口，切歌竞态保护见 [component-system.md 7.4](component-system.md#74-切歌竞态保护)。

***

## 5. history.py — 历史上的今天服务

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/history.py)

### 5.1 数据源

- API：`https://tmini.net/api/today`（GET，参数 `type=json`，可选 `ckey`）
- 返回：`{code, date, events: [{title, year, desc, link}]}`

### 5.2 HistoryService

| 方法                                    | 作用                                      |
| ------------------------------------- | --------------------------------------- |
| `fetch_history_today(use_cache=True)` | 拉取当日历史事件，缓存键 `history_today`，缓存间隔 `12h` |

成功返回 `{"date": "YYYY年MM月DD日", "events": [...]}`，失败返回 `None`。

***

## 6. word.py — 每日单词服务

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/word.py)

### 6.1 数据源

- API：`https://uapis.cn/api/v1/daily/word`（GET，参数 `category=WORD_CATEGORY`，当前为 `cet4`）
- 返回：`{date, language, category, seed, count, words: [{word, phonetic, translation, definition, collins, categories, examples}]}`

### 6.2 WordService

| 方法                                 | 作用                                             |
| ---------------------------------- | ---------------------------------------------- |
| `fetch_daily_word(use_cache=True)` | 请求当日单词（`words[0]`），缓存键 `daily_word`，缓存间隔 `12h` |

成功返回 `{"date": "YYYY-MM-DD", "word": {...}}`，失败返回 `None`。

***

## 7. sentence.py — 每日英语服务

[源码](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/services/sentence.py)

### 7.1 数据源

- API：`https://api.timelessq.com/english-sentence`（无参）
- 返回：`{errno, errmsg, data: {_id, sid, tts, content, note, translation, sharePicture, caption, picture, ..., date}}`；取 `content`（英文句子）/ `note`（中文翻译）/ `date`

### 7.2 SentenceService

| 方法                                     | 作用                                       |
| -------------------------------------- | ---------------------------------------- |
| `fetch_daily_sentence(use_cache=True)` | 请求当日英语句子，缓存键 `daily_sentence`，缓存间隔 `12h` |

成功返回 `{"date": "YYYY-MM-DD", "sentence": {content, note, translation}}`，失败返回 `None`。

***

## 8. 跨服务约定

### 8.1 缓存

所有服务使用 `core.utils.save_cache(name, content, interval_str)` 与 `get_cached_content(name)`。缓存文件位于 `DATA_CACHE`，结构含 `content` + `expiry` 时间戳。

### 8.2 日志

子模块日志器命名 `Glimpseon.services.{module}`，遵循层级命名约定。

### 8.3 预加载

壁纸 / 天气 / 一言在启动期由 `Preloader`（QThread）并行预加载，通过信号回主线程，避免 UI 卡顿。详见 [启动流程](launch-flow.md)。

### 8.4 错误处理

- 网络异常统一 `try/except` + `logger.error`，返回 `None` 或回退值。
- 不向上抛出，保证 UI 始终拿到可渲染数据。

