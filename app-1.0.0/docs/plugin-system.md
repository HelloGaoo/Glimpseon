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
  > 进入插件页且处于市场分区时才拉索引 (页面构建期不联网)
  > 拉索引 plugins.json (配置源 > 加速/镜像 > 会话缓存 > 磁盘旧缓存并标记 stale)
  > 浏览 > 下载 .gplugin > sha256 校验 > InstallPackage > 重启加载
  > 启动期另有自动更新 (Config.PluginsAutoUpdate 开启时, 只下载不安装)
```

- 任一插件加载失败只收集 Error 不中断启动 不影响其他插件
- 启停/安装/卸载均在重启后生效 (沿用 AppUtils.RequestRestart)
- 退出时 (lifetime.ShutdownRequested) 统一派发 AppClosing 并调用 OnUnload

## 3 目录与文件

### 3.1 运行期目录

```
data/
  plugins/                      插件安装根目录 (Paths.DataPlugins 新增)
    <pluginId>/                 单插件目录 目录名必须等于 manifest.id
      manifest.json             清单
      <entrance>.dll            入口程序集 (必须在插件目录内, 绝对路径/../ 一律拒载)
      <entrance>.deps.json      强烈建议随包提供: 解析器只按 deps.json 找依赖, 缺失时插件自带的
                                其它 dll 不会被解析 (表现为"未找到 GlimpseonPlugin 派生类")
      其余依赖 dll               可选 随包分发
      config.json               插件私有配置 (PluginContext 读写 覆盖安装时自动保留)
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
| id | string | 是 | 反向域名式全小写 如 com.example.sample 等于目录名 且需通过 id 白名单 |
| name | string | 是 | 显示名 |
| version | string | 是 | 语义化版本 (比较时兼容 v 前缀/4 段/预发布) |
| author | string | 否 | 作者 |
| description | string | 否 | 一句话描述 |
| homepage | string | 否 | 项目主页 (仅 http/https 会在详情页给出入口) |
| entrance | string | 是 | 入口 dll 相对路径 必须落在插件目录内 |
| apiVersion | int | 是 | 目标宿主 API 版本 当前仅 1 |
| icon | string | 否 | 图标相对路径或 http(s) 地址 |
| readme | string | 否 | 说明文档相对路径或 http(s) 地址 |

校验规则 (全部不满足即拒载并记 Error)

- id 非空 通过白名单 (无 `\ / : * ? " < > |` 无首尾空白 不以 . 结尾 非 CON/NUL 等设备名) 且等于所在目录名
- entrance 解析后必须仍在插件目录内 (绝对路径或 `../` 越界一律拒)
- entrance 指向的文件存在
- apiVersion 为 1 (未登记 0 视为兼容; 不等且非 0 拒载)

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

1. ZipFile.OpenRead 校验包内条目数/单文件大小/总解压体积/压缩比 (防解压炸弹), 缺 manifest.json 直接拒
2. 解压到 data/cache/plugin-packages/stage_<随机>/
3. 解析 manifest 走第 4 节校验 (id 白名单/entrance 目录内/apiVersion)
4. 目标 data/plugins/<id>/ 已存在: 先把旧目录改名 .bak_<随机>, 再把新目录搬入 (失败回滚)
5. 旧目录里的 config.json 若新包没带, 自动拷进新包 (覆盖升级不丢配置)
6. 删除 .bak 目录 (删不掉则留到下次启动 CleanupLeftovers 清理)
7. plugins.json 缺该 id 时补 enabled=true (已有值保留) 标记 PendingRestartIds

卸载流程 (PluginService.Uninstall)

- 直接删除 data/plugins/<id>/
- 该插件已加载时 dll 被锁定删除失败 > 写 .uninstall 标记 + 立刻注销其组件/课表提供者/事件订阅, 重启后启动期自动删目录
- 两种路径都会从内存列表与 plugins.json 里移除

