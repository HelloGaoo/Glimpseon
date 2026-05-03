import logging
import os
import sys
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:sys.path.insert(0, _BASE_DIR)

import re
import time
import webbrowser
from concurrent.futures import ThreadPoolExecutor

import pythoncom
from PyQt6.QtCore import (
    QFileInfo,
    QPointF,
    QRectF,
    Qt,
    QTimer,
    pyqtProperty,
    QSize,
    pyqtSignal,
)
from PyQt6.QtGui import (
    QBrush,
    QColor,
    QCursor,
    QFont,
    QFontMetrics,
    QLinearGradient,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
)
from PyQt6.QtWidgets import QFileIconProvider, QLabel, QSizePolicy, QWidget
from qfluentwidgets import InfoBar, isDarkTheme, RoundMenu, Action, FluentIcon as FIF
from win32com.shell import shell, shellcon

from core.config import cfg, save_cfg

logger = logging.getLogger(__name__)

DEFAULT_ICON_DIR = 'default_icon'

def get_default_icon_path(icon_filename='exe.ico'):
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base_dir, 'data', DEFAULT_ICON_DIR, icon_filename)

def get_ql_icon_path(icon_filename):
    if not icon_filename:
        return None
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon_path = os.path.join(base_dir, 'data', 'ql_icon', icon_filename)
    if os.path.exists(icon_path):
        return icon_path
    sw_path = os.path.join(base_dir, 'data', 'software_icon', icon_filename)
    if os.path.exists(sw_path):
        return sw_path
    default_icon = get_default_icon_path(icon_filename)
    if os.path.exists(default_icon):
        return default_icon
    return None

def get_ql_icon_save_dir():
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon_dir = os.path.join(base_dir, 'data', 'ql_icon')
    os.makedirs(icon_dir, exist_ok=True)
    return icon_dir

def get_folder_icon():
    return 'Directory.ico'

def get_url_icon():
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon_path = os.path.join(base_dir, 'data', 'software_icon', 'url.ico')
    if os.path.exists(icon_path):
        return 'url.ico'
    return 'exe.ico'

