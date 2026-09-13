# 组件系统

> \[!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

Glimpseon 定位是桌面组件化信息看板。

[core](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/component.py) · [ui](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/component.py)

***

## 1. 数据模型（core/component.py）

### 1.1 ResizeMode

```python
class ResizeMode(Enum):
    FIXED = "fixed"           # 固定尺寸
    HORIZONTAL = "horizontal" # 仅水平调整
    VERTICAL = "vertical"     # 仅垂直调整
    FREE = "free"             # 自由调整
```

### 1.2 ComponentDefinition

组件类型的元数据描述：

| 字段                                             | 类型         | 说明     |
| ---------------------------------------------- | ---------- | ------ |
| `id`                                           | str        | 唯一标识   |
| `display_name`                                 | str        | 显示名称   |
| `category`                                     | str        | 分类     |
| `icon`                                         | str        | 图标名    |
| `min_width_cells` / `min_height_cells`         | int        | 最小格子   |
| `default_width_cells` / `default_height_cells` | int        | 默认格子   |
| `resize_mode`                                  | ResizeMode | 调整模式   |
| `component_class`                              | Type       | UI 实现类 |
| `default_config`                               | dict       | 默认配置   |

支持 `to_dict()` / `from_dict()`

### 1.3 GridSettings / GridMetrics

```python
@dataclass
class GridSettings:
    short_side_cells: int = 6   # 短边格子数（cfg.gridShortSideCells）
    gap_ratio: float = 0.12     # 间隙比例
    inset_percent: int = 5      # 边距百分比（cfg.gridInsetPercent）

@dataclass
class GridMetrics:
    column_count, row_count     # 行列数
    cell_size                   # 单格像素
    gap_px                      # 间隙像素
    edge_inset_px               # 边距像素
    grid_width_px / grid_height_px  # 网格完整尺寸
```

### 1.4 PageMeta / PageManager

页面分两种类型：

| `type`   | 内容                       |
| -------- | ------------------------ |
| `"info"` | 组件页，含 `components` 列表    |
| `"nav"`  | 导航页，含 `items` 列表（应用快捷方式） |

`PageManager` 负责页面 CRUD 与持久化，配置文件 `data/config/home_layout.json`：

```json
{
  "current_page": 0,
  "pages": [
    {
      "name": "信息页",
      "type": "info",
      "components": [
        {"id":"...", "type":"...", "style":"...",
         "position":{"x":0.5,"y":0.5}, "size":{"w":200,"h":80},
         "enabled": true, "config": {}}
      ]
    },
    {"name":"导航页","type":"nav","items":[
      {"name":"...","path":"...","icon":"...","type":"app"}
    ]}
  ]
}
```

- 默认 2 页（信息页 + 导航页），`MAX_PAGES = 10`。

***

## 2. 网格布局算法（GridLayoutService）

### 2.1 calculate\_grid\_metrics

`calculate_grid_metrics(canvas_size, GridSettings) → GridMetrics`。短边格数固定为 `short_side_cells`（横屏定行数，竖屏定列数），另一方向按可用尺寸与 `gap_ratio` 推格数；边距由 `inset_percent` 控制，上限 80px。

### 2.2 坐标换算

- `get_cell_rect(metrics, col, row, w_cells, h_cells) → QRect`：格子坐标 → 屏幕像素矩形。
- `point_to_cell(metrics, point) → (row, column)`：屏幕点 → 格子坐标，网格外或间隙中返回 `(-1, -1)`。

### 2.3 碰撞检测

`check_collision(placements, target_row, target_col, w, h, exclude_id, page_index)`：同页、已启用、非自身的组件矩形是否重叠。`_rects_overlap` 用行列闭区间判断。

***

## 3. 组件注册（ComponentRegistry）

```python
class ComponentRegistry(QObject):
    definitions_changed = pyqtSignal()
```

| 方法                                          | 作用              |
| ------------------------------------------- | --------------- |
| `register(definition)`                      | 注册单个，发信号        |
| `register_batch(definitions)`               | 批量注册            |
| `unregister(id)`                            | 注销              |
| `get_definition(id)` / `has_definition(id)` | 查询              |
| `get_all_definitions()`                     | 全部              |
| `get_definitions_by_category(cat)`          | 按分类             |
| `get_categories()`                          | 所有分类            |
| `load_from_json(path, component_classes)`   | 从 json 加载并绑定实现类 |

### 3.1 内置组件

`BUILTIN_COMPONENT_DEFINITIONS` 预定义组件（数字时钟、月历等），仅用于组件库窗口展示卡片。注意：这些 `ComponentDefinition` 的 `component_class` 字段默认为 `None`，**不参与实例化**——实际创建 UI 走 [ui/component.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/component.py) 的 `COMPONENT_STYLES`（见第 9 章）。

***

## 4. UI 实现层（ui/component.py）

### 4.1 类层次

```
QWidget
 └─ DraggableWidget 
     └─ DraggableContainer
         ├─ DigitalClockComponent
         ├─ SquareClock1Component
        ├─ SquareClock2Component
         ├─ WeatherComponentBase
         │   ├─ WeatherIconTempComponent
         │   ├─ WeatherHourlyComponent
         │   └─ WeatherWeeklyComponent
         ├─ PoetryOneLineComponent
         ├─ NewsComponent
         │   └─ NewsBaidu/Weibo/...Component
         ├─ HistoryTodayComponent
         ├─ DailyWordComponent
         ├─ DailySentenceComponent
         ├─ CountdownEventComponent
         ├─ TimerCountdownComponent
         ├─ SchoolInfoComponent
         ├─ MediaPlayerComponent
         ├─ QuickLaunchDockComponent
         ├─ QuickLaunchGridComponent 
         ├─ TimetablePreviewComponent
        ├─ TimetableNowLessonComponent
        ├─ TimetableTimelineComponent
         ├─ CalculatorComponent
         ├─ WritingPadComponent
         ├─ ClassAlbumBaseComponent
         │   ├─ ClassAlbumHorizontalComponent
         │   └─ ClassAlbumVerticalComponent
         ├─ StickyNoteComponent
        ├─ CalendarMonthComponent
        ├─ MiniCalendarComponent
        └─ NavigationPage
```

### 4.2 DraggableWidget 编辑能力

- **选中框**：主题色（`_cached_primary_color`，默认 `#30c361`）边框 + 同色多层发光。
- **缩放柄**：右下角圆弧柄。
- **编辑/删除按钮**：hover 区分编辑/删除配色；使用全局 `componentCardOpacity` / `componentCardRadius`。
- **移动事件触发按钮重定位。**

### 4.3 ComponentManager

管理组件实例的生命周期、布局应用、持久化加载/保存，协调 `ComponentRegistry`、`GridLayoutService`、`PageManager`。

### 4.4 ComponentConfigDialog

`ComponentConfigDialog(MessageBoxBase)`：组件配置弹窗。

> \[!IMPORTANT]
> 约束：配置面板必须 parent 到 MainWindow（而非组件或设置窗口），以确保正确的 z-order（不被主界面遮挡）。每个组件配置独立存储。

### 4.5 组件库窗口

`ComponentLibraryWindow(FluentWindow)`：

- 加载 `resource/qss/{light,dark}/component.qss`。
- `CategoryPage` 按分类展示 `ComponentCard`，用户点击卡片添加组件到当前页。

***

## 5. 编辑模式交互

### 5.1 进入/退出

`HomeInterface.isEditMode` 切换。编辑模式下：

- 显示 `_GridOverlay` 网格背景。
- 显示 `GuideLineOverlay` 参考线。
- 组件显示选中框（主题色）、缩放柄、编辑/删除按钮。

### 5.2 拖拽与缩放

- 拖拽：`DraggableWidget` 处理鼠标事件，按 `ResizeMode` 限制方向。
- 吸附：基于 `GridLayoutService` 的格子坐标对齐。
- 碰撞：`check_collision` 重叠提醒。
- 缩放：**dpi**——每个组件都有缩放百分比（1\~300，1），右下角调整这个

#### 缩放机制（DraggableContainer）

| 成员                    | 作用                                                                                                                          |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `_dpi`               | 缩放（1\~300），由 `_init_dpi` 从存档读入，`set_dpi` 修改                      |
| `_scaled_px(base)`    | 按 `_scale_factor` 缩放基准像素，子类字号/图标/固定尺寸/圆角统一经它换算                                                                   |
| `apply_scale(factor)` | 子类按 factor 重应用样式；由基类在缩放变化时调用                                                                                                |
| `_scale_layouts()`    | 遍历 `findChildren(QLayout)`，按 `_scale_factor` 等比缩放所有子布局的 `contentsMargins` 与 `spacing`；首次调用缓存基准值（`_layout_bases`），后续始终基于基准重算 |
| `_applied_factor`     | 上次已应用到样式的缩放因子，判断是否还有未应用的差异                                                                                                  |
| `_scale_timer`        | 拖拽缩放节流定时器                                                                                                          |
| `_apply_scale_now()`  | 统一入口：执行 `apply_scale` + `_scale_layouts` 并同步 `_applied_factor`                                                              |

流程：拖拽手柄 → 位移换算为 `_dpi`（1%  1\~300）→ 占位尺寸重设为 `_base_size × dpi/100` → `_scale_timer` 节流触发 `_apply_scale_now()` → 松手应用并 `save_components` 保存 `scale` 与实际尺寸。

### 5.3 统一卡片背景（DraggableContainer）

> \[!IMPORTANT]
> **所有组件必须使用**：`_apply_card_style()` / `_card_bg_css()`。背景跟随全局设置 `componentCardOpacity` / `componentCardRadius`（浅色 `rgb(255,255,255)`，深色 `rgb(30,30,30)`，透明度=全局卡片透明度），支持组件覆盖。

基类 `DraggableContainer` 提供了统一的卡片背景：

| 成员                       | 说明                                                                                      |
| ------------------------ | --------------------------------------------------------------------------------------- |
| `_bg_opacity`            | 组件级不透明度覆盖（0\~100）；`None` 时回退全局 `cfg.componentCardOpacity.value`                         |
| `_corner_radius`         | 组件级圆角覆盖（px）；`None` 时回退全局 `cfg.componentCardRadius.value`                                |
| `_bg_mode`               | `"opacity"`（默认，跟随全局透明度）/ `"custom"`（使用 `_bg_color`）                                     |
| `_bg_color`              | `"custom"` 模式下的背景颜色（如 `"#ffffff"`）                                                      |
| `_card_bg_css(...)`      | 返回一段 QSS：`#objName { background-color: rgba(...); border-radius: Npx; [border: ...;] }` |
| `_apply_card_style(...)` | 把该 QSS 应用到 `target`（默认 `self`，取 `target.objectName()`）                                  |

`_apply_card_style(target=None, obj_name=None, bg_mode=None, bg_color=None, opacity=None, radius=None, border=None)`：

- `opacity` / `radius` 传 `None` 即使用 `self._bg_opacity` / `self._corner_radius`，再为 `None` 则回退全局设置。
- `border` 可选，用于给背景追加边框（如 `"1px solid rgba(0,0,0,0.06)"`，媒体播放器用）。

`cfg.componentCardOpacity`、`cfg.componentCardRadius` 或 `cfg.themeChanged` 变化时，基类自动触发 `_on_card_config_changed()` → 重应用背景并调用子类 `_apply_style()`，因此全局设置/主题变化无需每个组件单独监听。组件级 `bg_opacity` / `corner_radius` 由配置面板写入 `component_data["config"]`，经基类 `apply_config(config)` 读取生效。

组件在 `_apply_style()` 中的两种标准写法（二选一）：

```python
# 写法 A：背景与子控件样式分离（推荐）
def _apply_style(self):
    self._apply_card_style()                 # 容器背景跟随全局设置
    self.someLabel.setStyleSheet("...")      # 子控件只设文字/透明背景

# 写法 B：整份样式表覆盖 self 时，把卡片背景拼到最前面
def _apply_style(self):
    bg_css = self._card_bg_css()             # 用统一方法生成容器背景
    self.setStyleSheet(f"""
        {bg_css}
        #someContainer {{ ... }}
    """)
```

> \[!WARNING]
> 写法 B 必须把 `{self._card_bg_css()}` 拼进 `setStyleSheet`，因为 `setStyleSheet` 会**整体替换**原样式表——若只调 `_apply_card_style()` 再 `setStyleSheet(...)`，背景会被覆盖丢失（今日课表 `TimetablePreviewComponent` 曾因此无背景）。

### 5.4 多页与页面状态

- `PageIndicator`：多页小圆点。
  - **响应左/右键点击切换页**
- 页面切换由 `PageManager._current_page` 控制。

***

## 6. 手写画板（WritingPadComponent）

手写画板主体是 `_WritingOverlay`（全屏透明覆盖层，`FramelessWindowHint | Tool`，`WA_TranslucentBackground`），经 Windows `WM_POINTER` 只读触控点（`PT_TOUCH`），并拦截 Qt 侧重复派发的 mouse event。相关类：`_PenSettingsPopup`（画笔设置）、`_OverToolBtn`（悬浮工具按钮）。算法参考 Inkeys。

### 6.1 分层结构与渲染

| 层   | 载体                   | 作用                                     |
| --- | -------------------- | -------------------------------------- |
| 永久层 | `_buffer`（QPixmap）   | 实际笔画与擦除发生处，`paintEvent` 中 `drawPixmap` |
| 临时层 | `_temp_pixmaps[tid]` | 直线/矩形等形状的实时预览（抬笔前不落盘）                  |
| 光标层 | `paintEvent` 绘制      | 橡皮光标，每帧重绘，不写入 buffer                   |

### 6.2 定时器分工

| 定时器                  | 回调                     | 职责                            |
| -------------------- | ---------------------- | ----------------------------- |
| `_touch_timer`       | `_process_touch_queue` | 消费触控事件队列 `_touch_queue`，按模式分发 |
| `_erase_speed_timer` | `_sample_erase_speed`  | 采样擦除速度（EMA 平滑）                |
| `_erase_loop_timer`  | `_erase_loop_tick`     | 擦除主循环                         |

擦除状态按触点 tid 分组维护（`_erase_prev_pos` / `_erase_speed` / `_erase_rubber` / `_erase_trubber` / `_erase_cursors` 等）：目标直径随擦除速度变化，实际直径 `rubber` 平滑追随目标；橡皮尺寸经 `drawingScale` 适配屏幕分辨率。实际擦除用 `CompositionMode_DestinationOut`：移动画线（`RoundCap` / `RoundJoin`），原地画实心圆。

### 6.3 定时器循环而非事件驱动

事件驱动在输入停止后无法继续更新，半径会冻结在半路。擦除循环持续读取 `_erase_live_pos` 并让 `rubber` 向 `trubber` 追随，输入暂停时直径仍连续变化；速度采样独立定时器，降低逐事件计算的抖动。

### 6.4 撤回与重建

- `_undo_last_stroke`：`_history.pop()` → `_rebuild_buffer()`。
- `_rebuild_buffer`：按 `_history` 顺序重放所有 `draw` 笔画与 `erase` 会话。
- `clear_all`：清空全部历史与 buffer。

***

## 7. 媒体组件

单一 `MediaPlayerComponent`（`DraggableContainer` 子类），后台从 `services.media` 获取正在播放的媒体信息（标题/艺术家/封面/进度/歌词）。

### 7.1 双定时器

| 定时器           | 回调                 | 职责                                                  |
| ------------- | ------------------ | --------------------------------------------------- |
| `_timer`      | `_poll`            | 完整抓取（标题/艺术家/封面/歌词/进度），间隔 `cfg.mediaUpdateInterval` |
| `_prog_timer` | `_update_progress` | 播放中推算进度，不请求网络即更新进度条                               |

检测到新歌（`title_artist` 变化）时切到快速抓取间隔，尽快拿到封面/歌词。

### 7.2 线程化抓取

`threading.Thread`（daemon）+ pyqtSignal 回主线程：`_spawn_media_fetch` → `_media_worker` → `_media_ready` → `_on_media`；详情经 `_fetch(m)` → `_detail_ready` → `_on_detail`。

- `_fetching` 标志防重入，期间的新请求记入 `_pending_full`，完成后补抓。
- `stop()`（`closeEvent` / `__del__` 调用）停掉全部定时器，防止线程残留。

### 7.3 封面来源优先级

1. **SMTC 缩略图**：`m.thumbnail_data` 存在 → 直接 `_load_cover`，置 `_has_thumb`（优先级最高，不再被在线封面覆盖）。
2. **浏览器**：只等 `thumbnail_data`，不触发在线查询。
3. **酷狗**：详情线程内额外借用 SMTC 会话缩略图作封面。
4. **在线补全**：非浏览器 → `_fetch(m)`，仅 `not _has_thumb` 时应用封面。

封面载入后加渐变阴影并淡入；默认封面 `_default_cover` 自绘圆角矩形 + 音符图标（主题自适应配色）。

### 7.4 切歌竞态保护

详情补全与轮询抓取异步并行，快速切歌时旧结果可能滞后返回：

- 详情结果携带歌曲 key（`title_artist`），`_on_detail` 仅在 key 与当前歌曲一致时应用（`_apply_detail`）。
- 详情线程忙碌时 `_fetch` 记录 `_pending_key`，返回后自动补拉当前歌。
- `_no_media` 重置 `_pending_key`。

### 7.5 缓存与播放控制

- `_info_cache`（`OrderedDict` LRU）：以 `title_artist` 为 key 缓存详情补全结果；`clear_cache()` 清空并 `close_media()` 释放资源。
- 播放/暂停：先立即更新图标，后台 `media_control()` 后由 `_sync_confirm_timer` 轮询 SMTC 真实状态确认，同步期间旧状态不覆盖图标（`_playing_sync_pending`）。
- 上一首/下一首：后台 `media_next()` / `media_prev()`，随后重新完整拉取。
- 歌词行定位带 `cfg.mediaLyricsAdvance` 提前量；浏览器源无艺术家时标题换行显示并隐藏歌词行。

***

## 8. 持久化与加载流程

```
启动时:
  PageManager.load()
    └─ 读 home_layout.json → PageMeta 列表
  ComponentManager 按当前页 components 实例化
    └─ COMPONENT_STYLES[comp_data["type"]][comp_data["style"]]["class"] → 实现类
    └─ comp_class(parent_widget, comp_data) 创建 UI
    └─ 应用 position/size/config

编辑时:
  拖拽/缩放 → 更新实例 position/size
  配置弹窗 → 更新 config
  保存 → PageManager.save() → home_layout.json
```

> \[!IMPORTANT]
> 组件加载必须在 splash 期间**同步完成**，避免 `QTimer.singleShot(0)` 导致主窗口显示后才加载的延迟。

***

## 9. 扩展新组件

### 9.1 需要理解的组件配置

项目中有两套组件元数据，职责不同，新增组件时都要照顾到：

| 表                               | 位置                                                                                   | 作用                                                                     | 是否参与实例化                                                         |
| ------------------------------- | ------------------------------------------------------------------------------------ | ---------------------------------------------------------------------- | --------------------------------------------------------------- |
| `COMPONENT_STYLES`              | [ui/component.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/component.py)     | `comp_type → comp_style → {name, class, default_config, default_size}` | **是**，`ComponentManager` 据此 `comp_class(parent, comp_data)` 实例化 |
| `BUILTIN_COMPONENT_DEFINITIONS` | [core/component.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/component.py) | `ComponentDefinition` 列表（id/分类/格子数/resize\_mode）                       | 否，仅用于组件库窗口展示卡片                                                  |

组件在 `home_layout.json` 中存储的是 `type` + `style`（如 `"type":"clock","style":"digital"`），而非 `ComponentDefinition.id`。`ComponentManager.load_components()` 通过 `COMPONENT_STYLES[type][style]["class"]` 取实现类。

### 9.2 组件数据结构

每个组件实例在 `home_layout.json` 中形如：

```json
{
  "id": "clock_1",
  "type": "clock",
  "style": "digital",
  "position": {"x": 0.5, "y": 0.5},
  "size": {"w": 400, "h": 200},
  "enabled": true,
  "page_index": 0,
  "config": {}
}
```

### 9.3 新增步骤

假设要新增一个「打卡」组件，type=`checkin`、style=`default`。

步骤 1：注册到 `COMPONENT_STYLES`

在 [ui/component.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/component.py) 的 `COMPONENT_STYLES` 字典中新增条目。若是全新分类，加一个顶层键：

```python
"checkin": {
    "default": {
        "name": "打卡",
        "class": None,                 # 先置 None，步骤 3 再绑定
        "default_config": {"goal": 100},
        "default_size": (200, 120),
    },
},
```

**步骤 2：实现组件类**

在 `ui/component.py` 中实现，继承 `DraggableContainer`，构造签名固定为 `(self, parent, component_data: dict)`：

```python
class CheckinComponent(DraggableContainer):
    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"],
                         layout_direction="vertical")
        self.setObjectName("checkinContainer")
        self._config = component_data.get("config", {})
        self._setup_ui()          # 构建内部 UI
        self._apply_style()       # 主题相关样式（含统一背景）
        # 可选：监听 cfg 变化、实现 apply_scale(factor) 等

    def _apply_style(self):
        # 必须用统一背景方法，跟随全局 componentCardOpacity / componentCardRadius
        self._apply_card_style()
        # 子控件样式设到这里（文字/透明背景）

    def apply_scale(self, factor):
        # 按 factor 缩放内部元素（参考 MediaPlayerComponent.apply_scale）
        ...
```

要点：

- 通过 `component_data["config"]` 读取独立配置。
- **背景必须走统一方法**：`_apply_style()` 中调 `self._apply_card_style()`；若用整份样式表覆盖自身，则需把 `{self._card_bg_css()}` 拼在样式表最前面（见 [5.3 统一卡片背景](#53-统一卡片背景draggablecontainer)）。不要自行写 `background-color`。
- 实现主题切换响应（`_apply_style` / 重载 `_onThemeChanged`）。
- 若需随缩放，实现 `apply_scale(factor)`：内部字号/图标/固定尺寸/圆角一律用 `self._scaled_px(base)`；子布局边距/间距由基类 `_scale_layouts()` 自动等比缩放，无需手动处理。
- 初始化与 `apply_scale` 必须同步：`_setup_ui` 中用过 `_scaled_px` 的固定尺寸（行高/列宽/图标底图等），`apply_scale` 中必须重新设置。
- 调用 `self._set_natural_size(w, h)` 设自然尺寸，`self._size_explicitly_set = True`。

**步骤 3：绑定 class**

在文件末尾的绑定区追加：

```python
COMPONENT_STYLES["checkin"]["default"]["class"] = CheckinComponent
```

> \[!WARNING]
> 绑定放在类定义之后：`COMPONENT_STYLES` 定义时组件类还不存在，必须延后赋值。漏掉此步会导致 `load_components` 报「组件样式未注册」并跳过。

**步骤 4：（可选）加入组件库展示**

若希望该组件出现在组件库窗口供用户添加，在 [core/component.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/component.py) 的 `BUILTIN_COMPONENT_DEFINITIONS` 追加 `ComponentDefinition`：

```python
ComponentDefinition(
    id="checkin_default",
    display_name="打卡",
    category="Tool",
    icon="Checkmark",
    min_width_cells=2, min_height_cells=2,
    default_width_cells=2, default_height_cells=2,
    resize_mode=ResizeMode.FREE,
    default_config={"goal": 100},
),
```

`ComponentRegistry.register_batch(BUILTIN_COMPONENT_DEFINITIONS)`（[home.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/ui/home.py)）会在启动时注册，组件库窗口据此渲染卡片。

**步骤 5：（可选）配置面板**

若组件需要用户可调配置，实现一个继承 `MessageBoxBase` 的配置对话框，并将其实例的 parent 设为 **MainWindow**（而非组件自身或设置窗口），以保证 z-order 正确、不被主界面遮挡。配置写回 `component_data["config"]` 后调 `ComponentManager.save_components()` 持久化。

**步骤 6：资源与国际化**

- 图标 SVG 放入 `resource/fluent/{light,dark}/`，命名 `ic_fluent_{name}_{24|32}_regular.svg`，通过 `FUI.{NAME}` 引用。
- 文案键加入 `locale/{zh_CN,zh_TW,en_US}.json`，代码用 `tr("key")` 读取。

### 9.4 验证

1. 启动应用，进入编辑模式，打开组件库，确认新组件卡片出现。
2. 点击卡片添加，确认 `home_layout.json` 中生成 `type=checkin, style=default` 条目。
3. 重启应用，确认组件被 `load_components` 正确还原。
4. 切换浅/深主题，确认样式随主题更新。

### 9.5 常见坑

| 现象                   | 原因                                                               |
| -------------------- | ---------------------------------------------------------------- |
| 「组件样式未注册」日志，组件不出现    | 步骤 3 的 `class` 绑定遗漏，或 `type`/`style` 拼写不一致                       |
| 组件库无卡片，但手动改 json 能加载 | 步骤 4 的 `BUILTIN_COMPONENT_DEFINITIONS` 未追加                       |
| 配置弹窗被主界面遮挡           | 弹窗未 parent 到 MainWindow                                          |
| 缩放后内部元素不变            | 未实现 `apply_scale(factor)`；或已实现但字号/尺寸仍硬编码，需改用 `self._scaled_px()` |
| 缩放后固定尺寸停留在初始值        | `_setup_ui` 用了 `_scaled_px` 但 `apply_scale` 未重设（行高/列宽/图标底图等）     |
| 切主题样式不更新             | 未在 `_apply_style` 中重读主题色 / 未连 `cfg.themeChanged`                 |
