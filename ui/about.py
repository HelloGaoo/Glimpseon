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

import json
import os
import sys
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:sys.path.insert(0, _BASE_DIR)

import webbrowser
import platform

from PyQt6.QtCore import Qt, QSize
from PyQt6.QtGui import QPixmap
from PyQt6.QtWidgets import (
    QHBoxLayout, QLabel, QVBoxLayout, QWidget,
    QGridLayout, QListWidget, QListWidgetItem,
)
from qfluentwidgets import (
    CardWidget,
    FluentIcon as FIF,
    IconWidget,
    BodyLabel,
    CaptionLabel,
    TitleLabel,
    StrongBodyLabel,
    TextEdit,
    PushButton,
    Theme,
    ListWidget,
    MessageBoxBase,
)

from core.constants import get_resPath, load_qss
from core.utils import tr, TranslatableWidget
from version import BUILD_DATE, VERSION

from .common import BaseScrollAreaInterface, show_text_file


class AboutInterface(BaseScrollAreaInterface, TranslatableWidget):
    """关于界面"""

    def __init__(self, parent=None):
        super().__init__(tr("navigation.about"), parent)
        self.setObjectName("about")

        # 在基类的 scrollWidget 上建立布局
        self.vbox = QVBoxLayout(self.scrollWidget)
        self.vbox.setSpacing(10)
        self.vbox.setContentsMargins(60, 12, 60, 24)
        self.vbox.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)

        # 头部：图标 名称  版本  描述
        self._addHeader()

        # 应用信息
        self._addInfoSection()

        # 相关链接
        self._addLinksSection()

        # 更新日志
        self._addChangelogSection()

        # 底部版权
        self._addFooter()

        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setStyleSheet(load_qss('about.qss'))
        self.setup_translatable_ui()


    def _addHeader(self):
        card = CardWidget(self.scrollWidget)
        card.setObjectName("headerCard")
        lay = QVBoxLayout(card)
        lay.setContentsMargins(32, 20, 32, 16)
        lay.setSpacing(6)
        lay.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)

        icon = QLabel(card)
        icon.setFixedSize(64, 64)
        icon.setObjectName("appIconLabel")
        icon.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        p = get_resPath(os.path.join('resource', 'icons', 'CY.png'))
        if os.path.exists(p):
            icon.setPixmap(QPixmap(p).scaled(
                64, 64, Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation))

        name = TitleLabel("ClassLively", card)
        name.setObjectName("appNameLabel")
        name.setAlignment(Qt.AlignmentFlag.AlignLeft)

        desc = BodyLabel(tr("about.description"), card)
        desc.setObjectName("descriptionLabel")
        desc.setAlignment(Qt.AlignmentFlag.AlignLeft)
        desc.setWordWrap(True)

        verRow = QHBoxLayout()
        verRow.setSpacing(6)
        verIcon = IconWidget(FIF.UPDATE, card)
        verIcon.setFixedSize(14, 14)
        verText = CaptionLabel(f"v{VERSION}  ·  {BUILD_DATE}", card)
        verText.setObjectName("versionLabel")
        verRow.addWidget(verIcon)
        verRow.addWidget(verText)

        lay.addWidget(icon, 0, Qt.AlignmentFlag.AlignLeft)
        lay.addWidget(name, 0, Qt.AlignmentFlag.AlignLeft)
        lay.addSpacing(1)
        lay.addWidget(desc, 0, Qt.AlignmentFlag.AlignLeft)
        lay.addSpacing(4)
        lay.addLayout(verRow)

        self.vbox.addWidget(card)

    def _addInfoSection(self):
        """应用信息：版本 作者"""
        title = StrongBodyLabel(tr("about.app_info"), self.scrollWidget)
        title.setObjectName("sectionTitle")
        self.vbox.addWidget(title)

        card = CardWidget(self.scrollWidget)
        card.setObjectName("contentCard")
        lay = QHBoxLayout(card)
        lay.setContentsMargins(20, 14, 20, 14)
        lay.setSpacing(24)

        # 左侧：版本信息
        left = QVBoxLayout()
        left.setSpacing(4)
        verLabel = BodyLabel(f"v{VERSION}", card)
        verLabel.setObjectName("infoVersionLabel")
        dateLabel = CaptionLabel(
            f"{tr('about.build_date')}: {BUILD_DATE}", card)
        dateLabel.setObjectName("infoDateLabel")
        authorLabel = CaptionLabel(
            f"{tr('about.author')}: HelloGaoo", card)
        authorLabel.setObjectName("infoAuthorLabel")
        left.addWidget(verLabel)
        left.addWidget(dateLabel)
        left.addWidget(authorLabel)

        # 右侧：详情按钮
        right = QVBoxLayout()
        right.setAlignment(Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignRight)
        techBtn = PushButton(FIF.INFO, tr("about.thanks"), card)
        techBtn.setFixedHeight(32)
        techBtn.clicked.connect(self._showTechDialog)
        right.addWidget(techBtn)

        lay.addLayout(left, 1)
        lay.addLayout(right)

        self.vbox.addWidget(card)

    def _addLinksSection(self):
        title = StrongBodyLabel(tr("about.related_links"), self.scrollWidget)
        title.setObjectName("sectionTitle")
        self.vbox.addWidget(title)

        card = CardWidget(self.scrollWidget)
        card.setObjectName("contentCard")
        lay = QVBoxLayout(card)
        lay.setContentsMargins(16, 10, 16, 10)
        lay.setSpacing(4)

        links = [
            (FIF.GITHUB, tr("about.github_repo"),
             "github.com/HelloGaoo/ClassLively",
             "https://github.com/HelloGaoo/ClassLively"),
            (FIF.PEOPLE, tr("about.author_homepage"),
             "space.bilibili.com/1498602348",
             "https://space.bilibili.com/1498602348"),
            (FIF.DOCUMENT, tr("about.license"),
             "GNU General Public License v3.0",
             None),  # 点击打开许可证弹窗
        ]

        for icon, title_text, desc, url in links:
            row = QHBoxLayout()
            row.setContentsMargins(0, 4, 0, 4)
            row.setSpacing(12)

            iw = IconWidget(icon, card)
            iw.setFixedSize(22, 22)

            tcol = QVBoxLayout()
            tcol.setSpacing(1)
            tcol.setContentsMargins(0, 0, 0, 0)
            tl = BodyLabel(title_text, card)
            tl.setObjectName("linkTitleLabel")
            dl = CaptionLabel(desc, card)
            dl.setObjectName("linkDescLabel")
            tcol.addWidget(tl)
            tcol.addWidget(dl)

            btn = PushButton(FIF.LINK, tr("about.visit"), card)
            btn.setFixedHeight(30)
            if url:
                btn.clicked.connect(lambda _, u=url: webbrowser.open(u))
            else:
                btn.clicked.connect(self._viewLicense)

            row.addWidget(iw)
            row.addLayout(tcol, 1)
            row.addWidget(btn)
            lay.addLayout(row)

        self.vbox.addWidget(card)

    def _addChangelogSection(self):
        title = StrongBodyLabel(tr("about.changelog"), self.scrollWidget)
        title.setObjectName("sectionTitle")
        self.vbox.addWidget(title)

        card = CardWidget(self.scrollWidget)
        card.setObjectName("contentCard")
        lay = QVBoxLayout(card)
        lay.setContentsMargins(20, 12, 20, 12)
        lay.setSpacing(8)

        verTitle = BodyLabel(f"v{VERSION}", card)
        verTitle.setObjectName("changelogVersionLabel")

        path = get_resPath("changelog.md")
        if os.path.exists(path):
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    content = f.read()[:2000]
            except Exception:
                content = tr("about.changelog_unavailable")
        else:
            content = tr("about.changelog_unavailable")

        te = TextEdit(card)
        te.setPlainText(content)
        te.setReadOnly(True)
        te.setFixedHeight(110)
        te.setObjectName("changelogTextEdit")

        lay.addWidget(verTitle)
        lay.addWidget(te)
        self.vbox.addWidget(card)

    def _addFooter(self):
        footer = CaptionLabel(
            "© 2025 AzeLightStudios. All rights reserved.",
            self.scrollWidget)
        footer.setObjectName("copyrightLabel")
        footer.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.vbox.addSpacing(8)
        self.vbox.addWidget(footer)


    #  槽函数

    def _onThemeChanged(self, theme: Theme):
        self.setStyleSheet(load_qss('about.qss'))

    def _viewLicense(self):
        license_path = get_resPath("LICENSE")
        intro = tr("about.license_intro")
        show_text_file(tr("about.license_title"), intro,
                       license_path, parent=self.window())

    def _showTechDialog(self):
        """弹出鸣谢窗口"""
        w = _TechDialog(self.window())
        w.exec()


