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
ClassLively 主程序
"""

from PyQt5.QtCore import (
    Q_ARG, QDate, QLocale, QMetaObject, QEvent,
    QPropertyAnimation, QRect, Qt, QTime, QTranslator, QUrl, QTimer, pyqtSlot
)
from PyQt5.QtGui import (
    QFont, QFontDatabase, QColor, QIcon, QImage, QPainter, QPalette, QPixmap
)
from PyQt5.QtWidgets import (
    QApplication, QFileDialog, QFrame, QGridLayout, QHBoxLayout, 
    QLabel, QLineEdit, QListWidget, QListWidgetItem, QMessageBox, QMenu, 
    QPlainTextEdit, QPushButton, QSizePolicy, QSpacerItem, QStackedLayout, 
    QSystemTrayIcon, QVBoxLayout, QWidget, QGraphicsBlurEffect
)
from qfluentwidgets import (
    Action, BodyLabel, CardWidget, CheckBox, ExpandLayout, FluentIcon as FIF, 
    FluentTranslator, FluentWindow, ImageLabel, InfoBar, isDarkTheme, LineEdit, 
    ListWidget, NavigationItemPosition, PrimaryPushButton, ProgressBar, ProgressRing, 
    PushButton, qconfig, RadioButton, RoundMenu, ScrollArea, SettingCardGroup, 
    setTheme, SmoothScrollArea, StrongBodyLabel, SwitchSettingCard, TextEdit, 
    Theme, MessageBox
)
from concurrent.futures import ThreadPoolExecutor, wait
import requests
import sys
import os
import platform
import ctypes
import json
import threading
import shutil
import datetime
import cnlunar
import winreg
import logging
import subprocess
import webbrowser
from core.config import cfg, get_default_config_dict
from core.logger import logger, setup_exception_hook
from core.constants import APP_NAME
from core.updater import (
    check_version_from_github,
    download_update,
    extract_update,
    create_update_script,
    get_changelog_from_github
)
from core.downloader import Downloader
from version import VERSION, BUILD_DATE

from services.weather import WeatherService
from services.poetry import PoetryService

from widgets.clock import ClockComponent
from widgets.weather import WeatherComponent
from widgets.poetry import PoetryComponent

from ui.settings import SettingInterface
from ui.city_selector import RegionDatabase
from ui.wallpaper import WallpaperInterface
from ui import (
    AboutInterface, DownloadInterface, EditPanel,
    UpdateInterface
)

from config.url_dir import url_dir  # type: ignore

def check_single_instance():
    """检查是否已经有实例"""
    config_path = os.path.join(BASE_DIR, 'config', 'config.json')
    allow_multiple = False
    
    if os.path.exists(config_path):
        try:
            with open(config_path, 'r', encoding='utf-8') as f:
                config = json.load(f)
            if 'Other' in config and 'AllowMultipleInstances' in config['Other']:
                allow_multiple = config['Other']['AllowMultipleInstances']
        except Exception:
            pass
    if not allow_multiple:
        # 创建互斥体
        mutex_name = f"Global\\{APP_NAME}_Mutex"
        mutex = ctypes.windll.kernel32.CreateMutexW(None, False, mutex_name)
        if ctypes.windll.kernel32.GetLastError() == 183:
            return False
    return True

# 路径设置
if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
    MEIPASS_DIR = sys._MEIPASS
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    MEIPASS_DIR = None

def get_resource_path(relative_path):
    """获取绝对路径"""
    base_path = os.path.join(BASE_DIR, relative_path)
    if os.path.exists(base_path):
        return base_path
    if MEIPASS_DIR:
        meipass_path = os.path.join(MEIPASS_DIR, relative_path)
        if os.path.exists(meipass_path):
            return meipass_path
    return base_path

def extract_bundled_files():
    """从打包文件中提取必要的文件夹和文件"""
    if not getattr(sys, 'frozen', False) or not MEIPASS_DIR:
        return
    
    bundled_folders = ['resource', 'font', 'data']
    
    for folder in bundled_folders:
        src_folder = os.path.join(MEIPASS_DIR, folder)
        dst_folder = os.path.join(BASE_DIR, folder)
        
        if not os.path.exists(src_folder):
            continue
        
        if not os.path.exists(dst_folder):
            try:
                shutil.copytree(src_folder, dst_folder)
                logger.info(f"已提取文件夹: {folder}")
            except Exception as e:
                logger.error(f"提取文件夹 {folder} 失败: {e}")
        else:
            for root, dirs, files in os.walk(src_folder):
                rel_path = os.path.relpath(root, src_folder)
                dst_root = os.path.join(dst_folder, rel_path) if rel_path != '.' else dst_folder
                
                if not os.path.exists(dst_root):
                    os.makedirs(dst_root, exist_ok=True)
                
                for file in files:
                    src_file = os.path.join(root, file)
                    dst_file = os.path.join(dst_root, file)
                    if not os.path.exists(dst_file):
                        try:
                            shutil.copy2(src_file, dst_file)
                            logger.info(f"已提取文件: {os.path.join(folder, rel_path, file)}")
                        except Exception as e:
                            logger.error(f"提取文件 {os.path.join(folder, rel_path, file)} 失败: {e}")

def get_resource_path(relative_path):
    """获取绝对路径"""
    # 先检查BASE_DIR中的资源文件
    base_path = os.path.join(BASE_DIR, relative_path)
    if os.path.exists(base_path):
        return base_path
    # 如果BASE_DIR中不存在，检查MEIPASS_DIR
    if MEIPASS_DIR:
        meipass_path = os.path.join(MEIPASS_DIR, relative_path)
        if os.path.exists(meipass_path):
            return meipass_path
    # 如果都不存在，返回BASE_DIR中的路径
    return base_path

def get_auto_start_status():
    """获取开机自启动状态"""
    try:
        key_path = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_READ)
        try:
            value, _ = winreg.QueryValueEx(key, APP_NAME)
            winreg.CloseKey(key)
            return True, value
        except FileNotFoundError:
            winreg.CloseKey(key)
            return False, None
    except Exception as e:
        logger.error(f"获取开机自启动状态失败: {e}")
        return False, None


def set_auto_start(enabled, delay_seconds=5):
    """设置开机自启动"""
    try:
        key_path = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
        
        # 确保注册表键存在
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE)
        except FileNotFoundError:
            # 如果键不存在，创建它
            key = winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path)
        
        if enabled:
            if getattr(sys, 'frozen', False):
                # 打包为exe时
                exe_path = sys.executable
                if delay_seconds > 0:
                    command = f'cmd /c "timeout /t {delay_seconds} /nobreak >nul && start \"\" \"{exe_path}\" --autostart"'
                else:
                    command = f'"{exe_path}" --autostart'
                logger.info(f"准备设置exe开机自启动: {exe_path}, 延迟: {delay_seconds}秒")
            else:
                # 直接运行py文件时
                python_exe = sys.executable
                script_path = os.path.abspath(__file__)
                if delay_seconds > 0:
                    command = f'cmd /c "timeout /t {delay_seconds} /nobreak >nul && start \"\" \"{python_exe}\" \"{script_path}\" --autostart"'
                else:
                    command = f'"{python_exe}" "{script_path}" --autostart'
                logger.info(f"准备设置py开机自启动: {python_exe} {script_path}, 延迟: {delay_seconds}秒")
            
            winreg.SetValueEx(key, APP_NAME, 0, winreg.REG_SZ, command)
            success, stored_value = get_auto_start_status()
            if success and stored_value == command:
                logger.info(f"已成功设置开机自启动，延迟: {delay_seconds}秒")
                return True
            else:
                logger.error("设置开机自启动后验证失败")
                return False
                
        else:
            try:
                winreg.DeleteValue(key, APP_NAME)
                logger.info("已取消开机自启动")
            except FileNotFoundError:
                logger.info("开机自启动项不存在")
            winreg.CloseKey(key)
            success, _ = get_auto_start_status()
            if not success:
                logger.info("已确认开机自启动项已删除")
                return True
            else:
                logger.error("删除开机自启动项后验证失败")
                return False
                
    except PermissionError as e:
        logger.error(f"设置开机自启动失败 - 权限不足: {e}")
        return False
    except Exception as e:
        logger.error(f"设置开机自启动失败: {e}")
        return False


def sync_auto_start_with_config():
    try:
        config_auto_start = cfg.autoStart.value
        actual_auto_start, _ = get_auto_start_status()
        
        logger.info(f"同步自启动状态 - 配置: {config_auto_start}, 实际: {actual_auto_start}")
        
        if config_auto_start != actual_auto_start:
            logger.info(f"自启动状态不一致，正在同步...")
            result = set_auto_start(config_auto_start)
            if result:
                logger.info("自启动状态同步成功")
            else:
                logger.error("自启动状态同步失败")
                # 如果设置失败，更新配置匹配实际
                if actual_auto_start != config_auto_start:
                    cfg.autoStart.value = actual_auto_start
                    logger.info(f"已将配置更新为与实际状态一致: {actual_auto_start}")
            return result
        else:
            return True
            
    except Exception as e:
        logger.error(f"同步自启动状态失败: {e}")
        return False

class MainWindow(FluentWindow):
    """ 主窗口 """

    def __init__(self):
        super().__init__()
        
        setTheme(cfg.themeMode.value)
        
        icon_path = get_resource_path(os.path.join("resource", "icons", "CY.png"))
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))
        else:
            logger.warning("窗口图标文件不存在")
        
        self.isEditMode = False
        
        self.initMainNavigation()
        
        self.settingInterface = SettingInterface(parent=self)
        self.settingInterface.setObjectName("setting")
        self.addSubInterface(self.settingInterface, FIF.SETTING, "设置", NavigationItemPosition.BOTTOM)
        
        self.updateInterface = UpdateInterface(parent=self)
        self.addSubInterface(self.updateInterface, FIF.SYNC, "更新", NavigationItemPosition.BOTTOM)
        
        if cfg.autoCheckUpdate.value:
            logger.info("自动检查更新已启用")
            self.updateInterface._UpdateInterface__checkUpdate(auto_check=True)
        
        self.aboutInterface = AboutInterface(parent=self)
        self.addSubInterface(self.aboutInterface, FIF.INFO, "关于", NavigationItemPosition.BOTTOM)
        
        self.initSettingsNavigation()
        
        self.setWindowTitle(APP_NAME)
        
        self.resize(1100, 700)
        self.setMinimumSize(1100, 700)
        self.moveToCenter()
        
        # 系统托盘
        self.initSystemTray()
        
        # 时钟更新定时器
        self.clockTimer = QTimer(self)
        self.clockTimer.timeout.connect(self.__updateClock)
        self.clockTimer.start(1000)
        cfg.showClock.valueChanged.connect(self.__updateClock)
        cfg.showClockSeconds.valueChanged.connect(self.__updateClock)
        cfg.showLunarCalendar.valueChanged.connect(self.__updateClock)
        cfg.clockColor.valueChanged.connect(self.updateClockStyle)
        cfg.clockSize.valueChanged.connect(self.updateClockStyle)
        cfg.dateSize.valueChanged.connect(self.updateClockStyle)
        cfg.poetrySize.valueChanged.connect(self.updateClockStyle)
        cfg.weatherSize.valueChanged.connect(self.updateClockStyle)
        cfg.weatherIconSize.valueChanged.connect(self.__updateWeatherIcon)
        self.__updateClock()
        
        # 诗词更新定时器
        self.poetryTimer = QTimer(self)
        self.poetryTimer.timeout.connect(self.__updatePoetry)
        cfg.showPoetry.valueChanged.connect(self.__updatePoetry)
        cfg.poetryApiUrl.valueChanged.connect(self.__updatePoetry)
        cfg.poetryUpdateInterval.valueChanged.connect(self.__updatePoetryInterval)
        cfg.showPoetry.valueChanged.connect(self.__updatePoetry)
        self.__updatePoetryInterval()
        
        # 天气更新定时器
        self.weatherTimer = QTimer(self)
        self.weatherTimer.timeout.connect(self.__updateWeather)
        cfg.weatherUpdateInterval.valueChanged.connect(self.__updateWeatherInterval)
        cfg.showWeather.valueChanged.connect(self.__updateWeather)
        
        # 初始更新天气
        self.__updateWeatherInterval()
        
        sync_auto_start_with_config()
        
        cfg.autoStart.valueChanged.connect(lambda value: set_auto_start(value))
        
        # 空闲检测定时器
        self.idleTimer = QTimer(self)
        self.idleTimer.timeout.connect(self.__checkIdle)
        self.lastMouseActivity = QTime.currentTime()
        self.lastKeyboardActivity = QTime.currentTime()
        self.lastPageOperation = QTime.currentTime()
        self.isMinimized = False
        self.idleCheckInterval = 10000  # 10 秒
        self.hasTriggeredAutoOpen = False
        self.isVideoPlaying = False
        cfg.autoOpenOnIdle.valueChanged.connect(self.__updateIdleTimer)
        cfg.idleMinutes.valueChanged.connect(self.__updateIdleTimer)
        self.__updateIdleTimer()
        self.__installGlobalHooks()
        
        cfg.themeMode.valueChanged.connect(self.__onThemeChanged)
        
        # 连接主题信号
        cfg.themeChanged.connect(self.updateInterface._onThemeChanged)
        cfg.themeChanged.connect(self.downloadInterface._onThemeChanged)
        cfg.themeChanged.connect(self.wallpaper._onThemeChanged)
        cfg.themeChanged.connect(self.aboutInterface._onThemeChanged)
        
        self.navigationInterface.installEventFilter(self)
        
        logger.info("主窗口初始化完成")
    
    def eventFilter(self, obj, event):
        """ 拦截导航切换"""
        
        if hasattr(self, 'isEditMode') and self.isEditMode:
            if event.type() == QEvent.MouseButtonRelease:
                nav_interface = getattr(self, 'navigationInterface', None)
                if nav_interface and obj == nav_interface:
                    return True
        
        return super().eventFilter(obj, event)
    
    def __onThemeChanged(self):
        """主题变化时重新加载编辑面板样式"""
        if hasattr(self, 'editPanel') and self.editPanel:
            self.editPanel._updateTheme()
            self.editPanel.updateListItemColors()
    
    def initSystemTray(self):
        """ 初始化系统托盘 """
        icon_path = get_resource_path(os.path.join("resource", "icons", "CY.png"))
        if os.path.exists(icon_path):
            self.tray_icon = QSystemTrayIcon(QIcon(icon_path), self)
        else:
            self.tray_icon = QSystemTrayIcon(self)
        
        self.tray_menu = RoundMenu(APP_NAME, self)
        
        show_action = Action(FIF.HOME, "显示主窗口", self)
        show_action.triggered.connect(self.show)
        self.tray_menu.addAction(show_action)
        
        exit_action = Action(FIF.CLOSE, "退出", self)
        exit_action.triggered.connect(QApplication.quit)
        self.tray_menu.addAction(exit_action)
        
        self.tray_icon.setContextMenu(self.tray_menu)
        
        self.tray_icon.activated.connect(self.__onTrayIconActivated)
        
        self.tray_icon.show()
    
    def __onTrayIconActivated(self, reason):
        """ 托盘图标激活槽函数 """
        if reason == QSystemTrayIcon.DoubleClick:
            if self.isVisible():
                logger.info("双击托盘图标，隐藏主窗口")
                self.hide()
            else:
                logger.info("双击托盘图标，显示主窗口")
                self.show()
        elif reason == QSystemTrayIcon.Trigger:
            if self.isVisible():
                logger.info("单击托盘图标，隐藏主窗口")
                self.hide()
            else:
                logger.info("单击托盘图标，显示主窗口")
                self.show()
    
    def __updateIdleTimer(self):
        """ 更新空闲检测定时器 """
        self.idleTimer.stop()
        
        if cfg.autoOpenOnIdle.value:
            self.idleTimer.start(self.idleCheckInterval)
            logger.info(f"空闲检测已启用，检测间隔：{self.idleCheckInterval}ms")
        else:
            logger.info("空闲检测已禁用")
    
    def __checkIdle(self):
        """ 检查电脑是否空闲 """
        if not cfg.autoOpenOnIdle.value:
            self.hasTriggeredAutoOpen = False
            return
        
        if self.isVisible():
            self.lastMouseActivity = QTime.currentTime()
            self.hasTriggeredAutoOpen = False
            return
        
        # Windows API
        class LASTINPUTINFO(ctypes.Structure):
            _fields_ = [("cbSize", ctypes.c_uint),
                       ("dwTime", ctypes.c_uint)]
        
        try:
            if self.isVideoPlaying:
                return
            last_input = LASTINPUTINFO()
            last_input.cbSize = ctypes.sizeof(LASTINPUTINFO)
            ctypes.windll.user32.GetLastInputInfo(ctypes.byref(last_input))
            ticks = ctypes.windll.kernel32.GetTickCount()
            idle_time_ms = (ticks - last_input.dwTime)
            
            now = QTime.currentTime()
            page_operation_elapsed = self.lastPageOperation.msecsTo(now)
            is_recent_page_operation = page_operation_elapsed < 5000
            
            idle_minutes = cfg.idleMinutes.value
            idle_threshold = idle_minutes * 60 * 1000
            
            if idle_time_ms > idle_threshold and not self.hasTriggeredAutoOpen and not is_recent_page_operation:
                logger.info(f"检测到电脑空闲超过{idle_minutes}分钟，自动打开界面")
                self.__autoOpenFromMinimized()
                self.lastMouseActivity = QTime.currentTime()
                self.hasTriggeredAutoOpen = True
        except Exception as e:
            logger.error(f"检测空闲时间失败：{e}")
    
    def __autoOpenFromMinimized(self):
        """ 自动打开主界面 """
        logger.info("自动打开主界面")
        self.stackedWidget.setCurrentIndex(0)
        self.show()
        self.activateWindow()
        
        if cfg.autoOpenMaximize.value:
            logger.info("自动最大化窗口")
            self.showMaximized()
    
    def __installGlobalHooks(self):
        try:
            HOOKPROC = ctypes.CFUNCTYPE(ctypes.c_long, ctypes.c_int, ctypes.c_ulong, ctypes.c_ulong)
            
            self.keyboardProcWrapper = HOOKPROC(self.keyboardProc)
            self.mouseProcWrapper = HOOKPROC(self.mouseProc)
            self.keyboardHook = ctypes.windll.user32.SetWindowsHookExW(
                13,  # WH_KEYBOARD_LL
                self.keyboardProcWrapper,
                ctypes.windll.kernel32.GetModuleHandleW(None),
                0
            )
            
            self.mouseHook = ctypes.windll.user32.SetWindowsHookExW(
                14,  # WH_MOUSE_LL
                self.mouseProcWrapper,
                ctypes.windll.kernel32.GetModuleHandleW(None),
                0
            )
            
        except Exception as e:
            logger.error(f"全局钩子安装失败：{e}")
    
    def keyboardProc(self, nCode, wParam, lParam):
        """ 键盘钩子回调函数 """
        if nCode >= 0:
            # KBDLLHOOKSTRUCT结构体
            class KBDLLHOOKSTRUCT(ctypes.Structure):
                _fields_ = [
                    ("vkCode", ctypes.c_uint),
                    ("scanCode", ctypes.c_uint),
                    ("flags", ctypes.c_uint),
                    ("time", ctypes.c_uint),
                    ("dwExtraInfo", ctypes.c_ulong)
                ]
            
            # lParam
            kbd_struct = KBDLLHOOKSTRUCT.from_address(lParam)
            keyCode = kbd_struct.vkCode
            if keyCode in [33, 34]:  # PageUp/PageDown
                self.lastPageOperation = QTime.currentTime()
                logger.debug("检测到翻页操作")
        
        return ctypes.windll.user32.CallNextHookEx(self.keyboardHook, nCode, wParam, lParam)
    
    def mouseProc(self, nCode, wParam, lParam):
        """ 鼠标钩子回调函数 """
        if nCode >= 0:
            if wParam == 0x020A:  # WM_MOUSEWHEEL
                self.lastPageOperation = QTime.currentTime()
                logger.debug("检测到鼠标滚轮操作")
        
        return ctypes.windll.user32.CallNextHookEx(self.mouseHook, nCode, wParam, lParam)
    
    def setVideoPlaying(self, playing):
        """ 设置视频播放状态 """
        self.isVideoPlaying = playing
        if playing:
            logger.debug("视频播放，暂停空闲检测")
        else:
            logger.debug("视频结束，恢复空闲检测")
    
    def show(self):
        """ 显示窗口 """
        super().show()
        
        if cfg.autoOpenOnIdle.value:
            self.idleTimer.stop()
            logger.debug("窗口显示，已停止空闲检测")
    
    def hide(self):
        """隐藏窗口"""
        logger.info("隐藏主窗口")
        self.hasTriggeredAutoOpen = False
        
        if cfg.autoOpenOnIdle.value:
            self.idleTimer.start(self.idleCheckInterval)
            logger.debug("窗口隐藏，已启动空闲检测")
        
        super().hide()
    
    def closeEvent(self, event):
        """关闭事件处理"""
        if cfg.closeAction.value == "minimize":
            logger.info("关闭行为：最小化到托盘")
            event.ignore()
            
            if cfg.autoOpenOnIdle.value:
                self.idleTimer.start(self.idleCheckInterval)
                logger.debug("应用最小化，已启动空闲检测")
            
            self.hide()
            self.tray_icon.showMessage(APP_NAME, "应用已最小化到系统托盘", QSystemTrayIcon.Information, 2000)
        else:
            logger.info("关闭行为：退出应用")
            
            
            if hasattr(self, 'keyboardHook') and self.keyboardHook:
                ctypes.windll.user32.UnhookWindowsHookEx(self.keyboardHook)
            if hasattr(self, 'mouseHook') and self.mouseHook:
                ctypes.windll.user32.UnhookWindowsHookEx(self.mouseHook)
            
            QApplication.quit()
    
    def __enterEditMode(self):
        """切换编辑模式（显示/隐藏右侧编辑面板）"""
        if not hasattr(self, 'editPanel'):
            try:
                self.__createEditPanel()
            except Exception:
                logger.exception('创建编辑面板失败')
                InfoBar.error('编辑模式', '无法创建编辑面板', parent=self, duration=3000)
                return

        if self.editPanel.isVisible():
            self.editPanel.hidePanel()
            self.isEditMode = False
            self.navigationInterface.setEnabled(True)
        else:
            self.editPanel.showPanel()
            self.isEditMode = True
            self.navigationInterface.setEnabled(False)
            self.__updateEditButtonPosition()
    
    def __updateEditButtonPosition(self):
        """更新编辑按钮位置"""
        if not hasattr(self, 'editPanel') or not hasattr(self, 'editLayout'):
            return
        
        while self.editLayout.count():
            item = self.editLayout.takeAt(0)
            if item.widget():
                item.widget().setParent(None)
        
        if self.editPanel.isLeftSide:
            self.editLayout.setAlignment(Qt.AlignBottom | Qt.AlignLeft)
            self.editLayout.setContentsMargins(20, 0, 0, 20)
        else:
            self.editLayout.setAlignment(Qt.AlignBottom | Qt.AlignRight)
            self.editLayout.setContentsMargins(0, 0, 20, 20)
        self.editLayout.addWidget(self.editButton)

    def __createEditPanel(self):
        if hasattr(self, 'editPanel') and self.editPanel is not None:
            return
        self.editPanel = EditPanel(self)
        pr = self.rect()
        
        if self.editPanel.isLeftSide:
            self.editPanel.setGeometry(-self.editPanel._width, 0, self.editPanel._width, pr.height())
        else:
            self.editPanel.setGeometry(pr.width(), 0, self.editPanel._width, pr.height())
        self.editPanel.hide()
        self.editPanel.setVisible(False)
        self.selectedComponent = None
        
        if not self.editPanelCreated:
            self.__updateEditButtonPosition()
            self.editPanelCreated = True

    def initMainNavigation(self):
        """ 初始化主界面导航 """
        home = QWidget()
        home.setObjectName("home")
        
        # 照片显示控件
        self.homeBackgroundImage = QLabel()
        self.homeBackgroundImage.setAlignment(Qt.AlignCenter)
        self.originalPixmap = QPixmap(1, 1)
        self.originalPixmap.fill(Qt.transparent)
        self.homeBackgroundImage.setPixmap(self.originalPixmap)
        self.homeBackgroundImage.setMinimumSize(100, 100)
        
        # 时钟和日期标签
        self.clockLabel = QLabel("00:00:00")
        self.clockLabel.setAlignment(Qt.AlignCenter)
        
        self.dateLabel = QLabel("")
        self.dateLabel.setAlignment(Qt.AlignCenter)
        
        # 天气温度标签
        self.weatherTempLabel = QLabel("")
        self.weatherTempLabel.setAlignment(Qt.AlignVCenter | Qt.AlignRight)
        self.weatherTempLabel.setStyleSheet("""
            color: #FFFFFF; 
            font-size: 14px; 
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        
        # 天气图标
        self.weatherIconLabel = QLabel("")
        self.weatherIconLabel.setAlignment(Qt.AlignTop | Qt.AlignRight)
        self.weatherIconLabel.setStyleSheet("background-color: transparent;")
        
        # 诗词标签
        self.poetryLabel = QLabel("")
        self.poetryLabel.setAlignment(Qt.AlignBottom | Qt.AlignHCenter)
        self.poetryLabel.setStyleSheet("""
            color: #FFFFFF; 
            font-size: 16px; 
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        self.poetryLabel.setWordWrap(False)
        self.updateClockStyle()
        
        # 时钟容器
        clockContainer = QWidget()
        clockLayout = QVBoxLayout(clockContainer)
        clockLayout.setAlignment(Qt.AlignTop)
        clockLayout.setContentsMargins(0, 100, 0, 0)
        clockLayout.setSpacing(0)
        clockLayout.addWidget(self.clockLabel)
        clockLayout.addWidget(self.dateLabel)
        clockContainer.setStyleSheet("background-color: transparent;")
        
        # 天气容器
        weatherContainer = QWidget()
        weatherLayout = QHBoxLayout(weatherContainer)
        weatherLayout.setAlignment(Qt.AlignTop | Qt.AlignRight)
        weatherLayout.setContentsMargins(0, 20, 20, 0)
        weatherLayout.setSpacing(10)
        self.weatherTempLabel.setAlignment(Qt.AlignCenter)
        self.weatherIconLabel.setAlignment(Qt.AlignCenter)
        weatherLayout.addWidget(self.weatherTempLabel)
        weatherLayout.addWidget(self.weatherIconLabel)
        weatherContainer.setStyleSheet("background-color: transparent;")
    
        # 诗词容器
        poetryContainer = QWidget()
        poetryLayout = QVBoxLayout(poetryContainer)
        poetryLayout.setAlignment(Qt.AlignBottom)
        poetryLayout.setContentsMargins(0, 0, 0, 20)  # 最后一个为底部向上预留
        poetryLayout.addWidget(self.poetryLabel)
        poetryContainer.setStyleSheet("background-color: transparent;")
        
        # 编辑按钮
        editContainer = QWidget()
        self.editLayout = QVBoxLayout(editContainer)
        self.editLayout.setAlignment(Qt.AlignBottom)
        self.editLayout.setContentsMargins(0, 0, 0, 20)
        
        self.editButton = PushButton("编辑", parent=home)
        self.editButton.setObjectName("editButton")
        self.editButton.setFixedSize(80, 32)
        self.editButton.clicked.connect(self.__enterEditMode)
        
        self.editLayout.addWidget(self.editButton)
        editContainer.setStyleSheet("background-color: transparent;")
        
        # 网格布局
        gridLayout = QGridLayout()
        gridLayout.setContentsMargins(0, 0, 0, 0)
        gridLayout.addWidget(self.homeBackgroundImage, 0, 0, 1, 1)
        gridLayout.addWidget(clockContainer, 0, 0, 1, 1)
        gridLayout.addWidget(weatherContainer, 0, 0, 1, 1)
        gridLayout.addWidget(poetryContainer, 0, 0, 1, 1)
        gridLayout.addWidget(editContainer, 0, 0, 1, 1)
        
        self.homeContent = QWidget()
        self.homeContent.setLayout(gridLayout)
        
        # 主界面布局
        homeLayout = QVBoxLayout(home)
        homeLayout.setAlignment(Qt.AlignCenter)
        homeLayout.setContentsMargins(0, 0, 0, 0)
        homeLayout.addWidget(self.homeContent)
        
        self.addSubInterface(home, FIF.HOME, "主界面")
        
        self.editPanelCreated = False
        
        self.wallpaper = WallpaperInterface(mainWindow=self)
        self.wallpaper.setObjectName("wallpaper")
        self.addSubInterface(self.wallpaper, FIF.PHOTO, "壁纸")
        
        self.downloadInterface = DownloadInterface(parent=self)
        self.addSubInterface(self.downloadInterface, FIF.DOWNLOAD, "软件下载")
        
        icon_path = get_resource_path(os.path.join('resource', 'icons', 'CY.png'))
        
        self.downloadInterface.addSection("常用软件")
        self.downloadInterface.addSoftware(icon_path, "微信", "")
        self.downloadInterface.addSoftware(icon_path, "QQ", "")
        self.downloadInterface.addSoftware(icon_path, "UU 远程", "")
        self.downloadInterface.addSoftware(icon_path, "网易云音乐", "")
        self.downloadInterface.addSoftware(icon_path, "office2021", "")

        self.downloadInterface.addSection("希沃系列")
        self.downloadInterface.addSoftware(icon_path, "希沃白板 5", "")
        self.downloadInterface.addSoftware(icon_path, "剪辑师", "")
        self.downloadInterface.addSoftware(icon_path, "知识胶囊", "")
        self.downloadInterface.addSoftware(icon_path, "掌上看班", "")
        self.downloadInterface.addSoftware(icon_path, "希沃轻白板", "")
        self.downloadInterface.addSoftware(icon_path, "希沃智能笔", "")
        self.downloadInterface.addSoftware(icon_path, "希沃输入法", "")
        self.downloadInterface.addSoftware(icon_path, "希沃快传", "")
        self.downloadInterface.addSoftware(icon_path, "希沃管家", "")
        self.downloadInterface.addSoftware(icon_path, "希沃壁纸", "")
        self.downloadInterface.addSoftware(icon_path, "希沃集控", "")
        self.downloadInterface.addSoftware(icon_path, "希沃导播助手", "")
        self.downloadInterface.addSoftware(icon_path, "希沃视频展台", "")
        self.downloadInterface.addSoftware(icon_path, "希沃课堂助手", "")
        self.downloadInterface.addSoftware(icon_path, "希沃电脑助手", "")
        self.downloadInterface.addSoftware(icon_path, "希沃易课堂", "")
        self.downloadInterface.addSoftware(icon_path, "PPT 小工具", "")
        self.downloadInterface.addSoftware(icon_path, "希沃轻录播", "")
        self.downloadInterface.addSoftware(icon_path, "希沃物联校园", "")
        self.downloadInterface.addSoftware(icon_path, "远程互动课堂", "")
        self.downloadInterface.addSoftware(icon_path, "省平台登录插件", "")
        self.downloadInterface.addSoftware(icon_path, "希象传屏 [发送端]", "")
        self.downloadInterface.addSoftware(icon_path, "希沃品课 [小组端]", "")
        self.downloadInterface.addSoftware(icon_path, "希沃品课 [教师端]", "")
        
        self.downloadInterface.addSection("系统工具")
        self.downloadInterface.addSoftware(icon_path, "激活工具", "")
        
        self.downloadInterface.addSection("课表软件")
        self.downloadInterface.addSoftware(icon_path, "ClassIsland2", "")
        self.downloadInterface.addSoftware(icon_path, "ClassWidgets", "")
    
    def initSettingsNavigation(self):
        # 创建编辑面板
        try:
            self.__createEditPanel()
        except Exception:
            logger.exception('创建编辑面板失败')

    def resizeEvent(self, event):
        """ 窗口大小变化时调整图片大小 """
        super().resizeEvent(event)
        if hasattr(self, 'homeBackgroundImage') and self.homeBackgroundImage:
            try:
                available_width = self.width() - 50
                available_height = self.height()
                
                if hasattr(self, 'originalPixmap') and self.originalPixmap is not None and not self.originalPixmap.isNull():
                    scaled_pixmap = self.originalPixmap.scaled(available_width, available_height, Qt.IgnoreAspectRatio, Qt.SmoothTransformation)
                    
                    # 应用模糊效果
                    blur_effect = QGraphicsBlurEffect()
                    blur_radius = cfg.backgroundBlurRadius.value
                    blur_effect.setBlurRadius(blur_radius)
                    self.homeBackgroundImage.setGraphicsEffect(blur_effect)
                    
                    self.homeBackgroundImage.setPixmap(scaled_pixmap)
                else:
                    self.homeBackgroundImage.setMinimumSize(available_width, available_height)
                
                QApplication.processEvents()
            except Exception as e:
                logger.error(f"resizeEvent 错误：{e}")

    def moveToCenter(self):
        """ 移动窗口到屏幕中央 """
        desktop = QApplication.desktop().availableGeometry()
        w, h = desktop.width(), desktop.height()
        self.move(w//2 - self.width()//2, h//2 - self.height()//2)
    
    def __updateClock(self):
        """ 更新时钟显示 """
        if not cfg.showClock.value:
            self.clockLabel.hide()
            self.dateLabel.hide()
            return
        
        self.clockLabel.show()
        self.dateLabel.show()
        
        currentTime = QTime.currentTime()
        currentDate = QDate.currentDate()
        
        if cfg.showClockSeconds.value:
            timeString = currentTime.toString("HH:mm:ss")
        else:
            timeString = currentTime.toString("HH:mm")
        self.clockLabel.setText(timeString)
        
        # 公历日期
        solarString = currentDate.toString("yyyy 年 M 月 d 日 dddd")
        
        if cfg.showLunarCalendar.value:
            try:
                py_datetime = datetime.datetime(currentDate.year(), currentDate.month(), currentDate.day(), 0, 0, 0)
                lunar = cnlunar.Lunar(py_datetime)
                lunarMonthCn = lunar.lunarMonthCn
                lunarDayCn = lunar.lunarDayCn
                lunarMonthCn = lunarMonthCn.replace("大", "").replace("小", "")
                lunarString = f"{lunarMonthCn}{lunarDayCn}"
                dateString = f"{solarString} {lunarString}"
            except Exception as e:
                logging.error(f"农历显示错误：{e}")
                dateString = solarString
        else:
            dateString = solarString
        
        self.dateLabel.setText(dateString)
    
    def updateClockStyle(self):
        """ 更新时钟样式 """
        clock_color = cfg.clockColor.value
        color_str = clock_color.name() if hasattr(clock_color, 'name') else str(clock_color)
        
        clock_size = cfg.clockSize.value
        date_size = cfg.dateSize.value
        poetry_size = cfg.poetrySize.value
        weather_size = cfg.weatherSize.value
        
        self.clockLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {clock_size}px; 
            font-weight: bold; 
            font-family: "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        
        self.dateLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {date_size}px; 
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        
        self.poetryLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {poetry_size}px; 
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        
        self.weatherTempLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {weather_size}px; 
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
    
    def __updatePoetryInterval(self):
        """ 更新诗词更新间隔定时器 """
        self.poetryTimer.stop()
        
        interval_str = cfg.poetryUpdateInterval.value
        if interval_str == "从不":
            self.__updatePoetry()
            return
        elif interval_str == "10 分钟":
            interval = 10 * 60 * 1000
        elif interval_str == "30 分钟":
            interval = 30 * 60 * 1000
        elif interval_str == "1 小时":
            interval = 60 * 60 * 1000
        elif interval_str == "3 小时":
            interval = 3 * 60 * 60 * 1000
        elif interval_str == "6 小时":
            interval = 6 * 60 * 60 * 1000
        elif interval_str == "12 小时":
            interval = 12 * 60 * 60 * 1000
        elif interval_str == "1 天":
            interval = 24 * 60 * 60 * 1000
        else:
            interval = 60 * 60 * 1000
        
        self.poetryTimer.start(interval)
        self.__updatePoetry()
    
    def __updateWeatherInterval(self):
        """ 更新天气更新间隔定时器 """
        self.weatherTimer.stop()
        
        interval_str = cfg.weatherUpdateInterval.value
        if interval_str == "从不":
            self.__updateWeather()
            return
        elif interval_str == "15 分钟":
            interval = 15 * 60 * 1000
        elif interval_str == "30 分钟":
            interval = 30 * 60 * 1000
        elif interval_str == "1 小时":
            interval = 60 * 60 * 1000
        elif interval_str == "3 小时":
            interval = 3 * 60 * 60 * 1000
        elif interval_str == "6 小时":
            interval = 6 * 60 * 60 * 1000
        elif interval_str == "12 小时":
            interval = 12 * 60 * 60 * 1000
        elif interval_str == "24 小时":
            interval = 24 * 60 * 60 * 1000
        else:
            interval = 60 * 60 * 1000
        
        self.weatherTimer.start(interval)
        self.__updateWeather()
    

    
    def __updatePoetry(self):
        """ 更新诗词显示 """
        if not cfg.showPoetry.value:
            self.poetryLabel.hide()
            return
        
        self.poetryLabel.show()
        
        logger.debug("开始更新诗词")
        
        try:
            api_url = cfg.poetryApiUrl.value
            logger.debug(f"诗词 API URL: {api_url}")
            response = requests.get(api_url, timeout=10)
            
            if response.status_code == 200:
                logger.debug(f"诗词 API 请求成功，状态码：{response.status_code}")
                poetry_text = response.text.strip()
                self.poetryLabel.setText(poetry_text)
                logger.info(f"已更新诗词：{poetry_text[:50]}")
            else:
                logger.error(f"诗词 API 请求失败，状态码：{response.status_code}")
                self.poetryLabel.setText("他山之石，可以攻玉。——《诗经·小雅·鹤鸣》")
        except Exception as e:
            logger.error(f"诗词更新失败：{e}")
            self.poetryLabel.setText("他山之石，可以攻玉。——《诗经·小雅·鹤鸣》")
    
    def __updateWeather(self):
        """ 更新天气显示 """
        if not cfg.showWeather.value:
            self.weatherTempLabel.hide()
            self.weatherIconLabel.hide()
            return
        
        self.weatherTempLabel.show()
        self.weatherIconLabel.show()
        
        success = False
        try:
            city = cfg.city.value
            logger.info(f"正在更新天气，使用城市：{city}")
            
            city_db = RegionDatabase()
            city_code = city_db.get_code(city)
            
            if city_code:
                location_key = f"weathercn:{city_code}"
            else:
                location_key = "weathercn:101010100" 
                logger.warning(f"未找到城市 {city} 的代码，使用默认值")
            
            logger.info(f"城市 {city} 对应的 locationKey: {location_key}")
            
            api_url = f"https://weatherapi.market.xiaomi.com/wtr-v3/weather/all?locationKey={location_key}&latitude=39.9042&longitude=116.4074&appKey=weather20151024&sign=zUFJoAR2ZVrDy1vF3D07&isGlobal=false&locale=zh_cn"
            logger.info(f"天气 API 请求 URL: {api_url}")
            response = requests.get(api_url, timeout=10)
            
            if response.status_code == 200:
                data = response.json()
                logger.info(f"天气 API 响应数据：{json.dumps(data, ensure_ascii=False)}")
                if 'current' in data:
                    current = data['current']
                    
                    # 解析温度数据
                    temperature = current.get('temperature', {})
                    current_temp = temperature.get('value', 0)
                    temp_unit = temperature.get('unit', '°C')
                    
                    # 解析天气代码
                    weather_code = current.get('weather', 0)
                    try:
                        weather_code = int(weather_code)
                    except (ValueError, TypeError):
                        weather_code = 0
                        logger.warning(f"天气代码为空或无效: {weather_code}")
                    
                    max_temp = 0
                    min_temp = 0
                    if 'forecastDaily' in data and data['forecastDaily']:
                        temperature_data = data['forecastDaily'].get('temperature', {})
                        if temperature_data.get('status') == 0 and 'value' in temperature_data:
                            temp_values = temperature_data['value']
                            if temp_values:
                                first_day = temp_values[0]
                                max_temp = first_day.get('from', 0)
                                min_temp = first_day.get('to', 0)
                    
                    # 代码映射
                    weather_map = {
                        0: "晴",
                        1: "多云",
                        2: "阴",
                        3: "阵雨",
                        4: "雷阵雨",
                        5: "雷阵雨并伴有冰雹",
                        6: "雨夹雪",
                        7: "小雨",
                        8: "中雨",
                        9: "大雨",
                        10: "暴雨",
                        11: "大暴雨",
                        12: "特大暴雨",
                        13: "阵雪",
                        14: "小雪",
                        15: "中雪",
                        16: "大雪",
                        17: "暴雪",
                        18: "雾",
                        19: "冻雨",
                        20: "沙尘暴",
                        21: "小雨 - 中雨",
                        22: "中雨 - 大雨",
                        23: "大雨 - 暴雨",
                        24: "暴雨 - 大暴雨",
                        25: "大暴雨 - 特大暴雨",
                        26: "小雪 - 中雪",
                        27: "中雪 - 大雪",
                        28: "大雪 - 暴雪",
                        29: "浮尘",
                        30: "扬沙",
                        31: "强沙尘暴",
                        32: "飑",
                        33: "龙卷风",
                        34: "弱高吹雪",
                        35: "轻雾",
                        50: "晴",
                        51: "多云",
                        52: "阴",
                        53: "霾",
                        54: "小雨",
                        55: "中雨",
                        56: "大雨",
                        57: "暴雨",
                        58: "雷阵雨",
                        59: "冰雹",
                        60: "小雪",
                        61: "中雪",
                        62: "大雪",
                        63: "雾",
                        64: "霾",
                        65: "沙尘",
                        66: "大风",
                        67: "台风",
                        68: "暴雨",
                        69: "暴雪",
                        70: "雨夹雪",
                        71: "冻雨",
                        72: "雾凇",
                        73: "霜冻",
                        74: "沙尘暴",
                        75: "扬沙",
                        76: "浮尘",
                        77: "强沙尘暴",
                        99: "未知"
                    }
                    
                    weather = weather_map.get(weather_code, "未知")
                    logger.info(f"天气信息：{weather}，当前温度：{current_temp}{temp_unit}，最高温度：{max_temp}{temp_unit}，最低温度：{min_temp}{temp_unit}，天气代码：{weather_code}")
                    
                    weather_text = f"{current_temp}{temp_unit}"
                    self.weatherTempLabel.setText(weather_text)
                    logger.info(f"已更新天气标签：{weather_text}")
                    
                    self.current_weather_code = weather_code
                    
                    self.__updateWeatherIcon()
                    logger.info(f"已更新天气图标：天气代码={weather_code}, 天气状况={weather}")
                    success = True
            else:
                logger.error(f"天气 API 请求失败，状态码：{response.status_code}，响应内容：{response.text}")
        except Exception as e:
            logger.error(f"天气更新失败：{e}")
        
        if not success:
            self.weatherTempLabel.setText("? °C")
            self.current_weather_code = None
            self.weatherIconLabel.clear()
    
    def __updateWeatherIcon(self):
        """ 更新天气图标 """
        try:
            if not hasattr(self, 'current_weather_code') or self.current_weather_code is None:
                return
            
            # 代码到图标文件的映射
            icon_map = {
                0: "0.svg",      # 晴
                1: "1.svg",      # 多云
                2: "2.svg",      # 阴
                3: "7.svg",      # 阵雨
                4: "4.svg",      # 雷阵雨
                5: "5.svg",      # 雷阵雨并伴有冰雹
                6: "19.svg",     # 雨夹雪
                7: "7.svg",      # 小雨
                8: "8.svg",      # 中雨
                9: "9.svg",      # 大雨
                10: "10.svg",    # 暴雨
                11: "11.svg",    # 大暴雨
                12: "11.svg",    # 特大暴雨
                13: "14.svg",    # 阵雪
                14: "14.svg",    # 小雪
                15: "15.svg",    # 中雪
                16: "16.svg",    # 大雪
                17: "17.svg",    # 暴雪
                18: "18.svg",    # 雾
                19: "19.svg",    # 冻雨
                20: "20.svg",    # 沙尘暴
                21: "7.svg",     # 小雨 - 中雨
                22: "8.svg",     # 中雨 - 大雨
                23: "9.svg",     # 大雨 - 暴雨
                24: "10.svg",    # 暴雨 - 大暴雨
                25: "11.svg",    # 大暴雨 - 特大暴雨
                26: "14.svg",    # 小雪 - 中雪
                27: "15.svg",    # 中雪 - 大雪
                28: "16.svg",    # 大雪 - 暴雪
                29: "18.svg",    # 浮尘
                30: "20.svg",    # 扬沙
                31: "20.svg",    # 强沙尘暴
                32: "3.svg",     # 飑
                33: "3.svg",     # 龙卷风
                34: "16.svg",    # 弱高吹雪
                35: "18.svg",    # 轻雾
                50: "0.svg",     # 晴
                51: "1.svg",     # 多云
                52: "2.svg",     # 阴
                53: "18.svg",    # 霾
                54: "7.svg",     # 小雨
                55: "8.svg",     # 中雨
                56: "9.svg",     # 大雨
                57: "10.svg",    # 暴雨
                58: "4.svg",     # 雷阵雨
                59: "5.svg",     # 冰雹
                60: "14.svg",    # 小雪
                61: "15.svg",    # 中雪
                62: "16.svg",    # 大雪
                63: "18.svg",    # 雾
                64: "18.svg",    # 霾
                65: "18.svg",    # 沙尘
                66: "3.svg",     # 大风
                67: "3.svg",     # 台风
                68: "11.svg",    # 暴雨
                69: "17.svg",    # 暴雪
                70: "19.svg",    # 雨夹雪
                71: "19.svg",    # 冻雨
                72: "18.svg",    # 雾凇
                73: "18.svg",    # 霜冻
                74: "20.svg",    # 沙尘暴
                75: "20.svg",    # 扬沙
                76: "18.svg",    # 浮尘
                77: "20.svg"     # 强沙尘暴
            }
            
            icon_file = icon_map.get(self.current_weather_code, "0.svg")
            icon_path = get_resource_path(os.path.join("resource", "icons", "weather", icon_file))
            if os.path.exists(icon_path):
                # QIcon 加载 SVG
                icon = QIcon(icon_path)
                icon_size = cfg.weatherIconSize.value
                # 创建一个 pixmap
                pixmap = icon.pixmap(icon_size, icon_size)
                self.weatherIconLabel.setPixmap(pixmap)
                logger.info(f"已设置天气图标：代码={self.current_weather_code}, 图标文件={icon_file}, 图标大小={icon_size}x{icon_size}")
            else:
                self.weatherIconLabel.setText("")
                logger.warning(f"天气图标文件不存在：{icon_file}")
        except Exception as e:
            logger.error(f"天气图标更新失败：{e}")

def install_fonts():
    """ 检查并安装鸿蒙字体到系统 """
    system_font_dir = os.path.join(os.environ['WINDIR'], 'Fonts')
    local_font_dir = get_resource_path(os.path.join("font", "HarmonyOS_Sans"))
    font_files = [
        "HarmonyOS_Sans_Thin.ttf",
        "HarmonyOS_Sans_Light.ttf",
        "HarmonyOS_Sans_Regular.ttf",
        "HarmonyOS_Sans_Medium.ttf",
        "HarmonyOS_Sans_Bold.ttf",
        "HarmonyOS_Sans_Black.ttf"
    ]

    fonts_installed = True
    for font_file in font_files:
        system_font_path = os.path.join(system_font_dir, font_file)
        if not os.path.exists(system_font_path):
            fonts_installed = False
            break

    if not fonts_installed:
        try:
            for font_file in font_files:
                local_font_path = os.path.join(local_font_dir, font_file)
                system_font_path = os.path.join(system_font_dir, font_file)
                if os.path.exists(local_font_path) and not os.path.exists(system_font_path):
                    shutil.copy2(local_font_path, system_font_path)
                    ctypes.windll.gdi32.AddFontResourceW(system_font_path)

            HWND_BROADCAST = 0xFFFF
            WM_FONTCHANGE = 0x001D
            SMTO_ABORTIFHUNG = 0x0002
            ctypes.windll.user32.SendMessageTimeoutW(
                HWND_BROADCAST, WM_FONTCHANGE, 0, 0, SMTO_ABORTIFHUNG, 1000, None
            )
        except Exception:
            pass

def is_auto_start_launch():
    """检查是否是通过开机自启动启动的"""
    return '--autostart' in sys.argv or '/autostart' in sys.argv


if __name__ == "__main__":
    auto_start_launch = is_auto_start_launch()
    if auto_start_launch:
        print("检测到通过开机自启动启动")
    
    QApplication.setHighDpiScaleFactorRoundingPolicy(Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)
    QApplication.setAttribute(Qt.AA_EnableHighDpiScaling)
    QApplication.setAttribute(Qt.AA_UseHighDpiPixmaps)
    
    extract_bundled_files()
    
    app = QApplication(sys.argv)
    
    # 加载翻译
    locale = QLocale(QLocale.Chinese, QLocale.China)
    fluentTranslator = FluentTranslator(locale)
    app.installTranslator(fluentTranslator)
    if not check_single_instance():
        
        temp_widget = QWidget()
        temp_widget.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
        temp_widget.setAttribute(Qt.WA_TranslucentBackground)
        
        desktop = QApplication.desktop()
        screen_rect = desktop.availableGeometry()
        temp_widget.setGeometry(screen_rect)
        temp_widget.show()
        
        title = f"{APP_NAME} 已有实例运行"
        content = f"检测到{APP_NAME} 已有一个实例在运行中，请勿重复启动。\n\n(您可在“设置”中启用“允许重复启动”，可能会有不可言喻的问题。)"
        w = MessageBox(title, content, temp_widget)
        w.yesButton.setText('取消')
        w.hideCancelButton()
        w.exec()
        sys.exit(0)
    
    install_fonts()

    config_path = os.path.join(BASE_DIR, 'config', 'config.json')

    config_dir = os.path.join(BASE_DIR, 'config')
    if not os.path.exists(config_dir):
        os.makedirs(config_dir)

    if os.path.exists(config_path):
        with open(config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
        config['QFluentWidgets'] = {'FontFamilies': ['HarmonyOS Sans SC']}
        with open(config_path, 'w', encoding='utf-8') as f:
            json.dump(config, f, ensure_ascii=False, indent=4)
    else:
        default_config = get_default_config_dict()
        with open(config_path, 'w', encoding='utf-8') as f:
            json.dump(default_config, f, ensure_ascii=False, indent=4)

    if hasattr(cfg.logLevel.value, 'value'):
        log_level_str = cfg.logLevel.value.value
    else:
        log_level_str = str(cfg.logLevel.value)
    
    logger.update_config(
        disable_log=cfg.disableLog.value,
        log_level=log_level_str,
        max_count=cfg.logMaxCount.value,
        max_days=cfg.logMaxDays.value
    )
    logger.info("ClassLively")

    theme_mode_str = str(cfg.themeMode.value) if not hasattr(cfg.themeMode.value, 'name') else cfg.themeMode.value.name
    theme_color = cfg.themeColor.value
    theme_color_str = theme_color.name() if hasattr(theme_color, 'name') else str(theme_color)
    dpi_scale = cfg.dpiScale.value
    dpi_scale_str = str(dpi_scale) if not hasattr(dpi_scale, 'value') else str(dpi_scale.value)
    language = cfg.language.value
    language_str = str(language) if not hasattr(language, 'name') else language.name
    logger.info(f"主窗口配置：主题模式={theme_mode_str}, 主题颜色={theme_color_str}, DPI 缩放={dpi_scale_str}, 语言={language_str}")
    logger.info(f"日志配置：禁用日志={cfg.disableLog.value}, 日志级别={log_level_str}, 最大条目数={cfg.logMaxCount.value}, 最大保留天数={cfg.logMaxDays.value}")
    logger.info(f"其他配置：关闭动作={cfg.closeAction.value}, 允许多实例={cfg.allowMultipleInstances.value}, 开发者模式={cfg.developerMode.value}, 自动启动={cfg.autoStart.value}")
    logger.info(f"下载配置：下载源={cfg.downloadSource.value}")
    logger.info(f"壁纸配置：保存限制={cfg.wallpaperSaveLimit.value}, 获取间隔={cfg.autoGetInterval.value}, 自动同步桌面={cfg.autoSyncToDesktop.value}, API={cfg.wallpaperApi.value}")
    logger.info(f"外观配置：背景模糊半径={cfg.backgroundBlurRadius.value}")
    logger.info(f"时间配置：显示秒={cfg.showClockSeconds.value}, 显示农历={cfg.showLunarCalendar.value}, 时钟颜色={cfg.clockColor.value.name() if hasattr(cfg.clockColor.value, 'name') else str(cfg.clockColor.value)}, 时钟大小={cfg.clockSize.value}, 日期大小={cfg.dateSize.value}")
    logger.info(f"诗词配置：显示诗词={cfg.showPoetry.value}, API 地址={cfg.poetryApiUrl.value}, 更新间隔={cfg.poetryUpdateInterval.value}, 字体大小={cfg.poetrySize.value}")
    logger.info(f"天气配置：字体大小={cfg.weatherSize.value}, 图标大小={cfg.weatherIconSize.value}, 更新间隔={cfg.weatherUpdateInterval.value}, 城市={cfg.city.value}")
    logger.info(f"自动配置：空闲自动打开={cfg.autoOpenOnIdle.value}, 空闲分钟={cfg.idleMinutes.value}, 自动打开最大化={cfg.autoOpenMaximize.value}, 自动检查更新={cfg.autoCheckUpdate.value}, 自动更新={cfg.autoUpdate.value}")
    logger.info(f"{APP_NAME}版本信息：")
    logger.info(f"版本号：{VERSION} 构建日期：{BUILD_DATE}")
    logger.info(f"{APP_NAME}环境信息：")
    logger.info(f"系统版本：Windows {platform.version()} Python 版本：{platform.python_version()}")
    logger.info(f"软件运行路径：{BASE_DIR}")
    logger.debug(f"url_dir 内容：{url_dir}")

    font_dir = get_resource_path(os.path.join("font", "HarmonyOS_Sans"))
    font_loaded = False
    
    if os.path.exists(font_dir):
        font_files = [
            "HarmonyOS_Sans_Thin.ttf",
            "HarmonyOS_Sans_Light.ttf",
            "HarmonyOS_Sans_Regular.ttf",
            "HarmonyOS_Sans_Medium.ttf",
            "HarmonyOS_Sans_Bold.ttf",
            "HarmonyOS_Sans_Black.ttf"
        ]
        
        for font_file in font_files:
            try:
                font_path = os.path.join(font_dir, font_file)
                if os.path.exists(font_path):
                    font_id = QFontDatabase.addApplicationFont(font_path)
                    if font_id != -1:
                        font_loaded = True
                        logger.debug(f"成功加载字体：{font_file}")
                    else:
                        logger.warning(f"字体加载失败：{font_file}")
            except Exception as e:
                logger.warning(f"加载字体 {font_file} 时发生错误：{e}")
    else:
        logger.warning(f"字体目录不存在：{font_dir}")
    
    if font_loaded:
        QApplication.setFont(QFont("HarmonyOS Sans SC", 10))
        logger.info("字体已设置为：HarmonyOS Sans SC")
    else:
        QApplication.setFont(QFont("Microsoft YaHei", 10))
        logger.info("HarmonyOS Sans SC加载失败，已使用备用字体：Microsoft YaHei")
    
    setup_exception_hook()

    window = MainWindow()
    
    if auto_start_launch:
        logger.info("开机自启动模式：最小化到系统托盘")
        window.hide()
        if hasattr(window, 'tray_icon') and window.tray_icon:
            window.tray_icon.show()
            logger.info("系统托盘图标已显示")
    else:
        window.show()
        logger.info("正常启动模式：显示主窗口")
    
    sys.exit(app.exec_())
