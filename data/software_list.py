# ClassLively
# Copyright (C) 2026 HelloGaoo & WHYOS
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

"""
软件列表
"""

import os
from core.constants import get_resPath

SOFTWARE_CATEGORIES = [
    {
        "name": "常用软件",
        "software": [
            {
                "name": "微信",
                "description": "微信，是一个生活方式",
                "icon": "微信.ico"
            },
            {
                "name": "QQ",
                "description": "QQ-轻松做自己",
                "icon": "QQ.ico"
            },
            {
                "name": "UU远程",
                "description": "真4K、真免费、真好用",
                "icon": "UU远程.ico"
            },
            {
                "name": "网易云音乐",
                "description": "发现好音乐",
                "icon": "网易云音乐.ico"
            },
            {
                "name": "office2021",
                "description": "Office2021专业增强版三件套",
                "icon": "office2021.ico"
            }
        ]
    },
    {
        "name": "希沃系列",
        "software": [
            {
                "name": "希沃白板5",
                "description": "为互动教学而生 | 课件制作神器",
                "icon": "希沃白板5.ico"
            },
            {
                "name": "剪辑师",
                "description": "一分钟成为剪辑师",
                "icon": "剪辑师.ico"
            },
            {
                "name": "知识胶囊",
                "description": "互动式微课录制剪辑工具",
                "icon": "知识胶囊.ico"
            },
            {
                "name": "掌上看班",
                "description": "实时了解教室情况",
                "icon": "掌上看班.ico"
            },
            {
                "name": "希沃轻白板",
                "description": "易上手的白板书写软件",
                "icon": "希沃轻白板.ico"
            },
            {
                "name": "希沃智能笔",
                "description": "懂老师的希沃智能笔",
                "icon": "希沃智能笔.ico"
            },
            {
                "name": "希沃输入法",
                "description": "专为教师设计的输入法",
                "icon": "希沃输入法.ico"
            },
            {
                "name": "希沃快传",
                "description": "走到教室 即刻授课",
                "icon": "希沃快传.ico"
            },
            {
                "name": "希沃管家",
                "description": "系统防护 安全纯净",
                "icon": "希沃管家.ico"
            },
            {
                "name": "希沃壁纸",
                "description": "希沃原版桌面壁纸",
                "icon": "希沃壁纸.ico"
            },
            {
                "name": "希沃集控",
                "description": "远程管理 灵活高效",
                "icon": "希沃集控.ico"
            },
            {
                "name": "希沃导播助手",
                "description": "简单完成导播控制",
                "icon": "希沃导播助手.ico"
            },
            {
                "name": "希沃视频展台",
                "description": "轻松展示助力课堂",
                "icon": "希沃视频展台.ico"
            },
            {
                "name": "希沃课堂助手",
                "description": "新版 PPT 小工具",
                "icon": "希沃课堂助手.ico"
            },
            {
                "name": "希沃电脑助手",
                "description": "专注教师 轻松办公",
                "icon": "希沃电脑助手.ico"
            },
            {
                "name": "希沃易课堂",
                "description": "全员参与 互动生成",
                "icon": "希沃易课堂.ico"
            },
            {
                "name": "PPT小工具",
                "description": "希沃针对PPT的优化工具",
                "icon": "PPT小工具.ico"
            },
            {
                "name": "希沃物联校园",
                "description": "设备互联互通与高效管理",
                "icon": "希沃物联校园.ico"
            },
            {
                "name": "远程互动课堂",
                "description": "高效完成在线教学教研",
                "icon": "远程互动课堂.ico"
            },
            {
                "name": "省平台登录插件",
                "description": "登录插件下载",
                "icon": "省平台登录插件.ico"
            },
            {
                "name": "希象传屏[发送端]",
                "description": "轻松投屏快速演示",
                "icon": "希象传屏[发送端].ico"
            },
            {
                "name": "希沃品课[小组端]",
                "description": "构建高校多元互动教学",
                "icon": "希沃品课[小组端].ico"
            },
            {
                "name": "希沃品课[教师端]",
                "description": "构建高校多元互动教学",
                "icon": "希沃品课[教师端].ico"
            }
        ]
    },
    {
        "name": "系统工具",
        "software": [
            {
                "name": "激活工具",
                "description": "简洁高效的KMS/OEM智能激活工具",
                "icon": "激活工具.ico"
            }
        ]
    },
    {
        "name": "课表软件",
        "software": [
            {
                "name": "ClassIsland2",
                "description": "你的课表，无限可能",
                "icon": "ClassIsland2.ico"
            },
            {
                "name": "ClassWidgets",
                "description": "多样的桌面课表 由我们定义的全新桌面形态",
                "icon": "ClassWidgets.ico"
            }
        ]
    }
]


def get_software_icon_path(icon_filename):
    if not icon_filename:
        return get_resPath(os.path.join("resource", "icons", "CY.png"))
    icon_path = get_resPath(os.path.join("data", "software_icon", icon_filename))
    if os.path.exists(icon_path):
        return icon_path
    return get_resPath(os.path.join("resource", "icons", "CY.png"))
