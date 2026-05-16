# ClassLively
# Copyright (C) 2026 HelloGaoo
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

import logging
import os
import sys

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QDialog,
    QFrame, QSizePolicy, QApplication,
)
from qfluentwidgets import (
    StrongBodyLabel, BodyLabel, SwitchButton, SpinBox,
    ComboBox, PushButton, LineEdit, ListWidget,
    SmoothScrollArea, IconWidget, Theme, isDarkTheme,
    setFont, FluentIcon as FIF,
)

from core.config import cfg
from core.constants import load_qss

logger = logging.getLogger(__name__)


class ComponentSettingDialog(QDialog):
    COMPONENT_MAP = {}

    @classmethod
    def register(cls, component_id: str):
        def decorator(dialog_class):
            cls.COMPONENT_MAP[component_id] = dialog_class
            return dialog_class
        return decorator

    @classmethod
    def create(cls, component_id: str, parent=None):
        dialog_class = cls.COMPONENT_MAP.get(component_id)
        if dialog_class:
            return dialog_class(parent)
        return None

    def __init__(self, title: str, icon: FIF = FIF.SETTING, parent=None):
        super().__init__(parent)
        self._title = title
        self._icon = icon
        self.setMinimumWidth(380)
        self.setMaximumWidth(480)
        self.setWindowTitle(title)
        self.setWindowFlags(
            Qt.WindowType.Dialog
            | Qt.WindowType.WindowCloseButtonHint
            | Qt.WindowType.WindowStaysOnTopHint
        )
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self._initLayout()
        self._applyStyle()

    def _initLayout(self):
        root = QVBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        header = QWidget()
        header.setObjectName('dialogHeader')
        header_layout = QHBoxLayout(header)
        header_layout.setContentsMargins(20, 16, 20, 12)

        icon_label = IconWidget(self._icon, header)
        icon_label.setFixedSize(24, 24)
        header_layout.addWidget(icon_label)

        title_label = StrongBodyLabel(self._title, header)
        title_label.setObjectName('dialogTitle')
        header_layout.addWidget(title_label)
        header_layout.addStretch()
        root.addWidget(header)

        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setObjectName('dialogSeparator')
        root.addWidget(line)

        scroll = SmoothScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        scroll.setFrameShape(QFrame.Shape.NoFrame)
        scroll.setObjectName('dialogScroll')

        self._content = QWidget()
        self._content.setObjectName('dialogContent')
        self._layout = QVBoxLayout(self._content)
        self._layout.setContentsMargins(20, 16, 20, 16)
        self._layout.setSpacing(8)

        self._createSettings()

        self._layout.addStretch()
        scroll.setWidget(self._content)
        root.addWidget(scroll)

    def _createSettings(self):
        pass

    def _applyStyle(self):
        dark = isDarkTheme()
        bg = 'rgb(32, 32, 32)' if dark else 'rgb(255, 255, 255)'
        fg = '#FFFFFF' if dark else '#000000'
        sep = 'rgba(255,255,255,0.08)' if dark else 'rgba(0,0,0,0.08)'
        self.setStyleSheet(f"""
            ComponentSettingDialog, #dialogContent {{
                background-color: {bg};
                color: {fg};
            }}
            #dialogHeader {{
                background-color: {bg};
            }}
            #dialogTitle {{
                color: {fg};
                font-size: 18px;
            }}
            #dialogSeparator {{
                background-color: {sep};
                max-height: 1px;
                border: none;
            }}
            #dialogScroll {{
                background-color: {bg};
                border: none;
            }}
        """)

    def _addRow(self, label_text: str, widget: QWidget, layout=None):
        row = QHBoxLayout()
        label = BodyLabel(label_text)
        label.setFixedWidth(110)
        row.addWidget(label)
        widget.setFixedWidth(160)
        row.addWidget(widget)
        row.addStretch()
        target = layout or self._layout
        target.addLayout(row)
        return widget

    def _addSwitch(self, label_text: str, config_item, layout=None):
        switch = SwitchButton()
        switch.setChecked(config_item.value)
        switch.checkedChanged.connect(lambda v, ci=config_item: setattr(ci, 'value', v))
        return self._addRow(label_text, switch, layout), switch

    def _addSpinBox(self, label_text: str, config_item, min_val, max_val, layout=None):
        spin = SpinBox()
        spin.setRange(min_val, max_val)
        spin.setValue(config_item.value)
        spin.valueChanged.connect(lambda v, ci=config_item: setattr(ci, 'value', v))
        return self._addRow(label_text, spin, layout), spin

    def _addComboBox(self, label_text: str, items: list, config_item, layout=None):
        combo = ComboBox()
        combo.addItems(items)
        combo.setCurrentText(config_item.value)
        combo.currentTextChanged.connect(lambda t, ci=config_item: setattr(ci, 'value', t))
        return self._addRow(label_text, combo, layout), combo

    def _addLineEdit(self, label_text: str, config_item, placeholder='', layout=None):
        edit = LineEdit()
        edit.setText(config_item.value)
        edit.setPlaceholderText(placeholder)
        edit.textChanged.connect(lambda t, ci=config_item: setattr(ci, 'value', t))
        return self._addRow(label_text, edit, layout), edit

    def _addSectionTitle(self, text: str, layout=None):
        label = StrongBodyLabel(text)
        label.setObjectName('sectionTitle')
        target = layout or self._layout
        target.addWidget(label)

    def _addSeparator(self, layout=None):
        sep = QFrame()
        sep.setFrameShape(QFrame.Shape.HLine)
        sep.setObjectName('dialogSeparator')
        target = layout or self._layout
        target.addWidget(sep)

    def _addColorCombo(self, label_text: str, config_item, default='main', layout=None):
        combo = ComboBox()
        combo.addItems(['主要颜色', '白色', '黑色', '红色'])
        combo.setCurrentText(self._getColorText(config_item.value, default))
        combo.currentTextChanged.connect(lambda t, ci=config_item: self._applyColorText(t, ci))
        return self._addRow(label_text, combo, layout), combo

    def _getColorText(self, color, default='main'):
        if hasattr(color, 'name'):
            color = color.name().upper()
        mapping = {
            'primary': '主要颜色', '#FFFFFF': '白色', 'white': '白色',
            '#000000': '黑色', 'black': '黑色',
            '#FF0000': '红色', 'red': '红色',
        }
        return mapping.get(color, mapping.get(default, '主要颜色'))

    def _applyColorText(self, text, config_item):
        mapping = {
            '主要颜色': 'primary', '白色': '#FFFFFF',
            '黑色': '#000000', '红色': '#FF0000',
        }
        config_item.value = mapping.get(text, 'primary')


