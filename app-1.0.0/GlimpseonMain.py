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

import atexit
import ctypes
import darkdetect
import datetime
import json
import logging
import os
import platform
import sys
import time
from concurrent.futures import ThreadPoolExecutor, wait
import win32gui
import win32con

from PyQt6.QtCore import QEvent, QLocale, Qt, QThread, QTime, QTimer, QTranslator, pyqtSignal, pyqtSlot
from PyQt6.QtGui import QIcon, QPixmap, QFont
from PyQt6.QtWidgets import QApplication, QHBoxLayout, QLabel, QMessageBox, QSizePolicy, QSystemTrayIcon, QVBoxLayout, QWidget
from qfluentwidgets import (
    Action,
    BodyLabel,
    FluentTranslator,
    FluentWindow,
    InfoBar,
    MessageBox,
    NavigationItemPosition,
    ProgressBar,
    RoundMenu,
    setTheme,
    setThemeColor,
    StrongBodyLabel,
    Theme,
    isDarkTheme,
)
from qfluentwidgets.common.style_sheet import updateStyleSheet
from pycaw.pycaw import AudioUtilities

from core.config import cfg, save_cfg, Language
from core.constants import APP_NAME, APP_ICON, APP_DIR, BASE_DIR, DATA_CONFIG, WALLPAPER_DIR, get_resPath, load_qss, clear_qss_cache, ensure_data_dirs, VERSION
from core.downloader import cleanup_temp_directory
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
    tr,
    get_translation_manager,
    LanguageCode,
    TranslatableWidget,
    FUI,
)
from core.notification import NotificationManager
from resource.software_list import SOFTWARE_CATEGORIES, get_software_icon_path
from ui import AboutInterface, DownloadInterface, NotificationPage
from ui.home import HomeInterface
from ui.debug import DebugPanel
from ui.timetable import TimetablePage
from ui.wallpaper import WallpaperInterface
from resource.url_dir import url_dir

# Win32 MSG 结构体定义
if ctypes.sizeof(ctypes.c_void_p) == 8:
    _WPARAM = ctypes.c_uint64
    _LPARAM = ctypes.c_uint64
else:
    _WPARAM = ctypes.c_uint32
    _LPARAM = ctypes.c_uint32

class _MSG(ctypes.Structure):
    _fields_ = [
        ("hwnd", ctypes.c_void_p),
        ("message", ctypes.c_uint32),
        ("wParam", _WPARAM),
        ("lParam", _LPARAM),
        ("time", ctypes.c_uint32),
        ("pt_x", ctypes.c_long),
        ("pt_y", ctypes.c_long),
    ]

WIZARD_CONFIG_PATH = os.path.join(DATA_CONFIG, "Setup_Wizard.json")


def check_wizard_needed():
    wizard_path = WIZARD_CONFIG_PATH
    if not os.path.exists(wizard_path):
        return True
    try:
        with open(wizard_path, "r", encoding="utf-8") as f:
            data = json.load(f)
            return data.get("completed", 0) != 1
    except Exception:
        return True


def create_wizard_file():
    ensure_data_dirs()
    wizard_path = WIZARD_CONFIG_PATH
    os.makedirs(os.path.dirname(wizard_path), exist_ok=True)
    with open(wizard_path, "w", encoding="utf-8") as f:
        json.dump({"completed": 0}, f)


def complete_wizard():
    ensure_data_dirs()
    wizard_path = WIZARD_CONFIG_PATH
    os.makedirs(os.path.dirname(wizard_path), exist_ok=True)
    with open(wizard_path, "w", encoding="utf-8") as f:
        json.dump({"completed": 1}, f)