启动期残留清理: cache/plugin-packages/stage_*, data/plugins/*.bak_*, data/plugins/*.gplugin.failed

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

- OnUnload 在应用退出时调用 (lifetime.ShutdownRequested → PluginService.ShutdownPlugins, 先派发 AppClosing 再逐个 OnUnload)
- OnLoad 抛异常时宿主会回滚该插件已注册的组件/课表/订阅并调用一次 OnUnload, 状态记 Error

### 6.2 PluginContext API

| 成员 | 签名 | 说明 |
| ---- | ---- | ---- |
| Id | string | 清单 id |
| PluginDirectory | string | 插件目录绝对路径 |
| ApiVersion | int | 清单 apiVersion |
| LogInfo / LogWarning / LogError | void(string) | 写宿主日志 行首缀 `[plugin:<id>]` |
| ReadConfig | JsonObject() | 读插件目录 config.json 缺失返回空对象 |
| WriteConfig | void(JsonObject) | 写插件目录 config.json 缩进格式 (目录不存在会自动建) |
| RegisterComponent | void(ComponentDefinition, Func&lt;JsonElement?, Control&gt;) | 注册主界面组件定义与真身工厂 |
| RegisterScheduleProvider | void(string name, Func&lt;List&lt;ScheduleRow&gt;&gt;) | 注册课表数据提供者 (可注册多个) |
| PushAnnouncement | void(string text) | 经 Announcements.Append 追加公告 (线程安全) |
| ShowNotification | void(string content, int durationSeconds = 5, string style = "corner", bool speak = false) | 弹通知; 默认不朗读; 500ms 内多条会被丢弃; 只有用户开启"同步到公告板"才落盘 |
| Subscribe* / Get* | — | 事件订阅与状态拉取 (见下) |

约束

- RegisterComponent 的定义 Id **必须**含下划线且两段非空 (`<type>_<style>`) —— 组件存档按首个下划线拆分, 不含下划线的 id 重启后会找不到定义, 注册时直接拒
- 定义 Id 建议携带插件特征前缀避免与内置冲突 后注册覆盖内置 (RegisterBatch 语义)
- 工厂在 UI 线程调用 返回任意 Avalonia Control 宿主包入 DraggableContainer 并落定义默认宽高
- RegisterScheduleProvider 以 `pluginId:name` 为键 同名覆盖; 一个插件可以注册多个不同 name 的提供者

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
            // 参数: 科目/教师/开始/结束/节次/IsCurrent/IsBreak/课间名
            // IsCurrent 必须由提供者自己算, 传 false 会让 GetCurrentCourse() 永远返回 null
            return [new ScheduleRow("自习", "", now.ToString("HH:mm"), now.AddMinutes(40).ToString("HH:mm"), 1, true, false, "")];
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

- `Plugins` 只读列表 (按目录名排序) 驱动设置页渲染
- 注册表 (对外只读): `PluginComponentDefinitions` / `ComponentFactories` (组件 id > 工厂) / `ScheduleProviders` (`pluginId:name` > (pluginId, name, provider))
- `PendingRestartIds`: 本会话内装过/卸过的插件, 设置页据此显示"重启后生效"

### 7.2 加载算法 InitializePlugins

1. EnsureDataPlugins 目录 + 清理 stage_*/bak_*/failed
2. 处理 data/plugins 下待安装的 .gplugin (成功则删包, 失败改名 .failed)
3. 处理 .uninstall 标记目录 (删掉)
4. 读 data/config/plugins.json 的 enabled 映射 (文件损坏则备份为 .corrupt_<时间> 并按缺省启用)
5. 枚举 data/plugins/ 一级子目录 按名称排序
6. 每目录: manifest.json 缺失或解析失败 > 记 Error 继续
7. manifest 校验 (第 4 节规则) 失败 > 记 Error 继续
8. enabled=false > 记录状态跳过加载
9. 解析依赖得到加载顺序 (循环依赖记 Error)
10. new PluginLoadContext(id, 依赖, 入口dll) > LoadFromAssemblyPath; **构造/加载/实例化/OnLoad 任一步抛异常都只影响该插件**
11. 扫描程序集类型 (ReflectionTypeLoadException 时取 ex.Types 非空子集) 找 GlimpseonPlugin 派生类且带 public 无参构造
12. Activator.CreateInstance > 注入 Context > try/catch 调 OnLoad > 异常则回滚注册物并记 Error
13. OnLoad 内注册物进入注册表 (同一 id 重复注册覆盖 按加载顺序后者胜)
14. 汇总日志: 扫描 N 加载成功 M 失败 K 未启用 J

### 7.3 PluginLoadContext (ALC 隔离)

