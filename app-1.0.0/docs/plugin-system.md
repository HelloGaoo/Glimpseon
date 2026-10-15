# 插件系统

> [!NOTE]
> 编写者：HelloGaoo 最后修改：2026/10/14

基于 ClassIsland 与 Class-Widgets-2 插件体系调研后的 Glimpseon 首版插件方案 覆盖包格式 加载隔离 生命周期 能力面 设置页 与验证计划

## 1 参考实现调研

| 维度 | ClassIsland (.NET/Avalonia 同栈 主参考) | Class-Widgets-2 (Python/QML) |
| ---- | -------------------------------------- | ---------------------------- |
| 包格式 | .cipx = zip + manifest.yml | zip + plugin.json |
| 安装目录 | Plugins/`<id>`/ | plugins/`<id>`/ |
| 启停标记 | 目录内 .disabled 文件 | 配置内 enabled 集合 |
| 隔离 | 每插件独立 PluginLoadContext (ALC) | Python import 隔离 |
| 生命周期 | PluginBase Initialize(context IServiceCollection) | CW2Plugin on_load/on_unload |
| 版本门禁 | ApiVersion >= 2.0.0 拒载 | 无强门禁 |
| 失败处理 | 收集到 PluginLoadedStatus 不崩宿主 | try/except 单插件失败不崩 |
| 市场 | PluginRepoManifest 仓库索引 JSON | 广场在线下载 |

采纳结论

- 包格式采纳 ClassIsland 思路 换 JSON 清单 (不引 YamlDotNet) 扩展名 .gplugin
- 启停采纳 CW2 风格 存 `data/config/plugins.json` 与设置页开关一一对应 不用 .disabled 文件
- 隔离与门禁采纳 ClassIsland 独立 ALC + apiVersion 门禁
- 生命周期采纳 CW2 极简 OnLoad/OnUnload 不引 DI 容器
- 市场采纳 ClassIsland 索引仓库 + CW2 广场下载形态 从 GitHub 索引仓库拉取列表 分布式 PR 收录 本地导入入口保留

## 2 总体架构

```
启动 (GlimpseonMain.RunStartupAsync)
  > PluginService.InitializePlugins
      > 扫描 data/plugins/ 子目录 读 manifest.json
      > 校验 id/entrance/apiVersion
      > enabled=false 跳过  记录状态
      > PluginLoadContext 加载入口程序集 (ALC 隔离)
      > 找 GlimpseonPlugin 派生类 > 实例化 > 注入 PluginContext > OnLoad()
      > 注册物落入 PluginService 注册表 (组件定义 / 组件工厂 / 课表提供者)
  > MainWindow 构造
      > HomeView: ComponentRegistry.RegisterBatch(内置) + RegisterBatch(插件定义)
      > 组件实例化: StyleWidgetFactory.Create 先查插件工厂 再走内置 switch
      > 课表组件: TimetableScheduleProvider.GetTodaySchedule 按来源分发 (plugin:<id> > 联动桥 > 本地档案)

市场链路 (设置 > 插件 > 市场分区)
  > 拉索引 plugins.json (配置源 > jsDelivr 镜像 > 旧缓存)
  > 浏览 > 下载 .gplugin > sha256 校验 > InstallPackage > 重启加载
```

- 任一插件加载失败只收集 Error 不中断启动 不影响其他插件
- 启停/安装/卸载均在重启后生效 (沿用 AppUtils.RequestRestart)

## 3 目录与文件

### 3.1 运行期目录

```
data/
  plugins/                      插件安装根目录 (Paths.DataPlugins 新增)
    <pluginId>/                 单插件目录 目录名必须等于 manifest.id
      manifest.json             清单
      <entrance>.dll            入口程序集
      <entrance>.deps.json      可选 依赖清单 (AssemblyDependencyResolver 用)
      其余依赖 dll               可选 随包分发
      config.json               插件私有配置 (PluginContext 读写)
      icon.png                  可选 清单 icon 指向的图标
      README.md                 可选
  config/plugins.json           启停状态 (宿主管辖 插件不可写)
  cache/plugin-packages/        安装包解压暂存
```

