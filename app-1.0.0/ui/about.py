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

"""
关于界面
"""

import html
import json
import logging
import markdown as md_lib
import os
import re
import shutil
import string
import subprocess
import threading
import webbrowser

from PyQt6.QtCore import Qt, QSize, QTimer, pyqtSignal, pyqtSlot
from PyQt6.QtGui import QColor, QPixmap
from PyQt6.QtWebEngineCore import QWebEnginePage, QWebEngineSettings
from PyQt6.QtWidgets import (
    QApplication, QHBoxLayout, QLabel, QVBoxLayout, QWidget,
    QListWidgetItem,
)
from qfluentwidgets import (
    CardWidget,
    IconWidget,
    BodyLabel,
    CaptionLabel,
    TitleLabel,
    SubtitleLabel,
    PushButton,
    PrimaryPushButton,
    SwitchSettingCard,
    Theme,
    ListWidget,
    MessageBoxBase,
    ScrollArea,
    isDarkTheme,
)

from core.config import cfg
from core.constants import PACKAGE_ROOT, APP_DIR, APP_ICON, get_resPath, load_qss, VERSION, BUILD_DATE, FONT_FAMILY
from core.utils import tr, TranslatableWidget, FUI
from core.updater import check_github_version_legacy, get_github_changelog, download_update, extract_update, deploy_update, cleanup_update_files, create_update_script

from .common import show_text_file, create_html_view, HTML_BASE_URL

logger = logging.getLogger("Glimpseon.ui.about")

# 更新日志正文是否含 html 标签（GitHub body_html 必命中，纯文本/失败文案不命中）
_HTML_TAG_RE = re.compile(r'<[a-zA-Z][^>]*>')

# GitHub 把外链图片包成 camo 代理地址(部分网络环境不可达), data-canonical-src 存原始地址
_IMG_TAG_RE = re.compile(r'<img\b[^>]*>', re.I)
_CANONICAL_SRC_RE = re.compile(r'\sdata-canonical-src="([^"]+)"')

# GitHub alerts: "> [!IMPORTANT]" 等提示块, markdown 库渲染成普通 blockquote 后再改写
_ALERT_RE = re.compile(
    r'<blockquote>\s*<p>\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*(?:<br\s*/?>)?(.*?)</p>\s*</blockquote>',
    re.S | re.I)
_ALERT_KINDS = {'note': '详情', 'tip': '提示', 'important': '重要',
                'warning': '警告', 'caution': '注意'}

# 任务列表: "- [x] 待办" 改写成带状态样式的列表项
_TASK_RE = re.compile(r'<li>\s*\[([ xX])\]\s*')


def _decamo_images(fragment):
    """把 camo 图片地址换回原始地址"""
    def _fix(m):
        tag = m.group(0)
        cm = _CANONICAL_SRC_RE.search(tag)
        if not cm:
            return tag
        orig = cm.group(1)
        tag = re.sub(r'\ssrc="https://camo\.githubusercontent\.com/[^"]*"',
                     lambda _: f' src="{orig}"', tag, count=1)
        return tag.replace(cm.group(0), '')
    return _IMG_TAG_RE.sub(_fix, fragment)


def _md_to_html(md_text):
    """markdown 转 html(GitHub 风格扩展)"""
    out = md_lib.markdown(md_text, extensions=['tables', 'fenced_code', 'sane_lists', 'nl2br'])

    def _alert(m):
        kind = m.group(1).lower()
        title = _ALERT_KINDS.get(kind, kind.upper())
        body = m.group(2).strip()
        return (f'<div class="md-alert md-alert-{kind}">'
                f'<p class="md-alert-title">{title}</p>'
                f'<p>{body}</p></div>')

    out = _ALERT_RE.sub(_alert, out)
    out = _TASK_RE.sub(
        lambda m: f'<li class="task-list task-list-{"done" if m.group(1).strip() else "todo"}">',
        out)
    return out

