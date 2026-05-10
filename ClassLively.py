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

import atexit
import ctypes
import json
import logging
import os
import platform
import sys
import time
from concurrent.futures import ThreadPoolExecutor, wait

from PyQt6.QtCore import QEvent, QLocale, Qt, QTime, QTimer, QTranslator
from PyQt6.QtGui import QIcon, QPixmap
from PyQt6.QtWidgets import QApplication, QMessageBox, QSizePolicy, QSystemTrayIcon, QWidget
from qfluentwidgets import (
    Action,
    FluentIcon as FIF,
    FluentTranslator,
    FluentWindow,
    InfoBar,
    MessageBox,
    NavigationItemPosition,
    RoundMenu,
    setTheme,
)
from pycaw.pycaw import AudioUtilities

from core.config import cfg, save_cfg
from core.constants import APP_NAME, BASE_DIR, get_resPath
from core.downloader import clean_tempdir
from core.logger import logger, init_exhook
from core.updater import (
    create_update_script,
    download_update,
    extract_update,
    get_github_changelog,
    check_github_verison,
)
from core.utils import (
    verify_single_instance,
    release_single_instance,
    initialize_fonts,
    extract_files,
    sync_autostart_cfg,
    set_autostart,
    auto_start_launch,
)
from data.software_list import SOFTWARE_CATEGORIES, get_software_icon_path
from ui import AboutInterface, DownloadInterface, UpdateInterface, WizardWindow, check_wizard_needed, create_wizard_file
from ui.home import HomeInterface
from ui.debug import DebugPanel
from ui.settings import SettingInterface
from ui.wallpaper import WallpaperInterface
from ui.splash_screen import SplashScreen
from ui.edit_panel import EditPanel
from version import BUILD_DATE, VERSION
from data.url_dir import url_dir


