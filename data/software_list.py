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
from core.constants import get_resource_path

SOFTWARE_CATEGORIES = [
    {
        "name": "常用软件",
        "software": [
            {
                "name": "微信",
                "description": "",
                "icon": "微信.ico"
            },
            {
                "name": "QQ",
                "description": "",
                "icon": "QQ.ico"
            },
            {
                "name": "UU远程",
                "description": "",
                "icon": "UU远程.ico"
            },
            {
                "name": "网易云音乐",
                "description": "",
                "icon": "网易云音乐.ico"
            },
            {
                "name": "office2021",
                "description": "",
                "icon": "office2021.ico"
            }
        ]
    },
    {
        "name": "希沃系列",
        "software": [
            {
                "name": "希沃白板5",
                "description": "",
                "icon": "希沃白板5.ico"
            },
            {
                "name": "剪辑师",
                "description": "",
                "icon": "剪辑师.ico"
            },
            {
                "name": "知识胶囊",
                "description": "",
                "icon": "知识胶囊.ico"
            },
            {
                "name": "掌上看班",
                "description": "",
                "icon": "掌上看班.ico"
            },
            {
                "name": "希沃轻白板",
                "description": "",
                "icon": "希沃轻白板.ico"
            },
            {
                "name": "希沃智能笔",
                "description": "",
                "icon": "希沃智能笔.ico"
            },
            {
                "name": "希沃输入法",
                "description": "",
                "icon": "希沃输入法.ico"
            },
            {
                "name": "希沃快传",
                "description": "",
                "icon": "希沃快传.ico"
            },
            {
                "name": "希沃管家",
                "description": "",
                "icon": "希沃管家.ico"
            },
            {
                "name": "希沃壁纸",
                "description": "",
                "icon": "希沃壁纸.ico"
            },
            {
                "name": "希沃集控",
                "description": "",
                "icon": "希沃集控.ico"
            },
            {
                "name": "希沃导播助手",
                "description": "",
                "icon": "希沃导播助手.ico"
            },
            {
                "name": "希沃视频展台",
                "description": "",
                "icon": "希沃视频展台.ico"
            },
            {
                "name": "希沃课堂助手",
                "description": "",
                "icon": "希沃课堂助手.ico"
            },
            {
                "name": "希沃电脑助手",
                "description": "",
                "icon": "希沃电脑助手.ico"
            },
            {
                "name": "希沃易课堂",
                "description": "",
                "icon": "希沃易课堂.ico"
            },
            {
                "name": "PPT小工具",
                "description": "",
                "icon": "PPT小工具.ico"
            },
            {
                "name": "希沃物联校园",
                "description": "",
                "icon": "希沃物联校园.ico"
            },
            {
                "name": "远程互动课堂",
                "description": "",
                "icon": "远程互动课堂.ico"
            },
            {
                "name": "省平台登录插件",
                "description": "",
                "icon": "省平台登录插件.ico"
            },
            {
                "name": "希象传屏[发送端]",
                "description": "",
                "icon": "希象传屏[发送端].ico"
            },
            {
                "name": "希沃品课[小组端]",
                "description": "",
                "icon": "希沃品课[小组端].ico"
            },
            {
                "name": "希沃品课[教师端]",
                "description": "",
                "icon": "希沃品课[教师端].ico"
            }
        ]
    },
    {
        "name": "系统工具",
        "software": [
            {
                "name": "激活工具",
                "description": "",
                "icon": "激活工具.ico"
            }
        ]
    },
    {
        "name": "课表软件",
        "software": [
            {
                "name": "ClassIsland2",
                "description": "",
                "icon": "ClassIsland2.ico"
            },
            {
                "name": "ClassWidgets",
                "description": "",
                "icon": "ClassWidgets.ico"
            }
        ]
    }
]


def get_software_icon_path(icon_filename):
    if not icon_filename:return get_resource_path(os.path.join("resource", "icons", "CY.png"))
    icon_path = get_resource_path(os.path.join("resource", "icons", "software", icon_filename))
    if os.path.exists(icon_path):return icon_path
    return get_resource_path(os.path.join("resource", "icons", "CY.png"))
