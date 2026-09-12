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
软件下载界面模块（HTML）
"""

import base64
import json
import os
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from string import Template
from typing import Optional

from PyQt6.QtCore import QBuffer, QIODevice, QObject, QUrl, Qt, pyqtSignal, pyqtSlot
from PyQt6.QtGui import QColor, QDesktopServices, QPixmap
from PyQt6.QtWidgets import QVBoxLayout, QWidget
from PyQt6.QtWebChannel import QWebChannel
from qfluentwidgets import Theme, isDarkTheme, qconfig

from core.config import cfg
from core.constants import FONT_FAMILY
from core.downloader import DOWNLOAD_SOURCES, DEFAULT_SOURCE, Downloader, set_download_src
from core.utils import tr
from core.logger import logger
from resource.url_dir import url_dir

from ui.common import create_html_view

_software_icon_cache = {}


def get_cached_icon_data(icon_path: str, size: int = 64) -> Optional[str]:
    """读ico 转换为 base64 PNG data URI"""
    if not icon_path or not os.path.exists(icon_path):
        return None
    if icon_path in _software_icon_cache:
        return _software_icon_cache[icon_path]
    try:
        pixmap = QPixmap(icon_path)
        if pixmap.isNull():
            return None
        scaled = pixmap.scaled(size, size, Qt.AspectRatioMode.KeepAspectRatio,
                               Qt.TransformationMode.FastTransformation)
        buffer = QBuffer()
        buffer.open(QIODevice.OpenModeFlag.WriteOnly)
        scaled.save(buffer, "PNG")
        data_uri = "data:image/png;base64," + base64.b64encode(bytes(buffer.data())).decode("ascii")
        _software_icon_cache[icon_path] = data_uri
        return data_uri
    except Exception:
        return None


class DownloadBridge(QObject):
    """JS > Python 桥"""

    def __init__(self, interface):
        super().__init__()
        self._interface = interface

    @pyqtSlot(str)
    def download(self, name: str):
        self._interface._handleDownload(name)

    @pyqtSlot(str)
    def openLink(self, url: str):
        if url:
            QDesktopServices.openUrl(QUrl(url))

    @pyqtSlot(str)
    def setMode(self, mode: str):
        self._interface._mode = "multi" if mode == "multi" else "single"

    @pyqtSlot(str)
    def setSource(self, key: str):
        self._interface._applySource(key)

    @pyqtSlot(str)
    def startBatch(self, names_json: str):
        try:
            names = json.loads(names_json)
        except Exception:
            return
        if isinstance(names, list):
            self._interface._handleStartDownload([str(n) for n in names])

    @pyqtSlot(int, bool)
    def confirmResult(self, confirm_id: int, ok: bool):
        self._interface._onConfirmResult(confirm_id, ok)


class DownloadInterface(QWidget):
    """软件下载界面（HTML）"""

    # 子线程 > 主线程 通知
    _sigProgress = pyqtSignal(str, int)
    _sigError = pyqtSignal(str, str)
    _sigComplete = pyqtSignal(str)
    _sigBatchDone = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("download")

        self._allSoftwareData = []   # flat list: {section, icon_path, name, description, link}
        self._currentDataSection = None

        self._mode = "single"
        self._downloadingNames = set()
        self._progress = {}          # name -> int percent
        self._pendingLayout = False  # 隐藏时延迟到首次显示再渲染
        self._confirmSeq = 0
        self._pendingConfirms = {}   # id -> callback
        self.downloader = Downloader(logger)
        self.download_executor = None
        self.futures = []

        self.webView = create_html_view(self, mouse_transparent=False)
        self._bridge = DownloadBridge(self)
        self._channel = QWebChannel(self)
        self._channel.registerObject("bridge", self._bridge)
        self.webView.page().setWebChannel(self._channel)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self.webView)

        cfg.themeChanged.connect(self._onThemeChanged)
        cfg.themeColor.valueChanged.connect(self._onThemeColorChanged)

        self._sigProgress.connect(self._js_set_progress)
        self._sigError.connect(self._showDownloadError)
        self._sigComplete.connect(self._showDownloadComplete)
        self._sigBatchDone.connect(lambda: self._js("window.glimpseon && window.glimpseon.uiBatchDone()"))

    # 主题色
    def _accent_hex(self) -> str:
        """当前主题色（16）"""
        value = cfg.themeColor.value
        try:
            color = QColor(value) if isinstance(value, str) else value
            if isinstance(color, QColor) and color.isValid():
                return color.name()
        except Exception:
            pass
        return "#30c361"

    def _onThemeColorChanged(self, *_):
        self._render()

    # 数据填充
    def addSection(self, title):
        if not title:
            title = tr("download.common_software")  # 常用软件
        self._currentDataSection = title

    def addSoftware(self, icon_path, name, description, link=None):
        sec = self._currentDataSection or tr("download.common_software")
        self._allSoftwareData.append({
            'section': sec,
            'icon_path': icon_path,
            'name': name,
            'description': description,
            'link': link
        })

    def _onDataPopulated(self):
        self._requestRender()

    def _requestRender(self):
        if self.isVisible():
            self._render()
        else:
            self._pendingLayout = True

    # HTML
    def _collectSections(self):
        """顺序聚合 [{title, items}]"""
        sections = []
        for item in self._allSoftwareData:
            if not sections or sections[-1]["title"] != item["section"]:
                sections.append({"title": item["section"], "items": []})
            sections[-1]["items"].append({
                "name": item["name"],
                "desc": item["description"],
                "icon": get_cached_icon_data(item["icon_path"]) or "",
                "link": item.get("link") or ""
            })
        return sections

    def _buildTheme(self) -> dict:
        dark = isDarkTheme()
        if dark:
            return {
                "ink": "#ffffff", "sub": "#888888", "label": "#ffffff",
                "radio_text": "#ffffff", "radio_border": "#888888",
                "card_bg": "rgba(255,255,255,0.05)", "card_bd": "rgba(255,255,255,0.08)",
                "card_bg_hover": "rgba(255,255,255,0.08)",
                "ctrl_bg": "rgba(255,255,255,0.05)", "ctrl_bd": "rgba(255,255,255,0.08)",
                "ctrl_bg_hover": "rgba(255,255,255,0.08)", "ctrl_bd_hover": "rgba(255,255,255,0.15)",
                "btn_bg": "rgba(255,255,255,0.08)", "btn_bd": "rgba(255,255,255,0.08)",
                "btn_bg_hover": "rgba(255,255,255,0.15)", "btn_bd_hover": "rgba(255,255,255,0.15)",
                "link_hover": "rgba(255,255,255,0.1)",
                "scroll": "rgba(255,255,255,0.25)", "scroll_hover": "rgba(255,255,255,0.4)",
                "ring_track": "rgba(255,255,255,0.12)",
                "pop_bg": "#2b2b30", "pop_bd": "rgba(255,255,255,0.08)",
                "scrim": "rgba(28,28,30,0.78)",
            }
        return {
            "ink": "#000000", "sub": "#666666", "label": "#333333",
            "radio_text": "#333333", "radio_border": "#999999",
            "card_bg": "rgba(255,255,255,0.8)", "card_bd": "rgba(0,0,0,0.06)",
            "card_bg_hover": "rgba(255,255,255,1.0)",
            "ctrl_bg": "rgba(255,255,255,0.8)", "ctrl_bd": "rgba(0,0,0,0.06)",
            "ctrl_bg_hover": "rgba(255,255,255,1.0)", "ctrl_bd_hover": "rgba(0,0,0,0.15)",
            "btn_bg": "rgba(255,255,255,0.6)", "btn_bd": "rgba(0,0,0,0.06)",
            "btn_bg_hover": "rgba(255,255,255,1.0)", "btn_bd_hover": "rgba(0,0,0,0.15)",
            "link_hover": "rgba(0,0,0,0.05)",
            "scroll": "rgba(0,0,0,0.2)", "scroll_hover": "rgba(0,0,0,0.35)",
            "ring_track": "rgba(0,0,0,0.08)",
            "pop_bg": "#ffffff", "pop_bd": "rgba(0,0,0,0.08)",
            "scrim": "rgba(248,248,250,0.85)",
        }

    def _render(self):
        self.webView.setHtml(self._build_html(), QUrl("file:///glimpseon/download/"))

    def showEvent(self, event):
        """首次显示"""
        super().showEvent(event)
        if self._pendingLayout:
            self._pendingLayout = False
            self._render()
        else:
            self._js("window.relayout && window.relayout()")

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self._js("window.relayout && window.relayout()")

    def _build_html(self) -> str:
        theme = self._buildTheme()
        accent = self._accent_hex()
        payload = {
            "title": tr("navigation.download"),
            "sections": self._collectSections(),
            "state": {
                "mode": self._mode,
                "downloading": {name: int(self._progress.get(name, 0)) for name in self._downloadingNames},
            },
            "sources": [{"key": k, "label": tr(DOWNLOAD_SOURCES[k]["name_key"])} for k in DOWNLOAD_SOURCES],
            "sourceKey": cfg.downloadSource.value if cfg.downloadSource.value in DOWNLOAD_SOURCES else DEFAULT_SOURCE,
            "i18n": {
                "select_mode": tr("download.select_mode"),
                "single_mode": tr("download.single_mode"),
                "multi_mode": tr("download.multi_mode"),
                "select_all": tr("download.select_all"),
                "deselect_all": tr("download.deselect_all"),
                "download_source": tr("download.download_source"),
                "start_download": tr("download.start_download"),
                "download_btn": tr("download.download_btn"),
                "open_official_website": tr("download.open_official_website"),
                "confirm": tr("dialog.confirm"),
                "cancel": tr("dialog.cancel"),
            },
        }
        payload_json = json.dumps(payload, ensure_ascii=False).replace("</", "<\\/")
        return self._HTML_TEMPLATE.substitute(font=FONT_FAMILY, accent=accent, **theme, payload_json=payload_json)

    def _onThemeChanged(self, theme: Theme):
        self._render()

    # Python > JS
    def _js(self, code: str):
        self.webView.page().runJavaScript(code)

    def _js_set_state(self, name: str, downloading: bool, percent: int = 0):
        args = json.dumps(name, ensure_ascii=False)
        if downloading:
            self._js(f"window.glimpseon && window.glimpseon.uiStart({args},{int(percent)})")
        else:
            self._js(f"window.glimpseon && window.glimpseon.uiError({args})")

    def _js_set_progress(self, name: str, percent: int):
        args = json.dumps(name, ensure_ascii=False)
        self._js(f"window.glimpseon && window.glimpseon.uiProgress({args},{int(percent)})")

    # HTML 通知与确认框
    def _toast(self, kind: str, title: str, content: str, duration: int = 3000):
        self._js(
            "window.glimpseon && window.glimpseon.toast("
            f"{json.dumps(kind)}, {json.dumps(title, ensure_ascii=False)}, "
            f"{json.dumps(content, ensure_ascii=False)}, {int(duration)})"
        )

    def _confirm(self, title: str, content: str, callback):
        self._confirmSeq += 1
        cid = self._confirmSeq
        self._pendingConfirms[cid] = callback
        self._js(
            "window.glimpseon && window.glimpseon.confirm("
            f"{cid}, {json.dumps(title, ensure_ascii=False)}, {json.dumps(content, ensure_ascii=False)})"
        )

    def _onConfirmResult(self, confirm_id: int, ok: bool):
        cb = self._pendingConfirms.pop(int(confirm_id), None)
        if cb is None:
            return
        try:
            cb(bool(ok))
        except Exception:
            logger.error("确认框回调异常", exc_info=True)

    # 下载源
    def _applySource(self, source_key: str):
        if source_key and source_key in DOWNLOAD_SOURCES:
            qconfig.set(cfg.downloadSource, source_key)
            set_download_src(source_key)

    def _get_url(self, cache_file):
        if 'url' in cache_file:
            return cache_file['url']
        if 'github_path' in cache_file:
            source_key = cfg.downloadSource.value
            prefix = DOWNLOAD_SOURCES.get(source_key, DOWNLOAD_SOURCES[DEFAULT_SOURCE])["prefix"]
            return f'{prefix}{cache_file["github_path"]}'
        return None

    # 下载逻辑
    def _handleDownload(self, software_name: str):
        """单个下载按钮点击"""
        self._confirm(
            tr("download.confirm_download"),  # 确认下载
            tr("download.confirm_download_single").format(name=software_name),
            lambda ok: self._startSingleDownload(software_name) if ok else None
        )

    def _startSingleDownload(self, software_name: str):
        self._beginDownloadUI(software_name)
        self._toast("success", tr("download.starting_download"),
                    tr("download.downloading_single").format(name=software_name), 3000)

        def download_thread():
            try:
                cache_file = self._findCacheFile(software_name)
                if not cache_file:
                    self._notifyError(software_name, tr("download.error_no_url"))
                    return
                download_url = self._get_url(cache_file)
                if not download_url:
                    self._notifyError(software_name, tr("download.error_cannot_get_url"))
                    return
                cache_file['url'] = download_url
                self._runInstall(software_name, cache_file, delay_complete=True)
            except Exception as e:
                self._notifyError(software_name, str(e))

        threading.Thread(target=download_thread, daemon=True).start()

    def _handleStartDownload(self, selected_names: list):
        """批量下载"""
        if not selected_names:
            self._toast("warning", tr("download.no_selection"),
                        tr("download.please_select_first"), 3000)
            return

        software_list = "\n".join(selected_names)
        self._confirm(
            tr("download.confirm_download"),
            tr("download.confirm_batch").format(list=software_list),
            lambda ok: self._startBatchDownload(selected_names) if ok else None
        )

    def _startBatchDownload(self, selected_names: list):
        self._toast("success", tr("download.starting_download"),
                    tr("download.downloading_batch").format(count=len(selected_names)), 3000)

        for software_name in selected_names:
            self._beginDownloadUI(software_name)

        if self.download_executor is not None:
            try:
                logger.info("关闭旧线程池")
                self.download_executor.shutdown(wait=True)
                logger.info("旧线程池已关闭")
            except Exception as e:
                logger.error(f"关闭旧线程池出错: {str(e)}")
        max_workers = os.cpu_count() if os.cpu_count() else 4
        self.download_executor = ThreadPoolExecutor(max_workers=max_workers)
        logger.info(f"创建线程池 最大并发数: {max_workers}")
        self.futures = []

        for software_name in list(selected_names):
            future = self.download_executor.submit(self._runInstallTask, software_name)
            self.futures.append(future)

        def _wait_tasks():
            while True:
                all_done = all(future.done() for future in self.futures)
                if all_done:
                    break
                time.sleep(1)
            try:
                logger.info("关闭线程池")
                self.download_executor.shutdown(wait=True)
                logger.info("线程池已关闭")
            except Exception as e:
                logger.error(f"关闭线程池出错: {str(e)}")
            self._sigBatchDone.emit()

        threading.Thread(target=_wait_tasks, daemon=True).start()

    # 私有函数
    def _findCacheFile(self, software_name):
        _match_name = software_name.replace(" ", "").replace("[", "").replace("]", "")
        for item in url_dir:
            _match_filename = item.get("filename", "").replace(" ", "").replace("[", "").replace("]", "")
            if _match_filename.startswith(_match_name):
                cache_file = item.copy()
                if "hash" not in cache_file:
                    cache_file["hash"] = ""
                return cache_file
        return None

    def _beginDownloadUI(self, software_name: str):
        """界面进入下载中状态"""
        self._downloadingNames.add(software_name)
        self._progress[software_name] = 0
        self._js_set_state(software_name, True, 0)

    def _notifyError(self, software_name: str, error_msg: str):
        """子线程 -> 主线程 错误通知"""
        self._sigError.emit(software_name, error_msg)

    def _showDownloadError(self, software_name: str, error_msg: str):
        self._downloadingNames.discard(software_name)
        self._progress.pop(software_name, None)
        self._js_set_state(software_name, False)
        self._toast("error", tr("download.install_failed"),
                    tr("download.install_error").format(name=software_name, error=error_msg), 5000)

    def _showDownloadComplete(self, software_name: str):
        self._downloadingNames.discard(software_name)
        self._toast("success", tr("download.install_complete"),
                    tr("download.install_success").format(name=software_name), 3000)

    def _runInstall(self, software_name: str, cache_file: dict, delay_complete: bool = False):
        """子调用安装方法"""

        def update_progress(name_arg, percent):
            try:
                val = int(round(float(percent)))
            except Exception:
                return
            self._progress[software_name] = val
            self._sigProgress.emit(software_name, val)

        def _on_download_done(name_arg=None):
            logger.info(f"{software_name}: 下载完成")

        processed_name = software_name.replace(" ", "").replace("[", "").replace("]", "")
        install_method_name = f"_install_{processed_name}"
        if not hasattr(self.downloader, install_method_name):
            self._notifyError(software_name, tr("download.error_no_install_method"))
            return

        try:
            logger.info(f"{software_name}: 调用 {install_method_name}")
            getattr(self.downloader, install_method_name)(
                software_name,
                cache_file,
                progress_callback=update_progress,
                download_complete_callback=_on_download_done
            )
            self._progress[software_name] = 100
            self._sigProgress.emit(software_name, 100)
            if delay_complete:
                time.sleep(0.5)   
            self._sigComplete.emit(software_name)
        except Exception as e:
            logger.error(f"{software_name}: 安装函数异常 - {e}", exc_info=True)
            self._notifyError(software_name, str(e))

    def _runInstallTask(self, software_name: str):
        """线程池任务：查找链接并安装"""
        try:
            cache_file = self._findCacheFile(software_name)
            if not cache_file:
                self._notifyError(software_name, tr("download.error_no_url"))
                return
            download_url = self._get_url(cache_file)
            if not download_url:
                self._notifyError(software_name, tr("download.error_cannot_get_url"))
                return
            cache_file['url'] = download_url
            self._runInstall(software_name, cache_file, delay_complete=False)
        except Exception as e:
            self._notifyError(software_name, str(e))

    _HTML_TEMPLATE = Template(r'''
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { width: 100%; height: 100%; background: transparent; }
  body {
    font-family: $font;
    overflow-x: hidden;
    overflow-y: auto;
    -webkit-user-select: none; user-select: none;
  }
  ::-webkit-scrollbar { width: 8px; height: 8px; }
  ::-webkit-scrollbar-track { background: transparent; }
  ::-webkit-scrollbar-thumb { background: $scroll; border-radius: 4px; }
  ::-webkit-scrollbar-thumb:hover { background: $scroll_hover; }

  /* 标题 */
  .page-title {
    position: fixed; left: 60px; top: 63px;
    font-size: 33px; font-weight: 600; color: $ink;
    z-index: 10; pointer-events: none;
  }

  /* 标题覆盖层 （其实我感觉这样还是不太好看。。） */
  .title-scrim {
    position: fixed; top: 0; left: 0; right: 0; height: 120px; z-index: 5;
    pointer-events: none; opacity: 0;
    transition: opacity .2s ease;
    background: linear-gradient(to bottom, $scrim 0%, $scrim 55%, transparent 100%);
    -webkit-backdrop-filter: blur(16px);
    backdrop-filter: blur(16px);
    -webkit-mask-image: linear-gradient(to bottom, #000 0 55%, transparent 100%);
    mask-image: linear-gradient(to bottom, #000 0 55%, transparent 100%);
  }
  .title-scrim.on { opacity: 1; }

  .page { padding: 120px 60px 60px; }

  /* 模式行 */
  .mode-row {
    display: flex; align-items: center;
    margin-top: 8px; margin-bottom: 8px; min-height: 36px;
  }
  .mode-group { display: flex; align-items: center; gap: 16px; }
  .mode-label { font-size: 18px; color: $label; }
  .radio {
    display: inline-flex; align-items: center; gap: 7px;
    font-size: 14px; color: $radio_text; cursor: pointer;
  }
  .radio .dot {
    width: 16px; height: 16px; border-radius: 50%;
    border: 2px solid $radio_border; background: transparent;
    transition: border-color .15s, background .15s;
  }
  .radio:hover .dot { border-color: #ff8fb3; }
  .radio.checked .dot { border-color: #ff6b9d; background: #ff6b9d; }

  .source-group { display: flex; align-items: center; gap: 8px; margin-left: 20px; }

  .spacer { flex: 1; }

  .btn {
    font-family: inherit; border-radius: 6px; cursor: pointer;
    display: inline-flex; align-items: center; justify-content: center; gap: 6px;
    font-size: 14px; height: 36px; padding: 0 16px; outline: none;
    transition: background .15s, border-color .15s;
  }
  .btn-secondary {
    color: $label; background: $btn_bg; border: 1px solid $btn_bd;
  }
  .btn-secondary:hover { background: $btn_bg_hover; border-color: $btn_bd_hover; }
  .btn-secondary:active { background: $btn_bg; }
  .btn-start {
    color: #ffffff; background: #00c853; border: none;
  }
  .btn-start:hover { background: #00e676; }
  .btn-start:active { background: #009624; }

  /* 下拉框 */
  .combo {
    position: relative; width: 200px; height: 32px;
    background: $ctrl_bg; border: 1px solid $ctrl_bd; border-radius: 6px;
    display: flex; align-items: center; cursor: pointer;
    padding: 0 8px; font-size: 14px; color: $label;
  }
  .combo:hover { background: $ctrl_bg_hover; border-color: $ctrl_bd_hover; }
  .combo .arrow {
    position: absolute; right: 8px; width: 12px; height: 12px;
    display: flex; align-items: center; justify-content: center;
    transition: transform .15s;
  }
  .combo.open .arrow { transform: rotate(180deg); }
  /* 下拉浮层（fixed 定位，避免被卡片遮挡/被滚动容器裁切） */
  .combo-list {
    position: fixed; z-index: 70; min-width: 200px;
    background: $pop_bg; border: 1px solid $pop_bd; border-radius: 8px;
    box-shadow: 0 8px 28px rgba(0, 0, 0, .18);
    padding: 4px 0; max-height: 280px; overflow: hidden auto;
    opacity: 0; transform: translateY(-6px) scaleY(.96); transform-origin: top center;
    pointer-events: none;
    transition: opacity .16s ease, transform .16s ease;
  }
  .combo-list.show { opacity: 1; transform: none; pointer-events: auto; }
  .combo-item {
    height: 34px; display: flex; align-items: center; padding: 0 12px;
    font-size: 14px; color: $label; cursor: pointer; white-space: nowrap;
    transition: background .12s;
  }
  .combo-item:hover { background: $ctrl_bg_hover; }
  .combo-item.active { color: $accent; font-weight: 600; }

  /* 分区 */
  .section-title {
    font-size: 20px; font-weight: 600; color: $ink;
    margin-top: 20px; margin-bottom: 10px;
  }
  .section-grid {
    display: grid; gap: 12px; margin-bottom: 16px;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  /* 软件卡片 */
  .card {
    height: 100px;
    background: $card_bg; border: 1px solid $card_bd; border-radius: 8px;
    display: flex; align-items: center;
    padding: 16px 20px; gap: 16px; cursor: default;
    transition: background .15s;
  }
  .card:hover { background: $card_bg_hover; }
  .card img.icon { width: 64px; height: 64px; flex: none; object-fit: contain; }
  .card .icon-empty { width: 64px; height: 64px; flex: none; }
  .card .info { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 6px; }
  .card .name-row { display: flex; align-items: center; gap: 6px; min-width: 0; }
  .card .name {
    font-size: 18px; font-weight: 600; color: $ink;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .card .desc {
    font-size: 14px; color: $sub; height: 40px; line-height: 20px;
    display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical;
    overflow: hidden; word-break: break-all;
  }
  .link-btn {
    width: 20px; height: 20px; flex: none; border: none; background: transparent;
    border-radius: 4px; cursor: pointer; color: $ink; opacity: .75;
    display: inline-flex; align-items: center; justify-content: center; padding: 0;
  }
  .link-btn:hover { background: $link_hover; opacity: 1; }

  .btn-download {
    color: #ffffff; background: $accent; border: none; height: 36px;
    flex: none; padding: 0 14px;
  }
  .btn-download:hover { filter: brightness(1.1); }
  .btn-download:active { filter: brightness(0.9); }

  .checkbox {
    width: 40px; height: 40px; flex: none; border-radius: 6px;
    display: flex; align-items: center; justify-content: center; cursor: pointer;
    transition: background .15s;
  }
  .checkbox:hover { background: $link_hover; }
  .checkbox .box {
    width: 20px; height: 20px; border-radius: 6px;
    border: 1.5px solid $radio_border; background: transparent;
    display: flex; align-items: center; justify-content: center;
    transition: background .15s, border-color .15s, transform .1s;
  }
  .checkbox .box svg { opacity: 0; transition: opacity .12s; }
  .checkbox:hover .box { border-color: $accent; }
  .checkbox.checked .box { background: $accent; border-color: $accent; }
  .checkbox.checked .box svg { opacity: 1; }
  .checkbox:active .box { transform: scale(.92); }

  /* 进度环 */
  .ring { width: 60px; height: 60px; flex: none; display: none; }
  .ring.show { display: block; }
  .ring svg { width: 100%; height: 100%; display: block; }
  .ring .pct { font-size: 13px; font-weight: 600; fill: #00c853; }

  /* 右上角提示 */
  .toast-host {
    position: fixed; top: 16px; right: 16px; z-index: 60;
    display: flex; flex-direction: column; gap: 8px; align-items: flex-end;
    pointer-events: none;
  }
  .toast {
    pointer-events: auto; max-width: 360px; min-width: 220px;
    background: $pop_bg; border: 1px solid $pop_bd; border-radius: 8px;
    box-shadow: 0 6px 20px rgba(0, 0, 0, .14);
    padding: 12px 14px; display: flex; gap: 10px; align-items: flex-start;
    opacity: 0; transform: translateX(20px);
    transition: opacity .2s ease, transform .2s ease;
    cursor: pointer;
  }
  .toast.show { opacity: 1; transform: translateX(0); }
  .toast .t-icon {
    width: 18px; height: 18px; flex: none; margin-top: 1px;
    border-radius: 50%; color: #ffffff;
    display: flex; align-items: center; justify-content: center;
  }
  .toast.success .t-icon { background: #00c853; }
  .toast.error .t-icon { background: #e81123; }
  .toast.warning .t-icon { background: #f7a501; }
  .toast .t-body { flex: 1; min-width: 0; }
  .toast .t-title { font-size: 14px; font-weight: 600; color: $ink; line-height: 20px; }
  .toast .t-content {
    font-size: 12px; color: $sub; line-height: 18px; margin-top: 2px;
    white-space: pre-wrap; word-break: break-word;
  }

  /* 确认框 */
  .modal-mask {
    position: fixed; inset: 0; background: rgba(0, 0, 0, .25); z-index: 80;
    display: flex; align-items: center; justify-content: center;
    opacity: 0; visibility: hidden;
    transition: opacity .18s ease, visibility .18s ease;
  }
  .modal-mask.show { opacity: 1; visibility: visible; }
  .modal {
    width: 420px; max-width: calc(100% - 80px);
    background: $pop_bg; border: 1px solid $pop_bd; border-radius: 8px;
    box-shadow: 0 16px 44px rgba(0, 0, 0, .24);
    padding: 20px 24px 16px;
    opacity: 0; transform: scale(.94);
    transition: opacity .18s ease, transform .2s cubic-bezier(.2, .9, .25, 1);
  }
  .modal-mask.show .modal { opacity: 1; transform: scale(1); }
  .modal .m-title { font-size: 18px; font-weight: 600; color: $ink; }
  .modal .m-content {
    font-size: 14px; color: $sub; line-height: 22px; margin-top: 12px;
    white-space: pre-wrap; word-break: break-word; max-height: 300px; overflow: auto;
  }
  .modal .m-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 20px; }
  .modal .btn-cancel { color: $label; background: $btn_bg; border: 1px solid $btn_bd; }
  .modal .btn-cancel:hover { background: $btn_bg_hover; border-color: $btn_bd_hover; }
  .modal .btn-ok { color: #ffffff; background: $accent; border: none; }
  .modal .btn-ok:hover { filter: brightness(1.1); }
  .modal .btn-ok:active { filter: brightness(.92); }

  [hidden] { display: none !important; }
</style>
</head>
<body>
  <h1 class="page-title" id="pageTitle"></h1>
  <div class="title-scrim"></div>
  <div class="page">
    <div class="mode-row">
      <div class="mode-group">
        <span class="mode-label" id="modeLabel"></span>
        <label class="radio checked" id="radioSingle"><span class="dot"></span><span id="radioSingleText"></span></label>
        <label class="radio" id="radioMulti"><span class="dot"></span><span id="radioMultiText"></span></label>
        <button class="btn btn-secondary" id="selectAllBtn" hidden></button>
      </div>
      <div class="source-group">
        <span class="mode-label" id="sourceLabel"></span>
        <div class="combo" id="combo">
          <span id="comboText"></span>
          <span class="arrow">
            <svg width="12" height="12" viewBox="0 0 12 12" fill="none">
              <path d="M2.5 4.5L6 8L9.5 4.5" stroke="$label" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
          </span>
          <div class="combo-list" id="comboList"></div>
        </div>
      </div>
      <span class="spacer"></span>
      <button class="btn btn-start" id="startBtn" hidden></button>
    </div>
    <div id="sections"></div>
  </div>

  <!-- 右上角通知容器 -->
  <div class="toast-host" id="toastHost"></div>

  <!-- 确认框 -->
  <div class="modal-mask" id="modalMask">
    <div class="modal" id="modalBox">
      <div class="m-title" id="modalTitle"></div>
      <div class="m-content" id="modalContent"></div>
      <div class="m-actions">
        <button class="btn btn-cancel" id="modalCancel"></button>
        <button class="btn btn-ok" id="modalOk"></button>
      </div>
    </div>
  </div>

<script src="qrc:///qtwebchannel/qwebchannel.js"></script>
<script>
  var DATA = $payload_json;
  var I18N = DATA.i18n;
  var STATE = DATA.state;
  var CARDS = {};   // name -> {el, btn, checkbox, ring, pctText, ringFg, circumference, downloading}

  /* WebChannel 桥 */
  var pybridge = null;
  if (typeof QWebChannel !== 'undefined' && typeof qt !== 'undefined') {
    new QWebChannel(qt.webChannelTransport, function(channel) {
      pybridge = channel.objects.bridge;
    });
  }
  function bridge(fn) {
    if (!pybridge || !pybridge[fn]) return;
    pybridge[fn].apply(pybridge, Array.prototype.slice.call(arguments, 1));
  }

  var SVG_LINK = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none"><path d="M9.25 7C9.66421 7 10 7.33579 10 7.75C10 8.12656 9.72249 8.4383 9.36083 8.49187L9.25 8.5H7C5.067 8.5 3.5 10.067 3.5 12C3.5 13.864 4.95707 15.3876 6.79435 15.4941L7 15.5H9.25C9.66421 15.5 10 15.8358 10 16.25C10 16.6266 9.72249 16.9383 9.36083 16.9919L9.25 17H7C4.23858 17 2 14.7614 2 12C2 9.32226 4.10496 7.13615 6.75045 7.00612L7 7H9.25ZM17 7C19.7614 7 22 9.23858 22 12C22 14.6777 19.895 16.8638 17.2495 16.9939L17 17H14.75C14.3358 17 14 16.6642 14 16.25C14 15.8734 14.2775 15.5617 14.6392 15.5081L14.75 15.5H17C18.933 15.5 20.5 13.933 20.5 12C20.5 10.136 19.0429 8.6124 17.2057 8.50594L17 8.5H14.75C14.3358 8.5 14 8.16421 14 7.75C14 7.37344 14.2775 7.0617 14.6392 7.00813L14.75 7H17Z" fill="currentColor"/><path d="M7 11.25H17C17.4142 11.25 17.75 11.5858 17.75 12C17.75 12.3797 17.4678 12.6935 17.1018 12.7432L17 12.75H7C6.58579 12.75 6.25 12.4142 6.25 12C6.25 11.6203 6.53215 11.3065 6.89823 11.2568L7 11.25H17Z" fill="currentColor"/></svg>';
  var SVG_DOWNLOAD = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none"><path d="M18.2498 20.5009C18.664 20.5008 19 20.8365 19 21.2507C19 21.6649 18.6644 22.0008 18.2502 22.0009L5.25022 22.0047C4.836 22.0048 4.5 21.6691 4.5 21.2549C4.5 20.8407 4.83557 20.5048 5.24978 20.5047L18.2498 20.5009ZM11.6482 2.01271L11.75 2.00586C12.1297 2.00586 12.4435 2.28801 12.4932 2.65409L12.5 2.75586L12.499 16.4409L16.2208 12.7205C16.4871 12.4543 16.9038 12.4301 17.1974 12.648L17.2815 12.7206C17.5477 12.9869 17.5719 13.4036 17.354 13.6972L17.2814 13.7813L12.2837 18.7779C12.0176 19.044 11.6012 19.0683 11.3076 18.8507L11.2235 18.7782L6.22003 13.7816C5.92694 13.4889 5.92661 13.014 6.21931 12.7209C6.48539 12.4545 6.90204 12.43 7.1958 12.6477L7.27997 12.7202L10.999 16.4339L11 2.75586C11 2.37616 11.2822 2.06237 11.6482 2.01271L11.75 2.00586L11.6482 2.01271Z" fill="currentColor"/></svg>';
  var SVG_PLAY = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none"><path d="M7.60846 4.61489C7.1087 4.34296 6.5 4.70472 6.5 5.27368V18.726C6.5 19.2949 7.1087 19.6567 7.60846 19.3848L19.97 12.6586C20.4921 12.3746 20.4921 11.6251 19.97 11.341L7.60846 4.61489ZM5 5.27368C5 3.56682 6.82609 2.48151 8.32538 3.2973L20.687 10.0235C22.2531 10.8756 22.2531 13.124 20.687 13.9762L8.32538 20.7024C6.82609 21.5181 5 20.4328 5 18.726V5.27368Z" fill="currentColor"/></svg>';
  var SVG_CHECK = '<svg width="14" height="14" viewBox="0 0 16 16" fill="none"><path d="M3 8.4L6.2 11.6L13 4.6" stroke="#ffffff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  var SVG_TICK_S = '<svg width="11" height="11" viewBox="0 0 16 16" fill="none"><path d="M3 8.4L6.2 11.6L13 4.6" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  var SVG_CROSS_S = '<svg width="9" height="9" viewBox="0 0 16 16" fill="none"><path d="M4 4L12 12M12 4L4 12" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/></svg>';
  var SVG_BANG_S = '<svg width="11" height="11" viewBox="0 0 16 16" fill="none"><path d="M8 3.4V9" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/><circle cx="8" cy="12.4" r="1.2" fill="currentColor"/></svg>';
  var SVG_RING = '<svg viewBox="0 0 60 60">' +
    '<circle cx="30" cy="30" r="26" fill="none" stroke="$ring_track" stroke-width="4"/>' +
    '<circle class="ring-fg" cx="30" cy="30" r="26" fill="none" stroke="#00c853" stroke-width="4" stroke-linecap="round" transform="rotate(-90 30 30)"/>' +
    '<text class="pct" x="30" y="30" text-anchor="middle" dominant-baseline="central">0%</text></svg>';

  /* 文本 */
  document.getElementById('pageTitle').textContent = DATA.title;
  document.getElementById('modeLabel').textContent = I18N.select_mode + ':';
  document.getElementById('radioSingleText').textContent = I18N.single_mode;
  document.getElementById('radioMultiText').textContent = I18N.multi_mode;
  document.getElementById('sourceLabel').textContent = I18N.download_source + ':';
  document.getElementById('startBtn').innerHTML = SVG_PLAY + '<span>' + I18N.start_download + '</span>';

  /* 模式 */
  var mode = STATE.mode || 'single';
  var radioSingle = document.getElementById('radioSingle');
  var radioMulti = document.getElementById('radioMulti');
  var selectAllBtn = document.getElementById('selectAllBtn');
  var startBtn = document.getElementById('startBtn');

  function applyModeUI() {
    radioSingle.classList.toggle('checked', mode === 'single');
    radioMulti.classList.toggle('checked', mode === 'multi');
    selectAllBtn.hidden = mode !== 'multi';
    startBtn.hidden = mode !== 'multi';
    for (var name in CARDS) {
      var c = CARDS[name];
      if (c.downloading) continue;
      c.btn.hidden = mode !== 'single';
      c.checkbox.hidden = mode !== 'multi';
    }
    updateSelectAllLabel();
  }
  radioSingle.addEventListener('click', function() {
    if (mode === 'single') return;
    mode = 'single';
    applyModeUI();
    bridge('setMode', 'single');
  });
  radioMulti.addEventListener('click', function() {
    if (mode === 'multi') return;
    mode = 'multi';
    applyModeUI();
    bridge('setMode', 'multi');
  });

  /* 全选 */
  function availableCards() {
    var arr = [];
    for (var name in CARDS) { if (!CARDS[name].downloading) arr.push(CARDS[name]); }
    return arr;
  }
  function updateSelectAllLabel() {
    var avail = availableCards();
    var allChecked = avail.length > 0 && avail.every(function(c) { return c.checked; });
    selectAllBtn.textContent = allChecked ? I18N.deselect_all : I18N.select_all;
  }
  selectAllBtn.addEventListener('click', function() {
    var avail = availableCards();
    var allChecked = avail.length > 0 && avail.every(function(c) { return c.checked; });
    var target = !allChecked;
    avail.forEach(function(c) { setChecked(c, target); });
    updateSelectAllLabel();
  });

  /* 批量开始下载 */
  startBtn.addEventListener('click', function() {
    var selected = [];
    for (var name in CARDS) {
      if (!CARDS[name].downloading && CARDS[name].checked) selected.push(name);
    }
    bridge('startBatch', JSON.stringify(selected));
  });

  /* 下载源下拉框 */
  var combo = document.getElementById('combo');
  var comboText = document.getElementById('comboText');
  var comboList = document.getElementById('comboList');
  var currentSource = DATA.sourceKey;

  function buildCombo() {
    comboList.innerHTML = '';
    DATA.sources.forEach(function(s) {
      var item = document.createElement('div');
      item.className = 'combo-item' + (s.key === currentSource ? ' active' : '');
      item.textContent = s.label;
      item.addEventListener('click', function(e) {
        e.stopPropagation();
        currentSource = s.key;
        comboText.textContent = s.label;
        closeCombo();
        buildCombo();
        bridge('setSource', s.key);
      });
      comboList.appendChild(item);
    });
    var active = DATA.sources.filter(function(s) { return s.key === currentSource; })[0];
    if (active) comboText.textContent = active.label;
  }

  function openCombo() {
    var r = combo.getBoundingClientRect();
    comboList.style.left = r.left + 'px';
    comboList.style.top = (r.bottom + 4) + 'px';
    comboList.style.minWidth = r.width + 'px';
    void comboList.offsetHeight;
    comboList.classList.add('show');
    combo.classList.add('open');
  }

  function closeCombo() {
    comboList.classList.remove('show');
    combo.classList.remove('open');
  }

  buildCombo();
  combo.addEventListener('click', function(e) {
    e.stopPropagation();
    if (comboList.classList.contains('show')) closeCombo();
    else openCombo();
  });
  document.addEventListener('click', function() { closeCombo(); });
  document.addEventListener('keydown', function(e) { if (e.key === 'Escape') closeCombo(); });
  window.addEventListener('scroll', closeCombo, true);
  window.addEventListener('resize', closeCombo);

  /* 渲染分区与卡片 */
  var sectionsEl = document.getElementById('sections');

  function makeRing() {
    var wrap = document.createElement('div');
    wrap.className = 'ring';
    wrap.innerHTML = SVG_RING;
    var fg = wrap.querySelector('.ring-fg');
    var pct = wrap.querySelector('.pct');
    var C = 2 * Math.PI * 26;
    fg.style.strokeDasharray = C;
    fg.style.strokeDashoffset = C;
    return { el: wrap, fg: fg, pct: pct, C: C };
  }

  function setRing(r, percent) {
    r.fg.style.strokeDashoffset = r.C * (1 - Math.max(0, Math.min(100, percent)) / 100);
    r.pct.textContent = Math.round(percent) + '%';
  }

  function buildCard(item, grid) {
    var card = document.createElement('div');
    card.className = 'card';

    if (item.icon) {
      var img = document.createElement('img');
      img.className = 'icon';
      img.src = item.icon;
      img.draggable = false;
      card.appendChild(img);
    } else {
      var empty = document.createElement('div');
      empty.className = 'icon-empty';
      card.appendChild(empty);
    }

    var info = document.createElement('div');
    info.className = 'info';
    var nameRow = document.createElement('div');
    nameRow.className = 'name-row';
    var nameEl = document.createElement('span');
    nameEl.className = 'name';
    nameEl.textContent = item.name;
    nameEl.title = item.name;
    nameRow.appendChild(nameEl);
    if (item.link) {
      var linkBtn = document.createElement('button');
      linkBtn.className = 'link-btn';
      linkBtn.innerHTML = SVG_LINK;
      linkBtn.title = I18N.open_official_website;
      linkBtn.addEventListener('click', function(e) {
        e.stopPropagation();
        bridge('openLink', item.link);
      });
      nameRow.appendChild(linkBtn);
    }
    info.appendChild(nameRow);
    var descEl = document.createElement('div');
    descEl.className = 'desc';
    descEl.textContent = item.desc;
    descEl.title = item.desc;
    info.appendChild(descEl);
    card.appendChild(info);

    var ring = makeRing();
    card.appendChild(ring.el);

    var btn = document.createElement('button');
    btn.className = 'btn btn-download';
    btn.innerHTML = SVG_DOWNLOAD + '<span>' + I18N.download_btn + '</span>';
    btn.addEventListener('click', function() {
      if (CARDS[item.name] && CARDS[item.name].downloading) return;
      bridge('download', item.name);
    });
    card.appendChild(btn);

    var checkbox = document.createElement('div');
    checkbox.className = 'checkbox';
    checkbox.innerHTML = '<span class="box">' + SVG_CHECK + '</span>';
    checkbox.hidden = true;
    checkbox.addEventListener('click', function(e) {
      e.stopPropagation();
      var c = CARDS[item.name];
      if (!c || c.downloading) return;          // 下载中的卡片不能勾选
      setChecked(c, !c.checked);
      updateSelectAllLabel();
    });
    card.appendChild(checkbox);

    grid.appendChild(card);

    var isDownloading = Object.prototype.hasOwnProperty.call(STATE.downloading, item.name);
    CARDS[item.name] = {
      btn: btn, checkbox: checkbox, ring: ring,
      checked: false, downloading: isDownloading
    };
    if (isDownloading) {
      btn.hidden = true;
      checkbox.hidden = true;
      ring.el.classList.add('show');
      setRing(ring, STATE.downloading[item.name] || 0);
    }
  }

  /* 勾选状态来源：CARDS[name].checked */
  function setChecked(c, value) {
    c.checked = !!value;
    c.checkbox.classList.toggle('checked', c.checked);
  }

  DATA.sections.forEach(function(sec) {
    var title = document.createElement('div');
    title.className = 'section-title';
    title.textContent = sec.title;
    sectionsEl.appendChild(title);
    var grid = document.createElement('div');
    grid.className = 'section-grid';
    sectionsEl.appendChild(grid);
    sec.items.forEach(function(it) { buildCard(it, grid); });
  });

  applyModeUI();

  /* 列数 */
  var CARD_MIN_W = 380, CARD_MAX_W = 520, GRID_GAP = 12;

  function calcCols(avail) {
    if (avail <= CARD_MIN_W + GRID_GAP) return 1;
    var cols = Math.max(1, Math.floor(avail / (CARD_MAX_W + GRID_GAP)));
    while (true) {
      var cardW = (avail - (cols - 1) * GRID_GAP) / cols;
      if (cardW > CARD_MAX_W) cols++;
      else break;
    }
    while (cols > 1 && (avail - (cols - 1) * GRID_GAP) / cols < CARD_MIN_W) cols--;
    return cols;
  }

  function relayout() {
    var avail = Math.max(0, document.documentElement.clientWidth - 120);
    var cols = calcCols(avail);
    var grids = document.querySelectorAll('.section-grid');
    for (var i = 0; i < grids.length; i++) {
      grids[i].style.gridTemplateColumns = 'repeat(' + cols + ', minmax(0, 1fr))';
    }
  }

  var resizeTimer = null;
  window.addEventListener('resize', function() {
    if (resizeTimer) clearTimeout(resizeTimer);
    resizeTimer = setTimeout(relayout, 150);
  });
  relayout();
  window.relayout = relayout;   // Python 尺寸变化调用

  /* 标题覆盖层 */
  var scrimEl = document.querySelector('.title-scrim');
  function updateScrim() {
    var y = window.pageYOffset || document.documentElement.scrollTop || document.body.scrollTop || 0;
    if (scrimEl) scrimEl.classList.toggle('on', y > 4);
  }
  window.addEventListener('scroll', updateScrim);
  window.updateScrim = updateScrim;
  updateScrim();

  /* 右上角提示 */
  var toastHost = document.getElementById('toastHost');

  function showToast(kind, title, content, duration) {
    var el = document.createElement('div');
    el.className = 'toast ' + (kind || 'success');
    var glyph = (kind === 'error') ? SVG_CROSS_S : (kind === 'warning') ? SVG_BANG_S : SVG_TICK_S;
    el.innerHTML = '<span class="t-icon">' + glyph + '</span>' +
                   '<div class="t-body"><div class="t-title"></div><div class="t-content"></div></div>';
    el.querySelector('.t-title').textContent = title || '';
    el.querySelector('.t-content').textContent = content || '';
    toastHost.appendChild(el);
    requestAnimationFrame(function() { el.classList.add('show'); });

    var timer = setTimeout(remove, duration && duration > 0 ? duration : 3000);
    function remove() {
      clearTimeout(timer);
      el.classList.remove('show');
      setTimeout(function() { if (el.parentNode) el.parentNode.removeChild(el); }, 220);
    }
    el.addEventListener('click', remove);
  }

  /* 确认框 */
  var modalMask = document.getElementById('modalMask');
  var modalTitle = document.getElementById('modalTitle');
  var modalContent = document.getElementById('modalContent');
  var modalOk = document.getElementById('modalOk');
  var modalCancel = document.getElementById('modalCancel');
  var confirmId = null;

  modalOk.textContent = I18N.confirm;
  modalCancel.textContent = I18N.cancel;

  function closeConfirm(ok) {
    modalMask.classList.remove('show');
    if (confirmId === null) return;
    var id = confirmId;
    confirmId = null;
    bridge('confirmResult', id, ok);
  }

  function showConfirm(id, title, content) {
    modalTitle.textContent = title || '';
    modalContent.textContent = content || '';
    confirmId = id;
    modalMask.classList.add('show');
    modalOk.focus();
  }

  modalOk.addEventListener('click', function() { closeConfirm(true); });
  modalCancel.addEventListener('click', function() { closeConfirm(false); });
  modalMask.addEventListener('click', function(e) { if (e.target === modalMask) closeConfirm(false); });
  document.addEventListener('keydown', function(e) {
    if (confirmId === null) return;
    if (e.key === 'Escape') closeConfirm(false);
    else if (e.key === 'Enter') closeConfirm(true);
  });

  /* Python > JS  */
  window.glimpseon = {
    toast: showToast,
    confirm: showConfirm,
    uiStart: function(name, percent) {
      var c = CARDS[name];
      if (!c) return;
      c.downloading = true;
      c.btn.hidden = true;
      c.checkbox.hidden = true;
      setChecked(c, false);
      c.ring.el.classList.add('show');
      setRing(c.ring, percent || 0);
      updateSelectAllLabel();
    },
    uiProgress: function(name, percent) {
      var c = CARDS[name];
      if (!c) return;
      setRing(c.ring, percent);
    },
    uiError: function(name) {
      var c = CARDS[name];
      if (!c) return;
      c.downloading = false;
      c.ring.el.classList.remove('show');
      setRing(c.ring, 0);
      c.btn.hidden = mode !== 'single';
      c.checkbox.hidden = mode !== 'multi';
      updateSelectAllLabel();
    },
    uiBatchDone: function() {
      for (var name in CARDS) {
        var c = CARDS[name];
        if (!c.downloading) setChecked(c, false);
      }
      updateSelectAllLabel();
    }
  };

  /* 外部调用 */
  window.setMode = function(m) {
    mode = (m === 'multi') ? 'multi' : 'single';
    applyModeUI();
  };
</script>
</body>
</html>
''')