@ComponentSettingDialog.register('clock')
class ClockSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('时钟设置', FIF.DATE_TIME, parent)

    def _createSettings(self):
        self._addSectionTitle('功能开关')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用时钟', cfg.showClock)
        (self._secondsRow, self._secondsSwitch) = self._addSwitch('显示秒针', cfg.showClockSeconds)
        (self._lunarRow, self._lunarSwitch) = self._addSwitch('显示农历', cfg.showLunarCalendar)

        self._addSeparator()
        self._addSectionTitle('文字样式')
        (self._colorRow, self._colorCombo) = self._addColorCombo('时钟颜色', cfg.clockColor)
        (self._clockSizeRow, self._clockSizeSpin) = self._addSpinBox('时钟大小', cfg.clockSize, 80, 200)
        (self._dateSizeRow, self._dateSizeSpin) = self._addSpinBox('日期大小', cfg.dateSize, 12, 50)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showClock.value)

    def _updateEnabled(self, enabled):
        self._secondsSwitch.setEnabled(enabled)
        self._lunarSwitch.setEnabled(enabled)
        self._colorCombo.setEnabled(enabled)
        self._clockSizeSpin.setEnabled(enabled)
        self._dateSizeSpin.setEnabled(enabled)


@ComponentSettingDialog.register('weather')
class WeatherSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('天气设置', FIF.CLOUD, parent)

    def _createSettings(self):
        self._addSectionTitle('功能开关')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用天气', cfg.showWeather)

        self._addSeparator()
        self._addSectionTitle('数据源')
        self._cityButton = self._addRow('城市', PushButton(cfg.city.value))
        self._cityButton.clicked.connect(self._onCityClicked)

        self._addSeparator()
        self._addSectionTitle('外观')
        (self._sizeRow, self._sizeSpin) = self._addSpinBox('文字大小', cfg.weatherSize, 5, 50)
        (self._iconSizeRow, self._iconSizeSpin) = self._addSpinBox('图标大小', cfg.weatherIconSize, 32, 128)

        self._addSeparator()
        self._addSectionTitle('更新')
        (self._intervalRow, self._intervalCombo) = self._addComboBox(
            '更新间隔',
            ['从不', '5 分钟', '15 分钟', '30 分钟', '1 小时', '3 小时', '6 小时', '12 小时', '24 小时'],
            cfg.weatherUpdateInterval,
        )

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showWeather.value)

    def _onCityClicked(self):
        try:
            from services.weather import RegionSelectorDialog
            main_window = self.parent()
            while main_window and not hasattr(main_window, 'homeInterface'):
                main_window = main_window.parent()
            dialog = RegionSelectorDialog(main_window)
            if dialog.exec():
                selected_region = dialog.get_selected_region()
                if selected_region:
                    cfg.city.value = selected_region
                    self._cityButton.setText(selected_region)
                    if hasattr(main_window, '_MainWindow__updateWeather'):
                        main_window._MainWindow__updateWeather()
        except Exception:
            logger.exception('打开城市选择器失败')

    def _updateEnabled(self, enabled):
        self._cityButton.setEnabled(enabled)
        self._sizeSpin.setEnabled(enabled)
        self._iconSizeSpin.setEnabled(enabled)
        self._intervalCombo.setEnabled(enabled)


