# Glimpseon
# Copyright (C) 2026 HelloGaoo
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

"""主界面模块"""

import datetime
import os
import re
import time

import shiboken6

from PyQt6.QtCore import (
    QDate,
    QEasingCurve,
    QEvent,
    QPropertyAnimation,
    QRect,
    QRectF,
    QPointF,
    QPoint,
    QSize,
    Qt,
    QTime,
    QTimer,
    pyqtSignal,
)
from PyQt6.QtGui import (
    QBrush,
    QColor,
    QIcon,
    QImage,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
)
from PyQt6.QtWidgets import (
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
    QFileDialog,
)
from qfluentwidgets import (
    InfoBar,
    PushButton,
    MessageBoxBase,
    CalendarPicker,
    TimePicker,
    ComboBox,
    LineEdit,
    ToolButton,
    StrongBodyLabel,
    BodyLabel,
    SubtitleLabel,
    SmoothScrollArea,
    CardWidget,
    RoundMenu,
    Action,
    isDarkTheme,
)

from core.config import cfg, save_cfg
from core.constants import PACKAGE_ROOT, DATA_CONFIG, load_qss, FONT_FAMILY
from core.component import PageManager, GridLayoutService, GridSettings, ComponentRegistry, BUILTIN_COMPONENT_DEFINITIONS
from core.logger import logger
from core.utils import tr, TranslatableWidget, precise_now, FUI
from resource.software_list import get_software_icon_path
from ui.component import DraggableContainer, QuickLaunchDock, resolve_app_from_path


class PageIndicator(QWidget):
    """底部页面圆点"""

    pageClicked = pyqtSignal(int)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("pageIndicator")
        self.setMouseTracking(True)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self._count = 0
        self._current = 0
        self._dot_radius = 4
        self._dot_radius_active = 6
        self._dot_gap = 10

        theme_color = cfg.themeColor.value
        if isinstance(theme_color, str):
            self._active_color = QColor(theme_color)
        else:
            self._active_color = theme_color
        try:
            cfg.themeColor.valueChanged.connect(self._onThemeColorChanged)
        except Exception:
            pass

    def _onThemeColorChanged(self):
        c = cfg.themeColor.value
        if isinstance(c, str):
            self._active_color = QColor(c)
        else:
            self._active_color = c
        self.update()

    def set_count(self, count: int):
        self._count = max(0, count)
        if self._current >= self._count:
            self._current = max(0, self._count - 1)
        self._updateGeometry()
        self.update()

    def set_current(self, index: int):
        if 0 <= index < self._count:
            self._current = index
            self.update()

    def _updateGeometry(self):
        w = self._count * (self._dot_radius * 2) + max(0, self._count - 1) * self._dot_gap
        h = self._dot_radius_active * 2 + 4
        self.setFixedSize(max(w + 8, 16), h)

    def sizeHint(self):
        w = self._count * (self._dot_radius * 2) + max(0, self._count - 1) * self._dot_gap
        h = self._dot_radius_active * 2 + 4
        return QSize(max(w + 8, 16), h)

    def _dotRect(self, i: int) -> QRectF:
        r = self._dot_radius
        r_active = self._dot_radius_active
        total_w = self._count * (r * 2) + max(0, self._count - 1) * self._dot_gap
        start_x = (self.width() - total_w) / 2
        x = start_x + i * (r * 2 + self._dot_gap) + r
        y = self.height() / 2
        radius = r_active if i == self._current else r
        return QRectF(x - radius, y - radius, radius * 2, radius * 2)

    def _hitTest(self, pos: QPoint) -> int:
        for i in range(self._count):
            if self._dotRect(i).contains(QPointF(pos)):
                return i
        return -1

    def mousePressEvent(self, event):
        if event.button() in (Qt.MouseButton.LeftButton, Qt.MouseButton.RightButton):
            idx = self._hitTest(event.position().toPoint())
            if idx >= 0:
                self.pageClicked.emit(idx)
                event.accept()
                return
        super().mousePressEvent(event)

    def mouseReleaseEvent(self, event):
        super().mouseReleaseEvent(event)

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        is_dark = isDarkTheme()
        inactive = QColor(180, 180, 180, 200) if not is_dark else QColor(160, 160, 160, 200)
        for i in range(self._count):
            r = self._dotRect(i)
            if i == self._current:
                painter.setBrush(QBrush(self._active_color))
                painter.setPen(Qt.PenStyle.NoPen)
            else:
                painter.setBrush(QBrush(inactive))
                painter.setPen(Qt.PenStyle.NoPen)
            painter.drawEllipse(r)
        painter.end()


