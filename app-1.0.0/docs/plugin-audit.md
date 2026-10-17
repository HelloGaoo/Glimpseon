# 插件系统代码审计

> 审计范围：`core/Plugins.cs`、`core/PluginEvents.cs`、`GlimpseonMain.cs`（插件初始化/事件节拍）、`ui/SettingsWindow.axaml.cs`（插件页/市场页）、`ui/HomeView.axaml.cs` + `ui/Components.axaml.cs`（组件与课表接线）、`ui/ScheduleWindow.axaml.cs`（来源下拉）、`examples/PluginSample/*`、`docs/plugin-system.md`、`locale/*.json`
>
> 方式：逐行读码 + 全量编译（`dotnet build --no-incremental`，net10.0）核对警告 + 写探针程序实测两处运行时行为
>
> 结论：架构选型没问题，但实现层有 **8 处 P0**（其中 2 处可直接把「一个坏插件」升级成「整个应用打不开」）、**20+ 处 P1 逻辑错误**、大量死代码与文档漂移。整体观感是「先按文档写完了骨架，细节分支没走完就收工」。

---

## 修复状态 (2026-10-17 已全部处理)

本文件保留原始审计记录（含 file:line 为修复前位置）。修复后的实际行为以 `docs/plugin-system.md` 为准。

**已修（P0 全部）**
- 单插件异常不再影响启动：`PluginLoadContext` 构造/程序集加载/类型扫描/实例化/OnLoad 全在 try 内，`InitializePlugins` 再加一层；**实测**把 `deps.json` 改坏后该插件记 Error、其余插件与宿主正常启动
- 索引进来的 `id` 走 `IsSafePluginId` 白名单（ParseIndex 直接剔除 + 下载/缓存/卸载路径双保险）
- `entrance` / `icon` / `readme` 全部做目录包含校验（绝对路径、`../` 一律拒）
- 解压加上限：条目 ≤4096、单文件 ≤128MB、总解压 ≤512MB、压缩比 ≤1000
- `homepage` 只接受 http/https，否则详情页不给入口
- 循环依赖检测判断顺序修正（并加 try/finally 维护 walking）
- `PluginApi.IsCompatible(api)`：0 视为兼容，自动更新与 UI 判据统一
- `ReadmeCache` / `ReadmeTasks` / 图标缓存换 `ConcurrentDictionary`

**已修（P1）**
- 课表提供者改为 `pluginId:name` 多提供者模型，ProfileSource 存 `plugin:<providerKey>`，旧裸 pluginId 兼容，来源失效回落本地档案
- 组件 id 注册时强制 `<type>_<style>`，加载时兼容无下划线旧存档
- `MetaPending` 只在回源未结束时为真（`EnrichmentFinished` 收口），不再永久骨架
- 版本比较改 semver（v 前缀/4 段/预发布）
- 下载地址候选：downloadUrl > `releases/download/v<ver>` > `releases/download/<ver>` > latest，且按索引版本取，避免与 sha256 错位
- 索引回退旧缓存时返回 `stale`，UI 不清除重试状态
- 回源改按插件独立重试（3 次，1.5s/3s 退避，404 停止），去掉 5 轮全量重建与全局互斥
- 市场索引/回源改为"页面可见且处于市场分区"才发起（启动期只有显式开启自动更新时拉一次）
- 元数据缓存 TTL 统一 6h，磁盘命中按 semver 版本落后判定，`Inflight` 改 `GetOrAdd`
- 插件通知：默认不朗读、500ms 限频、3s 同内容去重、仅 `Config.NotificationSyncBoard` 开启时写公告板
- 公告 `Append` 加锁 + `Changed` 切回 UI 线程
- `OnUnload`/`AppClosing` 改到 `lifetime.ShutdownRequested` 统一收尾
- 卸载/`OnLoad` 失败立刻注销组件、课表提供者、事件订阅
- `plugins.json` 损坏先备份再按缺省启用；`.gplugin.failed` 下次启动清理
- 下载链路支持 CancellationToken、单地址重试，chunked 响应显示"下载中…"
- `DownloadSources.Candidates` 死分支删除；`LastStatusFor` 按候选地址查状态码（加速源下 404 检测恢复）
- 秒级节拍：只在有订阅时派发 TimeTick/计算课表，课间倒计时按 5s 粒度
- 启停开关不再在自身回调里重建控件；第二个安装请求给明确提示
- 安装结果改 `PluginResult` + `PluginOperationCode`（去掉 `"sha"`/`"pending"` 魔法字符串）
- 本会话装过/卸过的插件统一走 `PendingRestartIds`，本地插件覆盖安装也会提示重启
- 示例插件 `IsCurrent=true`；示例包补齐 `deps.json`（sha256 `ab9a6916…`）
- `en_US` 补齐 8 个缺失词条

**已修（P2/P3）**
- 删除 `ParseIndex.I()`、`_metaAttempted`、`BuildPluginItemActions(marketTab)`、18 个无用词条
- `PluginInfo.IsLocal/IsMarket` 改为派生属性；`PluginCatalog.Reload` 拆成 `BuildLocal`/`MergeMarket`
- 注册表对外只读；`PluginManifest.Load` 改元组返回并校验 name/id 白名单/entrance 目录内
- `PluginContext.Directory` → `PluginDirectory`；`ApplyMarkdownLook` 异常返回 false；Markdown 定时器 7 个合成 1 个
- 列表条目徽标与操作分列（Auto,*,Auto,Auto），圆角小按钮抽 `MakeRoundIconButton`
- 注释/文档笔误与实现漂移全部同步（plugin-system.md 第 2/3/4/5/6/7/8/9/10/11/12/14/15 节）

