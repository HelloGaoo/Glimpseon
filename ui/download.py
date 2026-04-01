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
软件下载界面模块
"""

from PyQt5.QtCore import Qt, pyqtSlot, QMetaObject, Q_ARG, QTimer
from PyQt5.QtWidgets import QLabel, QVBoxLayout, QHBoxLayout, QWidget, QGridLayout
from PyQt5.QtGui import QPixmap, QIcon
from qfluentwidgets import (
    CardWidget, CheckBox, ComboBox, FluentIcon as FIF, PrimaryPushButton, PushButton,
    InfoBar, isDarkTheme, ScrollArea, SmoothScrollArea, ExpandLayout, Theme,
    RadioButton, ProgressRing, MessageBox, qconfig
)
from core.downloader import DOWNLOAD_SOURCES, DEFAULT_SOURCE, set_download_source

from .base_scroll_area import BaseScrollAreaInterface

from core.downloader import Downloader
from concurrent.futures import ThreadPoolExecutor
from core.config import cfg, url_dir
from core.constants import get_resource_path
from core.logger import logger

import os
import threading


class DownloadInterface(BaseScrollAreaInterface):
    """ 软件下载界面 """
    
    def __init__(self, parent=None):
        super().__init__("软件下载", parent)
        self.setObjectName("download")
        
        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.mainLayout.setContentsMargins(60, 0, 60, 40)
        self.mainLayout.setSpacing(16)
        
        self.softwareList = []
        self.downloader = Downloader(logger)
        # 线程池
        self.download_executor = ThreadPoolExecutor(max_workers=4)
        
        self.__initWidgets()
        self.__initLayout()
        self.__setQss()
        self.__connectSignalToSlot()
    
    def __connectSignalToSlot(self):
        """ 连接信号与槽 """
        cfg.themeChanged.connect(self._onThemeChanged)
        self.singleModeButton.toggled.connect(self.__handleModeChange)
        self.multiModeButton.toggled.connect(self.__handleModeChange)
        self.startButton.clicked.connect(self.__handleStartDownload)
        self.sourceComboBox.currentTextChanged.connect(self.__handleSourceChange)
    
    def __handleSourceChange(self, source_name):
        source_key = None
        for key, value in DOWNLOAD_SOURCES.items():
            if value["name"] == source_name:
                source_key = key
                break
        if source_key:
            qconfig.set(cfg.downloadSource, source_key)
            logger.info(f"下载源已保存到配置：{source_name} ({source_key})")
            set_download_source(source_key)
    
    def __get_download_url(self, cache_file):
        source_index = self.sourceComboBox.currentIndex()
        source_keys = list(DOWNLOAD_SOURCES.keys())
        
        # 有url直接用
        if 'url' in cache_file:
            return cache_file['url']
        
        if 'github_path' in cache_file:
            github_path = cache_file['github_path']
            source_key = source_keys[source_index]
            source_config = DOWNLOAD_SOURCES[source_key]
            prefix = source_config["prefix"]
            return f'{prefix}{github_path}'
        
        return None
    
    def __setQss(self):
        """ 设置样式表 """
        self.scrollWidget.setObjectName('scrollWidget')
        self.titleLabel.setObjectName('settingLabel')
        
        theme = 'dark' if isDarkTheme() else 'light'
        try:
            qss_path = get_resource_path(os.path.join('resource', 'qss', theme, 'download_interface.qss'))
            with open(qss_path, encoding='utf-8') as f:
                self.setStyleSheet(f.read())
        except Exception:
            pass
    
    def _onThemeChanged(self, theme: Theme):
        """ 主题变更槽函数 """
        self.__setQss()
    
    def __handleDownload(self, software_name):
        """ 处理下载按钮点击事件 """
        msg_box = MessageBox(
            "确认下载",
            f"确定要下载并安装 {software_name} 吗？",
            self
        )
        
        result = msg_box.exec_()
        if result != 1:
            return
        software_item = None
        for item in self.softwareList:
            if item['name'] == software_name:
                software_item = item
                break
        
        if software_item:
            # 显示进度环，隐藏下载按钮
            software_item['progressBar'].show()
            software_item['progressBar'].setValue(0)
            software_item['button'].hide()
            try:
                software_item['checkbox'].hide()
            except Exception:
                pass
        
        info_bar = InfoBar.success(
            "开始下载",
            f"正在下载 {software_name}，请稍候...",
            parent=self,
            duration=3000
        )
        
        def download_thread():
            try:
                processed_name = software_name.replace(" ", "").replace("[", "").replace("]", "")
                install_method_name = f"_install_{processed_name}"
                if hasattr(self.downloader, install_method_name):
                    # 从url_dir中获取下载链接
                    cache_file = None
                    for item in url_dir:
                        if item.get("filename", "").startswith(software_name):
                            cache_file = item.copy()
                            if "hash" not in cache_file:
                                cache_file["hash"] = ""
                            break
                    
                    if not cache_file:
                        QMetaObject.invokeMethod(
                            self,
                            '_show_download_error_now',
                            Qt.QueuedConnection,
                            Q_ARG(str, software_name),
                            Q_ARG(str, "未找到对应的下载链接")
                        )
                        return

                    download_url = self.__get_download_url(cache_file)
                    if not download_url:
                        QMetaObject.invokeMethod(
                            self,
                            '_show_download_error_now',
                            Qt.QueuedConnection,
                            Q_ARG(str, software_name),
                            Q_ARG(str, "无法获取下载链接")
                        )
                        return
                    
                    # 更新 cache_file 的 url
                    cache_file['url'] = download_url
                    
                    # 进度回调（接收 downloader 的 (software_name, percent)）
                    def update_progress(software_name_arg, percent, item=software_item):
                        try:
                            val = int(round(float(percent)))
                        except Exception:
                            return
                        if item:
                            QMetaObject.invokeMethod(
                                item['progressBar'], 
                                'setValue', 
                                Qt.QueuedConnection, 
                                Q_ARG(int, val)
                            )

                    # 下载完成回调
                    def on_download_complete(software_name_arg=None, item=software_item):
                        name = software_name_arg if software_name_arg else software_name
                        if item:
                            logger.info(f"{name}: 下载完成")
                    
                    # 执行安装的函数
                    def run_install():
                        try:
                            logger.info(f"{software_name}: 开始调用安装函数 {install_method_name}")
                            getattr(self.downloader, install_method_name)(
                                software_name, 
                                cache_file, 
                                progress_callback=update_progress, 
                                download_complete_callback=on_download_complete
                            )
                            QMetaObject.invokeMethod(
                                software_item['progressBar'],
                                'setValue',
                                Qt.QueuedConnection,
                                Q_ARG(int, 100)
                            )
                            QMetaObject.invokeMethod(
                                self,
                                '_show_download_complete_now',
                                Qt.QueuedConnection,
                                Q_ARG(str, software_name),
                                Q_ARG(int, 500)
                            )
                        except Exception as e:
                            logger.error(f"{software_name}: 安装函数异常 - {e}", exc_info=True)
                            QMetaObject.invokeMethod(
                                self,
                                '_show_download_error_now',
                                Qt.QueuedConnection,
                                Q_ARG(str, software_name),
                                Q_ARG(str, str(e))
                            )
                    
                    # 启动安装线程
                    thread = threading.Thread(target=run_install, daemon=True)
                    thread.start()
                else:
                    # 显示未找到安装方法的提示
                    QMetaObject.invokeMethod(
                        self,
                        '_show_download_error_now',
                        Qt.QueuedConnection,
                        Q_ARG(str, software_name),
                        Q_ARG(str, "未找到对应的安装方法")
                    )
            except Exception as e:
                QMetaObject.invokeMethod(
                    self,
                    '_show_download_error_now',
                    Qt.QueuedConnection,
                    Q_ARG(str, software_name),
                    Q_ARG(str, str(e))
                )
        
        thread = threading.Thread(target=download_thread, daemon=True)
        thread.start()
    
    def __showDownloadComplete(self, software_item, software_name):
        """ 显示下载完成 """
        if software_item:
            # 先设置为100%
            QMetaObject.invokeMethod(
                software_item['progressBar'], 
                'setValue', 
                Qt.QueuedConnection, 
                Q_ARG(int, 100)
            )
            try:
                software_item['button'].hide()
            except Exception:
                pass
            try:
                software_item['checkbox'].hide()
            except Exception:
                pass
        
        InfoBar.success(
            "安装完成",
            f"{software_name} 已成功安装！",
            parent=self,
            duration=3000
        )
    
    def __showDownloadError(self, software_item, software_name, error_msg):
        """ 显示下载错误 """
        if software_item:
            software_item['progressBar'].hide()
            # 出错时恢复按钮与复选框显示
            try:
                software_item['button'].show()
            except Exception:
                pass
            try:
                software_item['checkbox'].show()
            except Exception:
                pass
        
        InfoBar.error(
            "安装失败",
            f"{software_name} 安装失败：{error_msg}",
            parent=self,
            duration=5000
        )

    @pyqtSlot(str, str)
    def _show_download_error_now(self, software_name, error_msg):
        """显示下载错误"""
        item = None
        for it in self.softwareList:
            if it.get('name') == software_name:
                item = it
                break
        self.__showDownloadError(item, software_name, error_msg)

    @pyqtSlot(str, int)
    def _show_download_complete_now(self, software_name, delay_ms=500):
        """延迟显示完成"""
        item = None
        for it in self.softwareList:
            if it.get('name') == software_name:
                item = it
                break
        if delay_ms and delay_ms > 0:
            QTimer.singleShot(delay_ms, lambda: self.__showDownloadComplete(item, software_name))
        else:
            self.__showDownloadComplete(item, software_name)

    @pyqtSlot()
    def _clear_selected_software_now(self):
        """清空已选择的软件列表"""
        try:
            self.selectedSoftware.clear()
        except Exception:
            self.selectedSoftware = []
    
    def __initWidgets(self):
        """ 初始化控件 """
        # 模式切换控件
        self.modeContainer = QWidget(self.scrollWidget)
        self.modeContainer.setObjectName("modeContainer")
        self.modeLayout = QHBoxLayout(self.modeContainer)
        self.modeLayout.setContentsMargins(0, 10, 0, 10)
        self.modeLayout.setSpacing(20)
        
        # 模式选择组
        modeGroup = QWidget(self.modeContainer)
        modeGroup.setObjectName("modeGroup")
        modeGroupLayout = QHBoxLayout(modeGroup)
        modeGroupLayout.setContentsMargins(0, 0, 0, 0)
        modeGroupLayout.setSpacing(16)
        modeGroupLayout.setAlignment(Qt.AlignLeft | Qt.AlignVCenter)
        
        self.modeLabel = QLabel("选择模式:", modeGroup)
        self.modeLabel.setObjectName("modeLabel")
        
        self.singleModeButton = RadioButton("单选", modeGroup)
        self.singleModeButton.setChecked(True)
        
        self.multiModeButton = RadioButton("多选", modeGroup)
        
        modeGroupLayout.addWidget(self.modeLabel)
        modeGroupLayout.addWidget(self.singleModeButton)
        modeGroupLayout.addWidget(self.multiModeButton)
        
        # 下载源选择
        sourceGroup = QWidget(self.modeContainer)
        sourceGroup.setObjectName("sourceGroup")
        sourceGroupLayout = QHBoxLayout(sourceGroup)
        sourceGroupLayout.setContentsMargins(0, 0, 0, 0)
        sourceGroupLayout.setSpacing(8)
        sourceGroupLayout.setAlignment(Qt.AlignLeft | Qt.AlignVCenter)
        
        self.sourceLabel = QLabel("下载源:", sourceGroup)
        self.sourceLabel.setObjectName("sourceLabel")   
        self.sourceComboBox = ComboBox(sourceGroup)
        self.sourceComboBox.setObjectName("sourceComboBox")
        self.sourceComboBox.setFixedWidth(200)
        source_items = [v["name"] for v in DOWNLOAD_SOURCES.values()]
        self.sourceComboBox.addItems(source_items)
        
        source_key = cfg.downloadSource.value
        default_index = list(DOWNLOAD_SOURCES.keys()).index(source_key)
        self.sourceComboBox.setCurrentIndex(default_index)
        set_download_source(source_key)
        
        sourceGroupLayout.addWidget(self.sourceLabel)
        sourceGroupLayout.addWidget(self.sourceComboBox)
        
        self.startButton = PrimaryPushButton(FIF.PLAY, "开始下载", self.modeContainer)
        self.startButton.setObjectName("startButton")
        self.startButton.hide()
        
        self.modeLayout.addWidget(modeGroup)
        self.modeLayout.addWidget(sourceGroup)
        self.modeLayout.addStretch()
        self.modeLayout.addWidget(self.startButton)
        self.modeLayout.setAlignment(Qt.AlignVCenter)
        
        # 软件容器
        self.softwareContainer = QWidget(self.scrollWidget)
        self.softwareLayout = QVBoxLayout(self.softwareContainer)
        self.softwareLayout.setContentsMargins(0, 0, 0, 0)
        self.softwareLayout.setSpacing(16)
        self.currentRow = 0
        self.currentCol = 0
        self.minColumns = 2
        self.currentGridLayout = None
        
        self.selectedSoftware = []
    
    def __initLayout(self):
        """ 初始化布局 """
        self.mainLayout.addWidget(self.modeContainer)
        self.mainLayout.addWidget(self.softwareContainer)
    
    def addSection(self, title):
        """ 添加分区标题 """
        sectionLabel = QLabel(title, self.softwareContainer)
        sectionLabel.setObjectName("sectionTitleLabel")
        self.softwareLayout.addWidget(sectionLabel)
        
        gridWidget = QWidget()
        self.currentGridLayout = QGridLayout(gridWidget)
        self.currentGridLayout.setContentsMargins(0, 0, 0, 0)
        self.currentGridLayout.setSpacing(12)
        self.currentRow = 0
        self.currentCol = 0
        
        self.softwareLayout.addWidget(gridWidget)
    
    def addSoftware(self, icon_path, name, description):
        """ 添加一个软件到列表 """
        if self.currentGridLayout is None:
            self.addSection("常用软件")
        
        softwareCard = CardWidget(self.softwareContainer)
        softwareCard.setMinimumHeight(100)
        softwareCard.setMaximumHeight(100)
        softwareCard.setMinimumWidth(400)
        
        cardLayout = QHBoxLayout(softwareCard)
        cardLayout.setContentsMargins(20, 16, 20, 16)
        cardLayout.setSpacing(16)
        
        iconLabel = QLabel(softwareCard)
        iconLabel.setFixedSize(64, 64)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path).scaled(64, 64, Qt.KeepAspectRatio, Qt.SmoothTransformation)
            iconLabel.setPixmap(pixmap)
        else:
            iconLabel.setText("")
            iconLabel.setAlignment(Qt.AlignCenter)
            iconLabel.setStyleSheet("font-size: 32px;")
        
        infoLayout = QVBoxLayout()
        infoLayout.setSpacing(4)
        infoLayout.setContentsMargins(0, 8, 0, 8)
        
        nameLabel = QLabel(name, softwareCard)
        nameLabel.setObjectName("softwareNameLabel")
        nameLabel.setWordWrap(True)
        
        descLabel = QLabel(description, softwareCard)
        descLabel.setObjectName("softwareDescLabel")
        descLabel.setWordWrap(True)
        descLabel.setFixedHeight(40)
        
        infoLayout.addWidget(nameLabel)
        infoLayout.addWidget(descLabel)
        
        # 创建进度环
        progressBar = ProgressRing(softwareCard)
        progressBar.setFixedSize(60, 60)
        progressBar.setValue(0)
        progressBar.setTextVisible(True)
        progressBar.hide()
        
        # 创建下载按钮或复选框
        downloadButton = PrimaryPushButton(FIF.DOWNLOAD, "下载", softwareCard)
        downloadButton.setFixedHeight(32)
        downloadButton.setFixedWidth(100)
        
        checkbox = CheckBox(softwareCard)
        checkbox.setFixedSize(32, 32)
        checkbox.hide()  # 初始隐藏
        
        cardLayout.addWidget(iconLabel)
        cardLayout.addLayout(infoLayout, 1)
        cardLayout.addWidget(progressBar)
        cardLayout.addWidget(downloadButton)
        cardLayout.addWidget(checkbox)
        
        self.currentGridLayout.addWidget(softwareCard, self.currentRow, self.currentCol)
        
        self.softwareList.append({
            'card': softwareCard,
            'name': name,
            'button': downloadButton,
            'checkbox': checkbox,
            'progressBar': progressBar
        })
        
        downloadButton.clicked.connect(lambda: self.__handleDownload(name))
        checkbox.stateChanged.connect(lambda state, n=name: self.__handleCheckboxChange(n, state))
        
        self.currentCol += 1
        if self.currentCol >= self.minColumns:
            self.currentCol = 0
            self.currentRow += 1
    
    def __handleCheckboxChange(self, software_name, state):
        """ 处理复选框状态变更 """
        if state == Qt.Checked:
            if software_name not in self.selectedSoftware:
                self.selectedSoftware.append(software_name)
        else:
            if software_name in self.selectedSoftware:
                self.selectedSoftware.remove(software_name)
    
    def __handleModeChange(self):
        """ 处理模式切换 """
        is_single_mode = self.singleModeButton.isChecked()
        
        for software in self.softwareList:
            try:
                in_progress = software.get('progressBar') is not None and software['progressBar'].isVisible()
            except Exception:
                in_progress = False

            if in_progress:
                software['button'].hide()
                software['checkbox'].hide()
                continue

            if is_single_mode:
                software['button'].show()
                software['checkbox'].hide()
            else:
                software['button'].hide()
                software['checkbox'].show()
        
        # 显示或隐藏开始按钮
        if is_single_mode:
            self.startButton.hide()
        else:
            self.startButton.show()
    
    def __handleStartDownload(self):
        """ 处理开始下载按钮点击 """
        if not self.selectedSoftware:
            InfoBar.warning(
                "未选择软件",
                "请先选择要下载的软件",
                parent=self,
                duration=3000
            )
            return
        
        # 显示确认对话框
        software_list = "\n".join(self.selectedSoftware)
        msg_box = MessageBox(
            "确认下载",
            f"确定要下载并安装以下软件吗？\n{software_list}",
            self
        )
        
        result = msg_box.exec_()
        if result != 1:
            return
        
        # 显示下载中提示
        info_bar = InfoBar.success(
            "开始下载",
            f"正在下载 {len(self.selectedSoftware)} 个软件，请稍候...",
            parent=self,
            duration=3000
        )
        
        for software_name in self.selectedSoftware:
            for software_item in self.softwareList:
                if software_item['name'] == software_name:
                    software_item['progressBar'].show()
                    software_item['progressBar'].setValue(0)
                    software_item['button'].hide()

                    try:
                        software_item['checkbox'].hide()
                    except Exception:
                        pass
                    break
        
        # 在后台线程中执行下载和安装
        futures = []

        def _run_install_task(software_name):
            item = None
            for it in self.softwareList:
                if it.get('name') == software_name:
                    item = it
                    break
            if item:
                try:
                    QMetaObject.invokeMethod(
                        item['progressBar'],
                        'show',
                        Qt.QueuedConnection
                    )
                    QMetaObject.invokeMethod(
                        item['progressBar'],
                        'setValue',
                        Qt.QueuedConnection,
                        Q_ARG(int, 0)
                    )
                    QMetaObject.invokeMethod(
                        item['button'],
                        'hide',
                        Qt.QueuedConnection
                    )
                    try:
                        QMetaObject.invokeMethod(
                            item['checkbox'],
                            'hide',
                            Qt.QueuedConnection
                        )
                    except Exception:
                        pass
                except Exception:
                    pass

            # 查找下载链接
            cache_file = None
            for entry in url_dir:
                if entry.get('filename', '').startswith(software_name):
                    cache_file = entry.copy()
                    if 'hash' not in cache_file:
                        cache_file['hash'] = ''
                    break

            if not cache_file:
                QMetaObject.invokeMethod(
                    self,
                    '_show_download_error_now',
                    Qt.QueuedConnection,
                    Q_ARG(str, software_name),
                    Q_ARG(str, '未找到对应的下载链接')
                )
                return

            # 根据选择的下载源获取实际 URL
            download_url = self._DownloadInterface__get_download_url(cache_file)
            if not download_url:
                QMetaObject.invokeMethod(
                    self,
                    '_show_download_error_now',
                    Qt.QueuedConnection,
                    Q_ARG(str, software_name),
                    Q_ARG(str, '无法获取下载链接')
                )
                return

            # 更新 cache_file 的 url
            cache_file['url'] = download_url

            # 定义进度回调和完成回调
            def update_progress(software_name_arg, percent, item_ref=item):
                try:
                    val = int(round(float(percent)))
                except Exception:
                    return
                if item_ref:
                    QMetaObject.invokeMethod(
                        item_ref['progressBar'],
                        'setValue',
                        Qt.QueuedConnection,
                        Q_ARG(int, val)
                    )

            def on_download_complete(software_name_arg=None, item_ref=item):
                name = software_name_arg if software_name_arg else software_name
                logger.info(f"{name}: 下载完成")

            # 调用 downloader 的安装方法
            try:
                processed_name = software_name.replace(' ', '').replace('[', '').replace(']', '')
                install_method_name = f"_install_{processed_name}"
                if hasattr(self.downloader, install_method_name):
                    getattr(self.downloader, install_method_name)(
                        software_name,
                        cache_file,
                        progress_callback=update_progress,
                        download_complete_callback=on_download_complete
                    )

                    # 保证设置为100并显示完成
                    if item:
                        QMetaObject.invokeMethod(
                            item['progressBar'],
                            'setValue',
                            Qt.QueuedConnection,
                            Q_ARG(int, 100)
                        )
                    QMetaObject.invokeMethod(
                        self,
                        '_show_download_complete_now',
                        Qt.QueuedConnection,
                        Q_ARG(str, software_name),
                        Q_ARG(int, 500)
                    )
                else:
                    QMetaObject.invokeMethod(
                        self,
                        '_show_download_error_now',
                        Qt.QueuedConnection,
                        Q_ARG(str, software_name),
                        Q_ARG(str, '未找到对应的安装方法')
                    )
            except Exception as e:
                logger.error(f"{software_name}: 安装函数异常 - {e}", exc_info=True)
                QMetaObject.invokeMethod(
                    self,
                    '_show_download_error_now',
                    Qt.QueuedConnection,
                    Q_ARG(str, software_name),
                    Q_ARG(str, str(e))
                )

        for software_name in list(self.selectedSoftware):
            futures.append(self.download_executor.submit(_run_install_task, software_name))

        # 在后台等待所有任务完成，然后在主线程清空选择
        def _wait_and_clear():
            try:
                wait(futures)
            except Exception:
                pass
            QMetaObject.invokeMethod(
                self,
                '_clear_selected_software_now',
                Qt.QueuedConnection
            )

        threading.Thread(target=_wait_and_clear, daemon=True).start()


