# 原生扩展（glimpseon\_native/）

> [!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

`glimpseon_native` 是用 C++17 + pybind11 编写的，编译为 `Glimpseon_native.pyd`（cp311-win\_amd64）。Python通过 `import Glimpseon_native` 调用。我不会c++，所以此扩展与此文档由ai生成。

***

## 1. 构建

[CMakeLists.txt](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/glimpseon_native/CMakeLists.txt)

```cmake
cmake_minimum_required(VERSION 3.15)
project(Glimpseon_native VERSION 0.1.0 LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
add_compile_options("/utf-8" "/openmp")   # OpenMP 并行

find_package(Python 3.8 REQUIRED COMPONENTS Interpreter Development.Module)
find_package(pybind11 CONFIG REQUIRED)

pybind11_add_module(Glimpseon_native
    src/wallpaper.cpp src/image.cpp src/hook.cpp src/sys.cpp)
```

### 1.1 构建步骤

```powershell
cd app-1.0.0/glimpseon_native
cmake -B build -S . -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
# 产物：build/Release/Glimpseon_native.cp311-win_amd64.pyd
```

### 1.2 依赖

- MSVC（支持 C++17）
- Windows SDK（Direct2D / D3D11 / DXGI / shellapi）
- pybind11
- 链接库：`d2d1.lib`、`d3d11.lib`、`dxgi.lib`、`dxguid.lib`、`shell32.lib`

### 1.3 注意

- 编译目标 Python 版本必须与运行时一致（仓库附带为 cp311）。
- `/openmp` 启用 OpenMP，用于 `blur_image` 的并行像素处理回退路径。
- `glimpseon_native/` 旧产物`classlively_native.pyd`；运行时导入的是 `Glimpseon_native.pyd`。

***

## 2. 导出 API（PYBIND11_MODULE）

[wallpaper.cpp](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/glimpseon_native/src/wallpaper.cpp) 的 `PYBIND11_MODULE(Glimpseon_native, m)` 统一注册全部导出函数。Python 侧可见 API：

| 函数                          | 签名                                                       | 说明            |
| --------------------------- | -------------------------------------------------------- | ------------- |
| `set_wallpaper`             | `(path: str) -> bool`                                    | 设置桌面壁纸        |
| `blur_image`                | `(input: bytes, w: int, h: int, radius: float) -> bytes` | 高斯模糊（BGRA）    |
| `install_hook`              | `() -> None`                                             | 安装全局低级钩子      |
| `uninstall_hook`            | `() -> None`                                             | 卸载钩子          |
| `was_page_operation_recent` | `(ms_threshold: int) -> bool`                            | 最近是否有翻页/滚轮操作  |
| `idle_get_milliseconds`     | `() -> int`                                              | 系统空闲毫秒（-1 失败） |
| `idle_get_seconds`          | `() -> int`                                              | 系统空闲秒         |
| `acquire_mutex`             | `(name: str) -> bool`                                    | 获取命名互斥锁       |
| `release_mutex`             | `() -> None`                                             | 释放互斥锁         |
| `install_font`              | `(path: str) -> int`                                     | 安装字体，返回添加数量   |
| `extract_icon`              | `(path: str, size: int = 256) -> (w, h, bgra_bytes)`     | 提取 exe/dll 图标 |

***

## 3. wallpaper.cpp — 桌面壁纸

`set_wallpaper(path)` 经 `SystemParametersInfoA(SPI_SETDESKWALLPAPER, ..., SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)` 设置壁纸：持久化并广播 `WM_SETTINGCHANGE`。

***

## 4. image.cpp — Direct2D 高斯模糊

[image.cpp](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/glimpseon_native/src/image.cpp)

### 4.1 管线

```
输入 BGRA 像素
  → D3D11 Texture (DXGI_FORMAT_B8G8R8A8_UNORM)
  → DXGI Surface → D2D Bitmap (目标 + 源)
  → CLSID_D2D1GaussianBlur Effect (standardDeviation = radius, BorderMode = Hard)
  → BeginDraw / DrawImage / EndDraw
  → CopyResource 到 Staging Texture
  → Map 读回 BGRA bytes
```

### 4.2 设备初始化（ensure\_init）

- `D2D1CreateFactory`（单线程）。
- `D3D11CreateDevice` 优先 `D3D_DRIVER_TYPE_HARDWARE`（GPU），失败回退 `D3D_DRIVER_TYPE_WARP`（软件光栅）。
- 均带 `D3D11_CREATE_DEVICE_BGRA_SUPPORT` 标志。
- 全局单例：`g_d2d_factory` / `g_d3d_device` / `g_d3d_ctx` / `g_d2d_device`。

### 4.3 关键参数

- 像素格式：`DXGI_FORMAT_B8G8R8A8_UNORM`，alphaMode `PREMULTIPLIED`。
- DPI：96.0。
- 目标 Bitmap：`D2D1_BITMAP_OPTIONS_TARGET | CANNOT_DRAW`。
- radius ≤ 0 或尺寸非法时直接拷贝原数据返回（不模糊）。
- 任意步骤失败回退为原像素拷贝（`fail:` 标签）。

### 4.4 Python 入口

`blur_image` 接受 buffer 协议对象（BGRA 像素），返回模糊后 bytes，对应壁纸 `backgroundBlurRadius`（0~30）。

***

## 5. hook.cpp — 全局输入钩子

[hook.cpp](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/glimpseon_native/src/hook.cpp)

### 5.1 用途

检测 PageUp / PageDown 按键与鼠标滚轮，用于「最近是否有翻页操作」判断（驱动 UI 翻页行为抑制等）。

### 5.2 实现

- `WH_KEYBOARD_LL` 低级键盘钩子，捕获 `VK_PRIOR`(33) / `VK_NEXT`(34)。
- `WH_MOUSE_LL` 低级鼠标钩子，捕获 `WM_MOUSEWHEEL`。
- 命中时更新 `g_last_page_tick = GetTickCount64()`。
- `was_page_operation_recent(ms_threshold)`：`(now - g_last_page_tick) < ms_threshold`，正确处理 49.7 天溢出。

### 5.3 生命周期

`install_hook` 幂等（已安装则跳过）；`uninstall_hook` 释放两个 `HHOOK`。钩子运行在 Windows 钩子线程，需消息泵支持（Qt 事件循环满足）。

***

## 6. sys.cpp — 系统工具

[sys.cpp](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/glimpseon_native/src/sys.cpp)

### 6.1 空闲检测

- `idle_get_milliseconds()`：`GetLastInputInfo` + `GetTickCount` 差值。
- `idle_get_seconds()`：毫秒除 1000。
- 由 `MainWindow._initIdleDetection` 配合 `cfg.idleMinutes` / `autoOpenOnIdle` 使用。

### 6.2 单例互斥锁

```cpp
bool acquire_mutex(const std::string& name) {
    g_mutex_handle = CreateMutexW(nullptr, TRUE, wname.c_str());
    if (GetLastError() == ERROR_ALREADY_EXISTS) { /* 已有实例 */ return false; }
    return true;
}
```

- 全局唯一 `g_mutex_handle`，已持有时直接返回 true。
- 创建失败也放行（避免锁死）。
- `release_mutex`：`ReleaseMutex` + `CloseHandle`。
- Python 侧由 `core.utils.SingleInstanceManager` 封装，Mutex 名 `Glimpseon_SingleInstance_Mutex_{GUID}`。

### 6.3 字体安装

`install_font(path)`：`AddFontResourceW`，返回添加数量。供 `initialize_fonts(install_to_system=True)` 安装 HarmonyOS Sans。

### 6.4 图标提取

`extract_icon(path, size=256)`：

1. `SHGetFileInfoW` 取 `SHGFI_ICON | SHGFI_LARGEICON`。
2. `GetIconInfo` 取 HBITMAP。
3. 创建兼容 DC + Bitmap，`DrawIconEx` 缩放绘制到目标尺寸。
4. `GetDIBits` 以 `BITMAPINFOHEADER`（`biHeight = -h` 自上而下，32bpp BGRA）读回像素。
5. 返回 `(w, h, py::bytes)`，Python 侧可构造 `QImage`。

