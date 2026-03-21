from PyQt5.QtWidgets import QApplication, QWidget, QSystemTrayIcon, QMenu, QAction, QLabel, QPushButton, QVBoxLayout, QHBoxLayout, QGridLayout, QSpacerItem, QSizePolicy, QFileDialog, QGraphicsBlurEffect,QStackedLayout, QPlainTextEdit
from PyQt5.QtCore import QTimer, Qt, QTime, QDate
from PyQt5.QtCore import QLocale, QTranslator, QUrl
from PyQt5.QtGui import QFontDatabase, QFont, QIcon, QPixmap, QImage, QPainter, QColor
from qfluentwidgets import (
    setTheme, Theme, FluentWindow, FluentTranslator,
    FluentIcon as FIF, NavigationItemPosition, RoundMenu, Action, MessageBox, ScrollArea, SmoothScrollArea, ExpandLayout, isDarkTheme,
    PushButton, CardWidget, ProgressBar, InfoBar, ImageLabel, qconfig, SwitchSettingCard, PrimaryPushButton, SettingCardGroup, TextEdit
)
import requests
import sys
import os
import platform
import ctypes
import json
import threading
from setting import SettingInterface
import shutil
from config import cfg, get_default_config_dict
from logger import logger, setup_exception_hook
from version import VERSION, BUILD_DATE
from version_updater import check_version_from_github, download_update, extract_update, create_update_script
from constants import APP_NAME
from city_selector import RegionDatabase
import datetime
import cnlunar
import winreg
import logging
def check_single_instance():
    """ 检查是否已经有实例 """
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
    # 打包为exe时
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
    MEIPASS_DIR = sys._MEIPASS
else:
    # 脚本运行时
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    MEIPASS_DIR = None

def extract_bundled_files():
    """从打包文件中提取必要的文件夹和文件"""
    if not getattr(sys, 'frozen', False) or not MEIPASS_DIR:
        return
    
    # 需要提取的文件夹列表
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
    """
    同步配置中的自启动设置与实际注册表状态
    在应用启动时调用，确保配置与实际状态一致
    """
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
            logger.info("自启动状态已同步，无需更改")
            return True
            
    except Exception as e:
        logger.error(f"同步自启动状态失败: {e}")
        return False


class BaseScrollAreaInterface(ScrollArea):
    """ 基础滚动区域界面 """
    
    def __init__(self, title: str, parent=None, width=1000, height=800, 
                 viewport_margins=(0, 120, 0, 20), title_position=(60, 63)):
        super().__init__(parent=parent)
        self.title = title
        self.scrollWidget = QWidget()
        self.titleLabel = QLabel(title, self)
        
        self.resize(width, height)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        self.setViewportMargins(*viewport_margins)
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        
        self.titleLabel.setObjectName('settingLabel')
        self.scrollWidget.setObjectName('scrollWidget')
        self.titleLabel.move(*title_position)


