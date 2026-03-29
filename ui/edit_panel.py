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
编辑面板模块
"""
from PyQt5.QtCore import QPropertyAnimation, QRect, Qt
from PyQt5.QtGui import QColor, QPalette
from PyQt5.QtWidgets import (
    QHBoxLayout, QLabel, QLineEdit, QListWidget, 
    QPushButton, QVBoxLayout, QWidget
)
from qfluentwidgets import (
    BodyLabel, FluentIcon as FIF, isDarkTheme, LineEdit, ListWidget, 
    PrimaryPushButton, PushButton, StrongBodyLabel, ToolButton
)

from .movable_widget import MovableWidget


class EditPanel(QWidget):
    """编辑面板"""
    
    def __init__(self, mainWindow, width=300):
        """初始化编辑面板"""
        super().__init__(parent=mainWindow)
        self.mainWindow = mainWindow
        self._width = width
        self.setFixedWidth(self._width)
        self.setObjectName('EditPanel')
        self.isLeftSide = False  # 是否在左侧显示
        
        # 设置不透明背景
        self.setAttribute(Qt.WA_StyledBackground, True)
        self.setAutoFillBackground(True)
        self._updateTheme()
        
        # 主布局
        v = QVBoxLayout(self)
        v.setContentsMargins(16, 16, 16, 16)
        v.setSpacing(12)
        
        titleLayout = QHBoxLayout()
        titleLabel = StrongBodyLabel('组件库', self)
        titleLayout.addWidget(titleLabel)
        titleLayout.addStretch()
        
        self.positionButton = ToolButton(parent=self)
        self.positionButton.setFixedSize(32, 32)
        self.positionButton.setToolTip('切换到左侧')
        self.positionButton.setIcon(FIF.CARE_LEFT_SOLID)
        self.positionButton.clicked.connect(self._togglePosition)
        titleLayout.addWidget(self.positionButton)
        
        v.addLayout(titleLayout)
        
        # 组件列表
        self.list = ListWidget(self)
        self.list.setFixedHeight(120)
        
        # 添加列表项
        for t in ('时钟', '天气', '诗词'):
            self.list.addItem(t)
        
        v.addWidget(self.list)
        
        # 添加按钮
        self.addButton = PrimaryPushButton('添加', self, icon=FIF.ADD)
        self.addButton.setFixedHeight(32)
        v.addWidget(self.addButton)
        
        # 分隔区域
        v.addSpacing(8)
        
        # 文本属性
        self.propLabel = StrongBodyLabel('文本属性', self)
        self.propLabel.setObjectName('propLabel')
        v.addWidget(self.propLabel)
        self.propEdit = LineEdit(self)
        self.propEdit.setPlaceholderText('输入文本内容')
        self.propEdit.setFixedHeight(32)
        v.addWidget(self.propEdit)
        
        # 大小配置
        self.sizeLabel = StrongBodyLabel('大小配置', self)
        self.sizeLabel.setObjectName('sizeLabel')
        v.addWidget(self.sizeLabel)
        
        # 大小输入布局
        sizeWidget = QWidget()
        sizeLayout = QHBoxLayout(sizeWidget)
        sizeLayout.setContentsMargins(0, 0, 0, 0)
        sizeLayout.setSpacing(8)
        
        self.widthLabel = BodyLabel('宽度:', self)
        self.widthEdit = LineEdit(self)
        self.widthEdit.setPlaceholderText('宽度')
        self.widthEdit.setFixedHeight(32)
        
        self.heightLabel = BodyLabel('高度:', self)
        self.heightEdit = LineEdit(self)
        self.heightEdit.setPlaceholderText('高度')
        self.heightEdit.setFixedHeight(32)
        
        sizeLayout.addWidget(self.widthLabel)
        sizeLayout.addWidget(self.widthEdit, 1)
        sizeLayout.addWidget(self.heightLabel)
        sizeLayout.addWidget(self.heightEdit, 1)
        
        v.addWidget(sizeWidget)
        
        self.deleteButton = PushButton('删除', self, icon=FIF.DELETE)
        self.deleteButton.setFixedHeight(32)
        v.addWidget(self.deleteButton)
        
        v.addStretch()
        
        self.closeButton = PushButton('关闭', self, icon=FIF.CLOSE)
        self.closeButton.setFixedHeight(32)
        v.addWidget(self.closeButton)
        
        self.addButton.clicked.connect(self._onAdd)
        self.deleteButton.clicked.connect(self._onDelete)
        self.closeButton.clicked.connect(self.hidePanel)
        self.propEdit.textChanged.connect(self._onPropEdit)
        self.widthEdit.textChanged.connect(self._onSizeEdit)
        self.heightEdit.textChanged.connect(self._onSizeEdit)
        
        self.anim = QPropertyAnimation(self, b'geometry')
        
        self.hide()
        self.setVisible(False)
    
    def _updateTheme(self):
        """更新主题"""
        if isDarkTheme():
            self.setStyleSheet("""
                #EditPanel {
                    background-color: rgb(32, 32, 32);
                    border-radius: 12px;
                }
            """)
        else:
            self.setStyleSheet("""
                #EditPanel {
                    background-color: rgb(255, 255, 255);
                    border-radius: 12px;
                }
            """)
    
    def _updateListStyle(self):
        """更新列表样式"""
        palette = self.list.palette()
        if isDarkTheme():
            palette.setColor(QPalette.Base, QColor(40, 40, 40))
            palette.setColor(QPalette.Text, QColor(255, 255, 255))
        else:
            palette.setColor(QPalette.Base, QColor(245, 245, 245))
            palette.setColor(QPalette.Text, QColor(0, 0, 0))
        
        self.list.setPalette(palette)

    def showPanel(self):
        """显示编辑面板"""
        parent = self.parent()
        if not parent:
            return
        
        # 更新主题
        self._updateTheme()
        self._updateListStyle()
        
        self.show()

        pr = parent.rect()
        if self.isLeftSide:
            end_rect = QRect(0, 0, self._width, pr.height())
            start_rect = QRect(-self._width, 0, self._width, pr.height())
        else:
            end_rect = QRect(pr.width() - self._width, 0, self._width, pr.height())
            start_rect = QRect(pr.width(), 0, self._width, pr.height())
        
        self.setGeometry(start_rect)
        
        try:
            self.anim.finished.disconnect(self._onHideFinished)
        except Exception:
            pass
        self.anim.stop()
        self.anim.setDuration(220)
        self.anim.setStartValue(start_rect)
        self.anim.setEndValue(end_rect)
        self.anim.start()

    def hidePanel(self):
        """隐藏编辑面板"""
        parent = self.parent()
        if not parent:
            return
        
        if hasattr(parent, 'deselectComponent'):
            parent.deselectComponent()
        
        # 禁用拖动
        if hasattr(parent, 'homeContent'):
            for widget in parent.homeContent.findChildren(MovableWidget):
                widget.isDraggable = False
        
        # 动画起始和结束位置
        pr = parent.rect()
        if self.isLeftSide:
            start_rect = QRect(0, 0, self._width, pr.height())
            end_rect = QRect(-self._width, 0, self._width, pr.height())
        else:
            start_rect = QRect(pr.width() - self._width, 0, self._width, pr.height())
            end_rect = QRect(pr.width(), 0, self._width, pr.height())
        
        # 滑出动画
        self.anim.stop()
        self.anim.setDuration(180)
        self.anim.setStartValue(start_rect)
        self.anim.setEndValue(end_rect)
        
        try:
            self.anim.finished.disconnect(self._onHideFinished)
        except Exception:
            pass
        self.anim.finished.connect(self._onHideFinished)
        self.anim.start()

    def _onHideFinished(self):
        try:
            self.hide()
        finally:
            try:
                self.anim.finished.disconnect(self._onHideFinished)
            except Exception:
                pass

    def _onAdd(self):
        """添加组件"""
        item = self.list.currentItem()
        t = item.text() if item else '时钟'
        if hasattr(self.mainWindow, 'addComponent'):
            self.mainWindow.addComponent(t)

    def _onDelete(self):
        """删除组件"""
        if hasattr(self.mainWindow, 'deleteSelectedComponent'):
            self.mainWindow.deleteSelectedComponent()

    def _onPropEdit(self, txt):
        """文本属性编辑"""
        if hasattr(self.mainWindow, 'applyPropertyChanges'):
            self.mainWindow.applyPropertyChanges(txt)
    
    def _onSizeEdit(self):
        """大小输入框的变化"""
        if hasattr(self.mainWindow, 'selectedComponent') and self.mainWindow.selectedComponent:
            try:
                # 获取输入的宽度和高度
                width = int(self.widthEdit.text()) if self.widthEdit.text().isdigit() else self.mainWindow.selectedComponent.width()
                height = int(self.heightEdit.text()) if self.heightEdit.text().isdigit() else self.mainWindow.selectedComponent.height()
                width = max(50, width)
                height = max(50, height)
                # 调整组件大小
                self.mainWindow.selectedComponent.resize(width, height)
            except Exception:
                pass
    
    def updateListItemColors(self):
        """主题切换时调用"""
        self._updateTheme()
        self._updateListStyle()
    
    def _togglePosition(self):
        """切换编辑面板位置"""
        self.isLeftSide = not self.isLeftSide
        if self.isLeftSide:
            self.positionButton.setIcon(FIF.CARE_RIGHT_SOLID)
            self.positionButton.setToolTip('切换到右侧')
        else:
            self.positionButton.setIcon(FIF.CARE_LEFT_SOLID)
            self.positionButton.setToolTip('切换到左侧')

        if self.isVisible():
            self.showPanel()
