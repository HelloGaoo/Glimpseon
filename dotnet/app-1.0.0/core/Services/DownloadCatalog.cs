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

// 下载目录数据
namespace Glimpseon.Core.Services;

public sealed record SoftwareEntry(string Name, string Description, string Icon, string? Link);

public sealed record SoftwareCategory(string NameKey, IReadOnlyList<SoftwareEntry> Software);

public sealed record DownloadUrlEntry(string Filename, string? Url, string? GithubPath);

public static class DownloadCatalog
{
    public static readonly SoftwareCategory[] Categories =
    {
        new("download.cat_common", new[]
        {
            new SoftwareEntry("微信", "微信，是一个生活方式", "微信.ico", "https://weixin.qq.com/"),
            new SoftwareEntry("QQ", "QQ-轻松做自己", "QQ.ico", "https://im.qq.com/"),
            new SoftwareEntry("UU远程", "真4K、真免费、真好用", "UU远程.ico", "https://uuyc.163.com/"),
            new SoftwareEntry("网易云音乐", "发现好音乐", "网易云音乐.ico", "https://music.163.com/"),
            new SoftwareEntry("office2021", "Office2021 专业增强版三件套", "office2021.ico", "https://www.microsoft.com/en-us/download/details.aspx?id=49117"),
        }),
        new("download.cat_seewo", new[]
        {
            new SoftwareEntry("希沃白板5", "为互动教学而生 | 课件制作神器", "希沃白板5.ico", "https://seewo.com/"),
            new SoftwareEntry("剪辑师", "一分钟成为剪辑师", "剪辑师.ico", "https://seewo.com/"),
            new SoftwareEntry("知识胶囊", "互动式微课录制剪辑工具", "知识胶囊.ico", "https://seewo.com/"),
            new SoftwareEntry("掌上看班", "实时了解教室情况", "掌上看班.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃轻白板", "易上手的白板书写软件", "希沃轻白板.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃智能笔", "懂老师的希沃智能笔", "希沃智能笔.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃输入法", "专为教师设计的输入法", "希沃输入法.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃快传", "走到教室 即刻授课", "希沃快传.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃管家", "系统防护 安全纯净", "希沃管家.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃壁纸", "希沃原版桌面壁纸", "希沃壁纸.ico", null),
            new SoftwareEntry("希沃集控", "远程管理 灵活高效", "希沃集控.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃导播助手", "简单完成导播控制", "希沃导播助手.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃视频展台", "轻松展示助力课堂", "希沃视频展台.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃课堂助手", "新版 PPT 小工具", "希沃课堂助手.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃电脑助手", "专注教师 轻松办公", "希沃电脑助手.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃易课堂", "全员参与 互动生成", "希沃易课堂.ico", "https://seewo.com/"),
            new SoftwareEntry("PPT小工具", "希沃针对 PPT 的优化工具", "PPT小工具.ico", "https://seewo.com/"),
            new SoftwareEntry("希沃物联校园", "设备互联互通与高效管理", "希沃物联校园.ico", "https://seewo.com/"),
            new SoftwareEntry("远程互动课堂", "高效完成在线教学教研", "远程互动课堂.ico", "https://seewo.com/"),
            new SoftwareEntry("省平台登录插件", "登录插件下载", "省平台登录插件.ico", "https://seewo.com/"),
            new SoftwareEntry("希象传屏 [发送端]", "轻松投屏快速演示", "希象传屏[发送端].ico", "https://seewo.com/"),
            new SoftwareEntry("希沃品课[小组端]", "构建高校多元互动教学", "希沃品课[小组端].ico", "https://seewo.com/"),
            new SoftwareEntry("希沃品课[教师端]", "构建高校多元互动教学", "希沃品课[教师端].ico", "https://seewo.com/"),
        }),
        new("download.cat_system", new[]
        {
            new SoftwareEntry("激活工具", "简洁高效的 KMS/OEM 智能激活工具", "激活工具.ico", ""),
        }),
        new("download.cat_schedule", new[]
        {
            new SoftwareEntry("ClassIsland2", "你的课表，无限可能", "ClassIsland2.ico", "https://classisland.tech/"),
            new SoftwareEntry("ClassWidgets", "多样的桌面课表 由我们定义的全新桌面形态", "ClassWidgets.ico", "https://classwidgets.rinlit.cn/zh/"),
        }),
    };

