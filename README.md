# Glimpseon

基于 Avalonia 的 Windows 桌面信息看板

## 功能

\-

## 构建

```
build.bat
```

 dotnet build Glimpseon.slnx > 产物 Glimpseon.exe (启动器) + app-1.0.0/GlimpseonMain.exe (主程序)

## 目录结构

```
Glimpseon.cs               启动器
app-1.0.0/                 版本目录
  GlimpseonMain.cs         入口
  core/                    配置 日志 路径 下载 更新 课表 联动 组件模型
  core/Services/           数据源
  core/Win32/              P/Invoke
  ui/                      Avalonia
  locale/                  翻译
  Assets/                  资源
  docs/                    文档
data/                      数据
doc/ChangeLogs/            更新日志
```

## 开发者文档

见 [app-1.0.0/docs/README.md](app-1.0.0/docs/README.md) > 架构总览 / 快速开始 / 目录结构 / 启动流程 / 核心模块 / 服务模块 / UI 模块 / 组件系统 / 配置系统 / 版本机制

## 许可

GPL-3.0 详见 LICENSE
