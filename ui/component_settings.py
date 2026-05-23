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
    ScrollArea, Theme, isDarkTheme,
    FluentIcon as FIF, ColorDialog,
    Pivot, StrongBodyLabel,
    SettingCard, SettingCardGroup, ExpandLayout,
)

from core.config import cfg
from core.constants import load_qss

logger = logging.getLogger("ClassLively.ui.component_settings")

import winreg


def _get_system_accent_color_hex() -> str:
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
            r'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent')
        palette = winreg.QueryValueEx(key, 'AccentPalette')[0]
        winreg.CloseKey(key)
        if len(palette) >= 12:
            b, g, r = palette[8], palette[9], palette[10]
            return f'#{r:02X}{g:02X}{b:02X}'
    except Exception:
        pass
    return '#30c361'


class _SettingRow(SettingCard):
    """通用设置行：左侧标签 + 右侧控件"""

    def __init__(self, title: str, widget: QWidget, parent=None):
        super().__init__(FIF.SETTING, title, '', parent)
        self.hBoxLayout.removeWidget(self.contentLabel)
        self.contentLabel.deleteLater()
        self.contentLabel = None
        self._widget = widget
        self.hBoxLayout.addWidget(widget, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)


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
        self.setMinimumSize(520, 500)
        self.resize(540, 600)
        self.setWindowTitle(title)
        self.setWindowFlags(
            Qt.WindowType.Dialog
            | Qt.WindowType.WindowCloseButtonHint
            | Qt.WindowType.WindowStaysOnTopHint
        )
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setObjectName('settingDialog')

        self._basic_groups = []
        self._advanced_groups = []

        self._initLayout()
        self._applyStyle()

    def _initLayout(self):
        root = QVBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        self._pivot = Pivot(self)
        root.addWidget(self._pivot, 0, Qt.AlignmentFlag.AlignHCenter)

        self._stack = QStackedWidget(self)

        self._basic_scroll = ScrollArea()
        self._basic_scroll.setWidgetResizable(True)
        self._basic_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self._basic_widget = QWidget()
        self._basic_widget.setObjectName('basicScrollWidget')
        self._basic_layout = ExpandLayout(self._basic_widget)
        self._basic_layout.setSpacing(16)
        self._basic_layout.setContentsMargins(20, 10, 20, 20)
        self._basic_scroll.setWidget(self._basic_widget)
        self._stack.addWidget(self._basic_scroll)

        self._advanced_scroll = ScrollArea()
        self._advanced_scroll.setWidgetResizable(True)
        self._advanced_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self._advanced_widget = QWidget()
        self._advanced_widget.setObjectName('advancedScrollWidget')
        self._advanced_layout = ExpandLayout(self._advanced_widget)
        self._advanced_layout.setSpacing(16)
        self._advanced_layout.setContentsMargins(20, 10, 20, 20)
        self._advanced_scroll.setWidget(self._advanced_widget)
        self._stack.addWidget(self._advanced_scroll)

        root.addWidget(self._stack, 1)

        self._pivot.addItem('basic', '组件设置')
        self._pivot.addItem('advanced', '高级设置')

        self._createSettings()

        self._pivot.currentItemChanged.connect(self._onPivotChanged)
        self._stack.setCurrentWidget(self._basic_scroll)
        self._pivot.setCurrentItem('basic')

    def _onPivotChanged(self, key):
        if key == 'basic':
            self._stack.setCurrentWidget(self._basic_scroll)
        elif key == 'advanced':
            self._stack.setCurrentWidget(self._advanced_scroll)

    def _createSettings(self):
        pass

    def _addGroup(self, name: str, is_advanced=False):
        parent_widget = self._advanced_widget if is_advanced else self._basic_widget
        layout = self._advanced_layout if is_advanced else self._basic_layout
        group = SettingCardGroup(name, parent_widget)
        if is_advanced:
            self._advanced_groups.append(group)
        else:
            self._basic_groups.append(group)
        layout.addWidget(group)
        return group

    def _beginGroup(self, group: str):
        if group == 'basic':
            self._addGroup('基本', is_advanced=False)
            return False
        if group == 'advanced':
            return True
        return False

    def _applyStyle(self):
        self.setStyleSheet(load_qss('setting_dialog.qss'))

    def _addSwitch(self, title: str, config_item, group=None, is_advanced=False):
        switch = SwitchButton()
        switch.setChecked(config_item.value)
        switch.checkedChanged.connect(lambda v, ci=config_item: setattr(ci, 'value', v))
        card = _SettingRow(title, switch)
        target_group = group
        if target_group is None:
            groups = self._advanced_groups if is_advanced else self._basic_groups
            target_group = groups[-1] if groups else None
        if target_group:
            target_group.addSettingCard(card)
        return card, switch

    def _addSpinBox(self, title: str, config_item, min_val, max_val, group=None, is_advanced=False):
        spin = SpinBox()
        spin.setRange(min_val, max_val)
        spin.setValue(config_item.value)
        spin.valueChanged.connect(lambda v, ci=config_item: setattr(ci, 'value', v))
        card = _SettingRow(title, spin)
        target_group = group
        if target_group is None:
            groups = self._advanced_groups if is_advanced else self._basic_groups
            target_group = groups[-1] if groups else None
        if target_group:
            target_group.addSettingCard(card)
        return card, spin

    def _addComboBox(self, title: str, items: list, config_item, group=None, is_advanced=False):
        combo = ComboBox()
        combo.addItems(items)
        combo.setCurrentText(config_item.value)
        combo.currentTextChanged.connect(lambda t, ci=config_item: setattr(ci, 'value', t))
        card = _SettingRow(title, combo)
        target_group = group
        if target_group is None:
            groups = self._advanced_groups if is_advanced else self._basic_groups
            target_group = groups[-1] if groups else None
        if target_group:
            target_group.addSettingCard(card)
        return card, combo

    def _addLineEdit(self, title: str, config_item, placeholder='', group=None, is_advanced=False):
        edit = LineEdit()
        edit.setText(config_item.value)
        edit.setPlaceholderText(placeholder)
        edit.textChanged.connect(lambda t, ci=config_item: setattr(ci, 'value', t))
        card = _SettingRow(title, edit)
        target_group = group
        if target_group is None:
            groups = self._advanced_groups if is_advanced else self._basic_groups
            target_group = groups[-1] if groups else None
        if target_group:
            target_group.addSettingCard(card)
        return card, edit

    def _addButtonRow(self, title: str, button: PushButton, group=None, is_advanced=False):
        card = _SettingRow(title, button)
        target_group = group
        if target_group is None:
            groups = self._advanced_groups if is_advanced else self._basic_groups
            target_group = groups[-1] if groups else None
        if target_group:
            target_group.addSettingCard(card)
        return card

    def _addColorPicker(self, title: str, config_item, presets=None, group=None, is_advanced=False):
        if presets is None:
            presets = ["#FFFFFF", "#000000", "#30c361"]

        color_container = QWidget()
        color_container.setFixedHeight(32)
        row = QHBoxLayout(color_container)
        row.setContentsMargins(0, 0, 0, 0)
        row.setSpacing(6)

        custom_btn = PushButton("自定义")
        custom_btn.setFixedSize(56, 24)
        custom_btn.setToolTip("自定义颜色")
        self._updateCustomBtnStyle(custom_btn, config_item.value)

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
            btn.clicked.connect(lambda checked, co=color, ci=config_item, cb=custom_btn: self._selectPresetColor(co, ci, cb))
            row.addWidget(btn)

        wall_btn = PushButton()
        wall_btn.setFixedSize(24, 24)
        wall_btn.setToolTip('跟随壁纸')
        wall_btn.setText('壁')
        wall_btn.setStyleSheet(
            f"PushButton {{ background: qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 #FFFFFF, stop:1 #000000);"
            f"border: 1px solid rgba(128,128,128,0.3); border-radius: 4px; color: #333; font-size: 10px; font-weight: bold; }}"
        )
        wall_btn.clicked.connect(lambda checked, ci=config_item, cb=custom_btn: self._selectPresetColor('wallpaper', ci, cb))
        row.addWidget(wall_btn)

        sys_btn = PushButton()
        sys_btn.setFixedSize(24, 24)
        sys_btn.setToolTip('跟随系统')
        sys_btn.setText('系')
        sys_accent = _get_system_accent_color_hex()
        sys_btn.setStyleSheet(
            f"PushButton {{ background-color: {sys_accent}; border: 1px solid rgba(128,128,128,0.3); border-radius: 4px; color: #333; font-size: 10px; font-weight: bold; }}"
            f"PushButton:hover {{ border: 2px solid #30c361; }}"
        )
        sys_btn.clicked.connect(lambda checked, ci=config_item, cb=custom_btn: self._selectPresetColor('system', ci, cb))
        row.addWidget(sys_btn)

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
        row.addWidget(theme_btn)

        row.addWidget(custom_btn)
        custom_btn.clicked.connect(lambda: self._onPickColor(custom_btn, config_item))

        theme_btn.clicked.connect(lambda checked, ci=config_item, cb=custom_btn: self._selectPresetColor("primary", ci, cb))

        row.addStretch()

        card = _SettingRow(title, color_container)
        target_group = group
        if target_group is None:
            groups = self._advanced_groups if is_advanced else self._basic_groups
            target_group = groups[-1] if groups else None
        if target_group:
            target_group.addSettingCard(card)
        return card, custom_btn

    def _addWidgetToGroup(self, widget: QWidget, group=None, is_advanced=False):
        target_group = group
        if target_group is None:
            groups = self._advanced_groups if is_advanced else self._basic_groups
            target_group = groups[-1] if groups else None
        if target_group:
            target_group.addSettingCard(widget)

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
        if color_val == 'wallpaper':
            text_c = "#333333" if not isDarkTheme() else "#FFFFFF"
            btn.setStyleSheet(
                f"PushButton {{ background: qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 #FFFFFF, stop:1 #000000);"
                f"border: 1px solid rgba(128,128,128,0.3); border-radius: 4px; color: {text_c}; font-size: 10px; font-weight: bold; }}"
            )
            return
        if color_val == 'system':
            sys_accent = _get_system_accent_color_hex()
            btn.setStyleSheet(
                f"PushButton {{ background-color: {sys_accent}; border: 1px solid rgba(128,128,128,0.3); "
                f"border-radius: 4px; color: #333; font-size: 11px; padding: 0; }}"
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
        dialog = ColorDialog(initial, "选择颜色", top_parent, enableAlpha=False)
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
        (self._enableCard, self._enableSwitch) = self._addSwitch('启用时钟', cfg.showClock, is_advanced=basic)
        (self._secondsCard, self._secondsSwitch) = self._addSwitch('显示秒针', cfg.showClockSeconds, is_advanced=basic)
        (self._lunarCard, self._lunarSwitch) = self._addSwitch('显示农历', cfg.showLunarCalendar, is_advanced=basic)

        g_style = self._addGroup('外观', is_advanced=True)
        (self._colorCard, self._colorBtn) = self._addColorPicker('时钟颜色', cfg.clockColor, group=g_style)
        (self._clockSizeCard, self._clockSizeSpin) = self._addSpinBox('时钟大小', cfg.clockSize, 80, 200, group=g_style)
        (self._dateSizeCard, self._dateSizeSpin) = self._addSpinBox('日期大小', cfg.dateSize, 12, 50, group=g_style)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showClock.value)

    def _updateEnabled(self, enabled):
        self._secondsCard.setEnabled(enabled)
        self._lunarCard.setEnabled(enabled)
        self._colorCard.setEnabled(enabled)
        self._clockSizeCard.setEnabled(enabled)
        self._dateSizeCard.setEnabled(enabled)