class UpdateInterface(BaseScrollAreaInterface):
    """ 更新界面 """
    
    def __init__(self, parent=None):
        super().__init__("更新", parent)
        self.setObjectName("update")
        
        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.mainLayout.setContentsMargins(60, 0, 60, 40)
        self.mainLayout.setSpacing(16)
        
        self.__initWidgets()
        self.__initLayout()
        self.__setQss()
        self.__connectSignalToSlot()
    
    def __connectSignalToSlot(self):
        """ 连接信号与槽 """
        cfg.themeChanged.connect(self._onThemeChanged)
    
    def __setQss(self):
        """ 设置样式表 """
        self.scrollWidget.setObjectName('scrollWidget')
        self.titleLabel.setObjectName('settingLabel')
        
        theme = 'dark' if isDarkTheme() else 'light'
        try:
            qss_path = get_resource_path(os.path.join('resource', 'qss', theme, 'update_interface.qss'))
            with open(qss_path, encoding='utf-8') as f:
                self.setStyleSheet(f.read())
        except Exception:
            pass
    
    def _onThemeChanged(self, theme: Theme):
        """ 主题变更槽函数 """
        self.__setQss()
    
    def __initWidgets(self):
        """ 初始化控件 """
        # 版本信息卡片
        self.versionCard = CardWidget(self.scrollWidget)
        self.versionLayout = QHBoxLayout(self.versionCard)
        self.versionLayout.setContentsMargins(24, 24, 24, 24)
        self.versionLayout.setSpacing(16)
        
        # 版本图标和标题
        self.versionHeaderLayout = QHBoxLayout()
        self.versionIcon = QLabel(self.versionCard)
        self.versionIcon.setFixedSize(48, 48)
        self.versionIcon.setPixmap(QPixmap("resource/icons/CY.png").scaled(
            48, 48, Qt.KeepAspectRatio, Qt.SmoothTransformation
        ))
        
        self.versionInfoLayout = QVBoxLayout()
        self.versionInfoLayout.setSpacing(4)
        self.versionTitle = QLabel(f"ClassLively {VERSION}", self.versionCard)
        self.versionTitle.setObjectName("versionTitle")
        self.buildDate = QLabel(f"构建日期：{BUILD_DATE}", self.versionCard)
        self.buildDate.setObjectName("buildDate")
        
        self.versionInfoLayout.addWidget(self.versionTitle)
        self.versionInfoLayout.addWidget(self.buildDate)
        self.versionHeaderLayout.addWidget(self.versionIcon)
        self.versionHeaderLayout.addLayout(self.versionInfoLayout)
        
        # 更新状态显示
        self.updateStatusLayout = QHBoxLayout()
        self.updateStatusLayout.setSpacing(8)
        self.updateStatusIcon = QLabel(self.versionCard)
        self.updateStatusIcon.setFixedSize(16, 16)
        self.updateStatusIcon.setStyleSheet("background-color: #999999; border-radius: 8px;")
        self.updateStatusLabel = QLabel("已就绪", self.versionCard)
        self.updateStatusLabel.setStyleSheet("color: #999999; font-size: 14px;")
        self.updateStatusLayout.addWidget(self.updateStatusIcon)
        self.updateStatusLayout.addWidget(self.updateStatusLabel)
        
        # 检查更新按钮
        self.checkUpdateButton = PrimaryPushButton(FIF.SYNC, "检查更新", self.versionCard)
        self.checkUpdateButton.setFixedHeight(36)
        self.checkUpdateButton.clicked.connect(self.__checkUpdate)
        
        self.versionLayout.addLayout(self.versionHeaderLayout)
        self.versionLayout.addStretch()
        self.versionLayout.addLayout(self.updateStatusLayout)
        self.versionLayout.addWidget(self.checkUpdateButton)
        
        # 更新日志卡片
        self.changelogCard = CardWidget(self.scrollWidget)
        self.changelogLayout = QVBoxLayout(self.changelogCard)
        self.changelogLayout.setContentsMargins(24, 24, 24, 24)
        self.changelogLayout.setSpacing(16)
        
        self.changelogTitle = QLabel("更新日志", self.changelogCard)
        self.changelogTitle.setObjectName("changelogTitle")
        
        # 使用 Fluent Widgets 的 TextEdit 组件
        self.changelogContent = TextEdit(self.changelogCard)
        self.changelogContent.setReadOnly(True)
        self.changelogContent.setPlaceholderText("正在加载更新日志...")
        self.changelogContent.setFixedHeight(200)
        
        self.changelogLayout.addWidget(self.changelogTitle)
        self.changelogLayout.addWidget(self.changelogContent)
        
        # 初始化时加载更新日志
        self.__loadChangelog()
        self.autoCheckUpdateCard = SwitchSettingCard(
            FIF.UPDATE,
            "自动检查更新",
            "启用后，应用启动时会自动检查新版本",
            configItem=cfg.autoCheckUpdate,
            parent=self.scrollWidget
        )
        self.autoUpdateCard = SwitchSettingCard(
            FIF.DOWNLOAD,
            "自动更新",
            "发现新版本时自动下载并安装",
            configItem=cfg.autoUpdate,
            parent=self.scrollWidget
        )
    
    def __initLayout(self):
        """ 初始化布局 """
        self.mainLayout.addWidget(self.versionCard)
        self.mainLayout.addWidget(self.changelogCard)
        self.mainLayout.addWidget(self.autoCheckUpdateCard)
        self.mainLayout.addWidget(self.autoUpdateCard)
        self.mainLayout.addStretch()
    
    def __loadChangelog(self):
        """ 加载更新日志 """
        def load():
            try:
                # 先尝试从 GitHub 获取
                changelog = get_changelog_from_github()
                if changelog:
                    logger.info("成功从 GitHub 获取更新日志")
                    return changelog
                else:
                    # GitHub 获取失败，尝试读取本地文件
                    logger.info("GitHub 获取失败，尝试读取本地更新日志")
                    changelog_path = os.path.join(BASE_DIR, 'changelog.md')
                    if os.path.exists(changelog_path):
                        try:
                            with open(changelog_path, 'r', encoding='utf-8') as f:
                                local_changelog = f.read()
                            if local_changelog.strip():
                                logger.info("成功从本地读取更新日志")
                                return local_changelog
                        except Exception as e:
                            logger.error(f"读取本地更新日志失败：{str(e)}")
                    
                    # 本地也没有，返回提示
                    return "暂无更新记录\n\n提示：更新日志文件尚未上传到 GitHub"
            except Exception as e:
                logger.error(f"加载更新日志失败：{str(e)}")
                return "加载失败"
        
        from PyQt5.QtCore import QMetaObject, Qt, Q_ARG
     
        def thread_func():
            changelog_text = load()
            # 使用 TextEdit 的 setPlainText 方法
            QMetaObject.invokeMethod(
                self.changelogContent,
                "setPlainText",
                Qt.QueuedConnection,
                Q_ARG(str, changelog_text)
            )
        
        thread = threading.Thread(target=thread_func, daemon=True)
        thread.start()
    
    def __checkUpdate(self, auto_check=False):
        """
        检查更新
        Args:
            auto_check: 是否为自动检查
        """
        if hasattr(self, 'has_new_version') and self.has_new_version:
            self.__downloadUpdate()
            return
        
        if not auto_check:
            self.checkUpdateButton.setEnabled(False)
            self.updateStatusLabel.setText("正在检查更新")
            self.updateStatusLabel.setStyleSheet("color: #0078D4;")
            self.updateStatusIcon.setStyleSheet("background-color: #0078D4; border-radius: 8px;")
        
        from PyQt5.QtCore import QMetaObject, Qt, Q_ARG
        
        def thread_func():
            try:
                result = check_version_from_github()
                
                if not result['success']:
                    if not auto_check:
                        QMetaObject.invokeMethod(
                            self.checkUpdateButton,
                            "setEnabled",
                            Qt.QueuedConnection,
                            Q_ARG(bool, True)
                        )
                        QMetaObject.invokeMethod(
                            self.updateStatusLabel,
                            "setText",
                            Qt.QueuedConnection,
                            Q_ARG(str, f"检查失败：{result['error']}")
                        )
                        color = "#FF0000"
                        QMetaObject.invokeMethod(
                            self.updateStatusLabel,
                            "setStyleSheet",
                            Qt.QueuedConnection,
                            Q_ARG(str, f"color: {color};")
                        )
                        QMetaObject.invokeMethod(
                            self.updateStatusIcon,
                            "setStyleSheet",
                            Qt.QueuedConnection,
                            Q_ARG(str, f"background-color: {color}; border-radius: 8px;")
                        )
                    return
                
                github_version = result['version']
                github_build_date = result['build_date']
                changelog = result['changelog']
                
                logger.info(f"GitHub 最新版本：{github_version} 构建日期：{github_build_date}")
                logger.info(f"当前版本：{VERSION}")
                
                has_update = (github_version != VERSION)
                
                if has_update:
                    self.has_new_version = True
                    self.new_version = github_version
                    self.build_date = github_build_date
                    self.update_url = result['update_url']
                    
                    if not auto_check:
                        QMetaObject.invokeMethod(
                            self.checkUpdateButton,
                            "setText",
                            Qt.QueuedConnection,
                            Q_ARG(str, "下载更新")
                        )
                        QMetaObject.invokeMethod(
                            self.checkUpdateButton,
                            "setIcon",
                            Qt.QueuedConnection,
                            Q_ARG(QIcon, QIcon(FIF.DOWNLOAD.value))
                        )
                        QMetaObject.invokeMethod(
                            self.checkUpdateButton,
                            "setEnabled",
                            Qt.QueuedConnection,
                            Q_ARG(bool, True)
                        )
                    
                    QMetaObject.invokeMethod(
                        self.updateStatusLabel,
                        "setText",
                        Qt.QueuedConnection,
                        Q_ARG(str, f"发现新版本：{github_version}")
                    )
                    color = "#FF8C00"
                    QMetaObject.invokeMethod(
                        self.updateStatusLabel,
                        "setStyleSheet",
                        Qt.QueuedConnection,
                        Q_ARG(str, f"color: {color};")
                    )
                    QMetaObject.invokeMethod(
                        self.updateStatusIcon,
                        "setStyleSheet",
                        Qt.QueuedConnection,
                        Q_ARG(str, f"background-color: {color}; border-radius: 8px;")
                    )
                    
                    if changelog:
                        QMetaObject.invokeMethod(
                            self.changelogContent,
                            "setPlainText",
                            Qt.QueuedConnection,
                            Q_ARG(str, changelog)
                        )
                else:
                    if not auto_check:
                        QMetaObject.invokeMethod(
                            self.updateStatusLabel,
                            "setText",
                            Qt.QueuedConnection,
                            Q_ARG(str, "已是最新版本")
                        )
                        color = "#107C10"
                        QMetaObject.invokeMethod(
                            self.updateStatusLabel,
                            "setStyleSheet",
                            Qt.QueuedConnection,
                            Q_ARG(str, f"color: {color};")
                        )
                        QMetaObject.invokeMethod(
                            self.updateStatusIcon,
                            "setStyleSheet",
                            Qt.QueuedConnection,
                            Q_ARG(str, f"background-color: {color}; border-radius: 8px;")
                        )
                        QMetaObject.invokeMethod(
                            self.checkUpdateButton,
                            "setEnabled",
                            Qt.QueuedConnection,
                            Q_ARG(bool, True)
                        )
                    
                    if changelog:
                        QMetaObject.invokeMethod(
                            self.changelogContent,
                            "setPlainText",
                            Qt.QueuedConnection,
                            Q_ARG(str, changelog)
                        )
                        
            except Exception as e:
                logger.error(f"检查更新时出错：{str(e)}")
                if not auto_check:
                    QMetaObject.invokeMethod(
                        self.checkUpdateButton,
                        "setEnabled",
                        Qt.QueuedConnection,
                        Q_ARG(bool, True)
                    )
                    QMetaObject.invokeMethod(
                        self.updateStatusLabel,
                        "setText",
                        Qt.QueuedConnection,
                        Q_ARG(str, f"检查失败：{str(e)}")
                    )
                    color = "#FF0000"
                    QMetaObject.invokeMethod(
                        self.updateStatusLabel,
                        "setStyleSheet",
                        Qt.QueuedConnection,
                        Q_ARG(str, f"color: {color};")
                    )
                    QMetaObject.invokeMethod(
                        self.updateStatusIcon,
                        "setStyleSheet",
                        Qt.QueuedConnection,
                        Q_ARG(str, f"background-color: {color}; border-radius: 8px;")
                    )
        
        thread = threading.Thread(target=thread_func, daemon=True)
        thread.start()
    
    def __downloadUpdate(self):
        """ 下载并安装更新 """
        self.checkUpdateButton.setEnabled(False)
        self.updateStatusLabel.setText("正在下载更新")
        
        update_folder = os.path.join(BASE_DIR, 'update_temp')
        download_path = os.path.join(update_folder, 'update.7z')
        
        from PyQt5.QtCore import QMetaObject, Qt, Q_ARG
        
        def download_thread():
            try:
                if os.path.exists(update_folder):
                    shutil.rmtree(update_folder)
                os.makedirs(update_folder)
                
                def progress_callback(current, total):
                    percent = (current / total) * 100
                    QMetaObject.invokeMethod(
                        self.updateStatusLabel,
                        "setText",
                        Qt.QueuedConnection,
                        Q_ARG(str, f"正在下载更新：{percent:.1f}%")
                    )
                
                logger.info(f"正在从 {self.update_url} 下载更新")
                if not download_update(download_path, progress_callback):
                    raise Exception("下载失败")
                
                QMetaObject.invokeMethod(
                    self.updateStatusLabel,
                    "setText",
                    Qt.QueuedConnection,
                    Q_ARG(str, "正在解压更新")
                )
                
                extract_folder = os.path.join(update_folder, 'extracted')
                if not extract_update(download_path, extract_folder):
                    raise Exception("解压失败")
                
                if os.path.exists(download_path):
                    os.remove(download_path)
                
                script_path = create_update_script(BASE_DIR, extract_folder)
                if not script_path:
                    raise Exception("创建更新脚本失败")
                
                import subprocess
                subprocess.Popen([script_path], creationflags=subprocess.DETACHED_PROCESS)
                
                QMetaObject.invokeMethod(
                    self.updateStatusLabel,
                    "setText",
                    Qt.QueuedConnection,
                    Q_ARG(str, "正在重启应用")
                )
                
                QApplication.instance().quit()
                
            except Exception as e:
                logger.error(f"更新失败：{str(e)}")
                QMetaObject.invokeMethod(
                    self.checkUpdateButton,
                    "setText",
                    Qt.QueuedConnection,
                    Q_ARG(str, "检查更新")
                )
                QMetaObject.invokeMethod(
                    self.checkUpdateButton,
                    "setIcon",
                    Qt.QueuedConnection,
                    Q_ARG(QIcon, QIcon(FIF.SYNC.value))
                )
                QMetaObject.invokeMethod(
                    self.checkUpdateButton,
                    "setEnabled",
                    Qt.QueuedConnection,
                    Q_ARG(bool, True)
                )
                QMetaObject.invokeMethod(
                    self.updateStatusLabel,
                    "setText",
                    Qt.QueuedConnection,
                    Q_ARG(str, f"更新失败：{str(e)}")
                )
                color = "#FF0000"
                QMetaObject.invokeMethod(
                    self.updateStatusLabel,
                    "setStyleSheet",
                    Qt.QueuedConnection,
                    Q_ARG(str, f"color: {color};")
                )
                QMetaObject.invokeMethod(
                    self.updateStatusIcon,
                    "setStyleSheet",
                    Qt.QueuedConnection,
                    Q_ARG(str, f"background-color: {color}; border-radius: 8px;")
                )
                
                self.has_new_version = False
                if os.path.exists(update_folder):
                    shutil.rmtree(update_folder, ignore_errors=True)
        
        thread = threading.Thread(target=download_thread, daemon=True)
        thread.start()


