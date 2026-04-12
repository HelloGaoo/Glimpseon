
import json
import os

from PyQt5.QtCore import Qt
from PyQt5.QtGui import QIcon, QPixmap
from PyQt5.QtWidgets import QVBoxLayout, QDialog, QStackedWidget, QWidget, QLabel
from qfluentwidgets import (
    BodyLabel,
    MessageBox,
    PrimaryPushButton,
)

from core.constants import BASE_DIR, get_resPath


class WizardWindow(QDialog):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("ClassLively 向导")
        self.setFixedSize(750, 550)
        self.setWindowFlags(Qt.Window | Qt.WindowCloseButtonHint)

        icon_path = get_resPath(os.path.join("resource", "icons", "CY.png"))
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))

        self.mainLayout = QVBoxLayout(self)
        self.mainLayout.setContentsMargins(40, 40, 40, 40)
        self.mainLayout.setSpacing(20)

        self.stackedWidget = QStackedWidget(self)
        self.mainLayout.addWidget(self.stackedWidget)

        self.page1 = QWidget()
        self.page1Layout = QVBoxLayout(self.page1)
        self.page1Layout.setAlignment(Qt.AlignCenter)
        self.page1Layout.setSpacing(20)

        self.iconLabel = QLabel(self.page1)
        self.iconLabel.setFixedSize(128, 128)
        self.iconLabel.setAlignment(Qt.AlignCenter)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            self.iconLabel.setPixmap(pixmap.scaled(128, 128, Qt.KeepAspectRatio, Qt.SmoothTransformation))

        self.welcomeLabel = BodyLabel("ClassLively", self.page1)
        self.welcomeLabel.setStyleSheet("font-size: 48px; font-weight: bold;")
        self.welcomeLabel.setAlignment(Qt.AlignCenter)

        self.nextButton = PrimaryPushButton("下一步", self.page1)
        self.nextButton.setFixedSize(160, 40)

        self.page1Layout.addWidget(self.iconLabel, 0, Qt.AlignCenter)
        self.page1Layout.addWidget(self.welcomeLabel, 0, Qt.AlignCenter)
        self.page1Layout.addWidget(self.nextButton, 0, Qt.AlignCenter)

        self.stackedWidget.addWidget(self.page1)

        self.nextButton.clicked.connect(self._onNextClicked)

    def closeEvent(self, event):
        msg_box = MessageBox(
            title="提示",
            content="向导未完成，确定要退出吗？",
            parent=self
        )
        if msg_box.exec_():
            event.accept()
        else:
            event.ignore()

    def _onNextClicked(self):
        pass


def check_wizard_needed():
    wizard_path = os.path.join(BASE_DIR, "config", "Setup_Wizard.json")
    if not os.path.exists(wizard_path):
        return True
    try:
        with open(wizard_path, "r", encoding="utf-8") as f:
            data = json.load(f)
            return data.get("completed", 0) != 1
    except Exception:
        return True


def create_wizard_file():
    wizard_path = os.path.join(BASE_DIR, "config", "Setup_Wizard.json")
    with open(wizard_path, "w", encoding="utf-8") as f:
        json.dump({"completed": 0}, f)


def complete_wizard():
    wizard_path = os.path.join(BASE_DIR, "config", "Setup_Wizard.json")
    with open(wizard_path, "w", encoding="utf-8") as f:
        json.dump({"completed": 1}, f)