**未做**
- `PluginInfo` 未彻底拆成 local/market 两个子对象（只做了派生属性收敛），改动面太大留待后续
- 下载中仍没有"取消"按钮（`DownloadAsync` 已支持 CancellationToken，UI 未接）
- 通知窗口的 `FontSize/FontWeight` 字段遮蔽告警、`Window.SystemDecorations` 等非插件问题未动
- 示例包已重打，但 GitHub Release 资产与索引 sha256 需要发布新 tag 后更新

---

## P0 会崩 / 越权 / 安全

### P0-1　一个坏 `deps.json` 能让整个应用启动失败
`core/Plugins.cs:755`

```csharp
var alc = new PluginLoadContext(manifest.Id, manifest.Dependencies, dllPath); // ← try 之外
Assembly asm;
try { asm = alc.LoadFromAssemblyPath(dllPath); } catch ...
```

`PluginLoadContext` 构造函数里 `new AssemblyDependencyResolver(entranceDll)` 会解析 `<入口>.deps.json`。实测（探针程序）：

```
THROW InvalidOperationException: Dependency resolution failed for component ... 
DETAILED: Failed to parse file [...PluginSample.deps.json]. JSON parsing exception
```

而 `InitializePlugins:554` 的 `foreach (var id in loadOrder) LoadPlugin(...)` 外层**没有 try**，异常直接冒到 `RunStartupAsync` 的总 catch → 弹「Glimpseon 启动失败」并 `Shutdown(1)`。

也就是说：`data/plugins/<id>/` 里随便一个被截断/损坏的 `.deps.json`，用户就再也打不开软件，还得手动去删目录。这与 `docs/plugin-system.md` 7.4「全链路 try/catch 每个插件独立失败互不传染」直接矛盾。

修：`alc` 的构造移进 try；`InitializePlugins` 的 foreach 再包一层 try，单插件失败记 Error 继续。

### P0-2　市场索引里的 `id` 完全不校验就拼路径（任意文件写）
`core/Plugins.cs:1467`、`:1711`、`:1772`

```csharp
var dest = Path.Combine(Paths.DataCache, "plugin-packages", plugin.Id + ".gplugin"); // plugin.Id 来自远端索引
...
private static string DiskPath(MarketPlugin mp) => Path.Combine(MetaDir, mp.Id + ".json");
File.WriteAllText(path, JsonSerializer.Serialize(entry, ...));
```

`IsSafePluginId()` 只在 `InstallPackage` 里对 **包内 manifest** 用过，索引来源的 `MarketPlugin.Id` 一路裸奔。索引写 `"id": "../../config/plugins"` 即可：
- `File.Create(dest)` 写出 `data/cache/..` 之外的任意 `*.gplugin`；
- `SaveDisk` 覆盖 `data/config/plugins.json`（下次启动全部插件被"启用")；
- `PluginCatalog:335` 的 `Directory.Exists(Path.Combine(DataPlugins, mp.Id))` 也会被带偏。

索引是远程 JSON、`MarketSource` 还允许用户自定义，属于"半可信输入"，必须按 `IsSafePluginId` 收口（且建议同时拒绝 `.`/`..`/保留设备名/结尾点空格）。

### P0-3　`manifest.entrance` 不受插件目录约束
`core/Plugins.cs:91`、`:754`

```csharp
if (string.IsNullOrWhiteSpace(m.Entrance) || !File.Exists(Path.Combine(dir, m.Entrance)))
```

`entrance` 写 `"../../../../Windows/System32/xxx.dll"` 或绝对路径同样能通过校验，随后 `Path.GetFullPath(Path.Combine(lp.Directory, manifest.Entrance))` 直接 `LoadFromAssemblyPath`。同理 `icon` / `readme`（`ResolveLocalReadme:441`）可以把宿主配置、日志当 Markdown 渲染进设置页。

修：对 `entrance`/`icon`/`readme` 统一做 `GetFullPath` + `StartsWith(dir + sep)` 的包含性校验（`InstallPackage` 解压那段其实已经写对了，只是没复用）。

### P0-4　解压无任何体积/条目上限（zip bomb）
`core/Plugins.cs:953-967`

`entries` 全量枚举 + `entry.ExtractToFile(target, true)`，没有单文件上限、总解压上限、条目数上限、压缩比检查。一个几百 KB 的 `.gplugin` 可以解出几十 GB 填满磁盘（解压发生在 `data/cache`，与用户数据同盘）。建议加：条目数 ≤ N、单文件 ≤ 64MB、累计 ≤ 256MB、`CompressedLength>0 && Length/CompressedLength > 1000` 直接拒。

### P0-5　索引给的 `homepage` 直接 `Process.Start(UseShellExecute)`
`ui/SettingsWindow.axaml.cs:2200-2211`

```csharp
Process.Start(new ProcessStartInfo { FileName = homepage, UseShellExecute = true });
```

`homepage` 来自索引或插件 manifest，未做 scheme 白名单。索引写 `C:\Windows\System32\calc.exe`、`\\attacker\share\x.exe`、`ms-settings:...`、`file:///...` 都能让用户点一下「主页」就执行。至少要 `Uri.TryCreate` + `scheme is http/https`。同一处 `open_folder` 用的是本地目录，没问题。