class HomeInterface(QWidget, TranslatableWidget):
    """主界面"""

    weather_updated = pyqtSignal(dict)
    poetry_updated = pyqtSignal(str)

    def __init__(self, mainWindow, parent=None):
        super().__init__(parent)
        self.mainWindow = mainWindow
        self.setObjectName("home")

        # 拖拽预览
        self._drag_preview_def = None
        self._drag_preview_x = 0
        self._drag_preview_y = 0
        self._drag_preview_width = 0
        self._drag_preview_height = 0
        self._drag_preview_collision = False
        self._drag_preview_size = (200, 80)

        # 编辑模式
        self._edit_mode_active = False
        self._edit_selected_placement_id = None

        # 翻页相关
        self._page_anim = None
        self._swipe_start_x = None
        self._swipe_start_y = None
        self._swipe_dragging = False
        self._swipe_moved = False
        self._swipe_last_dx = 0
        # 检测 pagesStack 是否卡在两页中间
        self._page_safety_timer = QTimer(self)
        self._page_safety_timer.setSingleShot(True)
        self._page_safety_timer.timeout.connect(self._checkPagePosition)
        # 检查鼠标按着
        self._swipe_watchdog = QTimer(self)
        self._swipe_watchdog.setSingleShot(False)
        self._swipe_watchdog.setInterval(100)
        self._swipe_watchdog.timeout.connect(self._swipeWatchdog)

        self.setAcceptDrops(True)

        self._initBackground()

        self.page_manager = PageManager(DATA_CONFIG)

        self._initLayout()
        self._initPages()

        self._cached_poetry = None

        self.grid_service = GridLayoutService()
        short_side_cells = cfg.gridShortSideCells.value if hasattr(cfg, 'gridShortSideCells') else 6
        inset_percent = cfg.gridInsetPercent.value if hasattr(cfg, 'gridInsetPercent') else 5
        self.grid_settings = GridSettings(
            short_side_cells=short_side_cells,
            gap_ratio=0.12,
            inset_percent=inset_percent
        )
        self._grid_metrics = None

        self.component_registry = ComponentRegistry(self)
        self.component_registry.register_batch(BUILTIN_COMPONENT_DEFINITIONS)

        from ui.component import ComponentManager
        self.component_manager = ComponentManager(self)
        self.component_manager.load_components()
        self._draggable_widgets = self.component_manager.get_all_containers()
        # 组件加载完毕后根据当前页显示/隐藏
        self._applyPageVisibility()

        self._initBottomBar()

        self.setStyleSheet(load_qss('home.qss'))
        cfg.themeChanged.connect(self._updateTheme)
        cfg.componentCardOpacity.valueChanged.connect(self._updateTheme)
        cfg.componentCardRadius.valueChanged.connect(self._updateTheme)
        cfg.backgroundBlurRadius.valueChanged.connect(self._computeBlurredBackground)
        if hasattr(cfg, 'gridShortSideCells'):
            cfg.gridShortSideCells.valueChanged.connect(self._onGridSettingsChanged)
        if hasattr(cfg, 'gridInsetPercent'):
            cfg.gridInsetPercent.valueChanged.connect(self._onGridSettingsChanged)
        self._updateTheme()

        self.setup_translatable_ui()

        logger.info(tr("home.init_complete"))
    
    def _update_grid_metrics(self):
        """更新网格尺寸计算"""
        self._grid_metrics = self.grid_service.calculate_grid_metrics(
            self.width(), self.height(), self.grid_settings
        )

    def _onGridSettingsChanged(self):
        """网格配置变化时更新网格设置"""
        short_side_cells = cfg.gridShortSideCells.value if hasattr(cfg, 'gridShortSideCells') else 6
        inset_percent = cfg.gridInsetPercent.value if hasattr(cfg, 'gridInsetPercent') else 5
        self.grid_settings = GridSettings(
            short_side_cells=short_side_cells,
            gap_ratio=0.12,
            inset_percent=inset_percent
        )
        self._update_grid_metrics()
        # 网格度量同步到覆盖层
        if hasattr(self, '_grid_overlay') and self._grid_overlay:
            self._grid_overlay.update_grid_metrics(self._grid_metrics)
    def _initBackground(self):
        self.homeBackgroundImage = QLabel()
        self.homeBackgroundImage.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.homeBackgroundImage.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        self.originalPixmap = QPixmap(1, 1)
        self.originalPixmap.fill(Qt.GlobalColor.transparent)
        self.homeBackgroundImage.setPixmap(self.originalPixmap)
        self.homeBackgroundImage.setMinimumSize(100, 100)

        self.homeDimOverlay = QWidget()
        self.homeDimOverlay.setObjectName("dimOverlay")
        self.homeDimOverlay.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
    def _initLayout(self):
        self.homeContent = QWidget(self)
        self.homeContent.setObjectName("homeContent")
        self.homeContent.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        self.gridLayout = QGridLayout(self.homeContent)
        self.gridLayout.setContentsMargins(0, 0, 0, 0)
        self.gridLayout.setSpacing(0)

        self.gridLayout.addWidget(self.homeBackgroundImage, 0, 0, 1, 1)
        self.gridLayout.addWidget(self.homeDimOverlay, 0, 0, 1, 1)

        # 翻页容器
        from PyQt6.QtWidgets import QFrame
        self.pagesContainer = QFrame(self.homeContent)
        self.pagesContainer.setObjectName("pagesContainer")
        self.pagesContainer.setStyleSheet("background: transparent; border: none;")
        self.pagesContainer.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, False)
        self.pagesStack = QWidget(self.pagesContainer)
        self.pagesStack.setObjectName("pagesStack")
        self.pagesStack.setStyleSheet("background: transparent;")
        self.pagesStack.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, False)
        self.pagesStack.show()

        homeLayout = QVBoxLayout(self)
        homeLayout.setContentsMargins(0, 0, 0, 0)
        homeLayout.addWidget(self.homeContent)

        self._grid_overlay = _GridOverlay(self)
        self._grid_overlay.setObjectName("gridOverlay")
        self._grid_overlay.hide()
        self._grid_overlay.setup(self)

    def _initPages(self):
        """根据 PageManager 创建所有页面 widget"""
        self._page_widgets = {}        # page_index -> QWidget

        from ui.component import NavigationPage
        for i, meta in enumerate(self.page_manager.pages()):
            if meta.type == "nav":
                page = NavigationPage(self.pagesStack, page_index=i, page_manager=self.page_manager)
            else:
                page = QWidget(self.pagesStack)
                page.setObjectName("infoPageWidget")
                page.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, False)
            self._page_widgets[i] = page
            page.show()

        self._currentPageIndex = self.page_manager.get_current_page()
        if not (0 <= self._currentPageIndex < len(self._page_widgets)):
            self._currentPageIndex = 0
        self._layoutPages()
        self._applyPageVisibility()

    def _layoutPages(self):
        """所有页面横向排列在 pagesStack 内 第 i 页 move(i*width, 0)
        pagesStack 自身偏移到 -currentPage*width"""
        if not hasattr(self, 'pagesContainer') or not self.pagesContainer:
            return
        w = self.pagesContainer.width()
        h = self.pagesContainer.height()
        n = max(1, len(self._page_widgets))
        for i, page in self._page_widgets.items():
            page.setFixedSize(w, h)
            page.move(i * w, 0)
        # pagesStack 宽度 = 页数 * 单页宽度
        self.pagesStack.setFixedSize(n * w, h)
        self.pagesStack.move(-self._currentPageIndex * w, 0)

    def _applyPageVisibility(self, visible_pages=None):
        """设置组件可见性"""
        if not hasattr(self, 'component_manager') or not self.component_manager:
            return
        cur = self._currentPageIndex
        if visible_pages is None:
            visible_pages = set()
        else:
            visible_pages = set(visible_pages)
        visible_pages.add(cur)
        for comp_id, instance in self.component_manager.components.items():
            try:
                page_idx = self.component_manager.get_component_page(comp_id)
                if page_idx in visible_pages:
                    stored = self.component_manager._component_data.get(comp_id, {})
                    if stored.get("enabled", True):
                        instance.show()
                    else:
                        instance.hide()
                else:
                    instance.hide()
            except Exception as e:
                logger.warning(f"[_applyPageVisibility] {comp_id}: {e}")

    def get_info_page_widget(self, page_index: int):
        """返回信息页的 widget"""
        if not hasattr(self, '_page_widgets'):
            return None
        meta = self.page_manager.get_page(page_index)
        if meta is None or meta.type != "info":
            return None
        return self._page_widgets.get(page_index)

    def _stopPageAnim(self):
        """停止翻页动画"""
        if self._page_anim:
            anim = self._page_anim
            self._page_anim = None
            try:
                anim.finished.disconnect()
            except Exception:
                pass
            anim.stop()

    def _goToPage(self, index: int, animate: bool = True):
        """切换到某页面"""
        if not (0 <= index < len(self._page_widgets)):
            return
        if hasattr(self, '_deselectAll'):
            try:
                self._deselectAll()
            except Exception:
                pass

        self._stopPageAnim()

        old_index = self._currentPageIndex
        self._currentPageIndex = index
        self.page_manager.set_current_page(index)

        w = self.pagesContainer.width()
        target_x = -index * w

        # 翻页过渡
        if animate and old_index != index:
            self._applyPageVisibility(visible_pages={old_index, index})

        if animate:
            cur_x = self.pagesStack.x()
            if cur_x == target_x:
                self._onPageAnimFinished()
            else:
                # QPropertyAnimation 在 C++ 动画 pos 属性
                anim = QPropertyAnimation(self.pagesStack, b"pos", self)
                anim.setStartValue(QPoint(cur_x, 0))
                anim.setEndValue(QPoint(target_x, 0))
                anim.setDuration(250)
                anim.setEasingCurve(QEasingCurve.Type.OutCubic)
                anim.finished.connect(self._onPageAnimFinished)
                self._page_anim = anim
                anim.start()
        else:
            self.pagesStack.move(int(target_x), 0)
            self._onPageAnimFinished()

        if hasattr(self, 'pageIndicator'):
            self.pageIndicator.set_current(index)

    def _onPageAnimFinished(self):
        self._page_anim = None
        # snap 到当前页的精确位置
        w = self.pagesContainer.width()
        if w > 0:
            self.pagesStack.move(-self._currentPageIndex * w, 0)
        self._applyPageVisibility()
        if hasattr(self, 'pageIndicator'):
            self.pageIndicator.set_current(self._currentPageIndex)
        if hasattr(self, '_grid_overlay') and self._grid_overlay:
            if self._edit_mode_active and self.page_manager.get_page(self._currentPageIndex) and \
               self.page_manager.get_page(self._currentPageIndex).type == "info":
                self._grid_overlay.show()
            else:
                self._grid_overlay.hide()
        self._page_safety_timer.start(300)

    def _checkPagePosition(self):
        if self._swipe_dragging or self._page_anim:
            return
        if not hasattr(self, 'pagesStack') or not self.pagesStack:
            return
        w = self.pagesContainer.width()
        if w <= 0:
            return
        expected_x = -self._currentPageIndex * w
        if self.pagesStack.x() != expected_x:
            self.pagesStack.move(expected_x, 0)

    def _initBottomBar(self):
        """底部栏"""
        self.bottomBar = QWidget(self.homeContent)
        self.bottomBar.setObjectName("bottomBar")
        self.bottomBar.setFixedSize(1400, 60)
        self.bottomBar.show()

        barLayout = QHBoxLayout(self.bottomBar)
        barLayout.setContentsMargins(16, 0, 16, 0)
        barLayout.setAlignment(Qt.AlignmentFlag.AlignVCenter)

        self.backToDesktopBtn = PushButton(tr("home.back_to_desktop"), self.bottomBar)
        self.backToDesktopBtn.setObjectName("backToDesktopBtn")
        self.backToDesktopBtn.clicked.connect(self.mainWindow.showMinimized)
        barLayout.addWidget(self.backToDesktopBtn)

        barLayout.addStretch()

        # 中间：页面指示器
        self.pageIndicator = PageIndicator(self.bottomBar)
        self.pageIndicator.set_count(self.page_manager.page_count())
        self.pageIndicator.set_current(self._currentPageIndex)
        self.pageIndicator.pageClicked.connect(lambda i: self._goToPage(i, animate=True))
        barLayout.addWidget(self.pageIndicator)

        barLayout.addStretch()

        # 编辑模式显示：
        # 添加页面按钮
        self.addPageBtn = PushButton(self.bottomBar)
        self.addPageBtn.setObjectName("addPageBtn")
        self.addPageBtn.setIcon(FUI.ADD)
        self._refreshAddPageBtn()
        self.addPageBtn.clicked.connect(self._addNewPage)
        self.addPageBtn.hide()
        barLayout.addWidget(self.addPageBtn)

        # 重命名页面按钮
        self.renamePageBtn = PushButton(self.bottomBar)
        self.renamePageBtn.setObjectName("renamePageBtn")
        self.renamePageBtn.setIcon(FUI.EDIT)
        self.renamePageBtn.setText(tr("home.rename_page"))
        self.renamePageBtn.clicked.connect(lambda: self._renamePage(self._currentPageIndex))
        self.renamePageBtn.hide()
        barLayout.addWidget(self.renamePageBtn)

        # 删除页面按钮
        self.delPageBtn = PushButton(self.bottomBar)
        self.delPageBtn.setObjectName("delPageBtn")
        self.delPageBtn.setIcon(FUI.DELETE)
        self.delPageBtn.setText(tr("home.delete_page"))
        self.delPageBtn.clicked.connect(lambda: self._deletePage(self._currentPageIndex))
        self.delPageBtn.hide()
        barLayout.addWidget(self.delPageBtn)

        self.menuBtn = ToolButton(FUI.MENU, self.bottomBar)
        self.menuBtn.setObjectName("bottomMenuBtn")
        self.menuBtn.setFixedSize(36, 36)
        self.menuBtn.setIconSize(QSize(28, 28))
        self.menuBtn.clicked.connect(self._showBottomMenu)
        barLayout.addWidget(self.menuBtn)

        self._updateBottomBarPosition()

    def _refreshAddPageBtn(self):
        """添加页面按钮文案"""
        if not hasattr(self, 'addPageBtn') or not self.addPageBtn:
            return
        total = self.page_manager.page_count()
        self.addPageBtn.setText(tr("home.add_page_with_count", count=total))

    def _addNewPage(self):
        """添加新信息页"""
        new_index = self.page_manager.add_page(page_type="info")
        if new_index < 0:
            InfoBar.warning(title=tr("home.page_limit"),
                            content="", parent=self, duration=2000)
            return
        # 新建对应页 widget
        page = QWidget(self.pagesStack)
        page.setObjectName("infoPageWidget")
        page.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, False)
        self._page_widgets[new_index] = page
        page.show()
        self._layoutPages()
        self.pageIndicator.set_count(self.page_manager.page_count())
        self._refreshAddPageBtn()
        self._goToPage(new_index, animate=True)

    def _renamePage(self, index: int):
        from qfluentwidgets import MessageBoxBase, LineEdit, SubtitleLabel
        meta = self.page_manager.get_page(index)
        if meta is None:
            return
        box = MessageBoxBase(self.window())
        title = SubtitleLabel(tr("home.rename_page_title"), box)
        box.viewLayout.addWidget(title)
        edit = LineEdit(box)
        edit.setText(meta.name)
        edit.setClearButtonEnabled(True)
        box.viewLayout.addWidget(edit)
        if box.exec():
            new_name = edit.text().strip()
            if new_name:
                self.page_manager.rename_page(index, new_name)

    def _deletePage(self, index: int):
        """删除页面"""
        from qfluentwidgets import MessageBox
        meta = self.page_manager.get_page(index)
        if meta is None or meta.type == "nav":
            return
        if self.page_manager.page_count() <= 1:
            return

        # 找一个信息页作为 fallback
        fallback = -1
        for i, m in enumerate(self.page_manager.pages()):
            if i != index and m.type == "info":
                fallback = i
                break

        # 确认
        if fallback >= 0:
            content = tr("home.delete_page_confirm", name=meta.name)
        else:
            content = tr("home.delete_page_no_fallback", name=meta.name)
        msg_box = MessageBox(
            tr("home.delete_page"),
            content,
            self.window(),
        )
        if not msg_box.exec():
            return

        # 迁移或删除被删页的组件
        to_remove = []
        for comp_id, instance in self.component_manager.components.items():
            pi = self.component_manager.get_component_page(comp_id)
            if pi == index:
                if fallback >= 0:
                    self.component_manager.set_component_page(comp_id, fallback)
                    new_parent = self._page_widgets.get(fallback)
                    if new_parent is not None:
                        instance.setParent(new_parent)
                else:
                    to_remove.append(comp_id)
        for cid in to_remove:
            self.component_manager.remove_component(cid)

        # 调整其他组件的 page_index
        self.component_manager.shift_pages_after_delete(index, fallback_index=fallback if fallback >= 0 else 0)

        self.page_manager.delete_page(index)

        old_widget = self._page_widgets.pop(index, None)
        if old_widget:
            old_widget.setParent(None)
            old_widget.deleteLater()

        # 重建 _page_widgets 的 key
        new_map = {}
        for i in sorted(self._page_widgets.keys()):
            new_map[len(new_map)] = self._page_widgets[i]
        self._page_widgets = new_map

        for i, page in self._page_widgets.items():
            if hasattr(page, '_page_index'):
                page._page_index = i

        self.pageIndicator.set_count(self.page_manager.page_count())
        self._refreshAddPageBtn()

        # 跳到合适页
        new_cur = self.page_manager.get_current_page()
        self._currentPageIndex = new_cur
        self._layoutPages()
        self._applyPageVisibility()
        if hasattr(self, 'pageIndicator'):
            self.pageIndicator.set_current(new_cur)

    def _updateBottomBarPosition(self):
        """距底部25px"""
        if not hasattr(self, 'bottomBar') or not self.bottomBar:
            return
        parent = self.homeContent
        x = (parent.width() - self.bottomBar.width()) // 2
        y = parent.height() - self.bottomBar.height() - 25
        self.bottomBar.move(x, y)

    def _openSettingsWindow(self):
        from ui.settings import SettingsWindow
        if not hasattr(self, '_settings_window') or self._settings_window is None:
            self._settings_window = SettingsWindow(self.mainWindow)
        self._settings_window.show()
        self._settings_window.raise_()
        self._settings_window.activateWindow()

    def _openComponentEditWindow(self):
        from ui.component import ComponentLibraryWindow
        if not hasattr(self, '_component_library_window') or self._component_library_window is None:
            self._component_library_window = ComponentLibraryWindow(self.component_registry)
            self._component_library_window.setAttribute(Qt.WidgetAttribute.WA_DeleteOnClose, True)
            self._component_library_window.installEventFilter(self)
            self._component_library_window.destroyed.connect(self._on_component_library_destroyed)

        # 进入编辑模式
        self._enterEditMode()

        self._component_library_window.show()
        self._component_library_window.raise_()
        self._component_library_window.activateWindow()

    def _on_component_library_destroyed(self):
        self._component_library_window = None
        self._exitEditMode()

    def eventFilter(self, obj, event):
        if obj == self._component_library_window and event.type() == QEvent.Type.Close:
            self._exitEditMode()
        return super().eventFilter(obj, event)

    def dragEnterEvent(self, event):
        """拖拽进入"""
        if event.mimeData().hasFormat("application/x-Glimpseon-component"):
            # 导航页时不接受拖入组件
            meta = self.page_manager.get_page(self._currentPageIndex) if hasattr(self, 'page_manager') else None
            if meta is not None and meta.type == "nav":
                event.ignore()
                return
            event.acceptProposedAction()

            data = event.mimeData().data("application/x-Glimpseon-component").data().decode('utf-8')

            if "|" in data:
                comp_type, comp_style = data.split("|", 1)
                self._drag_preview_def = self.component_registry.get_definition(f"{comp_type}_{comp_style}")
            else:
                self._drag_preview_def = self.component_registry.get_definition(data)

            logger.info(f"dragEnter: component={data}, def={self._drag_preview_def}")

            from ui.component import COMPONENT_STYLES
            comp_type, comp_style = self._resolve_component_type_style(data)
            style_info = COMPONENT_STYLES.get(comp_type, {}).get(comp_style, {})
            comp_class = style_info.get("class")
            if comp_class:
                try:
                    import uuid
                    temp_data = {
                        "id": f"temp_preview_{uuid.uuid4().hex[:8]}",
                        "type": comp_type,
                        "style": comp_style,
                        "config": style_info.get("default_config", {}),
                    }
                    temp_widget = comp_class(self, temp_data)
                    temp_widget.hide()
                    w, h = style_info.get("default_size", (200, 80))
                    self._drag_preview_size = (w, h)
                    temp_widget.setParent(None)
                    temp_widget.deleteLater()
                    logger.info(f"预览框实际尺寸: {w}x{h} ({comp_type}/{comp_style})")
                except Exception as e:
                    logger.warning(f"默认尺寸: {e}")
                    self._drag_preview_size = style_info.get("default_size", (200, 80))
            else:
                self._drag_preview_size = style_info.get("default_size", (200, 80))

            self._update_grid_metrics()
            logger.info(f"grid_metrics cell_size={self._grid_metrics.cell_size if self._grid_metrics else 'None'}")

            if hasattr(self, '_drag_preview_size') and self._drag_preview_size:
                self._drag_preview_width = self._drag_preview_size[0]
                self._drag_preview_height = self._drag_preview_size[1]

            if hasattr(self, '_grid_overlay') and self._grid_overlay:
                self._grid_overlay.update_grid_metrics(self._grid_metrics)
                self._grid_overlay.show_preview(False)
                self._grid_overlay.show()

            self.update()
        else:
            event.ignore()

    def dragMoveEvent(self, event):
        """拖拽移动"""
        if event.mimeData().hasFormat("application/x-Glimpseon-component"):
            event.acceptProposedAction()

            pos = event.position()
            default_size = getattr(self, '_drag_preview_size', (200, 80))
            comp_width, comp_height = default_size

            raw_x = pos.x() - comp_width / 2
            raw_y = pos.y() - comp_height / 2

            if self._grid_metrics and self._drag_preview_def:
                SNAP_THRESHOLD = 20
                snapped_x, snapped_y = self._snap_to_grid(
                    raw_x, raw_y, comp_width, comp_height, SNAP_THRESHOLD
                )
            else:
                snapped_x, snapped_y = raw_x, raw_y

            snapped_x = max(0, min(snapped_x, self.width() - comp_width))
            snapped_y = max(0, min(snapped_y, self.height() - comp_height))

            self._drag_preview_x = snapped_x
            self._drag_preview_y = snapped_y
            self._drag_preview_width = comp_width
            self._drag_preview_height = comp_height

            self._drag_preview_collision = self._check_pixel_collision(
                snapped_x, snapped_y, comp_width, comp_height
            )

            if hasattr(self, '_grid_overlay') and self._grid_overlay:
                self._grid_overlay.update_preview_pixel(
                    snapped_x, snapped_y, comp_width, comp_height,
                    self._drag_preview_collision
                )
                self._grid_overlay.show()
        else:
            event.ignore()

    def _snap_to_grid(self, x: float, y: float, width: float, height: float, threshold: float) -> tuple:
        """自由吸附"""
        metrics = self._grid_metrics
        inset = metrics.edge_inset_px
        pitch = metrics.pitch

        left = x
        right = x + width
        top = y
        bottom = y + height

        def find_nearest_grid_line(pos, is_vertical: bool):
            if is_vertical:
                for col in range(metrics.column_count + 1):
                    line_x = inset + col * pitch
                    if abs(pos - line_x) <= threshold:
                        return line_x
            else:
                for row in range(metrics.row_count + 1):
                    line_y = inset + row * pitch
                    if abs(pos - line_y) <= threshold:
                        return line_y
            return pos

        snapped_left = find_nearest_grid_line(left, True)
        snapped_right = find_nearest_grid_line(right, True)
        snapped_top = find_nearest_grid_line(top, False)
        snapped_bottom = find_nearest_grid_line(bottom, False)

        final_x = snapped_left if snapped_left != left else (snapped_right - width if snapped_right != right else x)
        final_y = snapped_top if snapped_top != top else (snapped_bottom - height if snapped_bottom != bottom else y)

        return (final_x, final_y)

    def _check_pixel_collision(self, x: float, y: float, width: float, height: float) -> bool:
        """像素位置碰撞检测"""
        if hasattr(self, 'component_manager') and self.component_manager:
            containers = self.component_manager.get_all_containers()
            for container in containers:
                if container and container.isVisible():
                    cx = container.x()
                    cy = container.y()
                    cw = container.width()
                    ch = container.height()
                    if not (x + width < cx or x > cx + cw or
                            y + height < cy or y > cy + ch):
                        return True
        return False

    def dragLeaveEvent(self, event):
        """拖拽离开"""
        self._drag_preview_def = None
        self._drag_preview_x = 0
        self._drag_preview_y = 0
        self._drag_preview_width = 0
        self._drag_preview_height = 0
        self._drag_preview_collision = False

        if hasattr(self, '_grid_overlay') and self._grid_overlay:
            if not self._edit_mode_active:
                self._grid_overlay.hide()
            else:
                self._grid_overlay.show_preview(False)

        self.update()
        event.accept()

    def dropEvent(self, event):
        """放置"""
        if not event.mimeData().hasFormat("application/x-Glimpseon-component"):
            event.ignore()
            return

        saved_x = self._drag_preview_x
        saved_y = self._drag_preview_y
        saved_w = self._drag_preview_width
        saved_h = self._drag_preview_height
        has_valid_preview = saved_w > 0 and saved_h > 0

        self._drag_preview_def = None
        self._drag_preview_x = 0
        self._drag_preview_y = 0
        self._drag_preview_width = 0
        self._drag_preview_height = 0
        self._drag_preview_collision = False

        if hasattr(self, '_grid_overlay') and self._grid_overlay:
            if self._edit_mode_active:
                self._grid_overlay.show_preview(False)
            else:
                self._grid_overlay.hide()

        data = event.mimeData().data("application/x-Glimpseon-component").data().decode('utf-8')

        component_type, component_style = self._resolve_component_type_style(data)

        drop_pos = event.position()

        logger.info(f"dropEvent: saved=({saved_x},{saved_y},{saved_w},{saved_h}), has_valid_preview={has_valid_preview}")

        if hasattr(self, 'component_manager') and self.component_manager:
            comp_id = self.component_manager.add_component(component_type, component_style,
                                                            page_index=self._currentPageIndex)
            if comp_id:
                comp = self.component_manager.components.get(comp_id)
                if comp:
                    # 用组件实际尺寸算放置百分比
                    comp_width = comp.width()
                    comp_height = comp.height()
                    if has_valid_preview:
                        available_width = self.width() - comp_width
                        available_height = self.height() - comp_height
                        if available_width > 0 and available_height > 0:
                            pos_x_pct = saved_x / available_width
                            pos_y_pct = saved_y / available_height
                        else:
                            pos_x_pct = 0.5
                            pos_y_pct = 0.5
                    else:
                        # 没有就鼠标位置减半宽高居中
                        if comp_width > 0 and comp_height > 0:
                            center_x = drop_pos.x() - comp_width / 2
                            center_y = drop_pos.y() - comp_height / 2
                            available_width = self.width() - comp_width
                            available_height = self.height() - comp_height
                            if available_width > 0 and available_height > 0:
                                pos_x_pct = center_x / available_width
                                pos_y_pct = center_y / available_height
                            else:
                                pos_x_pct = 0.5
                                pos_y_pct = 0.5

                    pos_x_pct = max(0.0, min(1.0, pos_x_pct))
                    pos_y_pct = max(0.0, min(1.0, pos_y_pct))
                    comp.setPositionPercent(pos_x_pct, pos_y_pct)
                    self.component_manager.save_components()
                    if comp not in self._draggable_widgets:
                        self._draggable_widgets.append(comp)
                    if self._edit_mode_active and hasattr(comp, 'setDraggable'):
                        comp.setDraggable(True)
                        try:
                            comp.selected.connect(self._selectComponent)
                        except Exception:
                            pass

                event.acceptProposedAction()
                self.update()

                from qfluentwidgets import InfoBarPosition
                InfoBar.success(
                    tr("component_edit.add_success"),
                    "",
                    orient=Qt.Orientation.Horizontal,
                    position=InfoBarPosition.TOP,
                    duration=2000,
                    parent=self
                )
                return

        logger.warning(f"dropEvent: 组件创建失败 type={component_type} style={component_style}")
        event.ignore()
        self.update()

    def _resolve_component_type_style(self, component_id: str) -> tuple:
        """从 definition id/type|style 格式读type style"""
        try:
            from ui.component import COMPONENT_STYLES
            if '|' in component_id:
                comp_type, comp_style = component_id.split('|', 1)
                if comp_type in COMPONENT_STYLES:
                    if comp_style in COMPONENT_STYLES[comp_type]:
                        return (comp_type, comp_style)
                    first_style = next(iter(COMPONENT_STYLES[comp_type]))
                    return (comp_type, first_style)
            parts = component_id.split('_')
            for i in range(len(parts), 0, -1):
                potential_type = '_'.join(parts[:i])
                potential_style = '_'.join(parts[i:])
                if potential_type in COMPONENT_STYLES and potential_style in COMPONENT_STYLES[potential_type]:
                    return (potential_type, potential_style)
        except Exception:
            pass
        parts = component_id.replace('|', '_').split('_')
        return (parts[0], '_'.join(parts[1:]))

    def _showBottomMenu(self):
        """底部菜单按钮弹出菜单"""
        menu = RoundMenu(parent=self.menuBtn)

        settings_action = Action(FUI.SETTING, tr("home.menu_settings"))
        settings_action.triggered.connect(self._openSettingsWindow)
        menu.addAction(settings_action)

        edit_action = Action(FUI.EDIT, tr("home.menu_component_edit"))
        edit_action.triggered.connect(self._openComponentEditWindow)
        menu.addAction(edit_action)

        menu.addSeparator()

        restart_action = Action(FUI.UPDATE, tr("home.menu_restart"))
        restart_action.triggered.connect(lambda: os.system('shutdown /r /t 0'))
        menu.addAction(restart_action)

        shutdown_action = Action(FUI.CLOSE, tr("home.menu_shutdown"))
        shutdown_action.triggered.connect(lambda: os.system('shutdown /s /t 0'))
        menu.addAction(shutdown_action)

        menu.exec(self.menuBtn.mapToGlobal(
            self.menuBtn.rect().bottomRight()
        ))

    def _enterEditMode(self):
        """进入编辑模式"""
        self._edit_mode_active = True
        # 更新网格度量
        self._update_grid_metrics()
        logger.info(f"进入编辑模式: grid_metrics={self._grid_metrics}, "
                    f"cell_size={self._grid_metrics.cell_size if self._grid_metrics else 'None'}")
        # 显示网格（信息页）
        meta = self.page_manager.get_page(self._currentPageIndex) if hasattr(self, 'page_manager') else None
        if hasattr(self, '_grid_overlay') and self._grid_overlay:
            self._grid_overlay.update_grid_metrics(self._grid_metrics)
            self._grid_overlay.show_preview(False)
            if meta is None or meta.type == "info":
                self._grid_overlay.show()
                self._grid_overlay.raise_()
            else:
                self._grid_overlay.hide()
        if hasattr(self, 'addPageBtn') and self.addPageBtn:
            self.addPageBtn.show()
        if hasattr(self, 'renamePageBtn') and self.renamePageBtn:
            self.renamePageBtn.show()
        if hasattr(self, 'delPageBtn') and self.delPageBtn:
            self.delPageBtn.show()
        # 设置可拖动
        self._set_all_draggable(True)

    def _selectComponent(self, component_id: str):
        """选中组件"""
        self._deselectAll()
        self._edit_selected_placement_id = component_id
        container = self.component_manager.components.get(component_id)
        if container:
            container.setSelected(True)
            container.showEditControls(True)

    def _deselectAll(self):
        """取消所有组件选中"""
        if self._edit_selected_placement_id:
            old = self.component_manager.components.get(self._edit_selected_placement_id)
            if old:
                old.setSelected(False)
                old.showEditControls(False)
        self._edit_selected_placement_id = None

    def deleteSelectedComponent(self, component_id: str):
        """删除组件"""
        self._deselectAll()
        self.component_manager.remove_component(component_id)
        self._draggable_widgets = [
            w for w in self._draggable_widgets
            if not (hasattr(w, 'component_id') and w.component_id == component_id)
        ]

    def mousePressEvent(self, event):
        if self._edit_mode_active and event.button() == Qt.MouseButton.LeftButton:
            self._deselectAll()
        # 翻页拖拽：记录起点
        if event.button() == Qt.MouseButton.LeftButton:
            meta = self.page_manager.get_page(self._currentPageIndex) if hasattr(self, 'page_manager') else None
            if not self._edit_mode_active or (meta and meta.type == "nav"):
                self._stopPageAnim()
                w = self.pagesContainer.width()
                if w > 0:
                    self.pagesStack.move(-self._currentPageIndex * w, 0)
                self._swipe_start_x = event.position().x()
                self._swipe_start_y = event.position().y()
                self._swipe_dragging = True
                self._swipe_moved = False
                self._swipe_last_dx = 0
                self.grabMouse()
                self._swipe_watchdog.start()
                event.accept()
                return
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event):
        """翻页跟手"""
        if self._swipe_dragging and self._swipe_start_x is not None:
            dx = event.position().x() - self._swipe_start_x
            dy = event.position().y() - self._swipe_start_y
            if abs(dx) > 10 and abs(dx) > abs(dy):
                if not self._swipe_moved:
                    self._swipe_moved = True
                    # 第一次移动时显示相邻页组件
                    pages = {self._currentPageIndex}
                    if self._currentPageIndex > 0:
                        pages.add(self._currentPageIndex - 1)
                    if self._currentPageIndex < len(self._page_widgets) - 1:
                        pages.add(self._currentPageIndex + 1)
                    self._applyPageVisibility(visible_pages=pages)
                # 边缘阻尼
                w = self.pagesContainer.width()
                raw_dx = dx
                if (self._currentPageIndex == 0 and raw_dx > 0) or \
                   (self._currentPageIndex == len(self._page_widgets) - 1 and raw_dx < 0):
                    raw_dx = int(raw_dx * 0.3)
                self._swipe_last_dx = int(raw_dx)
                self.pagesStack.move(-self._currentPageIndex * w + self._swipe_last_dx, 0)
            event.accept()
            return
        super().mouseMoveEvent(event)

    def _finish_swipe(self, dx: float):
        """按位移决定目标页并结束滑动状态"""
        w = self.pagesContainer.width()
        threshold = max(60, w * 0.15)
        target = self._currentPageIndex
        if self._swipe_moved:
            if dx > threshold and self._currentPageIndex > 0:
                target = self._currentPageIndex - 1
            elif dx < -threshold and self._currentPageIndex < len(self._page_widgets) - 1:
                target = self._currentPageIndex + 1
        self._swipe_dragging = False
        self._swipe_start_x = None
        self._swipe_moved = False
        self._goToPage(target, animate=True)

    def mouseReleaseEvent(self, event):
        """翻页释放"""
        if self._swipe_dragging and self._swipe_start_x is not None:
            self._swipe_watchdog.stop()
            try:
                self.releaseMouse()
            except Exception:
                pass
            dx = event.position().x() - self._swipe_start_x
            self._finish_swipe(dx)
            event.accept()
            return
        super().mouseReleaseEvent(event)

    def _swipeWatchdog(self):
        if not self._swipe_dragging:
            self._swipe_watchdog.stop()
            return
        # 左键
        from PyQt6.QtWidgets import QApplication
        if not (QApplication.mouseButtons() & Qt.MouseButton.LeftButton):
            self._swipe_watchdog.stop()
            try:
                self.releaseMouse()
            except Exception:
                pass
            self._finish_swipe(self._swipe_last_dx)

    def wheelEvent(self, event):
        """水平滚轮翻页"""
        if not self._edit_mode_active:
            delta = event.angleDelta()
            dx = delta.x()
            dy = delta.y()
            if abs(dx) < 1 and abs(dy) != 0:
                dx = -dy
            if abs(dx) > 30:
                if dx > 0:
                    self._goToPage(self._currentPageIndex - 1, animate=True)
                else:
                    self._goToPage(self._currentPageIndex + 1, animate=True)
                event.accept()
                return
        super().wheelEvent(event)

    def keyPressEvent(self, event):
        """Delete 键删除选中组件 方向键翻页"""
        if self._edit_mode_active and self._edit_selected_placement_id:
            if event.key() == Qt.Key.Key_Delete:
                self.deleteSelectedComponent(self._edit_selected_placement_id)
                return

        if event.key() == Qt.Key.Key_Left:
            self._goToPage(self._currentPageIndex - 1, animate=True)
            return
        if event.key() == Qt.Key.Key_Right:
            self._goToPage(self._currentPageIndex + 1, animate=True)
            return
        super().keyPressEvent(event)

    def _exitEditMode(self):
        """退出编辑模式"""
        self._edit_mode_active = False
        self._deselectAll()
        # 隐藏网格
        if hasattr(self, '_grid_overlay') and self._grid_overlay:
            self._grid_overlay.hide()
        if hasattr(self, 'addPageBtn') and self.addPageBtn:
            self.addPageBtn.hide()
        if hasattr(self, 'renamePageBtn') and self.renamePageBtn:
            self.renamePageBtn.hide()
        if hasattr(self, 'delPageBtn') and self.delPageBtn:
            self.delPageBtn.hide()
        # 设置不可拖动
        self._set_all_draggable(False)

    def _set_all_draggable(self, enabled: bool):
        """设置可拖动"""
        if not hasattr(self, '_draggable_widgets'):
            self._draggable_widgets = []

        if hasattr(self, 'component_manager') and self.component_manager:
            containers = self.component_manager.get_all_containers()
            logger.info(f"component_manager 有 {len(containers)} 个容器")
            for container in containers:
                if container not in self._draggable_widgets:
                    self._draggable_widgets.append(container)
                    logger.info(f"添加新容器到拖拽列表: {container}")

        try:
            from ui.component import DraggableContainer as DCont
            for child in self.findChildren(DCont):
                if child not in self._draggable_widgets:
                    self._draggable_widgets.append(child)
                    logger.info(f"findChildren 补充: {child}")
        except Exception:
            pass

        logger.info(f"拖拽列表共有 {len(self._draggable_widgets)} 个组件，设置draggable={enabled}")

        # 清理被删除的组件引用
        self._draggable_widgets = [
            w for w in self._draggable_widgets
            if w is not None and shiboken6.isValid(w)
        ]

        for widget in self._draggable_widgets:
            if widget and hasattr(widget, 'setDraggable'):
                try:
                    widget.setDraggable(enabled)
                    widget.raise_()
                    try:
                        widget.selected.disconnect(self._selectComponent)
                    except Exception:
                        pass
                    if enabled:
                        widget.selected.connect(self._selectComponent)
                except Exception as e:
                    logger.warning(f"设置组件可拖动状态失败: {e}")

    def resizeEvent(self, event):
        super().resizeEvent(event)
        available_width = self.width()
        available_height = self.height()

        # 更新网格计算
        self._update_grid_metrics()

        # 翻页容器跟随尺寸
        if hasattr(self, 'pagesContainer') and self.pagesContainer and hasattr(self, 'homeContent') and self.homeContent:
            self.pagesContainer.setGeometry(0, 0, self.homeContent.width(), self.homeContent.height())
            if hasattr(self, '_page_widgets'):
                self._stopPageAnim()
                self._layoutPages()

        if hasattr(self, 'homeBackgroundImage') and self.homeBackgroundImage:
            self._apply_background_size()

        if hasattr(self, '_draggable_widgets'):
            for widget in self._draggable_widgets:
                if (widget is not None and shiboken6.isValid(widget)
                        and hasattr(widget, 'onParentResize')):
                    try:
                        widget.onParentResize()
                    except RuntimeError:
                        pass

        self._updateBottomBarPosition()

    def _apply_background_size(self):
        """把模糊版/原图按当前窗口尺寸缩放后贴到背景"""
        try:
            available_width = self.width()
            available_height = self.height()
            blurred = getattr(self, '_blurredOriginalPixmap', None)
            if blurred and not blurred.isNull():
                self.homeBackgroundImage.setPixmap(
                    blurred.scaled(available_width, available_height,
                                   Qt.AspectRatioMode.IgnoreAspectRatio,
                                   Qt.TransformationMode.SmoothTransformation)
                )
            elif hasattr(self, 'originalPixmap') and self.originalPixmap is not None and not self.originalPixmap.isNull():
                self.homeBackgroundImage.setPixmap(
                    self.originalPixmap.scaled(available_width, available_height,
                                               Qt.AspectRatioMode.IgnoreAspectRatio,
                                               Qt.TransformationMode.SmoothTransformation)
                )
        except Exception as e:
            logger.error(f"背景缩放错误：{e}")

    def _computeBlurredBackground(self):
        if not hasattr(self, 'originalPixmap') or self.originalPixmap is None or self.originalPixmap.isNull():
            return
        blur_radius = cfg.backgroundBlurRadius.value
        if blur_radius <= 0:
            self._blurredOriginalPixmap = None
            self._apply_background_size()
            return

        src = self.originalPixmap
        try:
            from Glimpseon_native import blur_image
            qimg = src.toImage().convertToFormat(QImage.Format.Format_ARGB32)
            t0 = time.perf_counter()
            blurred_bytes = blur_image(qimg.bits().asstring(qimg.sizeInBytes()), qimg.width(), qimg.height(), float(blur_radius))
            elapsed = (time.perf_counter() - t0) * 1000
            blurred_qimage = QImage(blurred_bytes, qimg.width(), qimg.height(), QImage.Format.Format_ARGB32)
            self._blurredOriginalPixmap = QPixmap.fromImage(blurred_qimage)
            logger.info(f"[HOME-BLUR] 模糊完成: {qimg.width()}x{qimg.height()}, radius={blur_radius}, {elapsed:.1f}ms")
        except Exception as e:
            logger.error(f"[HOME-BLUR] 模糊失败: {e}")
            self._blurredOriginalPixmap = None
        self._apply_background_size()

    def _updateTheme(self):
        base_qss = load_qss('home.qss')
        card_qss = self._buildComponentCardQss()
        self.setStyleSheet(base_qss + "\n" + card_qss)

    def _buildComponentCardQss(self):
        opacity = cfg.componentCardOpacity.value / 100.0
        radius = cfg.componentCardRadius.value
        dark = isDarkTheme()
        if dark:
            bg_color = f"rgba(18, 18, 22, {opacity:.2f})"
            border_color = "rgba(255, 255, 255, 0.06)"
        else:
            bg_color = f"rgba(255, 255, 255, {opacity:.2f})"
            border_color = "rgba(0, 0, 0, 0.06)"
        card_names = [
            "#clockContainer", "#weatherContainer", "#weatherHourlyContainer",
            "#weatherWeeklyContainer", "#schoolInfoContainer", "#poetryContainer",
            "#countdownContainer", "#classAlbumContainer", "#mediaWidget",
            "#bottomBar", "#timetableContainer", "#newsBaiduContainer",
            "#newsWeiboContainer", "#newsJinritoutiaoContainer",
            "#newsTenxunwangContainer", "#newsCCTVContainer", "#writingPadContainer",
        ]
        hover_bg = "rgba(255, 255, 255, 0.12)" if dark else "rgba(0, 0, 0, 0.10)"
        return f"""
{", ".join(card_names)} {{
    background-color: {bg_color};
    border-radius: {radius}px;
    border: 1px solid {border_color};
}}
#navItemCell {{
    background-color: {bg_color};
    border-radius: {radius}px;
    border: 1px solid {border_color};
    font-family: {FONT_FAMILY};
}}
#navItemCell:hover {{
        background-color: {hover_bg};
}}
"""

    # 组件拖拽位置变更回调
    def saveComponentPositions(self):
        if not hasattr(self, '_draggable_widgets'):
            return

        if hasattr(self, 'page_manager'):
            try:
                self.page_manager.set_current_page(self._currentPageIndex)
            except Exception as e:
                logger.error(f"保存当前页失败: {e}")

        if hasattr(self, 'component_manager') and self.component_manager:
            try:
                self.component_manager.save_components()
            except Exception as e:
                logger.error(f"保存组件位置失败: {e}")