### 3.2 代码改动清单

| 文件 | 动作 | 内容 |
| ---- | ---- | ---- |
| core/Plugins.cs | 新建 | PluginManifest / GlimpseonPlugin / PluginContext / PluginService / PluginLoadContext / PluginMarket (索引拉取 下载安装) |
| core/Paths.cs | 修改 | 新增 DataPlugins 属性 EnsureDataDirs 增列该目录 |
| GlimpseonMain.cs | 修改 | RunStartupAsync 在 LogConfigSummary() 之后调用 PluginService.InitializePlugins() |
| core/AppConfig.cs | 修改 | 新增 Plugins/MarketSource 配置项 (默认官方索引 raw 地址) |
| ui/HomeView.axaml.cs | 修改 | 构造中 RegisterBatch(内置) 之后追加 RegisterBatch(PluginService.PluginComponentDefinitions) |
| ui/Components.axaml.cs | 修改 | StyleWidgetFactory.Create 开头前置插件工厂查询 TimetableScheduleProvider.GetTodaySchedule 增插件来源分发 |
| ui/ScheduleWindow.axaml.cs | 修改 | 档案页来源下拉动态追加插件提供者选项 |
| ui/SettingsWindow.axaml | 修改 | 追加 NavPlugins 页签 (Tag="plugins") |
| ui/SettingsWindow.axaml.cs | 修改 | NavItems / SetNavLabels / BuildPages 追加插件页 BuildPluginsPage (已安装/市场分区切换) |
| locale/zh_CN.json 等 | 修改 | settings.plugins.* 与 market 词条 三语言 |
| docs/plugin-system.md | 新建 | 本文档 |
| docs/core-modules.md 等 | 修改 | 增插件模块条目与目录说明 |
| examples/PluginSample/ | 新建 | 示例插件工程 (独立 csproj 不入宿主编译) |

## 4 manifest.json 规范

| 字段 | 类型 | 必填 | 说明 |
| ---- | ---- | ---- | ---- |
| id | string | 是 | 反向域名式全小写 如 com.example.sample 等于目录名 |
| name | string | 是 | 显示名 |
| version | string | 是 | 语义化版本 |
| author | string | 否 | 作者 |
| description | string | 否 | 一句话描述 |
| entrance | string | 是 | 入口 dll 相对路径 如 PluginSample.dll |
| apiVersion | int | 是 | 目标宿主 API 版本 当前仅 1 |
| icon | string | 否 | 图标相对路径 |
| readme | string | 否 | 说明文档相对路径 |

校验规则 (全部不满足即拒载并记 Error)

- id 非空 且等于所在目录名
- entrance 指向的文件存在
- apiVersion 为 1 (低于 1 或高于 HostApiVersion=1 均拒载)

示例

```json
{
  "id": "com.example.sample",
  "name": "示例插件",
  "version": "1.0.0",
  "author": "HelloGaoo",
  "description": "演示插件系统能力的最小插件",
  "entrance": "PluginSample.dll",
  "apiVersion": 1,
  "icon": "icon.png",
  "readme": "README.md"
}
```

## 5 插件包 .gplugin 与安装卸载

包结构 zip 扩展名 .gplugin manifest.json 必须在 zip 根 (不允许单根目录套壳 兼容: 解压后若根仅有唯一子目录则下钻一层)

安装流程 (PluginService.InstallPackage)

1. ZipFile.OpenRead 校验根存在 manifest.json
2. 解压到 data/cache/plugin-packages/<随机名>/
3. 解析 manifest 走第 4 节校验 (id/entrance/apiVersion)
4. 目标 data/plugins/<id>/ 已存在则先删除 (旧版本覆盖安装)
5. 目录搬移 校验失败清理暂存 返回错误信息

卸载流程 (PluginService.Uninstall)

- 直接删除 data/plugins/<id>/
- 该插件已加载时 dll 被锁定删除失败 > 返回错误 提示先禁用并重启后再卸载 (重启后未加载 删除必然成功)

## 6 插件 SDK (core/Plugins.cs)

