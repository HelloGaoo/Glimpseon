# 示例插件

Glimpseon 测试插件

## 功能

- 注册一个 `sample_hello` 组件
- 注册一个"示例课表"课表提供者
- 每秒接收一次 `TimeTick` 事件

## 目录结构

| 文件                    | 说明                        |
| --------------------- | ------------------------- |
| `SamplePlugin.cs`     | 插件入口，继承 `GlimpseonPlugin` |
| `manifest.json`       | 插件清单，字段见下                 |
| `PluginSample.csproj` | 项目文件，输出插件程序集              |

## manifest.json

```json
{
  "id": "com.example.sample",
  "name": "示例插件",
  "version": "1.0.0",
  "author": "HelloGaoo",
  "description": "演示插件",
  "entrance": "PluginSample.dll",
  "apiVersion": 1,
  "readme": "README.md"
}
```

- `id`：反向域名、全小写，安装目录名必须与之一致
- `entrance`：入口程序集（相对插件目录）
- `apiVersion`：宿主 API 版本，当前为 `1`
- `icon` / `readme`：可选的图标与说明文件，相对插件目录，也可填 http 地址

## 打包

把 `plugin.dll`、`manifest.json`、`README.md` 及图标一起压缩成 `.gplugin`（就是 zip）：

```bash
cd app-1.0.0/examples/PluginSample
dotnet build -c Release
# 将 bin/Release/net10.0-windows/ 下的 PluginSample.dll 与 manifest.json、README.md 一起打成 zip
```

也可以直接把 `.gplugin` 丢进 `data/plugins/` 目录，下次启动会自动安装。