class CountdownEditDialog(MessageBoxBase):
    """倒计时编辑对话框"""

    def __init__(self, parent=None, countdown_data=None):
        super().__init__(parent)
        self._countdown_data = countdown_data
        self._result = None
        self._init_ui()

    def _init_ui(self):

        self.viewLayout.setSpacing(8)

        title = SubtitleLabel(tr("home.edit_countdown") if self._countdown_data else tr("home.add_countdown"))
        self.viewLayout.addWidget(title)
        infoLabel = BodyLabel(tr("home.countdown_description"))
        self.viewLayout.addWidget(infoLabel)

        titleLabel = BodyLabel(tr("home.target_name"))
        self.viewLayout.addWidget(titleLabel)
        self.titleEdit = LineEdit()
        self.titleEdit.setPlaceholderText(tr("home.target_name_example"))
        if self._countdown_data:
            self.titleEdit.setText(self._countdown_data.get('title', ''))
        self.viewLayout.addWidget(self.titleEdit)

        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)

        dateLabel = BodyLabel(tr("home.target_date"))
        self.viewLayout.addWidget(dateLabel)
        self.datePicker = CalendarPicker()
        if self._countdown_data:
            target_time = self._countdown_data.get('target_time', '')
            if target_time:
                try:
                    dt = datetime.datetime.strptime(target_time, '%Y-%m-%d %H:%M')
                    self.datePicker.setDate(QDate(dt.year, dt.month, dt.day))
                except Exception:
                    pass
        else:
            now = datetime.datetime.now()
            self.datePicker.setDate(QDate(now.year, now.month, now.day))
        self.viewLayout.addWidget(self.datePicker)

        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)

        timeLabel = BodyLabel(tr("home.target_time"))
        self.viewLayout.addWidget(timeLabel)
        self.timePicker = TimePicker()
        if self._countdown_data:
            target_time = self._countdown_data.get('target_time', '')
            if target_time:
                try:
                    dt = datetime.datetime.strptime(target_time, '%Y-%m-%d %H:%M')
                    self.timePicker.setTime(QTime(dt.hour, dt.minute))
                except Exception:
                    pass
        else:
            self.timePicker.setTime(QTime(0, 0))
        self.viewLayout.addWidget(self.timePicker)

        self.yesButton.setText(tr("common.confirm"))
        self.cancelButton.setText(tr("common.cancel"))

        self.widget.setMinimumWidth(360)

        try:
            self.yesButton.clicked.disconnect()
        except TypeError:
            pass
        self.yesButton.clicked.connect(self._on_ok)

    def _on_ok(self):
        try:
            title_text = self.titleEdit.text().strip()
            if not title_text:
                InfoBar.error(tr("common.error"), tr("home.enter_target_name"), parent=self, duration=3000)
                return

            qdate = self.datePicker.date
            qtime = self.timePicker.time
            if not qdate.isValid() or not qtime.isValid():
                InfoBar.error(tr("common.error"), tr("home.enter_valid_datetime"), parent=self, duration=3000)
                return
            dt = datetime.datetime(qdate.year(), qdate.month(), qdate.day(), qtime.hour(), qtime.minute())
            self._result = {
                'title': title_text,
                'target_time': dt.strftime('%Y-%m-%d %H:%M')
            }
            self.accept()
        except Exception as e:
            logger.error(f'保存倒计时失败：{e}')
            InfoBar.error(tr("common.error"), tr("home.enter_valid_datetime_error", error=str(e)), parent=self, duration=5000)

    def get_countdown(self):
        return self._result


