
import json
import os
import sys

import win32com.client
from pathlib import Path
from PyQt6.QtCore import Qt, QPropertyAnimation, QEasingCurve, QLocale
from PyQt6.QtGui import QIcon, QPixmap, QColor, QFont
from PyQt6.QtWidgets import QVBoxLayout, QDialog, QStackedWidget, QWidget, QHBoxLayout, QLabel, QGraphicsOpacityEffect, QApplication, QPushButton, QLineEdit
from qfluentwidgets import (
    BodyLabel,
    CheckBox,
    ComboBoxSettingCard,
    CustomColorSettingCard,
    FluentIcon as FIF,
    FluentTranslator,
    InfoBar,
    MessageBox,
    PrimaryPushButton,
    PushButton,
    setTheme,
    setThemeColor,
    StrongBodyLabel,
    Theme,
    SwitchSettingCard,
    LineEdit,
    ToolButton,
)

from core.config import cfg
from core.constants import BASE_DIR, get_resPath, load_qss
from ui.city_selector import RegionSelectorDialog
from ui.common import show_text_file
from ui.edit_panel import CountdownEditDialog


class WizardWindow(QDialog):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("ClassLively 向导")
        self.setFixedSize(840, 650)
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint)

        locale = QLocale(QLocale.Language.Chinese, QLocale.Country.China)
        self.translator = FluentTranslator(locale)
        QApplication.instance().installTranslator(self.translator)

        setTheme(cfg.themeMode.value)

        icon_path = get_resPath(os.path.join("resource", "icons", "CY.png"))
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))

        self.mainLayout = QVBoxLayout(self)
        self.mainLayout.setContentsMargins(30, 30, 30, 30)
        self.mainLayout.setSpacing(16)

        self.stackedWidget = QStackedWidget(self)
        self.mainLayout.addWidget(self.stackedWidget)
        
        self.titleLabel = QLabel("ClassLively", self)
        self.titleLabel.setObjectName("titleLabel")
        self.titleLabel.move(30, 10)
        
        self.closeButton = QPushButton(self)
        self.closeButton.setObjectName("closeButton")
        self.closeButton.setFixedSize(30, 30)
        self.closeButton.move(self.width() - 35, 5)
        self.closeButton.setText("×")
        self.closeButton.clicked.connect(self.close)
        
        self.__setQss()

        # 第 1 页：欢迎页面
        self.page1 = QWidget()
        self.page1Layout = QVBoxLayout(self.page1)
        self.page1Layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.page1Layout.setSpacing(16)

        self.iconLabel = QLabel(self.page1)
        self.iconLabel.setFixedSize(112, 112)
        self.iconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            self.iconLabel.setPixmap(pixmap.scaled(112, 112, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation))

        self.welcomeLabel = StrongBodyLabel("ClassLively", self.page1)
        self.welcomeLabel.setAlignment(Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft)
        self.welcomeLabel.setTextFormat(Qt.TextFormat.RichText)
        self.welcomeLabel.setText('<span style="font-family:\'HarmonyOS Sans SC\',\'HarmonyOS Sans\',\'Microsoft YaHei UI\',\'Microsoft YaHei\',\'SimHei\',sans-serif; font-weight:900; font-size:34px;">ClassLively</span>')
        self.welcomeLabel.setObjectName("welcomeLabel")

        self.nextButton = PrimaryPushButton(FIF.RIGHT_ARROW, "继续", self.page1)
        self.nextButton.setFixedHeight(36)

        self.headerLayout = QHBoxLayout()
        self.headerLayout.setSpacing(12)
        self.headerLayout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.headerLayout.addWidget(self.iconLabel)
        self.headerLayout.addWidget(self.welcomeLabel)

        self.page1Layout.addLayout(self.headerLayout)
        self.page1Layout.addWidget(self.nextButton, 0, Qt.AlignmentFlag.AlignCenter)

        # 第 2 页：软件使用协议
        self.page2 = QWidget()
        self.page2Layout = QVBoxLayout(self.page2)
        self.page2Layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignCenter)
        self.page2Layout.setSpacing(16)
        self.page2Layout.addSpacing(50)

        self.agreementTitle = StrongBodyLabel("软件使用协议", self.page2)
        self.agreementTitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title_font = self.agreementTitle.font()
        title_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        title_font.setPointSize(30)
        title_font.setBold(True)
        self.agreementTitle.setFont(title_font)

        self.agreementText = BodyLabel("在使用本软件前，请阅读并同意以下协议：", self.page2)
        self.agreementText.setAlignment(Qt.AlignmentFlag.AlignCenter)
        txt_font = self.agreementText.font()
        txt_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        txt_font.setPointSize(14)
        self.agreementText.setFont(txt_font)

        self.page2Layout.addWidget(self.agreementTitle, 0, Qt.AlignmentFlag.AlignCenter)
        self.page2Layout.addWidget(self.agreementText, 0, Qt.AlignmentFlag.AlignCenter)
        self.page2Layout.addSpacing(16)

        def _make_check_with_link(box_text, link_text, target_path):
            container = QWidget(self.page2)
            container.setFixedHeight(56)
            container.setFixedWidth(430)
            
            h = QHBoxLayout(container)
            h.setSpacing(8)
            h.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
            h.setContentsMargins(0, 8, 0, 8)
            
            chk = CheckBox("", self.page2)
            chk_font = chk.font()
            chk_font.setPointSize(16)
            chk.setFont(chk_font)
            chk.setFixedWidth(24)
            
            lbl = BodyLabel("", self.page2)
            lbl.setTextFormat(Qt.TextFormat.RichText)
            if target_path and os.path.exists(target_path):
                uri = Path(target_path).as_uri()
            else:
                uri = ""
            theme_color = cfg.themeColor.value.name()
            link_style = f'color:{theme_color}; text-decoration:underline;'
            lbl.setText(f'<span style="font-family:\'HarmonyOS Sans SC\',\'HarmonyOS Sans\',\'Microsoft YaHei UI\',\'Microsoft YaHei\',\'SimHei\',sans-serif; font-size:16px;">我已阅读并同意&nbsp;<a href="{uri}" style="{link_style}">{link_text}</a></span>')
            lbl.setOpenExternalLinks(False)
            
            def _on_link_activated(url):
                if target_path and os.path.exists(target_path):
                    try:
                        show_text_file(link_text, f"{link_text}", target_path, parent=self.window())
                        return
                    except Exception:
                        try:
                            os.startfile(target_path)
                            return
                        except Exception:
                            pass
                
                msg = MessageBox(title="提示", content=f"无法打开协议文件：{link_text}", parent=self)
                msg.exec()
            
            lbl.linkActivated.connect(_on_link_activated)
            
            def _on_container_clicked():
                chk.setChecked(not chk.isChecked())
            
            container.mousePressEvent = lambda e: _on_container_clicked()
            
            h.addWidget(lbl, 0, Qt.AlignmentFlag.AlignLeft)
            h.addStretch(1)
            h.addWidget(chk, 0, Qt.AlignmentFlag.AlignRight)
            
            return chk, container

        license_path = os.path.join(BASE_DIR, "LICENSE")
        readme_path = os.path.join(BASE_DIR, "README.md")

        self.openSourceCheckBox, open_source_widget = _make_check_with_link(
            "", "项目开源协议 (GPL-3.0)", license_path)
        self.userAgreementCheckBox, user_agree_widget = _make_check_with_link(
            "", "用户协议", readme_path)
        self.privacyCheckBox, privacy_widget = _make_check_with_link(
            "", "隐私政策", "")

        self.agreeButton = PrimaryPushButton(FIF.ACCEPT, "完成", self.page2)
        self.agreeButton.setFixedHeight(36)
        self.agreeButton.setEnabled(False)

        checks_container = QWidget(self.page2)
        checks_container.setMaximumWidth(700)
        checks_layout = QVBoxLayout(checks_container)
        checks_layout.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop)
        checks_layout.setContentsMargins(0, 6, 0, 6)
        checks_layout.setSpacing(12)
        checks_layout.addWidget(open_source_widget)
        checks_layout.addWidget(user_agree_widget)
        checks_layout.addWidget(privacy_widget)
        self.page2Layout.addWidget(checks_container, 0, Qt.AlignmentFlag.AlignCenter)
        self.page2Layout.addSpacing(20)
        self.page2Layout.addWidget(self.agreeButton, 0, Qt.AlignmentFlag.AlignCenter)

        self.stackedWidget.addWidget(self.page1)
        self.stackedWidget.addWidget(self.page2)

        # 第 3 页：基本设置
        self.page3 = QWidget()
        self.page3Layout = QVBoxLayout(self.page3)
        self.page3Layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignCenter)
        self.page3Layout.setSpacing(16)
        self.page3Layout.addSpacing(50)

        self.settingsTitle = StrongBodyLabel("基本设置", self.page3)
        self.settingsTitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        settings_title_font = self.settingsTitle.font()
        settings_title_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        settings_title_font.setPointSize(30)
        settings_title_font.setBold(True)
        self.settingsTitle.setFont(settings_title_font)

        self.settingsText = BodyLabel("请选择您需要的功能选项：", self.page3)
        self.settingsText.setAlignment(Qt.AlignmentFlag.AlignCenter)
        settings_txt_font = self.settingsText.font()
        settings_txt_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        settings_txt_font.setPointSize(14)
        self.settingsText.setFont(settings_txt_font)

        self.page3Layout.addWidget(self.settingsTitle, 0, Qt.AlignmentFlag.AlignCenter)
        self.page3Layout.addWidget(self.settingsText, 0, Qt.AlignmentFlag.AlignCenter)
        self.page3Layout.addSpacing(16)

        settings_container = QWidget(self.page3)
        settings_container.setMaximumWidth(700)
        settings_layout = QVBoxLayout(settings_container)
        settings_layout.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop)
        settings_layout.setContentsMargins(0, 0, 0, 0)
        settings_layout.setSpacing(12)

        self.autoStartSwitch = self._createSwitchCard(
            FIF.PLAY,
            "开机自启动",
            "设置应用在系统启动时自动运行",
            cfg.autoStart.value
        )
        settings_layout.addWidget(self.autoStartSwitch)

        self.autoOpenOnIdleSwitch = self._createSwitchCard(
            FIF.VIEW,
            "空闲时自动打开",
            "电脑空闲时自动从最小化打开界面",
            cfg.autoOpenOnIdle.value
        )
        settings_layout.addWidget(self.autoOpenOnIdleSwitch)

        self.autoOpenMaximizeSwitch = self._createSwitchCard(
            FIF.FULL_SCREEN,
            "自动打开时最大化",
            "空闲自动打开界面时是否最大化窗口",
            cfg.autoOpenMaximize.value
        )
        settings_layout.addWidget(self.autoOpenMaximizeSwitch)
        self.desktopShortcutSwitch = self._createSwitchCard(
            FIF.LINK,
            "创建桌面快捷方式",
            "在桌面创建应用程序快捷方式",
            False
        )
        settings_layout.addWidget(self.desktopShortcutSwitch)

        self.page3Layout.addWidget(settings_container, 0, Qt.AlignmentFlag.AlignCenter)
        self.page3Layout.addSpacing(20)

        self.finishButton = PrimaryPushButton(FIF.ACCEPT, "完成", self.page3)
        self.finishButton.setFixedHeight(36)
        self.page3Layout.addWidget(self.finishButton, 0, Qt.AlignmentFlag.AlignCenter)

        self.stackedWidget.addWidget(self.page3)

        # 第 4 页：外观设置
        self.page4 = QWidget()
        self.page4Layout = QVBoxLayout(self.page4)
        self.page4Layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignCenter)
        self.page4Layout.setSpacing(16)
        self.page4Layout.addSpacing(50)

        self.appearanceTitle = StrongBodyLabel("外观设置", self.page4)
        self.appearanceTitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        appearance_title_font = self.appearanceTitle.font()
        appearance_title_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        appearance_title_font.setPointSize(30)
        appearance_title_font.setBold(True)
        self.appearanceTitle.setFont(appearance_title_font)

        self.appearanceText = BodyLabel("选择适合您的主题和颜色：", self.page4)
        self.appearanceText.setAlignment(Qt.AlignmentFlag.AlignCenter)
        appearance_txt_font = self.appearanceText.font()
        appearance_txt_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        appearance_txt_font.setPointSize(14)
        self.appearanceText.setFont(appearance_txt_font)

        self.page4Layout.addWidget(self.appearanceTitle, 0, Qt.AlignmentFlag.AlignCenter)
        self.page4Layout.addWidget(self.appearanceText, 0, Qt.AlignmentFlag.AlignCenter)
        self.page4Layout.addSpacing(16)

        appearance_container = QWidget(self.page4)
        appearance_container.setMaximumWidth(700)
        appearance_layout = QVBoxLayout(appearance_container)
        appearance_layout.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop)
        appearance_layout.setContentsMargins(0, 0, 0, 0)
        appearance_layout.setSpacing(12)

        self.themeCard = ComboBoxSettingCard(
            cfg.themeMode,
            FIF.BRUSH,
            "应用颜色主题",
            "更改应用程序的颜色外观",
            texts=["浅色", "深色", "使用系统设置"],
            parent=self.page4
        )
        self.themeCard.setFixedWidth(600)
        appearance_layout.addWidget(self.themeCard)

        self.themeColorCard = CustomColorSettingCard(
            cfg.themeColor,
            FIF.PALETTE,
            "主要颜色",
            "更改应用程序的主要颜色",
            parent=self.page4
        )
        self.themeColorCard.setFixedWidth(600)
        appearance_layout.addWidget(self.themeColorCard)

        self.page4Layout.addWidget(appearance_container, 0, Qt.AlignmentFlag.AlignCenter)
        self.page4Layout.addSpacing(20)

        self.finishButton2 = PrimaryPushButton(FIF.ACCEPT, "完成", self.page4)
        self.finishButton2.setFixedHeight(36)
        self.page4Layout.addWidget(self.finishButton2, 0, Qt.AlignmentFlag.AlignCenter)

        self.stackedWidget.addWidget(self.page4)

        # 第 5 页：学校信息设置
        self.page5 = QWidget()
        self.page5Layout = QVBoxLayout(self.page5)
        self.page5Layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignCenter)
        self.page5Layout.setSpacing(16)
        self.page5Layout.addSpacing(50)

        self.schoolInfoTitle = StrongBodyLabel("学校信息设置", self.page5)
        self.schoolInfoTitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        school_info_title_font = self.schoolInfoTitle.font()
        school_info_title_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        school_info_title_font.setPointSize(30)
        school_info_title_font.setBold(True)
        self.schoolInfoTitle.setFont(school_info_title_font)

        self.schoolInfoText = BodyLabel("请输入您的学校和班级信息，以及选择天气城市：", self.page5)
        self.schoolInfoText.setAlignment(Qt.AlignmentFlag.AlignCenter)
        school_info_txt_font = self.schoolInfoText.font()
        school_info_txt_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        school_info_txt_font.setPointSize(14)
        self.schoolInfoText.setFont(school_info_txt_font)

        self.page5Layout.addWidget(self.schoolInfoTitle, 0, Qt.AlignmentFlag.AlignCenter)
        self.page5Layout.addWidget(self.schoolInfoText, 0, Qt.AlignmentFlag.AlignCenter)
        self.page5Layout.addSpacing(16)

        school_info_container = QWidget(self.page5)
        school_info_container.setMaximumWidth(700)
        school_info_layout = QVBoxLayout(school_info_container)
        school_info_layout.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop)
        school_info_layout.setContentsMargins(0, 0, 0, 0)
        school_info_layout.setSpacing(12)

        # 天气城市选择
        city_row = QWidget(self.page5)
        city_row.setFixedHeight(56)
        city_row_layout = QHBoxLayout(city_row)
        city_row_layout.setContentsMargins(16, 8, 16, 8)
        city_row_layout.setSpacing(12)
        
        city_label = BodyLabel("天气城市", self.page5)
        city_label_font = city_label.font()
        city_label_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        city_label.setFont(city_label_font)
        city_label.setFixedWidth(120)
        city_row_layout.addWidget(city_label)
        
        self.cityDisplayButton = PushButton(cfg.city.value, self.page5)
        self.cityDisplayButton.setFixedWidth(400)
        self.cityDisplayButton.clicked.connect(self._onCityButtonClicked)
        city_row_layout.addWidget(self.cityDisplayButton)
        city_row_layout.addStretch()
        
        school_info_layout.addWidget(city_row)

        # 学校输入
        school_row = QWidget(self.page5)
        school_row.setFixedHeight(56)
        school_row_layout = QHBoxLayout(school_row)
        school_row_layout.setContentsMargins(16, 8, 16, 8)
        school_row_layout.setSpacing(12)
        
        school_label = BodyLabel("学校名称", self.page5)
        school_label_font = school_label.font()
        school_label_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        school_label.setFont(school_label_font)
        school_label.setFixedWidth(120)
        school_row_layout.addWidget(school_label)
        
        self.schoolLineEdit = LineEdit(self.page5)
        self.schoolLineEdit.setFixedWidth(400)
        self.schoolLineEdit.setPlaceholderText("请输入学校名称")
        self.schoolLineEdit.setText(cfg.school.value)
        school_row_layout.addWidget(self.schoolLineEdit)
        school_row_layout.addStretch()
        
        school_info_layout.addWidget(school_row)

        # 班级输入
        class_row = QWidget(self.page5)
        class_row.setFixedHeight(56)
        class_row_layout = QHBoxLayout(class_row)
        class_row_layout.setContentsMargins(16, 8, 16, 8)
        class_row_layout.setSpacing(12)
        
        class_label = BodyLabel("班级", self.page5)
        class_label_font = class_label.font()
        class_label_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        class_label.setFont(class_label_font)
        class_label.setFixedWidth(120)
        class_row_layout.addWidget(class_label)
        
        self.classLineEdit = LineEdit(self.page5)
        self.classLineEdit.setFixedWidth(400)
        self.classLineEdit.setPlaceholderText("请输入班级（如：1班、高二3班）")
        self.classLineEdit.setText(cfg.schoolClass.value)
        class_row_layout.addWidget(self.classLineEdit)
        class_row_layout.addStretch()
        
        school_info_layout.addWidget(class_row)

        # 倒计时配置
        countdown_row = QWidget(self.page5)
        countdown_row.setFixedHeight(56)
        countdown_row_layout = QHBoxLayout(countdown_row)
        countdown_row_layout.setContentsMargins(16, 8, 16, 8)
        countdown_row_layout.setSpacing(12)
        
        countdown_label = BodyLabel("倒计时配置", self.page5)
        countdown_label_font = countdown_label.font()
        countdown_label_font.setFamily('HarmonyOS Sans SC, HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif')
        countdown_label.setFont(countdown_label_font)
        countdown_label.setFixedWidth(120)
        countdown_row_layout.addWidget(countdown_label)
        
        self.countdownConfigButton = ToolButton(FIF.ADD, self.page5)
        self.countdownConfigButton.setFixedSize(36, 36)
        self.countdownConfigButton.setToolTip("添加倒计时")
        self.countdownConfigButton.clicked.connect(self._onCountdownConfigClicked)
        countdown_row_layout.addWidget(self.countdownConfigButton)
        countdown_row_layout.addStretch()
        
        school_info_layout.addWidget(countdown_row)

        self.page5Layout.addWidget(school_info_container, 0, Qt.AlignmentFlag.AlignCenter)
        self.page5Layout.addSpacing(20)

        self.finishButton3 = PrimaryPushButton(FIF.ACCEPT, "完成", self.page5)
        self.finishButton3.setFixedHeight(36)
        self.page5Layout.addWidget(self.finishButton3, 0, Qt.AlignmentFlag.AlignCenter)

        self.stackedWidget.addWidget(self.page5)

        self.nextButton.clicked.connect(self._onNextClicked)
        self.agreeButton.clicked.connect(self._onAgreeClicked)
        self.openSourceCheckBox.stateChanged.connect(self._onCheckBoxChanged)
        self.userAgreementCheckBox.stateChanged.connect(self._onCheckBoxChanged)
        self.privacyCheckBox.stateChanged.connect(self._onCheckBoxChanged)
        self.finishButton.clicked.connect(self._onFinishClicked)
        self.finishButton2.clicked.connect(self._onFinishClicked2)
        self.finishButton3.clicked.connect(self._onFinishClicked3)
        self.themeCard.comboBox.currentIndexChanged.connect(self._onThemeChanged)
        self.themeColorCard.colorChanged.connect(self._onColorChanged)

    def resizeEvent(self, event):
        self.titleLabel.move(30, 10)
        self.closeButton.move(self.width() - 35, 5)
        super().resizeEvent(event)

    def closeEvent(self, event):
        msg_box = MessageBox(
            title="提示",
            content="向导未完成，确定要退出吗？",
            parent=self
        )
        if msg_box.exec():
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
        anim.setEasingCurve(QEasingCurve.Type.InOutQuad)

        def _after_fade_out():
            self.stackedWidget.setCurrentIndex(index)
            anim2 = QPropertyAnimation(effect, b"opacity", self)
            anim2.setDuration(max(50, duration // 2))
            anim2.setStartValue(0.0)
            anim2.setEndValue(1.0)
            anim2.setEasingCurve(QEasingCurve.Type.InOutQuad)
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
        self._setCurrentIndexAnimated(2)

    def _onFinishClicked(self):
        cfg.autoStart.value = self.autoStartSwitch.isChecked()
        cfg.autoOpenOnIdle.value = self.autoOpenOnIdleSwitch.isChecked()
        cfg.autoOpenMaximize.value = self.autoOpenMaximizeSwitch.isChecked()
        
        if self.desktopShortcutSwitch.isChecked():
            self._createDesktopShortcut()
        
        self._setCurrentIndexAnimated(3)

    def _onFinishClicked2(self):
        self._setCurrentIndexAnimated(4)

    def _onFinishClicked3(self):
        """保存学校信息设置并完成向导"""
        cfg.city.value = self.cityDisplayButton.text()
        cfg.school.value = self.schoolLineEdit.text().strip()
        cfg.schoolClass.value = self.classLineEdit.text().strip()
        complete_wizard()
        self.accept()

    def _onCityButtonClicked(self):
        """打开城市选择对话框"""
        dialog = RegionSelectorDialog(self)
        if dialog.exec():
            selected_city = dialog.get_selected_region()
            if selected_city:
                self.cityDisplayButton.setText(selected_city)

    def _onCountdownConfigClicked(self):
        """打开倒计时添加对话框"""
        try:
            dialog = CountdownEditDialog(self)
            if dialog.exec():
                countdown_data = dialog.get_countdown()
                if countdown_data:
                    countdown_list = cfg.countdownList.value or []
                    countdown_list.append(countdown_data)
                    cfg.countdownList.value = countdown_list
                    InfoBar.success(
                        title="成功",
                        content=f"已添加倒计时：{countdown_data.get('title', '')}",
                        parent=self,
                        duration=3000
                    )
        except Exception as e:
            InfoBar.warning(
                title="提示",
                content=f"添加倒计时失败：{str(e)}",
                parent=self,
                duration=5000
            )

    def _createSwitchCard(self, icon, title, content, default_value):
        """开关设置卡片"""
        card = SwitchSettingCard(icon, title, content, None, self.page3)
        card.setChecked(default_value)
        card.setFixedWidth(600)
        return card

    def _createDesktopShortcut(self):
        """创建桌面快捷方式"""
        try:
            
            desktop_path = os.path.join(os.path.expanduser("~"), "Desktop")
            shortcut_path = os.path.join(desktop_path, "ClassLively.lnk")
            
            if getattr(sys, 'frozen', False):
                exe_path = sys.executable
            else:
                exe_path = sys.executable
                pass
            
            shell = win32com.client.Dispatch('WScript.Shell')
            shortcut = shell.CreateShortCut(shortcut_path)
            shortcut.Targetpath = exe_path
            if not getattr(sys, 'frozen', False):
                shortcut.Arguments = os.path.abspath(__file__)
            shortcut.WorkingDirectory = BASE_DIR
            shortcut.IconLocation = exe_path
            shortcut.save()
            
            InfoBar.success(
                title="成功",
                content="已创建桌面快捷方式",
                parent=self,
                duration=3000
            )
        except Exception as e:
            InfoBar.warning(
                title="提示",
                content=f"创建快捷方式失败：{str(e)}",
                parent=self,
                duration=5000
            )
    
    def _onThemeChanged(self, index):
        """主题变更"""
        theme_mode = cfg.themeMode.value
        setTheme(theme_mode.value)
        self.__setQss()
        def update_widget_style(widget):
            widget.style().unpolish(widget)
            widget.style().polish(widget)
            widget.update()
            for child in widget.children():
                if hasattr(child, 'style'):
                    update_widget_style(child)
        
        update_widget_style(self)
    
    def _onColorChanged(self, color):
        """颜色变更"""
        theme_color = cfg.themeColor.value
        setThemeColor(theme_color)
    
    def __setQss(self):
        self.setStyleSheet(load_qss('wizard_interface.qss'))


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
