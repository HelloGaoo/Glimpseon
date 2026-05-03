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
壁纸界面模块
"""

import ctypes
import datetime
import json
import logging
import os
from dataclasses import dataclass, asdict
from typing import List, Optional

import requests
from PyQt6.QtCore import Qt, pyqtSignal, QTimer, QSize
from PyQt6.QtGui import QPixmap, QImageReader, QColor
from PyQt6.QtWidgets import (
    QApplication,
    QFileDialog,
    QGridLayout,
    QGraphicsBlurEffect,
    QHBoxLayout,
    QLabel,
    QScrollArea,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)
from qfluentwidgets import (
    BodyLabel,
    CardWidget,
    ComboBoxSettingCard,
    FluentIcon as FIF,
    InfoBar,
    MessageBox,
    MaskDialogBase,
    MessageBoxBase,
    PrimaryPushButton,
    PushButton,
    RangeSettingCard,
    ScrollArea,
    SettingCard,
    SettingCardGroup,
    SmoothScrollArea,
    StrongBodyLabel,
    SubtitleLabel,
    SwitchSettingCard,
    Theme,
)

from core.config import cfg
from core.constants import BASE_DIR, get_resPath, load_qss
from core.cache_manager import get_cached_content, save_cache, get_cache_info

logger = logging.getLogger(__name__)

HISTORY_FILE_NAME = "history.json"
HISTORY_VERSION = 1
MAX_HISTORY_RECORDS = 100

INITIAL_MIN_ROWS = 2
INITIAL_MAX_ROWS = 4
LOAD_MORE_ROWS = 2
CARD_WIDTH = 160
CARD_HEIGHT = 130
CARD_SPACING = 10
GRID_MARGIN_H = 10

_thumbnail_cache = {}
_cache_max_size = 200

def get_cached_thumbnail(path: str, size: tuple = (144, 90)) -> Optional[QPixmap]:
    cache_key = (path, size)
    if cache_key in _thumbnail_cache:return _thumbnail_cache[cache_key]
    if not os.path.exists(path):return None
    try:
        pixmap = QPixmap(path)
        if pixmap.isNull():return None
        scaled = pixmap.scaled(size[0], size[1], Qt.AspectRatioMode.KeepAspectRatioByExpanding, Qt.TransformationMode.FastTransformation)
        if len(_thumbnail_cache) >= _cache_max_size:
            old_keys = list(_thumbnail_cache.keys())[:50]
            for k in old_keys:del _thumbnail_cache[k]

        _thumbnail_cache[cache_key] = scaled
        return scaled
    except Exception as e:
        logger.warning(f"加载缩略图失败：{e}")
        return None

def clear_thumbnail_cache():
    global _thumbnail_cache
    _thumbnail_cache.clear()


@dataclass
class WallpaperRecord:
    """记录类"""
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
    """历史管理"""
    
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
        
        self.wallpaper_dir = os.path.join(BASE_DIR, 'wallpaper')
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
            from core.config import cfg
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
        resolution = "未知"
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
    
    def delete_file(self, record_id: str) -> bool:
        record = self.get_by_id(record_id)
        if not record:return False
        
        if os.path.exists(record.path):
            try:
                os.remove(record.path)
            except Exception:
                return False
        
        return self.remove(record_id)
    
    def get_by_id(self, record_id: str) -> Optional[WallpaperRecord]:
        for record in self._history:
            if record.id == record_id:return record
        return None
    
    def get_all(self) -> List[WallpaperRecord]:
        return self._history.copy()
    
    def get_valid(self) -> List[WallpaperRecord]:
        return [r for r in self._history if r.exists()]
    
    def clear_invalid(self):
        invalid = [r for r in self._history if not r.exists()]
        for record in invalid:self._history.remove(record)
        if invalid:self._save()
        return len(invalid)
    
    def clear_all(self):
        self._history.clear()
        self._save()
    
    def count(self) -> int:
        return len(self._history)


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
        self.resolutionLabel = BodyLabel("分辨率：--", self)
        self.sizeLabel = BodyLabel("大小：--", self)
        self.sourceLabel = BodyLabel("来源：--", self)
        self.pathLabel = BodyLabel("路径：--", self)
        layout.addWidget(self.resolutionLabel)
        layout.addWidget(self.sizeLabel)
        layout.addWidget(self.sourceLabel)
        layout.addWidget(self.pathLabel, 1)
    
    def updateInfo(self, path: str = None, source: str = None):
        if not path or not os.path.exists(path):
            self.resolutionLabel.setText("分辨率：--")
            self.sizeLabel.setText("大小：--")
            self.sourceLabel.setText("来源：--")
            self.pathLabel.setText("路径：--")
            return
        
        file_size = os.path.getsize(path)
        if file_size < 1024:
            size_str = f"{file_size} B"
        elif file_size < 1024 * 1024:
            size_str = f"{file_size / 1024:.1f} KB"
        else:
            size_str = f"{file_size / 1024 / 1024:.1f} MB"
        
        resolution = "未知"
        try:
            reader = QImageReader(path)
            size = reader.size()
            if size.isValid(): resolution = f"{size.width()}x{size.height()}"
        except Exception:
            pass
        
        self.resolutionLabel.setText(f"分辨率：{resolution}")
        self.sizeLabel.setText(f"大小：{size_str}")
        self.sourceLabel.setText(f"来源：{source or '本地'}")
        
        display_path = path
        if len(path) > 50:
            display_path = "..." + path[-47:]
        self.pathLabel.setText(f"路径：{display_path}")


class WallpaperPreviewDialog(MessageBoxBase):
    """壁纸预览弹窗"""
    useRequested = pyqtSignal(str)
    deleteRequested = pyqtSignal(str)
    
    def __init__(self, record: WallpaperRecord, parent=None):
        super().__init__(parent)
        self.record = record
        self._setupUi()
    
    def _setupUi(self):
        self.titleLabel = SubtitleLabel(os.path.splitext(self.record.id)[0], self)
        
        self.imageCard = CardWidget(self)
        imageLayout = QVBoxLayout(self.imageCard)
        imageLayout.setContentsMargins(0, 0, 0, 0)
        self.imageLabel = QLabel()
        self.imageLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.imageLabel.setScaledContents(True)
        
        max_w, max_h = 640, 400
        if os.path.exists(self.record.path):
            pixmap = QPixmap(self.record.path)
            if not pixmap.isNull():
                scaled = pixmap.scaled(max_w, max_h, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
                self.imageLabel.setPixmap(scaled)
                self.imageLabel.setFixedSize(scaled.size())
            else:
                self.imageLabel.setText("无法加载图片")
                self.imageLabel.setFixedSize(max_w, max_h)
        else:
            self.imageLabel.setText("文件不存在")
            self.imageLabel.setFixedSize(max_w, max_h)
        
        imageLayout.addWidget(self.imageLabel, 0, Qt.AlignmentFlag.AlignCenter)
        
        infoText = f"分辨率：{self.record.resolution}  |  大小：{self._format_size(self.record.file_size)}  |  来源：{self.record.source}  |  时间：{self.record.added_time}"
        self.infoLabel = BodyLabel(infoText, self)
        self.infoLabel.setWordWrap(True)
        
        self.useButton = PrimaryPushButton(FIF.ACCEPT, "使用此壁纸", self)
        self.useButton.setFixedWidth(120)
        self.useButton.setFixedHeight(36)
        self.useButton.clicked.connect(self._onUse)

        self.deleteButton = PushButton(FIF.DELETE, "删除", self)
        self.deleteButton.setFixedWidth(100)
        self.deleteButton.setFixedHeight(36)
        self.deleteButton.clicked.connect(self._onDelete)
        
        btnLayout = QHBoxLayout()
        btnLayout.addStretch(1)
        btnLayout.addWidget(self.useButton)
        btnLayout.addSpacing(10)
        btnLayout.addWidget(self.deleteButton)
        btnLayout.addStretch(1)
        
        self.viewLayout.addWidget(self.titleLabel)
        self.viewLayout.addSpacing(12)
        self.viewLayout.addWidget(self.imageCard)
        self.viewLayout.addSpacing(10)
        self.viewLayout.addWidget(self.infoLabel)
        self.viewLayout.addSpacing(16)
        self.viewLayout.addLayout(btnLayout)
        
        self.yesButton.setText("关闭")
        self.cancelButton.hide()
        self.widget.setMinimumWidth(700)
        self.widget.setMinimumHeight(560)
    
    def _format_size(self, size: int) -> str:
        if size < 1024:
            return f"{size} B"
        elif size < 1024 * 1024:
            return f"{size / 1024:.1f} KB"
        else:
            return f"{size / 1024 / 1024:.1f} MB"
    
    def _onUse(self):
        if os.path.exists(self.record.path):
            self.useRequested.emit(self.record.path)
            self.accept()
    
    def _onDelete(self):
        self.deleteRequested.emit(self.record.id)
        self.reject()


class WallpaperThumbnailCard(CardWidget):
    """壁纸缩略图卡片"""
    
    clicked = pyqtSignal(WallpaperRecord)
    
    def __init__(self, record: WallpaperRecord, parent=None):
        super().__init__(parent)
        self.setObjectName("thumbnailCard")
        self.record = record
        self._setupUi()
    
    def _setupUi(self):
        self.setFixedSize(CARD_WIDTH, CARD_HEIGHT)
        self.setCursor(Qt.CursorShape.PointingHandCursor)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)

        self.imageLabel = QLabel()
        self.imageLabel.setObjectName("thumbImage")
        self.imageLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        cached_pixmap = get_cached_thumbnail(self.record.path)
        if cached_pixmap:
            self.imageLabel.setPixmap(cached_pixmap)
        elif os.path.exists(self.record.path):
            self._showPlaceholder("加载失败")
        else:
            self._showPlaceholder("文件不存在")

        self.infoLabel = BodyLabel(self)
        self.infoLabel.setObjectName("thumbInfo")
        self.infoLabel.setText(self.record.resolution)
        self.infoLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        layout.addWidget(self.imageLabel)
        layout.addWidget(self.infoLabel)
    
    def _showPlaceholder(self, text: str):
        self.imageLabel.setText(text)
    
    def mouseReleaseEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self.clicked.emit(self.record)


class WallpaperHistoryWidget(QWidget):
    """壁纸历史列表组件"""
    
    wallpaperSelected = pyqtSignal(str)
    historyChanged = pyqtSignal()
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("historyWidget")
        self.historyManager = get_wallpaper_history()
        self._allRecords: list[WallpaperRecord] = []
        self._displayedCount = 0
        self._cards: list[WallpaperThumbnailCard] = []
        self._currentColumns = 4
        self._setupUi()
        self._loadHistory()
    
    def _setupUi(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(10)
        
        headerLayout = QHBoxLayout()
        headerLayout.setContentsMargins(0, 0, 0, 0)
        
        self.titleLabel = StrongBodyLabel("历史壁纸", self)
        self.titleLabel.setObjectName("historyTitle")
        
        self.countLabel = BodyLabel("", self)
        self.countLabel.setObjectName("historyCount")
        
        self.clearInvalidBtn = PushButton(FIF.DELETE, "清空全部", self)
        self.clearInvalidBtn.setFixedHeight(36)
        self.clearInvalidBtn.setMinimumWidth(100)
        self.clearInvalidBtn.clicked.connect(self._clearAll)
        
        headerLayout.addWidget(self.titleLabel)
        headerLayout.addSpacing(10)
        headerLayout.addWidget(self.countLabel)
        headerLayout.addStretch(1)
        headerLayout.addWidget(self.clearInvalidBtn)
        
        self.gridContainer = QWidget()
        self.gridLayout = QGridLayout(self.gridContainer)
        self.gridLayout.setContentsMargins(GRID_MARGIN_H, 5, GRID_MARGIN_H, 5)
        self.gridLayout.setSpacing(CARD_SPACING)
        self.gridLayout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)
        
        self.loadMoreWidget = QWidget()
        loadMoreLayout = QHBoxLayout(self.loadMoreWidget)
        loadMoreLayout.setContentsMargins(0, 8, 0, 20)
        loadMoreLayout.addStretch(1)
        self.loadMoreBtn = PushButton(FIF.SYNC, "加载更多", self)
        self.loadMoreBtn.setObjectName("loadMoreBtn")
        self.loadMoreBtn.setFixedHeight(36)
        self.loadMoreBtn.setMinimumWidth(140)
        self.loadMoreBtn.clicked.connect(self._loadMore)
        loadMoreLayout.addWidget(self.loadMoreBtn)
        loadMoreLayout.addStretch(1)
        self.loadMoreWidget.hide()
        
        self.noMoreLabel = BodyLabel("已显示全部历史壁纸", self)
        self.noMoreLabel.setObjectName("emptyLabel")
        self.noMoreLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.noMoreLabel.hide()
        
        self.emptyLabel = BodyLabel("暂无历史壁纸记录", self)
        self.emptyLabel.setObjectName("emptyLabel")
        self.emptyLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.emptyLabel.hide()
        
        layout.addLayout(headerLayout)
        layout.addWidget(self.gridContainer)
        layout.addWidget(self.loadMoreWidget)
        layout.addWidget(self.noMoreLabel)
        layout.addWidget(self.emptyLabel)
    
    def _calcColumns(self) -> int:
        w = self.gridContainer.width()
        if w <= 0:
            p = self.parentWidget()
            if p:
                w = p.width()
        available = w - 2 * GRID_MARGIN_H
        if available <= 0:
            return 4
        cols = int((available + CARD_SPACING) / (CARD_WIDTH + CARD_SPACING))
        return max(1, cols)
    
    def _calcInitialCount(self) -> int:
        cols = self._calcColumns()
        rows = min(INITIAL_MAX_ROWS, max(INITIAL_MIN_ROWS, self.gridContainer.height() // (CARD_HEIGHT + CARD_SPACING) if self.gridContainer.height() > 0 else INITIAL_MIN_ROWS))
        return cols * rows
    
    def _calcLoadMoreCount(self) -> int:
        return self._calcColumns() * LOAD_MORE_ROWS
    
    def _rebuildGrid(self):
        columns = self._calcColumns()
        self._currentColumns = columns

        self.setUpdatesEnabled(False)

        while self.gridLayout.count():
            item = self.gridLayout.takeAt(0)
            if item.widget():
                item.widget().setParent(None)

        for col in range(columns):
            self.gridLayout.setColumnStretch(col, 0)

        for i, card in enumerate(self._cards):
            row = i // columns
            col = i % columns
            card.setParent(self.gridContainer)
            self.gridLayout.addWidget(card, row, col)

        self.setUpdatesEnabled(True)
    
    def _loadHistory(self):
        records = self.historyManager.get_valid()
        self._allRecords = records
        self._displayedCount = 0
        self._cards.clear()
        
        total = len(self._allRecords)
        self.countLabel.setText(f"({total})")
        
        if not total:
            self.gridContainer.hide()
            self.emptyLabel.show()
            self.loadMoreWidget.hide()
            self.noMoreLabel.hide()
            return
        
        self.gridContainer.show()
        self.emptyLabel.hide()
        
        self._displayPage(self._calcInitialCount())
    
    def _displayPage(self, count: int):
        start = self._displayedCount
        end = min(start + count, len(self._allRecords))

        self.setUpdatesEnabled(False)

        for i in range(start, end):
            record = self._allRecords[i]
            card = WallpaperThumbnailCard(record, self)
            card.clicked.connect(self._showPreview)
            self._cards.append(card)

        self._displayedCount = end
        self._rebuildGrid()

        self.setUpdatesEnabled(True)

        if self._displayedCount >= len(self._allRecords):
            self.loadMoreWidget.hide()
            if len(self._allRecords) > self._calcInitialCount():
                self.noMoreLabel.show()
            else:
                self.noMoreLabel.hide()
        else:
            remaining = len(self._allRecords) - self._displayedCount
            self.loadMoreBtn.setText(f"加载更多 ({remaining})")
            self.loadMoreWidget.show()
            self.noMoreLabel.hide()
    
    def _loadMore(self):
        self._displayPage(self._calcLoadMoreCount())
    
    def _showPreview(self, record: WallpaperRecord):
        mw = self.window()
        mask = QWidget(mw)
        mask.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        mask.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        mask.setGeometry(0, 0, mw.width(), mw.height())
        mask.setStyleSheet("background-color: rgba(0, 0, 0, 120);")
        mask.show()
        
        dialog = WallpaperPreviewDialog(record, mask)
        dialog.useRequested.connect(self._onUseWallpaper)
        dialog.deleteRequested.connect(self._onDeleteWallpaper)
        dialog.exec()
        mask.close()
        mask.deleteLater()
    
    def _onUseWallpaper(self, path: str):
        self.wallpaperSelected.emit(path)
    
    def _onDeleteWallpaper(self, record_id: str):
        self.historyManager.delete_file(record_id)
        self._loadHistory()
        self.historyChanged.emit()
    
    def _clearAll(self):
        count = self.historyManager.count()
        if count == 0:return
        mw = self.window()
        
        mask = QWidget(mw)
        mask.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        mask.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        mask.setGeometry(0, 0, mw.width(), mw.height())
        mask.setStyleSheet("background-color: rgba(0, 0, 0, 120);")
        mask.show()
        
        w = MessageBox("确认清空", f"确定要清空全部 {count} 条壁纸历史记录吗？", mask)
        w.yesButton.setText('确认清空')
        w.cancelButton.setText('取消')
        if w.exec():
            self.historyManager.clear_all()
            self._loadHistory()
            self.historyChanged.emit()
        mask.close()
        mask.deleteLater()
    
    def refresh(self):
        self._loadHistory()
    
    def addRecord(self, path: str, source: str, api_url: str):
        self.historyManager.add(path, source, api_url)
        self._loadHistory()
    
    def resizeEvent(self, event):
        super().resizeEvent(event)
        new_cols = self._calcColumns()
        if self._cards and new_cols != self._currentColumns:
            self._rebuildGrid()
        if self._allRecords and self._displayedCount < len(self._allRecords):
            target = self._calcInitialCount()
            if target > self._displayedCount:
                self._displayPage(target - self._displayedCount)


class _ShrinkableWidget(QWidget):
    def minimumSizeHint(self):
        hint = super().minimumSizeHint()
        return QSize(0, hint.height())


class WallpaperInterface(ScrollArea):
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
        self.originalPixmap = QPixmap(1, 1)
        self.originalPixmap.fill(Qt.GlobalColor.transparent)
        
        self.autoGetTimer = QTimer(self)
        self.autoGetTimer.timeout.connect(self._getWallpaper)
        self.autoSyncCheckTimer = QTimer(self)
        self.autoSyncCheckTimer.timeout.connect(self._checkAutoSync)
        
        self.wallpaperLabel = QLabel("壁纸", self)
        
        self.backgroundImage = QLabel()
        self.backgroundImage.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.backgroundImage.setSizePolicy(QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Ignored)
        
        self.dimOverlay = QWidget()
        self.dimOverlay.setObjectName("dimOverlay")
        self.dimOverlay.setSizePolicy(QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Ignored)
        
        self.contentWidget = QWidget()
        self.contentWidget.setObjectName("wallpaperContent")
        self.contentLayout = QVBoxLayout(self.contentWidget)
        self.contentLayout.setContentsMargins(60, 20, 60, 40)
        self.contentLayout.setSpacing(16)
        
        self.infoCard = WallpaperInfoCard(self.contentWidget)
        
        self.getButton = PrimaryPushButton(FIF.DOWNLOAD, "获取壁纸")
        self.getButton.setFixedHeight(36)
        self.getButton.setFixedWidth(140)
        self.saveButton = PushButton(FIF.SAVE, "另存壁纸")
        self.saveButton.setFixedHeight(36)
        self.saveButton.setFixedWidth(120)
        self.selectButton = PushButton(FIF.FOLDER, "手动选择")
        self.selectButton.setFixedHeight(36)
        self.selectButton.setFixedWidth(120)
        self.setWallpaperButton = PushButton(FIF.HOME, "设为桌面")
        self.setWallpaperButton.setFixedHeight(36)
        self.setWallpaperButton.setFixedWidth(120)
        
        self.settingsGroup = SettingCardGroup("壁纸设置", self.contentWidget)
        self.wallpaperSaveLimitCard = RangeSettingCard(
            cfg.wallpaperSaveLimit,
            FIF.SAVE,
            "壁纸保存量",
            "设置本地保存的壁纸数量上限（10-200）",
            parent=self.settingsGroup
        )
        self.settingsGroup.addSettingCard(self.wallpaperSaveLimitCard)
        self.autoGetIntervalCard = ComboBoxSettingCard(
            cfg.autoGetInterval,
            FIF.SYNC,
            "自动获取间隔",
            "自动获取新壁纸的时间间隔（0表示禁用）",
            texts=["从不", "10分钟", "30分钟", "1小时", "2小时", "6小时", "12小时", "1天", "3天", "7天"],
            parent=self.settingsGroup
        )
        self.settingsGroup.addSettingCard(self.autoGetIntervalCard)
        self.wallpaperApiCard = ComboBoxSettingCard(
            cfg.wallpaperApi,
            FIF.LINK,
            "壁纸API",
            "选择获取壁纸的API源",
            texts=["wp.upx8.com", "api.ltyuanfang.cn", "imlcd.cn_bg_high", "imlcd.cn_bg_mc", "imlcd.cn_bg_gq"],
            parent=self.settingsGroup
        )
        self.settingsGroup.addSettingCard(self.wallpaperApiCard)
        self.autoSyncToDesktopCard = SwitchSettingCard(
            FIF.HOME,
            "自动同步至桌面",
            "获取到新壁纸时自动设为系统桌面背景",
            configItem=cfg.autoSyncToDesktop,
            parent=self.settingsGroup
        )
        self.settingsGroup.addSettingCard(self.autoSyncToDesktopCard)
        
        self.effectsGroup = SettingCardGroup("背景效果", self.contentWidget)
        self.brightnessCard = RangeSettingCard(
            cfg.wallpaperBrightness,
            FIF.BRIGHTNESS,
            "亮度",
            "调整壁纸的明暗程度（-100最暗 ~ 0正常 ~ 100最亮）",
            parent=self.effectsGroup
        )
        self.effectsGroup.addSettingCard(self.brightnessCard)
        self.contrastCard = RangeSettingCard(
            cfg.wallpaperContrast,
            FIF.ALBUM,
            "对比度",
            "调整壁纸的对比度（-100最低 ~ 0正常 ~ 100最高）",
            parent=self.effectsGroup
        )
        self.effectsGroup.addSettingCard(self.contrastCard)
        self.saturationCard = RangeSettingCard(
            cfg.wallpaperSaturation,
            FIF.PALETTE,
            "饱和度",
            "调整壁纸的色彩鲜艳程度（-100黑白 ~ 0正常 ~ 100最艳）",
            parent=self.effectsGroup
        )
        self.effectsGroup.addSettingCard(self.saturationCard)
        self.colorTemperatureCard = RangeSettingCard(
            cfg.wallpaperColorTemperature,
            FIF.HIGHTLIGHT,
            "色温",
            "调整壁纸的色调（-100冷色调 ~ 0正常 ~ 100暖色调）",
            parent=self.effectsGroup
        )
        self.effectsGroup.addSettingCard(self.colorTemperatureCard)
        
        self._initWidget()
        self._connectSignalToSlot()
    
    def _onThemeChanged(self, theme: Theme):
        if hasattr(self, 'scrollWidget'):self._setQss()
    
    def _initWidget(self):
        self.resize(1000, 800)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setViewportMargins(0, 0, 0, 0)
        self.setWidgetResizable(True)
        
        self._initLayout()
        self._setQss()
        
        self.historyManager.sync_cleanup()
        
        self._loadDefaultWallpaper()
    
    def _initLayout(self):
        self.wallpaperLabel.setObjectName('settingLabel')
        
        actionRow = QHBoxLayout()
        actionRow.setSpacing(12)
        actionRow.addWidget(self.getButton)
        actionRow.addWidget(self.saveButton)
        actionRow.addWidget(self.selectButton)
        actionRow.addStretch(1)
        actionRow.addWidget(self.setWallpaperButton)
        
        self.contentLayout.addWidget(self.wallpaperLabel)
        self.contentLayout.addSpacing(16)
        self.contentLayout.addLayout(actionRow)
        self.contentLayout.addWidget(self.infoCard)
        self.contentLayout.addSpacing(24)
        self.contentLayout.addWidget(self.settingsGroup)
        self.contentLayout.addSpacing(24)
        self.contentLayout.addWidget(self.effectsGroup)
        self.contentLayout.addStretch(1)
        
        gridLayout = QGridLayout()
        gridLayout.setContentsMargins(0, 0, 0, 0)
        gridLayout.setSpacing(0)
        gridLayout.addWidget(self.dimOverlay, 0, 0, 1, 1)
        gridLayout.addWidget(self.contentWidget, 0, 0, 1, 1)
        
        self.scrollWidget = _ShrinkableWidget()
        self.scrollWidget.setObjectName('scrollWidget')
        self.scrollWidget.setLayout(gridLayout)
        self.backgroundImage.setParent(self.scrollWidget)
        self.backgroundImage.lower()
        self.setWidget(self.scrollWidget)
        self.scrollWidget.installEventFilter(self)
    
    def _setQss(self):
        self.scrollWidget.setObjectName('scrollWidget')
        self.wallpaperLabel.setObjectName('settingLabel')
        self.setStyleSheet(load_qss('setting_interface.qss'))
    
    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self.current_pixmap and not self.current_pixmap.isNull():
            if not hasattr(self, '_resizeTimer'): self._resizeTimer = QTimer(self); self._resizeTimer.setSingleShot(True); self._resizeTimer.timeout.connect(self._updateBackground)
            self._resizeTimer.start(100)
    
    def eventFilter(self, obj, event):
        if obj is self.scrollWidget and event.type() == event.Type.Resize:
            if self.current_pixmap and not self.current_pixmap.isNull():
                if not hasattr(self, '_resizeTimer'): self._resizeTimer = QTimer(self); self._resizeTimer.setSingleShot(True); self._resizeTimer.timeout.connect(self._updateBackground)
                self._resizeTimer.start(100)
        return super().eventFilter(obj, event)
    
    def _connectSignalToSlot(self):
        self.getButton.clicked.connect(self._getWallpaper)
        self.saveButton.clicked.connect(self._saveWallpaper)
        self.selectButton.clicked.connect(self._selectWallpaper)
        self.setWallpaperButton.clicked.connect(self._setWallpaper)
        
        cfg.autoGetInterval.valueChanged.connect(self._updateAutoGetTimer)
        cfg.autoSyncToDesktop.valueChanged.connect(self._updateAutoSyncCheckTimer)
        cfg.backgroundBlurRadius.valueChanged.connect(self._updateBackgroundBlur)
        cfg.wallpaperSaveLimit.valueChanged.connect(self._onWallpaperSaveLimitChanged)
        cfg.wallpaperBrightness.valueChanged.connect(self._applyEffects)
        cfg.wallpaperContrast.valueChanged.connect(self._applyEffects)
        cfg.wallpaperSaturation.valueChanged.connect(self._applyEffects)
        cfg.wallpaperColorTemperature.valueChanged.connect(self._applyEffects)
        
        self._updateAutoGetTimer()
        self._updateAutoSyncCheckTimer()
    
    def _updateAutoGetTimer(self):
        self.autoGetTimer.stop()
        interval_str = cfg.autoGetInterval.value
        
        if interval_str != "从不":
            interval_map = {
                "10分钟": 10 * 60 * 1000,
                "30分钟": 30 * 60 * 1000,
                "1小时": 60 * 60 * 1000,
                "3小时": 3 * 60 * 60 * 1000,
                "6小时": 6 * 60 * 60 * 1000,
                "12小时": 12 * 60 * 60 * 1000,
                "1天": 24 * 60 * 60 * 1000,
                "3天": 3 * 24 * 60 * 60 * 1000,
                "5天": 5 * 24 * 60 * 60 * 1000,
                "7天": 7 * 24 * 60 * 60 * 1000,
            }
            interval = interval_map.get(interval_str, 30 * 60 * 1000)
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
    
    def _updateBackgroundBlur(self):
        if hasattr(self, 'mainWindow') and self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage'):
            if self.mainWindow.originalPixmap is not None and not self.mainWindow.originalPixmap.isNull():
                self.mainWindow.resizeEvent(None)
    
    def _onWallpaperSaveLimitChanged(self, new_limit: int):
        wallpaper_dir = os.path.join(BASE_DIR, 'wallpaper')
        self._manageWallpaperLimit(wallpaper_dir, new_limit)
    
    def _applyEffects(self):
        """应用亮度、对比度、饱和度、色温效果"""
        if not self.current_pixmap or self.current_pixmap.isNull():
            return
        
        brightness = cfg.wallpaperBrightness.value
        contrast = cfg.wallpaperContrast.value
        saturation = cfg.wallpaperSaturation.value
        colorTemp = cfg.wallpaperColorTemperature.value
        
        if brightness == 0 and contrast == 0 and saturation == 0 and colorTemp == 0:
            self._updateBackground()
            return
        
        image = self.current_pixmap.toImage()
        
        for y in range(image.height()):
            for x in range(image.width()):
                color = image.pixelColor(x, y)
                r, g, b = color.red(), color.green(), color.blue()
                
                if brightness != 0:
                    factor = (259 * (brightness + 255)) / (255 * (259 - brightness))
                    r = min(255, max(0, int(factor * (r - 128) + 128)))
                    g = min(255, max(0, int(factor * (g - 128) + 128)))
                    b = min(255, max(0, int(factor * (b - 128) + 128)))
                
                if contrast != 0:
                    factor = (259 * (contrast + 255)) / (255 * (259 - contrast))
                    r = min(255, max(0, int(factor * (r - 128) + 128)))
                    g = min(255, max(0, int(factor * (g - 128) + 128)))
                    b = min(255, max(0, int(factor * (b - 128) + 128)))
                
                if saturation != 0:
                    gray = 0.299 * r + 0.587 * g + 0.114 * b
                    sat_factor = 1 + saturation / 100.0
                    r = min(255, max(0, int(gray + sat_factor * (r - gray))))
                    g = min(255, max(0, int(gray + sat_factor * (g - gray))))
                    b = min(255, max(0, int(gray + sat_factor * (b - gray))))
                
                if colorTemp != 0:
                    temp_factor = colorTemp / 100.0
                    r = min(255, max(0, int(r * (1 + temp_factor))))
                    b = min(255, max(0, int(b * (1 - temp_factor))))
                
                image.setPixelColor(x, y, QColor(r, g, b))
        
        self.current_pixmap = QPixmap.fromImage(image)
        self._updateBackground()
    
    def _loadWallpaperFromCache(self) -> bool:
        cached = get_cached_content("wallpaper")
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
        self.current_pixmap = QPixmap(wallpaper_path)
        self.current_wallpaper_path = wallpaper_path
        self.current_wallpaper_source = cached.get("source", "缓存")
        
        if self.current_pixmap.isNull():
            logger.error(f"无法加载缓存壁纸图片: {wallpaper_path}")
            return False
        
        self._updateBackground()
        self._updateMainWindowBackground()
        self.infoCard.updateInfo(wallpaper_path, cached.get("source", "缓存"))
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
        
        if self._loadWallpaperFromCache():return
        
        success = False
        
        try:
            url, source = self._getApiUrl()
            logger.info(f"请求壁纸 URL: {url}")
            response = requests.get(url, stream=True, timeout=10)
            
            if response.status_code == 200:
                logger.info(f"壁纸请求成功，状态码: {response.status_code}")
                wallpaper_dir = os.path.join(BASE_DIR, 'wallpaper')
                if not os.path.exists(wallpaper_dir):
                    os.makedirs(wallpaper_dir)
                    logger.info(f"创建壁纸目录: {wallpaper_dir}")
                
                current_date = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
                wallpaper_path = os.path.join(wallpaper_dir, f'wallpaper_{current_date}.jpg')
                
                with open(wallpaper_path, 'wb') as f:
                    f.write(response.content)
                logger.info(f"壁纸已保存到: {wallpaper_path}")
                
                self._manageWallpaperLimit(wallpaper_dir, cfg.wallpaperSaveLimit.value)
                
                self.current_pixmap = QPixmap(wallpaper_path)
                self.current_wallpaper_path = wallpaper_path
                self.current_wallpaper_source = source
                
                cache_data = {
                    "path": wallpaper_path,
                    "source": source,
                    "url": url,
                }
                save_cache("wallpaper", cache_data, cfg.autoGetInterval.value)
                
                if not self.current_pixmap.isNull():
                    self._updateBackground()
                    self._updateMainWindowBackground()
                    
                    self.historyManager.add(wallpaper_path, source, url)
                
                self.infoCard.updateInfo(wallpaper_path, source)
                
                InfoBar.success("成功", f"壁纸已下载", duration=3000, parent=self)
                success = True
                
                if cfg.autoSyncToDesktop.value:
                    self._setWallpaper(show_notification=True)
                    self.last_sync_path = wallpaper_path
            else:
                logger.error(f"获取壁纸失败，状态码: {response.status_code}")
                InfoBar.error("错误", f"获取壁纸失败：HTTP {response.status_code}", duration=5000, parent=self)
                
        except Exception as e:
            logger.error(f"获取壁纸失败：{str(e)}")
            InfoBar.error("错误", f"获取壁纸失败：{str(e)}", duration=5000, parent=self)
        
        if not success:
            self._loadDefaultWallpaper()
    
    def _loadDefaultWallpaper(self):
        logger.info("加载默认壁纸")
        if self._loadWallpaperFromCache():return
        default_wallpaper_path = get_resPath(os.path.join('resource', 'wallpaper', 'default.jpg'))
        
        if not os.path.exists(default_wallpaper_path):
            wallpaper_dir = os.path.join(BASE_DIR, 'wallpaper')
            if os.path.exists(wallpaper_dir):
                wallpapers = [f for f in os.listdir(wallpaper_dir) if f.endswith('.jpg') and f.startswith('wallpaper_')]
                if wallpapers:
                    wallpapers.sort(key=lambda x: os.path.getmtime(os.path.join(wallpaper_dir, x)), reverse=True)
                    default_wallpaper_path = os.path.join(wallpaper_dir, wallpapers[0])
        
        if os.path.exists(default_wallpaper_path):
            self.current_pixmap = QPixmap(default_wallpaper_path)
            self.current_wallpaper_path = default_wallpaper_path
            self.current_wallpaper_source = "默认"
            
            if not self.current_pixmap.isNull():
                self._updateBackground()
                self._updateMainWindowBackground()
            
            self.infoCard.updateInfo(default_wallpaper_path, "默认")
        else:
            self._setBlankBackground()
            self.infoCard.updateInfo()
    
    def _setBlankBackground(self):
        self.backgroundImage.setPixmap(QPixmap(1, 1))
        self.backgroundImage.setMinimumSize(100, 100)
        if self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage'):
            available_width = self.mainWindow.width() - 50
            available_height = self.mainWindow.height()
            blank_pixmap = QPixmap(available_width, available_height)
            blank_pixmap.fill(Qt.GlobalColor.transparent)
            self.mainWindow.originalPixmap = blank_pixmap
            self.mainWindow.homeBackgroundImage.setPixmap(blank_pixmap)
            self.mainWindow.homeBackgroundImage.setMinimumSize(available_width, available_height)
    
    def _updateBackground(self):
        if not self.current_pixmap or self.current_pixmap.isNull():
            return
        
        self.originalPixmap = self.current_pixmap
        available_width = self.viewport().width()
        available_height = max(self.viewport().height(), self.scrollWidget.height(), 600)
        
        if hasattr(self, '_cachedBgSize') and self._cachedBgSize == (available_width, available_height) and hasattr(self, '_cachedBgPixmap') and self._cachedBgPixmap == self.current_pixmap:return
        self._cachedBgSize = (available_width, available_height)
        self._cachedBgPixmap = self.current_pixmap
        
        scaled_pixmap = self.current_pixmap.scaled(
            available_width, available_height,
            Qt.AspectRatioMode.KeepAspectRatioByExpanding,
            Qt.TransformationMode.SmoothTransformation
        )
        
        if not hasattr(self, '_blurEffect'):
            self._blurEffect = QGraphicsBlurEffect()
            self._blurEffect.setBlurRadius(12)
            self.backgroundImage.setGraphicsEffect(self._blurEffect)
        
        self.backgroundImage.setPixmap(scaled_pixmap)
        self.backgroundImage.setGeometry(0, 0, available_width, available_height)
        self.backgroundImage.lower()
    
    def _updateMainWindowBackground(self):
        if self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage') and self.current_pixmap:
            self.mainWindow.originalPixmap = self.current_pixmap
            available_width = self.mainWindow.width() - 50
            available_height = self.mainWindow.height()
            scaled_pixmap = self.current_pixmap.scaled(
                available_width, available_height,
                Qt.AspectRatioMode.IgnoreAspectRatio, Qt.TransformationMode.SmoothTransformation
            )
            self.mainWindow.homeBackgroundImage.setPixmap(scaled_pixmap)
    
    def _saveWallpaper(self):
        logger.info("开始另存壁纸")
        if self.current_pixmap is None:
            InfoBar.warning("提示", "请先获取壁纸", duration=3000, parent=self)
            return
        
        file_path, _ = QFileDialog.getSaveFileName(
            self, "另存壁纸",
            os.path.join(BASE_DIR, "wallpaper"),
            "JPEG图片 (*.jpg);;PNG图片 (*.png)"
        )
        
        if file_path:
            try:
                self.current_pixmap.save(file_path)
                logger.info(f"壁纸已保存到: {file_path}")
                InfoBar.success("成功", f"壁纸已保存", duration=3000, parent=self)
            except Exception as e:
                logger.error(f"保存壁纸失败: {str(e)}")
                InfoBar.error("错误", f"保存失败：{str(e)}", duration=5000, parent=self)
    
    def _selectWallpaper(self):
        logger.info("开始手动选择壁纸")
        file_path, _ = QFileDialog.getOpenFileName(
            self, "选择壁纸",
            os.path.join(BASE_DIR, "wallpaper"),
            "图片文件 (*.jpg *.jpeg *.png *.bmp *.gif)"
        )
        
        if file_path:
            self._useWallpaper(file_path, "本地选择")
    
    def _useWallpaper(self, path: str, source: str):
        if not os.path.exists(path):
            InfoBar.error("错误", "壁纸文件不存在", duration=3000, parent=self)
            return
        
        try:
            self.current_pixmap = QPixmap(path)
            self.current_wallpaper_path = path
            self.current_wallpaper_source = source
            
            if not self.current_pixmap.isNull():
                self._updateBackground()
                self._updateMainWindowBackground()
                
                if source != "历史记录":
                    _, api_source = self._getApiUrl()
                    self.historyManager.add(path, source if source != "本地选择" else api_source, "")
                
                self.infoCard.updateInfo(path, source)
                
                InfoBar.success("成功", "已应用壁纸", duration=2000, parent=self)
                logger.info(f"已应用壁纸: {path}")
                
        except Exception as e:
            logger.error(f"应用壁纸失败: {str(e)}")
            InfoBar.error("错误", f"应用失败：{str(e)}", duration=5000, parent=self)
    
    def _manageWallpaperLimit(self, wallpaper_dir, save_limit):
        self.historyManager.sync_cleanup(save_limit)
    
    def _setWallpaper(self, show_notification=True):
        if self.current_wallpaper_path is None:
            if show_notification:
                InfoBar.warning("提示", "请先获取或选择壁纸", duration=3000, parent=self)
            return
        
        logger.info(f"设置壁纸路径: {self.current_wallpaper_path}")
        try:
            SPI_SETDESKWALLPAPER = 20
            SPIF_UPDATEINIFILE = 0x01
            SPIF_SENDWININICHANGE = 0x02
            
            ctypes.windll.user32.SystemParametersInfoW(
                SPI_SETDESKWALLPAPER,
                0,
                self.current_wallpaper_path,
                SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE
            )
            
            self.last_sync_path = self.current_wallpaper_path
            logger.info("壁纸已成功设置为桌面背景")
            
            if show_notification:
                InfoBar.success("成功", "壁纸已设置为桌面背景", duration=3000, parent=self)
                
        except Exception as e:
            logger.error(f"设置壁纸失败: {str(e)}")
            if show_notification:
                InfoBar.error("错误", f"设置失败：{str(e)}", duration=5000, parent=self)