@ComponentSettingDialog.register('poetry')
class PoetrySettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('一言设置', FIF.BOOK_SHELF, parent)

    def _createSettings(self):
        self._addSectionTitle('功能开关')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用一言', cfg.showPoetry)

        self._addSeparator()
        self._addSectionTitle('数据源')
        self._apiCombo = self._addRow('API', ComboBox())
        self._apiCombo.addItems(['一言 API', '诗词 API'])
        if cfg.poetryApiUrl.value == 'https://www.ffapi.cn/int/v1/shici':
            self._apiCombo.setCurrentText('诗词 API')
        else:
            self._apiCombo.setCurrentText('一言 API')
        self._apiCombo.currentTextChanged.connect(self._onApiChanged)

        self._addSeparator()
        self._addSectionTitle('外观')
        (self._sizeRow, self._sizeSpin) = self._addSpinBox('文字大小', cfg.poetrySize, 12, 50)

        self._addSeparator()
        self._addSectionTitle('更新')
        (self._intervalRow, self._intervalCombo) = self._addComboBox(
            '更新间隔',
            ['从不', '5 分钟', '10 分钟', '30 分钟', '1 小时', '3 小时', '6 小时', '12 小时', '1 天'],
            cfg.poetryUpdateInterval,
        )

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showPoetry.value)

    def _onApiChanged(self, text):
        if text == '诗词 API':
            cfg.poetryApiUrl.value = 'https://www.ffapi.cn/int/v1/shici'
        else:
            cfg.poetryApiUrl.value = 'https://api.imlcd.cn/yy/api.php'

    def _updateEnabled(self, enabled):
        self._apiCombo.setEnabled(enabled)
        self._sizeSpin.setEnabled(enabled)
        self._intervalCombo.setEnabled(enabled)