```csharp
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlyList<PluginDependency> _dependencies;

    public PluginLoadContext(string pluginId, IReadOnlyList<PluginDependency> dependencies, string entranceDll)
        : base($"plugin:{pluginId}")
    {
        _dependencies = dependencies;
        _resolver = new AssemblyDependencyResolver(entranceDll);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // 宿主/框架/UI 栈 (GlimpseonMain, System.*, Avalonia*, FluentAvalonia,
        // SkiaSharp, Markdown.Avalonia ...) 返回 null 回落 Default ALC 保证类型同一
        if (IsHostAssembly(name.Name))
        {
            return null;
        }
        // 先向依赖插件的上下文借 (共享同一份实例)
        foreach (var dep in _dependencies)
        {
            if (PluginService.LoadContexts.TryGetValue(dep.Id, out var context)
                && context.Load(name) is { } shared)
            {
                return shared;
            }
        }
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
```

- 主机名按前缀黑名单判定 (Avalonia / System. / FluentAvalonia / SkiaSharp ...) 防插件包内私搭同名 dll 造成类型分裂
- 其余依赖经 deps.json 解析; **deps.json 缺失或损坏时插件私有依赖解析不到** (损坏还会让解析器抛异常, 已被 try 包住只影响本插件)

### 7.4 失败策略

- 全链路 try/catch 每个插件独立失败互不传染 (含 ALC 构造与依赖解析)
- 失败信息进 LoadedPlugin.Error (设置页可见) 同时写宿主日志 Warning/Error
- ProfileSource 指向已卸载/禁用/加载失败的插件提供者 > 记警告并回落本地档案 (不会返回空课表)

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

- CreatePluginWidget: 查 `ComponentFactories` > 调工厂得 content > 包 `new DraggableContainer { Content = content, Width = definition.DefaultWidth, Height = definition.DefaultHeight }` > 工厂异常返回 null 落入 PlaceholderWidget 流程
- 插件需要卡片底座时自行继承 WidgetCardBase (public 于宿主程序集)

### 8.2 课表数据提供者

来源约定 `Config.ProfileSource.Value == "plugin:<providerKey>"` 其中 providerKey = `pluginId:name`, 分发顺序 (TimetableScheduleProvider.GetTodaySchedule)

1. ProfileSource 命中 plugin: 前缀且注册表存在该 providerKey > 调用返回 (异常记 Warning 返回空列表)
2. profile 里只有裸 pluginId (旧配置) > 取该插件的第一个提供者, 兼容旧值
3. 找不到提供者 (插件被卸载/禁用/改名) > 记 Warning 并回落本地档案
4. 非本地来源 > 现有 LinkageBridge/ClassWidgetsBridge 分发 (不变)
5. 本地档案读取 (不变)

UI 接线 (ScheduleWindow 档案页 `_sourceCombo`)

- 构建时在三个固定项后追加 `PluginService.ScheduleProviders` 的显示名 (重名时补 pluginId)
- SelectedIndex 映射扩展: 前三项不变 第 3+i 项 > `"plugin:" + providerKey`
- SelectionChanged 写 Config.ProfileSource 后走现有 SwitchSource (plugin 来源走默认分支 不启用联动面板 课程编辑器正常显示)
- 实例组件 (TimetablePreviewWidget/NowLesson/Timeline) 经 TimetableScheduleProvider 自动吃到插件数据

### 8.3 公告通知

- PluginContext.PushAnnouncement > `Announcements.Append(text)` (core/Notifications.cs) 线程安全 (内部加锁 + Changed 事件切回 UI 线程)
- PluginContext.ShowNotification > 独立的 NotificationManager 实例, 默认**不朗读** (speak 显式开启), 500ms 内多条丢弃, 同一内容 3s 内去重; 只有 Config.NotificationSyncBoard 打开时才写入公告板

### 8.4 私有配置与日志

- 私有配置读写限定在插件自身目录 互不可见 (覆盖安装自动保留 config.json)
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
| 卸载 | 删插件目录 (失败则写 .uninstall 并立刻注销运行期能力) | 立即从列表消失, 目录下次启动删 |
| 覆盖安装 | 旧目录改名备份 > 新目录落位 > 删备份 (保留 config.json) | 重启后生效 |

