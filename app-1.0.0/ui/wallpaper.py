# Glimpseon
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
壁纸界面模块
"""

import os
import datetime
import json
import logging
from dataclasses import dataclass, asdict
from typing import List, Optional

import requests
from PyQt6.QtCore import Qt, pyqtSignal, QTimer, QSize
from PyQt6.QtGui import QPixmap, QImageReader
from PyQt6.QtWidgets import (
    QFileDialog,
    QHBoxLayout,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)
from qfluentwidgets import (
    BodyLabel,
    CardWidget,
    ComboBoxSettingCard,
    InfoBar,
    MessageBox,
    MessageBoxBase,
    PrimaryPushButton,
    PushButton,
    RangeSettingCard,
    ScrollArea,
    SettingCardGroup,
    StrongBodyLabel,
    SubtitleLabel,
    SwitchSettingCard,
    Theme,
)

from core.config import cfg
from core.constants import WALLPAPER_DIR, get_resPath, load_qss, RESOURCE_DEFAULT_WALLPAPER
from core.utils import get_cached_content, save_cache, tr, TranslatableWidget, INTERVAL_MAP, FUI

logger = logging.getLogger("Glimpseon.ui.wallpaper")

HISTORY_FILE_NAME = "history.json"
HISTORY_VERSION = 1
MAX_HISTORY_RECORDS = 100


@dataclass
class WallpaperRecord:
    """壁纸记录"""
    id: str
    path: str
    source: str
    api_url: str
    added_time: str
    file_size: int
    resolution: str
    
    def exists(self) -> bool:
        return os.path.exists(self.path)
    
    def to_dict(self) -> dict:
        return asdict(self)
    
    @classmethod
    def from_dict(cls, data: dict) -> 'WallpaperRecord':
        return cls(
            id=data.get('id', ''),
            path=data.get('path', ''),
            source=data.get('source', ''),
            api_url=data.get('api_url', ''),
            added_time=data.get('added_time', ''),
            file_size=data.get('file_size', 0),
            resolution=data.get('resolution', '未知')
        )


class WallpaperHistory:
    """壁纸历史管理"""

    _instance: Optional['WallpaperHistory'] = None

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super().__new__(cls)
            cls._instance._initialized = False
        return cls._instance

    def __init__(self):
        if self._initialized:
            return
        self._initialized = True

        self.wallpaper_dir = WALLPAPER_DIR
        self.history_file = os.path.join(self.wallpaper_dir, HISTORY_FILE_NAME)
        self._history: List[WallpaperRecord] = []
        self._load()
    
    def _load(self):
        if not os.path.exists(self.history_file):
            self._history = []
            return
        
        try:
            with open(self.history_file, 'r', encoding='utf-8') as f:data = json.load(f)
            if data.get('version') != HISTORY_VERSION:
                self._history = []
                return
            self._history = [
                WallpaperRecord.from_dict(item)
                for item in data.get('history', [])
            ]
            
        except Exception as e:
            logger.error(f"加载壁纸历史记录失败：{e}")
            self._history = []
        
        self.clear_invalid()
    
    def sync_cleanup(self, max_files: int = None):
        if max_files is None:
            max_files = cfg.wallpaperSaveLimit.value
        if not os.path.exists(self.wallpaper_dir):return
        wallpapers = []
        for file in os.listdir(self.wallpaper_dir):
            if file.endswith('.jpg') and file.startswith('wallpaper_'):
                file_path = os.path.join(self.wallpaper_dir, file)
                mtime = os.path.getmtime(file_path)
                wallpapers.append((mtime, file_path))
        
        wallpapers.sort(key=lambda x: x[0])
        deleted_count = 0
        while len(wallpapers) > max_files:
            _, old_file_path = wallpapers.pop(0)
            try:
                os.remove(old_file_path)
                record_id = os.path.splitext(os.path.basename(old_file_path))[0]
                self.remove(record_id)
                deleted_count += 1
            except Exception as e:
                logger.warning(f"删除壁纸失败 {old_file_path}: {e}")
        if deleted_count > 0:logger.info(f"共删除 {deleted_count} 个壁纸")
        
        return deleted_count
    
    def _save(self):
        if not os.path.exists(self.wallpaper_dir):
            os.makedirs(self.wallpaper_dir)
        
        try:
            data = {
                'version': HISTORY_VERSION,
                'history': [record.to_dict() for record in self._history]
            }
            with open(self.history_file, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        except Exception as e:
            logger.error(f"保存壁纸历史记录失败：{e}")
    
    def add(self, path: str, source: str, api_url: str) -> WallpaperRecord:
        if not os.path.exists(path):return None
        record_id = os.path.splitext(os.path.basename(path))[0]
        existing = self.get_by_id(record_id)
        if existing:
            self._history.remove(existing)
            self._history.insert(0, existing)
            self._save()
            return existing
        file_size = os.path.getsize(path)
        resolution = tr("common.unknown")
        try:
            reader = QImageReader(path)
            size = reader.size()
            if size.isValid(): resolution = f"{size.width()}x{size.height()}"
        except Exception:
            pass
        record = WallpaperRecord(
            id=record_id,
            path=path,
            source=source,
            api_url=api_url,
            added_time=datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
            file_size=file_size,
            resolution=resolution
        )
        
        self._history.insert(0, record)
        while len(self._history) > MAX_HISTORY_RECORDS:
            self._history.pop()
        self._save()
        return record
    
    def remove(self, record_id: str) -> bool:
        record = self.get_by_id(record_id)
        if record:
            self._history.remove(record)
            self._save()
            return True
        return False
    
    def get_by_id(self, record_id: str) -> Optional[WallpaperRecord]:
        for record in self._history:
            if record.id == record_id:return record
        return None
    
    def clear_invalid(self):
        invalid = [r for r in self._history if not r.exists()]
        for record in invalid:self._history.remove(record)
        if invalid:self._save()
        return len(invalid)


def get_wallpaper_history() -> WallpaperHistory:
    return WallpaperHistory()


class WallpaperInfoCard(CardWidget):
    """壁纸信息卡片"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("wallpaperInfoCard")
        self._setupUi()
    
    def _setupUi(self):
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(16, 12, 16, 12)
        layout.setSpacing(20)
        self.resolutionLabel = BodyLabel(tr("wallpaper.resolution") + ": --", self)
        self.sizeLabel = BodyLabel(tr("wallpaper.size") + ": --", self)
        self.sourceLabel = BodyLabel(tr("wallpaper.source") + ": --", self)
        self.pathLabel = BodyLabel(tr("wallpaper.path") + ": --", self)
        layout.addWidget(self.resolutionLabel)
        layout.addWidget(self.sizeLabel)
        layout.addWidget(self.sourceLabel)
        layout.addWidget(self.pathLabel, 1)
    
    def updateInfo(self, path: str = None, source: str = None):
        if not path or not os.path.exists(path):
            self.resolutionLabel.setText(tr("wallpaper.resolution") + ": --")
            self.sizeLabel.setText(tr("wallpaper.size") + ": --")
            self.sourceLabel.setText(tr("wallpaper.source") + ": --")
            self.pathLabel.setText(tr("wallpaper.path") + ": --")
            return

        file_size = os.path.getsize(path)
        if file_size < 1024:
            size_str = f"{file_size} B"
        elif file_size < 1024 * 1024:
            size_str = f"{file_size / 1024:.1f} KB"
        else:
            size_str = f"{file_size / 1024 / 1024:.1f} MB"

        resolution = tr("wallpaper.unknown")
        try:
            reader = QImageReader(path)
            size = reader.size()
            if size.isValid(): resolution = f"{size.width()}x{size.height()}"
        except Exception:
            pass

        self.resolutionLabel.setText(f"{tr('wallpaper.resolution')}: {resolution}")
        self.sizeLabel.setText(f"{tr('wallpaper.size')}: {size_str}")
        self.sourceLabel.setText(f"{tr('wallpaper.source')}: {source or tr('wallpaper.source_local')}")

        display_path = path
        if len(path) > 50:
            display_path = "..." + path[-47:]
        self.pathLabel.setText(f"{tr('wallpaper.path')}: {display_path}")


