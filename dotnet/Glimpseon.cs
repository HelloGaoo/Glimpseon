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

// 启动器
using System.Diagnostics;
using System.Text.Json;

namespace Glimpseon;

internal static class Launcher
{
    private static int Main()
    {
        var root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidates = new List<(string Dir, int Current, Version Version)>();

        foreach (var dir in Directory.EnumerateDirectories(root, "app-*"))
        {
            var name = Path.GetFileName(dir);
            var recordPath = Path.Combine(dir, "record.json");
            if (!File.Exists(recordPath))
            {
                Console.WriteLine($"跳过 {name}: 无 record.json");
                continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(recordPath));
                var element = doc.RootElement;
                if (element.TryGetProperty("partial", out var partial) && partial.ValueKind == JsonValueKind.True)
                {
                    Console.WriteLine($"跳过 {name}");
                    continue;
                }
                var current = element.TryGetProperty("current", out var cur) && cur.ValueKind == JsonValueKind.Number ? cur.GetInt32() : 0;
                Version.TryParse(name.AsSpan("app-".Length), out var version);
                candidates.Add((dir, current, version ?? new Version(0, 0, 0)));
            }
            catch (Exception e)
            {
                Console.WriteLine($"跳过 {name}: record.json 解析失败 - {e.Message}");
            }
        }

        if (candidates.Count == 0)
        {
            Console.WriteLine("找不到app");
            return 1;
        }

        var selected = candidates
            .OrderByDescending(x => x.Current)
            .ThenByDescending(x => x.Version.Major)
            .ThenByDescending(x => x.Version.Minor)
            .ThenByDescending(x => x.Version.Build)
            .First().Dir;

        var mainExe = Path.Combine(selected, "GlimpseonMain.exe");
        if (!File.Exists(mainExe))
        {
            Console.WriteLine($"找不到主程序 {mainExe}");
            return 1;
        }

        Console.WriteLine($"启动版本: {Path.GetFileName(selected)}");

        var psi = new ProcessStartInfo
        {
            FileName = mainExe,
            UseShellExecute = false,
            WorkingDirectory = selected,
        };
        psi.Environment["Glimpseon_PackageRoot"] = root;
        psi.Environment["Glimpseon_AppDir"] = selected;

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return 1;
        }
        proc.WaitForExit();
        return proc.ExitCode;
    }
}
