# ClassLively
# Copyright (C) 2026 HelloGaoo
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
"""

from .edit_panel import EditPanel
from .update import UpdateInterface
from .about import AboutInterface
from .download import DownloadInterface
from .wallpaper import WallpaperInterface
from .base_scroll import BaseScrollAreaInterface
from .wizard import WizardWindow, check_wizard_needed, create_wizard_file, complete_wizard
from .home import HomeInterface
from .debug import DebugPanel

__all__ = [
    'BaseScrollAreaInterface',
    'UpdateInterface',
    'AboutInterface',
    'DownloadInterface',
    'WallpaperInterface',
    'EditPanel',
    'WizardWindow',
    'check_wizard_needed',
    'create_wizard_file',
    'complete_wizard',
    'HomeInterface',
    'DebugPanel',
]
