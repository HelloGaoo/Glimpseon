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
关于界面模块
"""

import sys
import os
import webbrowser

from PyQt5.QtCore import Qt
from PyQt5.QtWidgets import QLabel, QVBoxLayout, QHBoxLayout, QWidget, QMessageBox
from PyQt5.QtGui import QPixmap, QFont
from PyQt5.QtCore import QUrl
from PyQt5.QtGui import QDesktopServices
from qfluentwidgets import CardWidget, FluentIcon as FIF, ScrollArea, SmoothScrollArea, ExpandLayout, PushButton, TextEdit, MessageBox, isDarkTheme, Theme

from .base_scroll_area import BaseScrollAreaInterface
from version import VERSION, BUILD_DATE

# 路径设置
if getattr(sys, 'frozen', False):
    # 打包为 exe 时
    BASE_DIR = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    MEIPASS_DIR = sys._MEIPASS
else:
    # 脚本运行时
    BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    MEIPASS_DIR = None

def get_resource_path(relative_path):
    """获取绝对路径"""
    # 先检查 BASE_DIR 中的资源文件
    base_path = os.path.join(BASE_DIR, relative_path)
    if os.path.exists(base_path):
        return base_path
    # 如果 BASE_DIR 中不存在，检查 MEIPASS_DIR
    if MEIPASS_DIR:
        meipass_path = os.path.join(MEIPASS_DIR, relative_path)
        if os.path.exists(meipass_path):
            return meipass_path
    # 如果都不存在，返回 BASE_DIR 中的路径
    return base_path


class AboutInterface(BaseScrollAreaInterface):
    """ 关于界面 """
    
    def __init__(self, parent=None):
        super().__init__("关于", parent)
        self.setObjectName("about")
        
        self.scrollWidget = QWidget()
        self.mainLayout = QVBoxLayout(self.scrollWidget)
        
        # 关于卡片
        self.aboutCard = CardWidget(self.scrollWidget)
        self.aboutLayout = QVBoxLayout(self.aboutCard)
        self.aboutLayout.setContentsMargins(16, 16, 16, 16)
        self.aboutLayout.setSpacing(12)
        
        # 主内容水平布局
        mainContentLayout = QHBoxLayout()
        mainContentLayout.setSpacing(24)
        
        # 左侧：应用图标和名称
        leftLayout = QVBoxLayout()
        leftLayout.setSpacing(12)
        
        # 应用图标和名称水平布局
        appInfoLayout = QHBoxLayout()
        appInfoLayout.setSpacing(16)
        
        # 软件图标
        self.appIconLabel = QLabel(self.aboutCard)
        self.appIconLabel.setFixedSize(64, 64)
        self.appIconLabel.setObjectName("appIconLabel")
        
        # 尝试加载应用图标
        try:
            icon_path = get_resource_path(os.path.join('resource', 'icons', 'CY.png'))
            if os.path.exists(icon_path):
                pixmap = QPixmap(icon_path)
                self.appIconLabel.setPixmap(pixmap.scaled(64, 64, Qt.KeepAspectRatio, Qt.SmoothTransformation))
            else:
                self.appIconLabel.setText("📱")
                self.appIconLabel.setAlignment(Qt.AlignCenter)
        except Exception:
            self.appIconLabel.setText("📱")
            self.appIconLabel.setAlignment(Qt.AlignCenter)
        
        self.appNameLabel = QLabel("ClassLively", self.aboutCard)
        self.appNameLabel.setObjectName("appNameLabel")
        
        appInfoLayout.addWidget(self.appIconLabel)
        appInfoLayout.addWidget(self.appNameLabel, 1)
        
        self.descriptionLabel = QLabel("114514", self.aboutCard)
        self.descriptionLabel.setObjectName("descriptionLabel")
        self.descriptionLabel.setWordWrap(True)
        
        leftLayout.addLayout(appInfoLayout)
        leftLayout.addWidget(self.descriptionLabel)
        
        # 右侧：版本和开发者信息
        rightLayout = QVBoxLayout()
        rightLayout.setSpacing(8)
        rightLayout.setContentsMargins(0, 0, 0, 0)
        
        # 版本信息
        self.versionInfo = QLabel(f"当前版本：{VERSION}\n构建日期：{BUILD_DATE}", self.aboutCard)
        self.versionInfo.setObjectName("versionInfo")
        self.versionInfo.setWordWrap(True)
        rightLayout.addWidget(self.versionInfo)
        
        # 开发者信息
        self.developerLabel = QLabel("开发作者：HelloGaoo & WHYOS", self.aboutCard)
        self.developerLabel.setObjectName("developerLabel")
        rightLayout.addWidget(self.developerLabel)
        
        self.copyrightLabel = QLabel("© 2026 ClassLively. All rights reserved.", self.aboutCard)
        self.copyrightLabel.setObjectName("copyrightLabel")
        rightLayout.addWidget(self.copyrightLabel)
        
        # 添加到主水平布局
        mainContentLayout.addLayout(leftLayout, 1)
        mainContentLayout.addLayout(rightLayout)
        
        self.aboutLayout.addLayout(mainContentLayout)
        
        # 相关链接卡片
        self.githubCard = CardWidget(self.scrollWidget)
        self.githubLayout = QHBoxLayout(self.githubCard)
        self.githubLayout.setContentsMargins(16, 16, 16, 16)
        
        self.githubIcon = QLabel(self.githubCard)
        self.githubIcon.setFixedSize(24, 24)
        self.githubIcon.setObjectName("githubIcon")
        
        self.githubLabel = QLabel("GitHub 仓库", self.githubCard)
        self.githubLabel.setObjectName("linkLabel")
        
        self.githubButton = PushButton(FIF.GITHUB, "查看", self.githubCard)
        self.githubButton.setObjectName("linkButton")
        
        self.githubLayout.addWidget(self.githubIcon)
        self.githubLayout.addWidget(self.githubLabel, 1)
        self.githubLayout.addWidget(self.githubButton)
        
        # 作者主页卡片
        self.authorCard = CardWidget(self.scrollWidget)
        self.authorLayout = QHBoxLayout(self.authorCard)
        self.authorLayout.setContentsMargins(16, 16, 16, 16)
        
        self.authorIcon = QLabel(self.authorCard)
        self.authorIcon.setFixedSize(24, 24)
        self.authorIcon.setObjectName("authorIcon")
        
        self.authorLabel = QLabel("作者主页", self.authorCard)
        self.authorLabel.setObjectName("linkLabel")
        
        self.authorButton = PushButton(FIF.PEOPLE, "查看", self.authorCard)
        self.authorButton.setObjectName("linkButton")
        
        self.authorLayout.addWidget(self.authorIcon)
        self.authorLayout.addWidget(self.authorLabel, 1)
        self.authorLayout.addWidget(self.authorButton)
        
        # 开源许可证卡片
        self.licenseCard = CardWidget(self.scrollWidget)
        self.licenseLayout = QHBoxLayout(self.licenseCard)
        self.licenseLayout.setContentsMargins(16, 16, 16, 16)
        
        self.licenseIcon = QLabel(self.licenseCard)
        self.licenseIcon.setFixedSize(24, 24)
        self.licenseIcon.setObjectName("licenseIcon")
        
        self.licenseLabel = QLabel("GNU General Public License Version 3 开源许可证", self.licenseCard)
        self.licenseLabel.setObjectName("linkLabel")
        
        self.viewLicenseButton = PushButton(FIF.DOCUMENT, "查看", self.licenseCard)
        self.viewLicenseButton.setObjectName("linkButton")
        
        self.licenseLayout.addWidget(self.licenseIcon)
        self.licenseLayout.addWidget(self.licenseLabel, 1)
        self.licenseLayout.addWidget(self.viewLicenseButton)
        
        self.__initWidget()
        self.__connectSignalToSlot()
    
    def _onThemeChanged(self, theme: Theme):
        """ 主题变更槽函数 """
        self.__setQss()
    
    def __initWidget(self):
        """ 初始化界面 """
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        
        self.__setQss()
        self.__initLayout()
    
    def __initLayout(self):
        """ 初始化布局 """
        self.mainLayout.setSpacing(12)
        self.mainLayout.setContentsMargins(60, 0, 60, 40)
        
        # 添加关于卡片
        self.mainLayout.addWidget(self.aboutCard)
        
        # 添加链接卡片组标题
        linkGroupLabel = QLabel("相关链接", self.scrollWidget)
        linkGroupLabel.setObjectName("groupLabel")
        self.mainLayout.addWidget(linkGroupLabel)
        
        # 添加链接卡片
        self.mainLayout.addWidget(self.githubCard)
        self.mainLayout.addWidget(self.authorCard)
        self.mainLayout.addWidget(self.licenseCard)
        
        # 添加底部间距
        self.mainLayout.addSpacing(20)
    
    def __setQss(self):
        """ 设置样式表 """
        self.scrollWidget.setObjectName('scrollWidget')
        
        theme = 'dark' if isDarkTheme() else 'light'
        try:
            qss_path = get_resource_path(os.path.join('resource', 'qss', theme, 'about_interface.qss'))
            with open(qss_path, encoding='utf-8') as f:
                self.setStyleSheet(f.read())
        except Exception:
            pass
    
    def __connectSignalToSlot(self):
        """ 连接信号与槽 """
        self.githubButton.clicked.connect(self.__openGithub)
        self.authorButton.clicked.connect(self.__openAuthorPage)
        self.viewLicenseButton.clicked.connect(self.__viewLicense)
    
    def __openGithub(self):
        """ 打开 GitHub 仓库 """
        webbrowser.open("https://github.com/HelloGaoo/ClassLively")
    
    def __openAuthorPage(self):
        """ 打开作者主页 """
        webbrowser.open("https://space.bilibili.com/1498602348")
    
    def __viewLicense(self):
        """ 查看许可证文件 """
        theme = 'dark' if isDarkTheme() else 'light'
        
        license_path = get_resource_path("LICENSE")
        license_text = ""
        
        if os.path.exists(license_path):
            with open(license_path, 'r', encoding='utf-8') as f:
                license_text = f.read()
        else:
            license_text = "GNU GENERAL PUBLIC LICENSE\nVersion 3, 29 June 2007\n\nCopyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>\n\n此项目基于 GPL-3.0 许可证授权发布。"
        
        # 创建消息框
        msg_box = MessageBox(
            title="软件许可协议",
            content="此项目 (ClassLively) 基于 GNU General Public License Version 3 许可证发布：",
            parent=self
        )
        msg_box.cancelButton.hide()
        
        text_edit = TextEdit()
        text_edit.setPlainText(license_text)
        text_edit.setReadOnly(True)
        text_edit.setMinimumHeight(400)
        text_edit.setMinimumWidth(500)
        text_edit.setFont(QFont('Consolas', 12))
        
        msg_box.textLayout.addWidget(text_edit)
        msg_box.textLayout.insertSpacing(0, 10)
        
        msg_box.setMinimumWidth(600)
        
        msg_box.exec_()