# 深浅主题配色: text 正文 / heading 标题 / muted 次要 / code_bg 代码底 / border 边线 / link 链接 / alert_bg 提示块底
_CHANGELOG_PALETTES = {
    True: dict(text="#CCCCCC", heading="#FFFFFF", muted="#999999",
               code_bg="#2D2D2D", border="#3F3F3F", link="#4CA6FF",
               scrollbar="#4A4A4A", scrollbar_hover="#5C5C5C",
               alert_bg="rgba(255, 255, 255, 0.05)"),
    False: dict(text="#444444", heading="#000000", muted="#777777",
                code_bg="#F6F8FA", border="#DDDDDD", link="#0078D4",
                scrollbar="#C8C8C8", scrollbar_hover="#B0B0B0",
                alert_bg="rgba(0, 0, 0, 0.04)"),
}

_CHANGELOG_HTML = string.Template("""<!doctype html>
<html><head><meta charset="utf-8"><style>
html, body { margin: 0; padding: 0; background: transparent; }
body {
    font-family: $font;
    font-size: 13px; line-height: 1.65;
    color: $text; padding: 4px 10px 18px 10px;
}
h1, h2, h3, h4 { line-height: 1.35; margin: 20px 0 10px; color: $heading; }
body > :first-child { margin-top: 2px; }
h1 { font-size: 19px; } h2 { font-size: 17px; } h3 { font-size: 15px; }
p { margin: 8px 0; }
a { color: $link; text-decoration: none; }
a:hover { text-decoration: underline; }
img {
    max-width: 100%; height: auto; border-radius: 8px;
    border: 1px solid $border;
}
code {
    font-family: Consolas, monospace; font-size: 12px;
    background: $code_bg; padding: 2px 6px; border-radius: 4px;
}
pre {
    background: $code_bg; padding: 12px 14px; border-radius: 8px;
    overflow-x: auto; margin: 12px 0;
}
pre code { background: transparent; padding: 0; }
blockquote {
    margin: 10px 0; padding: 4px 14px;
    border-left: 3px solid $border; color: $muted;
}
.md-alert {
    margin: 10px 0; padding: 10px 14px;
    border-left: 3px solid $border; border-radius: 6px;
    background: $alert_bg;
}
.md-alert p { margin: 4px 0; }
.md-alert-title {
    font-weight: 600; color: inherit;
    display: flex; align-items: center; gap: 6px;
}
.md-alert-note { border-left-color: #3B82F6; } .md-alert-note .md-alert-title { color: #3B82F6; }
.md-alert-tip { border-left-color: #22C55E; } .md-alert-tip .md-alert-title { color: #22C55E; }
.md-alert-important { border-left-color: #A855F7; } .md-alert-important .md-alert-title { color: #A855F7; }
.md-alert-warning { border-left-color: #F97316; } .md-alert-warning .md-alert-title { color: #F97316; }
.md-alert-caution { border-left-color: #EF4444; } .md-alert-caution .md-alert-title { color: #EF4444; }
li.task-list { list-style: none; margin-left: -18px; }
li.task-list-done::before { content: "\\2611\\00A0"; }
li.task-list-todo::before { content: "\\2610\\00A0"; }
table { border-collapse: collapse; margin: 10px 0; }
th, td { border: 1px solid $border; padding: 6px 12px; }
th { background: $code_bg; }
hr { border: none; border-top: 1px solid $border; margin: 18px 0; }
ul, ol { margin: 8px 0; padding-left: 24px; }
li { margin: 3px 0; }
::-webkit-scrollbar { width: 8px; height: 8px; }
::-webkit-scrollbar-thumb { background: $scrollbar; border-radius: 4px; }
::-webkit-scrollbar-thumb:hover { background: $scrollbar_hover; }
::-webkit-scrollbar-track { background: transparent; }
</style></head><body>$body</body></html>""")


def _build_changelog_html(body_fragment):
    """组装更新日志网页"""
    palette = _CHANGELOG_PALETTES[isDarkTheme()]
    return _CHANGELOG_HTML.substitute(font=FONT_FAMILY, body=body_fragment, **palette)


