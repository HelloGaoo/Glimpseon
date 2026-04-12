
import json
import os

from PyQt5.QtCore import Qt
from PyQt5.QtGui import QIcon, QPixmap, QColor, QFont
from PyQt5.QtWidgets import QVBoxLayout, QDialog, QStackedWidget, QWidget, QHBoxLayout, QLabel
from qfluentwidgets import (
    BodyLabel,
    CheckBox,
    FluentIcon as FIF,
    HyperlinkLabel,
    MessageBox,
    PrimaryPushButton,
    setTheme,
    isDarkTheme,
    StrongBodyLabel,
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
        self.mainLayout.setContentsMargins(30, 30, 30, 30)
        self.mainLayout.setSpacing(16)

        self.stackedWidget = QStackedWidget(self)
        self.mainLayout.addWidget(self.stackedWidget)
        
        self.__setQss()

        self.page1 = QWidget()
        self.page1Layout = QVBoxLayout(self.page1)
        self.page1Layout.setAlignment(Qt.AlignCenter)
        self.page1Layout.setSpacing(16)

        self.iconLabel = QLabel(self.page1)
        self.iconLabel.setFixedSize(112, 112)
        self.iconLabel.setAlignment(Qt.AlignCenter)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            self.iconLabel.setPixmap(pixmap.scaled(112, 112, Qt.KeepAspectRatio, Qt.SmoothTransformation))

        self.welcomeLabel = StrongBodyLabel("ClassLively", self.page1)
        self.welcomeLabel.setAlignment(Qt.AlignVCenter | Qt.AlignLeft)
        # Use rich text to force a heavier-looking font and larger size
        self.welcomeLabel.setTextFormat(Qt.RichText)
        self.welcomeLabel.setText('<span style="font-family:\'Microsoft YaHei UI\',\'Microsoft YaHei\',\'SimHei\',sans-serif; font-weight:900; font-size:34px;">ClassLively</span>')
        self.welcomeLabel.setStyleSheet("margin:0; padding-left:8px;")

        self.nextButton = PrimaryPushButton(FIF.RIGHT_ARROW, "继续", self.page1)
        self.nextButton.setFixedHeight(36)

        self.headerLayout = QHBoxLayout()
        self.headerLayout.setSpacing(12)
        self.headerLayout.setAlignment(Qt.AlignCenter)
        self.headerLayout.addWidget(self.iconLabel)
        self.headerLayout.addWidget(self.welcomeLabel)

        self.page1Layout.addLayout(self.headerLayout)
        self.page1Layout.addWidget(self.nextButton, 0, Qt.AlignCenter)

        self.page2 = QWidget()
        self.page2Layout = QVBoxLayout(self.page2)
        self.page2Layout.setAlignment(Qt.AlignTop | Qt.AlignCenter)
        self.page2Layout.setSpacing(16)
        self.page2Layout.addSpacing(40)

        self.agreementTitle = StrongBodyLabel("软件使用协议", self.page2)
        self.agreementTitle.setAlignment(Qt.AlignCenter)

        self.agreementText = BodyLabel("在使用本软件前，请阅读并同意以下协议：", self.page2)
        self.agreementText.setAlignment(Qt.AlignCenter)

        self.openSourceCheckBox = CheckBox("项目开源协议 (GPL-3.0)", self.page2)
        
        self.userAgreementCheckBox = CheckBox("用户协议", self.page2)
        
        self.privacyCheckBox = CheckBox("隐私政策", self.page2)

        self.agreeButton = PrimaryPushButton(FIF.RIGHT_ARROW, "继续", self.page2)
        self.agreeButton.setFixedHeight(36)
        self.agreeButton.setEnabled(False)

        self.page2Layout.addWidget(self.agreementTitle, 0, Qt.AlignCenter)
        self.page2Layout.addWidget(self.agreementText, 0, Qt.AlignCenter)
        self.page2Layout.addSpacing(30)
        self.page2Layout.addWidget(self.openSourceCheckBox, 0, Qt.AlignCenter)
        self.page2Layout.addWidget(self.userAgreementCheckBox, 0, Qt.AlignCenter)
        self.page2Layout.addWidget(self.privacyCheckBox, 0, Qt.AlignCenter)
        self.page2Layout.addSpacing(30)
        self.page2Layout.addWidget(self.agreeButton, 0, Qt.AlignCenter)

        self.stackedWidget.addWidget(self.page1)
        self.stackedWidget.addWidget(self.page2)

        self.nextButton.clicked.connect(self._onNextClicked)
        self.agreeButton.clicked.connect(self._onAgreeClicked)
        self.openSourceCheckBox.stateChanged.connect(self._onCheckBoxChanged)
        self.userAgreementCheckBox.stateChanged.connect(self._onCheckBoxChanged)
        self.privacyCheckBox.stateChanged.connect(self._onCheckBoxChanged)

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
        self.stackedWidget.setCurrentIndex(1)
    
    def _onCheckBoxChanged(self, state):
        all_checked = (self.openSourceCheckBox.isChecked() and 
                      self.userAgreementCheckBox.isChecked() and 
                      self.privacyCheckBox.isChecked())
        self.agreeButton.setEnabled(all_checked)
    
    def _onAgreeClicked(self):
        complete_wizard()
        self.accept()
    
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