- plugins.json 不走 ConfigItem (键动态) 由 PluginService 自行读写 JSON
- 缺省无记录的插件视为启用 (首次手动放入目录即可用)
- 文件损坏时先备份成 `plugins.json.corrupt_<时间>` 再按"缺省启用"继续, 不会因一次写坏永久锁死

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

- 源顺序: Config Plugins/MarketSource (默认 raw.githubusercontent.com/HelloGaoo/Glimpseon-Plugins/main/plugins.json) > 按下载源改写的加速地址 > jsDelivr 兜底 (仅全部失败后) > 会话缓存 (5 分钟) > data/cache/plugin-market.json 旧缓存 (AppUtils.SaveCache TTL 1天)
- 任一源成功即停; 回退旧缓存时返回 `stale=true` (UI 不清除重试状态, 下次进入页面仍会联网)
- 缓存亦无 > 市场区显示网络错误与重试按钮
- 拉取时机: 进入插件页且处于市场分区时 (页面构建期不联网); 唯一例外是开启"自动更新"时启动期会拉一次索引

### 10.4 安装与更新

- 安装: 下载地址候选 = 索引 downloadUrl > `releases/download/v<version>/<asset>` > `releases/download/<version>/<asset>` > `releases/latest/download/<asset>` (每个再过加速源, 单地址失败重试一次) 落到 data/cache/plugin-packages/<id>.gplugin > sha256 校验 (不符报"校验失败"并删包) > InstallPackage (第 5 节流程) > 提示重启
- 进度: 有 Content-Length 时显示百分比, chunked 响应显示"下载中…"
- 版本比较用 semver (剥 v 前缀 / 4 段补齐 / 预发布视为更低), 高于本地标「可更新」
- 自动更新: 只在 Config.PluginsAutoUpdate 开启时执行, 只下载到 data/plugins/<id>.gplugin 并标记待重启, 不覆盖正在使用的目录
- 已安装同版本 > 按钮「已安装」禁用 (列表里本地条目显示卸载/启停而不是安装)

## 11 设置窗口插件页

- 页签: NavItems 数组末尾追加 `("plugins", "settings.plugins")` 位于「高级」之后 XAML 追加 NavPlugins (Tag="plugins"); code-behind 设 `NavPlugins.IconSource = FAIcons.IconSource(FASymbol.Shop)` 并 FontSize=20 (FASymbol 无 Puzzle, 与市场条目共用 Shop)
- 页面 BuildPluginsPage 结构 (页内分区切换 已安装/市场两容器 IsVisible 互斥):

```
标题 (Tr settings.plugins)
搜索框 + 工具行 (市场/已安装 TabStrip + 刷新 + "...")
左栏列表 / 右栏详情 (两栏各自滚动):
  条目 = 图标48 + 名称/描述 + 状态徽标(右上) + 操作(右下: 更新/卸载/启停 或 安装)
  详情 = 图标60 + 名称 + 元信息(版本|主页|作者) + 下载进度 + README
  市场未就绪 = 骨架条; 索引失败 = 网络错误 + 重试
```

- 市场数据首次切入分区时拉取 切走不销毁 返回不重拉 (手动刷新除外; stale 缓存会再拉)
- 回源 (manifest/README/图标) 在页面可见后并发进行, 单插件最多 3 次 (1.5s/3s 退避), 404 不再重试; 结束后未拿到的条目不再显示骨架
- 启停开关变更 > SetEnabled 写配置 > 提示重启生效 (UI 刷新排到事件回调之后, 不在回调里重建自身)
- 安装/卸载成功均提示重启 (AppUtils.RequestRestart 由用户手动触发 不自动重启)
- 同一时刻只允许一个下载; 第二个请求给"正在下载其他插件"提示
- 图标分配: Shop 页签与市场占位 / Download 导入与安装 / Folder 打开目录 / Dismiss 卸载 / Sync 刷新与更新

## 12 locale 词条 (settings.plugins.*)

zh_CN 基准 (zh_TW/en_US 键集完全一致, 新增/删除词条三语同步)