class AboutInterface(BaseScrollAreaInterface):
    """ 关于界面 """
    
    def __init__(self, parent=None):
        super().__init__("关于", parent)
        self.setObjectName("about")


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
        # 滚动区域的属性
        self.scrollArea.setWidgetResizable(False)
        self.scrollArea.setHorizontalScrollBarPolicy(Qt.ScrollBarAsNeeded)
        self.scrollArea.setVerticalScrollBarPolicy(Qt.ScrollBarAsNeeded)
        
        self.getButton = PushButton(FIF.DOWNLOAD, "获取壁纸")
        self.getButton.setFixedHeight(50)
        self.getButton.setFixedWidth(200)
        
        # 按钮
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
        
        # 程序运行时获取壁纸
        self.__getWallpaper()

    def __initLayout(self):
        """ 初始化布局 """
        # 标题
        self.wallpaperLabel.move(60, 63)

        # 按钮水平布局
        buttonLayout = QHBoxLayout()
        buttonLayout.addWidget(self.getButton)
        buttonLayout.addSpacing(10)
        buttonLayout.addWidget(self.saveButton)
        buttonLayout.addSpacing(10)
        buttonLayout.addWidget(self.selectButton)
        buttonLayout.addSpacing(10)
        buttonLayout.addWidget(self.setWallpaperButton)
        
        # 主布局
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
        """ 连接信号与槽 """
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
        # 停止当前定时器
        self.autoGetTimer.stop()
        
        # 获取时间间隔
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
            
            # 启动定时器
            self.autoGetTimer.start(interval)
    
    def __checkAutoSync(self):
        """ 检测自动同步至桌面是否启用 """
        if cfg.autoSyncToDesktop.value and self.current_wallpaper_path is not None:
            if self.last_sync_path != self.current_wallpaper_path:
                self.__setWallpaper(show_notification=False)
                self.last_sync_path = self.current_wallpaper_path
    
    def __updateAutoSyncCheckTimer(self):
        """ 更新自动同步检测定时器 """
        # 停止当前定时器
        self.autoSyncCheckTimer.stop()
        
        if cfg.autoSyncToDesktop.value:
            self.autoSyncCheckTimer.start(5000)
    
    def __updateBackgroundBlur(self):
        """ 更新背景模糊强度 """
        if hasattr(self, 'mainWindow') and self.mainWindow and hasattr(self.mainWindow, 'homeBackgroundImage'):
            if self.mainWindow.originalPixmap is not None and not self.mainWindow.originalPixmap.isNull():
                self.mainWindow.resizeEvent(None)

    def resizeEvent(self, event):
        """ 窗口大小变化时调整滚动区域大小 """
        super().resizeEvent(event)
        margin = 60
        available_width = self.width() - margin * 2
        available_height = self.height() - 240
        scroll_width = available_width
        scroll_height = min(int(scroll_width * 0.5), available_height)
        self.scrollArea.setFixedSize(scroll_width, scroll_height)

    def __getWallpaper(self):
        """ 获取壁纸 """
        logger.info("开始获取壁纸")
        try:
            wallpaper_api = cfg.wallpaperApi.value
            if wallpaper_api == "api.ltyuanfang.cn":
                url = "https://tu.ltyuanfang.cn/api/fengjing.php"
            else:
                url = "https://wp.upx8.com/api.php?content=风景"
            logger.info(f"请求壁纸URL: {url}")
            response = requests.get(url, stream=True)
            
            if response.status_code == 200:
                logger.info(f"壁纸请求成功，状态码: {response.status_code}")
                # 保存文件
                wallpaper_dir = os.path.join(BASE_DIR, 'wallpaper')
                if not os.path.exists(wallpaper_dir):
                    os.makedirs(wallpaper_dir)
                    logger.info(f"创建壁纸目录: {wallpaper_dir}")
                
                current_date = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
                wallpaper_path = os.path.join(wallpaper_dir, f'wallpaper_{current_date}.jpg')
                
                with open(wallpaper_path, 'wb') as f:
                    f.write(response.content)
                logger.info(f"壁纸已保存到: {wallpaper_path}")
                
                # 管理壁纸保存量
                save_limit = cfg.wallpaperSaveLimit.value
                logger.info(f"管理壁纸保存量，限制: {save_limit}")
                self.__manageWallpaperLimit(wallpaper_dir, save_limit)
                
                self.current_pixmap = QPixmap(wallpaper_path)
                self.current_wallpaper_path = wallpaper_path
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
                    f"壁纸已下载到: {wallpaper_path}",
                    duration=5000,
                    parent=self
                )
                
                if cfg.autoSyncToDesktop.value:
                    logger.info("自动同步到桌面已启用")
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
            logger.error(f"获取壁纸失败: {str(e)}")
            InfoBar.error(
                "错误",
                f"获取壁纸失败: {str(e)}",
                duration=5000,
                parent=self
            )
    
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
        logger.info("开始设置桌面壁纸")
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