    public static readonly DownloadUrlEntry[] UrlDir =
    {
        new("剪辑师.exe", "https://store-g1.seewo.com/seewo-report_a8af6d2a461847f1b851d31a6b391428?attname=Jianjishi_1.7.0.775.exe", null),
        new("知识胶囊.exe", "https://cstore-pub-seewo-report-tx.seewo.com/seewo-report_fd2dc77b5ee24f83a9f6ce257e44fb4d?attname=EasiCapsuleSetup_2.4.0.7802.exe", null),
        new("掌上看班.exe", "https://imlizhi-store-https.seewo.com/SeewoHugoKanbanWebApp_1.4.5.68(20240329093729).exe", null),
        new("激活工具.7z", null, "/HelloGaoo/SeevvoDownloader/releases/download/v1.0.0/HEU_KMS_Activator.7z"),
        new("希沃壁纸.7z", null, "/HelloGaoo/SeevvoDownloader/releases/download/v1.0.0/seewoWallpaper.7z"),
        new("希沃管家.exe", "https://cstore-pub-seewo-report-tx.seewo.com/seewo-report_79fc6c21a6694bf29160feda273b99c7?attname=SeewoServiceSetup_1.3.6.3254.exe", null),
        new("希沃快传.exe", "https://imlizhi-store-https.seewo.com/SeewoFileTransfer_2.0.10(20240830095652).exe", null),
        new("希沃集控.exe", "https://store-g1.seewo.com/seewo-report_abc60b691ca74da088507021f92bc381?attname=SeewoHugoWebApp_1.1.8.42.exe", null),
        new("希沃智能笔.exe", "https://imlizhi-store-https.seewo.com/SmartpenServiceSetup_2.0.1.749(20240619165806).exe", null),
        new("希沃易课堂.exe", "https://cstore-pub-seewo-report-tx.seewo.com/seewo-report_31a18b9dc7e74439b42669918dbdaf55?attname=EasiClassSetup_2.1.22.6341.exe", null),
        new("希沃输入法.exe", "https://imlizhi-store-https.seewo.com/seewoinput_1.0.5(20250820092142).exe", null),
        new("PPT小工具.exe", "https://store-g1.seewo.com/seewo-report_6594548a69c34306af2c9cc73a060e19?attname=PPTServiceSetup_1.0.0.795.exe", null),
        new("希沃轻白板.exe", "https://imlizhi-store-https.seewo.com/EasiNote5C_1.0.1.8095(20240703115236).exe", null),
        new("希沃白板5.exe", "https://cstore-pub-seewo-report-tx.seewo.com/seewo-report_2fc45eb4318e41c4bc538fd0660bae43?attname=EasiNoteSetup_5.2.4.9120_seewo.exe", null),
        new("希沃课堂助手.exe", "https://cstore-pub-seewo-report-tx.seewo.com/seewo-report_83de79eec5a94c07a337baeab68a8b07?attname=SeewoIwbAssistant_0.0.3.1207.exe", null),
        new("希沃电脑助手.exe", "https://imlizhi-store-https.seewo.com/seewoPCAssistant_2.1.6(20250523210530).exe", null),
        new("希沃导播助手.exe", "https://imlizhi-store-https.seewo.com/EasiDirector_1.0.10.195(20211105150841).exe", null),
        new("希沃视频展台.exe", "https://cstore-pub-seewo-report-tx.seewo.com/seewo-report_12f92ef48ca24ec982a5803393c2f719?attname=EasiCameraSetup_2.0.10.3816.exe", null),
        new("希沃物联校园.exe", "https://imlizhi-store-https.seewo.com/SeewoIotManageWebApp_1.0.0.8(20210609110648).exe", null),
        new("远程互动课堂.exe", "https://imlizhi-store-https.seewo.com/AirTeach_AirteachSetup_2.0.17.17064(20250507123142).exe", null),
        new("省平台登录插件.exe", "https://imlizhi-store-https.seewo.com/EasiNote_plugin_anhui_V0.1(20200616170758).exe", null),
        new("希象传屏[发送端].exe", "https://imlizhi-store-https.seewo.com/ExceedShare_6.7.1.20(20250610165636).exe", null),
        new("希沃品课[小组端].exe", "https://cstore-pub-seewo-report-tx.seewo.com/seewo-report_5d829b9cd5e24d5fa1c0a2b5602c9d6e?attname=seewoPincoGroupSetup_1.2.30.1640.exe", null),
        new("希沃品课[教师端].exe", "https://imlizhi-store-https.seewo.com/seewoPincoTeacher_1.2.43.7285(20250530191221).exe", null),
        new("ClassIsland2.exe", "https://get.classisland.tech/d/ClassIsland-Ningbo-S3/classisland/distribution-v2/2.0/2.0.0.2/ClassIsland_app_windows_x64_selfContained_folder.zip", null),
        new("ClassWidgets.exe", "https://ghfile.geekertao.top/https://github.com/Class-Widgets/Class-Widgets/releases/download/v1.2.0.1/ClassWidgets-Windows-x64.zip", null),
        new("微信.exe", "https://dldir1v6.qq.com/weixin/Universal/Windows/WeChatWin.exe", null),
        new("QQ.exe", "https://qqdl.gtimg.cn/qqfile/QQNT/9.9.33/release/497e2f1f/QQ_9.9.33_260813_x64_01.exe", null),
        new("UU远程.exe", "https://a56.gdl.netease.com/UURemote_Setup_4.21.0.7755_0424202757_gwqd.exe", null),
        new("网易云音乐.exe", "https://d8.music.126.net/dmusic2/NeteaseCloudMusic_Music_official_3.1.35.205293_64.exe", null),
        new("office2021.7z", null, "/HelloGaoo/SeevvoDownloader/releases/download/v1.0.0/office2021.7z"),
    };

    // 按软件名查直链
    public static DownloadUrlEntry? FindDownloadUrl(string softwareName)
    {
        foreach (var candidate in new[] { $"{softwareName}.exe", $"{softwareName}.7z", softwareName })
        {
            var hit = UrlDir.FirstOrDefault(u => u.Filename == candidate);
            if (hit is not null)
            {
                return hit;
            }
        }
        return null;
    }

    public static string GetSoftwareIconPath(string? iconFilename)
    {
        if (string.IsNullOrEmpty(iconFilename))
        {
            return Paths.GetResourcePath(Constants.AppIcon);
        }
        var candidates = new[]
        {
            Paths.GetResourcePath(Path.Combine("Assets", "icons", "software_icon", iconFilename)),
            Paths.GetResourcePath(Path.Combine("Assets", "icons", "default_icon", iconFilename)),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
        return Paths.GetResourcePath(Constants.AppIcon);
    }
}