宿主程序集即 SDK 插件工程直接 ProjectReference GlimpseonMain.csproj 同一程序集内类型天然统一

### 6.1 生命周期基类

```csharp
namespace Glimpseon.Core;

/// <summary>插件生命周期基类 宿主在启动时实例化并调 OnLoad</summary>
public abstract class GlimpseonPlugin
{
    public PluginContext Context { get; internal set; } = null!;
    public abstract void OnLoad();
    public virtual void OnUnload() { }
}
```

- 首版 OnUnload 仅语义预留 (启停/卸载经重启生效 运行期不调用)

### 6.2 PluginContext API

| 成员 | 签名 | 说明 |
| ---- | ---- | ---- |
| Id | string | 清单 id |
| Directory | string | 插件目录绝对路径 |
| ApiVersion | int | 清单 apiVersion |
| LogInfo / LogWarning / LogError | void(string) | 写宿主日志 行首缀 `[plugin:<id>]` |
| ReadConfig | JsonObject() | 读插件目录 config.json 缺失返回空对象 |
| WriteConfig | void(JsonObject) | 写插件目录 config.json 缩进格式 |
| RegisterComponent | void(ComponentDefinition, Func&lt;JsonElement?, Control&gt;) | 注册主界面组件定义与真身工厂 |
| RegisterScheduleProvider | void(string name, Func&lt;List&lt;ScheduleRow&gt;&gt;) | 注册课表数据提供者 |
| PushAnnouncement | void(string text) | 经 Announcements.Append 追加右下角公告 |

约束

- RegisterComponent 的定义 Id 必须形如 `<type>_<style>` 拆分规则沿用组件库首个下划线切分 (HomeView.AddComponentById)
- 定义 Id 建议携带插件特征前缀避免与内置冲突 后注册覆盖内置 (RegisterBatch 语义)
- 工厂在 UI 线程调用 返回任意 Avalonia Control 宿主包入 DraggableContainer 并落定义默认宽高
- RegisterScheduleProvider 同插件重复注册按 name 覆盖

### 6.3 示例

```csharp
public class SamplePlugin : GlimpseonPlugin
{
    public override void OnLoad()
    {
        Context.LogInfo("已加载");
        Context.RegisterComponent(new ComponentDefinition
        {
            Id = "sample_hello",
            DisplayName = "示例组件",
            Category = "Tools",
            Icon = "Puzzle",
        }, _ => new TextBlock { Text = "Hello Plugin" });
        Context.RegisterScheduleProvider("示例课表", () =>
        {
            var now = AppUtils.PreciseNow();
            return [new ScheduleRow("自习", "", now.ToString("HH:mm"), now.AddMinutes(40).ToString("HH:mm"), 1, false, false, "")];
        });
    }
}
```

## 7 加载器 (PluginService)

### 7.1 状态模型

```csharp
public sealed class LoadedPlugin
{
    public required PluginManifest Manifest;
    public required string Directory;
    public required bool Enabled;        // plugins.json 缺省视为 true
    public GlimpseonPlugin? Instance;    // null = 未加载或失败
    public string? Error;                // 校验/加载/OnLoad 失败原因
}
```

- `Plugins` 有序列表 (按目录名排序) 驱动设置页渲染
- 注册表: `PluginComponentDefinitions` (List<ComponentDefinition>) / `PluginComponentFactories` (typeId > 工厂) / `ScheduleProviders` (pluginId > (name provider))

### 7.2 加载算法 InitializePlugins

1. EnsureDataPlugins 目录
2. 读 data/config/plugins.json 的 enabled 映射
3. 枚举 data/plugins/ 一级子目录 按名称排序
4. 每目录: manifest.json 缺失或解析失败 > 记 Error 继续
5. manifest 校验 (第 4 节规则) 失败 > 记 Error 继续
6. enabled=false > 记录状态跳过加载
7. new PluginLoadContext(入口dll 全路径) > LoadFromAssemblyPath
8. 扫描程序集类型 (ReflectionTypeLoadException 时取 ex.Types 非空子集) 找 GlimpseonPlugin 非抽象派生类且带无参构造
9. Activator.CreateInstance > 注入 Context > try/catch 调 OnLoad > 异常记 Error
10. OnLoad 内注册物进入注册表 (同一 typeId 重复注册覆盖 按加载顺序后者胜)
11. 汇总日志: 扫描 N 启用 M 加载成功 K 失败 J