@ComponentSettingDialog.register('weather')
class WeatherSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('天气设置', FIF.CLOUD, parent)

    def _createSettings(self):
        basic = self._beginGroup('basic')
        (self._enableCard, self._enableSwitch) = self._addSwitch('启用天气', cfg.showWeather, is_advanced=basic)
        city_btn = PushButton(cfg.city.value)
        self._cityCard = self._addButtonRow('城市', city_btn, is_advanced=basic)
        city_btn.clicked.connect(self._onCityClicked)

        g_style = self._addGroup('样式', is_advanced=True)
        (self._sizeCard, self._sizeSpin) = self._addSpinBox('文字大小', cfg.weatherSize, 5, 50, group=g_style)
        (self._textColorCard, self._textColorBtn) = self._addColorPicker('文字颜色', cfg.weatherTextColor, group=g_style)
        (self._iconSizeCard, self._iconSizeSpin) = self._addSpinBox('图标大小', cfg.weatherIconSize, 32, 128, group=g_style)

        g_data = self._addGroup('数据', is_advanced=True)
        (self._intervalCard, self._intervalCombo) = self._addComboBox(
            '更新间隔',
            ['从不', '5 分钟', '15 分钟', '30 分钟', '1 小时', '3 小时', '6 小时', '12 小时', '24 小时'],
            cfg.weatherUpdateInterval,
            group=g_data
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
                    for btn in self._cityCard.findChildren(PushButton):
                        btn.setText(selected_region)
                    if hasattr(main_window, '_MainWindow__updateWeather'):
                        main_window._MainWindow__updateWeather()
        except Exception:
            logger.exception('打开城市选择器失败')

    def _updateEnabled(self, enabled):
        self._cityCard.setEnabled(enabled)
        self._sizeCard.setEnabled(enabled)
        self._textColorCard.setEnabled(enabled)
        self._iconSizeCard.setEnabled(enabled)
        self._intervalCard.setEnabled(enabled)


@ComponentSettingDialog.register('poetry')
class PoetrySettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('一言设置', FIF.BOOK_SHELF, parent)

    def _createSettings(self):
        basic = self._beginGroup('basic')
        (self._enableCard, self._enableSwitch) = self._addSwitch('启用一言', cfg.showPoetry, is_advanced=basic)
        api_combo = ComboBox()
        api_combo.addItems(['一言 API', '诗词 API'])
        if cfg.poetryApiUrl.value == 'https://www.ffapi.cn/int/v1/shici':
            api_combo.setCurrentText('诗词 API')
        else:
            api_combo.setCurrentText('一言 API')
        api_combo.currentTextChanged.connect(self._onApiChanged)
        (self._apiCard, self._apiCombo) = ('api_card', api_combo)
        self._apiCard = _SettingRow('API', api_combo)
        groups_basic = self._basic_groups
        if groups_basic:
            groups_basic[-1].addSettingCard(self._apiCard)
        self._apiCombo = api_combo

        g_style = self._addGroup('样式', is_advanced=True)
        (self._sizeCard, self._sizeSpin) = self._addSpinBox('文字大小', cfg.poetrySize, 12, 50, group=g_style)
        (self._textColorCard, self._textColorBtn) = self._addColorPicker('文字颜色', cfg.poetryTextColor, group=g_style)

        g_data = self._addGroup('数据', is_advanced=True)
        (self._intervalCard, self._intervalCombo) = self._addComboBox(
            '更新间隔',
            ['从不', '5 分钟', '10 分钟', '30 分钟', '1 小时', '3 小时', '6 小时', '12 小时', '1 天'],
            cfg.poetryUpdateInterval,
            group=g_data
        )

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showPoetry.value)

    def _onApiChanged(self, text):
        if text == '诗词 API':
            cfg.poetryApiUrl.value = 'https://www.ffapi.cn/int/v1/shici'
        else:
            cfg.poetryApiUrl.value = 'https://api.imlcd.cn/yy/api.php'

    def _updateEnabled(self, enabled):
        self._apiCard.setEnabled(enabled)
        self._sizeCard.setEnabled(enabled)
        self._textColorCard.setEnabled(enabled)
        self._intervalCard.setEnabled(enabled)


