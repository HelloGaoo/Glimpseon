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
开发者面板
"""

import os
import psutil
import time
from PyQt5.QtCore import QTimer, Qt, QPropertyAnimation, QRect, QEvent, QObject, QTime
from PyQt5.QtGui import QColor, QPainter, QFont
from PyQt5.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, 
    QScrollArea, QGridLayout, QApplication, QTextEdit, QFrame
)
from qfluentwidgets import (
    CardWidget, TitleLabel, SubtitleLabel, BodyLabel,
    StrongBodyLabel, InfoBar, InfoBarPosition, ComboBox,
    PrimaryPushButton, PushButton, ToggleButton, SpinBox,
    ProgressBar, ToolTipFilter, ToolTipPosition, FluentIcon as FIF,
    setCustomStyleSheet
)
from core.config import cfg
from core.logger import logger


class DeveloperPanel(QWidget):
    """开发者面板"""
    
    def __init__(self, mainWindow):
        super().__init__(mainWindow)
        self.mainWindow = mainWindow
        self.setObjectName('developerPanel')
        
        # 性能监控
        self.frameCount = 0
        self.lastFpsTime = time.time()
        self.currentFps = 0
        self.process = psutil.Process(os.getpid())
        
        self.lastGeometry = None
        self.lastCurrentWidget = None
        
        # CPU 使用率
        try:
            cpu_times = self.process.cpu_times()
            self.last_cpu_usage = cpu_times.user + cpu_times.system
            self.last_cpu_time = time.time()
        except Exception:
            self.last_cpu_usage = 0
            self.last_cpu_time = time.time()
        
        self.elementCheckEnabled = False
        self.elementCheckOverlay = None
        
        self._initUI()
        self._setupTimers()
        
    def _initUI(self):
        """初始化界面"""
        self.setWindowTitle("开发者面板")
        self.setWindowFlags(Qt.Tool | Qt.WindowStaysOnTopHint)
        self.resize(800, 600)
        mainLayout = QVBoxLayout()
        mainLayout.setSpacing(10)
        mainLayout.setContentsMargins(10, 10, 10, 10)
        scrollArea = QScrollArea()
        scrollArea.setWidgetResizable(True)
        scrollArea.setFrameShape(QFrame.NoFrame)
        scrollWidget = QWidget()
        scrollLayout = QVBoxLayout(scrollWidget)
        scrollLayout.setSpacing(15)
        


        debugCard = self._createDebugCard()
        scrollLayout.addWidget(debugCard)
        
        apiCard = self._createAPITestCard()
        scrollLayout.addWidget(apiCard)
        
        resourceCard = self._createResourceMonitorCard()
        scrollLayout.addWidget(resourceCard)
        
        windowCard = self._createWindowDebugCard()
        scrollLayout.addWidget(windowCard)
        
        elementCard = self._createElementCheckCard()
        scrollLayout.addWidget(elementCard)
        
        scrollArea.setWidget(scrollWidget)
        mainLayout.addWidget(scrollArea)
        
        self.setLayout(mainLayout)
        
        self._loadStyleSheet()
        QTimer.singleShot(500, self._refreshComponentTree)
        
        self.mainWindow.installEventFilter(self)
        
    def _createDebugCard(self):
        """创建调试信息显示卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(16)
        layout.setContentsMargins(16, 16, 16, 16)
        titleLayout = QHBoxLayout()
        iconLabel = QLabel()
        iconLabelPixmap = FIF.DEVELOPER_TOOLS.icon().pixmap(24, 24)
        iconLabel.setPixmap(iconLabelPixmap)
        titleLayout.addWidget(iconLabel)
        title = SubtitleLabel("调试信息", self)
        titleLayout.addWidget(title)
        titleLayout.addStretch()
        layout.addLayout(titleLayout)
        gridLayout = QGridLayout()
        gridLayout.setSpacing(12)
        gridLayout.setHorizontalSpacing(24)
        
        # FPS
        gridLayout.addWidget(StrongBodyLabel("FPS", self), 0, 0)
        self.fpsLabel = BodyLabel("0", self)
        self.fpsLabel.setStyleSheet("font-size: 18px; font-weight: bold;")
        gridLayout.addWidget(self.fpsLabel, 0, 1)
        # 内存占用
        gridLayout.addWidget(StrongBodyLabel("内存", self), 0, 2)
        self.memoryLabel = BodyLabel("0 MB", self)
        self.memoryLabel.setStyleSheet("font-size: 18px; font-weight: bold;")
        gridLayout.addWidget(self.memoryLabel, 0, 3)
        # CPU 使用率
        gridLayout.addWidget(StrongBodyLabel("CPU", self), 1, 0)
        self.cpuLabel = BodyLabel("0%", self)
        self.cpuLabel.setStyleSheet("font-size: 18px; font-weight: bold;")
        gridLayout.addWidget(self.cpuLabel, 1, 1)
        # 窗口状态
        gridLayout.addWidget(StrongBodyLabel("窗口状态", self), 1, 2)
        self.windowStateLabel = BodyLabel("正常", self)
        gridLayout.addWidget(self.windowStateLabel, 1, 3)
        # 界面状态
        gridLayout.addWidget(StrongBodyLabel("界面状态", self), 2, 0)
        self.interfaceStateLabel = BodyLabel("-", self)
        gridLayout.addWidget(self.interfaceStateLabel, 2, 1)
        # 组件数量
        gridLayout.addWidget(StrongBodyLabel("组件数量", self), 2, 2)
        self.componentCountLabel = BodyLabel("0", self)
        gridLayout.addWidget(self.componentCountLabel, 2, 3)
        layout.addLayout(gridLayout)
        # 分隔线
        line = QLabel(self)
        line.setFixedHeight(1)
        line.setStyleSheet("background-color: #3d3d3d;")
        layout.addWidget(line)
        # 实时更新按钮
        self.debugUpdateToggle = ToggleButton("实时更新", self)
        self.debugUpdateToggle.setChecked(True)
        self.debugUpdateToggle.setIcon(FIF.SYNC)
        layout.addWidget(self.debugUpdateToggle)
        
        return card
    
    def _createAPITestCard(self):
        """创建 API 测试工具卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(16)
        layout.setContentsMargins(16, 16, 16, 16)
        titleLayout = QHBoxLayout()
        iconLabel = QLabel()
        iconLabelPixmap = FIF.SETTING.icon().pixmap(24, 24)
        iconLabel.setPixmap(iconLabelPixmap)
        titleLayout.addWidget(iconLabel)
        title = SubtitleLabel("API 测试工具", self)
        titleLayout.addWidget(title)
        titleLayout.addStretch()
        layout.addLayout(titleLayout)
        poetryLayout = QHBoxLayout()
        poetryLayout.addWidget(StrongBodyLabel("一言 API", self))
        poetryLayout.addStretch()
        self.testPoetryButton = PrimaryPushButton("测试", self)
        self.testPoetryButton.setIcon(FIF.PLAY)
        self.testPoetryButton.setFixedWidth(120)
        self.testPoetryButton.clicked.connect(self._testPoetryAPI)
        poetryLayout.addWidget(self.testPoetryButton)
        layout.addLayout(poetryLayout)
        self.poetryResultLabel = BodyLabel("结果：-", self)
        self.poetryResultLabel.setWordWrap(True)
        layout.addWidget(self.poetryResultLabel)
        weatherLayout = QHBoxLayout()
        weatherLayout.addWidget(StrongBodyLabel("天气 API", self))
        weatherLayout.addStretch()
        self.testWeatherButton = PrimaryPushButton("测试", self)
        self.testWeatherButton.setIcon(FIF.PLAY)
        self.testWeatherButton.setFixedWidth(120)
        self.testWeatherButton.clicked.connect(self._testWeatherAPI)
        weatherLayout.addWidget(self.testWeatherButton)
        layout.addLayout(weatherLayout)
        self.weatherResultLabel = BodyLabel("结果：-", self)
        self.weatherResultLabel.setWordWrap(True)
        layout.addWidget(self.weatherResultLabel)
        
        # 分隔线
        line = QLabel(self)
        line.setFixedHeight(1)
        line.setStyleSheet("background-color: #3d3d3d;")
        layout.addWidget(line)
        
        self.rawDataEdit = QTextEdit(self)
        self.rawDataEdit.setPlaceholderText("API 原始响应数据将显示在这里...")
        self.rawDataEdit.setMaximumHeight(150)
        layout.addWidget(self.rawDataEdit)
        
        return card
    
    def _createResourceMonitorCard(self):
        """创建资源监控卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(10)
        title = SubtitleLabel("资源监控", self)
        layout.addWidget(title)
        gridLayout = QGridLayout()
        gridLayout.setSpacing(10)
        
        # 壁纸文件夹大小
        gridLayout.addWidget(BodyLabel("壁纸文件夹:", self), 0, 0)
        self.wallpaperSizeLabel = StrongBodyLabel("0 MB", self)
        gridLayout.addWidget(self.wallpaperSizeLabel, 0, 1)
        # 壁纸文件数量
        gridLayout.addWidget(BodyLabel("壁纸数量:", self), 0, 2)
        self.wallpaperCountLabel = StrongBodyLabel("0", self)
        gridLayout.addWidget(self.wallpaperCountLabel, 0, 3)
        # 日志文件数量
        gridLayout.addWidget(BodyLabel("日志文件:", self), 1, 0)
        self.logFileCountLabel = StrongBodyLabel("0", self)
        gridLayout.addWidget(self.logFileCountLabel, 1, 1)
        # 日志文件夹大小
        gridLayout.addWidget(BodyLabel("日志大小:", self), 1, 2)
        self.logSizeLabel = StrongBodyLabel("0 MB", self)
        gridLayout.addWidget(self.logSizeLabel, 1, 3)
        
        layout.addLayout(gridLayout)
        
        # 清理按钮
        buttonLayout = QHBoxLayout()
        self.clearCacheButton = PushButton("清理缓存", self)
        self.clearCacheButton.clicked.connect(self._clearCache)
        buttonLayout.addWidget(self.clearCacheButton)
        
        self.clearLogsButton = PushButton("清理日志", self)
        self.clearLogsButton.clicked.connect(self._clearLogs)
        buttonLayout.addWidget(self.clearLogsButton)
        
        layout.addLayout(buttonLayout)
        return card
    
    def _createWindowDebugCard(self):
        """创建窗口调试卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(10)
        title = SubtitleLabel("窗口调试", self)
        layout.addWidget(title)
        gridLayout = QGridLayout()
        gridLayout.setSpacing(10)
        
        # 窗口尺寸
        gridLayout.addWidget(BodyLabel("窗口尺寸:", self), 0, 0)
        self.windowSizeLabel = StrongBodyLabel("0x0", self)
        gridLayout.addWidget(self.windowSizeLabel, 0, 1)
        # 窗口位置
        gridLayout.addWidget(BodyLabel("窗口位置:", self), 0, 2)
        self.windowPosLabel = StrongBodyLabel("(0, 0)", self)
        gridLayout.addWidget(self.windowPosLabel, 0, 3)
        # DPI 缩放
        gridLayout.addWidget(BodyLabel("DPI 缩放:", self), 1, 0)
        self.dpiScaleLabel = StrongBodyLabel("1.0", self)
        gridLayout.addWidget(self.dpiScaleLabel, 1, 1)
        # 活动窗口
        gridLayout.addWidget(BodyLabel("活动窗口:", self), 1, 2)
        self.activeWindowLabel = StrongBodyLabel("-", self)
        gridLayout.addWidget(self.activeWindowLabel, 1, 3)
        layout.addLayout(gridLayout)
        # 操作按钮
        buttonLayout = QHBoxLayout()
        self.refreshWindowButton = PushButton("刷新信息", self)
        self.refreshWindowButton.clicked.connect(self._updateWindowDebug)
        buttonLayout.addWidget(self.refreshWindowButton)
        self.forceRepaintButton = PushButton("强制重绘", self)
        self.forceRepaintButton.clicked.connect(self._forceRepaint)
        buttonLayout.addWidget(self.forceRepaintButton)
        layout.addLayout(buttonLayout)
        # 窗口信息详情
        self.windowInfoEdit = QTextEdit(self)
        self.windowInfoEdit.setPlaceholderText("窗口详细信息")
        self.windowInfoEdit.setMaximumHeight(100)
        self.windowInfoEdit.setReadOnly(True)
        layout.addWidget(self.windowInfoEdit)
        
        return card
    
    def _createElementCheckCard(self):
        """创建界面元素检查卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(10)
        title = SubtitleLabel("界面元素", self)
        layout.addWidget(title)
        
        # 启用检查
        enableLayout = QHBoxLayout()
        enableLayout.addWidget(BodyLabel("启用元素检查:", self))
        self.elementCheckToggle = ToggleButton("启用", self)
        self.elementCheckToggle.toggled.connect(self._toggleElementCheck)
        enableLayout.addWidget(self.elementCheckToggle)
        layout.addLayout(enableLayout)
        # 元素信息
        infoLayout = QVBoxLayout()
        infoLayout.addWidget(BodyLabel("当前元素:", self))
        self.elementInfoEdit = QTextEdit(self)
        self.elementInfoEdit.setPlaceholderText("鼠标悬停在元素上查看信息")
        self.elementInfoEdit.setMaximumHeight(150)
        self.elementInfoEdit.setReadOnly(True)
        infoLayout.addWidget(self.elementInfoEdit)
        layout.addLayout(infoLayout)
        # 组件树
        treeLayout = QVBoxLayout()
        treeLayout.addWidget(BodyLabel("组件树:", self))
        self.componentTreeEdit = QTextEdit(self)
        self.componentTreeEdit.setPlaceholderText("组件层级结构")
        self.componentTreeEdit.setMaximumHeight(200)
        self.componentTreeEdit.setReadOnly(True)
        treeLayout.addWidget(self.componentTreeEdit)
        layout.addLayout(treeLayout)
    
        return card
    
    def _setupTimers(self):
        """设置定时器"""
        self.fpsTimer = QTimer(self)
        self.fpsTimer.timeout.connect(self._updateDebugInfo)
        self.fpsTimer.start(100)
        self.resourceTimer = QTimer(self)
        self.resourceTimer.timeout.connect(self._updateResourceMonitor)
        self.resourceTimer.start(10000)
        self.windowTimer = QTimer(self)
        self.windowTimer.timeout.connect(self._updateWindowDebug)
        self.windowTimer.start(2000)
        self.fpsCheckTimer = QTimer(self)
        self.fpsCheckTimer.timeout.connect(self._updateFPS)
        self.fpsCheckTimer.start(16)
        self.changeTimer = QTimer(self)
        self.changeTimer.timeout.connect(self._checkWindowChanges)
        self.changeTimer.start(50)
        
    def _loadStyleSheet(self):
        """加载样式表"""
        try:
            if cfg.themeMode.value == 'dark':
                qss_path = os.path.join(
                    os.path.dirname(__file__),
                    '..', 'resource', 'qss', 'dark', 'developer_panel.qss'
                )
            else:
                qss_path = os.path.join(
                    os.path.dirname(__file__),
                    '..', 'resource', 'qss', 'light', 'developer_panel.qss'
                )
            qss_path = os.path.normpath(qss_path)
            if os.path.exists(qss_path):
                with open(qss_path, 'r', encoding='utf-8') as f:
                    qss_content = f.read()
                self.setStyleSheet(qss_content)
                for widget in self.findChildren(QWidget):
                    widget.setStyleSheet(qss_content)
                logger.info(f"开发者面板样式已加载：{qss_path}")
            else:
                logger.warning(f"样式文件不存在：{qss_path}")
        except Exception as e:
            logger.error(f"加载样式失败：{e}")
    
    def _updateTheme(self):
        """更新主题"""
        self._loadStyleSheet()
    
    
    def eventFilter(self, obj, event):
        """事件过滤器 - 用于 FPS 计数"""
        #Paint
        if obj == self.mainWindow and event.type() == QEvent.Paint and self.debugUpdateToggle.isChecked():
            self.frameCount += 1
            currentTime = time.time()
            if currentTime - self.lastFpsTime >= 0.5:
                self.currentFps = self.frameCount / (currentTime - self.lastFpsTime)
                self.fpsLabel.setText(f"{self.currentFps:.1f}")
                self.frameCount = 0
                self.lastFpsTime = currentTime
        return QObject.eventFilter(self, obj, event)
    
    def _updateFPS(self):
        """更新FPS"""
        if not self.debugUpdateToggle.isChecked():
            return
        if self.mainWindow.isVisible():
            self.frameCount += 1
            currentTime = time.time()
            if currentTime - self.lastFpsTime >= 0.5:
                self.currentFps = self.frameCount / (currentTime - self.lastFpsTime)
                self.fpsLabel.setText(f"{self.currentFps:.1f}")
                self.frameCount = 0
                self.lastFpsTime = currentTime
    
    def _checkWindowChanges(self):
        """检测窗口变化来估算 FPS"""
        if not self.debugUpdateToggle.isChecked():
            return
        current_widget = self.mainWindow.stackedWidget.currentWidget()
        widget_changed = False
        if current_widget != self.lastCurrentWidget:
            self.lastCurrentWidget = current_widget
            widget_changed = True
        current_geometry = self.mainWindow.geometry()
        geometry_changed = False
        if current_geometry != self.lastGeometry:
            self.lastGeometry = current_geometry
            geometry_changed = True
        if widget_changed or geometry_changed:
            self.frameCount += 1
            currentTime = time.time()
            if currentTime - self.lastFpsTime >= 0.5:
                self.currentFps = self.frameCount / (currentTime - self.lastFpsTime)
                self.fpsLabel.setText(f"{self.currentFps:.1f}")
                self.frameCount = 0
                self.lastFpsTime = currentTime
    
    def _updateDebugInfo(self):
        """更新信息"""
        if not self.debugUpdateToggle.isChecked():
            return
        
        currentTime = time.time()
        
        # 内存占用
        try:
            mem_info = self.process.memory_info()
            mem_mb = mem_info.rss / 1024 / 1024
            self.memoryLabel.setText(f"{mem_mb:.1f} MB")
        except Exception:
            self.memoryLabel.setText("N/A")
        
        # CPU 使用
        try:
            cpu_times = self.process.cpu_times()
            process_cpu = cpu_times.user + cpu_times.system
            current_time = time.time()
            time_delta = current_time - self.last_cpu_time
            cpu_delta = process_cpu - self.last_cpu_usage
            if time_delta > 0:
                cpu_count = psutil.cpu_count() or 1
                cpu_percent = (cpu_delta / time_delta) * 100 / cpu_count
                cpu_percent = min(cpu_percent, 100.0)
                self.cpuLabel.setText(f"{cpu_percent:.1f}%")
            else:
                self.cpuLabel.setText("0.0%")
            self.last_cpu_time = current_time
            self.last_cpu_usage = process_cpu
        except Exception:
            self.cpuLabel.setText("N/A")
        
        if self.mainWindow.isVisible():
            self.windowStateLabel.setText("可见")
        else:
            self.windowStateLabel.setText("隐藏")
        
        # 界面状态
        current_widget = self.mainWindow.stackedWidget.currentWidget()
        if current_widget:
            self.interfaceStateLabel.setText(current_widget.objectName())
        
        # 组件数量
        component_count = len(self.mainWindow.findChildren(QWidget))
        self.componentCountLabel.setText(str(component_count))
    
    def _testPoetryAPI(self):
        """测试一言 API"""
        import requests
        
        start_time = time.time()
        try:
            timeout = 10
            
            api_url = cfg.poetryApiUrl.value
            response = requests.get(api_url, timeout=timeout)
            elapsed = (time.time() - start_time) * 1000
            
            self.poetryResultLabel.setText(f"✓ 成功 ({elapsed:.0f}ms): {response.text[:50]}")
            self.rawDataEdit.setText(response.text)
            
            InfoBar.success(
                title="API 测试",
                content="一言 API 测试成功",
                parent=self,
                duration=2000
            )
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.poetryResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"一言 API 测试失败：{e}")
            
            InfoBar.error(
                title="API 测试",
                content=f"一言 API 测试失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    def _testWeatherAPI(self):
        """测试天气 API"""
        from services.weather import WeatherService
        from ui.city_selector import RegionDatabase
        
        start_time = time.time()
        try:
            city_name = cfg.city.value if hasattr(cfg, 'city') and cfg.city.value else "北京"
            city_db = RegionDatabase()
            city_code = city_db.get_code(city_name)
            if not city_code:city_code = "101010100"
            
            weather_service = WeatherService(city_code)
            
            weather_data = weather_service.get_weather()
            elapsed = (time.time() - start_time) * 1000
            if weather_data:
                self.weatherResultLabel.setText(f"✓ 成功 ({elapsed:.0f}ms): {weather_data['weather_text']} {weather_data['temperature']}")
                self.rawDataEdit.setText(f"天气数据:\n温度：{weather_data['temperature']}\n天气：{weather_data['weather_text']}\n天气代码：{weather_data['weather_code']}\n图标：{weather_data['weather_icon']}")
                InfoBar.success(
                    title="API 测试",
                    content=f"天气 API 测试成功 - {weather_data['weather_text']} {weather_data['temperature']}",
                    parent=self,
                    duration=2000
                )
            else:
                self.weatherResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): 未获取到天气数据")
                self.rawDataEdit.setText("天气 API 返回数据为空")
                InfoBar.warning(
                    title="API 测试",
                    content="天气 API 测试失败 - 未获取到天气数据",
                    parent=self,
                    duration=3000
                )
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.weatherResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"天气 API 测试失败：{e}")
            InfoBar.error(
                title="API 测试",
                content=f"天气 API 测试失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    
    def _updateResourceMonitor(self):
        """更新资源监控"""
        try:
            wallpaper_dir = os.path.join(os.path.dirname(__file__), '..', 'wallpaper')
            wallpaper_dir = os.path.normpath(wallpaper_dir)
     
            if os.path.exists(wallpaper_dir):
                total_size = 0
                file_count = 0
                for root, dirs, files in os.walk(wallpaper_dir):
                    for f in files:
                        fp = os.path.join(root, f)
                        try:
                            total_size += os.path.getsize(fp)
                            file_count += 1
                        except Exception:
                            pass
                
                self.wallpaperSizeLabel.setText(f"{total_size / 1024 / 1024:.1f} MB")
                self.wallpaperCountLabel.setText(str(file_count))
            

            log_dir = os.path.join(os.path.dirname(__file__), '..', 'logs')
            log_dir = os.path.normpath(log_dir)
            
            if os.path.exists(log_dir):
                total_size = 0
                file_count = 0
                for root, dirs, files in os.walk(log_dir):
                    for f in files:
                        fp = os.path.join(root, f)
                        try:
                            total_size += os.path.getsize(fp)
                            file_count += 1
                        except Exception:
                            pass
                
                self.logFileCountLabel.setText(str(file_count))
                self.logSizeLabel.setText(f"{total_size / 1024 / 1024:.1f} MB")
        except Exception as e:
            logger.error(f"更新资源监控失败：{e}")
    
    def _clearCache(self):
        """清理缓存"""
        try:
            wallpaper_dir = os.path.join(os.path.dirname(__file__), '..', 'wallpaper')
            wallpaper_dir = os.path.normpath(wallpaper_dir)

            if os.path.exists(wallpaper_dir):
                deleted_count = 0
                deleted_size = 0
                for root, dirs, files in os.walk(wallpaper_dir):
                    for f in files:
                        fp = os.path.join(root, f)
                        try:
                            # 获取文件大小
                            deleted_size += os.path.getsize(fp)
                            os.remove(fp)
                            deleted_count += 1
                        except Exception as e:
                            logger.warning(f"删除壁纸文件失败：{fp}, {e}")
                
                # 更新监控数据
                self._updateResourceMonitor()
                
                InfoBar.success(
                    title="清理缓存",
                    content=f"已清理 {deleted_count} 个文件，释放 {deleted_size / 1024:.1f} KB 空间",
                    parent=self,
                    duration=3000
                )
            else:
                InfoBar.info(
                    title="清理缓存",
                    content="壁纸文件夹不存在",
                    parent=self,
                    duration=2000
                )
        except Exception as e:
            logger.error(f"清理缓存失败：{e}")
            InfoBar.error(
                title="清理缓存",
                content=f"清理缓存失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    def _clearLogs(self):
        """清理日志"""
        try:
            log_dir = os.path.join(os.path.dirname(__file__), '..', 'logs')
            log_dir = os.path.normpath(log_dir)
            
            if os.path.exists(log_dir):
                for root, dirs, files in os.walk(log_dir):
                    for f in files:
                        if f.endswith('.log'):
                            fp = os.path.join(root, f)
                            try:
                                os.remove(fp)
                            except Exception:
                                pass
                
                InfoBar.success(
                    title="清理日志",
                    content="日志已清理",
                    parent=self,
                    duration=2000
                )
                self._updateResourceMonitor()
        except Exception as e:
            logger.error(f"清理日志失败：{e}")
            InfoBar.error(
                title="清理日志",
                content=f"清理日志失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    def _updateWindowDebug(self):
        """更新窗口调试信息"""
        try:
            # 窗口尺寸
            size = self.mainWindow.size()
            self.windowSizeLabel.setText(f"{size.width()}x{size.height()}")
            # 窗口位置
            pos = self.mainWindow.pos()
            self.windowPosLabel.setText(f"({pos.x()}, {pos.y()})")
            # DPI缩放
            dpi = self.mainWindow.logicalDpiX()
            dpi_scale = dpi / 96.0
            self.dpiScaleLabel.setText(f"{dpi_scale:.2f}")
            # 活动窗口
            active_window = QApplication.activeWindow()
            if active_window:
                self.activeWindowLabel.setText(active_window.objectName())
            else:
                self.activeWindowLabel.setText("无")
            info = []
            info.append(f"窗口标题：{self.mainWindow.windowTitle()}")
            info.append(f"窗口状态：{'正常' if self.mainWindow.isVisible() else '隐藏'}")
            info.append(f"窗口激活：{'是' if self.mainWindow.isActiveWindow() else '否'}")
            info.append(f"窗口焦点：{'是' if self.mainWindow.hasFocus() else '否'}")
            info.append(f"窗口置顶：{'是' if self.mainWindow.windowFlags() & Qt.WindowStaysOnTopHint else '否'}")
            
            self.windowInfoEdit.setText("\n".join(info))
        except Exception as e:
            logger.error(f"更新窗口调试信息失败：{e}")
    
    def _forceRepaint(self):
        """强制重绘"""
        try:
            self.mainWindow.update()
            self.mainWindow.repaint()
            
            InfoBar.success(
                title="重绘",
                content="窗口已强制重绘",
                parent=self,
                duration=1500
            )
        except Exception as e:
            logger.error(f"强制重绘失败：{e}")
            InfoBar.error(
                title="重绘",
                content=f"强制重绘失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    def _toggleElementCheck(self, enabled):
        """切换元素检查"""
        self.elementCheckEnabled = enabled
        
        if enabled:
            QApplication.instance().installEventFilter(self)
            InfoBar.success(
                title="元素检查",
                content="元素检查已启用，鼠标悬停在元素上查看信息",
                parent=self,
                duration=3000
            )
        else:
            QApplication.instance().removeEventFilter(self)
            InfoBar.info(
                title="元素检查",
                content="元素检查已禁用",
                parent=self,
                duration=2000
            )
    
    def eventFilter(self, obj, event):
        """元素检查过滤"""
        if not self.elementCheckEnabled:
            return super().eventFilter(obj, event)
        
        from PyQt5.QtCore import QEvent
        from PyQt5.QtWidgets import QWidget
        
        if event.type() == QEvent.Enter:
            element_info = []
            element_info.append(f"对象名称：{obj.objectName()}")
            element_info.append(f"类    型：{obj.__class__.__name__}")
            element_info.append(f"可    见：{obj.isVisible()}")
            if isinstance(obj, QWidget):element_info.append(f"启    用：{obj.isEnabled()}")
            
            if hasattr(obj, 'geometry'):
                geom = obj.geometry()
                element_info.append(f"位    置：({geom.x()}, {geom.y()})")
                element_info.append(f"大    小：{geom.width()}x{geom.height()}")
            
            self.elementInfoEdit.setText("\n".join(element_info))
        
        return super().eventFilter(obj, event)
    
    def _refreshComponentTree(self):
        """刷新组件树"""
        try:
            tree_lines = []
            def get_widget_tree(widget, indent=0):
                lines = []
                prefix = "  " * indent
                widget_name = widget.objectName() or widget.__class__.__name__
                lines.append(f"{prefix}├─ {widget_name}")
                for child in widget.children():
                    if isinstance(child, QWidget):
                        lines.extend(get_widget_tree(child, indent + 1))
                return lines
            
            tree_lines = get_widget_tree(self.mainWindow)
            self.componentTreeEdit.setText("\n".join(tree_lines))
        except Exception as e:
            logger.error(f"刷新组件树失败：{e}")
    
    def _findComponent(self):
        """查找组件"""
        pass