### 7.3 PluginLoadContext (ALC 隔离)

```csharp
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string entranceDll)
        : base($"plugin:{Path.GetFileNameWithoutExtension(entranceDll)}")
        => _resolver = new AssemblyDependencyResolver(entranceDll);

    protected override Assembly? Load(AssemblyName name)
    {
        // 宿主与共享 UI 程序集返回 null 回落 Default ALC 保证类型同一
        if (name.Name is "GlimpseonMain" or "Avalonia.Base" or "Avalonia.Controls"
            or "Avalonia.Skia" or "Avalonia.Markup" or "FluentAvalonia")
        {
            return null;
        }
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
```

- 显式列出宿主程序集名 防插件包内私搭同名 dll 造成类型分裂
- 其余依赖经 deps.json 解析 解析不到自然回落 Default ALC

### 7.4 失败策略

- 全链路 try/catch 每个插件独立失败互不传染
- 失败信息进 LoadedPlugin.Error (设置页可见) 同时写宿主日志 Warning/Error
- ProfileSource 指向已卸载或加载失败的插件提供者 > 分发链按序回落本地档案 不崩

## 8 能力面接线

### 8.1 主界面组件

- 定义注入 > HomeView 构造 RegisterBatch(内置) 后追加 `ComponentRegistry.RegisterBatch(PluginService.PluginComponentDefinitions)` 组件库 (ComponentLibraryWindow) 经 GetCategories/GetDefinitionsByCategory 自动展示插件组件
- 实例创建 > StyleWidgetFactory.Create 开头前置:

```csharp
var pluginWidget = PluginService.CreatePluginWidget(typeId, definition, config);
if (pluginWidget is not null)
{
    return pluginWidget;
}
```

- CreatePluginWidget: 查 PluginComponentFactories > 调工厂得 content > 包 `new DraggableContainer { Content = content, Width = definition.DefaultWidth, Height = definition.DefaultHeight }` > 工厂异常返回 null 落入 PlaceholderWidget 流程
- 插件需要卡片底座时自行继承 WidgetCardBase (public 于宿主程序集)

### 8.2 课表数据提供者

来源约定 `Config.ProfileSource.Value == "plugin:<pluginId>"` 分发顺序 (TimetableScheduleProvider.GetTodaySchedule)

1. ProfileSource 命中 plugin: 前缀且注册表存在该提供者 > 调用返回 (异常记 Warning 返回空列表)
2. 非本地来源 > 现有 LinkageBridge/ClassWidgetsBridge 分发 (不变)
3. 本地档案读取 (不变)

UI 接线 (ScheduleWindow 档案页 `_sourceCombo`)

- 构建时在三个固定项后追加 `PluginService.ScheduleProviders` 的显示名 (顺序同注册表)
- SelectedIndex 映射扩展: 前三项不变 第 3+i 项 > `"plugin:" + pluginId`
- SelectionChanged 写 Config.ProfileSource 后走现有 SwitchSource (plugin 来源走默认分支 不启用联动面板 课程编辑器正常显示)
- 实例组件 (TimetablePreviewWidget/NowLesson/Timeline) 经 TimetableScheduleProvider 自动吃到插件数据

### 8.3 公告通知

- PluginContext.PushAnnouncement > `Announcements.Append(text)` (core/Notifications.cs) 复用现有公告存储与右下角弹窗链路

### 8.4 私有配置与日志

- 私有配置读写限定在插件自身目录 互不可见
- 日志统一走宿主 Log 前缀 `[plugin:<id>]` 便于过滤

## 9 启停语义 (data/config/plugins.json)

```json
{
  "enabled": {
    "com.example.sample": true
  }
}
```