@ComponentSettingDialog.register('countdown')
class CountdownSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('倒计时设置', FIF.STOP_WATCH, parent)

    def _createSettings(self):
        basic = self._beginGroup('basic')
        (self._enableCard, self._enableSwitch) = self._addSwitch('启用倒计时', cfg.showCountdown, is_advanced=basic)
        mode_combo = ComboBox()
        mode_combo.addItems(['同时显示', '轮播显示'])
        mode_combo.setCurrentText('同时显示' if cfg.countdownDisplayMode.value == 'simultaneous' else '轮播显示')
        mode_combo.currentTextChanged.connect(
            lambda t: setattr(cfg.countdownDisplayMode, 'value', 'simultaneous' if t == '同时显示' else 'carousel')
        )
        self._modeCard = _SettingRow('显示模式', mode_combo)
        if self._basic_groups:
            self._basic_groups[-1].addSettingCard(self._modeCard)
        self._modeCombo = mode_combo
        (self._carouselCard, self._carouselSpin) = self._addSpinBox('轮播间隔(秒)', cfg.countdownCarouselInterval, 1, 60, is_advanced=basic)

        g_style = self._addGroup('样式', is_advanced=True)
        (self._textColorCard, self._textColorBtn) = self._addColorPicker('文字颜色', cfg.countdownTextColor, group=g_style)
        (self._connectorColorCard, self._connectorColorBtn) = self._addColorPicker('连接词颜色', cfg.countdownConnectorColor, group=g_style)
        (self._textSizeCard, self._textSizeSpin) = self._addSpinBox('文字大小', cfg.countdownTextSize, 12, 120, group=g_style)
        (self._connectorSizeCard, self._connectorSizeSpin) = self._addSpinBox('连接词大小', cfg.countdownConnectorSize, 12, 60, group=g_style)

        g_list = self._addGroup('倒计时列表', is_advanced=True)
        self._listWidget = ListWidget()
        self._listWidget.setFixedHeight(140)
        list_card = _SettingRow('', self._listWidget)
        g_list.addSettingCard(list_card)

        btn_container = QWidget()
        btn_row = QHBoxLayout(btn_container)
        btn_row.setContentsMargins(0, 0, 0, 0)
        btn_row.setSpacing(8)
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
        btn_card = _SettingRow('', btn_container)
        g_list.addSettingCard(btn_card)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showCountdown.value)

    def _updateEnabled(self, enabled):
        self._textColorCard.setEnabled(enabled)
        self._connectorColorCard.setEnabled(enabled)
        self._textSizeCard.setEnabled(enabled)
        self._connectorSizeCard.setEnabled(enabled)
        self._carouselCard.setEnabled(enabled)
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
        (self._enableCard, self._enableSwitch) = self._addSwitch('启用学校信息', cfg.showSchoolInfo, is_advanced=basic)
        (self._classCard, self._classEdit) = self._addLineEdit('班级', cfg.schoolClass, '例：2516班', is_advanced=basic)
        (self._schoolCard, self._schoolEdit) = self._addLineEdit('学校', cfg.school, '例：第二实验中学', is_advanced=basic)

        g_appearance = self._addGroup('外观', is_advanced=True)
        (self._colorCard, self._colorBtn) = self._addColorPicker('文字颜色', cfg.schoolInfoTextColor, group=g_appearance)
        (self._sizeCard, self._sizeSpin) = self._addSpinBox('文字大小', cfg.schoolInfoTextSize, 12, 60, group=g_appearance)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showSchoolInfo.value)

    def _updateEnabled(self, enabled):
        self._classCard.setEnabled(enabled)
        self._schoolCard.setEnabled(enabled)
        self._colorCard.setEnabled(enabled)
        self._sizeCard.setEnabled(enabled)