class _ChangelogPage(QWebEnginePage):
    """更新日志网页"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setBackgroundColor(QColor(0, 0, 0, 0))

    def acceptNavigationRequest(self, url, navigation_type, is_main_frame):
        # 链接点击转交系统浏览器, 避免视图被导航走
        if is_main_frame and navigation_type == QWebEnginePage.NavigationType.NavigationTypeLinkClicked:
            logger.info(f"[关于] 打开更新日志链接: {url.toString()}")
            webbrowser.open(url.toString())
            return False
        return super().acceptNavigationRequest(url, navigation_type, is_main_frame)


class AboutInterface(ScrollArea, TranslatableWidget):
    """关于界面"""

    _check_result_signal = pyqtSignal(object)
    _changelog_loaded_signal = pyqtSignal(str)

    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("about")
        self.setWindowTitle(tr("navigation.about"))

        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)

        self.setViewportMargins(60, 120, 60, 20)

        self._container = QWidget()
        self._container.setObjectName("scrollWidget")
        self.setWidget(self._container)
        self.viewport().setAutoFillBackground(False)
        self._container.setAutoFillBackground(False)

        self._topLayout = QVBoxLayout(self._container)
        self._topLayout.setContentsMargins(0, 0, 0, 0)
        self._topLayout.setSpacing(0)

        self._splitLayout = QHBoxLayout()
        self._splitLayout.setContentsMargins(0, 0, 0, 0)
        self._splitLayout.setSpacing(20)

        self._initLeftPanel(self._container)
        self._initRightPanel(self._container)

        self._splitLayout.addWidget(self._leftPanel, 1)
        self._splitLayout.addWidget(self._rightPanel, 1)

        self._topLayout.addLayout(self._splitLayout)

        self._footerLabel = CaptionLabel("© 2025 HelloGaoo. All rights reserved.", self._container)
        self._footerLabel.setObjectName("copyrightLabel")
        self._footerLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._topLayout.addSpacing(16)
        self._topLayout.addWidget(self._footerLabel)

        self.titleLabel = SubtitleLabel(tr("navigation.about"), self)
        self.titleLabel.setObjectName('settingLabel')
        self.titleLabel.move(60, 63)

        self._progress_signal.connect(
            lambda p: self.updateStatusLabel.setText(tr("update.downloading_progress").format(percent=p)))
        self._check_result_signal.connect(self._on_check_result)
        self._changelog_loaded_signal.connect(self._on_changelog_loaded)
        self.setStyleSheet(load_qss('about.qss'))
        self.setup_translatable_ui()
        logger.debug(f"[关于] 关于页就绪 {VERSION} {BUILD_DATE}")

    # 左栏

    def _initLeftPanel(self, parent):
        self._leftPanel = QWidget(parent)
        self._leftPanel.setObjectName("leftPanel")

        layout = QVBoxLayout(self._leftPanel)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(12)

        self._addHeaderCard(layout)
        self._addInfoCard(layout)
        self._addLinksCard(layout)

    def _addHeaderCard(self, layout):
        """头部卡片"""
        self._headerCard = CardWidget(self._leftPanel)
        self._headerCard.setObjectName("headerCard")
        self._headerLay = QVBoxLayout(self._headerCard)
        self._headerLay.setContentsMargins(32, 24, 32, 24)
        self._headerLay.setSpacing(8)
        self._headerLay.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self._appIconLabel = QLabel(self._headerCard)
        self._appIconLabel.setObjectName("appIconLabel")
        self._appIconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._appIconLabel.setScaledContents(True)          # 自动缩放
        self._appIconLabel.setMaximumSize(256, 256)         # 最大尺寸
        p = get_resPath(APP_ICON)
        if os.path.exists(p):
            self._appIconPixmap = QPixmap(p)
            self._appIconLabel.setPixmap(self._appIconPixmap)
            logger.debug(f"[关于] 应用图标已加载: {p}")
        else:
            self._appIconPixmap = None
            logger.warning(f"[关于] 应用图标资源缺失: {p}")

        self._appNameLabel = TitleLabel("Glimpseon", self._headerCard)
        self._appNameLabel.setObjectName("appNameLabel")
        self._appNameLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self._descLabel = BodyLabel(tr("about.description"), self._headerCard)
        self._descLabel.setObjectName("descriptionLabel")
        self._descLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._descLabel.setWordWrap(True)

        self._headerLay.addStretch()
        self._headerLay.addWidget(self._appIconLabel, 0, Qt.AlignmentFlag.AlignCenter)
        self._headerLay.addWidget(self._appNameLabel, 0, Qt.AlignmentFlag.AlignCenter)
        self._headerLay.addWidget(self._descLabel, 0, Qt.AlignmentFlag.AlignCenter)
        self._headerLay.addStretch()

        layout.addWidget(self._headerCard, 1)  # stretch=1

    def _addInfoCard(self, layout):
        card = CardWidget(self._leftPanel)
        card.setObjectName("contentCard")
        lay = QVBoxLayout(card)
        lay.setContentsMargins(20, 16, 20, 16)
        lay.setSpacing(4)

        authorLabel = CaptionLabel(f"{tr('about.author')}: HelloGaoo", card)
        authorLabel.setObjectName("infoAuthorLabel")

        lay.addWidget(authorLabel)

        layout.addWidget(card)

    def _addLinksCard(self, layout):
        """链接卡片"""
        card = CardWidget(self._leftPanel)
        card.setObjectName("contentCard")
        lay = QVBoxLayout(card)
        lay.setContentsMargins(16, 12, 16, 12)
        lay.setSpacing(4)

        # 链接数据
        links = [
            (FUI.GITHUB, tr("about.github_repo"),
             "github.com/HelloGaoo/Glimpseon",
             "https://github.com/HelloGaoo/Glimpseon"),
            (FUI.PEOPLE, tr("about.author_homepage"),
             "space.bilibili.com/1498602348",
             "https://space.bilibili.com/1498602348"),
            (FUI.DOCUMENT, tr("about.license"),
             "GNU General Public License v3.0",
             None),  # 许可证用回调
            (FUI.HEART, tr("about.thanks"),
             tr("about.thanks_desc"),
             "thanks"),  # 鸣谢特殊标记
        ]

        for icon, title_text, desc, url_or_callback in links:
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

            btn = PushButton(FUI.LINK, tr("about.visit"), card)
            btn.setFixedHeight(30)

            if url_or_callback == "thanks":
                btn.clicked.connect(self._showTechDialog)
            elif url_or_callback is None:
                btn.clicked.connect(self._viewLicense)
            else:
                url = url_or_callback
                btn.clicked.connect(lambda _, u=url: webbrowser.open(u))

            row.addWidget(iw)
            row.addLayout(tcol, 1)
            row.addWidget(btn)
            lay.addLayout(row)

        layout.addWidget(card)

    # 右栏

    def _initRightPanel(self, parent):
        self._rightPanel = QWidget(parent)
        self._rightPanel.setObjectName("rightPanel")

        layout = QVBoxLayout(self._rightPanel)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(12)

        self._addVersionCard(layout)
        self._addChangelogCard(layout)  # 占满高度
        self._addUpdateSettings(layout)

    def _addVersionCard(self, layout):
        self.versionCard = CardWidget(self._rightPanel)
        self.versionCard.setObjectName("contentCard")
        vLay = QHBoxLayout(self.versionCard)
        vLay.setContentsMargins(24, 20, 24, 20)
        vLay.setSpacing(16)

        leftInfo = QVBoxLayout()
        leftInfo.setSpacing(4)
        self.versionTitle = SubtitleLabel(f"Glimpseon {VERSION}", self.versionCard)
        self.versionTitle.setObjectName("versionTitle")
        self.buildDate = CaptionLabel(f"{tr('update.build_date')}: {BUILD_DATE}", self.versionCard)
        self.buildDate.setObjectName("buildDate")
        leftInfo.addWidget(self.versionTitle)
        leftInfo.addWidget(self.buildDate)

        self.updateStatusLayout = QHBoxLayout()
        self.updateStatusLayout.setSpacing(8)
        self.updateStatusIcon = QLabel(self.versionCard)
        self.updateStatusIcon.setFixedSize(16, 16)
        self.updateStatusIcon.setObjectName('updateStatusIcon')
        self.updateStatusLabel = BodyLabel(tr("update.status_ready"), self.versionCard)
        self.updateStatusLabel.setObjectName('updateStatusLabel')
        self.updateStatusLayout.addWidget(self.updateStatusIcon)
        self.updateStatusLayout.addWidget(self.updateStatusLabel)

        self.checkUpdateButton = PrimaryPushButton(FUI.SYNC, tr("update.check_update"), self.versionCard)
        self.checkUpdateButton.setFixedHeight(36)
        self.checkUpdateButton.clicked.connect(self.checkUpdateManual)

        vLay.addLayout(leftInfo, 1)
        vLay.addLayout(self.updateStatusLayout)
        vLay.addWidget(self.checkUpdateButton)

        layout.addWidget(self.versionCard)

    def _addChangelogCard(self, layout):
        """更新日志卡片"""
        self.changelogCard = CardWidget(self._rightPanel)
        self.changelogCard.setObjectName("contentCard")
        cLay = QVBoxLayout(self.changelogCard)
        cLay.setContentsMargins(24, 20, 24, 20)
        cLay.setSpacing(12)

        self.changelogTitle = SubtitleLabel(tr("update.changelog"), self.changelogCard)
        self.changelogTitle.setObjectName("changelogTitle")

        # 网页视图渲染更新日志（QTextBrowser 不加载远程图片）
        self._changelog_content = tr("update.changelog_auto_load")
        self.changelogContent = create_html_view(self.changelogCard, mouse_transparent=False)
        self.changelogContent.setPage(_ChangelogPage(self.changelogContent))
        # file: 页面默认禁访远程资源, 开启后 release 描述里的图片才能加载
        self.changelogContent.page().settings().setAttribute(
            QWebEngineSettings.WebAttribute.LocalContentCanAccessRemoteUrls, True)
        self._renderChangelog()

        cLay.addWidget(self.changelogTitle)
        cLay.addWidget(self.changelogContent, 1)  # stretch=1 占满高度

        layout.addWidget(self.changelogCard, 1)  # stretch=1 占满右栏高度

    def _addUpdateSettings(self, layout):
        self.autoCheckUpdateCard = SwitchSettingCard(
            FUI.UPDATE,
            tr("update.auto_check"),
            tr("update.auto_check_desc"),
            configItem=cfg.autoCheckUpdate,
            parent=self._rightPanel
        )
        self.autoUpdateCard = SwitchSettingCard(
            FUI.DOWNLOAD,
            tr("update.auto_update"),
            tr("update.auto_update_desc"),
            configItem=cfg.autoUpdate,
            parent=self._rightPanel
        )
        layout.addWidget(self.autoCheckUpdateCard)
        layout.addWidget(self.autoUpdateCard)


    _progress_signal = pyqtSignal(str)

    _progress_signal = pyqtSignal(str)

    def __setUpdateStatus(self, status: str):
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
        logger.debug(f"[关于] 更新状态切换: {status}")

    # 更新日志加载

    def __loadChangelog(self, auto_load=False):
        if auto_load and not cfg.autoCheckUpdate.value:
            logger.debug("[关于] 自动加载更新日志已关闭")
            return

        def load():
            try:
                changelog = get_github_changelog()
                if changelog:
                    logger.info(f"{'自动' if auto_load else '手动'}加载更新日志")
                    return changelog
                else:
                    logger.info("获取更新日志失败")
                    return tr("update.no_changelog")
            except Exception as e:
                logger.error(f"加载更新日志失败 {str(e)}")
                return tr("update.load_failed")

        def thread_func():
            logger.debug(f"[关于] 更新日志加载线程启动: auto_load={auto_load}")
            changelog_text = load()
            self._changelog_loaded_signal.emit(changelog_text)

        thread = threading.Thread(target=thread_func, daemon=True)
        thread.start()

    @pyqtSlot(str)
    def _on_changelog_loaded(self, changelog_text: str):
        logger.debug(f"[关于] 更新日志已加载: {len(changelog_text)} 字符")
        self._setChangelogContent(changelog_text)

    def _setChangelogContent(self, content: str):
        """缓存更新日志内容并重绘网页"""
        self._changelog_content = content or ""
        self._renderChangelog()

    def _renderChangelog(self):
        """按当前主题把更新日志渲染进网页"""
        content = self._changelog_content
        if _HTML_TAG_RE.search(content):
            # 已是 html 的内容(防御), 去 camo 后直接渲染
            content = _decamo_images(content)
            logger.debug(f"[关于] 更新日志以 html 方式渲染 ({len(content)} 字符)")
        else:
            # markdown 原文(GitHub release 描述 / doc/ChangeLogs 的 App.md)
            try:
                content = _md_to_html(content)
                logger.debug(f"[关于] 更新日志以 Markdown 方式渲染 -> html ({len(content)} 字符)")
            except Exception as e:
                logger.warning(f"[关于] Markdown 渲染失败 按纯文本: {e}")
                escaped = html.escape(content).replace("\n", "<br>")
                content = f"<p>{escaped}</p>"
        self.changelogContent.setHtml(_build_changelog_html(content), HTML_BASE_URL)

    # 检查更新

    def checkUpdateManual(self):
        if hasattr(self, 'has_new_version') and self.has_new_version:
            logger.debug(f"[关于] 已知新版本 {self.new_version} 直接下载")
            self.__downloadUpdate()
            return

        logger.info("手动检查版本")
        self.checkUpdateButton.setEnabled(False)
        self.updateStatusLabel.setText(tr("update.checking"))
        self.__setUpdateStatus('checking')

        def do_check():
            try:
                result = check_github_version_legacy()
            except Exception as e:
                logger.error(f"手动检查 检查更新出错 - {str(e)}")
                result = {'success': False, 'error': str(e)}
            result['auto_check'] = False
            self._check_result_signal.emit(result)

        thread = threading.Thread(target=do_check, daemon=True)
        thread.start()

    def checkUpdateAuto(self):
        if hasattr(self, 'has_new_version') and self.has_new_version:
            logger.debug(f"[关于] 自动检查: 已知新版本 {self.new_version} 直接下载")
            self.__downloadUpdate(auto_update=True)
            return

        logger.info("自动检查版本")

        def do_check():
            try:
                result = check_github_version_legacy()
            except Exception as e:
                logger.error(f"自动检查 检查更新出错 - {str(e)}")
                result = {'success': False, 'error': str(e)}
            result['auto_check'] = True
            self._check_result_signal.emit(result)

        thread = threading.Thread(target=do_check, daemon=True)
        thread.start()

    @staticmethod
    def _is_newer_version(remote: str, current: str) -> bool:
        try:
            def _parse(v):
                return tuple(int(p) for p in v.strip().lstrip('vV').split('.'))
            has_new = _parse(remote) > _parse(current)
            logger.debug(f"[关于] 版本比较: 远端 {remote} vs 当前 {current} -> {'发现新版本' if has_new else '无更新'}")
            return has_new
        except (ValueError, AttributeError):
            # 任一侧不可解析时不比较,避免把乱码版本误报成"有更新"
            logger.debug(f"[关于] 版本号不可解析 remote={remote!r} current={current!r}")
            return False

    @pyqtSlot(object)
    def _on_check_result(self, result: object):
        auto_check = result.get('auto_check', False)
        try:
            if not result.get('success', False):
                logger.warning(f"检查版本失败 - {result.get('error', '未知错误')}")
                if not auto_check:
                    self.checkUpdateButton.setEnabled(True)
                    self.updateStatusLabel.setText(tr("update.check_failed").format(error=result.get('error', tr("update.unknown_error"))))
                    self.__setUpdateStatus('error')
                return

            github_version = result.get('version')
            github_build_date = result.get('build_date')
            changelog = result.get('changelog')

            logger.info(f"检查结果 最新版本 {github_version} (构建日期 {github_build_date})")

            has_update = self._is_newer_version(github_version, VERSION)

            if has_update:
                logger.info(f"[关于] 发现新版本: {github_version} (当前 {VERSION})")
                self.has_new_version = True
                self.new_version = github_version
                self.update_url = result.get('update_url')

                self.updateStatusLabel.setText(tr("update.new_version_found").format(version=github_version))
                self.__setUpdateStatus('update_available')

                if not auto_check:
                    self.checkUpdateButton.setText(tr("update.download"))
                    self.checkUpdateButton.setIcon(FUI.DOWNLOAD)
                    self.checkUpdateButton.setEnabled(True)

                if changelog:
                    self._setChangelogContent(changelog)

                if auto_check and cfg.autoUpdate.value:
                    logger.info("自动检查 启用自动更新 下载")
                    QTimer.singleShot(2000, lambda: self.__downloadUpdate(auto_update=True))

            else:
                logger.info("已是最新版本")
                self.updateStatusLabel.setText(tr("update.latest"))
                self.__setUpdateStatus('latest')
                if not auto_check:
                    self.checkUpdateButton.setEnabled(True)
                if changelog:
                    self._setChangelogContent(changelog)
        except Exception as e:
            logger.error(f"更新 ui 失败 {e}")

    def _updateErrorState(self, msg):
        logger.warning(f"[关于] 更新流程失败 已切换到重试状态: {msg}")
        self.checkUpdateButton.setText(tr("update.retry"))
        self.checkUpdateButton.setIcon(FUI.SYNC)
        self.checkUpdateButton.setEnabled(True)
        self.updateStatusLabel.setText(tr("update.failed").format(error=msg))
        self.updateStatusLabel.setStyleSheet("color: #FF0000;")
        self.updateStatusIcon.setStyleSheet("background-color: #FF0000; border-radius: 8px;")

    def __downloadUpdate(self, auto_update=False):
        logger.info(f"[关于] 下载更新 自动={auto_update} 目标版本={getattr(self, 'new_version', '未知')}")
        self.checkUpdateButton.setEnabled(False)
        self.updateStatusLabel.setText(tr("update.downloading"))
        self.__setUpdateStatus('downloading')

        backup_folder = os.path.join(PACKAGE_ROOT, 'update_backup')

        def download_thread():
            try:
                progress_guard = {'milestone': -1}

                def progress_callback(current, total):
                    # 工作线程内不可用 QTimer,经信号回主线程
                    percent = (current / total) * 100
                    self._progress_signal.emit(f"{percent:.1f}")
                    if percent // 25 > progress_guard['milestone']:
                        progress_guard['milestone'] = percent // 25
                        logger.info(f"[关于] 下载进度: {percent:.1f}% ({current}/{total}字节)")

                logger.info(f"下载更新 {self.update_url}")
                # download_update 决定临时路径 对超时重试 返回实际 zip 路径
                download_path = download_update(self.update_url, progress_callback)
                if not download_path:
                    logger.error(f"[关于] 下载更新返回空路径: {self.update_url}")
                    raise Exception("下载更新失败")

                QTimer.singleShot(0, lambda: self.updateStatusLabel.setText(tr("update.extracting")))

                # 按新版本号解压到 app-<version> 写入 record.json
                new_version_dir = extract_update(download_path, self.new_version)
                if not new_version_dir:
                    logger.error(f"[关于] 解压更新失败 无有效目录: {download_path}")
                    raise Exception("解压更新失败")

                cleanup_update_files()
                logger.info(f"[关于] 更新包已解压 {new_version_dir}")

                if auto_update:
                    QTimer.singleShot(0, lambda: self.updateStatusLabel.setText(tr("update.backing_up")))
                    try:
                        if os.path.exists(backup_folder):
                            shutil.rmtree(backup_folder)
                        shutil.copytree(APP_DIR, backup_folder,
                                        ignore=shutil.ignore_patterns('update_temp', 'update_backup', 'logs', '*.log'))
                        logger.info("已创建版本备份")
                    except Exception as e:
                        logger.warning(f"创建备份失败 {str(e)}")

                if not deploy_update(new_version_dir):
                    logger.error(f"[关于] 激活新版本失败: {new_version_dir}")
                    raise Exception("激活新版本失败")

                script_path = create_update_script(new_version_dir)
                if not script_path:
                    logger.error("[关于] 创建更新脚本失败")
                    raise Exception("创建更新脚本失败")
                logger.info(f"[关于] 更新脚本已创建 即将重启应用: {script_path}")
                QTimer.singleShot(0, lambda: self.updateStatusLabel.setText(tr("update.preparing")))

                subprocess.Popen(
                    f'cmd /c start "Glimpseon Update" /MIN "{script_path}"',
                    shell=True,
                    creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.DETACHED_PROCESS
                )

                QTimer.singleShot(0, lambda: self.updateStatusLabel.setText(tr("update.complete")))
                QApplication.instance().quit()

            except Exception as e:
                error_msg = str(e)
                logger.error(f"更新失败 {error_msg}")
                cleanup_update_files()
                QTimer.singleShot(0, lambda msg=error_msg: self._updateErrorState(msg))
                self.has_new_version = False

        thread = threading.Thread(target=download_thread, daemon=True)
        thread.start()

    # 槽函数

    def _onThemeChanged(self, theme: Theme):
        logger.debug(f"[关于] 主题变更: {theme}")
        self.setStyleSheet(load_qss('about.qss'))
        if getattr(self, 'changelogContent', None) is not None:
            self._renderChangelog()

    def _viewLicense(self):
        logger.info("[关于] 打开许可证信息")
        license_path = get_resPath("LICENSE")
        intro = tr("about.license_intro")
        show_text_file(tr("about.license_title"), intro,
                       license_path, parent=self.window())

    def _showTechDialog(self):
        logger.info("[关于] 打开鸣谢列表")
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

        deps = _get_dependencies()
        for name, ver, license_name, url in deps:
            display = f"{name}  {ver}   ({license_name})"
            logger.debug(f"[关于] 渲染依赖项: {display}")
            item = QListWidgetItem("")
            item.setData(Qt.ItemDataRole.UserRole, url)
            item.setSizeHint(QSize(0, 34))
            item.setFlags(item.flags() & ~Qt.ItemFlag.ItemIsEditable)
            self.listWidget.addItem(item)

            w = QWidget()
            lay = QHBoxLayout(w)
            lay.setContentsMargins(12, 0, 8, 0)
            lay.setSpacing(8)

            lbl = BodyLabel(display, w)
            lbl.setObjectName("depItemLabel")

            btn = PushButton(FUI.LINK, tr("common.link"), w)
            btn.setObjectName("depGithubBtn")
            btn.hide()
            btn.clicked.connect(lambda checked, u=url: webbrowser.open(u))

            lay.addWidget(lbl, 1)
            lay.addWidget(btn)
            self.listWidget.setItemWidget(item, w)

        self.listWidget.currentRowChanged.connect(
            self._onSelectionChanged)

        self.viewLayout.setSpacing(8)
        self.viewLayout.addWidget(self.titleLabel)
        self.viewLayout.addWidget(self.listWidget)
        self.widget.setMinimumWidth(500)

        self.cancelButton.hide()
        self.yesButton.setText(tr("common.confirm"))

    def _onSelectionChanged(self, row):
        logger.debug(f"[关于] 鸣谢列表选中行: {row}")
        for i in range(self.listWidget.count()):
            item = self.listWidget.item(i)
            w = self.listWidget.itemWidget(item) if item else None
            if w:
                btn = w.findChild(PushButton, "depGithubBtn")
                if btn:
                    btn.hide()

        item = self.listWidget.item(row)
        if not item or not item.data(Qt.ItemDataRole.UserRole):
            logger.debug("[关于] 当前鸣谢条目无仓库链接 不显示 GitHub 按钮")
            return
        w = self.listWidget.itemWidget(item)
        if w:
            btn = w.findChild(PushButton, "depGithubBtn")
            if btn:
                btn.show()


def _get_dependencies():
    from importlib.metadata import version, PackageNotFoundError

    path = get_resPath(os.path.join('resource', 'credits.json'))
    if not os.path.exists(path):
        logger.debug(f"[关于] credits.json 不存在: {path}")
        return []
    try:
        with open(path, 'r', encoding='utf-8') as f:
            deps = json.load(f)
    except Exception as e:
        logger.debug(f"[关于] credits.json 解析失败: {e}")
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
            logger.debug(f"[关于] 依赖元数据缺失: {meta_name}")
            ver = ''
        result.append((display, ver, license_name, url))
    logger.debug(f"[关于] 鸣谢已加载 {len(result)}项")
    return result