| 操作 | 行为 | 生效时机 |
| ---- | ---- | -------- |
| 安装 | 解压入 data/plugins/<id> enabled 映射写 true | 重启后加载 |
| 启用/禁用 | 改 enabled 映射并保存 | 重启后生效 (设置页提示) |
| 卸载 | 删插件目录 | 立即 (已加载时删除失败 先禁用重启) |
| 覆盖安装 | 删旧目录再装入 | 重启后生效 |

- plugins.json 不走 ConfigItem (键动态) 由 PluginService 自行读写 JSON
- 缺省无记录的插件视为启用 (首次手动放入目录即可用)

## 10 市场与 GitHub 索引仓库

### 10.1 仓库模式 (分布式 PR 收录)

- 索引仓库 `HelloGaoo/Glimpseon-Plugins` 仅存 plugins.json 与收录说明 不存插件源码
- 插件源码与发布由插件作者自治 .gplugin 挂作者仓库 GitHub Release asset
- 收录流程: 插件仓库就绪 (manifest.json 规范 + Release asset) > ISSUE 模板「插件收录申请」提交元数据 或直接 PR 修改 plugins.json > 审核合并
- 审核要点: id 反向域名规范 / apiVersion=1 / asset 可下载 / sha256 一致 / 描述合规
- 索引仓库 CI: PR 时校验 plugins.json (id 唯一 字段齐全 downloadUrl 可达 sha256 格式正确) 拒绝坏数据入库

### 10.2 索引 plugins.json schema

```json
{
  "name": "Glimpseon 插件索引",
  "apiVersion": 1,
  "updatedAt": "2026-10-14T00:00:00Z",
  "plugins": [
    {
      "id": "com.example.sample",
      "name": "示例插件",
      "author": "HelloGaoo",
      "description": "一句话描述",
      "tags": ["工具"],
      "iconUrl": "https://cdn.jsdelivr.net/gh/HelloGaoo/PluginSample@main/icon.png",
      "homepage": "https://github.com/HelloGaoo/PluginSample",
      "latest": {
        "version": "1.0.0",
        "apiVersion": 1,
        "downloadUrl": "https://github.com/HelloGaoo/PluginSample/releases/download/v1.0.0/com.example.sample.gplugin",
        "sha256": "",
        "publishedAt": "2026-10-14T00:00:00Z"
      }
    }
  ]
}
```

- 客户端只读 plugins 数组 其余字段忽略 索引加字段向后兼容
- latest.apiVersion 与宿主 HostApiVersion 不符 > 卡片标「不兼容」安装按钮禁用 (不隐藏 让作者可见待升级)
- iconUrl 可选 走 jsDelivr (jsDelivr 加速仓库文件 不加速 Release asset 下载直链)

### 10.3 索引拉取与多源

- 源顺序: Config Plugins/MarketSource (默认 raw.githubusercontent.com/HelloGaoo/Glimpseon-Plugins/main/plugins.json) > 自动变换 jsDelivr (raw.githubusercontent.com/Owner/Repo/branch/path > cdn.jsdelivr.net/gh/Owner/Repo@branch/path) > data/cache/plugin-market.json 旧缓存 (AppUtils.SaveCache TTL 1天)
- 任一源成功即停 全部失败用旧缓存 缓存亦无 > 市场区显示网络错误与重试按钮
- 拉取仅发生在进入市场分区时 启动不发网络请求

### 10.4 安装与更新

- 安装: 下载 downloadUrl (复用 Downloader 链路 超时沿用默认 进度显示百分比) 到 data/cache/plugin-packages/<id>.gplugin > sha256 校验 (不符提示失败并删除文件) > InstallPackage (第 5 节流程) > InfoBar 提示重启加载
- 更新: 进入市场分区时对比本地 manifest.version 与 latest.version (语义化版本比较) 高于本地标「可更新」按钮显示「更新」
- 已安装同版本 > 按钮「已安装」禁用
- 卸载与本地列表共用第 5 节卸载流程 与市场无关

## 11 设置窗口插件页

- 页签: NavItems 数组末尾追加 `("plugins", "settings.plugins")` 位于「高级」之后 XAML 追加 NavPlugins (Tag="plugins") 图标在 code-behind 设 `NavPlugins.IconSource = FAIcons.IconSource(FASymbol.Puzzle)` 并 FontSize=20 (枚举存在性编译期验证)
- 页面 BuildPluginsPage 结构 (页内分区切换 已安装/市场两容器 IsVisible 互斥):