```json
"plugins": {
  "title": "插件",
  "import_success": "已安装「{name}」，重启应用后生效",
  "import_failed": "安装失败: {error}",
  "empty": "尚未安装任何插件",
  "uninstall": "卸载",
  "uninstall_confirm": "确定要卸载「{name}」吗？卸载后需要重启应用才能完全移除",
  "uninstall_success": "已卸载「{name}」",
  "uninstall_pending": "已卸载「{name}」，重启应用后完成移除",
  "uninstall_failed": "卸载失败: {error}",
  "open_folder": "打开目录",
  "enabled": "已启用",
  "restart_hint": "重启后生效",
  "state_error": "加载失败",
  "tab_installed": "已安装",
  "tab_market": "市场",
  "market_refresh": "刷新",
  "market_retry": "重试",
  "market_empty": "索引暂无插件",
  "market_network_error": "加载失败，请检查网络后重试",
  "market_install": "安装",
  "market_update": "更新",
  "market_incompatible": "不兼容",
  "market_updatable": "可更新",
  "market_downloading": "下载中 {percent}%",
  "market_downloading_unknown": "下载中…",
  "market_sha_mismatch": "安装包校验失败，请稍后重试",
  "busy": "正在下载其他插件，请稍候",
  "auto_update": "自动更新",
  "no_readme": "暂无说明",
  "search": "搜索插件…",
  "detail_empty": "在左侧选择一个插件查看详情",
  "filter_empty": "找不到符合条件的插件",
  "more": "更多选项…",
  "homepage_link": "项目主页",
  "install_from_local": "从本地安装…",
  "open_plugins_folder": "打开插件文件夹…"
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

- 独立 csproj: net10.0-windows10.0.19041.0 类库 Reference 宿主 GlimpseonMain.dll
- 打包必须带 **dll + deps.json + manifest.json** (缺 deps.json 时插件私有依赖解析不到; 当前仓库里的 pkg/*.gplugin 只含 dll, 换包时补上)
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
| 右下角 | PushAnnouncement 的公告弹出 (ShowNotification 默认不写公告板) |
| 导入损坏包 (无 manifest) | InfoBar 报错 宿主不崩 |
| 卸载已启用插件 | 立即从列表消失 重启后目录被删 |
| manifest id 与目录不一致 / id 非法 | 拒载 设置页显示加载失败原因 |
| 坏 deps.json (手工改坏) | 只该插件记 Error, 宿主正常启动 |
| 市场分区首次进入 | 索引拉取成功 列表渲染; 停留在其它页时不发请求 |
| 市场安装 | 下载 sha256 校验通过 落 data/plugins 提示重启 |
| 断网进市场 | 落旧缓存并标记 stale (下次进入仍会重试); 无缓存显示网络错误与重试 |
| latest.apiVersion 缺失 | 视为兼容 (不再一律判不兼容/跳过自动更新) |
| 已安装同版本 | 本地条目显示卸载/启停; 市场条目显示已落位待重启或安装 |
| 重复点击两个插件的安装 | 第二个提示"正在下载其他插件" |

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
| 课表提供者 | ClassIsland 独立 IScheduleProvider 选择 UI | 复用 ProfileSource 增 `plugin:<pluginId>:<name>` 来源 分发链首位 | 三个内置来源已是字符串分发 零新概念 课表组件自动吃到数据 |
| SDK 形态 | ClassIsland 拆 Core 程序集 CW2 拆 api 模块 | 宿主程序集即 SDK ProjectReference 直引 | 未上线拆程序集是纯维护成本 插件生态出现再抽 Glimpseon.Sdk.dll |
| 组件宿主 | 要求继承自家 Widget 基类 | 工厂返回任意 Control 宿主包 DraggableContainer | 插件最小成本出活 需卡片底座再继承 WidgetCardBase |

### 15.3 已知弱点与演进路径

- 宿主程序集即 SDK > 宿主内部改动可破坏插件编译 apiVersion 只挡运行时崩 挡不住 ABI 漂移 > 演进: 抽独立 SDK 程序集
- 运行期不卸载 启停靠重启 > collectible ALC 热卸载在 Avalonia 事件/定时器持有引用时易泄漏 ClassIsland 亦只做到重启生效 > 卸载时已注销组件/课表/订阅, ALC 本身留到进程退出
- 市场为纯索引分发 无签名链 依赖索引仓库审核与 sha256 完整性 (索引 id 已过白名单, 但索引本身仍是信任源) > 演进: 收录包构建签名校验
- 自动更新只下载不安装 (需要重启), 且不校验版本一致性之外的东西
- 首版不做: 插件设置 UI 声明 (仅 JsonObject 读写) 权限模型 市场评分评论
