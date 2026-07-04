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

"""
软件下载界面模块
"""

import os
import sys
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:sys.path.insert(0, _BASE_DIR)

import threading
import time
from concurrent.futures import ThreadPoolExecutor, wait
from typing import Optional

from PyQt6.QtCore import QMetaObject, Q_ARG, QUrl, Qt, QTimer, pyqtSlot
from PyQt6.QtGui import QPixmap, QDesktopServices
from PyQt6.QtWidgets import QGridLayout, QHBoxLayout, QLabel, QToolButton, QVBoxLayout, QWidget
from qfluentwidgets import (
    CardWidget,
    CheckBox,
    ComboBox,
    InfoBar,
    MessageBox,
    PrimaryPushButton,
    ProgressRing,
    PushButton,
    RadioButton,
    ScrollArea,
    Theme,
    qconfig,
)

from core.config import cfg
from core.constants import BASE_DIR, get_resPath, load_qss
from core.downloader import DOWNLOAD_SOURCES, DEFAULT_SOURCE, Downloader, set_download_src, get_source_name
from data.url_dir import url_dir
from core.utils import tr, TranslatableWidget, FUI
from core.logger import logger

from .common import BaseScrollAreaInterface, show_text_file

_software_icon_cache = {}

def get_cached_icon(icon_path: str, size: tuple = (64, 64)) -> Optional[QPixmap]:
    if icon_path in _software_icon_cache:return _software_icon_cache[icon_path]
    if not os.path.exists(icon_path):return None
    try:
        pixmap = QPixmap(icon_path)
        if pixmap.isNull():return None
        scaled = pixmap.scaled(size[0], size[1], Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
        _software_icon_cache[icon_path] = scaled
        return scaled
    except Exception:
        return None


class DownloadInterface(BaseScrollAreaInterface, TranslatableWidget):
    """ 软件下载界面 """
    
    def __init__(self, parent=None):
        super().__init__(tr("navigation.download"), parent)  # 软件下载
        self.setObjectName("download")
        
        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.mainLayout.setContentsMargins(60, 0, 60, 40)
        self.mainLayout.setSpacing(16)
        
        # 数据存储（分页用）
        self._allSoftwareData = []   # flat list: {section, icon_path, name, description, link}
        self._currentDataSection = None
        
        # 分页状态
        self.currentPage = 1
        self.totalPages = 1
        self._pageButtons = []
        
        # 运行时状态
        self.softwareList = []       # 当前页面的 widget dict（给下载逻辑用）
        self.selectedSoftware = []
        self._downloadingNames = set()  # 正在下载中的软件名
        self.downloader = Downloader(logger)
        self.download_executor = None
        self.futures = []
        
        self.__initWidgets()
        self.__initPagination()
        self.__initLayout()
        self.__setQss()
        self.__connectSignalToSlot()
        self.setup_translatable_ui()
    
    def __connectSignalToSlot(self):
        """ 连接信号与槽 """
        cfg.themeChanged.connect(self._onThemeChanged)
        self.singleModeButton.toggled.connect(self.__handleModeChange)
        self.multiModeButton.toggled.connect(self.__handleModeChange)
        self.startButton.clicked.connect(self.__handleStartDownload)
        self.sourceComboBox.currentTextChanged.connect(self.__handleSourceChange)
        self.selectAllButton.clicked.connect(self.__handleSelectAll)
        cfg.downloadItemsPerPage.valueChanged.connect(self._onItemsPerPageChanged)
    
    def __handleSourceChange(self, source_name):
        idx = self.sourceComboBox.currentIndex()
        source_key = self.sourceComboBox.itemData(idx)
        if source_key:
            qconfig.set(cfg.downloadSource, source_key)
            logger.info(f"下载源已保存到配置：{source_name} ({source_key})")
            set_download_src(source_key)
    
    def __get_url(self, cache_file):
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
        self.setStyleSheet(load_qss('download.qss'))
    
    def _onThemeChanged(self, theme: Theme):
        """ 主题变更槽函数 """
        self.__setQss()
    
    def __handleDownload(self, software_name):
        """ 处理下载按钮点击事件 """
        msg_box = MessageBox(
            tr("download.confirm_download"),  # 确认下载
            tr("download.confirm_download_single").format(name=software_name),  # 确定要下载 {name} 吗？
            self
        )
        
        result = msg_box.exec()
        if not result:
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
            self._downloadingNames.add(software_name)
        
        info_bar = InfoBar.success(
            tr("download.starting_download"),  # 开始下载
            tr("download.downloading_single").format(name=software_name),  # 正在下载 {name}...
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
                    _match_name = software_name.replace(" ", "").replace("[", "").replace("]", "")
                    for item in url_dir:
                        _match_filename = item.get("filename", "").replace(" ", "").replace("[", "").replace("]", "")
                        if _match_filename.startswith(_match_name):
                            cache_file = item.copy()
                            if "hash" not in cache_file:
                                cache_file["hash"] = ""
                            break
                    
                    if not cache_file:
                        QMetaObject.invokeMethod(
                            self,
                            '_show_download_error',
                            Qt.ConnectionType.QueuedConnection,
                            Q_ARG(str, software_name),
                            Q_ARG(str, tr("download.error_no_url"))  # 未找到下载链接  # 未找到下载链接
                        )
                        return

                    download_url = self.__get_url(cache_file)
                    if not download_url:
                        QMetaObject.invokeMethod(
                            self,
                            '_show_download_error',
                            Qt.ConnectionType.QueuedConnection,
                            Q_ARG(str, software_name),
                            Q_ARG(str, tr("download.error_cannot_get_url"))  # 无法获取下载链接  # 无法获取下载链接
                        )
                        return
                    
                    # 更新 cache_file 的 url
                    cache_file['url'] = download_url
                    
                    # 接收 downloader 的 (software_name, percent)）
                    def update_progress(software_name_arg, percent, item=software_item):
                        try:
                            val = int(round(float(percent)))
                        except Exception:
                            return
                        if item:
                            QMetaObject.invokeMethod(
                                item['progressBar'], 
                                'setValue', 
                                Qt.ConnectionType.QueuedConnection, 
                                Q_ARG(int, val)
                            )

                    # 下载完成回调
                    def _on_download_done(software_name_arg=None, item=software_item):
                        name = software_name_arg if software_name_arg else software_name
                        if item:
                            logger.info(f"{name}: 下载完成")
                    
                    # 执行安装的函数
                    def run_install():
                        try:
                            logger.info(f"{software_name}: 调用 {install_method_name}")
                            getattr(self.downloader, install_method_name)(
                                software_name, 
                                cache_file, 
                                progress_callback=update_progress, 
                                download_complete_callback=_on_download_done
                            )
                            QMetaObject.invokeMethod(
                                software_item['progressBar'],
                                'setValue',
                                Qt.ConnectionType.QueuedConnection,
                                Q_ARG(int, 100)
                            )
                            QMetaObject.invokeMethod(
                                self,
                                '_show_download_complete',
                                Qt.ConnectionType.QueuedConnection,
                                Q_ARG(str, software_name),
                                Q_ARG(int, 500)
                            )
                        except Exception as e:
                            logger.error(f"{software_name}: 安装函数异常 - {e}", exc_info=True)
                            QMetaObject.invokeMethod(
                                self,
                                '_show_download_error',
                                Qt.ConnectionType.QueuedConnection,
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
                        '_show_download_error',
                        Qt.ConnectionType.QueuedConnection,
                        Q_ARG(str, software_name),
                        Q_ARG(str, tr("download.error_no_install_method"))  # 未找到安装方法  # 未找到安装方法
                    )
            except Exception as e:
                QMetaObject.invokeMethod(
                    self,
                    '_show_download_error',
                    Qt.ConnectionType.QueuedConnection,
                    Q_ARG(str, software_name),
                    Q_ARG(str, str(e))
                )
        
        thread = threading.Thread(target=download_thread, daemon=True)
        thread.start()
    
    def __show_download_complete(self, software_item, software_name):
        """ 显示下载完成 """
        if software_item:
            # 先设置为100%
            QMetaObject.invokeMethod(
                software_item['progressBar'], 
                'setValue', 
                Qt.ConnectionType.QueuedConnection, 
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
            tr("download.install_complete"),  # 安装完成
            tr("download.install_success").format(name=software_name),  # {name} 安装成功！
            parent=self,
            duration=3000
        )
        self._downloadingNames.discard(software_name)
    
    def __show_download_error(self, software_item, software_name, error_msg):
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
            tr("download.install_failed"),  # 安装失败
            tr("download.install_error").format(name=software_name, error=error_msg),  # {name} 安装失败：{error}
            parent=self,
            duration=5000
        )
        self._downloadingNames.discard(software_name)

    @pyqtSlot(str, str)
    def _show_download_error(self, software_name, error_msg):
        """显示下载错误"""
        item = None
        for it in self.softwareList:
            if it.get('name') == software_name:
                item = it
                break
        self.__show_download_error(item, software_name, error_msg)

    @pyqtSlot(str, int)
    def _show_download_complete(self, software_name, delay_ms=500):
        """延迟显示完成"""
        item = None
        for it in self.softwareList:
            if it.get('name') == software_name:
                item = it
                break
        if delay_ms and delay_ms > 0:
            QTimer.singleShot(delay_ms, lambda: self.__show_download_complete(item, software_name))
        else:
            self.__show_download_complete(item, software_name)

    @pyqtSlot()
    def _clear_selected_software(self):
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
        modeGroupLayout.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        
        self.modeLabel = QLabel(tr("download.select_mode") + ":", modeGroup)  # 选择模式
        self.modeLabel.setObjectName("modeLabel")
        
        self.singleModeButton = RadioButton(tr("download.single_mode"), modeGroup)  # 单个下载
        self.singleModeButton.setChecked(True)
        
        self.multiModeButton = RadioButton(tr("download.multi_mode"), modeGroup)  # 批量下载
        
        modeGroupLayout.addWidget(self.modeLabel)
        modeGroupLayout.addWidget(self.singleModeButton)
        modeGroupLayout.addWidget(self.multiModeButton)

        self.selectAllButton = PushButton(tr("download.select_all"), modeGroup)  # 全选
        self.selectAllButton.setObjectName("selectAllButton")
        self.selectAllButton.setFixedHeight(36)
        self.selectAllButton.hide()
        modeGroupLayout.addWidget(self.selectAllButton)
        
        # 下载源选择
        sourceGroup = QWidget(self.modeContainer)
        sourceGroup.setObjectName("sourceGroup")
        sourceGroupLayout = QHBoxLayout(sourceGroup)
        sourceGroupLayout.setContentsMargins(0, 0, 0, 0)
        sourceGroupLayout.setSpacing(8)
        sourceGroupLayout.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        
        self.sourceLabel = QLabel(tr("download.download_source") + ":", sourceGroup)  # 下载源
        self.sourceLabel.setObjectName("sourceLabel")  
        self.sourceComboBox = ComboBox(sourceGroup)
        self.sourceComboBox.setObjectName("sourceComboBox")
        self.sourceComboBox.setFixedWidth(200)
        source_items = [get_source_name(k) for k in DOWNLOAD_SOURCES]
        self.sourceComboBox.addItems(source_items)
        # 存储key到每个item的userData
        for i, key in enumerate(DOWNLOAD_SOURCES):
            self.sourceComboBox.setItemData(i, key)
        
        source_key = cfg.downloadSource.value
        default_index = list(DOWNLOAD_SOURCES.keys()).index(source_key)
        self.sourceComboBox.setCurrentIndex(default_index)
        set_download_src(source_key)
        
        sourceGroupLayout.addWidget(self.sourceLabel)
        sourceGroupLayout.addWidget(self.sourceComboBox)
        
        self.startButton = PrimaryPushButton(FUI.PLAY, tr("download.start_download"), self.modeContainer)  # 开始下载
        self.startButton.setObjectName("startButton")
        self.startButton.setFixedHeight(36)
        self.startButton.hide()
        
        self.modeLayout.addWidget(modeGroup)
        self.modeLayout.addWidget(sourceGroup)
        self.modeLayout.addStretch()
        self.modeLayout.addWidget(self.startButton)
        self.modeLayout.setAlignment(Qt.AlignmentFlag.AlignVCenter)
        
        # 软件容器
        self.softwareContainer = QWidget(self.scrollWidget)
        self.softwareLayout = QVBoxLayout(self.softwareContainer)
        self.softwareLayout.setContentsMargins(0, 0, 0, 0)
        self.softwareLayout.setSpacing(16)
        
        self.selectedSoftware = []
    
    def __initLayout(self):
        """ 初始化布局 """
        self.mainLayout.addWidget(self.modeContainer)
        self.mainLayout.addWidget(self.softwareContainer, 1)
        self.mainLayout.addWidget(self.paginationBar)
    
    def addSection(self, title):
        """ 存储分区标题（数据收集，不创建 widget） """
        if not title:
            title = tr("download.common_software")  # 常用软件
        self._currentDataSection = title
    
    def addSoftware(self, icon_path, name, description, link=None):
        """ 存储软件数据（数据收集，不创建 widget） """
        sec = self._currentDataSection or tr("download.common_software")
        self._allSoftwareData.append({
            'section': sec,
            'icon_path': icon_path,
            'name': name,
            'description': description,
            'link': link
        })
    
    # ==================== 分页相关 ====================
    
    def __initPagination(self):
        """ 初始化分页栏 """
        self.paginationBar = QWidget(self.scrollWidget)
        self.paginationBar.setObjectName("paginationBar")
        self.paginationLayout = QHBoxLayout(self.paginationBar)
        self.paginationLayout.setContentsMargins(0, 8, 0, 0)
        self.paginationLayout.setAlignment(Qt.AlignmentFlag.AlignCenter)
    
    def _calcPerPage(self):
        """根据可用高度动态计算每页显示数量（不滚动）"""
        h = self.scrollWidget.height()
        if h <= 0:
            return cfg.downloadItemsPerPage.value
        overhead = 50 + 60 + 80  # 标题 ~50 + modeContainer ~60 + 边距 ~80
        available = h - overhead
        row_h = 112  # 卡片 100 + 间距 12
        rows = max(1, int(available / row_h))
        return rows * 2  # 2列
    
    def _finishLoading(self, start_page=1):
        """ 所有数据加载完成后调用，渲染第一页 """
        per_page = self._calcPerPage()
        total = len(self._allSoftwareData)
        self.totalPages = max(1, (total + per_page - 1) // per_page)
        if start_page > self.totalPages:
            start_page = 1
        self.currentPage = start_page
        self._renderPage(start_page)
    
    def _clearSoftwareLayout(self):
        """ 清空 softwareLayout 中的所有 widget """
        self.softwareList.clear()
        while self.softwareLayout.count():
            item = self.softwareLayout.takeAt(0)
            w = item.widget()
            if w:
                w.deleteLater()
    
    def _renderPage(self, page_num):
        """ 渲染指定页码的软件卡片 """
        self._clearSoftwareLayout()
        
        per_page = self._calcPerPage()
        start = (page_num - 1) * per_page
        end = min(start + per_page, len(self._allSoftwareData))
        page_data = self._allSoftwareData[start:end]
        
        is_single = self.singleModeButton.isChecked()
        shown_sections = set()
        current_section = None
        grid_layout = None
        row, col = 0, 0
        
        for item in page_data:
            sec = item['section']
            if sec != current_section:
                if sec not in shown_sections:
                    label = QLabel(sec, self.softwareContainer)
                    label.setObjectName("sectionTitleLabel")
                    self.softwareLayout.addWidget(label)
                    shown_sections.add(sec)
                gw = QWidget()
                gl = QGridLayout(gw)
                gl.setContentsMargins(0, 0, 0, 0)
                gl.setSpacing(12)
                self.softwareLayout.addWidget(gw)
                current_section = sec
                grid_layout = gl
                row, col = 0, 0
            
            card, sw_dict = self._createSoftwareCard(item, is_single)
            grid_layout.addWidget(card, row, col)
            self.softwareList.append(sw_dict)
            
            col += 1
            if col >= 2:
                col = 0
                row += 1
        
        self.softwareLayout.addStretch()
        self.currentPage = page_num
        self._updatePaginationButtons()
    
    def _createSoftwareCard(self, data, is_single_mode):
        """ 创建一个软件卡片 widget，返回 (card_widget, software_dict) """
        name = data['name']
        icon_path = data['icon_path']
        description = data['description']
        link = data.get('link')
        
        card = CardWidget(self.softwareContainer)
        card.setMinimumHeight(100)
        card.setMaximumHeight(100)
        card.setMinimumWidth(400)
        
        layout = QHBoxLayout(card)
        layout.setContentsMargins(20, 16, 20, 16)
        layout.setSpacing(16)
        
        # 图标
        icon_label = QLabel(card)
        icon_label.setFixedSize(64, 64)
        cached = get_cached_icon(icon_path)
        if cached:
            icon_label.setPixmap(cached)
        else:
            icon_label.setText("")
            icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            icon_label.setObjectName("softwareEmptyIconLabel")
        
        # 信息区域
        info_layout = QVBoxLayout()
        info_layout.setSpacing(6)
        info_layout.setContentsMargins(0, 0, 0, 0)
        
        name_layout = QHBoxLayout()
        name_layout.setSpacing(6)
        name_layout.setContentsMargins(0, 0, 0, 0)
        name_label = QLabel(name, card)
        name_label.setObjectName("softwareNameLabel")
        name_label.setWordWrap(False)
        name_layout.addWidget(name_label)
        
        if link:
            link_btn = QToolButton(card)
            link_btn.setIcon(FUI.LINK.icon())
            link_btn.setFixedSize(20, 20)
            link_btn.setObjectName("softwareLinkButton")
            link_btn.setToolTip(tr("download.open_official_website"))
            link_btn.clicked.connect(lambda: QDesktopServices.openUrl(QUrl(link)))
            name_layout.addWidget(link_btn)
        
        name_layout.addStretch(1)
        info_layout.addLayout(name_layout)
        
        desc_label = QLabel(description, card)
        desc_label.setObjectName("softwareDescLabel")
        desc_label.setWordWrap(True)
        desc_label.setFixedHeight(40)
        info_layout.addWidget(desc_label)
        
        # 进度环
        progress_bar = ProgressRing(card)
        progress_bar.setFixedSize(60, 60)
        progress_bar.setValue(0)
        progress_bar.setTextVisible(True)
        progress_bar.hide()
        
        # 下载按钮 / 复选框
        btn = PrimaryPushButton(FUI.DOWNLOAD, tr("download.download_btn"), card)
        btn.setFixedHeight(36)
        
        checkbox = CheckBox(card)
        checkbox.setFixedSize(32, 32)
        checkbox.hide()
        
        layout.addWidget(icon_label)
        layout.addLayout(info_layout, 1)
        layout.addWidget(progress_bar)
        layout.addWidget(btn)
        layout.addWidget(checkbox)
        
        # 如果正在下载中，恢复进度状态
        is_downloading = name in self._downloadingNames
        if is_downloading:
            btn.hide()
            checkbox.hide()
            progress_bar.show()
        
        btn.clicked.connect(lambda checked, n=name: self.__handleDownload(n))
        checkbox.stateChanged.connect(lambda state, n=name: self.__handleCheckboxChange(n, state))
        
        sw_dict = {
            'card': card,
            'name': name,
            'button': btn,
            'checkbox': checkbox,
            'progressBar': progress_bar
        }
        return card, sw_dict
    
    def _updatePaginationButtons(self):
        """ 更新分页按钮状态 """
        # 清除旧按钮
        for btn in self._pageButtons:
            btn.deleteLater()
        self._pageButtons.clear()
        # 清除 layout 中已有的控件
        while self.paginationLayout.count():
            item = self.paginationLayout.takeAt(0)
            w = item.widget()
            if w:
                w.deleteLater()
        
        if self.totalPages <= 1:
            self.paginationBar.hide()
            return
        
        self.paginationBar.show()
        
        for i in range(1, self.totalPages + 1):
            btn = PushButton(str(i), self.paginationBar)
            btn.setFixedSize(36, 36)
            btn.setProperty("paginationActive", i == self.currentPage)
            btn.style().unpolish(btn)
            btn.style().polish(btn)
            btn.clicked.connect(lambda checked, p=i: self._gotoPage(p))
            self.paginationLayout.addWidget(btn)
            self._pageButtons.append(btn)
    
    def _gotoPage(self, page_num):
        """ 跳转到指定页 """
        if page_num < 1 or page_num > self.totalPages:
            return
        self._renderPage(page_num)
    
    def _onItemsPerPageChanged(self, value):
        """ 单页数量变更时重新加载 """
        self._finishLoading()
    
    def resizeEvent(self, e):
        """窗口缩放时重新计算分页"""
        super().resizeEvent(e)
        if self._allSoftwareData and self.totalPages > 0:
            new_per_page = self._calcPerPage()
            total = len(self._allSoftwareData)
            new_total = max(1, (total + new_per_page - 1) // new_per_page)
            if new_total != self.totalPages:
                self._finishLoading(self.currentPage)
    
    def __handleCheckboxChange(self, software_name, state):
        """ 复选框状态变更 """
        if state == Qt.CheckState.Checked:
            if software_name not in self.selectedSoftware:
                self.selectedSoftware.append(software_name)
        else:
            if software_name in self.selectedSoftware:
                self.selectedSoftware.remove(software_name)
        if self.multiModeButton.isChecked():
            self.__updateSelectAllButton()
    
    def __handleSelectAll(self):
        """ 全选按钮点击 """
        all_checked = all(
            software['checkbox'].isChecked() 
            for software in self.softwareList
            if not (software.get('progressBar') is not None and software['progressBar'].isVisible())
        )
        should_check_all = not all_checked
        for software in self.softwareList:
            try:
                in_progress = software.get('progressBar') is not None and software['progressBar'].isVisible()
            except Exception:
                in_progress = False
            if in_progress:
                continue
            software['checkbox'].setChecked(should_check_all)
        if should_check_all:
            self.selectAllButton.setText(tr("download.deselect_all"))  # 取消全选
        else:
            self.selectAllButton.setText(tr("download.select_all"))  # 全选
    
    def __updateSelectAllButton(self):
        if not self.softwareList:
            self.selectAllButton.setText(tr("download.select_all"))  # 全选
            return
        available_software = [
            software for software in self.softwareList
            if not (software.get('progressBar') is not None and software['progressBar'].isVisible())
        ]
        if not available_software:
            self.selectAllButton.setText(tr("download.select_all"))  # 全选
            return
        all_checked = all(software['checkbox'].isChecked() for software in available_software)
        if all_checked:
            self.selectAllButton.setText(tr("download.deselect_all"))  # 取消全选
        else:
            self.selectAllButton.setText(tr("download.select_all"))  # 全选
    
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
        
        if is_single_mode:
            self.startButton.hide()
            self.selectAllButton.hide()
        else:
            self.startButton.show()
            self.selectAllButton.show()
            self.__updateSelectAllButton()
        
        # 重新渲染当前页以更新卡片控件
        self._renderPage(self.currentPage)
    
    def __handleStartDownload(self):
        """ 处理开始下载按钮点击 """
        if not self.selectedSoftware:
            InfoBar.warning(
                tr("download.no_selection"),  # 未选择软件
                tr("download.please_select_first"),  # 请先选择要下载的软件
                parent=self,
                duration=3000
            )
            return
        
        # 显示确认对话框
        software_list = "\n".join(self.selectedSoftware)
        msg_box = MessageBox(
            tr("download.confirm_download"),  # 确认下载
            tr("download.confirm_batch").format(list=software_list),  # 确定要下载以下软件吗？\n{list}
            self
        )
        
        result = msg_box.exec()
        if not result:
            return
        
        # 显示下载中提示
        info_bar = InfoBar.success(
            tr("download.starting_download"),  # 开始下载
            tr("download.downloading_batch").format(count=len(self.selectedSoftware)),  # 正在下载 {count} 个软件...
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
                    self._downloadingNames.add(software_name)
                    break
        
        if hasattr(self, 'download_executor') and self.download_executor is not None:
            try:
                logger.info("关闭可能存在的旧线程池")
                self.download_executor.shutdown(wait=True)
                logger.info("旧线程池已关闭")
            except Exception as e:
                logger.error(f"关闭旧线程池时出错: {str(e)}")
        max_workers = os.cpu_count() if os.cpu_count() else 4
        self.download_executor = ThreadPoolExecutor(max_workers=max_workers)
        logger.info(f"创建线程池，最大并发数: {max_workers}")
        self.futures = []

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
                        Qt.ConnectionType.QueuedConnection
                    )
                    QMetaObject.invokeMethod(
                        item['progressBar'],
                        'setValue',
                        Qt.ConnectionType.QueuedConnection,
                        Q_ARG(int, 0)
                    )
                    QMetaObject.invokeMethod(
                        item['button'],
                        'hide',
                        Qt.ConnectionType.QueuedConnection
                    )
                    try:
                        QMetaObject.invokeMethod(
                            item['checkbox'],
                            'hide',
                            Qt.ConnectionType.QueuedConnection
                        )
                    except Exception:
                        pass
                except Exception:
                    pass

            # 查找下载链接
            cache_file = None
            _match_name = software_name.replace(" ", "").replace("[", "").replace("]", "")
            for entry in url_dir:
                _match_filename = entry.get('filename', '').replace(" ", "").replace("[", "").replace("]", "")
                if _match_filename.startswith(_match_name):
                    cache_file = entry.copy()
                    if 'hash' not in cache_file:
                        cache_file['hash'] = ''
                    break

            if not cache_file:
                QMetaObject.invokeMethod(
                    self,
                    '_show_download_error',
                    Qt.ConnectionType.QueuedConnection,
                    Q_ARG(str, software_name),
                    Q_ARG(str, tr("download.error_no_url"))
                )
                return

            download_url = self.__get_url(cache_file)
            if not download_url:
                QMetaObject.invokeMethod(
                    self,
                    '_show_download_error',
                    Qt.ConnectionType.QueuedConnection,
                    Q_ARG(str, software_name),
                    Q_ARG(str, tr("download.error_cannot_get_url"))
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
                        Qt.ConnectionType.QueuedConnection,
                        Q_ARG(int, val)
                    )

            def _on_download_done(software_name_arg=None, item_ref=item):
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
                        download_complete_callback=_on_download_done
                    )

                    # 保证设置为100并显示完成
                    if item:
                        QMetaObject.invokeMethod(
                            item['progressBar'],
                            'setValue',
                            Qt.ConnectionType.QueuedConnection,
                            Q_ARG(int, 100)
                        )
                    QMetaObject.invokeMethod(
                        self,
                        '_show_download_complete',
                        Qt.ConnectionType.QueuedConnection,
                        Q_ARG(str, software_name),
                        Q_ARG(int, 500)
                    )
                else:
                    QMetaObject.invokeMethod(
                        self,
                        '_show_download_error',
                        Qt.ConnectionType.QueuedConnection,
                        Q_ARG(str, software_name),
                        Q_ARG(str, tr("download.error_no_install_method"))
                    )
            except Exception as e:
                logger.error(f"{software_name}: 安装函数异常 - {e}", exc_info=True)
                QMetaObject.invokeMethod(
                    self,
                    '_show_download_error',
                    Qt.ConnectionType.QueuedConnection,
                    Q_ARG(str, software_name),
                    Q_ARG(str, str(e))
                )

        for software_name in list(self.selectedSoftware):
            future = self.download_executor.submit(_run_install_task, software_name)
            self.futures.append(future)

        def _wait_tasks():
            while True:
                all_done = all(future.done() for future in self.futures)
                if all_done:
                    break
                time.sleep(1)
            try:
                logger.info("所有任务完成，关闭线程池")
                self.download_executor.shutdown(wait=True)
                logger.info("线程池已关闭")
            except Exception as e:
                logger.error(f"关闭线程池时出错: {str(e)}")
            QMetaObject.invokeMethod(
                self,
                '_clear_selected_software',
                Qt.ConnectionType.QueuedConnection
            )

        threading.Thread(target=_wait_tasks, daemon=True).start()



