// Glimpseon
// Copyright (C) 2026 HelloGaoo
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text.Json;

namespace Glimpseon.Core;

public enum PluginClassPhase
{
    AfterSchool,
    Class,
    Break,
}

public sealed record PluginClassState
{
    public PluginClassPhase Phase { get; init; }
    public ScheduleRow? Current { get; init; }
    public ScheduleRow? Next { get; init; }
    public int BreakRemainingSeconds { get; init; }
}

internal sealed class PluginSubscription
{
    public required string PluginId { get; init; }
    public required string Event { get; init; }
    public required Action<object?> Handler { get; init; }
}

public static class PluginEventBus
{
    private static readonly List<PluginSubscription> Subscriptions = new();

    public static PluginClassState? LatestClassState { get; internal set; }

    public static bool HasSubscribers(string name)
    {
        lock (Subscriptions)
        {
            return Subscriptions.Any(s => s.Event == name);
        }
    }

    internal static void Add(PluginSubscription s)
    {
        var duplicate = false;
        lock (Subscriptions)
        {
            duplicate = Subscriptions.Any(x => x.PluginId == s.PluginId
                && x.Event == s.Event
                && Equals(x.Handler.Target, s.Handler.Target)
                && x.Handler.Method == s.Handler.Method);
            if (!duplicate)
            {
                Subscriptions.Add(s);
            }
        }
        if (duplicate)
        {
            Log.Debug($"[plugin:{s.PluginId}] 订阅 {s.Event} 已存在 跳过");
            return;
        }
        Log.Info($"[plugin:{s.PluginId}] 订阅事件 {s.Event}");
    }

    /// <summary>移除某插件的全部订阅</summary>
    public static void RemovePlugin(string pluginId)
    {
        int removed;
        lock (Subscriptions)
        {
            removed = Subscriptions.RemoveAll(s => s.PluginId == pluginId);
        }
        if (removed > 0)
        {
            Log.Info($"[plugin:{pluginId}] 已移除 {removed} 个事件订阅");
        }
    }

    public static void RemoveAll()
    {
        lock (Subscriptions)
        {
            Subscriptions.Clear();
        }
    }

    public static void RaiseTimeTick(DateTime now) => Raise(PluginApi.EventTimeTick, now);

    public static void RaiseCourseChanged(ScheduleRow? row) => Raise(PluginApi.EventCourseChanged, row);

    public static void RaiseClassStateChanged(PluginClassState state)
    {
        LatestClassState = state;
        Raise(PluginApi.EventClassStateChanged, state);
    }

    public static void RaiseAppClosing() => Raise(PluginApi.EventAppClosing, null);

    private static void Raise(string name, object? payload)
    {
        PluginSubscription[] snapshot;
        lock (Subscriptions)
        {
            if (Subscriptions.Count == 0)
            {
                return;
            }
            snapshot = Subscriptions.Where(s => s.Event == name).ToArray();
        }
        foreach (var s in snapshot)
        {
            try
            {
                s.Handler(payload);
            }
            catch (Exception e)
            {
                LogHandlerError(s.PluginId, name, e);
            }
        }
    }

    // 插件事件节流
    private static readonly Dictionary<string, (int Count, DateTime Window)> ErrorCounters = new(StringComparer.Ordinal);

    private static void LogHandlerError(string pluginId, string name, Exception e)
    {
        var key = pluginId + "|" + name;
        lock (ErrorCounters)
        {
            var now = DateTime.UtcNow;
            if (!ErrorCounters.TryGetValue(key, out var entry) || now - entry.Window > TimeSpan.FromMinutes(1))
            {
                ErrorCounters[key] = (1, now);
                Log.Warning($"[plugin:{pluginId}] {name} 处理失败 {e.GetType().Name}: {e.Message}");
                return;
            }
            if (entry.Count < 3)
            {
                ErrorCounters[key] = (entry.Count + 1, entry.Window);
                Log.Warning($"[plugin:{pluginId}] {name} 处理失败 {e.GetType().Name}: {e.Message}");
            }
            else if (entry.Count == 3)
            {
                ErrorCounters[key] = (entry.Count + 1, entry.Window);
                Log.Warning($"[plugin:{pluginId}] {name} 持续失败, 本轮不再记录");
            }
        }
    }
}
