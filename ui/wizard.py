
import json
import os

from PyQt5.QtCore import Qt
from PyQt5.QtGui import QIcon, QPixmap, QColor
from PyQt5.QtWidgets import QVBoxLayout, QDialog, QStackedWidget, QWidget, QLabel
from qfluentwidgets import (
    MessageBox,
    PrimaryPushButton,
    setTheme,
    isDarkTheme,
    Theme,
)

from core.config import cfg
from core.constants import BASE_DIR, get_resPath


class WizardWindow(QDialog):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("ClassLively 向导")
        self.setFixedSize(750, 550)
        self.setWindowFlags(Qt.Window | Qt.WindowCloseButtonHint)

        setTheme(cfg.themeMode.value)

        icon_path = get_resPath(os.path.join("resource", "icons", "CY.png"))
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))

        self.mainLayout = QVBoxLayout(self)
        self.mainLayout.setContentsMargins(40, 120, 40, 40)
        self.mainLayout.setSpacing(20)
        self.mainLayout.setAlignment(Qt.AlignTop | Qt.AlignCenter)
        
        self.__setQss()

        self.iconLabel = QLabel(self)
        self.iconLabel.setFixedSize(128, 128)
        self.iconLabel.setAlignment(Qt.AlignCenter)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            self.iconLabel.setPixmap(pixmap.scaled(128, 128, Qt.KeepAspectRatio, Qt.SmoothTransformation))

        text_color = "white" if isDarkTheme() else "black"
        self.welcomeLabel = QLabel("ClassLively", self)
        self.welcomeLabel.setStyleSheet(f"font-size: 48px; font-weight: bold; color: {text_color}; font-family: 'HarmonyOS Sans SC', 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;")
        self.welcomeLabel.setAlignment(Qt.AlignCenter)

        self.nextButton = PrimaryPushButton("下一步", self)
        self.nextButton.setFixedSize(160, 40)

        self.mainLayout.addWidget(self.iconLabel, 0, Qt.AlignCenter)
        self.mainLayout.addWidget(self.welcomeLabel, 0, Qt.AlignCenter)
        self.mainLayout.addWidget(self.nextButton, 0, Qt.AlignCenter)

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
    
    def __setQss(self):
        theme = 'dark' if isDarkTheme() else 'light'
        try:
            qss_path = get_resPath(os.path.join('resource', 'qss', theme, 'wizard_interface.qss'))
            with open(qss_path, encoding='utf-8') as f:
                self.setStyleSheet(f.read())
        except Exception:
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

