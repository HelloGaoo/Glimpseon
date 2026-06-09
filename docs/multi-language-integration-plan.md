# ClassLively 多语言引入方案

> 基于 ClassLively v0.1.0（~17,000 行 Python）的现状分析，提出分阶段引入其他编程语言的方案。

---

## 一、现状痛点

| 痛点 | 现状代码 | 问题 |
|------|----------|------|
| Windows API 调用粗糙 | `ctypes` 手写结构体 + `subprocess` 调 PowerShell | 类型不安全、异常难调试、进程开销大 |
| 系统主题切换 | 改注册表 + `Stop-Process explorer` | 暴力重启资源管理器，用户桌面闪烁 |
| 壁纸设置 | `SystemParametersInfoW` ctypes 封装 | 重复实现（`downloader.py` 和 `wallpaper.py` 各一份） |
| 全局钩子 | `SetWindowsHookExW` + Python 回调 | Python GIL 导致钩子回调延迟，可能丢失事件 |
| 媒体会话读取 | `pymem` 读进程内存 + `uiautomation` | 脆弱，版本更新即失效；内存读取有安全风险 |
| 图像处理 | `QGraphicsBlurEffect` + `PIL` + `cv2` | Qt 模糊效果性能差，大图模糊卡顿 |
| 自启动管理 | `win32com.client` 写快捷方式 + 注册表 | 逻辑分散，`utils.py` 和 `ClassLively.py` 各有 |
| QSS 样式管理 | 18 个 QSS 文件 + 70 处内联 `setStyleSheet` | 深浅色切换需同步维护两套，内联样式无法统一管理 |

---

## 二、引入语言及适用场景

### 2.1 C/C++ — 系统交互层

**适用场景：** 所有 Windows API 交互、性能敏感模块

| 模块 | 当前实现 | C/C++ 替代方案 |
|------|----------|----------------|
| 全局键盘/鼠标钩子 | `ctypes` + Python HOOKPROC 回调 | 原生 DLL，回调在 C 层处理，只把结果传给 Python |
| 空闲检测 | `GetLastInputInfo` ctypes | 封装为 `idle_get_seconds()` 导出函数 |
| 单实例互斥锁 | `kernel32.CreateMutexW` ctypes | 封装为 `acquire_mutex(name) -> bool` |
| 壁纸设置 | `SystemParametersInfoW` ctypes（重复 2 处） | 统一封装为 `set_wallpaper(path)` |
| 系统主题切换 | 改注册表 + 重启 explorer | 调用 `ImmersiveColorSet` API 通知系统，无需重启 |
| 字体安装广播 | `AddFontResourceW` + `SendMessageTimeoutW` | 封装为 `install_font(path) -> int` |
| 图像模糊 | `QGraphicsBlurEffect`（CPU，每帧重绘） | OpenMP 并行高斯模糊，返回处理后的像素缓冲区 |
| 图标提取 | `win32gui.PrivateExtractIcons` + GDI 位图操作 | 原生 `SHGetFileInfo` / `IExtractIcon`，直接返回 RGBA 数据 |

**集成方式：** `pybind11` 或 `ctypes` 加载编译后的 `.pyd` / `.dll`

**预期收益：**
- 钩子回调延迟从毫秒级降到微秒级
- 壁纸模糊从 ~200ms 降到 ~20ms
- 消除所有 `subprocess` 调用（重启应用除外）
- 系统主题切换不再闪烁

**示例代码（pybind11）：**

```cpp
// native/sys.cpp
#include <pybind11/pybind11.h>
#include <Windows.h>

namespace py = pybind11;

int idle_get_seconds() {
    LASTINPUTINFO lii;
    lii.cbSize = sizeof(LASTINPUTINFO);
    if (GetLastInputInfo(&lii)) {
        return (GetTickCount() - lii.dwTime) / 1000;
    }
    return -1;
}

bool set_wallpaper(const std::string& path) {
    return SystemParametersInfoA(SPI_SETDESKWALLPAPER, 0,
        (void*)path.c_str(), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
}

PYBIND11_MODULE(classlively_native, m) {
    m.def("idle_get_seconds", &idle_get_seconds);
    m.def("set_wallpaper", &set_wallpaper);
}
```

```python
# Python 端调用
from classlively_native import idle_get_seconds, set_wallpaper

idle_sec = idle_get_seconds()
set_wallpaper(r"C:\wallpaper\wp_001.jpg")
```

---

### 2.2 QML — 壁纸主界面

**适用场景：** HomeInterface 的自由拖拽组件布局

当前 `home.py` 有 **3284 行**，是最大单文件，核心痛点：