### P0-6　循环依赖检测是死代码（永远不会触发）
`core/Plugins.cs:701-714`

```csharp
if (node.Discovered) return;          // ← 先进这里
if (walking.Contains(node)) { ...循环依赖... return; }   // ← 永远到不了
node.Discovered = true;
walking.Add(node);
```

被加进 `walking` 的节点必然 `Discovered == true`，所以第二个判断不可达。A→B→A 时不会报错，只会在 `Discovered` 处静默返回，`Depth` 被算成 A=2、B=1，最终按 B→A 的顺序加载（B 的依赖 A 还没加载，`LoadContexts` 里查不到，退化成各自加载私有副本），错误信息 `循环依赖: ...` 一次也不会出现在日志里。

顺带：`walking.Remove(node)` 没有 `finally`，中途 return 会污染列表（一旦修好上面的判断，这条就会真的咬人）。

### P0-7　自动更新实际是关闭的（现网索引必中）
`core/Plugins.cs:1497-1501`

```csharp
if (mp.LatestApiVersion != PluginApi.HostApiVersion || CompareVersions(...) <= 0) continue;
```

`LatestApiVersion` 只在 `latest.apiVersion` 存在时才非 0（`ParseIndex:1372`）。仓库里真实的索引缓存 `data/cache/plugin-market.json` 的 `latest` 里**没有 `apiVersion` 字段** → 值为 0 → `0 != 1` → 每个插件都被 `continue`，`AutoUpdateAsync` 等于空转。

而同文件 `PluginCatalog:348` 对同一份数据用的是"0 表示未知、视为兼容"的语义，两处判断相反。修：抽出统一的 `IsApiCompatible(int api)`，只在 `marketApi != 0 && marketApi != Host` 时判不兼容。

### P0-8　README 缓存是跨线程裸 `Dictionary`
`ui/SettingsWindow.axaml.cs:3056 / 3122 / 2476`

```csharp
private static readonly Dictionary<string, string> ReadmeCache = new();
private static readonly Dictionary<string, Task<string?>> ReadmeTasks = new();
...
ReadmeCache[source] = doc;      // 在 Task.Run 里的 LoadMarkdownAsync 中写
if (ReadmeCache.TryGetValue(info.ReadmeSource, out var cached))  // UI 线程读
```

`BuildReadmeContent` 把加载丢进 `Task.Run`，多张卡片并发时 `Dictionary` 会被多线程同时读写 → 轻则丢缓存，重则内部桶损坏触发死循环/`IndexOutOfRange`（UI 线程卡死的经典症状）。旁边 `ReadmeMissing` 用了 `ConcurrentDictionary`，说明作者知道要并发安全，就是漏了这个。编译也报了 `SettingsWindow.axaml.cs(3127): CS8604 Remove(key) 可能为 null`。

修：一律 `ConcurrentDictionary`，或把写入 `Dispatcher.UIThread.Post` 回来。

---

## P1 功能逻辑错误

### P1-1　一个插件只能注册一个课表提供者，和文档/API 语义相反
`core/Plugins.cs:836-840`

```csharp
public static void RegisterScheduleProvider(string pluginId, string name, Func<...> provider)
    => ScheduleProviders[pluginId] = (name, provider);   // 键是 pluginId
```

API 收 `name`、文档 §6.2 写「同插件重复注册按 name 覆盖」，实现却是「按 pluginId 覆盖」：同一插件注册第二个提供者会**静默顶掉第一个**。而 `Config.ProfileSource` 又只存 `plugin:<pluginId>`（`Components.axaml.cs:2400`、`ScheduleWindow:1838`），根本没地方表达 name。要么改成 `plugin:<pluginId>:<name>`，要么把 API 收窄成"每插件一个"并在注册第二个时报错。

### P1-2　插件来源失效时不回落，课表直接空
`ui/Components.axaml.cs:2414-2417`

```csharp
Log.Warning($"[课表组件] 插件课表来源不存在: {pluginId}");
return result;      // 空列表
```

文档 §7.4 写的是「ProfileSource 指向已卸载或加载失败的插件提供者 → 分发链按序回落本地档案」。实际是 `return` 空表：用户卸载插件后，课表组件、预览、NowLesson 全变空，且 `ProfileSource` 永远卡在 `plugin:xxx`，用户不到课表设置里手动改回来就一直是空的。应回落到本地档案分支。

### P1-3　组件 Id 不含下划线时，重启后组件直接消失
`ui/HomeView.axaml.cs:901-908` + `ui/Components.axaml.cs:640-646`

`AddComponentById` 用**首个下划线**把 `defId` 拆成 `Type`/`Style` 存档；`ComponentManager.CreateWidget` 再用 `$"{Type}_{Style}"` 反查定义：

```csharp
var underscore = defId.IndexOf('_');
var compType = underscore > 0 ? defId[..underscore] : defId;
var compStyle = underscore > 0 ? defId[(underscore + 1)..] : "";   // 无下划线 → ""
...
var definition = ComponentRegistry.GetDefinition($"{data.Type}_{data.Style}");  // → "hello_"
```

插件若注册 `Id = "hello"`（完全合法，`RegisterComponent` 只校验非空），当下能加能显示，重启后 `GetDefinition("hello_")` 为 null → `组件定义未注册` → 组件静默丢失。