class MainWindow(FluentWindow):
    """主窗口"""

    def __init__(self):
        super().__init__()

        setTheme(cfg.themeMode.value)

        icon_path = get_resPath(os.path.join("resource", "icons", "CY.png"))
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))
        else:
            logger.warning("窗口图标文件不存在")

        self.isEditMode = False

        self._initNavigation()

        self._normal_size = (1050, 750)
        self._is_maximized = False
        self._resize_timer = QTimer(self)
        self._resize_timer.setSingleShot(True)
        self._resize_timer.timeout.connect(self._checkWindowSize)
        self.resize(*self._normal_size)
        self.setMinimumSize(*self._normal_size)
        if not self._loadWindowPosition():
            self.moveToCenter()

        self.initSystemTray()

        sync_autostart_cfg()
        cfg.autoStart.valueChanged.connect(lambda value: set_autostart(value))

        self._initIdleDetection()
        self._initThemeConnections()

        self.navigationInterface.installEventFilter(self)

        logger.info("主窗口初始化完成")

    def _initNavigation(self):
        self.homeInterface = HomeInterface(self)
        self.homeInterface.setObjectName("home")
        self.addSubInterface(self.homeInterface, FIF.HOME, "主界面")

        self.wallpaper = WallpaperInterface(mainWindow=self)
        self.wallpaper.setObjectName("wallpaper")
        self.addSubInterface(self.wallpaper, FIF.PHOTO, "壁纸")

        self.downloadInterface = DownloadInterface(parent=self)
        self.addSubInterface(self.downloadInterface, FIF.DOWNLOAD, "软件下载")

        for category in SOFTWARE_CATEGORIES:
            self.downloadInterface.addSection(category["name"])
            for software in category["software"]:
                icon_path = get_software_icon_path(software["icon"])
                link = software.get("link")
                self.downloadInterface.addSoftware(icon_path, software["name"], software["description"], link)

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

        self.debugPanel = DebugPanel(self)
        self.developerNavItem = self.addSubInterface(self.debugPanel, FIF.DEVELOPER_TOOLS, "调试", NavigationItemPosition.BOTTOM)
        self.developerNavItem.setVisible(cfg.developerMode.value)
        cfg.developerMode.valueChanged.connect(self._onDeveloperModeChanged)

        self._initEditPanel()

    def _initEditPanel(self):
        try:
            self.editPanel = EditPanel(self)
            pr = self.rect()
            if self.editPanel.isLeftSide:
                self.editPanel.setGeometry(-self.editPanel._width, 0, self.editPanel._width, pr.height())
            else:
                self.editPanel.setGeometry(pr.width(), 0, self.editPanel._width, pr.height())
            self.editPanel.hide()
            self.editPanel.setVisible(False)
        except Exception:
            logger.exception('创建编辑面板失败')

    def _initIdleDetection(self):
        self.idleTimer = QTimer(self)
        self.idleTimer.timeout.connect(self._checkIdle)
        self.lastMouseActivity = QTime.currentTime()
        self.lastKeyboardActivity = QTime.currentTime()
        self.lastPageOperation = QTime.currentTime()
        self.isMinimized = False
        self.idleCheckInterval = 10000
        self.hasTriggeredAutoOpen = False
        self.isVideoPlaying = False
        self.maxMinimizeNotifications = 5
        cfg.autoOpenOnIdle.valueChanged.connect(self._updateIdleTimer)
        cfg.idleMinutes.valueChanged.connect(self._updateIdleTimer)
        self._updateIdleTimer()
        self._installGlobalHooks()

    def _initThemeConnections(self):
        cfg.themeMode.valueChanged.connect(self._onThemeChanged)
        cfg.themeChanged.connect(self.updateInterface._onThemeChanged)
        cfg.themeChanged.connect(self.downloadInterface._onThemeChanged)
        cfg.themeChanged.connect(self.wallpaper._onThemeChanged)
        cfg.themeChanged.connect(self.aboutInterface._onThemeChanged)
        cfg.themeChanged.connect(self._onDebugPanelThemeChanged)
        cfg.themeChanged.connect(self._onEditPanelThemeChanged)

    def _onDeveloperModeChanged(self, value):
        self.developerNavItem.setVisible(value)
        if not value and self.stackedWidget.currentWidget() == self.debugPanel:
            self.switchTo(self.homeInterface)

    def _onThemeChanged(self):
        if hasattr(self, 'editPanel') and self.editPanel:
            self.editPanel._updateTheme()

    def _onDebugPanelThemeChanged(self):
        if hasattr(self, 'debugPanel') and self.debugPanel:
            self.debugPanel._updateTheme()

    def _onEditPanelThemeChanged(self):
        if hasattr(self, 'editPanel') and self.editPanel:
            self.editPanel._updateTheme()

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_F12:
            if cfg.developerMode.value and hasattr(self, 'debugPanel'):
                self.switchTo(self.debugPanel)
            return
        if event.modifiers() == Qt.KeyboardModifier.ControlModifier and event.key() == Qt.Key.Key_E:
            self.homeInterface._enterEditMode()
            return
        if event.modifiers() == Qt.KeyboardModifier.ControlModifier and event.key() == Qt.Key.Key_D:
            if hasattr(self.homeInterface, '_draggable_widgets'):
                for i, widget in enumerate(self.homeInterface._draggable_widgets):
                    if widget:
                        pos = widget.getPositionPercent()
                        size = widget.size()
                        logger.debug(f"组件 {widget.component_id}: 位置=({pos[0]:.3f}, {pos[1]:.3f}), 大小={size.width()}x{size.height()}")
            return
        super().keyPressEvent(event)

    def eventFilter(self, obj, event):
        if hasattr(self, 'isEditMode') and self.isEditMode:
            if event.type() == QEvent.Type.MouseButtonRelease:
                nav_interface = getattr(self, 'navigationInterface', None)
                if nav_interface and obj == nav_interface:
                    return True
        return super().eventFilter(obj, event)

    def _checkWindowSize(self):
        if not hasattr(self, '_normal_size'):
            return
        is_maximized = self.isMaximized() or (self.windowState() & Qt.WindowState.WindowMaximized)
        if not is_maximized:
            if self.width() != self._normal_size[0] or self.height() != self._normal_size[1]:
                self.resize(*self._normal_size)

    def changeEvent(self, event):
        if event.type() == QEvent.Type.WindowStateChange:
            is_max = self.windowState() & Qt.WindowState.WindowMaximized
            if is_max and not self._is_maximized:
                self._is_maximized = True
                self.setMinimumSize(0, 0)
                self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
            elif not is_max and self._is_maximized:
                self._is_maximized = False
                self.setMinimumSize(*self._normal_size)
                self.resize(*self._normal_size)
        super().changeEvent(event)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if hasattr(self, '_normal_size') and hasattr(self, '_resize_timer'):
            self._resize_timer.start(50)

    def moveToCenter(self):
        screen = QApplication.primaryScreen()
        if screen:
            rect = screen.availableGeometry()
            w, h = rect.width(), rect.height()
            self.move(w // 2 - self.width() // 2, h // 2 - self.height() // 2)

    def initSystemTray(self):
        icon_path = get_resPath(os.path.join("resource", "icons", "CY.png"))
        if os.path.exists(icon_path):
            self.tray_icon = QSystemTrayIcon(QIcon(icon_path), self)
        else:
            self.tray_icon = QSystemTrayIcon(self)

        self.tray_menu = RoundMenu(APP_NAME, self)

        show_action = Action(FIF.HOME, "显示主窗口", self)
        show_action.triggered.connect(self.show)
        self.tray_menu.addAction(show_action)
        if cfg.developerMode.value:
            dev_action = Action(FIF.DEVELOPER_TOOLS, "调试", self)
            dev_action.triggered.connect(lambda: self.switchTo(self.debugPanel))
            self.tray_menu.addAction(dev_action)

        exit_action = Action(FIF.CLOSE, "退出", self)
        exit_action.triggered.connect(lambda: (release_single_instance(), QApplication.quit()))
        self.tray_menu.addAction(exit_action)

        self.tray_icon.setContextMenu(self.tray_menu)
        self.tray_icon.activated.connect(self._onTrayIconActivated)
        self.tray_icon.show()

    def _onTrayIconActivated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.DoubleClick:
            if self.isVisible():
                self.hide()
            else:
                self.show()
        elif reason == QSystemTrayIcon.ActivationReason.Trigger:
            if self.isVisible():
                self.hide()
            else:
                self.show()

    def _updateIdleTimer(self):
        self.idleTimer.stop()
        if cfg.autoOpenOnIdle.value:
            self.idleTimer.start(self.idleCheckInterval)
        else:
            logger.info("空闲检测已禁用")

    def _isMediaPlaying(self):
        try:
            sessions = AudioUtilities.GetAllSessions()
            for session in sessions:
                if session.State == 1:
                    proc = session.Process
                    if proc:
                        proc_name = proc.name().lower()
                        if any(browser in proc_name for browser in [
                            'chrome', 'msedge', 'firefox', 'brave', 'opera',
                            'vivaldi', 'iexplore', 'edge'
                        ]) or any(player in proc_name for player in [
                            'music', 'vlc', 'potplayer', 'spotify', 'netflix'
                        ]):
                            return True
            return False
        except Exception:
            return False

    def _checkIdle(self):
        if not cfg.autoOpenOnIdle.value:
            self.hasTriggeredAutoOpen = False
            return
        if self.isVisible():
            self.lastMouseActivity = QTime.currentTime()
            self.hasTriggeredAutoOpen = False
            return

        class LASTINPUTINFO(ctypes.Structure):
            _fields_ = [("cbSize", ctypes.c_uint), ("dwTime", ctypes.c_uint)]

        try:
            if self.isVideoPlaying:
                return
            last_input = LASTINPUTINFO()
            last_input.cbSize = ctypes.sizeof(LASTINPUTINFO)
            ctypes.windll.user32.GetLastInputInfo(ctypes.byref(last_input))
            ticks = ctypes.windll.kernel32.GetTickCount()
            idle_time_ms = ticks - last_input.dwTime

            now = QTime.currentTime()
            page_operation_elapsed = self.lastPageOperation.msecsTo(now)
            is_recent_page_operation = page_operation_elapsed < 5000

            idle_minutes = cfg.idleMinutes.value
            idle_threshold = idle_minutes * 60 * 1000

            if idle_time_ms > idle_threshold and not self.hasTriggeredAutoOpen and not is_recent_page_operation:
                if self._isMediaPlaying():
                    self.lastMouseActivity = QTime.currentTime()
                    return
                logger.info(f"检测到电脑空闲超过{idle_minutes}分钟，自动打开界面")
                self._autoOpenFromMinimized()
                self.lastMouseActivity = QTime.currentTime()
                self.hasTriggeredAutoOpen = True
        except Exception as e:
            logger.error(f"检测空闲时间失败：{e}")

    def _autoOpenFromMinimized(self):
        self.stackedWidget.setCurrentIndex(0)
        self.show()
        self.activateWindow()
        if cfg.autoOpenMaximize.value:
            self._is_maximized = True
            self.setMinimumSize(0, 0)
            self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
            super().showMaximized()

    def _installGlobalHooks(self):
        try:
            HOOKPROC = ctypes.CFUNCTYPE(ctypes.c_long, ctypes.c_int, ctypes.c_ulong, ctypes.c_ulong)
            self.keyboardProcWrapper = HOOKPROC(self._keyboardProc)
            self.mouseProcWrapper = HOOKPROC(self._mouseProc)
            self.keyboardHook = ctypes.windll.user32.SetWindowsHookExW(
                13, self.keyboardProcWrapper,
                ctypes.windll.kernel32.GetModuleHandleW(None), 0
            )
            self.mouseHook = ctypes.windll.user32.SetWindowsHookExW(
                14, self.mouseProcWrapper,
                ctypes.windll.kernel32.GetModuleHandleW(None), 0
            )
        except Exception as e:
            logger.error(f"全局钩子安装失败：{e}")

    def _keyboardProc(self, nCode, wParam, lParam):
        if nCode >= 0:
            class KBDLLHOOKSTRUCT(ctypes.Structure):
                _fields_ = [
                    ("vkCode", ctypes.c_uint),
                    ("scanCode", ctypes.c_uint),
                    ("flags", ctypes.c_uint),
                    ("time", ctypes.c_uint),
                    ("dwExtraInfo", ctypes.c_ulong)
                ]
            kbd_struct = KBDLLHOOKSTRUCT.from_address(lParam)
            keyCode = kbd_struct.vkCode
            if keyCode in [33, 34]:
                self.lastPageOperation = QTime.currentTime()
        return ctypes.windll.user32.CallNextHookEx(self.keyboardHook, nCode, wParam, lParam)

    def _mouseProc(self, nCode, wParam, lParam):
        if nCode >= 0:
            if wParam == 0x020A:
                self.lastPageOperation = QTime.currentTime()
        return ctypes.windll.user32.CallNextHookEx(self.mouseHook, nCode, wParam, lParam)

    def setVideoPlaying(self, playing):
        self.isVideoPlaying = playing

    def show(self):
        super().show()
        if cfg.autoOpenOnIdle.value:
            self.idleTimer.stop()

    def showMaximized(self):
        self._is_maximized = True
        self.setMinimumSize(0, 0)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        super().showMaximized()

    def showNormal(self):
        self._is_maximized = False
        self.setMinimumSize(*self._normal_size)
        self.resize(*self._normal_size)
        super().showNormal()

    def hide(self):
        self.hasTriggeredAutoOpen = False
        if cfg.autoOpenOnIdle.value:
            self.idleTimer.start(self.idleCheckInterval)
        super().hide()

    def closeEvent(self, event):
        if hasattr(self, 'homeInterface'):
            self.homeInterface.saveComponentPositions()

        if cfg.developerMode.value:
            event.accept()
            if hasattr(self, 'keyboardHook') and self.keyboardHook:
                ctypes.windll.user32.UnhookWindowsHookEx(self.keyboardHook)
            if hasattr(self, 'mouseHook') and self.mouseHook:
                ctypes.windll.user32.UnhookWindowsHookEx(self.mouseHook)
            release_single_instance()
            QApplication.quit()
            return

        if cfg.closeAction.value == "minimize":
            event.ignore()
            if cfg.autoOpenOnIdle.value:
                self.idleTimer.start(self.idleCheckInterval)
            self.hide()
            if cfg.minimizeNotificationCount.value < self.maxMinimizeNotifications:
                self.tray_icon.showMessage(APP_NAME, "应用已最小化到系统托盘", QSystemTrayIcon.MessageIcon.Information, 2000)
                cfg.minimizeNotificationCount.value = cfg.minimizeNotificationCount.value + 1
                save_cfg()
        else:
            if hasattr(self, 'keyboardHook') and self.keyboardHook:
                ctypes.windll.user32.UnhookWindowsHookEx(self.keyboardHook)
            if hasattr(self, 'mouseHook') and self.mouseHook:
                ctypes.windll.user32.UnhookWindowsHookEx(self.mouseHook)
            release_single_instance()
            QApplication.quit()

    def saveComponentPositions(self):
        if hasattr(self, 'homeInterface'):
            self.homeInterface.saveComponentPositions()

    def _loadWindowPosition(self):
        try:
            config_path = os.path.join(BASE_DIR, 'config', 'component_positions.json')
            if not os.path.exists(config_path):
                return False
            with open(config_path, 'r', encoding='utf-8') as f:
                positions = json.load(f)
            if "window" not in positions:
                return False
            window_pos = positions["window"]
            if window_pos.get("maximized", False):
                self._is_maximized = True
                self.setMinimumSize(0, 0)
                self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
                QTimer.singleShot(100, self.showMaximized)
                return True
            screen = QApplication.primaryScreen()
            if not screen:
                return False
            rect = screen.availableGeometry()
            x = int(window_pos["x"] * rect.width())
            y = int(window_pos["y"] * rect.height())
            self.move(x, y)
            return True
        except Exception as e:
            logger.error(f"加载窗口位置失败: {e}")
            return False


if __name__ == "__main__":
    _auto_start_launch = auto_start_launch()

    QApplication.setHighDpiScaleFactorRoundingPolicy(Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)

    if cfg.enableGpuAcceleration.value:
        QApplication.setAttribute(Qt.ApplicationAttribute.AA_UseOpenGLES)

    extract_files()

    app = QApplication(sys.argv)

    init_exhook()
    atexit.register(release_single_instance)

    if check_wizard_needed():
        create_wizard_file()
        wizard = WizardWindow()
        wizard.exec()

    icon_path = get_resPath(os.path.join("resource", "icons", "CY.png"))

    splash = SplashScreen(APP_NAME, VERSION, icon_path)
    splash.show()
    splash.setProgress(0)

    def allow_ui_update(duration=0.06):
        end = time.time() + duration
        while time.time() < end:
            app.processEvents()
            time.sleep(0.005)

    app.processEvents()
    executor = ThreadPoolExecutor(max_workers=1)

    def _background_init():
        try:
            splash.status_signal.emit("正在清理临时文件")
            splash.progress_signal.emit(10)
            clean_tempdir(logger=logger)
            splash.status_signal.emit("正在加载资源")
            splash.progress_signal.emit(70)
        except Exception as e:
            logger.exception(f"后台初始化失败: {e}")

    future = executor.submit(_background_init)

    splash.updateStatus("正在加载翻译")
    splash.setProgress(15)
    allow_ui_update(0.06)
    locale = QLocale(QLocale.Language.Chinese, QLocale.Country.China)
    fluentTranslator = FluentTranslator(locale)
    app.installTranslator(fluentTranslator)

    if not verify_single_instance():
        splash.close()
        temp_widget = QWidget()
        temp_widget.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        temp_widget.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        screen = QApplication.primaryScreen()
        if screen:
            screen_rect = screen.availableGeometry()
            temp_widget.setGeometry(screen_rect)
        temp_widget.show()
        title = f"{APP_NAME} 已有实例运行"
        content = f"检测到{APP_NAME} 已有一个实例在运行中，请勿重复启动。\n\n(您可在“设置”中启用“允许重复启动”，可能会有不可言喻的问题。)"
        w = MessageBox(title, content, temp_widget)
        w.yesButton.setText('取消')
        w.hideCancelButton()
        w.exec()
        sys.exit(0)

    splash.updateStatus("正在初始化字体")
    splash.setProgress(30)
    allow_ui_update(0.06)
    initialize_fonts(app, install_to_system=True)

    splash.updateStatus("正在配置日志")
    splash.setProgress(40)
    allow_ui_update(0.06)

    if hasattr(cfg.logLevel.value, 'value'):
        log_level_str = cfg.logLevel.value.value
    else:
        log_level_str = str(cfg.logLevel.value)

    if cfg.developerMode.value:
        log_max_count = 3
        log_max_days = 1
    else:
        log_max_count = cfg.logMaxCount.value
        log_max_days = cfg.logMaxDays.value

    logger.update_cfg(
        disable_log=cfg.disableLog.value,
        log_level=log_level_str,
        max_count=log_max_count,
        max_days=log_max_days
    )

    splash.updateStatus("正在加载配置")
    splash.setProgress(55)
    allow_ui_update(0.06)

    theme_mode_str = str(cfg.themeMode.value) if not hasattr(cfg.themeMode.value, 'name') else cfg.themeMode.value.name
    theme_color = cfg.themeColor.value
    theme_color_str = theme_color.name() if hasattr(theme_color, 'name') else str(theme_color)
    dpi_scale = cfg.dpiScale.value
    dpi_scale_str = str(dpi_scale) if not hasattr(dpi_scale, 'value') else str(dpi_scale.value)
    language = cfg.language.value
    language_str = str(language) if not hasattr(language, 'name') else language.name
    logger.info(f"主窗口配置：主题模式={theme_mode_str}, 主题颜色={theme_color_str}, DPI 缩放={dpi_scale_str}, 语言={language_str}")
    logger.info(f"日志配置：禁用日志={cfg.disableLog.value}, 日志级别={log_level_str}, 最大条目数={cfg.logMaxCount.value}, 最大保留天数={cfg.logMaxDays.value}")
    logger.info(f"其他配置：关闭动作={cfg.closeAction.value}, 允许多实例={cfg.allowMultipleInstances.value}, 调试模式={cfg.developerMode.value}, 自动启动={cfg.autoStart.value}")
    logger.info(f"下载配置：下载源={cfg.downloadSource.value}")
    logger.info(f"壁纸配置：保存限制={cfg.wallpaperSaveLimit.value}, 获取间隔={cfg.autoGetInterval.value}, 自动同步桌面={cfg.autoSyncToDesktop.value}, API={cfg.wallpaperApi.value}")
    logger.info(f"外观配置：背景模糊半径={cfg.backgroundBlurRadius.value}")
    logger.info(f"时间配置：显示秒={cfg.showClockSeconds.value}, 显示农历={cfg.showLunarCalendar.value}, 时钟大小={cfg.clockSize.value}, 日期大小={cfg.dateSize.value}")
    logger.info(f"一言配置：显示一言={cfg.showPoetry.value}, API 地址={cfg.poetryApiUrl.value}, 更新间隔={cfg.poetryUpdateInterval.value}")
    logger.info(f"天气配置：字体大小={cfg.weatherSize.value}, 图标大小={cfg.weatherIconSize.value}, 更新间隔={cfg.weatherUpdateInterval.value}, 城市={cfg.city.value}")
    logger.info(f"倒计时配置：启用={cfg.showCountdown.value}, 显示模式={cfg.countdownDisplayMode.value}, 轮播间隔={cfg.countdownCarouselInterval.value}秒")
    logger.info(f"学校信息配置：启用={cfg.showSchoolInfo.value}, 学校={cfg.school.value}, 班级={cfg.schoolClass.value}")
    logger.info(f"快捷启动栏配置：启用={cfg.showQuickLaunch.value}, 图标大小={cfg.quickLaunchIconSize.value}, 应用数量={len(cfg.quickLaunchApps.value)}")
    logger.info(f"自动配置：空闲自动打开={cfg.autoOpenOnIdle.value}, 空闲分钟={cfg.idleMinutes.value}, 自动检查更新={cfg.autoCheckUpdate.value}")
    logger.info(f"版本号：{VERSION} 构建日期：{BUILD_DATE}")
    logger.info(f"系统版本：Windows {platform.version()} Python 版本：{platform.python_version()}")
    logger.info(f"软件运行路径：{BASE_DIR}")

    wait_start = time.time()
    while not future.done():
        allow_ui_update(0.02)
        if time.time() - wait_start > 5.0:
            logger.warning("后台初始化超时，继续启动主窗口")
            break

    splash.updateStatus("正在创建主窗口...")
    splash.setProgress(70)
    splash.waitForProgress(70, timeout=1.0)
    allow_ui_update(0.12)
    window = MainWindow()

    splash.updateStatus("正在完成启动")
    splash.setProgress(90)
    allow_ui_update(0.06)

    time.sleep(0.06)
    splash.setProgress(100)
    splash.waitForProgress(100, timeout=1.0)
    allow_ui_update(0.06)
    time.sleep(0.04)
    splash.close()

    if _auto_start_launch:
        logger.info("开机自启动模式：最大化窗口")
        window.show()
        window.showMaximized()
        if hasattr(window, 'tray_icon') and window.tray_icon:
            window.tray_icon.show()
    else:
        window.show()
        logger.info("正常启动模式：显示主窗口")

    sys.exit(app.exec())
