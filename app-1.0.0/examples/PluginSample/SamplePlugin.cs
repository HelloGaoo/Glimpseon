// Glimpseon 示例插件
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Glimpseon.Core;

namespace PluginSample;

public class SamplePlugin : GlimpseonPlugin
{
    public override void OnLoad()
    {
        Context.LogInfo("已加载");

        // 私有配置读写
        var config = Context.ReadConfig();
        var greet = config["greeting"]?.GetValue<string>() ?? "Hello Plugin";
        config["greeting"] = greet;
        Context.WriteConfig(config);

        // 注册主界面组件
        Context.RegisterComponent(new ComponentDefinition
        {
            Id = "sample_hello",
            DisplayName = "示例组件",
            Category = "Tools",
            Icon = "Puzzle",
            DefaultWidthPx = 400,
            DefaultHeightPx = 200,
        }, _ => new TextBlock
        {
            Text = greet,
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // 注册课表数据提供者
        Context.RegisterScheduleProvider("示例课表", () =>
        {
            var now = AppUtils.PreciseNow();
            return
            [
                new ScheduleRow("自习", "", now.ToString("HH:mm"), now.AddMinutes(40).ToString("HH:mm"), 1, false, false, ""),
            ];
        });

        // 公告
        Context.PushAnnouncement("示例插件已加载");
    }

    public override void OnUnload()
    {
        Context.LogInfo("已卸载");
    }
}