class _TechDialog(MessageBoxBase):
    """鸣谢弹窗"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.titleLabel = TitleLabel(tr("about.thanks"), self)
        self.titleLabel.setAlignment(
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self.listWidget = ListWidget(self)
        self.listWidget.setFixedHeight(400)

        # 依赖项（可选 含链接按钮）
        deps = _get_dependencies()
        for name, ver, license_name, url in deps:
            display = f"{name}  {ver}   ({license_name})"
            item = QListWidgetItem("")
            item.setData(Qt.ItemDataRole.UserRole, url)
            item.setSizeHint(QSize(0, 34))
            item.setFlags(item.flags() & ~Qt.ItemFlag.ItemIsEditable)
            self.listWidget.addItem(item)

            # 行内 widget：文字 + url（可隐藏）
            w = QWidget()
            lay = QHBoxLayout(w)
            lay.setContentsMargins(12, 0, 8, 0)
            lay.setSpacing(8)

            lbl = BodyLabel(display, w)
            lbl.setObjectName("depItemLabel")

            btn = PushButton(FIF.LINK, tr("common.link"), w)
            btn.setObjectName("depGithubBtn")
            btn.hide()
            btn.clicked.connect(lambda checked, u=url: webbrowser.open(u))

            lay.addWidget(lbl, 1)
            lay.addWidget(btn)
            self.listWidget.setItemWidget(item, w)

        self.listWidget.currentRowChanged.connect(
            self._onSelectionChanged)

        # 布局
        self.viewLayout.setSpacing(8)
        self.viewLayout.addWidget(self.titleLabel)
        self.viewLayout.addWidget(self.listWidget)
        self.widget.setMinimumWidth(500)

        self.cancelButton.hide()
        self.yesButton.setText(tr("common.confirm"))

    def _onSelectionChanged(self, row):
        """选中项时显示url按钮"""
        # 隐藏所有按钮
        for i in range(self.listWidget.count()):
            item = self.listWidget.item(i)
            w = self.listWidget.itemWidget(item) if item else None
            if w:
                btn = w.findChild(PushButton, "depGithubBtn")
                if btn:
                    btn.hide()

        # 显示当前项的url按钮
        item = self.listWidget.item(row)
        if not item or not item.data(Qt.ItemDataRole.UserRole):
            return
        w = self.listWidget.itemWidget(item)
        if w:
            btn = w.findChild(PushButton, "depGithubBtn")
            if btn:
                btn.show()


def _get_dependencies():
    """ 读取依赖列表（credits.json）  获取版本（importlib.metadata）"""
    from importlib.metadata import version, PackageNotFoundError

    path = get_resPath(os.path.join('resource', 'credits.json'))
    if not os.path.exists(path):
        return []
    try:
        with open(path, 'r', encoding='utf-8') as f:
            deps = json.load(f)
    except Exception:
        return []

    result = []
    for entry in deps:
        if entry.get('type') == 'reference':
            display = entry.get('display_name', '')
            license_name = entry.get('license', '')
            url = entry.get('url', '')
            result.append((display, '', license_name, url))
            continue

        import_name = entry.get('import_name', '')
        meta_name = entry.get('metadata_name') or import_name
        display = entry.get('display_name', import_name)
        license_name = entry.get('license', '')
        url = entry.get('url', '')
        try:
            ver = version(meta_name)
        except PackageNotFoundError:
            ver = ''
        result.append((display, ver, license_name, url))
    return result
