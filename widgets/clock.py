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
时钟组件模块
"""

from PyQt5.QtWidgets import QWidget, QLabel, QVBoxLayout
from PyQt5.QtCore import QTimer, QTime, QDate, Qt
from typing import Dict, Any, Optional
import datetime
import cnlunar

from widgets.base import ContainerComponent


class ClockComponent(ContainerComponent):
    """时钟组件类"""
    
    COMPONENT_TYPE = "时钟"
    
    def __init__(self, parent: Optional[QWidget] = None, 
                 show_seconds: bool = True,
                 show_lunar: bool = True,
                 clock_size: int = 48,
                 date_size: int = 16,
                 color: str = "#FFFFFF"):
        """
        初始化时钟组件
        
        Args:
            parent: 父组件
            show_seconds: 是否显示秒
            show_lunar: 是否显示农历
            clock_size: 时钟字体大小
            date_size: 日期字体大小
            color: 字体颜色
        """
        super().__init__(parent)
        self.show_seconds = show_seconds
        self.show_lunar = show_lunar
        self.clock_size = clock_size
        self.date_size = date_size
        self.color = color
        
        self.clock_label: Optional[QLabel] = None
        self.date_label: Optional[QLabel] = None
        self.timer: Optional[QTimer] = None
    
    def create_widget(self) -> QWidget:
        """创建时钟 UI"""
        container = self._create_container('vertical')
        
        # 时钟标签
        self.clock_label = QLabel("00:00:00")
        self.clock_label.setAlignment(Qt.AlignCenter)
        self.clock_label.setStyleSheet(f"""
            color: {self.color};
            font-size: {self.clock_size}px;
            font-weight: bold;
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        
        # 日期标签
        self.date_label = QLabel("")
        self.date_label.setAlignment(Qt.AlignCenter)
        self.date_label.setStyleSheet(f"""
            color: {self.color};
            font-size: {self.date_size}px;
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        
        self.add_widget_to_layout(self.clock_label)
        self.add_widget_to_layout(self.date_label)
        
        self.widget = container
        return container
    
    def update_content(self):
        """更新时间"""
        if not self.clock_label or not self.date_label:
            return
        
        current_time = QTime.currentTime()
        current_date = QDate.currentDate()
        
        # 更新时间
        if self.show_seconds:
            time_string = current_time.toString("HH:mm:ss")
        else:
            time_string = current_time.toString("HH:mm")
        self.clock_label.setText(time_string)
        
        # 更新日期
        solar_string = current_date.toString("yyyy 年 M 月 d 日 dddd")
        
        if self.show_lunar:
            try:
                py_datetime = datetime.datetime(
                    current_date.year(),
                    current_date.month(),
                    current_date.day(),
                    0, 0, 0
                )
                lunar = cnlunar.Lunar(py_datetime)
                lunar_month = lunar.lunarMonthCn.replace("大", "").replace("小", "")
                lunar_day = lunar.lunarDayCn
                lunar_string = f"{lunar_month}{lunar_day}"
                date_string = f"{solar_string} {lunar_string}"
            except Exception as e:
                date_string = solar_string
        else:
            date_string = solar_string
        
        self.date_label.setText(date_string)
    
    def start_timer(self):
        """启动定时器"""
        if self.timer:
            self.timer.stop()
            self.timer.deleteLater()
        
        self.timer = QTimer()
        self.timer.timeout.connect(self.update_content)
        self.timer.start(1000)  # 每秒更新一次
        
        # 立即更新一次
        self.update_content()
    
    def stop_timer(self):
        """停止定时器"""
        if self.timer:
            self.timer.stop()
    
    def get_config(self) -> Dict[str, Any]:
        """获取组件配置"""
        return {
            'type': self.COMPONENT_TYPE,
            'show_seconds': self.show_seconds,
            'show_lunar': self.show_lunar,
            'clock_size': self.clock_size,
            'date_size': self.date_size,
            'color': self.color
        }
    
    def load_config(self, config: Dict[str, Any]):
        """加载组件配置"""
        self.show_seconds = config.get('show_seconds', True)
        self.show_lunar = config.get('show_lunar', True)
        self.clock_size = config.get('clock_size', 48)
        self.date_size = config.get('date_size', 16)
        self.color = config.get('color', "#FFFFFF")
    
    def cleanup(self):
        """清理资源"""
        self.stop_timer()
        super().cleanup()
