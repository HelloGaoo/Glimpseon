# 快速开始

> [!NOTE]
> 编写者：HelloGaoo　最后修改：2026/09/12

## 1. 环境要求

| 项      | 要求                                                 |
| ------ | -------------------------------------------------- |
| 操作系统   | Windows 10 / 11（x64）                               |
| Python | 3.11                                               |
| 构建工具   | CMake ≥ 3.15、Visual Studio（MSVC，支持 C++17）、pybind11 |
| GPU    | 可选；启用 `enableGpuAcceleration` 时使用 OpenGLES         |

> [!WARNING]
> 原生扩展需匹配 Python 版本。仓库已附 `Glimpseon_native.cp311-win_amd64.pyd`，若用其它 Python 版本需自行重新编译（见 [原生扩展](native-extension.md)）。

## 2. 获取代码

```bash
git clone https://github.com/HelloGaoo/Glimpseon.git
cd Glimpseon
```

## 3. 安装依赖

建议使用虚拟环境：

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1

# 安装 pymem（仓库含 whl）
pip install pymem-1.14.0-py3-none-any.whl

# 安装其余依赖
pip install -r requirements.txt
```

依赖清单见 [requirements.txt](https://github.com/HelloGaoo/Glimpseon/blob/main/requirements.txt)。关键依赖：

- `PyQt6`、`PyQt6-Fluent-Widgets`、`PyQt6-Frameless-Window`、`shiboken6`
- `PyQt6-WebEngine`（HTML）
- `py7zr`、`requests`（下载/解压）
- `pycaw`、`pywin32`、`uiautomation`、`comtypes`（Windows）
- `easyocr`、`torch`、`opencv-python-headless`（OCR / 图像）
- `cnlunar`（农历）
- `darkdetect`（系统主题检测）

## 4. 运行

### 方式 A：通过启动器

```powershell
python Glimpseon.py
```

启动器（[Glimpseon.py](https://github.com/HelloGaoo/Glimpseon/blob/main/Glimpseon.py)）扫描所有 `app-*` 目录，读取每个 `record.json`，运行最适合的版本。

`record.json` 关键字段：

| 字段           | 含义                   |
| ------------ | -------------------- |
| `version`    | 版本字符串，同时决定 `VERSION` |
| `build_date` | 构建日期，决定 `BUILD_DATE` |
| `current`    | `1` 表示当前激活版本         |
| `partial`    | `true` 表示不完整，启动器会跳过  |

### 方式 B：直接运行主程序

```powershell
cd app-1.0.0
python GlimpseonMain.py
```

> [!NOTE]
> 直接运行时无环境变量，[core/paths.py](https://github.com/HelloGaoo/Glimpseon/blob/main/app-1.0.0/core/paths.py) 会回退为基于 `__file__` 推导 `PACKAGE_ROOT`（上三级，`paths.py` 位于 `app-*/core/`，三级父目录即包根）。

## 5. 首次运行

- 若 `data/config/Setup_Wizard.json` 不存在或 `completed != 1`，将弹出 `WizardWindow` 向导
- 同意协议并完成后，向导写入 `completed: 1`，之后不再显示。

## 6. 打包（PyInstaller）

项目内置 PyInstaller 打包支持。关键点：

1. `Glimpseon_native.pyd` 随包携带
2. 资源目录 `resource/`、`font/`、`locale/`、`glimpseon_native/`、`Tools/` 必须打包！！！！！
3. 打包后经 `_MEIPASS` 访问内置资源（`get_resource_path`会找）
4. `data/` 不打包

## 7. 调试技巧

- **调试模式**：开启设置中的 DebugMode（或 `cfg.debugMode`）后：
  - 日志条目数与保留天数降到最小值。
  - 主窗口底部导航显示「调试」面板（`DebugPanel`）。
  - 跳过单实例检查（多开调试）。
  - 按 `F12` 跳转到调试面板。
- **日志位置**：`data/log/`，格式 `precise_time|level|caller|module:line|message`。
- **多开**：`cfg.allowMultipleInstances = True` 或 DebugMode 下可绕过单例锁。

> [!NOTE]
> os：其实我觉得这个调试模式非常没用

## 8. 常见问题

| 现象         | 排查                                                               |
| ---------- | ---------------------------------------------------------------- |
| 启动白屏       | `splash.close()` 前的 `allow_ui_update` 未充分让事件循环处理；检查 `enableGpuAcceleration` |
| 找不到 app 目录 | 确认 `app-*/record.json` 存在且 `partial != true`                     |
| QSS 不生效    | 切主题后需 `clear_qss_cache()`；检查 `resource/qss/{theme}/`             |
| 图标不显示      | 确认 `resource/icons/CY.png` 与 `resource/fluent/{theme}/` 存在       |
| 字体异常       | `initialize_fonts(install_to_system=True)` 会装 HarmonyOS Sans 到系统 |
| 原生模块导入失败   | 确认 Python 版本为 3.11 x64；或重新编译 `glimpseon_native`                  |

