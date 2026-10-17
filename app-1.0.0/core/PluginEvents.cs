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
        lock (Subscriptions)
        {
            Subscriptions.Add(s);
        }
        Log.Info($"[plugin:{s.PluginId}] 订阅事件 {s.Event}");
    }

    public static void RaiseTimeTick(DateTime now) => Raise("TimeTick", now);

    public static void RaiseCourseChanged(ScheduleRow? row) => Raise("CourseChanged", row);

    public static void RaiseClassStateChanged(PluginClassState state)
    {
        LatestClassState = state;
        Raise("ClassStateChanged", state);
    }

    public static void RaiseAppClosing() => Raise("AppClosing", null);

    private static void Raise(string name, object? payload)
    {
        PluginSubscription[] snapshot;
        lock (Subscriptions)
        {
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
                Log.Warning($"[plugin:{s.PluginId}] {name} 处理失败: {e.Message}");
            }
        }
    }
}