修：`RegisterComponent` 里强制校验 `<type>_<style>` 形态（或至少含下划线且两段非空），并在 `CreateWidget` 反查失败时回退 `$"{Type}_{Style}".TrimEnd('_')`。

### P1-4　`MetaPending` 复制粘贴重复赋值
`core/Plugins.cs:341-342`

两行完全一样。删一行，并把赋值移出 `if` 分支范围再确认语义（本地插件也被赋了 `MetaPending`，见 P1-5）。

### P1-5　本地插件 + 市场元数据 → "没有说明"变成永久骨架屏
`core/Plugins.cs:341` + `ui/SettingsWindow.axaml.cs:2465-2474`

`BuildReadmeContent`：`ReadmeSource` 为空且 `MetaPending == true` → `return panel`（只有骨架条，没有终态）。本地插件本来就不需要云端元数据，但只要它在索引里出现且 `Meta` 还没回源完，详情页就是一片永远脉冲的灰条。`MetaPending` 应该只对 `!IsLocal` 有意义。

### P1-6　版本比较用 `Version.TryParse`，`v` 前缀和预发布全部误判
`core/Plugins.cs:1215-1222`

```csharp
if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) return va.CompareTo(vb);
return string.CompareOrdinal(a, b);
```

三个坑：
1. 索引常用 `v1.0.0` → 解析失败 → 字符串比较 `"v1.0.0" > "1.0.0"` → 永远"可更新"（自动更新每次启动重下同一个包）。
2. `"1.0.0.0"` vs `"1.0.0"`：`Version.CompareTo` 把未指定的 build 当 -1，判 `1.0.0 < 1.0.0.0` → 假更新。
3. 预发布 `1.0.0-beta` 解析失败 → 与 `1.0.0` 做序数比较 → `-` 的 ASCII 小于数字，判成更旧/更新都可能错。

修：正规 semver 解析（剥 `v`、拆 `-pre`、按段数字比较）。

### P1-7　自动更新下的是 `releases/latest`，与索引里的 sha256/版本对不上
`core/Plugins.cs:1169-1172`

```csharp
: $"https://github.com/{Repo}/releases/latest/download/{Asset}";
```

索引 `latest.version` 说是 1.2.0、`latest.sha256` 是 1.2.0 的，可 URL 指向"最新 release"。当作者发布了 1.3.0 而索引还没更新时：下载到 1.3.0 → sha256 不匹配 → 直接删包 + 报 `sha` 失败；或者更糟（索引没填 sha）→ 装了个索引没登记的版本。应优先用索引里的显式 `downloadUrl`，回退构造时用 `releases/download/v{version}/{asset}`。

### P1-8　索引拉取"回退旧缓存"时返回 `error = null`，UI 当成功且不再重试
`core/Plugins.cs:1278-1282` vs `:1284`

```csharp
return (ParseIndex(cache, null), null);      // 磁盘旧缓存，假装成功
...
return (null, "network");                     // 真失败
```

`SettingsWindow:1815` 用 `_marketFetched = error is null` 决定"本会话是否已拉过"。回退旧缓存也置 true → 后续进入插件页不会再尝试刷新，用户永远看旧列表且看不到"网络错误，可重试"的提示。语义上应区分 `stale: true`。

### P1-9　元数据回源：5 轮 × 2s 重试 + 每轮全量重建列表 + 全局 `_enriching` 互斥
`ui/SettingsWindow.axaml.cs:1861-1884`、`:1829-1834`

- 每一轮都对**整份**列表 `await Task.Delay(2000)` 后并发重试，再 `RefreshCatalog + RenderMarketList + RebuildPluginList`，最多 5 轮。列表里 30 个插件就是 30×5 次 raw 请求 + 5 次整表重建，用户看到界面反复闪。
- `_enriching` 是全局布尔：用户在回源期间点「刷新」→ `EnrichMarketMetadataAsync` 直接 `return`（只打一行 Debug），新列表的 metadata **永远不会**被补全（旧任务还在补旧 list）。
- 没有 CancellationToken，关掉设置窗口/切页也照跑。

建议：单个插件独立超时 + 指数退避，回源完成后一次性重建；用 `Task` 引用而不是 bool 做互斥，新列表直接取代旧任务。

### P1-10　打开设置窗口就联网（与文档"进入市场分区才拉取"相反）
`ui/SettingsWindow.axaml.cs:341`（`BuildPages` 是 eager 的）+ `:1549-1552`

`BuildPluginsPage()` 里 `_ = FetchMarketIndexAsync()`；`BuildPluginsBody()` 里又 `_ = EnrichMarketMetadataAsync(...)`。`BuildPages` 在窗口构造时一次性全建，所以用户只是打开设置看一眼"通用"，后台已经在拉索引 + 逐个回源 + 下图标。文档 §10.3 明确写「拉取仅发生在进入市场分区时」。应改成 Tab 首次选中/页面首次可见时再拉。

### P1-11　`PluginMetaStore` 的 TTL 设计自相矛盾
`core/Plugins.cs:1555`、`:1725-1735`

- 内存 `Cache` 没有 TTL（进程活多久就缓存多久），磁盘 `MetaCacheTtl = 3 分钟`；
- 读磁盘还要求 `entry.Version == mp.LatestVersion`，而 `entry.Version` 来自插件仓库 manifest、`mp.LatestVersion` 来自索引，两者只要有一边写法不同（`v1.0.0` vs `1.0.0`）就永远不命中 → 每次进页面都重新回源。

