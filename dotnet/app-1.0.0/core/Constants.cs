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

// 常量定义
namespace Glimpseon.Core;

public static class Constants
{
    public const string AppName = "Glimpseon";
    public const string AppIcon = "Assets/icons/CY.png";
    public const string AppLicense = "LICENSE";

    public const string ExternalClassWidgets = "ClassWidgets";
    public const string ExternalClassIsland = "ClassIsland";

    public static readonly string[] TimetableSources = { "Glimpseon", "ClassIsland", "ClassWidgets" };
    public const string TimetableSourceGlimpseon = "Glimpseon";
    public const string TimetableSourceClassIsland = "classisland";
    public const string TimetableSourceClassWidgets = "classwidgets";

    public const string ResourceRoot = "Assets";
    public const string ResourceIcons = "Assets/icons";
    public const string ResourceWallpaper = "Assets/wallpaper";
    public const string ResourceCityDb = "Assets/city.db";
    public const string ResourceCredits = "Assets/credits.json";
    public const string ResourceDefaultWallpaper = "Assets/wallpaper/default.jpg";

    public static readonly IReadOnlyDictionary<string, string> NewsIcons = new Dictionary<string, string>
    {
        ["baidu"] = "Assets/icons/news/baidu.svg",
        ["weibo"] = "Assets/icons/news/weibo.svg",
        ["jinritoutiao"] = "Assets/icons/news/jinritoutiao.svg",
        ["tencent"] = "Assets/icons/news/tencent.svg",
        ["cctv"] = "Assets/icons/news/cctv.svg",
    };

    public const string FontPrimary = "HarmonyOS Sans";
    public const string FontFamily = "HarmonyOS Sans, HarmonyOS Sans SC, Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Arial";
}
