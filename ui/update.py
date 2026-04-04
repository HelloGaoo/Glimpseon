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
软件更新界面模块
"""

from PyQt5.QtCore import Qt, QMetaObject, Q_ARG, QTimer
from PyQt5.QtWidgets import QLabel, QVBoxLayout, QHBoxLayout, QWidget
from PyQt5.QtGui import QPixmap, QIcon
from qfluentwidgets import (
    CardWidget, FluentIcon as FIF, PrimaryPushButton, PushButton,
    InfoBar, isDarkTheme, ScrollArea, SmoothScrollArea, ExpandLayout, Theme,
    TextEdit, SwitchSettingCard
)

from .base_scroll_area import BaseScrollAreaInterface

from version import VERSION, BUILD_DATE
from core.updater import check_version_from_github, get_changelog_from_github
from core.config import cfg
from core.constants import get_resource_path, BASE_DIR

import threading
import logging
import os

logger = logging.getLogger(__name__)


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
    
    def __setUpdateStatus(self, status: str):
        """ 设置更新状态样式 """
        colors = {
            'checking': ('#0078D4', '#0078D4'),
            'error': ('#FF0000', '#FF0000'),
            'update_available': ('#FF8C00', '#FF8C00'),
            'latest': ('#107C10', '#107C10'),
            'downloading': ('#0078D4', '#0078D4'),
        }
        
        text_color, icon_color = colors.get(status, ('#999999', '#999999'))
        
        self.updateStatusLabel.setStyleSheet(f"color: {text_color};")
        self.updateStatusIcon.setStyleSheet(f"background-color: {icon_color}; border-radius: 8px;")
    
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
        self.updateStatusIcon.setObjectName('updateStatusIcon')
        self.updateStatusLabel = QLabel("已就绪", self.versionCard)
        self.updateStatusLabel.setObjectName('updateStatusLabel')
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
        
        def thread_func():
            changelog_text = load()
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
        
        check_type = "自动检查" if auto_check else "手动检查"
        logger.info(f"{check_type}：开始检查版本")
        
        if not auto_check:
            self.checkUpdateButton.setEnabled(False)
            self.updateStatusLabel.setText("正在检查更新")
            self.__setUpdateStatus('checking')
        
        def do_check():
            try:
                result = check_version_from_github()
                
                if not result['success']:
                    logger.warning(f"{check_type}：检查版本失败 - {result.get('error', '未知错误')}")
                    if not auto_check:
                        self.checkUpdateButton.setEnabled(True)
                        self.updateStatusLabel.setText(f"检查失败：{result.get('error', '未知错误')}")
                        self.__setUpdateStatus('error')
                    return
                
                github_version = result['version']
                github_build_date = result['build_date']
                changelog = result['changelog']
                
                logger.info(f"{check_type}：GitHub 最新版本：{github_version} (构建日期：{github_build_date})，更新日志长度：{len(changelog) if changelog else 0}，当前版本：{VERSION}")
                
                has_update = (github_version != VERSION)
                
                if has_update:
                    logger.info(f"{check_type}：发现新版本 {github_version}")
                    
                    self.has_new_version = True
                    self.new_version = github_version
                    self.build_date = github_build_date
                    self.update_url = result['update_url']
                    
                    self.updateStatusLabel.setText(f"发现新版本：{github_version}")
                    self.__setUpdateStatus('update_available')
                    
                    if not auto_check:
                        self.checkUpdateButton.setText("下载更新")
                        self.checkUpdateButton.setIcon(FIF.DOWNLOAD)
                        self.checkUpdateButton.setEnabled(True)
                    
                    if changelog:
                        self.changelogContent.setPlainText(changelog)
                    
                    if auto_check and cfg.autoUpdate.value:
                        logger.info("自动检查：启用自动更新，开始下载")
                        QTimer.singleShot(2000, lambda: self.__downloadUpdate(auto_update=True))
                        
                else:
                    logger.info(f"{check_type}：已是最新版本")
                    
                    self.updateStatusLabel.setText("已是最新版本")
                    self.__setUpdateStatus('latest')
                    
                    if not auto_check:
                        self.checkUpdateButton.setEnabled(True)
                    
                    if changelog:
                        self.changelogContent.setPlainText(changelog)
                
            except Exception as e:
                logger.error(f"{check_type}：检查更新时出错 - {str(e)}")
                if not auto_check:
                    self.checkUpdateButton.setEnabled(True)
                    self.updateStatusLabel.setText(f"更新 UI 失败：{str(e)}")
        
        QTimer.singleShot(100, do_check)
    
    def __downloadUpdate(self, auto_update=False):
        """下载并安装更新"""
        self.checkUpdateButton.setEnabled(False)
        self.updateStatusLabel.setText("正在下载更新")
        self.__setUpdateStatus('downloading')
        
        update_folder = os.path.join(BASE_DIR, 'update_temp')
        download_path = os.path.join(update_folder, 'update.7z')
        backup_folder = os.path.join(BASE_DIR, 'update_backup')
        
        def download_thread():
            try:
                # 清理旧的更新文件夹
                if os.path.exists(update_folder):
                    shutil.rmtree(update_folder)
                os.makedirs(update_folder)
                
                # 下载进度回调
                def progress_callback(current, total):
                    percent = (current / total) * 100
                    QMetaObject.invokeMethod(
                        self.updateStatusLabel,
                        "setText",
                        Qt.QueuedConnection,
                        Q_ARG(str, f"正在下载更新：{percent:.1f}%")
                    )
                
                logger.info(f"正在从 {self.update_url} 下载更新")
                download_success = False
                max_download_retries = 3
                
                for retry in range(max_download_retries):
                    try:
                        if retry > 0:
                            logger.info(f"下载更新重试 {retry}/{max_download_retries}")
                            QMetaObject.invokeMethod(
                                self.updateStatusLabel,
                                "setText",
                                Qt.QueuedConnection,
                                Q_ARG(str, f"下载失败，重试 {retry}/{max_download_retries}...")
                            )
                        
                        if download_update(download_path, progress_callback):
                            download_success = True
                            break
                        else:
                            if os.path.exists(download_path):
                                os.remove(download_path)
                    except Exception as e:
                        logger.warning(f"下载尝试 {retry + 1} 失败：{str(e)}")
                        if os.path.exists(download_path):
                            os.remove(download_path)
                
                if not download_success:
                    raise Exception("下载更新失败，已达到最大重试次数")

                QMetaObject.invokeMethod(
                    self.updateStatusLabel,
                    "setText",
                    Qt.QueuedConnection,
                    Q_ARG(str, "正在解压更新")
                )
                
                extract_folder = os.path.join(update_folder, 'extracted')
                if not extract_update(download_path, extract_folder):
                    raise Exception("解压更新失败")
                
                # 删除下载的压缩包
                if os.path.exists(download_path):
                    os.remove(download_path)
                
                # 创建备份
                if auto_update:
                    QMetaObject.invokeMethod(
                        self.updateStatusLabel,
                        "setText",
                        Qt.QueuedConnection,
                        Q_ARG(str, "正在备份当前版本")
                    )
                    
                    try:
                        if os.path.exists(backup_folder):
                            shutil.rmtree(backup_folder)
                        shutil.copytree(BASE_DIR, backup_folder, 
                                     ignore=shutil.ignore_patterns('update_temp', 'update_backup', 'logs', '*.log'))
                        logger.info("已创建版本备份")
                    except Exception as e:
                        logger.warning(f"创建备份失败：{str(e)}")

                script_path = create_update_script(BASE_DIR, extract_folder)
                if not script_path:
                    raise Exception("创建更新脚本失败")
                QMetaObject.invokeMethod(
                    self.updateStatusLabel,
                    "setText",
                    Qt.QueuedConnection,
                    Q_ARG(str, "正在准备更新，程序即将重启")
                )
                
                subprocess.Popen(
                    f'cmd /c start "ClassLively Update" /MIN "{script_path}"',
                    shell=True,
                    creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.DETACHED_PROCESS
                )
                
                logger.info("更新脚本已启动，准备退出应用")
                
                QMetaObject.invokeMethod(
                    self.updateStatusLabel,
                    "setText",
                    Qt.QueuedConnection,
                    Q_ARG(str, "更新完成，正在重启应用")
                )
                
                QApplication.instance().quit()
                
            except Exception as e:
                logger.error(f"更新失败：{str(e)}")
                if os.path.exists(update_folder):
                    try:
                        shutil.rmtree(update_folder)
                    except Exception:
                        pass
                QMetaObject.invokeMethod(
                    self.checkUpdateButton,
                    "setText",
                    Qt.QueuedConnection,
                    Q_ARG(str, "重试更新")
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
                QMetaObject.invokeMethod(
                    self.updateStatusLabel,
                    "setStyleSheet",
                    Qt.QueuedConnection,
                    Q_ARG(str, "color: #FF0000;")
                )
                QMetaObject.invokeMethod(
                    self.updateStatusIcon,
                    "setStyleSheet",
                    Qt.QueuedConnection,
                    Q_ARG(str, "background-color: #FF0000; border-radius: 8px;")
                )
                
                self.has_new_version = False
        
        thread = threading.Thread(target=download_thread, daemon=True)
        thread.start()