class AppEditDialog(MessageBoxBase):
    """应用编辑对话框"""

    def __init__(self, parent=None, app_data=None):
        super().__init__(parent)
        self._app_data = app_data
        self._result = None
        self._init_ui()

    def _init_ui(self):
        self.viewLayout.setSpacing(8)

        title = SubtitleLabel(tr("home.edit_app") if self._app_data else tr("home.add_app"))
        self.viewLayout.addWidget(title)

        descLabel = BodyLabel(tr("home.app_config_description"))
        self.viewLayout.addWidget(descLabel)

        nameLabel = BodyLabel(tr("home.app_name"))
        self.viewLayout.addWidget(nameLabel)
        self.nameEdit = LineEdit(self)
        self.nameEdit.setPlaceholderText(tr("home.app_name_example"))
        if self._app_data:self.nameEdit.setText(self._app_data.get('name', ''))
        self.viewLayout.addWidget(self.nameEdit)
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)

        pathLabel = BodyLabel(tr("home.app_path"))
        self.viewLayout.addWidget(pathLabel)
        pathLayout = QHBoxLayout()
        self.pathEdit = LineEdit(self)
        self.pathEdit.setPlaceholderText(tr("home.app_path_example"))
        if self._app_data:self.pathEdit.setText(self._app_data.get('path', ''))
        self.pathEdit.textChanged.connect(self._on_path_changed)
        pathLayout.addWidget(self.pathEdit)
        self.browseButton = PushButton(tr("common.browse"), self)
        self.browseButton.clicked.connect(self._on_browse)
        pathLayout.addWidget(self.browseButton)
        self.viewLayout.addLayout(pathLayout)
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)

        iconPathLabel = BodyLabel(tr("home.icon_path"))
        self.viewLayout.addWidget(iconPathLabel)
        iconInputLayout = QHBoxLayout()
        self.iconPreviewLabel = QLabel(self)
        self.iconPreviewLabel.setObjectName("iconPreviewLabel")
        self.iconPreviewLabel.setFixedSize(48, 48)
        self.iconPreviewLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._set_default_icon()
        iconInputLayout.addWidget(self.iconPreviewLabel)
        self.iconPathEdit = LineEdit(self)
        self.iconPathEdit.setPlaceholderText(tr("home.icon_path_placeholder"))
        if self._app_data:self.iconPathEdit.setText(self._app_data.get('icon', ''))
        self.iconPathEdit.textChanged.connect(self._on_icon_path_changed)
        iconInputLayout.addWidget(self.iconPathEdit)
        self.iconBrowseButton = PushButton(tr("common.browse"), self)
        self.iconBrowseButton.clicked.connect(self._on_icon_browse)
        iconInputLayout.addWidget(self.iconBrowseButton)
        self.viewLayout.addLayout(iconInputLayout)

        self.yesButton.setText(tr("common.confirm"))
        self.cancelButton.setText(tr("common.cancel"))
        self.widget.setMinimumWidth(400)

        try:
            self.yesButton.clicked.disconnect()
        except TypeError:
            pass
        self.yesButton.clicked.connect(self._on_ok)

        self._icon_filename = self._app_data.get('icon', '') if self._app_data else ''
        if self._icon_filename:
            self._load_icon_preview(self._icon_filename)

    def _set_default_icon(self):
        default_icon = QIcon.fromTheme('application-x-executable')
        if default_icon.isNull():
            pixmap = QPixmap(48, 48)
            pixmap.fill(QColor(100, 100, 100))
            self.iconPreviewLabel.setPixmap(pixmap)
        else:
            self.iconPreviewLabel.setPixmap(default_icon.pixmap(48, 48))

    def _load_icon_preview(self, icon_filename):
        icon_path = get_software_icon_path(icon_filename)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            if not pixmap.isNull():
                scaled = pixmap.scaled(36, 36, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
                self.iconPreviewLabel.setPixmap(scaled)
            else:
                self._set_default_icon()
        else:
            self._set_default_icon()

    def _get_icon_name(self):
        name_text = self.nameEdit.text().strip()
        if name_text:
            cleaned_name = re.sub(r'[^\w\u4e00-\u9fff]', '', name_text)
            if cleaned_name:
                return cleaned_name + '.ico'
        return 'default.ico'

    def _on_path_changed(self, path):
        if path.lower().endswith('.exe') and os.path.exists(path):
            base_name = os.path.splitext(os.path.basename(path))[0]
            self.nameEdit.setText(base_name)
            self._do_extract_icon(path)

    def _do_extract_icon(self, exe_path):
        from ui.component import extract_app_icon
        name = self._get_icon_name()
        base = os.path.splitext(name)[0] if name else None
        icon_path = extract_app_icon(exe_path, base) or 'exe.ico'
        if icon_path:
            self._icon_filename = icon_path
            self.iconPathEdit.setText('')
            self._load_icon_preview(icon_path)

    def _on_icon_path_changed(self, path):
        if path:
            self._icon_filename = path
            self._load_icon_preview(path)

    def _on_icon_browse(self):

        file_path, _ = QFileDialog.getOpenFileName(
            self,
            '选择图标',
            '',
            'Image Files (*.ico *.png *.jpg *.jpeg *.bmp);;All Files (*)'
        )

        if file_path:
            self.iconPathEdit.setText(file_path)

    def _on_browse(self):

        file_path, _ = QFileDialog.getOpenFileName(
            self,
            '选择应用程序',
            '',
            'Executable Files (*.exe);;All Files (*)'
        )

        if file_path:
            self.pathEdit.setText(file_path)

    def _on_ok(self):
        name_text = self.nameEdit.text().strip()
        if not name_text:
            InfoBar.error(tr("dialog.error"), tr("home.enter_target_name"), parent=self, duration=2000)
            return

        path_text = self.pathEdit.text().strip()
        icon_text = self.iconPathEdit.text().strip()

        if icon_text:
            icon_val = icon_text
        elif self._icon_filename:
            icon_val = self._icon_filename
        else:
            icon_val = self._get_icon_name()

        self._result = {
            'name': name_text,
            'path': path_text,
            'icon': icon_val
        }
        self.accept()

    def get_app_data(self):
        return self._result



class _GridOverlay(QWidget):
    """网格覆盖层"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self._home = None
        self._grid_metrics = None
        self._preview_visible = False
        # 像素坐标
        self._preview_x = 0
        self._preview_y = 0
        self._preview_width_px = 0
        self._preview_height_px = 0
        self._preview_collision = False
        if parent:
            parent.installEventFilter(self)

    def eventFilter(self, obj, event):
        if obj is self.parent() and event.type() == QEvent.Type.Resize:
            self.setGeometry(self.parent().rect())
            if self._home:
                self._home._update_grid_metrics()
                self._grid_metrics = self._home._grid_metrics
                self.update()
        return super().eventFilter(obj, event)

    def setup(self, home_interface):
        """设置 HomeInterface 引用"""
        self._home = home_interface

    def update_grid_metrics(self, metrics):
        """更新网格度量并重绘"""
        self._grid_metrics = metrics
        if metrics:
            self.setGeometry(self.parent().rect())
        self.update()

    def show_preview(self, visible: bool):
        """显示/隐藏预览框"""
        self._preview_visible = visible
        if not visible:
            self._preview_x = 0
            self._preview_y = 0
        self.update()

    def update_preview_pixel(self, x: float, y: float, width: float, height: float, collision=False):
        self._preview_visible = True
        self._preview_x = x
        self._preview_y = y
        self._preview_width_px = width
        self._preview_height_px = height
        self._preview_collision = collision
        self.update()

    def paintEvent(self, event):
        """绘制网格和预览框"""
        if not self._grid_metrics or self._grid_metrics.cell_size <= 0:
            return

        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        metrics = self._grid_metrics
        inset = metrics.edge_inset_px
        pitch = metrics.pitch
        cell_size = metrics.cell_size

        # 网格线样式
        dash_segment = max(2, cell_size * 0.25)
        grid_color = QColor(200, 200, 200, 220) 
        grid_pen = QPen(grid_color)
        grid_pen.setWidthF(1.0)
        grid_pen.setDashPattern([dash_segment, dash_segment])  # 自适应虚线
        painter.setPen(grid_pen)

        # 计算可用区域右/下边缘
        cw = self.width()
        ch = self.height()
        right_edge = cw - inset
        bottom_edge = ch - inset

        # 竖直线：从 inset 开始，每隔 pitch 画一条，直到超出右边缘
        col = 0
        while True:
            x = inset + col * pitch
            if x > right_edge:
                break
            painter.drawLine(int(x), int(inset), int(x), int(bottom_edge))
            col += 1
        # 右边缘闭合线
        painter.drawLine(int(right_edge), int(inset), int(right_edge), int(bottom_edge))

        # 水平线：从 inset 开始，每隔 pitch 画一条，直到超出下边缘
        row = 0
        while True:
            y = inset + row * pitch
            if y > bottom_edge:
                break
            painter.drawLine(int(inset), int(y), int(right_edge), int(y))
            row += 1
        # 下边缘闭合线
        painter.drawLine(int(inset), int(bottom_edge), int(right_edge), int(bottom_edge))

        # 绘制预览框
        if self._preview_visible:
            # 像素模式 使用像素坐标
            rect = QRectF(
                self._preview_x,
                self._preview_y,
                self._preview_width_px,
                self._preview_height_px
            )

            if rect:
                # 颜色
                if self._preview_collision:
                    # 红色 - 碰撞/无效
                    border_color = QColor(255, 59, 48, 255)  # #FF3B30
                    fill_color = QColor(255, 59, 48, 100)    # 红色
                else:
                    # 蓝色 - 正常
                    border_color = QColor(10, 132, 255, 255)  # #FF0A84FF
                    fill_color = QColor(10, 132, 255, 100)    # 蓝色

                # 圆角动态计算
                min_side = min(rect.width(), rect.height())
                corner_radius = max(14, min(26, min_side * 0.11))

                # 绘制填充
                painter.setPen(Qt.PenStyle.NoPen)
                painter.setBrush(fill_color)
                path = QPainterPath()
                path.addRoundedRect(rect, corner_radius, corner_radius)
                painter.drawPath(path)

                # 绘制边框
                border_pen = QPen(border_color)
                border_pen.setWidthF(2)
                painter.setPen(border_pen)
                painter.setBrush(Qt.BrushStyle.NoBrush)
                painter.drawRoundedRect(rect, corner_radius, corner_radius)

        painter.end()