class SplashScreen(QWidget, TranslatableWidget):
    """启动窗口"""
    status_signal = pyqtSignal(str)
    progress_signal = pyqtSignal(int)

    def __init__(self, app_name: str, version: str, icon_path: str = None):
        super().__init__()
        self.app_name = app_name
        self.version = version
        self.icon_path = icon_path
        self.status_signal.connect(self.updateStatus)
        self.progress_signal.connect(self.setProgress)
        self._initUI()
        QTimer.singleShot(50, self._loadResourcesAsync)

    def _loadResourcesAsync(self):
        self._loadIcon()
        self._loadQss()

    def _initUI(self):
        """初始化 UI"""
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setFixedSize(360, 160)

        self.content_widget = QWidget(self)
        self.content_widget.setObjectName("contentWidget")
        self.content_widget.setGeometry(0, 0, 360, 160)

        self._updateBackgroundStyle()

        content_layout = QVBoxLayout(self.content_widget)
        content_layout.setContentsMargins(20, 15, 20, 10)
        content_layout.setSpacing(10)
        header_layout = QHBoxLayout()
        header_layout.setSpacing(15)
        self.icon_label = QLabel()
        self.icon_label.setFixedSize(64, 64)
        header_layout.addWidget(self.icon_label)

        title_layout = QHBoxLayout()
        title_layout.setSpacing(8)
        self.app_name_label = StrongBodyLabel(self.app_name)
        title_layout.addWidget(self.app_name_label)

        self.version_label = BodyLabel(self.version)
        self.version_label.setObjectName("versionLabel")
        title_layout.addWidget(self.version_label)
        title_layout.addStretch()

        header_layout.addLayout(title_layout)
        header_layout.addStretch()
        content_layout.addLayout(header_layout)
        self.status_label = BodyLabel(tr("splash.initializing"))  # 正在初始化...
        self.status_label.setObjectName("statusLabel")
        content_layout.addWidget(self.status_label)
        content_layout.addStretch(1)
        self.progress_bar = ProgressBar()
        self.progress_bar.setFixedHeight(4)
        self.progress_bar.setValue(0)
        self._current_progress = 0
        self._target_progress = 0
        self._anim_timer = QTimer(self)
        self._anim_timer.setInterval(8)
        self._anim_timer.timeout.connect(self._advance_progress)
        content_layout.addWidget(self.progress_bar)
        self.centerOnScreen()

    def _loadQss(self):
        """加载 QSS"""
        self.setStyleSheet(load_qss('app.qss'))

    def _updateBackgroundStyle(self):
        """更新背景"""
        bg_color = "rgba(32, 32, 32, 200)" if isDarkTheme() else "rgba(255, 255, 255, 200)"

        self.content_widget.setStyleSheet(f"""
            #contentWidget {{
                background-color: {bg_color};
                border-radius: 10px;
            }}
        """)

    def _loadIcon(self):
        """加载图标"""
        logger.info(f"[Splash] icon_path={self.icon_path}, exists={os.path.exists(self.icon_path) if self.icon_path else 'N/A'}")
        if self.icon_path and os.path.exists(self.icon_path):
            pixmap = QPixmap(self.icon_path)
            if pixmap.isNull():
                logger.warning(f"[Splash] 图标加载失败: {self.icon_path}")
            else:
                self.icon_label.setPixmap(pixmap.scaled(
                    64, 64, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation
                ))
        else:
            logger.warning(f"[Splash] 图标文件: {self.icon_path}")
            
    def centerOnScreen(self):
        """将窗口居中显示"""
        screen = QApplication.primaryScreen()
        if screen:
            screen_rect = screen.availableGeometry()
            x = (screen_rect.width() - self.width()) // 2
            y = (screen_rect.height() - self.height()) // 2
            self.move(x, y)

    @pyqtSlot(str)
    def updateStatus(self, status: str):
        """更新状态文本"""
        self.status_label.setText(status)

    @pyqtSlot(int)
    def setProgress(self, value: int):
        """设置0-100"""
        try:
            v = max(0, min(100, value))
        except Exception:
            return
        self._target_progress = v
        if not self._anim_timer.isActive():
            self._anim_timer.start()

    def _advance_progress(self):
        if self._current_progress < self._target_progress:
            step = max(1, (self._target_progress - self._current_progress) // 6)
            self._current_progress += step
            if self._current_progress > self._target_progress:
                self._current_progress = self._target_progress
            self.progress_bar.setValue(self._current_progress)
        else:
            self._anim_timer.stop()

    def waitForProgress(self, target: int = 100, timeout: float = 3.0):
        # try:
        # target = int(max(0, min(100, target)))
        # except Exception:
        #     target = 100
        end = time.time() + float(timeout)
        while time.time() < end and self._current_progress < target:
            QApplication.processEvents()
            time.sleep(0.003)

    def paintEvent(self, event):
        pass


# ==================== WizardWindow 向导窗口 ====================
import win32com.client
from pathlib import Path
from PyQt6.QtCore import QByteArray, QPropertyAnimation, QEasingCurve
from PyQt6.QtGui import QColor
from PyQt6.QtWidgets import QVBoxLayout, QDialog, QStackedWidget, QPushButton, QLineEdit, QGraphicsOpacityEffect
from qfluentwidgets import (
    BodyLabel,
    CheckBox,
    ComboBoxSettingCard,
    CustomColorSettingCard,
    PrimaryPushButton,
    PushButton,
    StrongBodyLabel,
    SwitchSettingCard,
    LineEdit,
    ToolButton,
)
from services.weather import RegionSelectorDialog
from ui.common import show_text_file


class WizardWindow(QDialog, TranslatableWidget):
    def __init__(self):
        super().__init__()
        self.setWindowTitle(tr("wizard.title"))  # Glimpseon 向导
        self.setFixedSize(840, 650)
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint)

        setTheme(cfg.themeMode.value)

        icon_path = get_resPath(APP_ICON)
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))

        self.mainLayout = QVBoxLayout(self)
        self.mainLayout.setContentsMargins(30, 30, 30, 30)
        self.mainLayout.setSpacing(16)

        self.stackedWidget = QStackedWidget(self)
        self.mainLayout.addWidget(self.stackedWidget)

        self.titleLabel = QLabel("Glimpseon", self)
        self.titleLabel.setObjectName("titleLabel")
        self.titleLabel.move(30, 10)

        self.closeButton = QPushButton(self)
        self.closeButton.setObjectName("closeButton")
        self.closeButton.setFixedSize(30, 30)
        self.closeButton.move(self.width() - 35, 5)
        self.closeButton.setText("×")
        self.closeButton.clicked.connect(self.close)

        self.__setQss()

        # 第 1 页：欢迎页面
        self.page1 = QWidget()
        self.page1Layout = QVBoxLayout(self.page1)
        self.page1Layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.page1Layout.setSpacing(16)

        self.iconLabel = QLabel(self.page1)
        self.iconLabel.setFixedSize(112, 112)
        self.iconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            self.iconLabel.setPixmap(pixmap.scaled(112, 112, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation))

        self.welcomeLabel = StrongBodyLabel(tr("wizard.welcome"), self.page1)  # Glimpseon
        self.welcomeLabel.setAlignment(Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft)
        self.welcomeLabel.setTextFormat(Qt.TextFormat.RichText)
        self.welcomeLabel.setText('<span style="font-family: HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif; font-weight:900; font-size:34px;">Glimpseon</span>')
        self.welcomeLabel.setObjectName("welcomeLabel")

        self.nextButton = PrimaryPushButton(FUI.RIGHT_ARROW, tr("wizard.next"), self.page1)  # 继续
        self.nextButton.setFixedHeight(36)

        self.headerLayout = QHBoxLayout()
        self.headerLayout.setSpacing(12)
        self.headerLayout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.headerLayout.addWidget(self.iconLabel)
        self.headerLayout.addWidget(self.welcomeLabel)

        self.page1Layout.addLayout(self.headerLayout)
        self.page1Layout.addWidget(self.nextButton, 0, Qt.AlignmentFlag.AlignCenter)

        # 第 2 页：软件使用协议
        self.page2 = QWidget()
        self.page2Layout = QVBoxLayout(self.page2)
        self.page2Layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignCenter)
        self.page2Layout.setSpacing(16)
        self.page2Layout.addSpacing(50)

        self.agreementTitle = StrongBodyLabel(tr("wizard.agreement_title"), self.page2)  # 软件使用协议
        self.agreementTitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title_font = self.agreementTitle.font()
        title_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        title_font.setPointSize(30)
        title_font.setBold(True)
        self.agreementTitle.setFont(title_font)

        self.agreementText = BodyLabel(tr("wizard.agreement_text"), self.page2)  # 在使用本软件前，请阅读并同意以下协议：
        self.agreementText.setAlignment(Qt.AlignmentFlag.AlignCenter)
        txt_font = self.agreementText.font()
        txt_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        txt_font.setPointSize(14)
        self.agreementText.setFont(txt_font)

        self.page2Layout.addWidget(self.agreementTitle, 0, Qt.AlignmentFlag.AlignCenter)
        self.page2Layout.addWidget(self.agreementText, 0, Qt.AlignmentFlag.AlignCenter)
        self.page2Layout.addSpacing(16)

        def _make_check_with_link(box_text, link_text, target_path):
            container = QWidget(self.page2)
            container.setFixedHeight(56)
            container.setFixedWidth(430)

            h = QHBoxLayout(container)
            h.setSpacing(8)
            h.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
            h.setContentsMargins(0, 8, 0, 8)

            chk = CheckBox("", self.page2)
            chk_font = chk.font()
            chk_font.setPointSize(16)
            chk.setFont(chk_font)
            chk.setFixedWidth(24)

            lbl = BodyLabel("", self.page2)
            lbl.setTextFormat(Qt.TextFormat.RichText)
            if target_path and os.path.exists(target_path):
                uri = Path(target_path).as_uri()
            else:
                uri = ""
            theme_color = cfg.themeColor.value.name()
            link_style = f'color:{theme_color}; text-decoration:underline;'
            lbl.setText(f'<span style="font-family:\'HarmonyOS Sans\',\'Microsoft YaHei\',\'SimHei\',sans-serif; font-size:16px;">{tr("wizard.agreement_check")}&nbsp;<a href="{uri}" style="{link_style}">{link_text}</a></span>')  # 我已阅读并同意
            lbl.setOpenExternalLinks(False)

            def _on_link_activated(url):
                if target_path and os.path.exists(target_path):
                    try:
                        show_text_file(link_text, f"{link_text}", target_path, parent=self.window())
                        return
                    except Exception:
                        try:
                            os.startfile(target_path)
                            return
                        except Exception:
                            pass

                msg = MessageBox(title=tr("wizard.exit_confirm_title"), content=tr("wizard.file_open_error", file=link_text), parent=self)  # 提示 / 无法打开协议文件：{file}
                msg.exec()

            lbl.linkActivated.connect(_on_link_activated)

            def _on_container_clicked():
                chk.setChecked(not chk.isChecked())

            container.mousePressEvent = lambda e: _on_container_clicked()

            h.addWidget(lbl, 0, Qt.AlignmentFlag.AlignLeft)
            h.addStretch(1)
            h.addWidget(chk, 0, Qt.AlignmentFlag.AlignRight)

            return chk, container

        license_path = os.path.join(BASE_DIR, "LICENSE")
        readme_path = os.path.join(BASE_DIR, "README.md")

        self.openSourceCheckBox, open_source_widget = _make_check_with_link(
            "", tr("wizard.open_source_license"), license_path)  # 项目开源协议 (GPL-3.0)
        self.userAgreementCheckBox, user_agree_widget = _make_check_with_link(
            "", tr("wizard.user_agreement"), readme_path)  # 用户协议
        self.privacyCheckBox, privacy_widget = _make_check_with_link(
            "", tr("wizard.privacy_policy"), "")  # 隐私政策

        self.agreeButton = PrimaryPushButton(FUI.ACCEPT, tr("wizard.agree"), self.page2)  # 完成
        self.agreeButton.setFixedHeight(36)
        self.agreeButton.setEnabled(False)

        checks_container = QWidget(self.page2)
        checks_container.setMaximumWidth(700)
        checks_layout = QVBoxLayout(checks_container)
        checks_layout.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop)
        checks_layout.setContentsMargins(0, 6, 0, 6)
        checks_layout.setSpacing(12)
        checks_layout.addWidget(open_source_widget)
        checks_layout.addWidget(user_agree_widget)
        checks_layout.addWidget(privacy_widget)
        self.page2Layout.addWidget(checks_container, 0, Qt.AlignmentFlag.AlignCenter)
        self.page2Layout.addSpacing(20)
        self.page2Layout.addWidget(self.agreeButton, 0, Qt.AlignmentFlag.AlignCenter)

        self.stackedWidget.addWidget(self.page1)
        self.stackedWidget.addWidget(self.page2)

        # 第 3 页：基本设置
        self.page3 = QWidget()
        self.page3Layout = QVBoxLayout(self.page3)
        self.page3Layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignCenter)
        self.page3Layout.setSpacing(16)
        self.page3Layout.addSpacing(50)

        self.settingsTitle = StrongBodyLabel(tr("wizard.settings_title"), self.page3)  # 基本设置
        self.settingsTitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        settings_title_font = self.settingsTitle.font()
        settings_title_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        settings_title_font.setPointSize(30)
        settings_title_font.setBold(True)
        self.settingsTitle.setFont(settings_title_font)

        self.settingsText = BodyLabel(tr("wizard.settings_text"), self.page3)  # 请选择您需要的功能选项：
        self.settingsText.setAlignment(Qt.AlignmentFlag.AlignCenter)
        settings_txt_font = self.settingsText.font()
        settings_txt_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        settings_txt_font.setPointSize(14)
        self.settingsText.setFont(settings_txt_font)

        self.page3Layout.addWidget(self.settingsTitle, 0, Qt.AlignmentFlag.AlignCenter)
        self.page3Layout.addWidget(self.settingsText, 0, Qt.AlignmentFlag.AlignCenter)
        self.page3Layout.addSpacing(16)

        settings_container = QWidget(self.page3)
        settings_container.setMaximumWidth(700)
        settings_layout = QVBoxLayout(settings_container)
        settings_layout.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop)
        settings_layout.setContentsMargins(0, 0, 0, 0)
        settings_layout.setSpacing(12)

        self.autoStartSwitch = self._createSwitchCard(
            FUI.PLAY,
            tr("wizard.auto_start"),
            tr("wizard.auto_start_desc"),
            cfg.autoStart.value
        )  # 开机自启动 / 设置应用在系统启动时自动运行
        settings_layout.addWidget(self.autoStartSwitch)

        self.autoOpenOnIdleSwitch = self._createSwitchCard(
            FUI.VIEW,
            tr("wizard.auto_open_idle"),
            tr("wizard.auto_open_idle_desc"),
            cfg.autoOpenOnIdle.value
        )  # 空闲时自动打开 / 电脑空闲时自动从最小化打开界面
        settings_layout.addWidget(self.autoOpenOnIdleSwitch)

        self.autoOpenMaximizeSwitch = self._createSwitchCard(
            FUI.FULL_SCREEN,
            tr("wizard.auto_open_maximize"),
            tr("wizard.auto_open_maximize_desc"),
            cfg.autoOpenMaximize.value
        )  # 自动打开时最大化 / 空闲自动打开界面时是否最大化窗口
        settings_layout.addWidget(self.autoOpenMaximizeSwitch)

        self.desktopShortcutSwitch = self._createSwitchCard(
            FUI.LINK,
            tr("wizard.desktop_shortcut"),
            tr("wizard.desktop_shortcut_desc"),
            False
        )  # 创建桌面快捷方式 / 在桌面创建应用程序快捷方式
        settings_layout.addWidget(self.desktopShortcutSwitch)

        self.page3Layout.addWidget(settings_container, 0, Qt.AlignmentFlag.AlignCenter)
        self.page3Layout.addSpacing(20)

        self.finishButton = PrimaryPushButton(FUI.ACCEPT, tr("wizard.finish"), self.page3)  # 完成
        self.finishButton.setFixedHeight(36)
        self.page3Layout.addWidget(self.finishButton, 0, Qt.AlignmentFlag.AlignCenter)

        self.stackedWidget.addWidget(self.page3)

        # 第 4 页：外观设置
        self.page4 = QWidget()
        self.page4Layout = QVBoxLayout(self.page4)
        self.page4Layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignCenter)
        self.page4Layout.setSpacing(16)
        self.page4Layout.addSpacing(50)

        self.appearanceTitle = StrongBodyLabel(tr("wizard.appearance_title"), self.page4)  # 外观设置
        self.appearanceTitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        appearance_title_font = self.appearanceTitle.font()
        appearance_title_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        appearance_title_font.setPointSize(30)
        appearance_title_font.setBold(True)
        self.appearanceTitle.setFont(appearance_title_font)

        self.appearanceText = BodyLabel(tr("wizard.appearance_text"), self.page4)  # 选择适合您的主题和颜色：
        self.appearanceText.setAlignment(Qt.AlignmentFlag.AlignCenter)
        appearance_txt_font = self.appearanceText.font()
        appearance_txt_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        appearance_txt_font.setPointSize(14)
        self.appearanceText.setFont(appearance_txt_font)

        self.page4Layout.addWidget(self.appearanceTitle, 0, Qt.AlignmentFlag.AlignCenter)
        self.page4Layout.addWidget(self.appearanceText, 0, Qt.AlignmentFlag.AlignCenter)
        self.page4Layout.addSpacing(16)

        appearance_container = QWidget(self.page4)
        appearance_container.setMaximumWidth(700)
        appearance_layout = QVBoxLayout(appearance_container)
        appearance_layout.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop)
        appearance_layout.setContentsMargins(0, 0, 0, 0)
        appearance_layout.setSpacing(12)

        self.themeCard = ComboBoxSettingCard(
            cfg.themeMode,
            FUI.BRUSH,
            tr("wizard.theme_mode"),
            tr("wizard.theme_mode_desc"),
            texts=[tr("wizard.theme_light"), tr("wizard.theme_dark"), tr("wizard.theme_system")],
            parent=self.page4
        )  # 应用颜色主题 / 更改应用程序的颜色外观 / 浅色 / 深色 / 使用系统设置
        self.themeCard.setFixedWidth(600)
        appearance_layout.addWidget(self.themeCard)

        self.themeColorCard = CustomColorSettingCard(
            cfg.themeColor,
            FUI.PALETTE,
            tr("wizard.primary_color"),
            tr("wizard.primary_color_desc"),
            parent=self.page4
        )  # 主要颜色 / 更改应用程序的主要颜色
        self.themeColorCard.setFixedWidth(600)
        appearance_layout.addWidget(self.themeColorCard)

        self.page4Layout.addWidget(appearance_container, 0, Qt.AlignmentFlag.AlignCenter)
        self.page4Layout.addSpacing(20)

        self.finishButton2 = PrimaryPushButton(FUI.ACCEPT, tr("wizard.finish"), self.page4)  # 完成
        self.finishButton2.setFixedHeight(36)
        self.page4Layout.addWidget(self.finishButton2, 0, Qt.AlignmentFlag.AlignCenter)

        self.stackedWidget.addWidget(self.page4)

        # 第 5 页：学校信息设置
        self.page5 = QWidget()
        self.page5Layout = QVBoxLayout(self.page5)
        self.page5Layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignCenter)
        self.page5Layout.setSpacing(16)
        self.page5Layout.addSpacing(50)

        self.schoolInfoTitle = StrongBodyLabel(tr("wizard.school_info_title"), self.page5)  # 学校信息设置
        self.schoolInfoTitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        school_info_title_font = self.schoolInfoTitle.font()
        school_info_title_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        school_info_title_font.setPointSize(30)
        school_info_title_font.setBold(True)
        self.schoolInfoTitle.setFont(school_info_title_font)

        self.schoolInfoText = BodyLabel(tr("wizard.school_info_text"), self.page5)  # 请输入您的学校和班级信息，以及选择天气城市：
        self.schoolInfoText.setAlignment(Qt.AlignmentFlag.AlignCenter)
        school_info_txt_font = self.schoolInfoText.font()
        school_info_txt_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        school_info_txt_font.setPointSize(14)
        self.schoolInfoText.setFont(school_info_txt_font)

        self.page5Layout.addWidget(self.schoolInfoTitle, 0, Qt.AlignmentFlag.AlignCenter)
        self.page5Layout.addWidget(self.schoolInfoText, 0, Qt.AlignmentFlag.AlignCenter)
        self.page5Layout.addSpacing(16)

        school_info_container = QWidget(self.page5)
        school_info_container.setMaximumWidth(700)
        school_info_layout = QVBoxLayout(school_info_container)
        school_info_layout.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop)
        school_info_layout.setContentsMargins(0, 0, 0, 0)
        school_info_layout.setSpacing(12)

        # 天气城市选择
        city_row = QWidget(self.page5)
        city_row.setFixedHeight(56)
        city_row_layout = QHBoxLayout(city_row)
        city_row_layout.setContentsMargins(16, 8, 16, 8)
        city_row_layout.setSpacing(12)

        city_label = BodyLabel(tr("wizard.weather_city"), self.page5)  # 天气城市
        city_label_font = city_label.font()
        city_label_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        city_label_font.setPointSize(city_label_font.pointSize() if city_label_font.pointSize() > 0 else 10)
        city_label.setFont(city_label_font)
        city_label.setFixedWidth(120)
        city_row_layout.addWidget(city_label)

        # self.cityDisplayButton = PushButton(cfg.city.value, self.page5)
        # self.cityDisplayButton.setFixedWidth(400)
        self.cityDisplayButton = PushButton(self.page5)
        _city = cfg.city.value
        if not _city or _city in ("点击选择", "Click to select", "點據選擇"):
            self.cityDisplayButton.setText(tr("component_settings.click_to_select"))
        else:
            self.cityDisplayButton.setText(_city)
        self.cityDisplayButton.clicked.connect(self._onCityButtonClicked)
        city_row_layout.addWidget(self.cityDisplayButton)
        city_row_layout.addStretch()

        school_info_layout.addWidget(city_row)

        # 学校输入
        school_row = QWidget(self.page5)
        school_row.setFixedHeight(56)
        school_row_layout = QHBoxLayout(school_row)
        school_row_layout.setContentsMargins(16, 8, 16, 8)
        school_row_layout.setSpacing(12)

        school_label = BodyLabel(tr("wizard.school_name"), self.page5)  # 学校名称
        school_label_font = school_label.font()
        school_label_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        school_label_font.setPointSize(school_label_font.pointSize() if school_label_font.pointSize() > 0 else 10)
        school_label.setFont(school_label_font)
        school_label.setFixedWidth(120)
        school_row_layout.addWidget(school_label)

        self.schoolLineEdit = LineEdit(self.page5)
        self.schoolLineEdit.setFixedWidth(400)
        self.schoolLineEdit.setPlaceholderText(tr("wizard.school_name_placeholder"))  # 请输入学校名称
        self.schoolLineEdit.setText(cfg.school.value)
        school_row_layout.addWidget(self.schoolLineEdit)
        school_row_layout.addStretch()

        school_info_layout.addWidget(school_row)

        # 班级输入
        class_row = QWidget(self.page5)
        class_row.setFixedHeight(56)
        class_row_layout = QHBoxLayout(class_row)
        class_row_layout.setContentsMargins(16, 8, 16, 8)
        class_row_layout.setSpacing(12)

        class_label = BodyLabel(tr("wizard.class_name"), self.page5)  # 班级
        class_label_font = class_label.font()
        class_label_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        class_label_font.setPointSize(class_label_font.pointSize() if class_label_font.pointSize() > 0 else 10)
        class_label.setFont(class_label_font)
        class_label.setFixedWidth(120)
        class_row_layout.addWidget(class_label)

        self.classLineEdit = LineEdit(self.page5)
        self.classLineEdit.setFixedWidth(400)
        self.classLineEdit.setPlaceholderText(tr("wizard.class_name_placeholder"))  # 请输入班级（如：1班、高二3班）
        self.classLineEdit.setText(cfg.schoolClass.value)
        class_row_layout.addWidget(self.classLineEdit)
        class_row_layout.addStretch()

        school_info_layout.addWidget(class_row)

        # 倒计时配置
        countdown_row = QWidget(self.page5)
        countdown_row.setFixedHeight(56)
        countdown_row_layout = QHBoxLayout(countdown_row)
        countdown_row_layout.setContentsMargins(16, 8, 16, 8)
        countdown_row_layout.setSpacing(12)

        countdown_label = BodyLabel(tr("wizard.countdown_config"), self.page5)  # 倒计时配置
        countdown_label_font = countdown_label.font()
        countdown_label_font.setFamily('HarmonyOS Sans, Microsoft YaHei, SimHei, sans-serif')
        countdown_label_font.setPointSize(countdown_label_font.pointSize() if countdown_label_font.pointSize() > 0 else 10)
        countdown_label.setFont(countdown_label_font)
        countdown_label.setFixedWidth(120)
        countdown_row_layout.addWidget(countdown_label)

        self.countdownConfigButton = ToolButton(FUI.ADD, self.page5)
        self.countdownConfigButton.setFixedSize(36, 36)
        self.countdownConfigButton.setToolTip(tr("wizard.add_countdown"))  # 添加倒计时
        self.countdownConfigButton.clicked.connect(self._onCountdownConfigClicked)
        countdown_row_layout.addWidget(self.countdownConfigButton)
        countdown_row_layout.addStretch()

        school_info_layout.addWidget(countdown_row)

        self.page5Layout.addWidget(school_info_container, 0, Qt.AlignmentFlag.AlignCenter)
        self.page5Layout.addSpacing(20)

        self.finishButton3 = PrimaryPushButton(FUI.ACCEPT, tr("wizard.finish"), self.page5)  # 完成
        self.finishButton3.setFixedHeight(36)
        self.page5Layout.addWidget(self.finishButton3, 0, Qt.AlignmentFlag.AlignCenter)

        self.stackedWidget.addWidget(self.page5)

        self.nextButton.clicked.connect(self._onNextClicked)
        self.agreeButton.clicked.connect(self._onAgreeClicked)
        self.openSourceCheckBox.stateChanged.connect(self._onCheckBoxChanged)
        self.userAgreementCheckBox.stateChanged.connect(self._onCheckBoxChanged)
        self.privacyCheckBox.stateChanged.connect(self._onCheckBoxChanged)
        self.finishButton.clicked.connect(self._onFinishClicked)
        self.finishButton2.clicked.connect(self._onFinishClicked2)
        self.finishButton3.clicked.connect(self._onFinishClicked3)
        self.themeColorCard.colorChanged.connect(self._onColorChanged)

    def resizeEvent(self, event):
        self.titleLabel.move(30, 10)
        self.closeButton.move(self.width() - 35, 5)
        super().resizeEvent(event)

    def closeEvent(self, event):
        msg_box = MessageBox(
            title=tr("wizard.exit_confirm_title"),
            content=tr("wizard.exit_confirm_content"),
            parent=self
        )  # 提示 / 向导未完成，确定要退出吗？
        if msg_box.exec():
            event.accept()
        else:
            event.ignore()

    def _onNextClicked(self):
        self._setCurrentIndexAnimated(1)

    def _setCurrentIndexAnimated(self, index, duration=300):
        effect = self.stackedWidget.graphicsEffect()
        if effect is None:
            effect = QGraphicsOpacityEffect(self.stackedWidget)
            effect.setOpacity(1.0)
            self.stackedWidget.setGraphicsEffect(effect)

        anim = QPropertyAnimation(effect, QByteArray(b"opacity"), self)
        anim.setDuration(max(50, duration // 2))
        anim.setStartValue(1.0)
        anim.setEndValue(0.0)
        anim.setEasingCurve(QEasingCurve.Type.InOutQuad)

        def _after_fade_out():
            self.stackedWidget.setCurrentIndex(index)
            anim2 = QPropertyAnimation(effect, QByteArray(b"opacity"), self)
            anim2.setDuration(max(50, duration // 2))
            anim2.setStartValue(0.0)
            anim2.setEndValue(1.0)
            anim2.setEasingCurve(QEasingCurve.Type.InOutQuad)
            self._current_animation = anim2
            anim2.start()

        anim.finished.connect(_after_fade_out)
        self._current_animation = anim
        anim.start()

    def _onCheckBoxChanged(self, state):
        all_checked = (self.openSourceCheckBox.isChecked() and
                      self.userAgreementCheckBox.isChecked() and
                      self.privacyCheckBox.isChecked())
        self.agreeButton.setEnabled(all_checked)

    def _onAgreeClicked(self):
        self._setCurrentIndexAnimated(2)

    def _onFinishClicked(self):
        cfg.autoStart.value = self.autoStartSwitch.isChecked()
        cfg.autoOpenOnIdle.value = self.autoOpenOnIdleSwitch.isChecked()
        cfg.autoOpenMaximize.value = self.autoOpenMaximizeSwitch.isChecked()

        if self.desktopShortcutSwitch.isChecked():
            self._createDesktopShortcut()

        self._setCurrentIndexAnimated(3)

    def _onFinishClicked2(self):
        self._setCurrentIndexAnimated(4)

    def _onFinishClicked3(self):
        """保存学校信息设置并完成向导"""
        from services.weather import RegionDatabase
        
        city = self.cityDisplayButton.text()
        cfg.city.value = city
        cfg.school.value = self.schoolLineEdit.text().strip()
        cfg.schoolClass.value = self.classLineEdit.text().strip()
        
        # 获取经纬度
        db = RegionDatabase()
        lon, lat = db.get_coordinates(city)
        if lon is not None and lat is not None:
            cfg.longitude.value = lon
            cfg.latitude.value = lat
            logger.info(f"向导：城市={city}, 经纬度=({lon}, {lat})")
        
        complete_wizard()
        self.accept()

    def _onCityButtonClicked(self):
        """打开城市选择对话框"""
        dialog = RegionSelectorDialog(self)
        if dialog.exec():
            selected_city = dialog.get_selected_region()
            if selected_city:
                self.cityDisplayButton.setText(selected_city)

    def _onCountdownConfigClicked(self):
        """打开倒计时添加对话框"""
        try:
            from ui.home import CountdownEditDialog
            dialog = CountdownEditDialog(self)
            if dialog.exec():
                countdown_data = dialog.get_countdown()
                if countdown_data:
                    countdown_list = cfg.countdownList.value or []
                    countdown_list.append(countdown_data)
                    cfg.countdownList.value = countdown_list
                    InfoBar.success(
                        title=tr("wizard.success_title"),
                        content=tr("wizard.countdown_added", title=countdown_data.get('title', '')),
                        parent=self,
                        duration=3000
                    )  # 成功 / 已添加倒计时：{title}
        except Exception as e:
            InfoBar.warning(
                title=tr("wizard.exit_confirm_title"),
                content=tr("wizard.countdown_add_failed", error=str(e)),
                parent=self,
                duration=5000
            )  # 提示 / 添加倒计时失败：{error}

    def _createSwitchCard(self, icon, title, content, default_value):
        """开关设置卡片"""
        card = SwitchSettingCard(icon, title, content, None, self.page3)
        card.setChecked(default_value)
        card.setFixedWidth(600)
        return card

    def _createDesktopShortcut(self):
        """创建桌面快捷方式"""
        try:

            desktop_path = os.path.join(os.path.expanduser("~"), "Desktop")
            shortcut_path = os.path.join(desktop_path, "Glimpseon.lnk")

            # if getattr(sys, 'frozen', False):
            #     exe_path = sys.executable
            # else:
            #     exe_path = sys.executable
            #     pass
            exe_path = sys.executable

            shell = win32com.client.Dispatch('WScript.Shell')
            shortcut = shell.CreateShortCut(shortcut_path)
            shortcut.Targetpath = exe_path
            if not getattr(sys, 'frozen', False):
                shortcut.Arguments = os.path.abspath(__file__)
            shortcut.WorkingDirectory = BASE_DIR
            shortcut.IconLocation = exe_path
            shortcut.save()

            InfoBar.success(
                title=tr("wizard.success_title"),
                content=tr("wizard.shortcut_created"),
                parent=self,
                duration=3000
            )  # 成功 / 已创建桌面快捷方式
        except Exception as e:
            InfoBar.warning(
                title=tr("wizard.exit_confirm_title"),
                content=tr("wizard.shortcut_failed", error=str(e)),
                parent=self,
                duration=5000
            )  # 提示 / 创建快捷方式失败：{error}

    def _onThemeChanged(self, index):
        """主题变更"""
        theme_mode = cfg.themeMode.value
        setTheme(theme_mode.value)
        self.__setQss()
        def update_widget_style(widget):
            widget.style().unpolish(widget)
            widget.style().polish(widget)
            widget.update()
            for child in widget.children():
                if hasattr(child, 'style'):
                    update_widget_style(child)

        update_widget_style(self)

    def _onColorChanged(self, color):
        """颜色变更"""
        try:
            setThemeColor(color)
        except Exception:
            theme_color = cfg.themeColor.value
            try:
                setThemeColor(theme_color)
            except Exception:
                pass

    def __setQss(self):
        self.setStyleSheet(load_qss('app.qss'))

class MainWindow(FluentWindow):
    """主窗口"""

    def __init__(self):
        super().__init__()
        self.setWindowTitle(APP_NAME)
        # self.setWindowFlags(self.windowFlags() | Qt.WindowType.FramelessWindowHint)
        # self.setSystemTitleBarButtonVisible(False)
        # self.updateFrameless()

        setTheme(cfg.themeMode.value)

        icon_path = get_resPath(APP_ICON)
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))
        else:
            logger.warning("窗口图标文件不存在")

        self.isEditMode = False

        _t_i18n = time.time()
        self._initTranslation()
        logger.info(f"[MW] 翻译系统初始化 耗时{time.time()-_t_i18n:.2f}s")

        _t_nav = time.time()
        self._initNavigation()
        logger.info(f"[MW] _initNavigation 总耗时{time.time()-_t_nav:.2f}s")

        self._normal_size = (1050, 750)
        self._is_maximized = False
        self._resize_timer = QTimer(self)
        self._resize_timer.setSingleShot(True)
        # self._resize_timer.timeout.connect(self._checkWindowSize)
        self.resize(*self._normal_size)
        self.setMinimumSize(*self._normal_size)
        if not self._loadWindowPosition():
            self.moveToCenter()

        self.initSystemTray()

        _t = time.time()
        sync_autostart_cfg()
        logger.info(f"[MW] sync_autostart_cfg 耗时{time.time()-_t:.2f}s")
        cfg.autoStart.valueChanged.connect(lambda value: set_autostart(value))

        self._initIdleDetection()
        self._initThemeConnections()
        self._initSystemThemeMonitor()

        self.navigationInterface.installEventFilter(self)

        logger.info("主窗口初始化完成")

        # _t_i18n = time.time()
        # self._initTranslation()
        # logger.info(f"[MW] 翻译系统初始化 耗时{time.time()-_t_i18n:.2f}s")
 
    def disable_restore_button(self):
        """禁用系统菜单中的 还原/移动/大小 选项"""
        try:
            hwnd = int(self.winId())
            hMenu = ctypes.windll.user32.GetSystemMenu(hwnd, False)
            if hMenu:
                # 灰化还原 (SC_RESTORE)
                ctypes.windll.user32.EnableMenuItem(hMenu, 0xF120,
                                                    win32con.MF_BYCOMMAND | win32con.MF_GRAYED)
                # 灰化移动 (SC_MOVE)
                ctypes.windll.user32.EnableMenuItem(hMenu, 0xF010,
                                                    win32con.MF_BYCOMMAND | win32con.MF_GRAYED)
                # 灰化大小 (SC_SIZE)
                ctypes.windll.user32.EnableMenuItem(hMenu, 0xF000,
                                                    win32con.MF_BYCOMMAND | win32con.MF_GRAYED)
        except Exception:
            pass

    def showEvent(self, event):
        super().showEvent(event)
        # 窗口首次显示时灰化系统菜单
        if not hasattr(self, '_menu_disabled'):
            self._menu_disabled = True
            QTimer.singleShot(100, self.disable_restore_button)  # 延迟确保菜单已创建
            
    def _initNavigation(self):
        _t = time.time()
        self.homeInterface = HomeInterface(self)
        self.homeInterface.setObjectName("home")
        self.addSubInterface(self.homeInterface, FUI.HOME, tr("navigation.home"))  # 主界面
        logger.info(f"[MW] HomeInterface 耗时{time.time()-_t:.2f}s")

        _t = time.time()
        self.wallpaper = WallpaperInterface(mainWindow=self)
        self.wallpaper.setObjectName("wallpaper")
        self.addSubInterface(self.wallpaper, FUI.PHOTO, tr("navigation.wallpaper"))  # 壁纸
        logger.info(f"[MW] WallpaperInterface 耗时{time.time()-_t:.2f}s")

        _t = time.time()
        self.notificationPage = NotificationPage(self)
        self.notificationPage.setObjectName("notification")
        self.addSubInterface(self.notificationPage, FUI.MESSAGE, tr("navigation.notification"))  # 通知
        self.notifManager = NotificationManager(self)
        self.notificationPage.send_notification.connect(self.notifManager.handle_notification)
        self.notifManager.notification_finished.connect(self.notificationPage._on_notification_shown)
        logger.info(f"[MW] NotificationPage 耗时{time.time()-_t:.2f}s")

        _t = time.time()
        self.timetablePage = TimetablePage(self)
        self.timetablePage.setObjectName("timetable")
        self.addSubInterface(self.timetablePage, FUI.EDUCATION, tr("navigation.timetable"))  # 课程表
        logger.info(f"[MW] TimetablePage 耗时{time.time()-_t:.2f}s")

        _t = time.time()
        self.downloadInterface = DownloadInterface(parent=self)
        self.addSubInterface(self.downloadInterface, FUI.DOWNLOAD, tr("navigation.download"))  # 软件下载

        def _populateDownload():
            for category in SOFTWARE_CATEGORIES:
                self.downloadInterface.addSection(tr(category["name_key"]))
                for software in category["software"]:
                    icon_path = get_software_icon_path(software["icon"])
                    link = software.get("link")
                    self.downloadInterface.addSoftware(icon_path, software["name"], software["description"], link)
            self.downloadInterface._onDataPopulated()
        QTimer.singleShot(0, _populateDownload)

        _t = time.time()
        self.aboutInterface = AboutInterface(parent=self)
        self.addSubInterface(self.aboutInterface, FUI.INFO, tr("navigation.about"), NavigationItemPosition.BOTTOM)  # 关于
        logger.info(f"[MW] AboutInterface 耗时{time.time()-_t:.2f}s")

        _t = time.time()
        self.debugPanel = DebugPanel(self)
        self.debugNavItem = self.addSubInterface(self.debugPanel, FUI.DEVELOPER_TOOLS, tr("navigation.debug"), NavigationItemPosition.BOTTOM)  # 调试
        self.debugNavItem.setVisible(cfg.debugMode.value)
        cfg.debugMode.valueChanged.connect(self._onDebugModeChanged)

        logger.info(f"[MW] DebugPanel 耗时{time.time()-_t:.2f}s")

        self.editPanel = None  # deprecated, kept for compatibility

    def _initIdleDetection(self):
        self.idleTimer = QTimer(self)
        self.idleTimer.timeout.connect(self._checkIdle)
        self.lastMouseActivity = QTime.currentTime()
        self.lastKeyboardActivity = QTime.currentTime()
        self._minimized_flag = False
        self.idleCheckInterval = 10000
        self.hasTriggeredAutoOpen = False
        self.isVideoPlaying = False
        self.maxMinimizeNotifications = 5
        cfg.autoOpenOnIdle.valueChanged.connect(self._updateIdleTimer)
        cfg.idleMinutes.valueChanged.connect(self._updateIdleTimer)
        self._updateIdleTimer()
        self._installGlobalHooks()

    def _initThemeConnections(self):
        cfg.themeChanged.connect(self.downloadInterface._onThemeChanged)
        cfg.themeChanged.connect(self.wallpaper._onThemeChanged)
        cfg.themeChanged.connect(self.notificationPage._onThemeChanged)
        cfg.themeChanged.connect(self.timetablePage._onThemeChanged)
        cfg.themeChanged.connect(self.aboutInterface._onThemeChanged)
        cfg.themeChanged.connect(self._onDebugPanelThemeChanged)

    def _initSystemThemeMonitor(self):
        self._themeCheckTimer = QTimer(self)
        self._themeCheckTimer.setInterval(5000)
        self._themeCheckTimer.timeout.connect(self._checkSystemTheme)
        cfg.themeMode.valueChanged.connect(self._onThemeModeChanged)
        if cfg.themeMode.value == Theme.AUTO:
            self._themeCheckTimer.start()
            self._checkSystemTheme()

    def _onThemeModeChanged(self, mode: Theme):
        # 清 QSS 缓存
        clear_qss_cache()

        if mode == Theme.AUTO:
            self._themeCheckTimer.start()
            self._checkSystemTheme()
        else:
            self._themeCheckTimer.stop()
            if mode == Theme.DARK:
                cfg.theme = Theme.DARK
            else:
                cfg.theme = Theme.LIGHT
            # setTheme() 会 updateStyleSheet() 更新pfw组件
            # 值不同会发 themeChanged 回调中 load_qss 会读真主题
            setTheme(cfg.theme)

        # 如果 setTheme() 因值相同没发 themeChanged 手动补发
        cfg.themeChanged.emit(cfg.theme)

    def _checkSystemTheme(self):
        """检查系统主题变更"""
        try:
            current = darkdetect.theme()
            if not current:
                return
            current_theme = Theme.LIGHT if current == 'Light' else Theme.DARK
            if cfg.theme != current_theme:
                logger.info(f"系统主题已变更: {cfg.theme} → {current_theme}")
                clear_qss_cache()
                cfg.theme = current_theme
                setTheme(current_theme)
                cfg.themeChanged.emit(cfg.theme)
        except Exception as e:
            logger.warning(f"检查系统主题时出错: {e}")

    def _onDebugModeChanged(self, value):
        self.debugNavItem.setVisible(value)
        if not value and self.stackedWidget.currentWidget() == self.debugPanel:
            self.switchTo(self.homeInterface)

    def _onDebugPanelThemeChanged(self):
        if hasattr(self, 'debugPanel') and self.debugPanel:
            self.debugPanel._updateTheme()

    def _initTranslation(self):
        """翻译"""
        try:
            manager = get_translation_manager()
            language_map = {
                Language.CHINESE_SIMPLIFIED: LanguageCode.ZH_CN.value,
                Language.CHINESE_TRADITIONAL: LanguageCode.ZH_TW.value,
                Language.ENGLISH: LanguageCode.EN_US.value,
                Language.AUTO: self._detect_system_language().value,
            }
            cfg.language.valueChanged.connect(self._onLanguageConfigChanged)
            target_lang = language_map.get(cfg.language.value, LanguageCode.ZH_CN.value)
            manager.set_language(target_lang)
        except Exception as e:
            logger.error(f"翻译初始化失败: {e}")

    @staticmethod
    def _detect_system_language() -> LanguageCode:
        """检测系统语言"""
        try:
            system_locale = QLocale()
            language = system_locale.language()
            if language == QLocale.Language.Chinese:
                if system_locale.territory() in [QLocale.Country.HongKong, QLocale.Country.Taiwan, QLocale.Country.Macau]:
                    return LanguageCode.ZH_TW
                return LanguageCode.ZH_CN
            elif language == QLocale.Language.English:
                return LanguageCode.EN_US
            else:
                return LanguageCode.ZH_CN
        except Exception:
            return LanguageCode.ZH_CN

    def _onLanguageConfigChanged(self, new_language):
        """语言切换回调重启提示"""
        from qfluentwidgets import MessageBox

        w = MessageBox(
            tr("settings.restart_required"),
            tr("settings.restart_required_desc"),
            self
        )
        w.yesButton.setText(tr("common.restart_now"))
        w.cancelButton.setText(tr("common.restart_later"))

        if w.exec():
            import sys
            import subprocess
            QApplication.quit()
            subprocess.Popen([sys.executable] + sys.argv)

    def updateInterfaceText(self, interface, text: str, position=None):
        """更新子界面导航（已弃用）"""
        try:
            if hasattr(interface, 'objectName'):
                route_key = interface.objectName()
                if hasattr(self.navigationInterface, 'panel'):
                    item = self.navigationInterface.panel.items.get(route_key)
                    if item and hasattr(item, 'setText'):
                        item.setText(text)
        except Exception as e:
            logger.warning(f"更新界面文本失败 [{interface.objectName() if hasattr(interface, 'objectName') else 'unknown'}]: {e}")

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_F12:
            if cfg.debugMode.value and hasattr(self, 'debugPanel'):
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

    # def _checkWindowSize(self):
    #     if not hasattr(self, '_normal_size'):
    #         return
    #     if self.isFullScreen():
    #         return
    #     self.showMaximized()

    def changeEvent(self, event):
        # super().changeEvent(event)
        # if event.type() == QEvent.Type.WindowStateChange:
        #     if not self.isMinimized() and not self.isFullScreen():
        #         QTimer.singleShot(0, self._forceFullScreen)
        super().changeEvent(event)
        if event.type() == QEvent.Type.WindowStateChange:
            # 立刻最大化回去
            if not self.isMaximized() and not self.isMinimized():
                QTimer.singleShot(0, self.showMaximized)

    def _forceFullScreen(self):
        if not self.isMinimized():
            self.showMaximized()

    def resizeEvent(self, event):
        super().resizeEvent(event)
        # if hasattr(self, '_normal_size') and hasattr(self, '_resize_timer'):
        #     self._resize_timer.start(50)

    def moveToCenter(self):
        screen = QApplication.primaryScreen()
        if screen:
            rect = screen.availableGeometry()
            w, h = rect.width(), rect.height()
            self.move(w // 2 - self.width() // 2, h // 2 - self.height() // 2)

    def initSystemTray(self):
        icon_path = get_resPath(APP_ICON)
        if os.path.exists(icon_path):
            self.tray_icon = QSystemTrayIcon(QIcon(icon_path), self)
        else:
            self.tray_icon = QSystemTrayIcon(self)

        self.tray_menu = RoundMenu(APP_NAME, self)

        show_action = Action(FUI.HOME, tr("tray.show_window"), self)  # 显示主窗口
        show_action.triggered.connect(self.showMaximized)
        self.tray_menu.addAction(show_action)
        if cfg.debugMode.value:
            dev_action = Action(FUI.DEVELOPER_TOOLS, tr("navigation.debug"), self)  # 调试
            dev_action.triggered.connect(lambda: self.switchTo(self.debugPanel))
            self.tray_menu.addAction(dev_action)

        exit_action = Action(FUI.CLOSE, tr("tray.exit"), self)  # 退出
        exit_action.triggered.connect(lambda: (release_single_instance(), QApplication.quit()))
        self.tray_menu.addAction(exit_action)

        self.tray_icon.setContextMenu(self.tray_menu)
        self.tray_icon.activated.connect(self._onTrayIconActivated)
        self.tray_icon.show()

    def _onTrayIconActivated(self, reason):
        if reason in (QSystemTrayIcon.ActivationReason.DoubleClick, QSystemTrayIcon.ActivationReason.Trigger):
            if self.isMinimized() or not self.isVisible():
                self.showMaximized()
            else:
                self.hide()

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

        try:
            if self.isVideoPlaying:
                return
            from Glimpseon_native import idle_get_milliseconds
            idle_time_ms = idle_get_milliseconds()
            if idle_time_ms < 0: return  # API 调用失败 跳过

            now = QTime.currentTime()
            try:
                from Glimpseon_native import was_page_operation_recent
                is_recent_page_operation = was_page_operation_recent(5000)
            except Exception:
                is_recent_page_operation = False

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
        self.showMaximized()
        self.activateWindow()

    def _installGlobalHooks(self):
        try:
            from Glimpseon_native import install_hook
            install_hook()
        except Exception as e:
            logger.error(f"全局钩子安装失败：{e}")

    def setVideoPlaying(self, playing):
        self.isVideoPlaying = playing

    def showMaximized(self):
        self._is_maximized = True
        super().showMaximized()

    def showNormal(self):
        self._forceFullScreen()

    def _lockWindowFullScreen(self):
        """锁定全屏"""
        try:
            hwnd = int(self.winId())
            style = ctypes.windll.user32.GetWindowLongW(hwnd, -16)  # GWL_STYLE
            new_style = style & ~0x10000 & ~0x40000  # 去掉 WS_MAXIMIZEBOX | WS_THICKFRAME
            if new_style != style:
                ctypes.windll.user32.SetWindowLongW(hwnd, -16, new_style)
                ctypes.windll.user32.SetWindowPos(
                    hwnd, 0, 0, 0, 0, 0,
                    0x0020 | 0x0002 | 0x0001 | 0x0004  # SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER
                )
        except Exception:
            pass

    def nativeEvent(self, eventType, message):
        try:
            if eventType == b"windows_generic_MSG":
                msg = _MSG.from_address(int(message))
                if msg.message == 0x0112:  # WM_SYSCOMMAND
                    low_word = msg.wParam & 0xFFFF
                    # 阻止拖拽移动 (0xF010) 和调整大小 (0xF000 系列)
                    if (low_word & 0xFFF0) in (0xF010, 0xF000):
                        return True, 0
                    # 拦截还原 (SC_RESTORE)，仅允许从最小化恢复
                    if low_word == 0xF120:
                        if self.isMinimized():
                            self.setWindowState(Qt.WindowState.WindowMaximized)
                        # 返回 True 阻止系统处理，还原按钮无任何效果
                        return True, 0
        except Exception:
            pass
        return super().nativeEvent(eventType, message)

    def hide(self):
        self.hasTriggeredAutoOpen = False
        if cfg.autoOpenOnIdle.value:
            self.idleTimer.start(self.idleCheckInterval)
        super().hide()

    def closeEvent(self, event):
        if hasattr(self, 'homeInterface'):
            self.homeInterface.saveComponentPositions()

        # 清理线程
        _preloader = getattr(self, '_preloader', None)
        if _preloader and _preloader.isRunning():
            _preloader.cancel()
            if not _preloader.wait(3000):
                _preloader.terminate()
                _preloader.wait(1000)

        if cfg.debugMode.value:
            event.accept()
            try:
                from Glimpseon_native import uninstall_hook
                uninstall_hook()
            except Exception:
                pass
            release_single_instance()
            QApplication.quit()
            return

        if cfg.closeAction.value == "minimize":
            event.ignore()
            if cfg.autoOpenOnIdle.value:
                self.idleTimer.start(self.idleCheckInterval)
            self.hide()
            if cfg.minimizeNotificationCount.value < self.maxMinimizeNotifications:
                self.tray_icon.showMessage(APP_NAME, tr("tray.minimize_message"), QSystemTrayIcon.MessageIcon.Information, 2000)  # 应用已最小化到系统托盘
                cfg.minimizeNotificationCount.value = cfg.minimizeNotificationCount.value + 1
                save_cfg()
        else:
            try:
                from Glimpseon_native import uninstall_hook
                uninstall_hook()
            except Exception:
                pass
            release_single_instance()
            QApplication.quit()

    def saveComponentPositions(self):
        if hasattr(self, 'homeInterface'):
            self.homeInterface.saveComponentPositions()

    def _loadWindowPosition(self):
        try:
            config_path = os.path.join(DATA_CONFIG, 'component_positions.json')
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

    # 刷新委托到 homeInterface
    def refresh_quick_launch(self):
        hi = getattr(self, 'homeInterface', None)
        if hi and hasattr(hi, '_updateQuickLaunch'): hi._updateQuickLaunch()


class Preloader(QThread):
    sig_wp = pyqtSignal(str, str, str)
    sig_wt = pyqtSignal(dict)
    sig_po = pyqtSignal(str)

    def __init__(self, win):
        super().__init__()
        self.win = win
        self._stop = False

    def cancel(self):
        self._stop = True

    def run(self):
        try:
            self._load_wp()
            if not self._stop: self._load_wt()
            if not self._stop: self._load_po()
        except Exception as e:
            logger.error(f"[PRELOAD] {e}")

    def _load_wp(self):
        if self._stop: return
        wp = getattr(self.win, 'wallpaper', None)
        if not wp: return
        try:
            if wp.current_pixmap and not wp.current_pixmap.isNull(): return
        except: return

        from core.utils import get_cached_content, save_cache

        cached = get_cached_content("wallpaper", ignore_expiry=True)  # 过期也显示旧的
        if cached and os.path.exists(cached.get('path', '')):
            self.sig_wp.emit(cached['path'], cached.get('source', ''), cached.get('url', ''))
            return

        if self._stop: return
        import requests
        url, src = wp._getApiUrl()
        resp = requests.get(url, stream=True, timeout=15)
        if resp.status_code == 200:
            d = WALLPAPER_DIR  # os.path.join(BASE_DIR, 'wallpaper')
            os.makedirs(d, exist_ok=True)
            p = os.path.join(d, f"wp_{datetime.datetime.now().strftime('%H%M%S')}.jpg")
            with open(p, 'wb') as f: f.write(resp.content)
            wp._manageWallpaperLimit(d, cfg.wallpaperSaveLimit.value)
            save_cache("wallpaper", {"path": p, "source": src, "url": url}, cfg.autoGetInterval.value)
            if not self._stop: self.sig_wp.emit(p, src, url)
            return

        default = get_resPath(os.path.join('resource', 'wallpaper', 'default.jpg'))
        if os.path.exists(default):
            if not self._stop: self.sig_wp.emit(default, tr("wallpaper.default_source"), "")
            return

        wd = WALLPAPER_DIR  # os.path.join(BASE_DIR, 'wallpaper')
        if os.path.exists(wd):
            olds = sorted(
                [f for f in os.listdir(wd) if f.endswith('.jpg') and f.startswith('wallpaper_')],
                key=lambda f: os.path.getmtime(os.path.join(wd, f)),
                reverse=True
            )
            if olds:
                p = os.path.join(wd, olds[0])
                if not self._stop: self.sig_wp.emit(p, tr("wallpaper.source_cache"), "")

    def _load_wt(self):
        if self._stop: return
        hi = getattr(self.win, 'homeInterface', None)
        if not hi or not cfg.showWeather.value: return

        from core.utils import get_cached_content, save_cache
        from services.weather import RegionDatabase, WeatherService

        cached = get_cached_content("weather")
        if cached:
            if not self._stop:
                data = {
                    'current_temp': cached.get('current_temp', cached.get('temp', '?')),
                    'temp_unit': cached.get('temp_unit', cached.get('unit', '°C')),
                    'weather_code': cached.get('weather_code', cached.get('code')),
                    'forecast_hourly': cached.get('forecast_hourly', {}),
                    'forecast_daily': cached.get('forecast_daily', {}),
                }
                self.sig_wt.emit(data)
            return

        if self._stop: return
        
        city_name = cfg.city.value
        if city_name:
            lon, lat = RegionDatabase().get_coordinates(city_name)
            if lon is not None and lat is not None:
                cfg.longitude.value = lon
                cfg.latitude.value = lat
        
        try:
            ws = WeatherService()
            data = ws.fetch_all()
            if data:
                save_cache("weather", data, cfg.weatherUpdateInterval.value)
                if not self._stop: self.sig_wt.emit(data)
        except Exception as e:
            logger.error(f"[PRELOAD] 天气预加载失败: {e}")

    def _load_po(self):
        if self._stop: return
        hi = getattr(self.win, 'homeInterface', None)
        if not hi or not cfg.showPoetry.value: return

        from core.utils import get_cached_content, save_cache
        from services.poetry import PoetryService

        cached = get_cached_content("poetry")
        if cached:
            if not self._stop: self.sig_po.emit(cached)
            return

        if self._stop: return
        text = PoetryService.get_poetry()
        save_cache("poetry", text, cfg.poetryUpdateInterval.value)
        if not self._stop: self.sig_po.emit(text)


if __name__ == "__main__":
    _auto_start_launch = auto_start_launch()

    QApplication.setHighDpiScaleFactorRoundingPolicy(Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)

    if cfg.enableGpuAcceleration.value:
        QApplication.setAttribute(Qt.ApplicationAttribute.AA_UseOpenGLES)

    app = QApplication(sys.argv)

    init_exhook()
    atexit.register(release_single_instance)

    _extract_future = None
    executor = ThreadPoolExecutor(max_workers=2)
    
    def _background_extract():
        try:
            extract_files()
        except Exception as e:
            logger.exception(f"资源提取失败: {e}")
    
    _extract_future = executor.submit(_background_extract)

    if check_wizard_needed():
        create_wizard_file()
        wizard = WizardWindow()
        wizard.exec()

    icon_path = get_resPath(APP_ICON)
    logger.info(f"[BOOT] APP_DIR={APP_DIR}, APP_ICON={APP_ICON}, icon_path={icon_path}, exists={os.path.exists(icon_path)}")

    _boot_t0 = time.time()

    splash = SplashScreen(APP_NAME, VERSION, icon_path)
    splash.show()
    splash.setProgress(0)
    logger.info(f"[BOOT] Splash显示 耗时{time.time()-_boot_t0:.2f}s")

    def allow_ui_update(duration=0.06):
        end = time.time() + duration
        while time.time() < end:
            app.processEvents()
            time.sleep(0.005)

    app.processEvents()

    def _background_init():
        try:
            splash.status_signal.emit(tr("splash.cleaning_temp"))  # 正在清理临时文件...
            splash.progress_signal.emit(10)
            cleanup_temp_directory(logger=logger)
            splash.status_signal.emit(tr("splash.loading_resources"))  # 正在加载资源...
            splash.progress_signal.emit(70)
        except Exception as e:
            logger.exception(f"后台初始化失败: {e}")

    future = executor.submit(_background_init)

    # 退出程序的时候关线程池
    atexit.register(lambda: executor.shutdown(wait=False))

    splash.updateStatus(tr("splash.loading_translation"))  # 正在加载翻译
    splash.setProgress(15)
    allow_ui_update(0.06)
    _t = time.time()

    language_locale_map = {
        Language.CHINESE_SIMPLIFIED: QLocale(QLocale.Language.Chinese, QLocale.Country.China),
        Language.CHINESE_TRADITIONAL: QLocale(QLocale.Language.Chinese, QLocale.Country.HongKong),
        Language.ENGLISH: QLocale(QLocale.Language.English),
        Language.AUTO: QLocale(),
    }
    locale = language_locale_map.get(cfg.language.value, QLocale())
    fluentTranslator = FluentTranslator(locale)
    app.installTranslator(fluentTranslator)
    logger.info(f"[BOOT] 语言配置: {cfg.language.value}，耗时{time.time()-_t:.2f}s")

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
        title = tr("dialog.instance_running", app=APP_NAME)  # {app} 已有实例运行
        content = tr("dialog.instance_running_detail", app=APP_NAME)  # 检测到{app} 已有一个实例在运行中，请勿重复启动。\n\n(您可在【设置】中启用【允许重复启动】，可能会有不可言喻的问题。)
        w = MessageBox(title, content, temp_widget)
        w.yesButton.setText(tr("common.cancel"))  # 取消
        w.hideCancelButton()
        w.exec()
        sys.exit(0)

    splash.updateStatus(tr("splash.initializing_fonts"))  # 正在初始化字体
    splash.setProgress(30)
    allow_ui_update(0.06)
    
    if _extract_future:
         _extract_future.result(timeout=10)
    
    _t = time.time()
    initialize_fonts(app, install_to_system=True)
    logger.info(f"[BOOT] 字体初始化 耗时{time.time()-_t:.2f}s")

    splash.updateStatus(tr("splash.configuring_log"))  # 正在配置日志
    splash.setProgress(40)
    allow_ui_update(0.06)

    if hasattr(cfg.logLevel.value, 'value'):
        log_level_str = cfg.logLevel.value.value
    else:
        log_level_str = str(cfg.logLevel.value)

    if cfg.debugMode.value:
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

    splash.updateStatus(tr("splash.loading_config"))  # 正在加载配置
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
    logger.info(f"其他配置：关闭动作={cfg.closeAction.value}, 允许多实例={cfg.allowMultipleInstances.value}, 调试模式={cfg.debugMode.value}, 自动启动={cfg.autoStart.value}")
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
    # logger.info(f"版本号：{VERSION} 构建日期：{BUILD_DATE}")
    from core.paths import VERSION, BUILD_DATE
    logger.info(f"版本号：{VERSION} 构建日期：{BUILD_DATE}")
    logger.info(f"系统版本：Windows {platform.version()} Python 版本：{platform.python_version()}")
    logger.info(f"软件运行路径：{BASE_DIR}")

    _t = time.time()
    wait_start = time.time()
    while not future.done():
        allow_ui_update(0.02)
        if time.time() - wait_start > 5.0:
            logger.warning("后台初始化超时，继续启动主窗口")
            break
    logger.info(f"[BOOT] 后台等待 耗时{time.time()-_t:.2f}s")

    splash.updateStatus(tr("splash.creating_main_window"))  # 正在创建主窗口...
    splash.setProgress(70)
    splash.waitForProgress(70, timeout=0.5)
    _t = time.time()
    window = MainWindow()

    logger.info(f"[BOOT] 创建主窗口 耗时{time.time()-_t:.2f}s")

    def _upd_wp(path, src, url):
        try:
            wp = getattr(window, 'wallpaper', None)
            if not wp: return
            wp.current_pixmap = QPixmap(path)
            wp.current_wallpaper_path = path
            wp.current_wallpaper_source = src
            if not wp.current_pixmap.isNull():
                wp._updateMainWindowBackground()
                wp._applyEffects()
                wp.infoCard.updateInfo(path, src)
                wp.historyManager.add(path, src, url)
                wp.wallpaperChanged.emit()
        except Exception as e:
            logger.error(f"[PRELOAD-UI] wp: {e}")

    def _update_weather_display(weather_data):
        try:
            hi = getattr(window, 'homeInterface', None)
            if not hi: return
            # 更新缓存
            hi._cached_weather = weather_data
            hi.current_weather_code = weather_data.get('weather_code')
            hi.weather_updated.emit(weather_data)
        except Exception as e:
            logger.error(f"[PRELOAD-UI] wt: {e}")

    def _upd_po(text):
        try:
            hi = getattr(window, 'homeInterface', None)
            if not hi: return
            # 更新缓存
            hi._cached_poetry = text
            hi.poetry_updated.emit(text)
        except Exception as e:
            logger.error(f"[PRELOAD-UI] po: {e}")

    splash.status_signal.emit(tr("splash.preloading"))  # 正在预加载...
    splash.progress_signal.emit(75)

    loader = Preloader(window)
    window._preloader = loader
    loader.sig_wp.connect(_upd_wp)
    loader.sig_wt.connect(_update_weather_display)
    loader.sig_po.connect(_upd_po)
    loader.start()

    if cfg.autoCheckUpdate.value:
        window.aboutInterface.checkUpdateAuto()

    splash.updateStatus(tr("splash.completing_startup"))  # 正在完成启动
    t0 = time.time()

    while loader.isRunning():
        allow_ui_update(0.02)
        if time.time() - t0 > 12:
            loader.cancel()
            loader.wait(5000)
            # QThread.terminate()
            # if loader.isRunning():
            #     loader.terminate()
            #     loader.wait(1000)
            break

    logger.info(f"[BOOT] 预加载 {time.time()-t0:.2f}s")
    splash.setProgress(95)
    allow_ui_update(0.06)

    splash.setProgress(100)
    _t2 = time.time()
    splash.waitForProgress(100, timeout=1.0)
    allow_ui_update(0.06)
    logger.info(f"[BOOT] 进度条100%等待 耗时{time.time()-_t2:.2f}s")
    splash.close()
    logger.info(f"[BOOT] 总启动耗时{time.time()-_boot_t0:.2f}s")

    window.showMaximized()
    if hasattr(window, 'tray_icon') and window.tray_icon:
        window.tray_icon.show()
    if _auto_start_launch:
        logger.info("开机自启动模式：全屏启动")
    else:
        logger.info("一般启动模式：全屏启动")

    sys.exit(app.exec())

