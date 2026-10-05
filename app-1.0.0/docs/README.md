# Glimpseon 开发者文档

> [!NOTE]
> 编写者：HelloGaoo 最后修改：2026/10/05

Glimpseon 是一款基于 Avalonia 的 Windows 桌面信息看板

## 文档索引

| 文档                             | 内容                                  |
| ------------------------------ | ----------------------------------- |
| [架构总览](architecture.md)        | 技术栈 分层结构 全局对象 数据目录                  |
| [快速开始](getting-started.md)     | 环境要求 构建 运行 调试                       |
| [目录结构](directory-structure.md) | 仓库根目录 版本目录 core ui locale assets 详解 |
| [启动流程](launch-flow.md)         | 启动器 > 主程序初始化 > 主窗口的完整时序             |
| [核心模块](core-modules.md)        | core/ 下各模块实现                        |
| [服务模块](services.md)            | core/Services/ 下数据获取服务              |
| [UI 模块](ui-modules.md)         | ui/ 下各界面实现                          |
| [组件系统](component-system.md)    | 网格布局 组件定义 页面管理 编辑模式                 |
| [配置系统](configuration.md)       | 配置项机制与全表                            |
| [版本机制](Versioning.md)          | 启动器选择 record.json 更新流水线 回退          |

## 项目入口

- 顶层启动器：Glimpseon.cs (编译为 Glimpseon.exe)
- 主程序：app-1.0.0/GlimpseonMain.cs (编译为 GlimpseonMain.exe)

## 版本

版本号与构建日期来自 app-1.0.0/record.json 由 core/Paths.cs 读取为 Paths.Version / Paths.BuildDate