```
标题 (Tr settings.plugins)
顶部切换段 (分段按钮): 已安装 / 市场
已安装分区:
  说明小字 (重启生效语义)
  导入卡片 (FASettingsExpander + Download 图标 + 「导入插件」按钮 > 文件选择器 *.gplugin/*.zip > InstallPackage > InfoBar 成败)
  插件列表 (空列表显示占位文字):
    每插件一张自绘卡片:
      行1  名称 + 版本 (右侧 ToggleSwitch 启用)
      行2  作者 > 描述 (小字)
      行3  状态: 已加载 / 未启用(重启后加载) / 加载失败: <原因>
      行4  按钮: 打开目录 (Folder) 卸载 (Dismiss > Confirm 后 Uninstall)
市场分区:
  头行右侧刷新按钮 (Sync)
  卡片列表 (iconUrl 有则显图 无则 Puzzle 占位):
    行1  名称 + latest.version + 标记 (可更新/不兼容)
    行2  作者 > 描述 (小字)
    行3  按钮: 安装 / 更新 / 已安装(禁用) / 不兼容禁用 下载中显示百分比
  拉取失败 > 显示网络错误 + 重试
```

- 市场数据首次切入分区时拉取 切走不销毁 返回不重拉 (手动刷新除外)
- 启停开关变更 > SetEnabled 写配置 > InfoBar 提示重启生效
- 安装/卸载成功均提示重启 (AppUtils.RequestRestart 由用户手动触发 不自动重启)
- 图标分配 (设置窗口零重复): Puzzle 页签与市场占位 / Download 导入 / Folder 打开目录 / Dismiss 卸载 / Sync 市场刷新

## 12 locale 词条 (settings.plugins.*)

zh_CN 基准 (zh_TW/en_US 对应翻译)

```json
"plugins": {
  "title": "插件",
  "note": "插件安装在 data/plugins 启用 禁用 安装 卸载需重启应用生效",
  "import": "导入插件",
  "import_desc": "选择 .gplugin 插件包安装到 data/plugins",
  "import_success": "已安装 {name} 重启后加载",
  "import_failed": "安装失败: {error}",
  "empty": "尚未安装任何插件",
  "uninstall": "卸载",
  "uninstall_confirm": "卸载插件 {name}? 插件目录将一并删除",
  "uninstall_success": "已卸载 {name}",
  "uninstall_failed": "卸载失败: {error}",
  "open_folder": "打开目录",
  "enabled": "启用",
  "restart_hint": "重启后生效",
  "state_loaded": "已加载",
  "state_disabled": "未启用",
  "state_error": "加载失败",
  "api_mismatch": "插件 API 版本不受支持 宿主支持 1",
  "tab_installed": "已安装",
  "tab_market": "市场",
  "market_refresh": "刷新",
  "market_retry": "重试",
  "market_empty": "索引暂无插件",
  "market_network_error": "市场加载失败 请检查网络后重试",
  "market_install": "安装",
  "market_update": "更新",
  "market_installed": "已安装",
  "market_incompatible": "不兼容",
  "market_updatable": "可更新",
  "market_downloading": "下载中 {percent}%",
  "market_sha_mismatch": "校验失败 文件与索引 sha256 不一致"
}
```

## 13 文档同步

- docs/core-modules.md 增插件模块条目 (PluginService/PluginContext/加载流程)
- docs/architecture.md 增插件层与启动顺序
- docs/directory-structure.md 增 data/plugins 与 data/config/plugins.json
- 本文档 (plugin-system.md) 为插件体系单一事实源

## 14 验证计划

### 14.1 编译

- dotnet build app-1.0.0/GlimpseonMain.csproj 零警告零错误
- 杀存留 GlimpseonMain 进程后重启验证 (taskkill 需脱离沙箱)

### 14.2 示例插件 examples/PluginSample