class MainWindow(FluentWindow):
    """ 主窗口 """

    def __init__(self):
        logger.info("开始初始化主窗口")
        super().__init__()
        logger.info("父类构造函数调用完成")
        
        setTheme(cfg.themeMode.value)
        logger.info(f"主题已设置为：{cfg.themeMode.value}")
        
        # 设置窗口图标
        icon_path = get_resource_path(os.path.join("resource", "icons", "CY.png"))
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))
            logger.info(f"窗口图标已设置: {icon_path}")
        else:
            logger.warning("窗口图标文件不存在")
        
        logger.info("开始初始化主界面导航")
        self.initMainNavigation()
        logger.info("主界面导航初始化完成")
        
        logger.info("开始初始化设置导航")
        self.initSettingsNavigation()
        logger.info("设置导航初始化完成")
        
        self.setWindowTitle(APP_NAME)
        logger.info(f"窗口标题已设置为: {APP_NAME}")
        
        self.resize(1100, 700)
        self.setMinimumSize(400, 300)
        self.moveToCenter()
        logger.info(f"窗口大小已设置: 1100x700，最小大小: 400x300")
        
        # 初始化系统托盘
        logger.info("开始初始化系统托盘")
        self.initSystemTray()
        logger.info("系统托盘初始化完成")
        
        # 时钟更新定时器
        logger.info("开始初始化时钟更新定时器")
        self.clockTimer = QTimer(self)
        self.clockTimer.timeout.connect(self.__updateClock)
        self.clockTimer.start(1000)
        cfg.showClockSeconds.valueChanged.connect(self.__updateClock)
        cfg.showLunarCalendar.valueChanged.connect(self.__updateClock)
        cfg.clockColor.valueChanged.connect(self.updateClockStyle)
        cfg.clockSize.valueChanged.connect(self.updateClockStyle)
        cfg.dateSize.valueChanged.connect(self.updateClockStyle)
        cfg.poetrySize.valueChanged.connect(self.updateClockStyle)
        cfg.weatherSize.valueChanged.connect(self.updateClockStyle)
        cfg.weatherIconSize.valueChanged.connect(self.__updateWeatherIcon)
        self.__updateClock()
        logger.info("时钟更新定时器初始化完成")
        
        # 诗词更新定时器
        logger.info("开始初始化诗词更新定时器")
        self.poetryTimer = QTimer(self)
        self.poetryTimer.timeout.connect(self.__updatePoetry)
        cfg.showPoetry.valueChanged.connect(self.__updatePoetry)
        cfg.poetryApiUrl.valueChanged.connect(self.__updatePoetry)
        cfg.poetryUpdateInterval.valueChanged.connect(self.__updatePoetryInterval)
        self.__updatePoetryInterval()
        logger.info("诗词更新定时器初始化完成")
        
        # 天气更新定时器
        logger.info("开始初始化天气更新定时器")
        self.weatherTimer = QTimer(self)
        self.weatherTimer.timeout.connect(self.__updateWeather)
        cfg.weatherUpdateInterval.valueChanged.connect(self.__updateWeatherInterval)
        
        # 初始更新天气
        self.__updateWeatherInterval()
        logger.info("天气更新定时器初始化完成")

        # 同步自启动状态
        logger.info("开始同步开机自启动状态")
        sync_auto_start_with_config()
        
        # 连接配置变化信号到设置函数
        cfg.autoStart.valueChanged.connect(lambda value: set_auto_start(value))
        
        # 空闲检测定时器
        logger.info("开始初始化空闲检测定时器")
        self.idleTimer = QTimer(self)
        self.idleTimer.timeout.connect(self.__checkIdle)
        self.lastMouseActivity = QTime.currentTime()
        self.isMinimized = False
        self.idleCheckInterval = 10000  # 10 秒
        self.hasTriggeredAutoOpen = False
        cfg.autoOpenOnIdle.valueChanged.connect(self.__updateIdleTimer)
        cfg.idleMinutes.valueChanged.connect(self.__updateIdleTimer)
        self.__updateIdleTimer()
        logger.info("空闲检测定时器初始化完成")
        
        logger.info("主窗口初始化完成!")
    
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
        
        # 窗口隐藏时，检测全局鼠标活动
        # Windows API
        class LASTINPUTINFO(ctypes.Structure):
            _fields_ = [("cbSize", ctypes.c_uint),
                       ("dwTime", ctypes.c_uint)]
        
        try:
            last_input = LASTINPUTINFO()
            last_input.cbSize = ctypes.sizeof(LASTINPUTINFO)
            ctypes.windll.user32.GetLastInputInfo(ctypes.byref(last_input))
            ticks = ctypes.windll.kernel32.GetTickCount()
            idle_time_ms = (ticks - last_input.dwTime)
            
            idle_minutes = cfg.idleMinutes.value
            idle_threshold = idle_minutes * 60 * 1000
            
            # 系统空闲时间超过阈值且未触发过时触发
            if idle_time_ms > idle_threshold and not self.hasTriggeredAutoOpen:
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
    
    def show(self):
        """ 显示窗口 """
        logger.info("显示主窗口")
        super().show()
        
        if cfg.autoCheckUpdate.value:
            logger.info("自动检查更新已启用，检查更新中")
            if hasattr(self, 'updateInterface'):
                QTimer.singleShot(1000, lambda: self.updateInterface._UpdateInterface__checkUpdate(auto_check=True))
    
    def hide(self):
        """ 隐藏窗口 """
        logger.info("隐藏主窗口")
        self.hasTriggeredAutoOpen = False
        super().hide()
    
    def closeEvent(self, event):
        """ 关闭事件处理 """
        logger.info("关闭事件触发")
        if cfg.closeAction.value == "minimize":
            # 最小化到托盘
            logger.info("关闭行为: 最小化到托盘")
            event.ignore()
            self.hide()
            self.tray_icon.showMessage(
                APP_NAME,
                "应用已最小化到系统托盘",
                QSystemTrayIcon.Information,
                2000
            )
        else:
            # 退出应用
            logger.info("关闭行为: 退出应用")
            QApplication.quit()
    
    def onCurrentInterfaceChanged(self, index):
        """ 当前界面切换事件 """
        if hasattr(self, '_subInterfaces'):
            if 0 <= index < len(self._subInterfaces):
                interface = self._subInterfaces[index]
                logger.info(f"切换到界面: {interface.text()}")

    def initMainNavigation(self):
        """ 初始化主界面导航 """
        logger.info("开始初始化主界面导航")
        home = QWidget()
        home.setObjectName("home")
        logger.info("创建主界面对象")
        
        # 创建主界面的照片显示控件
        logger.info("创建背景图片控件")
        self.homeBackgroundImage = QLabel()
        self.homeBackgroundImage.setAlignment(Qt.AlignCenter)
        self.originalPixmap = None
        
        # 时钟和日期标签
        logger.info("创建时钟和日期标签")
        self.clockLabel = QLabel("00:00:00")
        self.clockLabel.setAlignment(Qt.AlignCenter)
        
        self.dateLabel = QLabel("")
        self.dateLabel.setAlignment(Qt.AlignCenter)
        
        # 天气温度标签
        logger.info("创建天气温度标签")
        self.weatherTempLabel = QLabel("")
        self.weatherTempLabel.setAlignment(Qt.AlignVCenter | Qt.AlignRight)
        self.weatherTempLabel.setStyleSheet("""
            color: #FFFFFF; 
            font-size: 14px; 
            font-weight: bold; 
            font-family: 'Microsoft YaHei';
            background-color: transparent;
        """)
        
        # 天气图标
        logger.info("创建天气图标")
        self.weatherIconLabel = QLabel("")
        self.weatherIconLabel.setAlignment(Qt.AlignTop | Qt.AlignRight)
        self.weatherIconLabel.setStyleSheet("background-color: transparent;")
        
        # 诗词标签
        logger.info("创建诗词标签")
        self.poetryLabel = QLabel("")
        self.poetryLabel.setAlignment(Qt.AlignBottom | Qt.AlignHCenter)
        self.poetryLabel.setStyleSheet("""
            color: #FFFFFF; 
            font-size: 16px; 
            font-weight: bold; 
            font-family: 'Microsoft YaHei';
            background-color: transparent;
        """)
        self.poetryLabel.setWordWrap(False)
        
        logger.info("更新时钟样式")
        self.updateClockStyle()
        
        # 时钟容器
        logger.info("创建时钟容器")
        clockContainer = QWidget()
        clockLayout = QVBoxLayout(clockContainer)
        clockLayout.setAlignment(Qt.AlignTop)
        clockLayout.setContentsMargins(0, 100, 0, 0)
        clockLayout.setSpacing(0)  # 时钟和日期之间的间距
        
        clockLayout.addWidget(self.clockLabel)
        clockLayout.addWidget(self.dateLabel)
        clockContainer.setStyleSheet("background-color: transparent;")
        
        # 天气容器
        logger.info("创建天气容器")
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
        logger.info("创建诗词容器")
        poetryContainer = QWidget()
        poetryLayout = QVBoxLayout(poetryContainer)
        poetryLayout.setAlignment(Qt.AlignBottom)
        poetryLayout.setContentsMargins(0, 0, 0, 20)  # 最后一个为底部向上预留
        poetryLayout.addWidget(self.poetryLabel)
        poetryContainer.setStyleSheet("background-color: transparent;")
    
        logger.info("创建网格布局")
        gridLayout = QGridLayout()
        gridLayout.setContentsMargins(0, 0, 0, 0)
        gridLayout.addWidget(self.homeBackgroundImage, 0, 0, 1, 1)
        gridLayout.addWidget(clockContainer, 0, 0, 1, 1)
        gridLayout.addWidget(weatherContainer, 0, 0, 1, 1)
        gridLayout.addWidget(poetryContainer, 0, 0, 1, 1)
        
        gridWidget = QWidget()
        gridWidget.setLayout(gridLayout)
        
        # 主界面布局
        logger.info("设置主界面布局")
        homeLayout = QVBoxLayout(home)
        homeLayout.setAlignment(Qt.AlignCenter)
        homeLayout.setContentsMargins(0, 0, 0, 0)
        homeLayout.addWidget(gridWidget)
        
        logger.info("添加主界面到导航")
        self.addSubInterface(home, FIF.HOME, "主界面")
        
        logger.info("创建壁纸界面")
        self.wallpaper = WallpaperInterface(mainWindow=self)
        self.wallpaper.setObjectName("wallpaper")
        logger.info("添加壁纸界面到导航")
        self.addSubInterface(self.wallpaper, FIF.PHOTO, "壁纸")
        
        logger.info("主界面导航初始化完成")

    def initSettingsNavigation(self):
        """ 初始化设置导航 """
        logger.info("开始初始化设置导航")
        
        logger.info("创建设置界面")
        self.settingInterface = SettingInterface(parent=self)
        self.settingInterface.setObjectName("setting")
        logger.info("添加设置界面到导航")
        self.addSubInterface(self.settingInterface, FIF.SETTING, "设置", NavigationItemPosition.BOTTOM)
        
        logger.info("创建更新界面")
        self.updateInterface = UpdateInterface(parent=self)
        logger.info("添加更新界面到导航")
        self.addSubInterface(self.updateInterface, FIF.SYNC, "更新", NavigationItemPosition.BOTTOM)
        
        logger.info("创建关于界面")
        self.aboutInterface = AboutInterface(parent=self)
        logger.info("添加关于界面到导航")
        self.addSubInterface(self.aboutInterface, FIF.INFO, "关于", NavigationItemPosition.BOTTOM)
        
        # 连接主题切换信号
        cfg.themeChanged.connect(self.updateInterface._onThemeChanged)
        cfg.themeChanged.connect(self.wallpaper._onThemeChanged)
        
        logger.info("设置导航初始化完成")

    def resizeEvent(self, event):
        """ 窗口大小变化时调整图片大小 """
        super().resizeEvent(event)
        if hasattr(self, 'homeBackgroundImage') and self.originalPixmap is not None:
            available_width = self.width() - 50
            available_height = self.height()
            
            # 从原始图片重新缩放
            if not self.originalPixmap.isNull():
                scaled_pixmap = self.originalPixmap.scaled(available_width, available_height, Qt.IgnoreAspectRatio, Qt.SmoothTransformation)
                
                # 应用模糊效果
                blur_effect = QGraphicsBlurEffect()
                blur_radius = cfg.backgroundBlurRadius.value
                blur_effect.setBlurRadius(blur_radius)
                self.homeBackgroundImage.setGraphicsEffect(blur_effect)
                
                self.homeBackgroundImage.setPixmap(scaled_pixmap)
                
                QApplication.processEvents()

    def moveToCenter(self):
        """ 移动窗口到屏幕中央 """
        desktop = QApplication.desktop().availableGeometry()
        w, h = desktop.width(), desktop.height()
        self.move(w//2 - self.width()//2, h//2 - self.height()//2)
    
    def __updateClock(self):
        """ 更新时钟显示 """
        currentTime = QTime.currentTime()
        currentDate = QDate.currentDate()
        
        if cfg.showClockSeconds.value:
            timeString = currentTime.toString("HH:mm:ss")
        else:
            timeString = currentTime.toString("HH:mm")
        self.clockLabel.setText(timeString)
        
        # 公历日期
        solarString = currentDate.toString("yyyy 年 M 月 d 日 dddd")
        
        # 根据配置决定是否显示农历
        if cfg.showLunarCalendar.value:
            # 农历日期
            try:
                # 将 QDate 转换为 datetime.datetime 对象
                py_datetime = datetime.datetime(currentDate.year(), currentDate.month(), currentDate.day(), 0, 0, 0)
                lunar = cnlunar.Lunar(py_datetime)
                lunarMonthCn = lunar.lunarMonthCn
                lunarDayCn = lunar.lunarDayCn
                # 去掉月份中的"大"、"小"字
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
            font-family: 'Segoe UI', 'Microsoft YaHei';
            background-color: transparent;
        """)
        
        self.dateLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {date_size}px; 
            font-weight: bold; 
            font-family: 'Microsoft YaHei';
            background-color: transparent;
        """)
        
        self.poetryLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {poetry_size}px; 
            font-weight: bold; 
            font-family: 'Microsoft YaHei';
            background-color: transparent;
        """)
        
        self.weatherTempLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {weather_size}px; 
            font-weight: bold; 
            font-family: 'Microsoft YaHei';
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
        logger.debug("开始更新诗词")
        if not cfg.showPoetry.value:
            logger.debug("诗词显示已禁用")
            self.poetryLabel.setText("")
            self.poetryLabel.hide()
            return
        
        self.poetryLabel.show()
        
        try:
            api_url = cfg.poetryApiUrl.value
            logger.debug(f"诗词 API URL: {api_url}")
            response = requests.get(api_url, timeout=10)
            
            if response.status_code == 200:
                logger.debug(f"诗词 API 请求成功，状态码: {response.status_code}")
                # 尝试解析为 JSON
                try:
                    data = response.json()
                    logger.debug(f"诗词 API 返回数据: {data}")
                    if data.get('success') and 'data' in data:
                        poetry_data = data['data']
                        content = poetry_data.get('content', '')
                        author = poetry_data.get('author', '')
                        origin = poetry_data.get('origin', '')

                        poetry_text = f"「{content}」"
                        if author or origin:
                            poetry_text += f"\n——{author if author else ''}《{origin}》" if origin else f"\n——{author if author else ''}"
                        
                        self.poetryLabel.setText(poetry_text)
                        logger.info(f"已更新诗词: {content}")
                    else:
                        logger.error(f"诗词 API 返回数据格式错误：{data}")
                        self.poetryLabel.setText("")
                except Exception as json_error:
                    logger.debug(f"JSON解析失败，使用文本模式: {json_error}")
                    poetry_text = response.text.strip()
                    self.poetryLabel.setText(poetry_text)
                    logger.info(f"已更新诗词 (文本模式): {poetry_text[:50]}...")
            else:
                logger.error(f"诗词 API 请求失败，状态码：{response.status_code}")
                self.poetryLabel.setText("")
        except Exception as e:
            logger.error(f"诗词更新失败：{e}")
            self.poetryLabel.setText("")
    
    def __updateWeather(self):
        """ 更新天气显示 """
        logger.info("获取天气数据")
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
                logger.info("已获取天气数据")
            else:
                logger.error(f"天气 API 请求失败，状态码：{response.status_code}，响应内容：{response.text}")
        except Exception as e:
            logger.error(f"天气更新失败：{e}")
    
    def __updateWeatherIcon(self):
        """ 更新天气图标大小 """
        try:
            if not hasattr(self, 'current_weather_code'):
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
    # 检查启动参数
    auto_start_launch = is_auto_start_launch()
    if auto_start_launch:
        print("检测到通过开机自启动启动")
    
    QApplication.setHighDpiScaleFactorRoundingPolicy(Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)
    QApplication.setAttribute(Qt.AA_EnableHighDpiScaling)
    QApplication.setAttribute(Qt.AA_UseHighDpiPixmaps)
    
    extract_bundled_files()
    
    app = QApplication(sys.argv)
    
    # 检查是否已经有实例在运行
    if not check_single_instance():
        # 加载翻译
        locale = QLocale(QLocale.Chinese, QLocale.China)
        fluentTranslator = FluentTranslator(locale)
        app.installTranslator(fluentTranslator)
        
        # 创建一个全屏临时窗口作为父窗口
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
    logger.info(f"读取日志禁用配置: {cfg.disableLog.value}")
    logger.info(f"读取重复启动开关配置: {cfg.allowMultipleInstances.value}")
    logger.info(f"读取主题配置设置: {cfg.themeMode.value}")
    logger.info(f"读取颜色配置设置: {cfg.themeColor.value.name() if hasattr(cfg.themeColor.value, 'name') else cfg.themeColor.value}")
    logger.info(f"读取日志级别配置: {cfg.logLevel.value}")
    logger.info(f"读取日志数量上限配置: {cfg.logMaxCount.value}")
    logger.info(f"读取日志时间上限配置: {cfg.logMaxDays.value}")
    logger.info(f"读取关闭事件行为配置: {cfg.closeAction.value}")

    locale = QLocale(QLocale.Chinese, QLocale.China)
    fluentTranslator = FluentTranslator(locale)
    app.installTranslator(fluentTranslator)

    font_dir = get_resource_path(os.path.join("font", "HarmonyOS_Sans"))
    font_files = [
        "HarmonyOS_Sans_Thin.ttf",
        "HarmonyOS_Sans_Light.ttf",
        "HarmonyOS_Sans_Regular.ttf",
        "HarmonyOS_Sans_Medium.ttf",
        "HarmonyOS_Sans_Bold.ttf",
        "HarmonyOS_Sans_Black.ttf"
    ]

    for font_file in font_files:
        font_path = os.path.join(font_dir, font_file)
        if os.path.exists(font_path):
            QFontDatabase.addApplicationFont(font_path)

    QApplication.setFont(QFont("HarmonyOS Sans SC", 10))
    logger.info("字体已设置为：HarmonyOS Sans SC")
    
    # 设置全局异常钩子
    setup_exception_hook()
    logger.info("已设置全局异常钩子")

    window = MainWindow()
    
    # 根据启动方式决定窗口显示状态
    if auto_start_launch:
        # 如果是开机自启动，最小化到托盘
        logger.info("开机自启动模式：最小化到系统托盘")
        window.hide()
        # 确保托盘图标显示
        if hasattr(window, 'tray_icon') and window.tray_icon:
            window.tray_icon.show()
            logger.info("系统托盘图标已显示")
    else:
        window.show()
        logger.info("正常启动模式：显示主窗口")
    
    logger.info(f"{APP_NAME}版本信息：")
    logger.info(f"版本号：{VERSION} 构建日期：{BUILD_DATE}")
    logger.info(f"{APP_NAME}环境信息：")
    logger.info(f"系统版本：Windows {platform.version()} Python 版本：{platform.python_version()}")
    logger.info(f"软件运行路径：{BASE_DIR}")
    def fetch_latest_version():
        try:
            github_version, github_build_date = get_version_from_github()
            if github_version and github_build_date:
                logger.info(f"GitHub 最新版本：{github_version} 构建日期：{github_build_date}")
                if github_version != VERSION:
                    logger.info(f"发现新版本：{github_version}，当前版本：{VERSION}")
                    if cfg.autoCheckUpdate.value:
                        from PyQt5.QtCore import QTimer
                        def show_new_version_info():
                            InfoBar.info(
                                title="发现新版本",
                                content=f"GitHub 最新版本：{github_version} (构建日期：{github_build_date})\n当前版本：{VERSION}\n请在'更新'页面检查更新。",
                                duration=10000,
                                parent=window
                            )
                        QTimer.singleShot(3000, show_new_version_info)
        except Exception as e:
            logger.error(f"获取 GitHub 版本信息失败：{str(e)}")
    
    # 后台线程
    version_thread = threading.Thread(target=fetch_latest_version, daemon=True)
    version_thread.start()
    
    sys.exit(app.exec_())