class _ShrinkableWidget(QWidget):
    def minimumSizeHint(self):
        hint = super().minimumSizeHint()
        return QSize(0, hint.height())


class WallpaperInterface(ScrollArea, TranslatableWidget):
    """壁纸界面"""

    def __init__(self, mainWindow=None, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("wallpaper")
        self.mainWindow = mainWindow
        self.historyManager = get_wallpaper_history()
        
        self.current_pixmap = None
        self.current_wallpaper_path = None
        self.current_wallpaper_source = None
        self.last_sync_path = None
        
        self.autoGetTimer = QTimer(self)
        self.autoGetTimer.timeout.connect(self._getWallpaper)
        self.autoSyncCheckTimer = QTimer(self)
        self.autoSyncCheckTimer.timeout.connect(self._checkAutoSync)

        self.wallpaperLabel = SubtitleLabel(tr("navigation.wallpaper"), self)

        self.contentWidget = QWidget()
        self.contentWidget.setObjectName("wallpaperContent")
        self.contentLayout = QVBoxLayout(self.contentWidget)
        self.contentLayout.setContentsMargins(60, 0, 60, 40)
        self.contentLayout.setSpacing(16)

        self.infoCard = WallpaperInfoCard(self.contentWidget)

        self.getButton = PrimaryPushButton(FUI.DOWNLOAD, tr("wallpaper.get_wallpaper"))
        self.getButton.setFixedHeight(36)
        self.saveButton = PushButton(FUI.SAVE, tr("wallpaper.save_as"))
        self.saveButton.setFixedHeight(36)
        self.selectButton = PushButton(FUI.FOLDER, tr("wallpaper.manual_select"))
        self.selectButton.setFixedHeight(36)
        self.setWallpaperButton = PushButton(FUI.HOME, tr("wallpaper.set_desktop"))
        self.setWallpaperButton.setFixedHeight(36)

        self.settingsGroup = SettingCardGroup(tr("wallpaper.settings"), self.contentWidget)
        self.wallpaperSaveLimitCard = RangeSettingCard(
            cfg.wallpaperSaveLimit,
            FUI.SAVE,
            tr("wallpaper.save_limit"),
            tr("wallpaper.save_limit_desc"),
            parent=self.settingsGroup
        )
        self.settingsGroup.addSettingCard(self.wallpaperSaveLimitCard)
        self.autoGetIntervalCard = ComboBoxSettingCard(
            cfg.autoGetInterval,
            FUI.SYNC,
            tr("wallpaper.auto_interval"),
            tr("wallpaper.auto_interval_desc"),
            texts=[tr("time.never"), tr("time.minutes_10"), tr("time.minutes_30"), tr("time.hour_1"), tr("wallpaper.interval_3h"), tr("time.hours_6"), tr("time.hours_12"), tr("time.day_1"), tr("wallpaper.interval_3d"), tr("wallpaper.interval_5d"), tr("wallpaper.interval_7d")],
            parent=self.settingsGroup
        )
        self.settingsGroup.addSettingCard(self.autoGetIntervalCard)
        self.wallpaperApiCard = ComboBoxSettingCard(
            cfg.wallpaperApi,
            FUI.LINK,
            tr("wallpaper.api"),
            tr("wallpaper.api_desc"),
            texts=["wp.upx8.com", "api.ltyuanfang.cn", "imlcd.cn_bg_high", "imlcd.cn_bg_mc", "imlcd.cn_bg_gq"],
            parent=self.settingsGroup
        )
        self.settingsGroup.addSettingCard(self.wallpaperApiCard)
        self.autoSyncToDesktopCard = SwitchSettingCard(
            FUI.HOME,
            tr("wallpaper.auto_sync"),
            tr("wallpaper.auto_sync_desc"),
            configItem=cfg.autoSyncToDesktop,
            parent=self.settingsGroup
        )
        self.settingsGroup.addSettingCard(self.autoSyncToDesktopCard)

        self.effectsGroup = SettingCardGroup(tr("wallpaper.background_effects"), self.contentWidget)
        self.blurCard = RangeSettingCard(
            cfg.backgroundBlurRadius,
            FUI.BRUSH,
            tr("wallpaper.blur"),
            tr("wallpaper.blur_desc"),
            parent=self.effectsGroup
        )
        self.effectsGroup.addSettingCard(self.blurCard)
        self.brightnessCard = RangeSettingCard(
            cfg.wallpaperBrightness,
            FUI.BRUSH,
            tr("wallpaper.brightness"),
            tr("wallpaper.brightness_desc"),
            parent=self.effectsGroup
        )
        self.effectsGroup.addSettingCard(self.brightnessCard)
        
        self._initWidget()
        self._connectSignalToSlot()
        self.setup_translatable_ui()

    def _getHome(self):
        if hasattr(self.mainWindow, 'homeInterface'):
            return self.mainWindow.homeInterface
        return None

    def _onThemeChanged(self, theme: Theme):
        self._setQss()
    
    def _initWidget(self):
        self.resize(1000, 800)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setViewportMargins(0, 120, 0, 20)
        self.setWidgetResizable(True)
        
        self._initLayout()
        self._setQss()
        
        QTimer.singleShot(0, self.__deferredInit)
    
    def __deferredInit(self):
        self.historyManager.sync_cleanup()
        self._loadDefaultWallpaper()
    
    def _initLayout(self):
        self.wallpaperLabel.setObjectName('settingLabel')
        self.wallpaperLabel.move(60, 63)

        actionRow = QHBoxLayout()
        actionRow.setSpacing(12)
        actionRow.addWidget(self.getButton)
        actionRow.addWidget(self.saveButton)
        actionRow.addWidget(self.selectButton)
        actionRow.addStretch(1)
        actionRow.addWidget(self.setWallpaperButton)

        self.contentLayout.addLayout(actionRow)
        self.contentLayout.addWidget(self.infoCard)
        self.contentLayout.addSpacing(24)
        self.contentLayout.addWidget(self.settingsGroup)
        self.contentLayout.addWidget(self.effectsGroup)
        self.contentLayout.addStretch(1)
        
        self.scrollWidget = _ShrinkableWidget()
        self.scrollWidget.setObjectName('scrollWidget')
        self.scrollWidget.setLayout(self.contentLayout)
        self.setWidget(self.scrollWidget)
        self.scrollWidget.installEventFilter(self)
    
    def _setQss(self):
        self.setStyleSheet(load_qss('wallpaper.qss'))

    def _connectSignalToSlot(self):
        self.getButton.clicked.connect(self._getWallpaper)
        self.saveButton.clicked.connect(self._saveWallpaper)
        self.selectButton.clicked.connect(self._selectWallpaper)
        self.setWallpaperButton.clicked.connect(self._setWallpaper)

        cfg.autoGetInterval.valueChanged.connect(self._updateAutoGetTimer)
        cfg.autoSyncToDesktop.valueChanged.connect(self._updateAutoSyncCheckTimer)
        cfg.wallpaperSaveLimit.valueChanged.connect(self.historyManager.sync_cleanup)
        cfg.wallpaperBrightness.valueChanged.connect(self._applyEffects)

        self._updateAutoGetTimer()
        self._updateAutoSyncCheckTimer()

    def hideEvent(self, event):
        self.autoGetTimer.stop()
        self.autoSyncCheckTimer.stop()
        super().hideEvent(event)

    def showEvent(self, event):
        super().showEvent(event)
        self._updateAutoGetTimer()
        self._updateAutoSyncCheckTimer()

    def _updateAutoGetTimer(self):
        self.autoGetTimer.stop()
        interval_str = cfg.autoGetInterval.value

        if interval_str != "never":
            interval = INTERVAL_MAP.get(interval_str.strip(), 30 * 60) * 1000
            self.autoGetTimer.start(interval)
    
    def _checkAutoSync(self):
        if cfg.autoSyncToDesktop.value and self.current_wallpaper_path is not None:
            if self.last_sync_path != self.current_wallpaper_path:
                self._setWallpaper(show_notification=False)
                self.last_sync_path = self.current_wallpaper_path
    
    def _updateAutoSyncCheckTimer(self):
        self.autoSyncCheckTimer.stop()
        if cfg.autoSyncToDesktop.value:
            self.autoSyncCheckTimer.start(5000)
    
    def _applyEffects(self):
        """暗化（-100最暗 ~ 0正常）"""
        dim_value = cfg.wallpaperBrightness.value
        alpha = abs(dim_value) / 100.0
        style_str = f"#dimOverlay {{ background-color: rgba(0, 0, 0, {alpha:.2f}); }}"
        home = self._getHome()
        if home and hasattr(home, 'homeDimOverlay'):home.homeDimOverlay.setStyleSheet(style_str)
    
    def _loadWallpaperFromCache(self) -> bool:
        cached = get_cached_content("wallpaper", ignore_expiry=True)
        if not cached:
            logger.debug("壁纸缓存不存在")
            return False

        wallpaper_path = cached.get("path", "")
        if not wallpaper_path:
            logger.warning("壁纸缓存数据异常：缺少路径信息")
            return False

        if not os.path.exists(wallpaper_path):
            logger.warning(f"缓存壁纸文件不存在: {wallpaper_path}")
            return False

        logger.info(f"使用缓存壁纸: {wallpaper_path}")
        source = cached.get("source", tr("wallpaper.source_cache"))
        if not self._applyCurrentWallpaper(wallpaper_path, source):
            logger.error(f"无法加载缓存壁纸图片: {wallpaper_path}")
            return False
        return True
    
    def _getApiUrl(self) -> tuple:
        wallpaper_api = cfg.wallpaperApi.value
        api_map = {
            "api.ltyuanfang.cn": ("https://tu.ltyuanfang.cn/api/fengjing.php", "api.ltyuanfang.cn"),
            "imlcd.cn_bg_high": ("https://api.imlcd.cn/bg/high.php", "imlcd.cn_bg_high"),
            "imlcd.cn_bg_mc": ("https://api.imlcd.cn/bg/mc.php", "imlcd.cn_bg_mc"),
            "imlcd.cn_bg_gq": ("https://api.imlcd.cn/bg/gq.php", "imlcd.cn_bg_gq"),
        }
        return api_map.get(wallpaper_api, ("https://wp.upx8.com/api.php?content=风景", "wp.upx8.com"))
    
    def _getWallpaper(self):
        logger.info("开始获取壁纸")
        
        success = False
        
        try:
            url, source = self._getApiUrl()
            logger.info(f"请求壁纸 URL: {url}")
            response = requests.get(url, stream=True, timeout=10)
            
            if response.status_code == 200:
                logger.info(f"壁纸请求成功，状态码: {response.status_code}")
                wallpaper_dir = WALLPAPER_DIR
                if not os.path.exists(wallpaper_dir):
                    os.makedirs(wallpaper_dir)
                    logger.info(f"创建壁纸目录: {wallpaper_dir}")
                
                current_date = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
                wallpaper_path = os.path.join(wallpaper_dir, f'wallpaper_{current_date}.jpg')
                
                with open(wallpaper_path, 'wb') as f:
                    f.write(response.content)
                logger.info(f"壁纸已保存到: {wallpaper_path}")
                
                # 解码失败(返回 HTML/损坏数据)不算成功,删掉坏文件
                if self._applyCurrentWallpaper(wallpaper_path, source):
                    self.historyManager.sync_cleanup(cfg.wallpaperSaveLimit.value)
                    self.historyManager.add(wallpaper_path, source, url)
                    save_cache("wallpaper", {"path": wallpaper_path, "source": source, "url": url},
                               cfg.autoGetInterval.value)
                    InfoBar.success(tr("wizard.success_title"), tr("wallpaper.downloaded"), duration=3000, parent=self)
                    success = True
                    if cfg.autoSyncToDesktop.value:
                        self._setWallpaper(show_notification=True)
                else:
                    logger.error(f"壁纸图片无效: {wallpaper_path}")
                    try:
                        os.remove(wallpaper_path)
                    except OSError:
                        pass
                    InfoBar.error(tr("dialog.error"), tr("wallpaper.load_image_failed"), duration=5000, parent=self)
            else:
                logger.error(f"获取壁纸失败，状态码: {response.status_code}")
                InfoBar.error(tr("dialog.error"), tr("wallpaper.fetch_failed_http").format(status=response.status_code), duration=5000, parent=self)
                
        except Exception as e:
            logger.error(f"获取壁纸失败：{str(e)}")
            InfoBar.error(tr("dialog.error"), tr("wallpaper.fetch_failed").format(error=str(e)), duration=5000, parent=self)
        
        # 已有壁纸就保持现状,失败不覆盖用户当前选择
        if not success and self.current_wallpaper_path is None:
            self._loadDefaultWallpaper()
    
    def _loadDefaultWallpaper(self):
        logger.info("加载默认壁纸")
        if self._loadWallpaperFromCache():return
        default_wallpaper_path = get_resPath(RESOURCE_DEFAULT_WALLPAPER)

        if not os.path.exists(default_wallpaper_path):
            if os.path.exists(WALLPAPER_DIR):
                wallpapers = [f for f in os.listdir(WALLPAPER_DIR) if f.endswith('.jpg') and f.startswith('wallpaper_')]
                if wallpapers:
                    wallpapers.sort(key=lambda x: os.path.getmtime(os.path.join(WALLPAPER_DIR, x)), reverse=True)
                    default_wallpaper_path = os.path.join(WALLPAPER_DIR, wallpapers[0])
        
        if os.path.exists(default_wallpaper_path):
            self._applyCurrentWallpaper(default_wallpaper_path, tr("wallpaper.default_source"))
        else:
            self.infoCard.updateInfo()
    
    def _applyCurrentWallpaper(self, path: str, source: str) -> bool:
        """载入图片并刷新背景与信息卡,解码失败返回 False"""
        pixmap = QPixmap(path)
        if pixmap.isNull():
            # 校验失败不动任何 current_*,避免坏路径被轮询拿去设桌面
            return False
        self.current_pixmap = pixmap
        self.current_wallpaper_path = path
        self.current_wallpaper_source = source
        self._updateMainWindowBackground()
        self._applyEffects()
        self.infoCard.updateInfo(path, source)
        return True

    def _updateMainWindowBackground(self):
        home = self._getHome()
        if home and self.current_pixmap:
            home.originalPixmap = self.current_pixmap
            home._computeBlurredBackground()

    def _saveWallpaper(self):
        logger.info("开始另存壁纸")
        if self.current_pixmap is None or self.current_pixmap.isNull():
            InfoBar.warning(tr("common.tip"), tr("wallpaper.fetch_first"), duration=3000, parent=self)
            return
        
        file_path, _ = QFileDialog.getSaveFileName(
            self, tr("wallpaper.save_title"),
            WALLPAPER_DIR,
            f"{tr('wallpaper.jpeg_files')} (*.jpg);;{tr('wallpaper.png_files')} (*.png)"
        )
        
        if not file_path:
            return
        try:
            saved = self.current_pixmap.save(file_path)
        except Exception as e:
            saved = False
            logger.error(f"保存壁纸失败: {str(e)}")
        if saved:
            logger.info(f"壁纸已保存到: {file_path}")
            InfoBar.success(tr("wizard.success_title"), tr("wallpaper.saved"), duration=3000, parent=self)
        else:
            logger.error(f"保存壁纸失败: {file_path}")
            InfoBar.error(tr("dialog.error"), tr("wallpaper.save_failed").format(error=file_path), duration=5000, parent=self)
    
    def _selectWallpaper(self):
        logger.info("开始手动选择壁纸")
        file_path, _ = QFileDialog.getOpenFileName(
            self, tr("wallpaper.select_title"),
            WALLPAPER_DIR,
            tr("wallpaper.image_filter")
        )
        
        if file_path:
            self._useWallpaper(file_path, tr("wallpaper.source_local"))
    
    def _useWallpaper(self, path: str, source: str):
        if not os.path.exists(path):
            InfoBar.error(tr("dialog.error"), tr("wallpaper.file_not_exist"), duration=3000, parent=self)
            return
        
        try:
            if self._applyCurrentWallpaper(path, source):
                self.historyManager.add(path, source, "")
                InfoBar.success(tr("wizard.success_title"), tr("wallpaper.applied"), duration=2000, parent=self)
                logger.info(f"已应用壁纸: {path}")
        except Exception as e:
            logger.error(f"应用壁纸失败: {str(e)}")
            InfoBar.error(tr("dialog.error"), tr("wallpaper.apply_failed").format(error=str(e)), duration=5000, parent=self)

    def _setWallpaper(self, show_notification=True):
        if self.current_wallpaper_path is None:
            if show_notification:
                InfoBar.warning(tr("common.tip"), tr("wallpaper.fetch_or_select_first"), duration=3000, parent=self)
            return
        
        logger.info(f"设置壁纸路径: {self.current_wallpaper_path}")
        try:
            from Glimpseon_native import set_wallpaper
            set_wallpaper(self.current_wallpaper_path)
            
            self.last_sync_path = self.current_wallpaper_path
            logger.info("壁纸已同步桌面")
            
            if show_notification:
                InfoBar.success(tr("wizard.success_title"), tr("wallpaper.set_as_desktop"), duration=3000, parent=self)
                
        except Exception as e:
            logger.error(f"设置壁纸失败: {str(e)}")
            if show_notification:
                InfoBar.error(tr("wallpaper.error"), f"{tr('wallpaper.set_failed')}: {str(e)}", duration=5000, parent=self)