建议：统一 TTL（比如 6h）+ 放宽版本匹配（只在索引版本明确更高时失效）。

### P1-12　`Inflight` 检查-写入非原子
`core/Plugins.cs:1617-1623`

`TryGetValue` 未命中后直接 `Inflight[id] = task`，两个并发调用都会去 `FetchAsync`。`Clear()`（1571）也没清 `Inflight`。用 `GetOrAdd` 即可。

### P1-13　插件通知完全绕过用户的通知/TTS 配置，还顺手污染公告板
`core/Plugins.cs:220`、`:223-244`

```csharp
private static readonly NotificationManager NotifManager = new();   // 独立实例
...
NotifManager.HandleNotification(new NotificationRequest { Type = type, Content = content, Duration = ... });
Announcements.Append(content);                                       // ← 每条通知永久写盘
```

三个问题：
1. `NotificationRequest` 只填 3 个字段，`BgColor/BgAlpha/TextColor/FontSize/FontWeight/TtsVoice/TtsRate/TtsVolume` 全走默认值。用户在通知页里设置的样式、以及把 TTS 设成 `done`（禁用）都会被无视——插件通知**永远**用黑底白字 + 晓晓音色朗读。
2. `HandleNotification` 无条件 `SpeakText`，即每次插件通知都会 `Task.Run` 起一个 `python -m edge_tts` 子进程。插件在 `SubscribeTimeTick` 里调一次 `ShowNotification` 就是**每秒 fork 一个进程**，没有任何限流/去重。
3. `Announcements.Append` 会把临时通知当成公告永久写入 `data/user/announcements.json`（并且每条都全量读写一次文件）。`PushAnnouncement` 已经承担了这个语义，`ShowNotification` 不该再写。

### P1-14　`Announcements.Append` 无锁 + 事件在任意线程触发
`core/Notifications.cs:111-130`

`Load()` 读文件 → `Insert(0)` → `Save()` 写文件，全流程无锁。`PluginContext.PushAnnouncement` 是同步 API，插件在后台线程调用时：
- 两个线程交错 → 丢条目 / JSON 半截；
- `NotifyChanged()` 在后台线程 `Invoke`，订阅方（主页公告组件）会直接跨线程改 UI → Avalonia 抛 `InvalidOperationException`。

至少加 `lock`，并把 `Changed` 用 `Dispatcher.UIThread.Post` 派发。

### P1-15　`OnUnload` 永远不会被调用；`ProcessExit` 里发事件
`core/Plugins.cs:564-573`

```csharp
AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { PluginEventBus.RaiseAppClosing(); } catch { } };
```

- 全项目没有任何地方调 `OnUnload()`，`GlimpseonPlugin.OnUnload` 是纯摆设，文档也承认"语义预留"，但 SDK 对外发布时这就是个坑（插件作者会以为能保存状态）。
- `RaiseAppClosing` 在 `ProcessExit` 上跑，此时 UI 线程可能已经停转、`Dispatcher` 已关闭；插件在里面 `ShowNotification`/`Context.WriteConfig` 都可能半死。
- 这个 handler 注册在 `InitializePlugins` 内部，方法被重复调用就会重复注册。

建议：在 `MainWindow.Closing`/`lifetime.Shutdown` 里显式 `OnUnload` + `RaiseAppClosing`；把事件从 `ProcessExit` 挪走。

### P1-16　卸载"成功"后插件仍在运行，注册表也不清
`core/Plugins.cs:1034-1078`

`Uninstall` 删除目录失败 → 写 `.uninstall` 标记 → `ForgetPlugin` 从 `Plugins` 移除 → UI 立刻看不到它。但本会话里 `Instance`、`ComponentFactories`、`PluginComponentDefinitions`、`ScheduleProviders`、`EventBus` 订阅全都还在：组件照常渲染、课表照常供数、定时器照常回调、通知照常弹。下次启动才发现"卸载了但组件还在配置里"。

同理 `InitializePlugins` 只 `Clear()` 列表，不 `OnUnload`、不 `alc.Unload()`、不清 `Subscriptions`。

### P1-17　`plugins.json` 解析失败 = 全部插件被启用
`core/Plugins.cs:888-891`

```csharp
catch (Exception e) { Log.Warning("plugins.json 读取失败 视为全启用"); }
return map;   // 空 map → PreprocessPlugin 里 TryGetValue 失败 → enabled = true
```

一次磁盘写坏/手工编辑错一个逗号，用户之前禁用的插件（可能正是导致崩溃的那个）全部复活。建议失败时保留 `.bak` 并默认"全禁用"更安全。

### P1-18　`.failed` 包永久堆积且无人告知
`core/Plugins.cs:598-606`

启动期安装失败 → `File.Move(pkg, pkg + ".failed")`，之后没有任何代码清理、也没有 UI 提示。用户看到市场里"已安装/重启后生效"但版本一直不变，日志里只有一行 Warning。

### P1-19　`DownloadAsync` 另起一套下载链路
`core/Plugins.cs:1386-1462`

绕过 `GitHubFetch.SendAsync`（限流 + 3 次重试 + 统一 UA/Accept + 状态码日志），直接 `GitHubFetch.Http.SendAsync`：
- 没有 `CancellationToken`（下载中无法取消，关窗口也照跑）；
- 没有重试（`SendAsync` 里那套 400/1200ms 退避享受不到）；
- `if (total > 0)` 才回调进度，chunked 响应时进度条永远 0%、只有转圈；
- 失败只留 `lastError`，多个候选源全失败时信息只剩最后一个。

