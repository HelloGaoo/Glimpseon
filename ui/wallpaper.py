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

from PyQt5.QtCore import Qt, QTimer
from PyQt5.QtWidgets import QLabel, QVBoxLayout, QHBoxLayout, QWidget, QFileDialog
from PyQt5.QtGui import QPixmap, QIcon
from qfluentwidgets import (
    CardWidget, FluentIcon as FIF, ImageLabel, PrimaryPushButton, PushButton,
    InfoBar, isDarkTheme, ScrollArea, SmoothScrollArea, ExpandLayout, Theme
)

import logging
import requests
import os
import datetime
import ctypes
from core.config import cfg
from core.constants import get_resource_path, BASE_DIR
from PyQt5.QtWidgets import QApplication

logger = logging.getLogger(__name__)


class WallpaperInterface(ScrollArea):
    """ 壁纸界面 """

    def __init__(self, mainWindow=None, parent=None):
        super().__init__(parent=parent)
        self.mainWindow = mainWindow
        self.scrollWidget = QWidget()
        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.wallpaperLabel = QLabel("壁纸", self)
        self.scrollArea = SmoothScrollArea()
        self.imageLabel = ImageLabel()
        self.imageLabel.setAlignment(Qt.AlignCenter)
        self.scrollArea.setWidget(self.imageLabel)
        self.scrollArea.setWidgetResizable(True)
        self.scrollArea.setHorizontalScrollBarPolicy(Qt.ScrollBarAsNeeded)
        self.scrollArea.setVerticalScrollBarPolicy(Qt.ScrollBarAsNeeded)
        self.getButton = PushButton(FIF.DOWNLOAD, "获取壁纸")
        self.getButton.setFixedHeight(50)
        self.getButton.setFixedWidth(200)
        self.saveButton = PushButton(FIF.SAVE, "另存壁纸")
        self.saveButton.setFixedHeight(50)
        self.saveButton.setFixedWidth(200)
        self.selectButton = PushButton(FIF.FOLDER, "手动选择")
        self.selectButton.setFixedHeight(50)
        self.selectButton.setFixedWidth(200)
        self.setWallpaperButton = PushButton(FIF.HOME, "设为桌面")
        self.setWallpaperButton.setFixedHeight(50)
        self.setWallpaperButton.setFixedWidth(200)
        
        self.current_pixmap = None
        self.current_wallpaper_path = None
        self.last_sync_path = None
        self.autoGetTimer = QTimer(self)
        self.autoGetTimer.timeout.connect(self.__getWallpaper)
        self.autoSyncCheckTimer = QTimer(self)
        self.autoSyncCheckTimer.timeout.connect(self.__checkAutoSync)

        self.__initWidget()
        self.__connectSignalToSlot()
    
    def _onThemeChanged(self, theme: Theme):
        """ 主题变更槽函数 """
        self.__setQss()

    def __initWidget(self):
        """ 初始化界面 """
        self.resize(1000, 800)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        self.setViewportMargins(0, -40, 0, 20)
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)

        self.__setQss()
        self.__initLayout()
        
        self.__getWallpaper()

    def __initLayout(self):
        """ 初始化布局 """
        self.wallpaperLabel.move(60, 63)

        buttonLayout = QHBoxLayout()
        buttonLayout.addWidget(self.getButton)
        buttonLayout.addSpacing(10)
        buttonLayout.addWidget(self.saveButton)
        buttonLayout.addSpacing(10)
        buttonLayout.addWidget(self.selectButton)
        buttonLayout.addSpacing(10)
        buttonLayout.addWidget(self.setWallpaperButton)

        self.mainLayout.setSpacing(20)
        self.mainLayout.setContentsMargins(60, 160, 60, 0) 
        self.mainLayout.addWidget(self.scrollArea)
        self.mainLayout.addLayout(buttonLayout)

    def __setQss(self):
        """ 设置样式表 """
        self.scrollWidget.setObjectName('scrollWidget')
        self.wallpaperLabel.setObjectName('settingLabel')

        theme = 'dark' if isDarkTheme() else 'light'
        try:
            qss_path = get_resource_path(os.path.join('resource', 'qss', theme, 'setting_interface.qss'))
            with open(qss_path, encoding='utf-8') as f:
                self.setStyleSheet(f.read())
        except Exception:
            pass

    def __connectSignalToSlot(self):
        self.getButton.clicked.connect(self.__getWallpaper)
        self.saveButton.clicked.connect(self.__saveWallpaper)
        self.selectButton.clicked.connect(self.__selectWallpaper)
        self.setWallpaperButton.clicked.connect(self.__setWallpaper)
        cfg.autoGetInterval.valueChanged.connect(self.__updateAutoGetTimer)
        cfg.autoSyncToDesktop.valueChanged.connect(self.__updateAutoSyncCheckTimer)
        cfg.backgroundBlurRadius.valueChanged.connect(self.__updateBackgroundBlur)
        self.__updateAutoGetTimer()
        self.__updateAutoSyncCheckTimer()

    def __updateAutoGetTimer(self):
        """ 更新自动获取壁纸的定时器 """
        self.autoGetTimer.stop()
        interval_str = cfg.autoGetInterval.value
        
        if interval_str != "从不":
            if interval_str == "10分钟":
                interval = 10 * 60 * 1000
            elif interval_str == "30分钟":
                interval = 30 * 60 * 1000
            elif interval_str == "1小时":
                interval = 60 * 60 * 1000
            elif interval_str == "3小时":
                interval = 3 * 60 * 60 * 1000
            elif interval_str == "6小时":
                interval = 6 * 60 * 60 * 1000
            elif interval_str == "12小时":
                interval = 12 * 60 * 60 * 1000
            elif interval_str == "1天":
                interval = 24 * 60 * 60 * 1000
            elif interval_str == "3天":
                interval = 3 * 24 * 60 * 60 * 1000
            elif interval_str == "5天":
                interval = 5 * 24 * 60 * 60 * 1000
            elif interval_str == "7天":
                interval = 7 * 24 * 60 * 60 * 1000
            else:
                interval = 30 * 60 * 1000
            
            self.autoGetTimer.start(interval)
    
    def __checkAutoSync(self):
        """ 检测自动同步至桌面是否启用 """
        if cfg.autoSyncToDesktop.value and self.current_wallpaper_path is not None:
            if self.last_sync_path != self.current_wallpaper_path:
                self.__setWallpaper(show_notification=False)
                self.last_sync_path = self.current_wallpaper_path
    
    def __updateAutoSyncCheckTimer(self):
        """ 更新自动同步检测定时器 """
        self.autoSyncCheckTimer.stop()
        if cfg.autoSyncToDesktop.value:self.autoSyncCheckTimer.start(5000)
    
    def __updateBackgroundBlur(self):
        """ 更新背景模糊强度 """
        if hasattr(self, 'mainWindow') and self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage'):
            if self.mainWindow.originalPixmap is not None and not self.mainWindow.originalPixmap.isNull():
                self.mainWindow.resizeEvent(None)

    def __getWallpaper(self):
        """ 获取壁纸 """
        logger.info("开始获取壁纸")
        success = False
        try:
            wallpaper_api = cfg.wallpaperApi.value
            if wallpaper_api == "api.ltyuanfang.cn":
                url = "https://tu.ltyuanfang.cn/api/fengjing.php"
            elif wallpaper_api == "imlcd.cn_bg_high":
                url = "https://api.imlcd.cn/bg/high.php"
            elif wallpaper_api == "imlcd.cn_bg_mc":
                url = "https://api.imlcd.cn/bg/mc.php"
            elif wallpaper_api == "imlcd.cn_bg_gq":
                url = "https://api.imlcd.cn/bg/gq.php"
            else:
                url = "https://wp.upx8.com/api.php?content=风景"
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

                save_limit = cfg.wallpaperSaveLimit.value
                self.__manageWallpaperLimit(wallpaper_dir, save_limit)
                self.current_pixmap = QPixmap(wallpaper_path)
                self.current_wallpaper_path = wallpaper_path
                if not self.current_pixmap.isNull():
                    self.imageLabel.setPixmap(self.current_pixmap)
                    # 更新主界面的背景照片
                    if self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage'):
                        self.mainWindow.originalPixmap = self.current_pixmap
                        available_width = self.mainWindow.width() - 50
                        available_height = self.mainWindow.height()
                        scaled_pixmap = self.current_pixmap.scaled(available_width, available_height, Qt.IgnoreAspectRatio, Qt.SmoothTransformation)
                        self.mainWindow.homeBackgroundImage.setPixmap(scaled_pixmap)
                        QApplication.processEvents()
                
                InfoBar.success(
                    "成功",
                    f"壁纸已下载到：{wallpaper_path}",
                    duration=5000,
                    parent=self
                )
                success = True
                
                if cfg.autoSyncToDesktop.value:
                    self.__setWallpaper(show_notification=True)
                    self.last_sync_path = wallpaper_path
            else:
                logger.error(f"获取壁纸失败，状态码: {response.status_code}")
                InfoBar.error(
                    "错误",
                    f"获取壁纸失败，状态码: {response.status_code}",
                    duration=5000,
                    parent=self
                )
        except Exception as e:
            logger.error(f"获取壁纸失败：{str(e)}")
            InfoBar.error(
                "错误",
                f"获取壁纸失败：{str(e)}",
                duration=5000,
                parent=self
            )
        
        if not success:
            self.__loadDefaultWallpaper()
    
    def __loadDefaultWallpaper(self):
        default_wallpaper_path = get_resource_path(os.path.join('resource', 'wallpaper', 'default.jpg'))
        
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
            if not self.current_pixmap.isNull():
                self.imageLabel.setPixmap(self.current_pixmap)
                if self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage'):
                    self.mainWindow.originalPixmap = self.current_pixmap
                    available_width = self.mainWindow.width() - 50
                    available_height = self.mainWindow.height()
                    scaled_pixmap = self.current_pixmap.scaled(available_width, available_height, Qt.IgnoreAspectRatio, Qt.SmoothTransformation)
                    self.mainWindow.homeBackgroundImage.setPixmap(scaled_pixmap)
                    QApplication.processEvents()
        else:
            if self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage'):
                available_width = self.mainWindow.width() - 50
                available_height = self.mainWindow.height()
                blank_pixmap = QPixmap(available_width, available_height)
                blank_pixmap.fill(Qt.transparent)
                self.mainWindow.originalPixmap = blank_pixmap
                self.mainWindow.homeBackgroundImage.setPixmap(blank_pixmap)
                self.mainWindow.homeBackgroundImage.setMinimumSize(available_width, available_height)
                QApplication.processEvents()
    
    def __saveWallpaper(self):
        """ 另存壁纸 """
        logger.info("开始另存壁纸")
        if self.current_pixmap is None:
            logger.warning("没有可保存的壁纸")
            InfoBar.warning(
                "提示",
                "请先获取壁纸",
                duration=5000,
                parent=self
            )
            return
        
        file_path, _ = QFileDialog.getSaveFileName(
            self, 
            "另存壁纸", 
            os.path.join(BASE_DIR, "wallpaper"), 
            "JPEG图片 (*.jpg);;PNG图片 (*.png)"
        )
        
        if file_path:
            logger.info(f"用户选择保存路径: {file_path}")
            try:
                self.current_pixmap.save(file_path)
                logger.info(f"壁纸已成功保存到: {file_path}")
                InfoBar.success(
                    "成功",
                    f"壁纸已保存到: {file_path}",
                    duration=5000,
                    parent=self
                )
            except Exception as e:
                logger.error(f"保存壁纸失败: {str(e)}")
                InfoBar.error(
                    "错误",
                    f"保存壁纸失败: {str(e)}",
                    duration=5000,
                    parent=self
                )
    
    def __selectWallpaper(self):
        """ 手动选择壁纸 """
        logger.info("开始手动选择壁纸")
        file_path, _ = QFileDialog.getOpenFileName(
            self, 
            "选择壁纸", 
            os.path.join(BASE_DIR, "wallpaper"), 
            "图片文件 (*.jpg *.jpeg *.png *.bmp *.gif)"
        )
        
        if file_path:
            logger.info(f"用户选择壁纸路径: {file_path}")
            try:
                self.current_pixmap = QPixmap(file_path)
                self.current_wallpaper_path = file_path
                if not self.current_pixmap.isNull():
                    logger.info("壁纸加载成功，更新界面显示")
                    self.imageLabel.setPixmap(self.current_pixmap)
                    # 更新主界面的背景照片
                    if self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage'):
                        self.mainWindow.originalPixmap = self.current_pixmap
                        available_width = self.mainWindow.width() - 50
                        available_height = self.mainWindow.height()
                        scaled_pixmap = self.current_pixmap.scaled(available_width, available_height, Qt.IgnoreAspectRatio, Qt.SmoothTransformation)
                        self.mainWindow.homeBackgroundImage.setPixmap(scaled_pixmap)
                        QApplication.processEvents()
                        logger.info("主界面背景已更新")
                
                InfoBar.success(
                    "成功",
                    f"已选择壁纸：{file_path}",
                    duration=5000,
                    parent=self
                )
            except Exception as e:
                logger.error(f"选择壁纸失败: {str(e)}")
                InfoBar.error(
                    "错误",
                    f"选择壁纸失败: {str(e)}",
                    duration=5000,
                    parent=self
                )
    
    def __manageWallpaperLimit(self, wallpaper_dir, save_limit):
        """ 管理壁纸保存量，超过限制时删除最旧的壁纸 """
        wallpapers = []
        for file in os.listdir(wallpaper_dir):
            if file.endswith('.jpg') and file.startswith('wallpaper_'):
                file_path = os.path.join(wallpaper_dir, file)
                mtime = os.path.getmtime(file_path)
                wallpapers.append((mtime, file_path))
        
        wallpapers.sort(key=lambda x: x[0])
        
        # 删除超过限制的最旧壁纸
        while len(wallpapers) > save_limit:
            _, file_path = wallpapers.pop(0)
            try:
                os.remove(file_path)
            except Exception:
                pass
    
    def __setWallpaper(self, show_notification=True):
        """ 设为桌面壁纸 """
        if self.current_wallpaper_path is None:
            logger.warning("没有可设置的壁纸")
            if show_notification:
                InfoBar.warning(
                    "提示",
                    "请先获取或选择壁纸",
                    duration=5000,
                    parent=self
                )
            return
        
        logger.info(f"设置壁纸路径: {self.current_wallpaper_path}")
        try:
            # 使用ctypes设置桌面壁纸
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
                InfoBar.success(
                    "成功",
                    "壁纸已设置为桌面背景",
                    duration=5000,
                    parent=self
                )
        except Exception as e:
            logger.error(f"设置壁纸失败: {str(e)}")
            if show_notification:
                InfoBar.error(
                    "错误",
                    f"设置壁纸失败: {str(e)}",
                    duration=5000,
                    parent=self
                )



