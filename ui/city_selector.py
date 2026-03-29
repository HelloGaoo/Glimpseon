# ClassLively
# Copyright (C) 2026 HelloGaoo & WHYOS
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

"""
城市选择器模块
"""

from PyQt5.QtCore import Qt, QCoreApplication
from PyQt5.QtWidgets import QWidget
from qfluentwidgets import (
    MessageBoxBase, SearchLineEdit, ListWidget, SubtitleLabel, BodyLabel
)
import sqlite3
import os
import sys
from core.config import cfg

# 路径配置
if getattr(sys, 'frozen', False):
    # 打包为 exe 时
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
    MEIPASS_DIR = sys._MEIPASS
else:
    # 脚本运行时 - 从 ui/ 目录向上两级到项目根目录
    BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    MEIPASS_DIR = None

def get_resource_path(relative_path):
    """获取资源文件的绝对路径"""
    # 先检查 BASE_DIR 中的资源文件
    base_path = os.path.join(BASE_DIR, relative_path)
    if os.path.exists(base_path):
        return base_path
    # 如果 BASE_DIR 中不存在，检查 MEIPASS_DIR
    if MEIPASS_DIR:
        meipass_path = os.path.join(MEIPASS_DIR, relative_path)
        if os.path.exists(meipass_path):
            return meipass_path
    # 如果都不存在，返回 BASE_DIR 中的路径
    return base_path


class RegionDatabase:
    """
    地区数据管理器
    负责从 SQLite 数据库中查询地区信息
    """
    
    def __init__(self):
        """初始化数据库路径"""
        self._db_path = get_resource_path(os.path.join('data', 'xiaomi_weather.db'))
        if not os.path.exists(self._db_path):
            self._db_path = os.path.join(BASE_DIR, 'data', 'xiaomi_weather.db')
    
    def _connect(self):
        """创建数据库连接"""
        return sqlite3.connect(self._db_path)
    
    def search(self, keyword=None):
        """
        搜索地区
        :param keyword: 搜索关键词，为空时返回所有地区
        :return: 地区名称列表
        """
        try:
            if not os.path.exists(self._db_path):
                return []
            
            with self._connect() as conn:
                cursor = conn.cursor()
                if keyword is None or len(keyword.strip()) == 0:
                    cursor.execute('SELECT name FROM citys ORDER BY name')
                else:
                    cursor.execute(
                        'SELECT name FROM citys WHERE name LIKE ? ORDER BY name',
                        ('%' + keyword + '%',)
                    )
                return [row[0] for row in cursor.fetchall()]
        except Exception as err:
            print(f'搜索地区出错：{err}')
            return []
    
    def get_code(self, region_name):
        """
        根据地区名获取地区代码
        :param region_name: 地区名称
        :return: 地区代码字符串
        """
        try:
            if not os.path.exists(self._db_path):
                return ''
            
            with self._connect() as conn:
                cursor = conn.cursor()
                cursor.execute('SELECT city_num FROM citys WHERE name = ?', (region_name,))
                result = cursor.fetchone()
                return result[0] if result else ''
        except Exception as err:
            print(f'获取地区代码失败：{err}')
            return ''
    
    def get_name(self, region_code):
        """
        根据地区代码获取地区名
        :param region_code: 地区代码
        :return: 地区名称
        """
        try:
            if not os.path.exists(self._db_path):
                return ''
            
            # 移除前缀
            code = region_code
            if code and code.startswith('weathercn:'):
                code = code[10:]
            
            with self._connect() as conn:
                cursor = conn.cursor()
                cursor.execute('SELECT name FROM citys WHERE city_num LIKE ?', ('%' + code + '%',))
                result = cursor.fetchone()
                return result[0] if result else ''
        except Exception as err:
            print(f'通过代码获取地区名失败：{err}')
            return ''


class RegionSelectorDialog(MessageBoxBase):
    """
    地区选择对话框
    提供搜索和选择地区的功能
    """
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self._database = RegionDatabase()
        self._init_ui()
        self._select_current()
    
    def _init_ui(self):
        """初始化用户界面"""
        # 标题
        title = SubtitleLabel()
        title.setText(QCoreApplication.translate('RegionSelector', '选择地区'))
        
        # 搜索输入框
        self._search_input = SearchLineEdit()
        self._search_input.setPlaceholderText(
            QCoreApplication.translate('RegionSelector', '请输入地区名')
        )
        self._search_input.setClearButtonEnabled(True)
        self._search_input.textChanged.connect(self._on_search)
        
        # 地区列表
        self._region_list = ListWidget()
        self._refresh_list()
        self._region_list.itemDoubleClicked.connect(self._on_double_click)
        
        # 添加到主布局
        self.viewLayout.addWidget(title)
        self.viewLayout.addWidget(self._search_input)
        self.viewLayout.addWidget(self._region_list)
        
        # 按钮文字
        self.yesButton.setText(QCoreApplication.translate('RegionSelector', '确定'))
        self.cancelButton.setText(QCoreApplication.translate('RegionSelector', '取消'))
        
        # 窗口大小
        self.widget.setMinimumWidth(520)
        self.widget.setMinimumHeight(620)
    
    def _refresh_list(self, keyword=None):
        """刷新地区列表"""
        self._region_list.clear()
        regions = self._database.search(keyword)
        self._region_list.addItems(regions)
        self._region_list.clearSelection()
    
    def _on_search(self, text):
        """搜索框内容变化处理"""
        self._refresh_list(text if text.strip() else None)
    
    def _on_double_click(self, item):
        """双击列表项处理"""
        self.yesButton.click()
    
    def _select_current(self):
        """选中当前配置的地区"""
        try:
            current = cfg.city.value
            if current:
                items = self._region_list.findItems(current, Qt.MatchExactly)
                if items:
                    self._region_list.setCurrentItem(items[0])
                    self._region_list.scrollToItem(items[0])
        except Exception as err:
            print(f'选中当前地区失败：{err}')
    
    def get_selected_region(self):
        """获取用户选中的地区"""
        selected = self._region_list.selectedItems()
        return selected[0].text() if selected else None