@ComponentSettingDialog.register('countdown')
class CountdownSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('倒计时设置', FIF.STOP_WATCH, parent)

    def _createSettings(self):
        self._addSectionTitle('功能开关')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用倒计时', cfg.showCountdown)

        self._addSeparator()
        self._addSectionTitle('文字样式')
        (self._textColorRow, self._textColorCombo) = self._addColorCombo('文字颜色', cfg.countdownTextColor, 'red')
        (self._connectorColorRow, self._connectorColorCombo) = self._addColorCombo('连接词颜色', cfg.countdownConnectorColor, 'white')
        (self._textSizeRow, self._textSizeSpin) = self._addSpinBox('文字大小', cfg.countdownTextSize, 12, 120)
        (self._connectorSizeRow, self._connectorSizeSpin) = self._addSpinBox('连接词大小', cfg.countdownConnectorSize, 12, 60)

        self._addSeparator()
        self._addSectionTitle('显示模式')
        mode_combo = ComboBox()
        mode_combo.addItems(['同时显示', '轮播显示'])
        mode_combo.setCurrentText('同时显示' if cfg.countdownDisplayMode.value == 'simultaneous' else '轮播显示')
        mode_combo.currentTextChanged.connect(
            lambda t: setattr(cfg.countdownDisplayMode, 'value', 'simultaneous' if t == '同时显示' else 'carousel')
        )
        self._addRow('显示模式', mode_combo)
        (self._carouselRow, self._carouselSpin) = self._addSpinBox('轮播间隔(秒)', cfg.countdownCarouselInterval, 1, 60)

        self._addSeparator()
        self._addSectionTitle('倒计时列表')
        self._listWidget = ListWidget()
        self._listWidget.setFixedHeight(140)
        self._layout.addWidget(self._listWidget)

        btn_row = QHBoxLayout()
        add_btn = PushButton('添加')
        add_btn.clicked.connect(self._onAdd)
        edit_btn = PushButton('编辑')
        edit_btn.clicked.connect(self._onEdit)
        del_btn = PushButton('删除')
        del_btn.clicked.connect(self._onDelete)
        btn_row.addWidget(add_btn)
        btn_row.addWidget(edit_btn)
        btn_row.addWidget(del_btn)
        btn_row.addStretch()
        self._layout.addLayout(btn_row)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showCountdown.value)

    def _updateEnabled(self, enabled):
        self._textColorCombo.setEnabled(enabled)
        self._connectorColorCombo.setEnabled(enabled)
        self._textSizeSpin.setEnabled(enabled)
        self._connectorSizeSpin.setEnabled(enabled)
        self._carouselSpin.setEnabled(enabled)
        self._listWidget.setEnabled(enabled)

    def _onAdd(self):
        from PyQt6.QtWidgets import QInputDialog
        title, ok1 = QInputDialog.getText(self, '添加倒计时', '名称：')
        if not ok1 or not title:
            return
        target, ok2 = QInputDialog.getText(self, '添加倒计时', '目标时间 (YYYY-MM-DD HH:MM)：')
        if not ok2 or not target:
            return
        try:
            import datetime
            datetime.datetime.strptime(target, '%Y-%m-%d %H:%M')
        except ValueError:
            return
        items = cfg.countdownList.value or []
        items.append({'title': title, 'target_time': target})
        cfg.countdownList.value = items

    def _onEdit(self):
        row = self._listWidget.currentRow()
        items = cfg.countdownList.value or []
        if row < 0 or row >= len(items):
            return
        from PyQt6.QtWidgets import QInputDialog
        item = items[row]
        title, ok1 = QInputDialog.getText(self, '编辑倒计时', '名称：', text=item.get('title', ''))
        if not ok1:
            return
        target, ok2 = QInputDialog.getText(self, '编辑倒计时', '目标时间 (YYYY-MM-DD HH:MM)：', text=item.get('target_time', ''))
        if not ok2:
            return
        items[row] = {'title': title, 'target_time': target}
        cfg.countdownList.value = items

    def _onDelete(self):
        row = self._listWidget.currentRow()
        items = cfg.countdownList.value or []
        if row < 0 or row >= len(items):
            return
        items.pop(row)
        cfg.countdownList.value = items


