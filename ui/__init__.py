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
UI 模块
包含所有界面类
"""

from .edit_panel import EditPanel
from .movable_widget import MovableWidget
from .update_interface import UpdateInterface
from .about_interface import AboutInterface
from .download_interface import DownloadInterface
from .wallpaper_interface import WallpaperInterface
from .base_scroll_area import BaseScrollAreaInterface

__all__ = [
    'BaseScrollAreaInterface',
    'UpdateInterface',
    'AboutInterface',
    'DownloadInterface',
    'WallpaperInterface',
    'EditPanel',
    'MovableWidget'
]