@ComponentSettingDialog.register('media')
class MediaSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        self._all_cards = []
        super().__init__('媒体信息设置', FIF.MUSIC, parent)

    def _createSettings(self):
        basic = self._beginGroup('basic')
        (self._enableCard, self._enableSwitch) = self._addSwitch('启用媒体信息', cfg.showMediaInfo, is_advanced=basic)
        (self._coverCard, self._coverSwitch) = self._addSwitch('显示封面', cfg.showMediaCover, is_advanced=basic)
        self._all_cards.append(self._coverCard)
        (self._progressCard, self._progressSwitch) = self._addSwitch('显示进度条', cfg.showMediaProgress, is_advanced=basic)
        self._all_cards.append(self._progressCard)
        (self._lyricsShowCard, self._lyricsShowSwitch) = self._addSwitch('显示歌词', cfg.showMediaLyrics, is_advanced=basic)
        self._all_cards.append(self._lyricsShowCard)

        g_size = self._addGroup('尺寸', is_advanced=True)
        (card, spin) = self._addSpinBox('组件宽度', cfg.mediaWidth, 200, 800, group=g_size)
        self._widthSpin = spin
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('组件高度', cfg.mediaHeight, 80, 300, group=g_size)
        self._heightSpin = spin
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('文字大小', cfg.mediaTextSize, 12, 32, group=g_size)
        self._textSizeSpin = spin
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('封面大小', cfg.mediaCoverSize, 32, 128, group=g_size)
        self._coverSizeSpin = spin
        self._all_cards.append(card)

        g_bg = self._addGroup('背景', is_advanced=True)
        (card, btn) = self._addColorPicker('背景颜色', cfg.mediaBgColor, group=g_bg)
        self._bgColorBtn = btn
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('背景透明度', cfg.mediaBgOpacity, 0, 100, group=g_bg)
        self._bgOpacitySpin = spin
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('圆角半径', cfg.mediaBorderRadius, 0, 30, group=g_bg)
        self._borderRadiusSpin = spin
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('边框宽度', cfg.mediaBorderWidth, 0, 5, group=g_bg)
        self._borderWidthSpin = spin
        self._all_cards.append(card)
        (card, btn) = self._addColorPicker('边框颜色', cfg.mediaBorderColor, group=g_bg)
        self._borderColorBtn = btn
        self._all_cards.append(card)

        g_text = self._addGroup('文字', is_advanced=True)
        (card, btn) = self._addColorPicker('标题颜色', cfg.mediaTitleColor, group=g_text)
        self._titleColorBtn = btn
        self._all_cards.append(card)
        (card, btn) = self._addColorPicker('艺术家颜色', cfg.mediaArtistColor, group=g_text)
        self._artistColorBtn = btn
        self._all_cards.append(card)
        (card, btn) = self._addColorPicker('时间颜色', cfg.mediaTimeColor, group=g_text)
        self._timeColorBtn = btn
        self._all_cards.append(card)

        g_lyrics = self._addGroup('歌词', is_advanced=True)
        (card, btn) = self._addColorPicker('歌词颜色', cfg.mediaLyricsColor, group=g_lyrics)
        self._lyricsColorBtn = btn
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('歌词字号', cfg.mediaLyricsSize, 10, 24, group=g_lyrics)
        self._lyricsSizeSpin = spin
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('歌词提前(ms)', cfg.mediaLyricsAdvance, 0, 2000, group=g_lyrics)
        self._lyricsAdvanceSpin = spin
        self._all_cards.append(card)

        g_progress = self._addGroup('进度条', is_advanced=True)
        (card, btn) = self._addColorPicker('进度条颜色', cfg.mediaProgressColor, group=g_progress)
        self._progressColorBtn = btn
        self._all_cards.append(card)
        (card, btn) = self._addColorPicker('轨道颜色', cfg.mediaProgressTrackColor, group=g_progress)
        self._progressTrackBtn = btn
        self._all_cards.append(card)
        (card, spin) = self._addSpinBox('进度条高度', cfg.mediaProgressHeight, 2, 8, group=g_progress)
        self._progressHeightSpin = spin
        self._all_cards.append(card)

        g_cover = self._addGroup('封面', is_advanced=True)
        (card, spin) = self._addSpinBox('封面圆角', cfg.mediaCoverBorderRadius, 0, 20, group=g_cover)
        self._coverRadiusSpin = spin
        self._all_cards.append(card)
        (card, btn) = self._addColorPicker('封面边框色', cfg.mediaCoverBorderColor, group=g_cover)
        self._coverBorderColorBtn = btn
        self._all_cards.append(card)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showMediaInfo.value)

    def _updateEnabled(self, enabled):
        for card in self._all_cards:
            card.setEnabled(enabled)


