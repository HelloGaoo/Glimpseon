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