### P1-20　`Candidates` 里的"直连保留"分支是死代码
`core/Downloader.cs:110-117`

```csharp
var resolved = Resolve(url);
Add(resolved);
if (string.Equals(resolved, url, StringComparison.Ordinal)) Add(url);   // Add 内部已去重 → 必然 no-op
```

注释说要"直连只在未配置加速源时保留"，实际什么都没做（`resolved == url` 时本来加的就是 `url`）。

### P1-21　配了加速源后，回源的 404 检测失效
`core/Downloader.cs:1199` vs `core/Plugins.cs:1657`

`LastStatus[url] = status` 记录的是**改写后**的候选 URL（`Candidates` 返回 `Resolve(url)`），而 `PluginMetaStore.FetchAsync` 用**原始** `manifestUrl` 去查：

```csharp
var status = GitHubFetch.LastStatus.TryGetValue(manifestUrl, out var st) ? st : 0;
if (status is 404 or 410) NotFound[mp.Id] = true; else "稍后重试"
```

只要用户配了 `DownloadSource != original`（默认就是 `edgeone`！），`status` 恒为 0 → 永远走"稍后重试"分支 → `NotFound` 永不置位 → 每个坏 repo 的插件每次刷新都要被重试 5 轮。

顺带：`LastStatus` 是永不清理的全局字典（键是按 URL），长时间运行会无限增长。

### P1-22　每秒一次的节拍器：算得比用得多
`GlimpseonMain.cs:250-325`

- `RaiseTimeTick` 无条件每秒派发（哪怕 `TimeTick` 没人订阅），紧接着又 `HasSubscribers("CourseChanged")` + `HasSubscribers("ClassStateChanged")` 各遍历一次（每次都 `lock` + `Any`），真正派发时 `Raise` 里再遍历一次。
- 只要有任一订阅者，就每秒 `TimetableScheduleProvider.GetTodaySchedule()`：读配置、可能调插件提供者、可能走联动桥/文件 IO，全在 UI 线程。
- `BreakRemainingSeconds` 只在 `stateSignature` 的 `breakRemaining/30` 变化时才更新，字段名却暗示"实时剩余秒数"——插件拿去显示倒计时会最多滞后 30s。

### P1-23　设置页插件的启停开关会重建"正在处理事件的自己"
`ui/SettingsWindow.axaml.cs:2040-2058`

```csharp
toggle.IsCheckedChanged += async (_, _) => {
    ...
    PluginService.SetEnabled(info.Id, enabled);
    ...
    RefreshCatalog();
    RebuildPluginList();      // ← 正在处理事件的 CheckBox 被 Clear 并从视觉树摘掉
    RenderMarketList();
};
```

在控件自身的事件回调里把自己从父容器清空，Avalonia 一般能扛，但焦点/键盘导航会丢，且 `RebuildPluginList` 里又会对新实例 `IsChecked = IsEnabledEffective(info)`，靠 `_enabledOverrides` 兜底。

同时 `_enabledOverrides`（1523）**永不清空**，卸载再重装同一 id 后，残留的 override 会盖掉磁盘配置。而 `info.Local.Enabled` 也已经被赋过值 —— 一份状态存三处（`plugins.json` / `Load.Enabled` / `_enabledOverrides`）。

### P1-24　第二个安装请求静默失败
`ui/SettingsWindow.axaml.cs:2571-2574`

```csharp
if (_pluginsPageHost is null || _downloadingPluginId is not null) return;
```

列表里点「更新」、再点另一个插件的「安装」→ 直接 return，没有 InfoBar、没有禁用按钮、日志也没有。用户以为没点到。

### P1-25　`"sha"` / `"pending"` 魔法字符串当返回值契约
`core/Plugins.cs:1052`、`:1453`、`ui/SettingsWindow.axaml.cs:2601`、`:3201`

`(bool, string)` 元组的 string 既当"人类可读消息"又当"错误码"用，调用方 `msg == "sha"` / `msg == "pending"` 硬比较。改一个字就静默走错分支（"sha" 分支丢了就会把校验失败显示成"安装失败: sha"）。应改成 `enum PluginInstallError { None, ShaMismatch, Pending, Io, ... }`。

### P1-26　覆盖安装后不提示重启（只有市场独有插件才提示）
`core/Plugins.cs:335`

`PendingRestart` 只在"索引有、本地无"的分支里按 `Directory.Exists` 推断。对一个**已安装**插件做更新/覆盖安装后，`PluginService.Plugins` 里还是旧的 manifest，界面继续显示旧版本 + "已加载"，不会出现"重启后生效"的标记。用户完全感知不到"新版本已经落盘了"。

### P1-27　示例插件自身就是错的：`IsCurrent` 恒 false
`examples/PluginSample/SamplePlugin.cs:48`、`docs/plugin-system.md:202`

```csharp
return [new ScheduleRow("自习", "", now.ToString("HH:mm"), now.AddMinutes(40).ToString("HH:mm"), 1, false, false, "")];
//                                                                                        ↑ IsCurrent = false
```

`ScheduleRow` 的签名是 `(Subject, Teacher, StartTime, EndTime, Index, IsCurrent, IsBreak, BreakName)`。示例把 `IsCurrent` 写死 `false`，于是 `Context.GetCurrentCourse()` 永远返回 null，示例组件那行"当前: xxx"永远显示"当前无课"。这是示例 + 文档双份错误，会直接教坏所有插件作者。应改成 `IsCurrent: true`（或提供 `ScheduleRow.Current(...)` 之类的构造帮助方法）。