- 独立 csproj: net10.0-windows10.0.19041.0 类库 ProjectReference 宿主工程 构建后产物 (dll + deps.json + manifest.json) 拷入 data/plugins/com.example.sample/
- 产出 .gplugin 包另测导入链路

### 14.3 手工验证清单

| 步骤 | 预期 |
| ---- | ---- |
| 启动宿主 | 日志出现插件扫描汇总 加载成功 1 失败 0 |
| 设置 > 插件 | 列表出现示例插件 状态已加载 |
| 关闭开关 > 重启 | 状态未启用 日志不计入加载 |
| 重新启用 > 重启 | 恢复已加载 |
| 课表窗口档案页 | 来源下拉出现「示例课表」选中后课表组件显示插件数据 |
| 组件库 | Tools 分类出现「示例组件」双击添加到主页可拖拽 |
| 右下角 | OnLoad 触发的公告弹出 |
| 导入损坏包 (无 manifest) | InfoBar 报错 宿主不崩 |
| 卸载已启用插件 | 提示失败先禁用重启 卸载未加载插件成功 |
| manifest id 与目录不一致 | 拒载 设置页显示加载失败原因 |
| 市场分区首次进入 | 索引拉取成功 列表渲染 (MarketSource 临时指向本地静态服务实测) |
| 市场安装 | 下载 sha256 校验通过 落 data/plugins 提示重启 |
| 断网进市场 | 自动落旧缓存 无缓存显示网络错误与重试 |
| latest.apiVersion 不匹配项 | 卡片标不兼容 安装按钮禁用 |
| 已安装同版本 | 按钮「已安装」禁用 本地版本更低则显示「更新」 |

## 15 与参考实现的差异

### 15.1 沿用部分

| 做法 | 来源 |
| ---- | ---- |
| zip 包 + 清单 + 拒载校验 | ClassIsland .cipx (清单换 JSON 省 YamlDotNet) |
| 每插件独立 ALC + 宿主程序集回落 Default ALC | ClassIsland PluginLoadContext |
| apiVersion 门禁 | ClassIsland |
| enabled 集合存配置 | Class-Widgets-2 (与设置页开关一一对应 弃 .disabled 文件) |
| 市场索引仓库 + Release 托管 | ClassIsland PluginRepoManifest + Class-Widgets-2 广场 (多源拉取与 sha256 校验为本方案增强) |

### 15.2 自有设计 (两个参考均无)

| 设计 | 参考项目做法 | 本方案 | 理由 |
| ---- | ---- | ---- | ---- |
| 组件注册 | ClassIsland DI 容器 CW2 API 门面 | 定义进 ComponentRegistry 真身工厂 StyleWidgetFactory 前置查询 | 全项目无 DI 注册表零改动接入现有组件库/拖拽/配置重建链路 |
| 课表提供者 | ClassIsland 独立 IScheduleProvider 选择 UI | 复用 ProfileSource 增 `plugin:<id>` 来源 分发链首位 | 三个内置来源已是字符串分发 零新概念 课表组件自动吃到数据 |
| SDK 形态 | ClassIsland 拆 Core 程序集 CW2 拆 api 模块 | 宿主程序集即 SDK ProjectReference 直引 | 未上线拆程序集是纯维护成本 插件生态出现再抽 Glimpseon.Sdk.dll |
| 组件宿主 | 要求继承自家 Widget 基类 | 工厂返回任意 Control 宿主包 DraggableContainer | 插件最小成本出活 需卡片底座再继承 WidgetCardBase |

### 15.3 已知弱点与演进路径

- 宿主程序集即 SDK > 宿主内部改动可破坏插件编译 apiVersion 只挡运行时崩 挡不住 ABI 漂移 > 演进: 抽独立 SDK 程序集
- 运行期不卸载 启停靠重启 > collectible ALC 热卸载在 Avalonia 事件/定时器持有引用时易泄漏 ClassIsland 亦只做到重启生效 > 首版不碰
- 市场为纯索引分发 无签名链 依赖索引仓库审核与 sha256 完整性 > 演进: 收录包构建签名校验
- 首版不做: 插件设置 UI 声明 (仅 JsonObject 读写) 权限模型 市场评分评论
