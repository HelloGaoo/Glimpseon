// Glimpseon 示例插件
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Glimpseon.Core;

namespace PluginSample;

public class SamplePlugin : GlimpseonPlugin
{
    private TextBlock? _statusText;

    public override void OnLoad()
    {
        Context.LogInfo("已加载");

        var config = Context.ReadConfig();
        var greet = config["greeting"]?.GetValue<string>() ?? "Hello Plugin";
        config["greeting"] = greet;
        Context.WriteConfig(config);

        Context.RegisterComponent(new ComponentDefinition
        {
            Id = "sample_hello",
            DisplayName = "示例组件",
            Category = "Tools",
            Icon = "Puzzle",
            DefaultWidthPx = 400,
            DefaultHeightPx = 200,
        }, _ =>
        {
            _statusText = new TextBlock
            {
                Text = greet,
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            };
            return _statusText;
        });

        Context.RegisterScheduleProvider("示例课表", () =>
        {
            var now = Context.GetNow();
            return
            [
                new ScheduleRow("自习", "", now.ToString("HH:mm"), now.AddMinutes(40).ToString("HH:mm"), 1, false, false, ""),
            ];
        });

        Context.SubscribeTimeTick(now =>
        {
            if (_statusText is { } text)
            {
                var course = Context.GetCurrentCourse();
                text.Text = course is null
                    ? $"{greet}\n当前无课 {now:HH:mm:ss}"
                    : $"{greet}\n当前: {course.Subject} {course.StartTime}-{course.EndTime}";
            }
        });

        Context.SubscribeClassStateChanged(state =>
        {
            var text = state.Phase switch
            {
                PluginClassPhase.Class => $"上课了: {state.Current?.Subject}",
                PluginClassPhase.Break => $"课间 {state.BreakRemainingSeconds / 60 + 1} 分钟后上 {state.Next?.Subject ?? "自习"}",
                _ => state.Next is null ? "已放学" : $"未上课 下一节 {state.Next.Subject} {state.Next.StartTime}",
            };
            Context.LogInfo($"时段变化: {text}");
            Context.ShowNotification(text);
        });

        Context.SubscribeAppClosing(() => Context.LogInfo("应用即将退出 保存状态"));

        Context.PushAnnouncement("示例插件已加载");
    }

    public override void OnUnload()
    {
        Context.LogInfo("已卸载");
    }
}