### P1-28　`en_US.json` 缺 8 个插件词条 → 英文界面直接显示 key
`locale/en_US.json`

用到的 34 个 key 中，`en_US.json` 缺：

```
detail_empty / enabled / filter_empty / homepage_link / install_from_local / more / open_plugins_folder / search
```

`AppUtils.Tr` 未命中时**返回 key 本身**（AppUtils.cs:191-204），所以英文用户会看到 `settings.plugins.search` 这种文本。`zh_TW` 是齐的，只有 `en_US` 漏了。

---

## P2 死代码 / 重复 / 啰嗦 / 设计

1. **`ParseIndex` 里 `I()` 从未使用**（`Plugins.cs:1350`，编译器 `CS8321` 已报）。真正用的只有 `LI()`。
2. **`_metaAttempted` 字段从未使用**（`SettingsWindow:1521`，注释"已经尝试过回源的插件"）。
3. **18 个 locale 词条无人使用**：`tab_overview / tab_errors / tab_dependencies / dependencies_none / required_yes / required_no / detail / import / import_desc / note / auto_update_desc / auto_updated / meta_failed / open_homepage / state_loaded / state_disabled / market_loading / market_installed`。它们描述的是一个"概览/依赖/错误"三 Tab 的详情页，代码里只剩单页 —— 要么把功能补上，要么把词条删掉。
4. **`BuildPluginItemActions(info, marketTab)` 的 `marketTab` 参数完全没用**（`SettingsWindow:2016`）。传了个上下文却不使用，读代码的人会以为有分 Tab 逻辑。
5. **`PluginInfo` 是 20+ 个可变标志位的大杂烩**：`IsLocal / IsMarket / Local / Market / MetadataReady / MetaPending / IsIncompatible / IsUpdateAvailable / PendingRestart / Status / Error / Directory / LocalVersion / MarketVersion / IconSource / ReadmeSource ...`。多个状态互为派生（`IsLocal == Local is not null`、`IsMarket == Market is not null`），而且 `PluginCatalog.Reload` 里有 3 段几乎对称的"从 meta 回填 name/author/desc/icon/readme"的 if。建议拆成 `LocalPluginState` + `MarketPluginState` 两个小对象，`PluginInfo` 只做聚合与显示字段。
6. **`PluginCatalog.Reload` 一个方法 95 行干 4 件事**（合并两边 / 回填字段 / 推断版本兼容 / 排序），中间夹着 `MetaPending` 重复赋值。拆成 `BuildLocal()`、`BuildMarket()`、`Merge()`。
7. **`PluginService` 的注册表全是 public 可变静态字段**：`Plugins`、`PluginComponentDefinitions`、`ScheduleProviders`。外部（UI/组件）能直接 `Clear()`/`Add()`，插件系统失去了唯一入口的可控性。至少暴露 `IReadOnlyList`。
8. **`PluginManifest.Load(dir, out error, bool)` 的 out-param 风格**：项目里已经有 `(bool Ok, string Message)` 元组风格，两种混用。且 `checkDirName` 只在安装时传 false，不如拆成 `Load(dir)` / `LoadForInstall(dir)`。
9. **`ApplyMarkdownLook` catch 里 `return true`**（`SettingsWindow:2445-2449`）：返回值的语义是"已应用样式"，异常路径返回 true 只是为了让调用方把 `Opacity` 设成 1。语义反了，应显式区分"是否可见"。
10. **每个 Markdown 查看器创建 6+1 个 `DispatcherTimer`**（`ScheduleMarkdownLook:2280-2306`），只为反复试探"是否已生成视觉树"。整段 30 行可以换成一次 `LayoutUpdated` + 单次重试。
11. **图标 chip / 圆角小按钮重复三份**：`MakeItemIconButton`、`BuildPluginItemActions` 里手写的 `installBtn`（28×28、`CornerRadius(14)`）、市场刷新按钮，尺寸/圆角各写一遍。
12. **`PluginCatalog.Reload` 每次都重新 `File.Exists` 5 个候选图标 + readme**（`ResolveLocalIcon:400-427`）。每次启停开关都会触发 Reload → 每插件 6 次磁盘探测。加个以目录 mtime 为键的缓存。
13. **`PluginContext.Directory` 属性遮蔽 `System.IO.Directory`**（`Plugins.cs:117`）。在 `PluginContext` 里想写 `Directory.CreateDirectory` 会编译不过/语义混乱。改名 `PluginDirectory`。
14. **`_lastMarketPlugins / _marketFetched / _marketLoading / _enriching` 四个 bool 表达同一份"市场状态"**，且互相之间没有明确的状态机，任意两个组合都可能出现。
15. **`BuildPluginListItem` 里状态徽标和右下角按钮都放在第 3 列**（`badges` 顶部、`bottomActions` 跨两行贴底），条目被压扁时两者会重叠。建议徽标挪到名字右侧的单独容器。
16. **`PluginCatalog.ResolveLocalIcon` 用 `name.StartsWith("http")` 判断远程**，这类"前缀判 URL"的写法在项目里出现多次（`ResolveLocalReadme`、`LoadMarkdownAsync`、`LoadIconBytesAsync`）；`"httpx.png"` 也会被当 URL。
17. **`InstallPackage` 里手写 `Path.Combine(Paths.DataCache, "plugin-packages")`**（`InstallFromMarketAsync:1466`），而 `PluginService` 已有私有 `PackagesCacheDir`（:496），两处路径兜圈。
18. **`PluginInfo.Homepage` 只能来自索引**：manifest 里没有 `homepage` 字段，导致本地插件永远没有主页链接，详情页的"主页"按钮对本地插件是死的。

