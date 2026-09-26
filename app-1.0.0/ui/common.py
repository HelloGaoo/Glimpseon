
import os
from PyQt6.QtCore import Qt, QUrl
from PyQt6.QtGui import QColor, QFont
from PyQt6.QtWidgets import QWidget
from qfluentwidgets import MessageBox, ScrollArea, TextEdit, SubtitleLabel
from core.utils import tr
HTML_BASE_URL = QUrl("file:///glimpseon/")


def create_html_view(parent=None, mouse_transparent: bool = True):
    """创建透明背景html
    Args:
        parent: 父控件
        mouse_transparent: 鼠标事件穿透，纯展示组件应为 True，避免拦截宿主的拖拽/点击
    """
    from PyQt6.QtWebEngineWidgets import QWebEngineView

    view = QWebEngineView(parent)
    view.page().setBackgroundColor(QColor(0, 0, 0, 0))
    if mouse_transparent:
        view.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, True)
    return view


class BaseScrollAreaInterface(ScrollArea):
    """设置类基类"""

    def __init__(self, title: str, parent=None, width=1000, height=800,
                 viewport_margins=(0, 120, 0, 20), title_position=(60, 63)):
        super().__init__(parent=parent)
        self.title = title
        self.scrollWidget = QWidget()
        self.titleLabel = SubtitleLabel(title, self)

        self.resize(width, height)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setViewportMargins(*viewport_margins)
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)

        self.viewport().setAutoFillBackground(False)
        self.scrollWidget.setAutoFillBackground(False)

        self.titleLabel.setObjectName('settingLabel')
        self.scrollWidget.setObjectName('scrollWidget')
        self.titleLabel.move(*title_position)


def show_text_file(title: str, intro: str, file_path: str, parent=None):
    """文件不存在展示 intro"""
    content_text = ""
    if file_path and os.path.exists(file_path):
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content_text = f.read()
        except Exception:
            content_text = tr("common.file_read_error").format(file_path=file_path)
    else:
        content_text = intro

    msg_box = MessageBox(title=title, content=intro, parent=parent)
    try:
        msg_box.cancelButton.hide()
    except Exception:
        pass

    text_edit = TextEdit()
    text_edit.setPlainText(content_text)
    text_edit.setReadOnly(True)
    text_edit.setMinimumHeight(360)
    text_edit.setMinimumWidth(520)
    text_edit.setFont(QFont('Consolas', 12))

    try:
        msg_box.textLayout.addWidget(text_edit)
        msg_box.textLayout.insertSpacing(0, 10)
    except Exception:
        pass

    msg_box.setMinimumWidth(600)
    msg_box.exec()
