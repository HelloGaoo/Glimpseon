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
关于界面
"""

import json
import logging
import os
import sys
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:sys.path.insert(0, _BASE_DIR)

import shutil
import subprocess
import threading
import webbrowser

from PyQt6.QtCore import Qt, QSize, QTimer, pyqtSignal, pyqtSlot
from PyQt6.QtGui import QPixmap, QFont
from PyQt6.QtWidgets import (
    QApplication, QHBoxLayout, QLabel, QVBoxLayout, QWidget,
    QGridLayout, QListWidget, QListWidgetItem, QTextBrowser,
)
from qfluentwidgets import (
    CardWidget,
    IconWidget,
    BodyLabel,
    CaptionLabel,
    TitleLabel,
    StrongBodyLabel,
    TextEdit,
    PushButton,
    PrimaryPushButton,
    SwitchSettingCard,
    Theme,
    ListWidget,
    MessageBoxBase,
    ScrollArea,
)

from core.config import cfg
from core.constants import BASE_DIR, get_resPath, load_qss
from core.utils import tr, TranslatableWidget, FUI
from core.updater import check_github_verison, get_github_changelog, download_update, extract_update, create_update_script
from version import BUILD_DATE, VERSION

from .common import show_text_file

logger = logging.getLogger("ClassLively.ui.about")


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

        self._footerLabel = CaptionLabel("© 2025 AzeLightStudios. All rights reserved.", self._container)
        self._footerLabel.setObjectName("copyrightLabel")
        self._footerLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._topLayout.addSpacing(16)
        self._topLayout.addWidget(self._footerLabel)

        self.titleLabel = QLabel(tr("navigation.about"), self)
        self.titleLabel.setObjectName('settingLabel')
        self.titleLabel.move(60, 63)

        self._connectSignalToSlot()
        self._check_result_signal.connect(self._on_check_result)
        self._changelog_loaded_signal.connect(self._on_changelog_loaded)
        self.setStyleSheet(load_qss('about.qss'))
        self.setup_translatable_ui()

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
        self._appIconLabel.setMinimumSize(64, 64)
        p = get_resPath(os.path.join('resource', 'icons', 'CY.png'))
        if os.path.exists(p):
            self._appIconPixmap = QPixmap(p)
            self._appIconLabel.setPixmap(self._appIconPixmap.scaled(
                64, 64, Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation))
        else:
            self._appIconPixmap = None

        self._appNameLabel = TitleLabel("ClassLively", self._headerCard)
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

        verLabel = BodyLabel(f"v{VERSION}", card)
        verLabel.setObjectName("infoVersionLabel")
        dateLabel = CaptionLabel(f"{tr('about.build_date')}: {BUILD_DATE}", card)
        dateLabel.setObjectName("infoDateLabel")
        authorLabel = CaptionLabel(f"{tr('about.author')}: HelloGaoo", card)
        authorLabel.setObjectName("infoAuthorLabel")

        lay.addWidget(verLabel)
        lay.addWidget(dateLabel)
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
             "github.com/HelloGaoo/ClassLively",
             "https://github.com/HelloGaoo/ClassLively"),
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
        self.versionTitle = QLabel(f"ClassLively {VERSION}", self.versionCard)
        self.versionTitle.setObjectName("versionTitle")
        self.buildDate = QLabel(f"{tr('update.build_date')}: {BUILD_DATE}", self.versionCard)
        self.buildDate.setObjectName("buildDate")
        leftInfo.addWidget(self.versionTitle)
        leftInfo.addWidget(self.buildDate)

        self.updateStatusLayout = QHBoxLayout()
        self.updateStatusLayout.setSpacing(8)
        self.updateStatusIcon = QLabel(self.versionCard)
        self.updateStatusIcon.setFixedSize(16, 16)
        self.updateStatusIcon.setObjectName('updateStatusIcon')
        self.updateStatusLabel = QLabel(tr("update.status_ready"), self.versionCard)
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

        self.changelogTitle = QLabel(tr("update.changelog"), self.changelogCard)
        self.changelogTitle.setObjectName("changelogTitle")

        # QTextBrowser 显示 Markdown
        self.changelogContent = QTextBrowser(self.changelogCard)
        self.changelogContent.setObjectName("changelogTextEdit")
        self.changelogContent.setOpenExternalLinks(False)
        self.changelogContent.setFont(QFont("HarmonyOS Sans", 12))
        self.changelogContent.setPlaceholderText(tr("update.changelog_auto_load"))
        self.changelogContent.setStyleSheet("""
            QTextBrowser {
                background: transparent;
                border: none;
                color: #CCCCCC;
            }
        """)

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


    def _connectSignalToSlot(self):
        pass


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

    # 更新日志加载

    def __loadChangelog(self, auto_load=False):
        if auto_load and not cfg.autoCheckUpdate.value:
            return

        def load():
            try:
                changelog = get_github_changelog()
                if changelog:
                    logger.info(f"{'自动' if auto_load else '手动'}加载：成功从 GitHub 获取更新日志")
                    return changelog
                else:
                    logger.info("GitHub 获取失败")
                    return tr("update.no_changelog")
            except Exception as e:
                logger.error(f"加载更新日志失败：{str(e)}")
                return tr("update.load_failed")

        def thread_func():
            changelog_text = load()
            self._changelog_loaded_signal.emit(changelog_text)

        thread = threading.Thread(target=thread_func, daemon=True)
        thread.start()

    @pyqtSlot(str)
    def _on_changelog_loaded(self, changelog_text: str):
        # setMarkdown 提示 Markdown
        self.changelogContent.setMarkdown(changelog_text)

    # 检查更新

    def checkUpdateManual(self):
        if hasattr(self, 'has_new_version') and self.has_new_version:
            self.__downloadUpdate()
            return

        logger.info("手动检查：开始检查版本")
        self.checkUpdateButton.setEnabled(False)
        self.updateStatusLabel.setText(tr("update.checking"))
        self.__setUpdateStatus('checking')

        def do_check():
            try:
                result = check_github_verison()
            except Exception as e:
                logger.error(f"手动检查：检查更新时出错 - {str(e)}")
                result = {'success': False, 'error': str(e)}
            result['auto_check'] = False
            self._check_result_signal.emit(result)

        thread = threading.Thread(target=do_check, daemon=True)
        thread.start()

    def checkUpdateAuto(self):
        if hasattr(self, 'has_new_version') and self.has_new_version:
            self.__downloadUpdate(auto_update=True)
            return

        logger.info("自动检查：开始检查版本")

        def do_check():
            try:
                result = check_github_verison()
            except Exception as e:
                logger.error(f"自动检查：检查更新时出错 - {str(e)}")
                result = {'success': False, 'error': str(e)}
            result['auto_check'] = True
            self._check_result_signal.emit(result)

        thread = threading.Thread(target=do_check, daemon=True)
        thread.start()

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

            logger.info(f"检查结果：GitHub 最新版本：{github_version} (构建日期：{github_build_date})")

            has_update = (github_version != VERSION)

            if has_update:
                self.has_new_version = True
                self.new_version = github_version
                self.build_date = github_build_date
                self.update_url = result.get('update_url')

                self.updateStatusLabel.setText(tr("update.new_version_found").format(version=github_version))
                self.__setUpdateStatus('update_available')

                if not auto_check:
                    self.checkUpdateButton.setText(tr("update.download"))
                    self.checkUpdateButton.setIcon(FUI.DOWNLOAD)
                    self.checkUpdateButton.setEnabled(True)

                if changelog:
                    self.changelogContent.setMarkdown(changelog)

                if auto_check and cfg.autoUpdate.value:
                    logger.info("自动检查：启用自动更新，开始下载")
                    QTimer.singleShot(2000, lambda: self.__downloadUpdate(auto_update=True))

            else:
                logger.info("已是最新版本")
                self.updateStatusLabel.setText(tr("update.latest"))
                self.__setUpdateStatus('latest')
                if not auto_check:
                    self.checkUpdateButton.setEnabled(True)
                if changelog:
                    self.changelogContent.setMarkdown(changelog)
        except Exception as e:
            logger.error(f"更新 UI 失败：{e}")

    def _updateErrorState(self, msg):
        self.checkUpdateButton.setText(tr("update.retry"))
        self.checkUpdateButton.setIcon(FUI.SYNC)
        self.checkUpdateButton.setEnabled(True)
        self.updateStatusLabel.setText(tr("update.failed").format(error=msg))
        self.updateStatusLabel.setStyleSheet("color: #FF0000;")
        self.updateStatusIcon.setStyleSheet("background-color: #FF0000; border-radius: 8px;")

    def __downloadUpdate(self, auto_update=False):
        self.checkUpdateButton.setEnabled(False)
        self.updateStatusLabel.setText(tr("update.downloading"))
        self.__setUpdateStatus('downloading')

        update_folder = os.path.join(BASE_DIR, 'update_temp')
        download_path = os.path.join(update_folder, 'update.7z')
        backup_folder = os.path.join(BASE_DIR, 'update_backup')

        def download_thread():
            try:
                if os.path.exists(update_folder):
                    shutil.rmtree(update_folder)
                os.makedirs(update_folder)

                def progress_callback(current, total):
                    percent = (current / total) * 100
                    QTimer.singleShot(0, lambda p=percent: self.updateStatusLabel.setText(tr("update.downloading_progress").format(percent=f"{p:.1f}")))

                logger.info(f"正在从 {self.update_url} 下载更新")
                download_success = False
                max_download_retries = 3

                for retry in range(max_download_retries):
                    try:
                        if retry > 0:
                            logger.info(f"下载更新重试 {retry}/{max_download_retries}")
                            QTimer.singleShot(0, lambda r=retry, m=max_download_retries: self.updateStatusLabel.setText(tr("update.download_retry").format(retry=r, max_retries=m)))

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

                QTimer.singleShot(0, lambda: self.updateStatusLabel.setText(tr("update.extracting")))

                extract_folder = os.path.join(update_folder, 'extracted')
                if not extract_update(download_path, extract_folder):
                    raise Exception("解压更新失败")

                if os.path.exists(download_path):
                    os.remove(download_path)

                if auto_update:
                    QTimer.singleShot(0, lambda: self.updateStatusLabel.setText(tr("update.backing_up")))
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
                QTimer.singleShot(0, lambda: self.updateStatusLabel.setText(tr("update.preparing")))

                subprocess.Popen(
                    f'cmd /c start "ClassLively Update" /MIN "{script_path}"',
                    shell=True,
                    creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.DETACHED_PROCESS
                )

                logger.info("更新脚本已启动，准备退出应用")
                QTimer.singleShot(0, lambda: self.updateStatusLabel.setText(tr("update.complete")))
                QApplication.instance().quit()

            except Exception as e:
                logger.error(f"更新失败：{str(e)}")
                if os.path.exists(update_folder):
                    try:
                        shutil.rmtree(update_folder)
                    except Exception:
                        pass
                QTimer.singleShot(0, lambda: self._updateErrorState(str(e)))
                self.has_new_version = False

        thread = threading.Thread(target=download_thread, daemon=True)
        thread.start()

    # 槽函数

    def _onThemeChanged(self, theme: Theme):
        self.setStyleSheet(load_qss('about.qss'))

    def _viewLicense(self):
        license_path = get_resPath("LICENSE")
        intro = tr("about.license_intro")
        show_text_file(tr("about.license_title"), intro,
                       license_path, parent=self.window())

    def _showTechDialog(self):
        w = _TechDialog(self.window())
        w.exec()

    def resizeEvent(self, event):
        """调整图标大小"""
        super().resizeEvent(event)
        if hasattr(self, '_appIconPixmap') and self._appIconPixmap and self._appIconPixmap and not self._appIconPixmap.isNull():
            card_height = self._headerCard.height() if hasattr(self, '_headerCard') and self._headerCard else 200
            icon_size = min(128, max(64, card_height // 3))
            scaled_pixmap = self._appIconPixmap.scaled(
                icon_size, icon_size, Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation)
            if not scaled_pixmap.isNull():
                self._appIconLabel.setPixmap(scaled_pixmap)


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
        for i in range(self.listWidget.count()):
            item = self.listWidget.item(i)
            w = self.listWidget.itemWidget(item) if item else None
            if w:
                btn = w.findChild(PushButton, "depGithubBtn")
                if btn:
                    btn.hide()

        item = self.listWidget.item(row)
        if not item or not item.data(Qt.ItemDataRole.UserRole):
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