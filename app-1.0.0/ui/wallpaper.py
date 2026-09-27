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
            logger.debug("壁纸历史单例复用")
            return
        self._initialized = True

        self.wallpaper_dir = WALLPAPER_DIR
        self.history_file = os.path.join(self.wallpaper_dir, HISTORY_FILE_NAME)
        self._history: List[WallpaperRecord] = []
        self._load()
        logger.debug(f"壁纸历史就绪 {len(self._history)}条 {self.history_file}")

    def _load(self):
        if not os.path.exists(self.history_file):
            self._history = []
            logger.debug(f"跳过加载 {self.history_file}")
            return
        
        try:
            with open(self.history_file, 'r', encoding='utf-8') as f:data = json.load(f)
            if data.get('version') != HISTORY_VERSION:
                logger.warning(f"壁纸历史文件版本不匹配: 文件 {data.get('version')} ≠ 程序 {HISTORY_VERSION} 重置历史")
                self._history = []
                return
            self._history = [
                WallpaperRecord.from_dict(item)
                for item in data.get('history', [])
            ]
            logger.debug(f"壁纸历史文件已加载: {len(self._history)}条")
            
        except Exception as e:
            logger.error(f"加载壁纸历史记录失败 {e}")
            self._history = []
        
        self.clear_invalid()
    
    def sync_cleanup(self, max_files: int = None):
        if max_files is None:
            max_files = cfg.wallpaperSaveLimit.value
        if not os.path.exists(self.wallpaper_dir):
            logger.debug(f"跳过清理 {self.wallpaper_dir}")
            return
        logger.debug(f"壁纸目录清理 目录{self.wallpaper_dir} 保留{max_files}个")
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
                logger.debug(f"已删除过期壁纸文件: {old_file_path}")
            except Exception as e:
                logger.warning(f"删除壁纸失败 {old_file_path}: {e}")
        if deleted_count > 0:
            logger.info(f"共删除{deleted_count}个壁纸")
        else:
            logger.debug("壁纸目录无需清理: 文件数在保留上限内")

        return deleted_count
    
    def _save(self):
        if not os.path.exists(self.wallpaper_dir):
            logger.debug(f"壁纸目录不存在 创建: {self.wallpaper_dir}")
            os.makedirs(self.wallpaper_dir)
        
        try:
            data = {
                'version': HISTORY_VERSION,
                'history': [record.to_dict() for record in self._history]
            }
            with open(self.history_file, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            logger.debug(f"壁纸历史已写入: {self.history_file} 共 {len(self._history)} 条")
        except Exception as e:
            logger.error(f"保存壁纸历史记录失败 {e}")
    
    def add(self, path: str, source: str, api_url: str) -> WallpaperRecord:
        if not os.path.exists(path):
            logger.warning(f"跳过写入历史 {path}")
            return None
        record_id = os.path.splitext(os.path.basename(path))[0]
        existing = self.get_by_id(record_id)
        if existing:
            logger.debug(f"壁纸记录已存在 移到历史首位: {record_id}")
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
            else: logger.debug(f"壁纸分辨率读取无效: {path}")
        except Exception as e:
            logger.warning(f"壁纸分辨率读取失败: {path} - {e}")
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
        logger.info(f"壁纸入历史: {record.id} ({source} {resolution} {file_size // 1024}KB 共{len(self._history)}条)")
        return record
    
    def remove(self, record_id: str) -> bool:
        record = self.get_by_id(record_id)
        if record:
            self._history.remove(record)
            self._save()
            logger.info(f"已删除壁纸记录: {record_id} ({record.path})")
            return True
        logger.debug(f"壁纸记录未找到 id={record_id}")
        return False
    
    def get_by_id(self, record_id: str) -> Optional[WallpaperRecord]:
        for record in self._history:
            if record.id == record_id:
                logger.debug(f"壁纸记录命中: {record_id} -> {record.path}")
                return record
        return None
    
    def clear_invalid(self):
        invalid = [r for r in self._history if not r.exists()]
        for record in invalid:self._history.remove(record)
        if invalid:
            self._save()
            logger.info(f"已清理失效壁纸记录{len(invalid)}条 剩余{len(self._history)}条")
        else:
            logger.debug("壁纸历史无失效记录")
        return len(invalid)


def get_wallpaper_history() -> WallpaperHistory:
    logger.debug("获取壁纸历史管理器 单例")
    return WallpaperHistory()


class WallpaperInfoCard(CardWidget):
    """壁纸信息卡片"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("wallpaperInfoCard")
        self._setupUi()
    
    def _setupUi(self):
        logger.debug("壁纸信息卡初始化: 分辨率/大小/来源/路径 标签")
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
            logger.debug(f"信息卡重置为空状态 (path={path})")
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
            if size.isValid():
                resolution = f"{size.width()}x{size.height()}"
            else:
                logger.debug(f"信息卡分辨率读取无效: {path}")
        except Exception as e:
            logger.warning(f"信息卡分辨率读取失败: {path} - {e}")

        self.resolutionLabel.setText(f"{tr('wallpaper.resolution')}: {resolution}")
        self.sizeLabel.setText(f"{tr('wallpaper.size')}: {size_str}")
        self.sourceLabel.setText(f"{tr('wallpaper.source')}: {source or tr('wallpaper.source_local')}")

        display_path = path
        if len(path) > 50:
            display_path = "..." + path[-47:]
        self.pathLabel.setText(f"{tr('wallpaper.path')}: {display_path}")
        logger.debug(f"信息卡 路径={path} 来源={source or tr('wallpaper.source_local')} 大小={size_str} 分辨率={resolution}")


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
        self._last_auto_interval = None
        
        self.autoGetTimer = QTimer(self)
        self.autoGetTimer.timeout.connect(lambda: (logger.info("[Wallpaper] 自动获取壁纸定时器到期触发"), self._getWallpaper()))
        self.autoSyncCheckTimer = QTimer(self)
        self.autoSyncCheckTimer.timeout.connect(self._checkAutoSync)
        logger.debug("壁纸定时器创建: 自动获取 + 同步桌面检查")

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
        logger.debug(f"壁纸就绪 mainWindow={'已关联' if self.mainWindow else '未关联'} 历史{len(self.historyManager._history)}条")

    def _getHome(self):
        if hasattr(self.mainWindow, 'homeInterface'):
            logger.debug("_getHome: 已关联主页控件")
            return self.mainWindow.homeInterface
        logger.debug("_getHome: 主窗口未关联或无 homeInterface")
        return None

    def _onThemeChanged(self, theme: Theme):
        logger.debug(f"壁纸页主题变更: {theme}")
        self._setQss()
    
    def _initWidget(self):
        self.resize(1000, 800)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setViewportMargins(0, 120, 0, 20)
        self.setWidgetResizable(True)
        logger.debug(f"壁纸界面控件初始化: 尺寸 1000x800 上边距 120")

        self._initLayout()
        self._setQss()

        QTimer.singleShot(0, self.__deferredInit)

    def __deferredInit(self):
        logger.debug("壁纸界面延迟初始化: 清理过期壁纸并加载默认壁纸")
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
        logger.debug("壁纸布局就绪 操作行+信息卡+设置组+特效组")
    
    def _setQss(self):
        logger.debug("壁纸页样式加载: wallpaper.qss")
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
        logger.debug("壁纸页信号槽已连接 4按钮+4配置项+定时器")

    def hideEvent(self, event):
        self.autoGetTimer.stop()
        self.autoSyncCheckTimer.stop()
        logger.debug("壁纸页面隐藏: 已停止自动获取与同步检查定时器")
        super().hideEvent(event)

    def showEvent(self, event):
        super().showEvent(event)
        logger.debug("墙纸页显示 重载配置")
        self._updateAutoGetTimer()
        self._updateAutoSyncCheckTimer()

    def _updateAutoGetTimer(self):
        self.autoGetTimer.stop()
        interval_str = cfg.autoGetInterval.value

        if interval_str != "never":
            interval = INTERVAL_MAP.get(interval_str.strip(), 30 * 60) * 1000
            if interval_str.strip() not in INTERVAL_MAP:
                logger.debug(f"自动获取间隔未识别 {interval_str} 用 1800 秒")
            self.autoGetTimer.start(interval)
        if interval_str != self._last_auto_interval:
            self._last_auto_interval = interval_str
            if interval_str == "never":
                logger.info("自动获取壁纸已禁用")
            else:
                logger.info(f"自动获取间隔 {interval_str} ({INTERVAL_MAP.get(interval_str.strip(), 30 * 60)}秒)")

    def _checkAutoSync(self):
        if cfg.autoSyncToDesktop.value and self.current_wallpaper_path is not None:
            if self.last_sync_path != self.current_wallpaper_path:
                logger.info(f"壁纸变更 自动同步桌面: {self.current_wallpaper_path} (上次同步: {self.last_sync_path})")
                self._setWallpaper(show_notification=False)
                self.last_sync_path = self.current_wallpaper_path
            self._lastAutoSyncSkipLogged = True
        elif getattr(self, '_lastAutoSyncSkipLogged', True):
            # 守卫: 该检查由定时器高频触发, 仅在跳过状态切换时记一次
            self._lastAutoSyncSkipLogged = False
            logger.debug("自动同步 关闭或无壁纸")

    def _updateAutoSyncCheckTimer(self):
        self.autoSyncCheckTimer.stop()
        if cfg.autoSyncToDesktop.value:
            self.autoSyncCheckTimer.start(5000)
            logger.debug("自动同步桌面检查定时器已启动: 间隔 5000 ms")
        else:
            logger.debug("自动同步桌面已关闭 检查定时器停止")
    
    def _applyEffects(self):
        """暗化（-100最暗 ~ 0正常）"""
        dim_value = cfg.wallpaperBrightness.value
        alpha = abs(dim_value) / 100.0
        style_str = f"#dimOverlay {{ background-color: rgba(0, 0, 0, {alpha:.2f}); }}"
        logger.debug(f"应用背景暗化效果: 亮度值 {dim_value} 透明度 {alpha:.2f}")
        home = self._getHome()
        if home and hasattr(home, 'homeDimOverlay'):
            home.homeDimOverlay.setStyleSheet(style_str)
        else:
            logger.debug("暗化 主页或 homeDimOverlay 不存在")
    
    def _loadWallpaperFromCache(self) -> bool:
        cached = get_cached_content("wallpaper", ignore_expiry=True)
        if not cached:
            logger.debug("壁纸缓存不存在")
            return False

        wallpaper_path = cached.get("path", "")
        if not wallpaper_path:
            logger.warning("壁纸缓存缺路径")
            return False

        if not os.path.exists(wallpaper_path):
            logger.warning(f"缓存壁纸文件不存在: {wallpaper_path}")
            return False

        logger.info(f"用缓存壁纸: {wallpaper_path}")
        logger.debug(f"缓存壁纸详情: 来源={cached.get('source')} url={cached.get('url')}")
        source = cached.get("source", tr("wallpaper.source_cache"))
        if not self._applyCurrentWallpaper(wallpaper_path, source):
            logger.error(f"缓存壁纸加载失败 {wallpaper_path}")
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
        logger.info(f"用壁纸 api: {wallpaper_api}")
        if wallpaper_api in api_map:
            return api_map[wallpaper_api]
        logger.debug(f"api {wallpaper_api} 无映射 用默认: wp.upx8.com")
        return api_map.get(wallpaper_api, ("https://wp.upx8.com/api.php?content=风景", "wp.upx8.com"))
    
    def _getWallpaper(self):
        logger.info("获取壁纸")
        
        success = False
        
        try:
            url, source = self._getApiUrl()
            logger.info(f"请求壁纸 url: {url}")
            response = requests.get(url, stream=True, timeout=10)
            
            if response.status_code == 200:
                logger.info(f"壁纸已请求 http {response.status_code}")
                wallpaper_dir = WALLPAPER_DIR
                if not os.path.exists(wallpaper_dir):
                    os.makedirs(wallpaper_dir)
                    logger.info(f"创建壁纸目录: {wallpaper_dir}")
                
                current_date = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
                wallpaper_path = os.path.join(wallpaper_dir, f'wallpaper_{current_date}.jpg')
                
                with open(wallpaper_path, 'wb') as f:
                    f.write(response.content)
                logger.info(f"壁纸已保存到: {wallpaper_path}")
                logger.debug(f"壁纸下载字节数: {len(response.content)}")
                
                # 解码失败(返回 html/损坏数据)不算成功,删掉坏文件
                if self._applyCurrentWallpaper(wallpaper_path, source):
                    self.historyManager.sync_cleanup(cfg.wallpaperSaveLimit.value)
                    self.historyManager.add(wallpaper_path, source, url)
                    save_cache("wallpaper", {"path": wallpaper_path, "source": source, "url": url},
                               cfg.autoGetInterval.value)
                    logger.debug(f"壁纸缓存已写入: path={wallpaper_path} source={source}")
                    InfoBar.success(tr("wizard.success_title"), tr("wallpaper.downloaded"), duration=3000, parent=self)
                    success = True
                    if cfg.autoSyncToDesktop.value:
                        self._setWallpaper(show_notification=True)
                else:
                    logger.error(f"壁纸图片无效: {wallpaper_path}")
                    try:
                        os.remove(wallpaper_path)
                    except OSError as e:
                        logger.warning(f"删除无效壁纸文件失败: {wallpaper_path} - {e}")
                    InfoBar.error(tr("dialog.error"), tr("wallpaper.load_image_failed"), duration=5000, parent=self)
            else:
                logger.error(f"获取壁纸失败 状态码: {response.status_code}")
                InfoBar.error(tr("dialog.error"), tr("wallpaper.fetch_failed_http").format(status=response.status_code), duration=5000, parent=self)
                
        except Exception as e:
            logger.error(f"获取壁纸失败 {str(e)}")
            InfoBar.error(tr("dialog.error"), tr("wallpaper.fetch_failed").format(error=str(e)), duration=5000, parent=self)
        
        # 已有壁纸就保持现状,失败不覆盖用户当前选择
        if not success and self.current_wallpaper_path is None:
            logger.debug("获取失败 无壁纸 用默认壁纸")
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
                    logger.debug(f"资源壁纸缺失 改用最近的历史壁纸: {default_wallpaper_path}")

        if os.path.exists(default_wallpaper_path):
            logger.debug(f"用内置默认壁纸: {default_wallpaper_path}")
            self._applyCurrentWallpaper(default_wallpaper_path, tr("wallpaper.default_source"))
        else:
            logger.warning("无可用壁纸 缓存 内置资源与历史记录均为空")
            self.infoCard.updateInfo()
    
    def _applyCurrentWallpaper(self, path: str, source: str) -> bool:
        """载入图片并刷新背景与信息卡,解码失败返回 False"""
        pixmap = QPixmap(path)
        if pixmap.isNull():
            # 校验失败不动任何 current_*,避免坏路径被轮询拿去设桌面
            logger.warning(f"壁纸解码失败 保持不变: {path}")
            return False
        self.current_pixmap = pixmap
        self.current_wallpaper_path = path
        self.current_wallpaper_source = source
        logger.debug(f"壁纸预览加载: {pixmap.width()}x{pixmap.height()} @ {path} (来源: {source})")
        self._updateMainWindowBackground()
        self._applyEffects()
        self.infoCard.updateInfo(path, source)
        return True

    def _updateMainWindowBackground(self):
        home = self._getHome()
        if home and self.current_pixmap:
            logger.debug(f"更新主窗口背景: {self.current_wallpaper_path}")
            home.originalPixmap = self.current_pixmap
            home._computeBlurredBackground()
        else:
            logger.debug("更新背景 主页未关联或无壁纸")

    def _saveWallpaper(self):
        logger.info("另存壁纸")
        if self.current_pixmap is None or self.current_pixmap.isNull():
            logger.debug("另存壁纸终止 无壁纸")
            InfoBar.warning(tr("common.tip"), tr("wallpaper.fetch_first"), duration=3000, parent=self)
            return
        
        file_path, _ = QFileDialog.getSaveFileName(
            self, tr("wallpaper.save_title"),
            WALLPAPER_DIR,
            f"{tr('wallpaper.jpeg_files')} (*.jpg);;{tr('wallpaper.png_files')} (*.png)"
        )
        
        if not file_path:
            logger.debug("用户取消另存壁纸")
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
        logger.info("手动选择壁纸")
        file_path, _ = QFileDialog.getOpenFileName(
            self, tr("wallpaper.select_title"),
            WALLPAPER_DIR,
            tr("wallpaper.image_filter")
        )
        
        if file_path:
            logger.debug(f"已选择壁纸文件: {file_path}")
            self._useWallpaper(file_path, tr("wallpaper.source_local"))
        else:
            logger.debug("用户取消手动选择壁纸")
    
    def _useWallpaper(self, path: str, source: str):
        if not os.path.exists(path):
            logger.warning(f"选择的壁纸文件不存在: {path}")
            InfoBar.error(tr("dialog.error"), tr("wallpaper.file_not_exist"), duration=3000, parent=self)
            return
        
        try:
            if self._applyCurrentWallpaper(path, source):
                self.historyManager.add(path, source, "")
                InfoBar.success(tr("wizard.success_title"), tr("wallpaper.applied"), duration=2000, parent=self)
                logger.info(f"已应用壁纸: {path}")
            else:
                logger.debug(f"壁纸应用失败 解码错误 不写历史 {path}")
        except Exception as e:
            logger.error(f"应用壁纸失败: {str(e)}")
            InfoBar.error(tr("dialog.error"), tr("wallpaper.apply_failed").format(error=str(e)), duration=5000, parent=self)

    def _setWallpaper(self, show_notification=True):
        if self.current_wallpaper_path is None:
            logger.debug("设置桌面壁纸 无壁纸")
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