| 问题 | 现状 | QML 方案 |
|------|------|----------|
| 组件定位 | 手动计算 `x/y` 像素 + 比例存储 | `AnchorLayout` / `Layout` 自动适配 |
| 动画效果 | `QPropertyAnimation` 手动管理 | `Behavior on x/y { SpringAnimation {} }` 声明式 |
| 深浅色切换 | 70 处内联 `setStyleSheet` | `Qt.application.palette` 自动响应 |
| 组件拖拽 | `mousePressEvent/mouseMoveEvent/mouseReleaseEvent` 重写 | `DragHandler {}` 一行搞定 |
| 字体/颜色 | Python 字符串拼接 QSS | QML 属性绑定 |

**集成方式：** `QQuickWidget` 嵌入现有 PyQt6 窗口

**迁移策略：** 逐步替换，不重写

```
阶段 1：新建 QML 壁纸层（背景 + 模糊 + 暗化）
阶段 2：迁移时钟组件 → QML
阶段 3：迁移天气/诗词组件 → QML
阶段 4：迁移倒计时/学校信息 → QML
阶段 5：迁移媒体播放器 → QML
```

**示例代码：**

```qml
// HomeView.qml
import QtQuick 2.15
import QtQuick.Controls 2.15

Rectangle {
    id: root
    color: "transparent"

    // 背景壁纸
    Image {
        id: wallpaper
        anchors.fill: parent
        source: "file:///" + wallpaperPath
        fillMode: Image.PreserveAspectCrop

        // 模糊效果 - GPU 加速
        layer.enabled: blurRadius > 0
        layer.effect: FastBlur {
            radius: blurRadius
        }

        // 暗化遮罩
        Rectangle {
            anchors.fill: parent
            color: "black"
            opacity: brightness  // -1.0 ~ 0.0
        }
    }

    // 时钟组件 - 声明式动画
    Text {
        id: clock
        text: currentTime
        font.pixelSize: clockSize
        color: clockColor
        x: parent.width * clockX
        y: parent.height * clockY

        Behavior on x { SpringAnimation { spring: 2; damping: 0.2 } }
        Behavior on y { SpringAnimation { spring: 2; damping: 0.2 } }

        DragHandler {
            onTranslationChanged: {
                clockX = clock.x / root.width
                clockY = clock.y / root.height
            }
        }
    }
}
```

```python
# Python 端嵌入
from PyQt6.QtQuickWidgets import QQuickWidget

quick_widget = QQuickWidget(self)
quick_widget.setSource(QUrl.fromLocalFile("qml/HomeView.qml"))
quick_widget.rootContext().setContextProperty("wallpaperPath", wp_path)
quick_widget.rootContext().setContextProperty("blurRadius", blur_value)
```

---

### 2.3 Rust — 安全系统模块

**适用场景：** 替代 `pymem` 内存读取、系统级操作

| 模块 | 当前实现 | Rust 替代方案 |
|------|----------|---------------|
| 网易云音乐读取 | `pymem` 读进程内存，硬编码偏移 | `windows-rs` crate，类型安全的 Win32 API |
| QQ 音乐读取 | `pymem` + `uiautomation` | `windows-rs` UI Automation COM 接口 |
| GSMTC 媒体会话 | `winsdk` (Python) + async | `windows-rs` `GlobalSystemMediaTransportControlsSessionManager` |
| 进程优先级设置 | `kernel32.OpenProcess` ctypes | `windows-rs` `SetPriorityClass` |
| 自启动管理 | `win32com.client` 写快捷方式 | `windows-rs` `IShellLinkW` COM |

**集成方式：** `PyO3` 编写 Python 扩展

**预期收益：**
- 消除 `pymem` 依赖（进程内存读取改为安全的 Win32 API）
- 消除 `pywin32` / `pywin32-ctypes` / `comtypes` 三个重叠依赖
- 类型安全，编译期捕获 API 调用错误

**示例代码：**

```rust
// src/media.rs
use pyo3::prelude::*;
use windows::Media::Control::{
    GlobalSystemMediaTransportControlsSessionManager,
    GlobalSystemMediaTransportControlsSession,
};

#[pyfunction]
fn get_media_info() -> PyResult<Option<MediaInfo>> {
    let manager = GlobalSystemMediaTransportControlsSessionManager::RequestAsync()
        .map_err(|e| pyo3::exceptions::RuntimeError::new_err(e.to_string()))?
        .get()
        .map_err(|e| pyo3::exceptions::RuntimeError::new_err(e.to_string()))?;

    let session = manager.GetCurrentSession()
        .map_err(|e| pyo3::exceptions::RuntimeError::new_err(e.to_string()))?;

    // ... 安全地获取媒体信息
    Ok(Some(MediaInfo { title, artist, .. }))
}

#[pymodule]
fn classlively_media(_py: Python, m: &PyModule) -> PyResult<()> {
    m.add_function(wrap_pyfunction!(get_media_info, m)?)?;
    Ok(())
}
```

