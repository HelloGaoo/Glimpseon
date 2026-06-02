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
关于界面模块
"""

import os
import sys
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:sys.path.insert(0, _BASE_DIR)

import webbrowser

from PyQt6.QtCore import Qt
from PyQt6.QtGui import QPixmap
from PyQt6.QtWidgets import QHBoxLayout, QLabel, QVBoxLayout, QWidget
from qfluentwidgets import (
    CardWidget,
    FluentIcon as FIF,
    MessageBox,
    PushButton,
    Theme,
)

from core.constants import get_resPath, load_qss
from core.utils import tr, TranslatableWidget
from core.logger import logger
from version import BUILD_DATE, VERSION
from .common import show_text_file

from .common import BaseScrollAreaInterface, show_text_file


class AboutInterface(BaseScrollAreaInterface, TranslatableWidget):
    """ 关于界面 """

    def __init__(self, parent=None):
        super().__init__(tr("navigation.about"), parent)  # 关于
        self.setObjectName("about")
        self.scrollWidget = QWidget()
        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.aboutCard = CardWidget(self.scrollWidget)
        self.aboutLayout = QVBoxLayout(self.aboutCard)
        self.aboutLayout.setContentsMargins(16, 16, 16, 16)
        self.aboutLayout.setSpacing(12)
        mainContentLayout = QHBoxLayout()
        mainContentLayout.setSpacing(24)
        leftLayout = QVBoxLayout()
        leftLayout.setSpacing(12)
        appInfoLayout = QHBoxLayout()
        appInfoLayout.setSpacing(16)
        self.appIconLabel = QLabel(self.aboutCard)
        self.appIconLabel.setFixedSize(64, 64)
        self.appIconLabel.setObjectName("appIconLabel")
        try:
            icon_path = get_resPath(os.path.join('resource', 'icons', 'CY.png'))
            if os.path.exists(icon_path):
                pixmap = QPixmap(icon_path)
                self.appIconLabel.setPixmap(pixmap.scaled(64, 64, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation))
            else:
                self.appIconLabel.setText("📱")
                self.appIconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        except Exception:
            self.appIconLabel.setText("📱")
            self.appIconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        
        self.appNameLabel = QLabel("ClassLively", self.aboutCard)
        self.appNameLabel.setObjectName("appNameLabel")
        appInfoLayout.addWidget(self.appIconLabel)
        appInfoLayout.addWidget(self.appNameLabel, 1)
        self.descriptionLabel = QLabel(tr("about.description"), self.aboutCard)  # 班级教学辅助工具
        self.descriptionLabel.setObjectName("descriptionLabel")
        self.descriptionLabel.setWordWrap(True)
        leftLayout.addLayout(appInfoLayout)
        leftLayout.addWidget(self.descriptionLabel)
        rightLayout = QVBoxLayout()
        rightLayout.setSpacing(8)
        rightLayout.setContentsMargins(0, 0, 0, 0)
        self.versionInfo = QLabel(f"{tr('about.current_version')}: {VERSION}\n{tr('about.build_date')}: {BUILD_DATE}", self.aboutCard)
        self.versionInfo.setObjectName("versionInfo")
        self.versionInfo.setWordWrap(True)
        rightLayout.addWidget(self.versionInfo)
        self.authorLabel = QLabel(f"{tr('about.author')}: HelloGaoo", self.aboutCard)
        self.authorLabel.setObjectName("authorLabel")
        rightLayout.addWidget(self.authorLabel)
        self.copyrightLabel = QLabel("© 2026 ClassLively. All rights reserved.", self.aboutCard)
        self.copyrightLabel.setObjectName("copyrightLabel")
        rightLayout.addWidget(self.copyrightLabel)
        mainContentLayout.addLayout(leftLayout, 1)
        mainContentLayout.addLayout(rightLayout)
        self.aboutLayout.addLayout(mainContentLayout)
        self.githubCard = CardWidget(self.scrollWidget)
        self.githubCard.setFixedHeight(64)
        self.githubLayout = QHBoxLayout(self.githubCard)
        self.githubLayout.setContentsMargins(16, 16, 16, 16)
        self.githubIcon = QLabel(self.githubCard)
        self.githubIcon.setFixedSize(24, 24)
        self.githubIcon.setObjectName("githubIcon")
        self.githubLabel = QLabel(tr("about.github_repo"), self.githubCard)  # GitHub 仓库
        self.githubLabel.setObjectName("linkLabel")
        self.githubButton = PushButton(FIF.GITHUB, tr("about.view"), self.githubCard)  # 查看
        self.githubButton.setObjectName("linkButton")
        self.githubButton.setFixedHeight(36)
        self.githubLayout.addWidget(self.githubIcon)
        self.githubLayout.addWidget(self.githubLabel, 1)
        self.githubLayout.addWidget(self.githubButton)
        self.authorCard = CardWidget(self.scrollWidget)
        self.authorCard.setFixedHeight(64)
        self.authorLayout = QHBoxLayout(self.authorCard)
        self.authorLayout.setContentsMargins(16, 16, 16, 16)
        self.authorIcon = QLabel(self.authorCard)
        self.authorIcon.setFixedSize(24, 24)
        self.authorIcon.setObjectName("authorIcon")
        self.authorLabel = QLabel(tr("about.author_homepage"), self.authorCard)  # 作者主页
        self.authorLabel.setObjectName("linkLabel")
        self.authorButton = PushButton(FIF.PEOPLE, tr("about.view"), self.authorCard)  # 查看
        self.authorButton.setObjectName("linkButton")
        self.authorButton.setFixedHeight(36)
        self.authorLayout.addWidget(self.authorIcon)
        self.authorLayout.addWidget(self.authorLabel, 1)
        self.authorLayout.addWidget(self.authorButton)
        self.licenseCard = CardWidget(self.scrollWidget)
        self.licenseCard.setFixedHeight(64)
        self.licenseLayout = QHBoxLayout(self.licenseCard)
        self.licenseLayout.setContentsMargins(16, 16, 16, 16)  
        self.licenseIcon = QLabel(self.licenseCard)
        self.licenseIcon.setFixedSize(24, 24)
        self.licenseIcon.setObjectName("licenseIcon") 
        self.licenseLabel = QLabel(tr("about.license"), self.licenseCard)  # GNU General Public License Version 3 开源许可证
        self.licenseLabel.setObjectName("linkLabel")
        self.viewLicenseButton = PushButton(FIF.DOCUMENT, tr("about.view"), self.licenseCard)  # 查看
        self.viewLicenseButton.setObjectName("linkButton")
        self.viewLicenseButton.setFixedHeight(36)
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
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)

        self.__setQss()
        self.__initLayout()
        self.setup_translatable_ui()


    def __initLayout(self):
        """ 初始化布局 """
        self.mainLayout.setSpacing(12)
        self.mainLayout.setContentsMargins(60, 0, 60, 40)
        self.mainLayout.addWidget(self.aboutCard)
        linkGroupLabel = QLabel(tr("about.related_links"), self.scrollWidget)  # 相关链接
        linkGroupLabel.setObjectName("groupLabel")
        self.mainLayout.addWidget(linkGroupLabel)
        self.mainLayout.addWidget(self.githubCard)
        self.mainLayout.addWidget(self.authorCard)
        self.mainLayout.addWidget(self.licenseCard)
        
        # 底部间距
        self.mainLayout.addSpacing(20)
    
    def __setQss(self):
        """ 设置样式表 """
        self.scrollWidget.setObjectName('scrollWidget')
        self.setStyleSheet(load_qss('about.qss'))
    
    def __connectSignalToSlot(self):
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
        license_path = get_resPath("LICENSE")
        intro = "此项目 (ClassLively) 基于 GNU General Public License Version 3 许可证发布："
        show_text_file("开源许可协议", intro, license_path, parent=self.window())
