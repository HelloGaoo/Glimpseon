
import json
import os

from PyQt5.QtCore import Qt, QPropertyAnimation, QEasingCurve
from PyQt5.QtGui import QIcon, QPixmap, QColor, QFont
from PyQt5.QtWidgets import QVBoxLayout, QDialog, QStackedWidget, QWidget, QHBoxLayout, QLabel, QGraphicsOpacityEffect
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
        self.setFixedSize(840, 650)
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
        self.page2Layout.addSpacing(100)

        self.agreementTitle = StrongBodyLabel("软件使用协议", self.page2)
        self.agreementTitle.setAlignment(Qt.AlignCenter)
        title_font = self.agreementTitle.font()
        title_font.setPointSize(30)
        title_font.setBold(True)
        self.agreementTitle.setFont(title_font)

        self.agreementText = BodyLabel("在使用本软件前，请阅读并同意以下协议：", self.page2)
        self.agreementText.setAlignment(Qt.AlignCenter)
        txt_font = self.agreementText.font()
        txt_font.setPointSize(24)
        self.agreementText.setFont(txt_font)
        def _make_check_with_link(box_text, link_text, target_path):
            container = QWidget(self.page2)
            container.setFixedHeight(56)
            container.setFixedWidth(430)
            
            h = QHBoxLayout(container)
            h.setSpacing(8)
            h.setAlignment(Qt.AlignLeft | Qt.AlignVCenter)
            h.setContentsMargins(0, 8, 0, 8)
            
            chk = CheckBox("", self.page2)
            chk_font = chk.font()
            chk_font.setPointSize(16)
            chk.setFont(chk_font)
            chk.setFixedWidth(24)
            
            lbl = BodyLabel("", self.page2)
            lbl.setTextFormat(Qt.RichText)
            if target_path and os.path.exists(target_path):
                uri = Path(target_path).as_uri()
            else:
                uri = ""
            link_style = 'color:#4fc08d; text-decoration:underline;'
            lbl.setText(f'<span style="font-size:16px;">我已阅读并同意&nbsp;<a href="{uri}" style="{link_style}">{link_text}</a></span>')
            lbl.setOpenExternalLinks(False)
            
            def _on_link_activated(url):
                if target_path and os.path.exists(target_path):
                    try:
                        from .common import show_text_file
                        show_text_file(link_text, f"{link_text}", target_path, parent=self.window())
                        return
                    except Exception:
                        try:
                            os.startfile(target_path)
                            return
                        except Exception:
                            pass
                
                msg = MessageBox(title="提示", content=f"无法打开协议文件：{link_text}", parent=self)
                msg.exec_()
            
            lbl.linkActivated.connect(_on_link_activated)
            
            def _on_container_clicked():
                chk.setChecked(not chk.isChecked())
            
            container.mousePressEvent = lambda e: _on_container_clicked()
            
            h.addWidget(lbl, 0, Qt.AlignLeft)
            h.addStretch(1)
            h.addWidget(chk, 0, Qt.AlignRight)
            
            return chk, container

        from pathlib import Path
        license_path = os.path.join(BASE_DIR, "LICENSE")
        readme_path = os.path.join(BASE_DIR, "README.md")

        self.openSourceCheckBox, open_source_widget = _make_check_with_link(
            "", "项目开源协议 (GPL-3.0)", license_path)
        self.userAgreementCheckBox, user_agree_widget = _make_check_with_link(
            "", "用户协议", readme_path)
        self.privacyCheckBox, privacy_widget = _make_check_with_link(
            "", "隐私政策", "")

        self.agreeButton = PrimaryPushButton(FIF.RIGHT_ARROW, "继续", self.page2)
        self.agreeButton.setFixedHeight(36)
        self.agreeButton.setEnabled(False)

        self.page2Layout.addWidget(self.agreementTitle, 0, Qt.AlignCenter)
        self.page2Layout.addWidget(self.agreementText, 0, Qt.AlignCenter)
        self.page2Layout.addSpacing(18)
        checks_container = QWidget(self.page2)
        checks_container.setMaximumWidth(560)
        checks_layout = QVBoxLayout(checks_container)
        checks_layout.setAlignment(Qt.AlignHCenter | Qt.AlignTop)
        checks_layout.setContentsMargins(0, 6, 0, 6)
        checks_layout.setSpacing(14)
        checks_layout.addWidget(open_source_widget)
        checks_layout.addWidget(user_agree_widget)
        checks_layout.addWidget(privacy_widget)
        self.page2Layout.addWidget(checks_container, 0, Qt.AlignCenter)
        self.page2Layout.addSpacing(20)
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
        self._setCurrentIndexAnimated(1)

    def _setCurrentIndexAnimated(self, index, duration=300):
        effect = self.stackedWidget.graphicsEffect()
        if effect is None:
            effect = QGraphicsOpacityEffect(self.stackedWidget)
            effect.setOpacity(1.0)
            self.stackedWidget.setGraphicsEffect(effect)

        anim = QPropertyAnimation(effect, b"opacity", self)
        anim.setDuration(max(50, duration // 2))
        anim.setStartValue(1.0)
        anim.setEndValue(0.0)
        anim.setEasingCurve(QEasingCurve.InOutQuad)

        def _after_fade_out():
            self.stackedWidget.setCurrentIndex(index)
            anim2 = QPropertyAnimation(effect, b"opacity", self)
            anim2.setDuration(max(50, duration // 2))
            anim2.setStartValue(0.0)
            anim2.setEndValue(1.0)
            anim2.setEasingCurve(QEasingCurve.InOutQuad)
            self._current_animation = anim2
            anim2.start()

        anim.finished.connect(_after_fade_out)
        self._current_animation = anim
        anim.start()
    
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