---

### 2.4 C# — Windows 生态快捷方案

**适用场景：** 快速解决 Windows 特定问题，团队熟悉 .NET 的情况

| 模块 | 当前实现 | C# 替代方案 |
|------|----------|-------------|
| 系统主题切换 | 改注册表 + 重启 explorer | `Windows.UI.ViewManagement.UISettings` 事件监听 |
| 壁纸设置 | `SystemParametersInfoW` | `Microsoft.Windows.SDK` `DesktopWallpaper` COM |
| 自启动管理 | 注册表 + 快捷方式 | `Shell` 命名空间，一行代码 |
| 文件关联 | 无 | `ApplicationAssociation` API |

**集成方式：** `pythonnet` 调用 .NET 程序集

**示例代码：**

```python
# 通过 pythonnet 调用 C#
import clr
clr.AddReference("System")
from System import Environment, IO

# 设置壁纸 - 一行搞定
from Microsoft.Windows.SDK import DesktopWallpaper
dw = DesktopWallpaper()
dw.SetWallpaper(None, r"C:\wallpaper\wp_001.jpg")

# 监听系统主题变化
from Microsoft.Windows.SDK import UISettings
settings = UISettings()
settings.ColorValuesChanged += on_system_theme_changed
```

---

## 三、推荐路线图

### 阶段 1：C/C++ 系统层封装（优先级最高）

**目标：** 消除所有 ctypes 手写代码和 subprocess 调用

```
classlively_native/
├── CMakeLists.txt
├── pyproject.toml          # pybind11 构建
├── src/
│   ├── sys.cpp             # 空闲检测、互斥锁、字体安装
│   ├── wallpaper.cpp       # 壁纸设置（统一入口）
│   ├── theme.cpp           # 系统主题切换（不重启 explorer）
│   ├── hook.cpp            # 全局键盘/鼠标钩子
│   ├── icon.cpp            # 图标提取
│   └── image.cpp           # 高斯模糊（OpenMP 加速）
└── tests/
    ├── test_sys.py
    ├── test_wallpaper.py
    └── ...
```

**替换清单：**

| 文件 | 行号 | 当前代码 | 替换为 |
|------|------|----------|--------|
| `ClassLively.py` | 1340-1420 | ctypes 空闲检测 | `native.idle_get_seconds()` |
| `ClassLively.py` | 1370-1420 | ctypes 全局钩子 | `native.install_hook(callback)` |
| `ClassLively.py` | 1083-1106 | subprocess 改注册表主题 | `native.set_system_theme(is_dark)` |
| `core/utils.py` | 互斥锁部分 | ctypes CreateMutex | `native.acquire_mutex(name)` |
| `core/utils.py` | 字体安装部分 | ctypes AddFontResource | `native.install_font(path)` |
| `core/downloader.py` | 壁纸设置 | ctypes SystemParametersInfo | `native.set_wallpaper(path)` |
| `ui/wallpaper.py` | 壁纸设置 | ctypes SystemParametersInfo | `native.set_wallpaper(path)` |
| `ui/home.py` | 图标提取 | win32gui + GDI | `native.extract_icon(path, size)` |

**构建方式：**

```bash
pip install pybind11 cmake
cd classlively_native && mkdir build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release
cmake --build . --config Release
# 产出 classlively_native.pyd
```

---

### 阶段 2：QML 主界面迁移

**目标：** 将 `home.py` 从 3284 行逐步迁移到 QML

```
qml/
├── HomeView.qml            # 主视图容器
├── components/
│   ├── ClockWidget.qml     # 时钟（含农历）
│   ├── WeatherWidget.qml   # 天气
│   ├── PoetryWidget.qml    # 一言/诗词
│   ├── CountdownWidget.qml # 倒计时
│   ├── SchoolWidget.qml    # 学校信息
│   ├── MediaWidget.qml     # 媒体播放器
│   └── QuickLaunch.qml     # 快捷启动栏
├── effects/
│   ├── BlurEffect.qml      # 高斯模糊
│   └── DimOverlay.qml      # 暗化遮罩
└── Style.qml               # 全局样式定义（深浅色统一管理）
```

**迁移优先级：**

1. **背景层**（壁纸 + 模糊 + 暗化）— 最独立，风险最低
2. **时钟** — 最常用，QML 动画效果最好
3. **天气/诗词** — 纯展示，数据从 Python 传入
4. **倒计时** — 含轮播动画
5. **媒体播放器** — 最复杂，最后迁移