def resolve_app_from_path(file_path):
    real_path = file_path
    app_type = "app"
    
    if file_path.lower().endswith('.lnk'):
        try:
            shortcut = pythoncom.CoCreateInstance(
                shell.CLSID_ShellLink, None, pythoncom.CLSCTX_INPROC_SERVER, shell.IID_IShellLink
            )
            persist = shortcut.QueryInterface(pythoncom.IID_IPersistFile)
            persist.Load(file_path)
            real_path = shortcut.GetPath(shell.SLGP_RAWPATH)[0]
            if not real_path:
                real_path = file_path
        except Exception:
            pass

    if file_path.lower().endswith('.lnk'):
        name = os.path.splitext(os.path.basename(file_path))[0]
    else:
        name = os.path.splitext(os.path.basename(real_path))[0]
    
    if os.path.isdir(real_path):
        app_type = "folder"
        icon_filename = get_folder_icon()
        return {"name": name, "path": real_path, "icon": icon_filename, "type": app_type}
    
    provider = QFileIconProvider()
    fi = QFileInfo(real_path if os.path.exists(real_path) else file_path)
    icon = provider.icon(fi)
    icon_filename = 'exe.ico'
    sizes = icon.availableSizes()
    if sizes:
        best_size = max(sizes, key=lambda s: s.width() * s.height())
        pixmap = icon.pixmap(best_size)
        if not pixmap.isNull():
            target_size = 256
            if pixmap.width() < target_size:
                pixmap = pixmap.scaled(target_size, target_size, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
            cleaned_name = re.sub(r'[^\w\u4e00-\u9fff]', '', name)
            if cleaned_name:
                icon_filename = cleaned_name + '.ico'
            else:
                icon_filename = 'default.ico'
            icon_dir = get_ql_icon_save_dir()
            icon_save_path = os.path.join(icon_dir, icon_filename)
            pixmap.save(icon_save_path, 'PNG')

    return {"name": name, "path": real_path, "icon": icon_filename, "type": app_type}

def resolve_url_from_string(url_string, name=None):
    url = url_string.strip()
    if not url.startswith(('http://', 'https://')):
        url = 'https://' + url
    
    if not name:
        try:
            from urllib.parse import urlparse
            parsed = urlparse(url)
            name = parsed.netloc or url
        except Exception:
            name = url
    
    icon_filename = get_url_icon()
    return {"name": name, "path": url, "icon": icon_filename, "type": "url"}


class QuickLaunchDock(QWidget):
    MAX_SCALE = 1.45
    BASE_SCALE = 1.0
    MAGNIFY_RANGE = 100
    ANIM_SPEED = 0.22
    BOUNCE_H = 14
    BOUNCE_DUR = 800
    PAD_X = 20
    PAD_Y_BOTTOM = 6
    PAD_Y_TOP = 6
    RADIUS = 16
    FPS = 120
    MAX_APPS = 12
    
    _launch_result = pyqtSignal(str, str, bool)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("quickLaunchDock")
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Maximum)
        self.setMouseTracking(True)
        self.setAcceptDrops(True)
        
        self._executor = ThreadPoolExecutor(max_workers=1)
        self._launch_result.connect(self._on_launch_result)
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._show_labels = cfg.quickLaunchShowLabels.value

        self._apps = []
        self._pixmaps = []
        self._scales = []
        self._target_scales = []
        self._hover_idx = -1
        self._bounce_idx = -1
        self._bounce_y = 0.0
        self._bounce_active = False
        self._bounce_start_time = 0.0
        self._painting = False
        self._last_frame = 0.0
        
        self._dragging_idx = -1
        self._drag_start_pos = None
        self._is_internal_drag = False
        self._drop_target_idx = -1
        self._drag_pos = None

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._tick)
        self._timer.start(int(1000 / self.FPS))

    def _sz(self):
        return cfg.quickLaunchIconSize.value

    def set_apps(self, apps, animate_idx=-1):
        self._apps = list(apps)
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._pixmaps = []
        for a in apps:
            fn = a.get("icon", "exe.ico")
            p = get_ql_icon_path(fn)
            pm = None
            if p and os.path.exists(p):
                raw = QPixmap(p)
                if not raw.isNull():
                    dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
                    raw.setDevicePixelRatio(dpr)
                    pm = raw
            self._pixmaps.append(pm)
        n = len(apps)
        self._scales = [self.BASE_SCALE] * n
        self._target_scales = [self.BASE_SCALE] * n
        self._fix_size()
        
        if animate_idx >= 0 and animate_idx < n:
            self._start_add_animation(animate_idx)
        
        self.update()
    
    def _start_add_animation(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        self._start_bounce(idx)

    def update_icon_size(self, size):
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._show_labels = cfg.quickLaunchShowLabels.value
        self._pixmaps = []
        for a in self._apps:
            fn = a.get("icon", "exe.ico")
            p = get_ql_icon_path(fn)
            pm = None
            if p and os.path.exists(p):
                raw = QPixmap(p)
                if not raw.isNull():
                    dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
                    raw.setDevicePixelRatio(dpr)
                    pm = raw
            self._pixmaps.append(pm)
        self._fix_size()
        self.update()

    def _bg_rect(self):
        sz = self._sz()
        n = len(self._apps)
        if n == 0:
            return QRectF()
        w = n * sz + (n - 1) * self._icon_gap + self.PAD_X * 2
        h = sz + self.PAD_Y_TOP + self.PAD_Y_BOTTOM
        x = (self.width() - w) / 2
        y = self.height() - h - cfg.quickLaunchOffsetY.value
        return QRectF(x, y, w, h)

    def _fix_size(self):
        sz = self._sz()
        n = len(self._apps)
        if n == 0:
            self.setFixedSize(0, 0)
            return
        w_icons = n * sz + (n - 1) * self._icon_gap + self.PAD_X * 2
        h_icons = sz + self.PAD_Y_TOP + self.PAD_Y_BOTTOM
        scale_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE))
        bounce_overflow = self.BOUNCE_H + 10
        side_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE) * 0.3)
        label_overflow = 28 if self._show_labels else 0
        offset_y = cfg.quickLaunchOffsetY.value
        drag_extra = int(sz * 0.5)
        w = w_icons + side_overflow * 2 + drag_extra
        h = h_icons + scale_overflow + bounce_overflow + label_overflow + offset_y + drag_extra
        self.setFixedSize(w, h)

    def _icon_positions(self):
        sz = self._sz()
        n = len(self._scales)
        if n == 0:
            return []

        widths = [sz * sc for sc in self._scales]
        total = sum(widths) + (n - 1) * self._icon_gap
        bg = self._bg_rect()
        content_w = bg.width() - self.PAD_X * 2
        start_x = bg.x() + self.PAD_X + (content_w - total) / 2

        pos = []
        cx = start_x
        for i in range(n):
            pos.append(cx + widths[i] / 2)
            cx += widths[i] + self._icon_gap
        return pos

    def _icon_rect(self, i, positions=None):
        if positions is None:
            positions = self._icon_positions()
        s = self._sz() * self._scales[i]
        cx = positions[i]
        bg = self._bg_rect()
        by = bg.y() + bg.height() - self.PAD_Y_BOTTOM
        return QRectF(cx - s / 2, by - s, s, s)

    def _smoothstep(self, t):
        t = max(0.0, min(1.0, t))
        return t * t * (3.0 - 2.0 * t)

    def _get_icon_at_pos(self, pos):
        if not self._apps:
            return -1
        pos_list = self._icon_positions()
        for i in range(len(self._apps)):
            r = self._icon_rect(i, pos_list)
            if r.contains(pos):
                return i
        return -1

    def mouseMoveEvent(self, e):
        if self._dragging_idx >= 0 and self._drag_start_pos and not self._is_internal_drag:
            dist = (e.position() - self._drag_start_pos).manhattanLength()
            if dist > 10:
                self._start_internal_drag(self._dragging_idx)
        
        if self._is_internal_drag and self._dragging_idx >= 0:
            rect = self.rect()
            sz = self._sz() * self.MAX_SCALE
            pos = e.position()
            x = max(rect.x() + sz / 2, min(pos.x(), rect.x() + rect.width() - sz / 2))
            y = max(rect.y() + sz / 2, min(pos.y(), rect.y() + rect.height() - sz / 2))
            self._drag_pos = QPointF(x, y)
            self._update_drop_target(e.position())
            self.update()
        
        self._calc_targets(e.position())
        super().mouseMoveEvent(e)

    def mousePressEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(e.position()):
                    self._drag_start_pos = e.position()
                    self._dragging_idx = i
                    break
        elif e.button() == Qt.MouseButton.RightButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(e.position()):
                    self._show_context_menu(i, e.position())
                    break
        super().mousePressEvent(e)

    def mouseReleaseEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton:
            if self._is_internal_drag and self._dragging_idx >= 0:
                self._finish_drag_reorder()
            elif self._dragging_idx >= 0 and self._drag_start_pos:
                pl = self._icon_positions()
                if self._icon_rect(self._dragging_idx, pl).contains(e.position()):
                    self._click(self._dragging_idx)
            
            self._dragging_idx = -1
            self._drag_start_pos = None
            self._is_internal_drag = False
            self._drop_target_idx = -1
            self._drag_pos = None
            self.update()
        
        super().mouseReleaseEvent(e)

    def _start_internal_drag(self, idx):
        self._is_internal_drag = True
        self._dragging_idx = idx
        self._drag_pos = self._drag_start_pos
        self._drop_target_idx = idx
        self.update()

    def _update_drop_target(self, pos):
        if not self._apps:
            return
        
        sz = self._sz()
        bg = self._bg_rect()
        content_w = bg.width() - self.PAD_X * 2
        n = len(self._apps)
        total_w = n * sz + (n - 1) * self._icon_gap
        start_x = bg.x() + self.PAD_X + (content_w - total_w) / 2
        
        new_target = -1
        for i in range(n):
            icon_x = start_x + i * (sz + self._icon_gap)
            if pos.x() < icon_x + sz / 2:
                new_target = i
                break
        
        if new_target == -1:
            new_target = n
        
        if new_target != self._drop_target_idx:
            self._drop_target_idx = new_target

    def _finish_drag_reorder(self):
        if self._dragging_idx < 0 or self._drop_target_idx < 0:
            return
        
        if self._dragging_idx == self._drop_target_idx:
            return
        
        apps = list(self._apps)
        dragged_app = apps.pop(self._dragging_idx)
        
        insert_idx = self._drop_target_idx
        if insert_idx > self._dragging_idx:
            insert_idx -= 1
        
        apps.insert(insert_idx, dragged_app)
        
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=insert_idx)
        
        logger.info(f"快捷启动栏顺序已调整: {self._dragging_idx} -> {insert_idx}")

    def _show_context_menu(self, idx, pos):
        if idx < 0 or idx >= len(self._apps):
            return
        
        app = self._apps[idx]
        menu = RoundMenu(app.get("name", "应用"), self)
        
        open_action = Action(FIF.PLAY, "打开", self)
        open_action.triggered.connect(lambda: self._click(idx))
        menu.addAction(open_action)
        
        menu.addSeparator()
        
        edit_action = Action(FIF.EDIT, "编辑", self)
        edit_action.triggered.connect(lambda: self._edit_app(idx))
        menu.addAction(edit_action)
        
        delete_action = Action(FIF.DELETE, "删除", self)
        delete_action.triggered.connect(lambda: self._delete_app(idx))
        menu.addAction(delete_action)
        
        menu.addSeparator()
        
        app_type = app.get("type", "app")
        if app_type == "url":
            path_info = f"网址: {app.get('path', '')}"
        else:
            path_info = f"路径: {app.get('path', '')}"
        
        info_action = Action(FIF.INFO, path_info, self)
        info_action.setEnabled(False)
        menu.addAction(info_action)
        
        menu.exec(QCursor.pos())

    def _edit_app(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        
        from ui.edit_panel import AppEditDialog
        
        dialog = AppEditDialog(self.window(), self._apps[idx])
        if dialog.exec():
            result = dialog.get_app_data()
            if result:
                self._apps[idx] = result
                cfg.quickLaunchApps.value = self._apps
                save_cfg()
                self.set_apps(self._apps)
                InfoBar.success("保存成功", "快捷方式已更新", parent=self.window(), duration=2000)

    def _delete_app(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        
        app_name = self._apps[idx].get("name", "此应用")
        
        mw = self.window()
        mask = QWidget(mw)
        mask.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        mask.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        mask.setGeometry(0, 0, mw.width(), mw.height())
        mask.setStyleSheet("background-color: rgba(0, 0, 0, 120);")
        mask.show()
        
        from qfluentwidgets import MessageBox
        box = MessageBox("确认删除", f"确定要从快捷启动栏删除 \"{app_name}\" 吗？", mask)
        box.yesButton.setText("删除")
        box.cancelButton.setText("取消")
        
        if box.exec():
            self._apps.pop(idx)
            cfg.quickLaunchApps.value = self._apps
            save_cfg()
            self.set_apps(self._apps)
            InfoBar.success("删除成功", f"已删除 {app_name}", parent=self.window(), duration=2000)
        
        mask.close()
        mask.deleteLater()

    def leaveEvent(self, e):
        n = len(self._target_scales)
        self._target_scales = [self.BASE_SCALE] * n
        self._hover_idx = -1
        super().leaveEvent(e)

    def _calc_targets(self, pos):
        if not self._apps:
            return
        mx = pos.x()
        my = pos.y()
        pos_list = self._icon_positions()
        new_hover = -1

        for i in range(len(self._apps)):
            r = self._icon_rect(i, pos_list)
            if r.contains(pos):
                new_hover = i
                break

        if new_hover < 0:
            bg = self._bg_rect()
            if bg.contains(pos):
                min_dist = float('inf')
                for i in range(len(self._apps)):
                    cx = pos_list[i]
                    d = abs(mx - cx)
                    if d < min_dist:
                        min_dist = d
                        new_hover = i

        for i in range(len(self._apps)):
            if new_hover >= 0 and abs(i - new_hover) <= 2:
                cx = pos_list[i]
                d = abs(mx - cx)
                if d < self.MAGNIFY_RANGE:
                    t = self._smoothstep(1.0 - d / self.MAGNIFY_RANGE)
                    sc = self.BASE_SCALE + (self.MAX_SCALE - self.BASE_SCALE) * t
                else:
                    sc = self.BASE_SCALE
            else:
                sc = self.BASE_SCALE
            self._target_scales[i] = sc

        if new_hover != self._hover_idx:
            self._hover_idx = new_hover

    def _tick(self):
        now = time.time()
        dt = min(now - self._last_frame, 0.05) if self._last_frame > 0 else 0.016
        self._last_frame = now
        changed = False

        for i in range(len(self._scales)):
            if i >= len(self._target_scales):
                break
            cur = self._scales[i]
            tgt = self._target_scales[i]
            diff = tgt - cur
            if abs(diff) > 0.001:
                sp = self.ANIM_SPEED * (60.0 * dt)
                if abs(diff) < 0.008:
                    self._scales[i] = tgt
                else:
                    self._scales[i] += diff * min(sp, 1.0)
                changed = True

        if self._bounce_active:
            elapsed = (now - self._bounce_start_time) * 1000.0
            dur = float(self.BOUNCE_DUR)
            bh = float(self.BOUNCE_H)
            if elapsed >= dur:
                self._bounce_y = 0.0
                self._bounce_active = False
                self._bounce_idx = -1
            else:
                t = elapsed / dur
                kfs = [
                    (0.00, 0.0), (0.14, -bh), (0.28, 0.0),
                    (0.44, -bh * 0.50), (0.58, 0.0),
                    (0.72, -bh * 0.22), (0.86, 0.0), (1.00, 0.0),
                ]
                lo_t, lo_v = kfs[0], kfs[1]
                for j in range(len(kfs) - 1):
                    if kfs[j][0] <= t <= kfs[j + 1][0]:
                        lo_t, lo_v = kfs[j], kfs[j + 1]
                        break
                span = lo_v[0] - lo_t[0]
                if span > 0:
                    lt = (t - lo_t[0]) / span
                    lt = lt * lt * (3.0 - 2.0 * lt)
                    self._bounce_y = lo_t[1] + (lo_v[1] - lo_t[1]) * lt
                else:
                    self._bounce_y = lo_v[1]
                changed = True

        if changed:
            self.update()

    def dragEnterEvent(self, e):
        if e.mimeData().hasUrls():
            urls = e.mimeData().urls()
            for url in urls:
                path = url.toLocalFile()
                if path:
                    if path.lower().endswith(('.exe', '.lnk')) or os.path.isdir(path):
                        e.acceptProposedAction()
                        return
        elif e.mimeData().hasText():
            text = e.mimeData().text().strip()
            if text.startswith(('http://', 'https://', 'www.')):
                e.acceptProposedAction()
                return
        e.ignore()

    def dragMoveEvent(self, e):
        if e.mimeData().hasUrls() or e.mimeData().hasText():
            e.acceptProposedAction()
        else:
            e.ignore()

    def dropEvent(self, e):
        e.acceptProposedAction()
        
        if e.mimeData().hasUrls():
            urls = e.mimeData().urls()
            for url in urls:
                path = url.toLocalFile()
                if not path:
                    continue
                if os.path.isdir(path):
                    self._add_folder_from_path(path)
                elif path.lower().endswith(('.exe', '.lnk')):
                    self._add_app_from_path(path)
        
        elif e.mimeData().hasText():
            text = e.mimeData().text().strip()
            if text.startswith(('http://', 'https://', 'www.')):
                self._add_url(text)

    def _add_app_from_path(self, file_path):
        new_app = resolve_app_from_path(file_path)
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title="数量限制",
                content=f"快捷启动栏最多只能添加 {self.MAX_APPS} 个应用",
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_app)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success("添加成功", f"已添加 {new_app['name']}", parent=self.window(), duration=2000)

    def _add_folder_from_path(self, folder_path):
        name = os.path.basename(folder_path)
        new_item = {
            "name": name,
            "path": folder_path,
            "icon": get_folder_icon(),
            "type": "folder"
        }
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title="数量限制",
                content=f"快捷启动栏最多只能添加 {self.MAX_APPS} 个项目",
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_item)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success("添加成功", f"已添加文件夹 {name}", parent=self.window(), duration=2000)

    def _add_url(self, url):
        new_item = resolve_url_from_string(url)
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title="数量限制",
                content=f"快捷启动栏最多只能添加 {self.MAX_APPS} 个项目",
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_item)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success("添加成功", f"已添加网址 {new_item['name']}", parent=self.window(), duration=2000)

    def _click(self, idx):
        a = self._apps[idx]
        path = a.get("path", "")
        name = a.get("name", "")
        app_type = a.get("type", "app")
        self._start_bounce(idx)
        if path:
            self._executor.submit(self._launch_thread, path, name, app_type)

    def _launch_thread(self, target, name, app_type):
        try:
            if not target:
                self._launch_result.emit(name, "未配置路径", False)
                return
            
            if app_type == "url":
                webbrowser.open(target)
                self._launch_result.emit(name, target, True)
            elif app_type == "folder":
                if os.path.exists(target):
                    os.startfile(target)
                    self._launch_result.emit(name, target, True)
                else:
                    self._launch_result.emit(name, f"文件夹不存在: {target}", False)
            else:
                if os.path.exists(target):
                    os.startfile(target)
                    self._launch_result.emit(name, target, True)
                else:
                    self._launch_result.emit(name, f"路径不存在: {target}", False)
        except Exception as e:
            self._launch_result.emit(name, str(e), False)

    def _launch_app_thread(self, app_path, app_name):
        self._launch_thread(app_path, app_name, "app")

    def _on_launch_result(self, app_name, info, success):
        if success:
            logger.info(f"已启动：{app_name} ({info})")
            InfoBar.success(
                title="启动成功",
                content=f"正在打开 {app_name}",
                parent=self.window(),
                duration=2000
            )
        else:
            logger.warning(f"启动失败：{app_name}, {info}")
            InfoBar.error(
                title="启动失败",
                content=f"{app_name}: {info}",
                parent=self.window(),
                duration=3000
            )

    def _start_bounce(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        self._bounce_idx = idx
        self._bounce_y = 0.0
        self._bounce_active = True
        self._bounce_start_time = time.time()

    def _get_by(self):
        return self._bounce_y

    def _set_by(self, v):
        self._bounce_y = v
        self.update()

    bounceY = pyqtProperty(float, _get_by, _set_by)

    def paintEvent(self, event):
        if self._painting:
            return
        self._painting = True
        try:
            self._render()
        finally:
            self._painting = False

    def _render(self):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)

        bg = self._bg_rect()
        if bg.isEmpty():
            p.end()
            return

        dark = isDarkTheme()

        path = QPainterPath()
        path.addRoundedRect(bg, self.RADIUS, self.RADIUS)

        if dark:
            bg_c = QColor(30, 30, 32, 165)
            brd_c = QColor(255, 255, 255, 20)
            sh_top = QColor(255, 255, 255, 22)
            sh_mid = QColor(255, 255, 255, 6)
            inner_glow = QColor(255, 255, 255, 8)
        else:
            bg_c = QColor(235, 235, 240, 172)
            brd_c = QColor(0, 0, 0, 12)
            sh_top = QColor(255, 255, 255, 95)
            sh_mid = QColor(255, 255, 255, 18)
            inner_glow = QColor(255, 255, 255, 25)

        p.setPen(Qt.PenStyle.NoPen)

        shadow_path = QPainterPath()
        sr = QRectF(bg.x() + 1.5, bg.y() + 2, bg.width() - 3, bg.height() * 0.5)
        shadow_path.addRoundedRect(sr, self.RADIUS - 3, self.RADIUS - 3)
        p.setBrush(QBrush(inner_glow))
        p.drawPath(shadow_path)

        p.setBrush(bg_c)
        p.drawPath(path)

        grad = QLinearGradient(bg.x(), bg.y(), bg.x(), bg.y() + bg.height())
        grad.setColorAt(0.0, sh_top)
        grad.setColorAt(0.30, sh_mid)
        grad.setColorAt(1.0, QColor(0, 0, 0, 0))

        p.setCompositionMode(QPainter.CompositionMode.CompositionMode_Lighten)
        p.fillPath(path, grad)
        p.setCompositionMode(QPainter.CompositionMode.CompositionMode_SourceOver)

        pen = QPen(brd_c)
        pen.setWidth(1)
        p.setPen(pen)
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawPath(path)

        pl = self._icon_positions()
        baseline_y = bg.y() + bg.height() - self.PAD_Y_BOTTOM
        sz = self._sz()

        for i in range(len(self._apps)):
            if self._is_internal_drag and i == self._dragging_idx:
                continue
            
            pm = self._pixmaps[i]
            sc = self._scales[i]
            s = sz * sc
            
            if self._is_internal_drag and self._dragging_idx >= 0:
                if i >= self._drop_target_idx and i < self._dragging_idx:
                    cx = pl[i + 1]
                elif i < self._drop_target_idx and i > self._dragging_idx:
                    cx = pl[i - 1]
                else:
                    cx = pl[i]
            else:
                cx = pl[i]
            
            top = baseline_y - s
            if i == self._bounce_idx:
                top += self._bounce_y
            
            if pm and not pm.isNull():
                p.drawPixmap(
                    QRectF(cx - s / 2, top, s, s),
                    pm,
                    QRectF(0, 0, pm.width(), pm.height()),
                )
            else:
                p.setBrush(QColor(120, 120, 120, 60))
                p.setPen(QPen(QColor(120, 120, 120, 100), 1))
                r = QRectF(cx - s / 2, top, s, s)
                p.drawRoundedRect(r, 8, 8)
                p.setPen(QPen(QColor(180, 180, 180, 150), 2))
                font = p.font()
                font.setPixelSize(int(s * 0.4))
                p.setFont(font)
                p.drawText(r, Qt.AlignmentFlag.AlignCenter, "?")
            
            if i == self._hover_idx and self._show_labels and not self._is_internal_drag:
                name = self._apps[i].get("name", "")
                if name:
                    label_font = p.font()
                    label_font.setFamily("HarmonyOS Sans,Microsoft YaHei,sans-serif")
                    label_font.setPixelSize(14)
                    label_font.setWeight(QFont.Weight.Medium)
                    p.setFont(label_font)
                    fm = QFontMetrics(label_font)
                    
                    display_name = name
                    if len(name) > 50:
                        display_name = name[:50] + "..."
                    
                    padding_x = 10
                    label_w = fm.horizontalAdvance(display_name) + padding_x * 2
                    label_h = 24
                    label_x = cx - label_w / 2
                    label_y = top - label_h - 4
                    
                    widget_rect = self.rect()
                    if label_x < widget_rect.left() + 2:
                        label_x = widget_rect.left() + 2
                    if label_x + label_w > widget_rect.right() - 2:
                        label_x = widget_rect.right() - label_w - 2
                    if label_y < widget_rect.top() + 2:
                        label_y = top + sz + 4
                    
                    label_path = QPainterPath()
                    label_path.addRoundedRect(label_x, label_y, label_w, label_h, label_h / 2, label_h / 2)
                    p.setPen(Qt.PenStyle.NoPen)
                    p.setBrush(QColor(0, 0, 0, 220))
                    p.drawPath(label_path)
                    p.setPen(QColor(255, 255, 255, 255))
                    p.setFont(label_font)
                    text_rect = QRectF(label_x, label_y, label_w, label_h)
                    p.drawText(text_rect, Qt.AlignmentFlag.AlignCenter | Qt.TextFlag.TextSingleLine, display_name)

        if self._is_internal_drag and self._dragging_idx >= 0 and self._drag_pos:
            pm = self._pixmaps[self._dragging_idx] if self._dragging_idx < len(self._pixmaps) else None
            s = sz * self.MAX_SCALE
            cx = self._drag_pos.x()
            top = self._drag_pos.y() - s / 2
            
            if pm and not pm.isNull():
                p.drawPixmap(
                    QRectF(cx - s / 2, top, s, s),
                    pm,
                    QRectF(0, 0, pm.width(), pm.height()),
                )
            else:
                p.setBrush(QColor(120, 120, 120, 100))
                p.setPen(QPen(QColor(120, 120, 120, 150), 1))
                r = QRectF(cx - s / 2, top, s, s)
                p.drawRoundedRect(r, 8, 8)

        p.end()

    def minimumSizeHint(self):
        bg = self._bg_rect()
        return QSize(int(bg.width()), int(bg.height()))

    def hideEvent(self, e):
        self._timer.stop()
        super().hideEvent(e)

    def showEvent(self, e):
        self._timer.start(int(1000 / self.FPS))
        super().showEvent(e)
