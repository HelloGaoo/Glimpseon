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
import logging
from PyQt5.QtCore import QObject, pyqtSignal

logger = logging.getLogger(__name__)


class QuickLaunchConfig(QObject):
    quickLaunchChanged = pyqtSignal()

    def __init__(self):
        super().__init__()
        from core.config import cfg
        self._cfg = cfg

    @property
    def show_quick_launch(self):
        return self._cfg.showQuickLaunch.value

    @show_quick_launch.setter
    def show_quick_launch(self, value):
        self._cfg.showQuickLaunch.value = value

    @property
    def quick_launch_apps(self):
        apps = self._cfg.quickLaunchApps.value
        if not apps:
            return self._default_apps()
        return apps

    @quick_launch_apps.setter
    def quick_launch_apps(self, value):
        self._cfg.quickLaunchApps.value = value

    @property
    def icon_size(self):
        return self._cfg.quickLaunchIconSize.value

    @icon_size.setter
    def icon_size(self, value):
        self._cfg.quickLaunchIconSize.value = value

    @property
    def icon_spacing(self):
        return self._cfg.quickLaunchIconSpacing.value

    @icon_spacing.setter
    def icon_spacing(self, value):
        self._cfg.quickLaunchIconSpacing.value = value

    @property
    def show_labels(self):
        return self._cfg.quickLaunchShowLabels.value

    @show_labels.setter
    def show_labels(self, value):
        self._cfg.quickLaunchShowLabels.value = value

    def _default_apps(self):
        return []

    def save(self, emit_signal=True):
        from core.config import save_cfg
        save_cfg()
        if emit_signal:
            self.quickLaunchChanged.emit()

    def _create_default_config(self, emit_signal=True):
        self._cfg.quickLaunchApps.value = self._default_apps()
        self._cfg.quickLaunchIconSize.value = 64
        self._cfg.quickLaunchIconSpacing.value = 12
        self._cfg.quickLaunchShowLabels.value = False
        self.save(emit_signal=emit_signal)

    def set_show(self, show):
        self.show_quick_launch = show
        self.save()

    def set_apps(self, apps):
        self.quick_launch_apps = apps
        self.save()

    def set_icon_size(self, size):
        self.icon_size = size
        self.save()

    def set_icon_spacing(self, spacing):
        self.icon_spacing = spacing
        self.save()

    def set_show_labels(self, show):
        self.show_labels = show
        self.save()

    def remove_app(self, index):
        apps = list(self.quick_launch_apps)
        if 0 <= index < len(apps):
            apps.pop(index)
            self.quick_launch_apps = apps
            self.save()

    def update_app(self, index, app):
        apps = list(self.quick_launch_apps)
        if 0 <= index < len(apps):
            apps[index] = app
            self.quick_launch_apps = apps
            self.save()


ql_cfg = QuickLaunchConfig()