---

### 阶段 3：Rust 媒体模块

**目标：** 替换 `pymem` + `winsdk` + `uiautomation`

```
classlively_media/
├── Cargo.toml
├── src/
│   ├── lib.rs              # PyO3 模块入口
│   ├── gsmtc.rs            # 系统媒体会话
│   ├── netease.rs          # 网易云音乐
│   ├── qqmusic.rs          # QQ 音乐
│   └── lyrics.rs           # 歌词解析
└── tests/
```

**可消除的依赖：**
- `pymem` → Rust `windows-rs`
- `winsdk` → Rust `windows-rs`
- `uiautomation` → Rust `windows-rs` UI Automation
- `pycaw` → Rust `windows-rs` Audio

---

### 阶段 4：C# 辅助（可选）

仅在团队更熟悉 .NET 时考虑。Rust 已覆盖大部分场景，C# 仅在需要 WPF/WinUI 组件时引入。

---

## 四、依赖影响分析

### 可消除的 Python 包

| 包名 | 大小 | 替代方案 | 阶段 |
|------|------|----------|------|
| `pymem` | ~50KB | Rust `windows-rs` | 3 |
| `pycaw` | ~30KB | Rust `windows-rs` | 3 |
| `uiautomation` | ~200KB | Rust `windows-rs` | 3 |
| `winsdk` | ~5MB | Rust `windows-rs` | 3 |
| `pywin32` | ~12MB | C++ pybind11 / Rust PyO3 | 1/3 |
| `pywin32-ctypes` | ~100KB | 同上 | 1/3 |
| `comtypes` | ~1MB | 同上 | 1/3 |
| `opencv-python-headless` | ~30MB | C++ OpenMP 模糊 | 1 |

### 新增构建依赖

| 依赖 | 用途 | 阶段 |
|------|------|------|
| `pybind11` | C++ Python 绑定 | 1 |
| `cmake` | C++ 构建 | 1 |
| `rustc` + `cargo` | Rust 编译 | 3 |
| `PyO3` + `maturin` | Rust Python 绑定 | 3 |
| Qt QML 模块 | QML 运行时 | 2 |

---

## 五、打包策略

### PyInstaller 适配

```python
# ClassLively.spec 关键修改
a = Analysis(
    ['ClassLively.py'],
    datas=[
        ('classlively_native.pyd', '.'),       # C++ 扩展
        ('classlively_media.pyd', '.'),         # Rust 扩展
        ('qml/*.qml', 'qml'),                   # QML 文件
        ('qml/components/*.qml', 'qml/components'),
        ('qml/effects/*.qml', 'qml/effects'),
    ],
    binaries=[
        ('classlively_native.pyd', '.'),
        ('classlively_media.pyd', '.'),
    ],
    # ...
)
```

### 构建流水线

```
┌─────────────┐    ┌──────────────┐    ┌──────────────┐
│ C++ 编译     │───▶│ Rust 编译     │───▶│ Python 打包   │
│ pybind11    │    │ PyO3/maturin │    │ PyInstaller  │
│ → .pyd      │    │ → .pyd       │    │ → .exe       │
└─────────────┘    └──────────────┘    └──────────────┘
```

---

## 六、风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| 多语言增加构建复杂度 | CI/CD 流水线变长 | 用 GitHub Actions matrix 并行构建 |
| QML + PyQt6 混合渲染 | 可能有性能问题 | `QQuickWidget` 共享 OpenGL 上下文 |
| Rust 学习曲线 | 开发效率短期下降 | 先用 C++ 覆盖系统层，Rust 仅限媒体模块 |
| C++ 内存安全 | 可能引入崩溃 | pybind11 管理生命周期，关键路径加 RAII |
| 多平台兼容性 | 目前仅 Windows | 用 `#ifdef _WIN32` 隔离平台代码 |

---

## 七、总结

| 语言 | 职责 | 引入阶段 | 核心价值 |
|------|------|----------|----------|
| **C/C++** | 系统交互 + 性能优化 | 阶段 1 | 消除 ctypes/subprocess，性能提升 10x |
| **QML** | 主界面组件化 | 阶段 2 | 代码量减半，动画更流畅，深浅色统一管理 |
| **Rust** | 媒体模块 + 安全系统调用 | 阶段 3 | 消除 pymem 等不安全依赖，类型安全 |
| **C#** | Windows 生态快捷方案 | 阶段 4（可选） | 仅在需要 .NET 生态时引入 |

**建议从 C/C++ 系统层封装开始**，这是投入产出比最高的方向——直接解决当前的 ctypes 痛点，且不改变现有 Python 架构。
