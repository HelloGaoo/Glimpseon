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
from PyQt6.QtGui import QColor
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QDialog,
    QStackedWidget,
)
from qfluentwidgets import (
    BodyLabel, SwitchButton, SpinBox,
    ComboBox, PushButton, LineEdit, ListWidget,
    Theme, isDarkTheme,
    FluentIcon as FIF, ColorDialog,
    Pivot,
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
        self.setMinimumSize(460, 480)
        self.setWindowTitle(title)
        self.setWindowFlags(
            Qt.WindowType.Dialog
            | Qt.WindowType.WindowCloseButtonHint
            | Qt.WindowType.WindowStaysOnTopHint
        )
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self._basic_layout = None
        self._advanced_layout = None
        self._group_count = 0
        self._initLayout()
        self._applyStyle()

    def _initLayout(self):
        root = QVBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        self._pivot = Pivot(self)
        root.addWidget(self._pivot, 0, Qt.AlignmentFlag.AlignHCenter)

        self._stack = QStackedWidget(self)

        self._basic_page = QWidget()
        self._basic_page.setObjectName('basic')
        self._basic_layout = QVBoxLayout(self._basic_page)
        self._basic_layout.setContentsMargins(16, 12, 16, 12)
        self._basic_layout.setSpacing(8)
        self._stack.addWidget(self._basic_page)

        self._advanced_page = QWidget()
        self._advanced_page.setObjectName('advanced')
        self._advanced_layout = QVBoxLayout(self._advanced_page)
        self._advanced_layout.setContentsMargins(16, 12, 16, 12)
        self._advanced_layout.setSpacing(8)
        self._stack.addWidget(self._advanced_page)

        root.addWidget(self._stack, 1)

        self._pivot.addItem('basic', '组件设置')
        self._pivot.addItem('advanced', '高级设置')

        self._group_count = 2
        self._createSettings()

        self._basic_layout.addStretch()
        self._advanced_layout.addStretch()

        self._pivot.currentItemChanged.connect(
            lambda k: self._stack.setCurrentWidget(self._stack.findChild(QWidget, k)))
        self._stack.setCurrentWidget(self._basic_page)
        self._pivot.setCurrentItem('basic')

    def _createSettings(self):
        pass

    def _beginGroup(self, group: str):
        return group == 'advanced'

    def _endGroup(self):
        pass

    def _targetLayout(self, is_advanced=False):
        if is_advanced:
            return self._advanced_layout
        return self._basic_layout

    def _applyStyle(self):
        dark = isDarkTheme()
        bg = 'rgb(32, 32, 32)' if dark else 'rgb(255, 255, 255)'
        fg = '#FFFFFF' if dark else '#000000'
        self.setStyleSheet(f"""
            ComponentSettingDialog {{
                background-color: {bg};
                color: {fg};
            }}
            QStackedWidget {{
                background-color: {bg};
            }}
            #basic, #advanced {{
                background-color: {bg};
            }}
        """)

    def _addRow(self, label_text: str, widget: QWidget, is_advanced=False):
        row = QHBoxLayout()
        row.setSpacing(12)
        label = BodyLabel(label_text)
        label.setFixedWidth(100)
        row.addWidget(label)
        widget.setFixedWidth(160)
        row.addWidget(widget)
        row.addStretch()
        target = self._targetLayout(is_advanced)
        target.addLayout(row)
        return widget

    def _addSwitch(self, label_text: str, config_item, is_advanced=False):
        switch = SwitchButton()
        switch.setChecked(config_item.value)
        switch.checkedChanged.connect(lambda v, ci=config_item: setattr(ci, 'value', v))
        return self._addRow(label_text, switch, is_advanced), switch

    def _addSpinBox(self, label_text: str, config_item, min_val, max_val, is_advanced=False):
        spin = SpinBox()
        spin.setRange(min_val, max_val)
        spin.setValue(config_item.value)
        spin.valueChanged.connect(lambda v, ci=config_item: setattr(ci, 'value', v))
        return self._addRow(label_text, spin, is_advanced), spin

    def _addComboBox(self, label_text: str, items: list, config_item, is_advanced=False):
        combo = ComboBox()
        combo.addItems(items)
        combo.setCurrentText(config_item.value)
        combo.currentTextChanged.connect(lambda t, ci=config_item: setattr(ci, 'value', t))
        return self._addRow(label_text, combo, is_advanced), combo

    def _addLineEdit(self, label_text: str, config_item, placeholder='', is_advanced=False):
        edit = LineEdit()
        edit.setText(config_item.value)
        edit.setPlaceholderText(placeholder)
        edit.textChanged.connect(lambda t, ci=config_item: setattr(ci, 'value', t))
        return self._addRow(label_text, edit, is_advanced), edit

    def _addSectionTitle(self, text: str, is_advanced=False):
        pass

    def _addSeparator(self, is_advanced=False):
        pass

    def _addColorCombo(self, label_text: str, config_item, default='main', is_advanced=False):
        combo = ComboBox()
        combo.addItems(['主要颜色', '白色', '黑色', '红色'])
        combo.setCurrentText(self._getColorText(config_item.value, default))
        combo.currentTextChanged.connect(lambda t, ci=config_item: self._applyColorText(t, ci))
        return self._addRow(label_text, combo, is_advanced), combo

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

    def _addColorPicker(self, label_text: str, config_item, presets=None, is_advanced=False):
        if presets is None:
            presets = ["#FFFFFF", "#000000", "#30c361"]

        row_layout = QHBoxLayout()
        row_layout.setContentsMargins(0, 4, 0, 4)

        label = BodyLabel(label_text)
        label.setFixedWidth(90)
        row_layout.addWidget(label)

        for color in presets:
            btn = PushButton()
            btn.setFixedSize(24, 24)
            btn.setToolTip(color)
            c = QColor(color)
            brightness = (c.red() * 299 + c.green() * 587 + c.blue() * 114) / 1000
            border_c = "rgba(128,128,128,0.4)" if brightness > 128 else "rgba(255,255,255,0.2)"
            btn.setStyleSheet(
                f"PushButton {{ background-color: {color}; border: 1px solid {border_c}; border-radius: 4px; padding: 0; }}"
                f"PushButton:hover {{ border: 2px solid #30c361; }}"
            )
            btn.clicked.connect(lambda checked, co=color, ci=config_item: self._selectPresetColor(co, ci, custom_btn))
            row_layout.addWidget(btn)

        theme_btn = PushButton()
        theme_btn.setFixedSize(32, 24)
        theme_btn.setToolTip("跟随主题")
        text_c = "#333333" if not isDarkTheme() else "#FFFFFF"
        theme_btn.setText("Aa")
        theme_btn.setStyleSheet(
            f"PushButton {{"
            f"background: qlineargradient(x1:0, y1:0, x2:1, y2:0,"
            f"stop:0 #FFFFFF, stop:1 #000000);"
            f"border: 1px solid rgba(128,128,128,0.3);"
            f"border-radius: 4px; color: {text_c}; font-size: 10px; font-weight: bold;"
            f"}}"
            f"PushButton:hover {{ border: 2px solid #30c361; }}"
        )
        row_layout.addWidget(theme_btn)

        custom_btn = PushButton("自定义")
        custom_btn.setFixedSize(56, 24)
        custom_btn.setToolTip("自定义颜色")
        self._updateCustomBtnStyle(custom_btn, config_item.value)
        custom_btn.clicked.connect(lambda: self._onPickColor(custom_btn, config_item))
        row_layout.addWidget(custom_btn)

        theme_btn.clicked.connect(lambda checked, ci=config_item, cb=custom_btn: self._selectPresetColor("primary", ci, cb))

        row_layout.addStretch()

        target = self._targetLayout(is_advanced)
        target.addLayout(row_layout)
        return row_layout, custom_btn

    def _selectPresetColor(self, color, config_item, custom_btn):
        config_item.value = color
        self._updateCustomBtnStyle(custom_btn, color)

    def _updateCustomBtnStyle(self, btn, color_val):
        if hasattr(color_val, 'name'):
            color_val = color_val.name()
        if color_val == "primary":
            text_c = "#333333" if not isDarkTheme() else "#FFFFFF"
            btn.setStyleSheet(
                f"PushButton {{"
                f"background: qlineargradient(x1:0, y1:0, x2:1, y2:0,"
                f"stop:0 #FFFFFF, stop:1 #000000);"
                f"border: 1px solid rgba(128,128,128,0.3);"
                f"border-radius: 4px; color: {text_c}; font-size: 10px; font-weight: bold;"
                f"}}"
            )
            return
        c = QColor(color_val)
        brightness = (c.red() * 299 + c.green() * 587 + c.blue() * 114) / 1000
        text_c = "#333333" if brightness > 128 else "#FFFFFF"
        btn.setStyleSheet(
            f"PushButton {{ background-color: {color_val}; border: 1px solid rgba(128,128,128,0.3); "
            f"border-radius: 4px; color: {text_c}; font-size: 11px; padding: 0; }}"
        )

    def _onPickColor(self, btn, config_item):
        current = config_item.value
        if hasattr(current, 'name'):
            current = current.name()
        initial = QColor(current)
        top_parent = self.window()
        dialog = ColorDialog(initial, "选择颜色", top_parent, enableAlpha=True)
        dialog.colorChanged.connect(lambda c: self._updateCustomBtnStyle(btn, c.name()))
        if dialog.exec():
            config_item.value = dialog.color.name()
            self._updateCustomBtnStyle(btn, config_item.value)


@ComponentSettingDialog.register('clock')
class ClockSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('时钟设置', FIF.DATE_TIME, parent)

    def _createSettings(self):
        basic = self._beginGroup('basic')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用时钟', cfg.showClock, is_advanced=basic)
        (self._secondsRow, self._secondsSwitch) = self._addSwitch('显示秒针', cfg.showClockSeconds, is_advanced=basic)
        (self._lunarRow, self._lunarSwitch) = self._addSwitch('显示农历', cfg.showLunarCalendar, is_advanced=basic)

        advanced = self._beginGroup('advanced')
        (self._colorRow, self._colorCombo) = self._addColorCombo('时钟颜色', cfg.clockColor, is_advanced=advanced)
        (self._clockSizeRow, self._clockSizeSpin) = self._addSpinBox('时钟大小', cfg.clockSize, 80, 200, is_advanced=advanced)
        (self._dateSizeRow, self._dateSizeSpin) = self._addSpinBox('日期大小', cfg.dateSize, 12, 50, is_advanced=advanced)

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
        basic = self._beginGroup('basic')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用天气', cfg.showWeather, is_advanced=basic)
        self._cityButton = self._addRow('城市', PushButton(cfg.city.value), is_advanced=basic)
        self._cityButton.clicked.connect(self._onCityClicked)

        advanced = self._beginGroup('advanced')
        (self._sizeRow, self._sizeSpin) = self._addSpinBox('文字大小', cfg.weatherSize, 5, 50, is_advanced=advanced)
        (self._iconSizeRow, self._iconSizeSpin) = self._addSpinBox('图标大小', cfg.weatherIconSize, 32, 128, is_advanced=advanced)
        (self._intervalRow, self._intervalCombo) = self._addComboBox(
            '更新间隔',
            ['从不', '5 分钟', '15 分钟', '30 分钟', '1 小时', '3 小时', '6 小时', '12 小时', '24 小时'],
            cfg.weatherUpdateInterval,
            is_advanced=advanced
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
        basic = self._beginGroup('basic')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用一言', cfg.showPoetry, is_advanced=basic)
        self._apiCombo = self._addRow('API', ComboBox(), is_advanced=basic)
        self._apiCombo.addItems(['一言 API', '诗词 API'])
        if cfg.poetryApiUrl.value == 'https://www.ffapi.cn/int/v1/shici':
            self._apiCombo.setCurrentText('诗词 API')
        else:
            self._apiCombo.setCurrentText('一言 API')
        self._apiCombo.currentTextChanged.connect(self._onApiChanged)

        advanced = self._beginGroup('advanced')
        (self._sizeRow, self._sizeSpin) = self._addSpinBox('文字大小', cfg.poetrySize, 12, 50, is_advanced=advanced)
        (self._intervalRow, self._intervalCombo) = self._addComboBox(
            '更新间隔',
            ['从不', '5 分钟', '10 分钟', '30 分钟', '1 小时', '3 小时', '6 小时', '12 小时', '1 天'],
            cfg.poetryUpdateInterval,
            is_advanced=advanced
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
        basic = self._beginGroup('basic')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用倒计时', cfg.showCountdown, is_advanced=basic)
        mode_combo = ComboBox()
        mode_combo.addItems(['同时显示', '轮播显示'])
        mode_combo.setCurrentText('同时显示' if cfg.countdownDisplayMode.value == 'simultaneous' else '轮播显示')
        mode_combo.currentTextChanged.connect(
            lambda t: setattr(cfg.countdownDisplayMode, 'value', 'simultaneous' if t == '同时显示' else 'carousel')
        )
        self._addRow('显示模式', mode_combo, is_advanced=basic)
        (self._carouselRow, self._carouselSpin) = self._addSpinBox('轮播间隔(秒)', cfg.countdownCarouselInterval, 1, 60, is_advanced=basic)

        advanced = self._beginGroup('advanced')
        (self._textColorRow, self._textColorCombo) = self._addColorCombo('文字颜色', cfg.countdownTextColor, 'red', is_advanced=advanced)
        (self._connectorColorRow, self._connectorColorCombo) = self._addColorCombo('连接词颜色', cfg.countdownConnectorColor, 'white', is_advanced=advanced)
        (self._textSizeRow, self._textSizeSpin) = self._addSpinBox('文字大小', cfg.countdownTextSize, 12, 120, is_advanced=advanced)
        (self._connectorSizeRow, self._connectorSizeSpin) = self._addSpinBox('连接词大小', cfg.countdownConnectorSize, 12, 60, is_advanced=advanced)
        self._listWidget = ListWidget()
        self._listWidget.setFixedHeight(140)
        self._advanced_layout.addWidget(self._listWidget)
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
        self._advanced_layout.addLayout(btn_row)

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
        basic = self._beginGroup('basic')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用学校信息', cfg.showSchoolInfo, is_advanced=basic)
        (self._classRow, self._classEdit) = self._addLineEdit('班级', cfg.schoolClass, '例如：高三 (1) 班', is_advanced=basic)
        (self._schoolRow, self._schoolEdit) = self._addLineEdit('学校', cfg.school, '例如：XX 中学', is_advanced=basic)

        advanced = self._beginGroup('advanced')
        (self._colorRow, self._colorCombo) = self._addColorCombo('文字颜色', cfg.schoolInfoTextColor, 'white', is_advanced=advanced)
        (self._sizeRow, self._sizeSpin) = self._addSpinBox('文字大小', cfg.schoolInfoTextSize, 12, 60, is_advanced=advanced)

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
        self._all_widgets = []
        super().__init__('媒体信息设置', FIF.MUSIC, parent)

    def _createSettings(self):
        basic = self._beginGroup('basic')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用媒体信息', cfg.showMediaInfo, is_advanced=basic)
        self._all_widgets.append(self._enableSwitch)
        (self._coverRow, self._coverSwitch) = self._addSwitch('显示封面', cfg.showMediaCover, is_advanced=basic)
        self._all_widgets.append(self._coverSwitch)
        (self._progressRow, self._progressSwitch) = self._addSwitch('显示进度条', cfg.showMediaProgress, is_advanced=basic)
        self._all_widgets.append(self._progressSwitch)
        (self._lyricsShowRow, self._lyricsShowSwitch) = self._addSwitch('显示歌词', cfg.showMediaLyrics, is_advanced=basic)
        self._all_widgets.append(self._lyricsShowSwitch)

        advanced = self._beginGroup('advanced')
        (self._widthRow, self._widthSpin) = self._addSpinBox('组件宽度', cfg.mediaWidth, 200, 800, is_advanced=advanced)
        self._all_widgets.append(self._widthSpin)
        (self._heightRow, self._heightSpin) = self._addSpinBox('组件高度', cfg.mediaHeight, 80, 300, is_advanced=advanced)
        self._all_widgets.append(self._heightSpin)
        (self._textSizeRow, self._textSizeSpin) = self._addSpinBox('文字大小', cfg.mediaTextSize, 12, 32, is_advanced=advanced)
        self._all_widgets.append(self._textSizeSpin)
        (self._coverSizeRow, self._coverSizeSpin) = self._addSpinBox('封面大小', cfg.mediaCoverSize, 32, 128, is_advanced=advanced)
        self._all_widgets.append(self._coverSizeSpin)
        (self._bgColorRow, self._bgColorBtn) = self._addColorPicker('背景颜色', cfg.mediaBgColor, is_advanced=advanced)
        self._all_widgets.append(self._bgColorBtn)
        (self._bgOpacityRow, self._bgOpacitySpin) = self._addSpinBox('背景透明度', cfg.mediaBgOpacity, 0, 100, is_advanced=advanced)
        self._all_widgets.append(self._bgOpacitySpin)
        (self._borderRadiusRow, self._borderRadiusSpin) = self._addSpinBox('圆角半径', cfg.mediaBorderRadius, 0, 30, is_advanced=advanced)
        self._all_widgets.append(self._borderRadiusSpin)
        (self._borderWidthRow, self._borderWidthSpin) = self._addSpinBox('边框宽度', cfg.mediaBorderWidth, 0, 5, is_advanced=advanced)
        self._all_widgets.append(self._borderWidthSpin)
        (self._borderColorRow, self._borderColorBtn) = self._addColorPicker('边框颜色', cfg.mediaBorderColor, is_advanced=advanced)
        self._all_widgets.append(self._borderColorBtn)
        (self._titleColorRow, self._titleColorBtn) = self._addColorPicker('标题颜色', cfg.mediaTitleColor, is_advanced=advanced)
        self._all_widgets.append(self._titleColorBtn)
        (self._artistColorRow, self._artistColorBtn) = self._addColorPicker('艺术家颜色', cfg.mediaArtistColor, is_advanced=advanced)
        self._all_widgets.append(self._artistColorBtn)
        (self._timeColorRow, self._timeColorBtn) = self._addColorPicker('时间颜色', cfg.mediaTimeColor, is_advanced=advanced)
        self._all_widgets.append(self._timeColorBtn)
        (self._lyricsColorRow, self._lyricsColorBtn) = self._addColorPicker('歌词颜色', cfg.mediaLyricsColor, is_advanced=advanced)
        self._all_widgets.append(self._lyricsColorBtn)
        (self._progressColorRow, self._progressColorBtn) = self._addColorPicker('进度条颜色', cfg.mediaProgressColor, is_advanced=advanced)
        self._all_widgets.append(self._progressColorBtn)
        (self._progressTrackRow, self._progressTrackBtn) = self._addColorPicker('轨道颜色', cfg.mediaProgressTrackColor, is_advanced=advanced)
        self._all_widgets.append(self._progressTrackBtn)
        (self._progressHeightRow, self._progressHeightSpin) = self._addSpinBox('进度条高度', cfg.mediaProgressHeight, 2, 8, is_advanced=advanced)
        self._all_widgets.append(self._progressHeightSpin)
        (self._coverRadiusRow, self._coverRadiusSpin) = self._addSpinBox('封面圆角', cfg.mediaCoverBorderRadius, 0, 20, is_advanced=advanced)
        self._all_widgets.append(self._coverRadiusSpin)
        (self._coverBorderColorRow, self._coverBorderColorBtn) = self._addColorPicker('封面边框色', cfg.mediaCoverBorderColor, is_advanced=advanced)
        self._all_widgets.append(self._coverBorderColorBtn)
        (self._lyricsSizeRow, self._lyricsSizeSpin) = self._addSpinBox('歌词字号', cfg.mediaLyricsSize, 10, 24, is_advanced=advanced)
        self._all_widgets.append(self._lyricsSizeSpin)
        (self._lyricsAdvanceRow, self._lyricsAdvanceSpin) = self._addSpinBox('歌词提前(ms)', cfg.mediaLyricsAdvance, 0, 2000, is_advanced=advanced)
        self._all_widgets.append(self._lyricsAdvanceSpin)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showMediaInfo.value)

    def _updateEnabled(self, enabled):
        for w in self._all_widgets:
            w.setEnabled(enabled)


@ComponentSettingDialog.register('quick_launch')
class QuickLaunchSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('快捷启动栏设置', FIF.LINK, parent)

    def _createSettings(self):
        basic = self._beginGroup('basic')
        (self._enableRow, self._enableSwitch) = self._addSwitch('启用快捷启动栏', cfg.showQuickLaunch, is_advanced=basic)
        edit_btn = PushButton('编辑应用')
        edit_btn.clicked.connect(self._onEditApps)
        self._addRow('应用列表', edit_btn, is_advanced=basic)

        advanced = self._beginGroup('advanced')
        (self._iconSizeRow, self._iconSizeSpin) = self._addSpinBox('图标大小', cfg.quickLaunchIconSize, 32, 96, is_advanced=advanced)
        (self._spacingRow, self._spacingSpin) = self._addSpinBox('图标间距', cfg.quickLaunchIconSpacing, 4, 40, is_advanced=advanced)
        (self._labelsRow, self._labelsSwitch) = self._addSwitch('显示名称', cfg.quickLaunchShowLabels, is_advanced=advanced)
        (self._offsetRow, self._offsetSpin) = self._addSpinBox('向上偏移', cfg.quickLaunchOffsetY, 0, 120, is_advanced=advanced)

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