@ComponentSettingDialog.register('quick_launch')
class QuickLaunchSettingDialog(ComponentSettingDialog):
    def __init__(self, parent=None):
        super().__init__('快捷启动栏设置', FIF.LINK, parent)

    def _createSettings(self):
        basic = self._beginGroup('basic')
        (self._enableCard, self._enableSwitch) = self._addSwitch('启用快捷启动栏', cfg.showQuickLaunch, is_advanced=basic)
        edit_btn = PushButton('编辑应用')
        edit_btn.clicked.connect(self._onEditApps)
        self._editCard = self._addButtonRow('应用列表', edit_btn, is_advanced=basic)

        g_icon = self._addGroup('图标', is_advanced=True)
        (self._iconSizeCard, self._iconSizeSpin) = self._addSpinBox('图标大小', cfg.quickLaunchIconSize, 32, 96, group=g_icon)
        (self._spacingCard, self._spacingSpin) = self._addSpinBox('图标间距', cfg.quickLaunchIconSpacing, 4, 40, group=g_icon)

        g_layout = self._addGroup('布局', is_advanced=True)
        (self._labelsCard, self._labelsSwitch) = self._addSwitch('显示名称', cfg.quickLaunchShowLabels, group=g_layout)
        (self._offsetCard, self._offsetSpin) = self._addSpinBox('向上偏移', cfg.quickLaunchOffsetY, 0, 120, group=g_layout)

        self._enableSwitch.checkedChanged.connect(self._updateEnabled)
        self._updateEnabled(cfg.showQuickLaunch.value)

    def _updateEnabled(self, enabled):
        self._iconSizeCard.setEnabled(enabled)
        self._spacingCard.setEnabled(enabled)
        self._labelsCard.setEnabled(enabled)
        self._offsetCard.setEnabled(enabled)

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