---

## P3 笔误 / 注释 / 文档漂移

| 位置 | 问题 |
| --- | --- |
| `Plugins.cs:1240` 注释 `// 竞态镜像` | 词不达意；这里其实是"单源拉取 + 后面回退 jsDelivr"，跟竞态无关 |
| `Plugins.cs:1573` `/// <summary>弃单个插件的缓存</summary>` | 漏字，应为"丢弃" |
| `Plugins.cs:1547` `/// <summary>回源确定 404 的插件</summary>` | 无谓语，应为"记录已确认 404 的插件" |
| `Downloader.cs:1225` `/// <summary>竟态</summary>` | 错别字：竞态 |
| `Plugins.cs:1352` `int LI(string name)` / `:1350` `I` | `I` 未被任何地方调用 |
| `docs/plugin-system.md:224` | 写的是 `PluginComponentFactories`，实际叫 `ComponentFactories`（且是 private） |
| `docs/plugin-system.md:245-250` | 给出的 `PluginLoadContext` 构造函数是 1 参版本，实际是 3 参（`pluginId, dependencies, entranceDll`） |
| `docs/plugin-system.md:135` | "目标已存在则先删除"，实现是"改名备份 → 装入 → 删备份" |
| `docs/plugin-system.md:141` | "卸载立即生效"，实现是 `Directory.Delete` 失败即写 `.uninstall` 标记延后到下次启动 |
| `docs/plugin-system.md:182` | "同插件重复注册按 name 覆盖"，实现按 pluginId 覆盖（P1-1） |
| `docs/plugin-system.md:272` | "分发链按序回落本地档案"，实现直接返回空表（P1-2） |
| `docs/plugin-system.md:393` | 页签图标写 `FASymbol.Puzzle`，代码用 `FASymbol.Shop`（`SettingsWindow:113`） |
| `docs/plugin-system.md:382` | "启动不发网络请求"，实际 `StartPluginEventTicker` 末尾就 `_ = PluginMarket.AutoUpdateAsync()`（`GlimpseonMain:327`） |
| `docs/plugin-system.md:99` | `entrance` 说明没提"必须在插件目录内"（实现也没校验，P0-3） |
| `docs/plugin-system.md:202` | 示例 `ScheduleRow(... false, false, "")`，`IsCurrent` 传了 false（P1-27） |
| `docs/plugin-system.md:63` | `.deps.json` 标"可选"：实测缺 deps.json 时 `AssemblyDependencyResolver` 只会返回 null，**不会**去插件目录探测，带私有依赖的插件必须带 deps.json |
| `examples/PluginSample/pkg/com.example.sample.gplugin` | 包内只有 `manifest.json / README.md / icon.png / PluginSample.dll`，**没有 `PluginSample.deps.json`**，与 §14.2 "构建产物 dll + deps.json" 不符 |
| `Plugins.cs:465-466` 宿主程序集白名单 | 缺 `Avalonia.Markup.Xaml / Avalonia.Themes.Fluent / Avalonia.Controls.ColorPicker / Avalonia.Controls.DataGrid / Avalonia.Markup.Xaml` —— 插件包若带上这些 dll 会产生类型分身（XAML/主题崩），建议改成"以 `Avalonia.` / `FluentAvalonia` / `GlimpseonMain` 前缀反射宿主已加载程序集" |
| `Plugins.cs:96` `m.ApiVersion != PluginApi.HostApiVersion` | 精确相等：宿主升到 apiVersion 2 时所有 v1 插件立刻全灭；参考实现（ClassIsland）用的是"最低要求"语义 |
| `Plugins.cs:818-819` `IsPluginType` | 只认 `public` 无参构造；插件类若是 `internal` + 非 public 构造会被判"未找到 GlimpseonPlugin 派生类"（而 `Activator.CreateInstance` 其实能创建 internal 类型） |
| `Plugins.cs:62` 错误文案 `manifest.json 缺失` | 建议带完整路径，多插件时无法定位是哪个包 |

---

## 建议的修复顺序

1. **止血（半天）**：P0-1 包 try、P0-6 调整判断顺序、P0-7 统一 api 兼容判断、P1-21 用候选 URL 查状态码 —— 四个都是几行的改动，收益最大。
2. **安全收口（1 天）**：`IsSafePluginId` 复用到索引 id / 元数据缓存路径；`entrance`/`icon`/`readme` 做目录包含校验；解压加体积上限；`homepage` 加 scheme 白名单。
3. **并发与生命周期（1 天）**：`ReadmeCache/ReadmeTasks` 换 `ConcurrentDictionary`；`Announcements` 加锁 + UI 线程派发；`OnUnload` 接到窗口关闭；卸载/禁用时清注册表与订阅。
4. **语义对齐（1-2 天）**：`RegisterScheduleProvider` 的多提供者模型、`PendingRestart` 覆盖本地更新、`MetaPending` 只对市场条目生效、版本比较换 semver。
5. **收尾**：删死代码与 18 个无用词条、补 `en_US` 8 个词条、按本文件 P3 表逐条同步文档或代码。