@ComponentSettingDialog.register('school_info')
class SchoolInfoSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('学校信息设置', FIF.EDUCATION, parent)

    def _createSettings(self):
        self._addSectionTitle('功能开关')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用学校信息', cfg.showSchoolInfo)

        self._addSeparator()
        self._addSectionTitle('内容')
        (self._classRow, self._classEdit) = self._addLineEdit('班级', cfg.schoolClass, '例如：高三 (1) 班')
        (self._schoolRow, self._schoolEdit) = self._addLineEdit('学校', cfg.school, '例如：XX 中学')

        self._addSeparator()
        self._addSectionTitle('文字样式')
        (self._colorRow, self._colorCombo) = self._addColorCombo('文字颜色', cfg.schoolInfoTextColor, 'white')
        (self._sizeRow, self._sizeSpin) = self._addSpinBox('文字大小', cfg.schoolInfoTextSize, 12, 60)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showSchoolInfo.value)

    def _updateEnabled(self, enabled):
        self._classEdit.setEnabled(enabled)
        self._schoolEdit.setEnabled(enabled)
        self._colorCombo.setEnabled(enabled)
        self._sizeSpin.setEnabled(enabled)


@ComponentSettingDialog.register('media')
class MediaSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('媒体信息设置', FIF.MUSIC, parent)

    def _createSettings(self):
        self._addSectionTitle('功能开关')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用媒体信息', cfg.showMediaInfo)
        (self._coverRow, self._coverSwitch) = self._addSwitch('显示封面', cfg.showMediaCover)

        self._addSeparator()
        self._addSectionTitle('尺寸')
        (self._widthRow, self._widthSpin) = self._addSpinBox('组件宽度', cfg.mediaWidth, 200, 800)

        self._addSeparator()
        self._addSectionTitle('歌词')
        (self._lyricsRow, self._lyricsSpin) = self._addSpinBox('歌词提前(ms)', cfg.mediaLyricsAdvance, 0, 2000)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showMediaInfo.value)

    def _updateEnabled(self, enabled):
        self._coverSwitch.setEnabled(enabled)
        self._widthSpin.setEnabled(enabled)
        self._lyricsSpin.setEnabled(enabled)


@ComponentSettingDialog.register('quick_launch')
class QuickLaunchSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('快捷启动栏设置', FIF.LINK, parent)

    def _createSettings(self):
        self._addSectionTitle('功能开关')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用快捷启动栏', cfg.showQuickLaunch)

        self._addSeparator()
        self._addSectionTitle('外观')
        (self._iconSizeRow, self._iconSizeSpin) = self._addSpinBox('图标大小', cfg.quickLaunchIconSize, 32, 96)
        (self._spacingRow, self._spacingSpin) = self._addSpinBox('图标间距', cfg.quickLaunchIconSpacing, 4, 40)
        (self._labelsRow, self._labelsSwitch) = self._addSwitch('显示名称', cfg.quickLaunchShowLabels)

        self._addSeparator()
        self._addSectionTitle('布局')
        (self._offsetRow, self._offsetSpin) = self._addSpinBox('向上偏移', cfg.quickLaunchOffsetY, 0, 120)

        self._addSeparator()
        self._addSectionTitle('应用管理')
        edit_btn = PushButton('编辑应用')
        edit_btn.clicked.connect(self._onEditApps)
        self._addRow('应用列表', edit_btn)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showQuickLaunch.value)

    def _updateEnabled(self, enabled):
        self._iconSizeSpin.setEnabled(enabled)
        self._spacingSpin.setEnabled(enabled)
        self._labelsSwitch.setEnabled(enabled)
        self._offsetSpin.setEnabled(enabled)

    def _onEditApps(self):
        try:
            from ui.home import QuickLaunchEditDialog
            main_window = self.parent()
            while main_window:
                if hasattr(main_window, 'quickLaunchDock'):
                    break
                main_window = main_window.parent()
            if main_window and hasattr(main_window, 'quickLaunchDock'):
                dock = main_window.quickLaunchDock
                apps = dock.apps if hasattr(dock, 'apps') else []
                dialog = QuickLaunchEditDialog(apps, self)
                if dialog.exec() == 1:
                    dock.apps = dialog.getApps()
                    dock._refreshIcons()
        except Exception:
            logger.exception('打开应用编辑失败')
