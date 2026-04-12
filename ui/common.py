# Common UI helpers for dialogs
import os
from PyQt5.QtGui import QFont
from qfluentwidgets import MessageBox, TextEdit, isDarkTheme


def show_text_file(title: str, intro: str, file_path: str, parent=None):
    """Show a text file in a MessageBox with a read-only TextEdit.

    If file_path does not exist, intro will be shown as fallback content.
    """
    theme = 'dark' if isDarkTheme() else 'light'

    content_text = ""
    if file_path and os.path.exists(file_path):
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content_text = f.read()
        except Exception:
            content_text = f"无法读取文件：{file_path}"
    else:
        content_text = intro

    main_window = parent

    msg_box = MessageBox(
        title=title,
        content=intro,
        parent=main_window
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
    msg_box.exec_()
