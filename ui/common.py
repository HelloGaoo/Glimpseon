# Common UI helpers for dialogs and base classes
import os
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QIcon, QPixmap, QFont
from PyQt6.QtWidgets import QHBoxLayout, QLabel, QVBoxLayout, QWidget
from qfluentwidgets import (
    CardWidget,
    ExpandLayout,
    FluentIcon as FIF,
    InfoBar,
    MessageBox,
    PrimaryPushButton,
    PushButton,
    ScrollArea,
    SmoothScrollArea,
    TextEdit,
    isDarkTheme,
)


class BaseScrollAreaInterface(ScrollArea):
    """ 基础滚动区域界面 """

    def __init__(self, title: str, parent=None, width=1000, height=800,
                 viewport_margins=(0, 120, 0, 20), title_position=(60, 63)):
        super().__init__(parent=parent)
        self.title = title
        self.scrollWidget = QWidget()
        self.titleLabel = QLabel(title, self)

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
    """Show a text file in a MessageBox with a read-only TextEdit.

    If file_path does not exist, intro will be shown as fallback content.
    """
    content_text = ""
    if file_path and os.path.exists(file_path):
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content_text = f.read()
        except Exception:
            content_text = tr("common.file_read_error").format(file_path=file_path)
    else:
        content_text = intro

    msg_box = MessageBox(
        title=title,
        content=intro,
        parent=parent
    )
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

    # Insert the text area into the message box
    try:
        msg_box.textLayout.addWidget(text_edit)
        msg_box.textLayout.insertSpacing(0, 10)
    except Exception:
        pass

    msg_box.setMinimumWidth(600)
    msg_box.exec()
