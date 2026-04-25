import os
import time
from concurrent.futures import ThreadPoolExecutor

from PyQt5.QtCore import (
    QPoint,
    QPointF,
    QRectF,
    Qt,
    QTimer,
    pyqtProperty,
    QSize,
    pyqtSignal,
    QThread,
)
from PyQt5.QtGui import (
    QBrush,
    QColor,
    QFont,
    QFontMetrics,
    QLinearGradient,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
)
from PyQt5.QtWidgets import QLabel, QSizePolicy, QVBoxLayout, QWidget

from core.config import cfg

def get_ql_icon_path(icon_filename):
    if not icon_filename:return None
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon_path = os.path.join(base_dir, 'data', 'ql_icon', icon_filename)
    if os.path.exists(icon_path):return icon_path
    sw_path = os.path.join(base_dir, 'data', 'software_icon', icon_filename)
    if os.path.exists(sw_path):return sw_path
    exe_icon = os.path.join(base_dir, 'data', 'software_icon', 'exe.ico')
    if os.path.exists(exe_icon):return exe_icon
    return None

def get_ql_icon_save_dir():
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon_dir = os.path.join(base_dir, 'data', 'ql_icon')
    os.makedirs(icon_dir, exist_ok=True)
    return icon_dir


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
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setSizePolicy(QSizePolicy.Maximum, QSizePolicy.Maximum)
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

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._tick)
        self._timer.start(int(1000 / self.FPS))

    def _sz(self):
        return cfg.quickLaunchIconSize.value

    def set_apps(self, apps):
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
        self.update()

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
        if n == 0:return QRectF()
        w = n * sz + (n - 1) * self._icon_gap + self.PAD_X * 2
        h = sz + self.PAD_Y_TOP + self.PAD_Y_BOTTOM
        x = (self.width() - w) / 2
        y = self.height() - h
        return QRectF(x, y, w, h)

    def _fix_size(self):
        bg = self._bg_rect()
        if bg.width() > 0:
            sz = self._sz()
            scale_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE))
            bounce_overflow = self.BOUNCE_H + 10
            side_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE) * 0.3)
            label_overflow = 24 if self._show_labels else 0
            overflow = scale_overflow + bounce_overflow + label_overflow
            w = int(bg.width()) + side_overflow * 2
            h = int(bg.height()) + overflow
            self.setFixedSize(w, h)

    def _icon_positions(self):
        sz = self._sz()
        n = len(self._scales)
        if n == 0:return []

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
        if positions is None:positions = self._icon_positions()
        s = self._sz() * self._scales[i]
        cx = positions[i]
        bg = self._bg_rect()
        by = bg.y() + bg.height() - self.PAD_Y_BOTTOM
        return QRectF(cx - s / 2, by - s, s, s)

    def _smoothstep(self, t):
        t = max(0.0, min(1.0, t))
        return t * t * (3.0 - 2.0 * t)

    def mouseMoveEvent(self, e):
        self._calc_targets(e.pos())
        super().mouseMoveEvent(e)

    def leaveEvent(self, e):
        n = len(self._target_scales)
        self._target_scales = [self.BASE_SCALE] * n
        self._hover_idx = -1
        super().leaveEvent(e)

    def _calc_targets(self, pos):
        if not self._apps:return
        mx = pos.x()
        pos_list = self._icon_positions()
        new_hover = -1

        for i in range(len(self._apps)):
            r = self._icon_rect(i, pos_list)
            if r.contains(QPointF(pos)):
                new_hover = i
                break

        if new_hover < 0:
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
            if i >= len(self._target_scales):break
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

    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(QPointF(e.pos())):
                    self._click(i)
                    break
        super().mousePressEvent(e)

    def dragEnterEvent(self, e):
        if e.mimeData().hasUrls():
            urls = e.mimeData().urls()
            for url in urls:
                path = url.toLocalFile()
                if path and (path.lower().endswith('.exe') or path.lower().endswith('.lnk')):
                    e.acceptProposedAction()
                    return
        e.ignore()

    def dragMoveEvent(self, e):
        if e.mimeData().hasUrls():
            e.acceptProposedAction()
        else:
            e.ignore()

    def dropEvent(self, e):
        e.acceptProposedAction()
        urls = e.mimeData().urls()
        for url in urls:
            path = url.toLocalFile()
            if not path:
                continue
            if not (path.lower().endswith('.exe') or path.lower().endswith('.lnk')):
                continue
            self._add_app_from_path(path)

    def _add_app_from_path(self, file_path):
        import re
        from PyQt5.QtWidgets import QFileIconProvider
        from PyQt5.QtCore import QFileInfo

        real_path = file_path
        if file_path.lower().endswith('.lnk'):
            try:
                import pythoncom
                from win32com.shell import shell, shellcon
                shortcut = pythoncom.CoCreateInstance(
                    shell.CLSID_ShellLink, None, pythoncom.CLSCTX_INPROC_SERVER, shell.IID_IShellLink
                )
                persist = shortcut.QueryInterface(pythoncom.IID_IPersistFile)
                persist.Load(file_path)
                real_path = shortcut.GetPath(shell.SLGP_SHORTPATH)[0]
                if not real_path:
                    real_path = file_path
            except Exception:
                pass

        if file_path.lower().endswith('.lnk'):
            name = os.path.splitext(os.path.basename(file_path))[0]
        else:
            name = os.path.splitext(os.path.basename(real_path))[0]

        provider = QFileIconProvider()
        fi = QFileInfo(real_path if os.path.exists(real_path) else file_path)
        icon = provider.icon(fi)

        icon_filename = 'default.ico'
        sizes = icon.availableSizes()
        if sizes:
            best_size = max(sizes, key=lambda s: s.width() * s.height())
            pixmap = icon.pixmap(best_size)
            if not pixmap.isNull():
                target_size = 256
                if pixmap.width() < target_size:
                    pixmap = pixmap.scaled(target_size, target_size, Qt.KeepAspectRatio, Qt.SmoothTransformation)
                cleaned_name = re.sub(r'[^\w\u4e00-\u9fff]', '', name)
                if cleaned_name:
                    icon_filename = cleaned_name + '.ico'
                else:
                    icon_filename = 'default.ico'
                icon_dir = get_ql_icon_save_dir()
                icon_save_path = os.path.join(icon_dir, icon_filename)
                pixmap.save(icon_save_path, 'PNG')

        new_app = {"name": name, "path": real_path, "icon": icon_filename}
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            from qfluentwidgets import InfoBar
            InfoBar.warning(
                title="数量限制",
                content=f"快捷启动栏最多只能添加 {self.MAX_APPS} 个应用",
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_app)
        cfg.quickLaunchApps.value = apps
        from core.config import save_cfg
        save_cfg()

    def _click(self, idx):
        a = self._apps[idx]
        path = a.get("path", "")
        name = a.get("name", "")
        self._start_bounce(idx)
        if path:
            self._executor.submit(self._launch_app_thread, path, name)

    def _launch_app_thread(self, app_path, app_name):
        try:
            if not app_path:
                self._launch_result.emit(app_name, "未配置路径", False)
                return
            
            if os.path.exists(app_path):
                os.startfile(app_path)
                self._launch_result.emit(app_name, app_path, True)
            else:
                self._launch_result.emit(app_name, f"路径不存在: {app_path}", False)
        except Exception as e:
            self._launch_result.emit(app_name, str(e), False)

    def _on_launch_result(self, app_name, info, success):
        import logging
        from qfluentwidgets import InfoBar
        logger = logging.getLogger(__name__)
        
        if success:
            logger.info(f"已启动应用：{app_name} ({info})")
            InfoBar.success(
                title="启动成功",
                content=f"正在打开 {app_name}",
                parent=self.window(),
                duration=2000
            )
        else:
            logger.warning(f"启动应用失败：{app_name}, {info}")
            InfoBar.error(
                title="启动失败",
                content=f"{app_name}: {info}",
                parent=self.window(),
                duration=3000
            )

    def _start_bounce(self, idx):
        if idx < 0 or idx >= len(self._apps):return
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
        if self._painting:return
        self._painting = True
        try:
            self._render()
        finally:
            self._painting = False

    def _render(self):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)
        p.setRenderHint(QPainter.SmoothPixmapTransform)

        bg = self._bg_rect()
        if bg.isEmpty():
            p.end()
            return

        from qfluentwidgets import isDarkTheme
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

        p.setPen(Qt.NoPen)

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

        p.setCompositionMode(QPainter.CompositionMode_Lighten)
        p.fillPath(path, grad)
        p.setCompositionMode(QPainter.CompositionMode_SourceOver)

        pen = QPen(brd_c)
        pen.setWidth(1)
        p.setPen(pen)
        p.setBrush(Qt.NoBrush)
        p.drawPath(path)

        pl = self._icon_positions()
        baseline_y = bg.y() + bg.height() - self.PAD_Y_BOTTOM
        sz = self._sz()

        for i in range(len(self._apps)):
            pm = self._pixmaps[i]
            sc = self._scales[i]
            s = sz * sc
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
                p.drawText(r, Qt.AlignCenter, "?")
            
            if i == self._hover_idx and self._show_labels:
                name = self._apps[i].get("name", "")
                if name:
                    label_font = p.font()
                    label_font.setPixelSize(12)
                    label_font.setWeight(QFont.Medium)
                    p.setFont(label_font)
                    fm = QFontMetrics(label_font)
                    text_width = fm.horizontalAdvance(name)
                    padding_x = 8
                    label_w = text_width + padding_x * 2
                    label_h = 20
                    label_x = cx - label_w / 2
                    label_y = top - label_h - 4
                    label_path = QPainterPath()
                    label_path.addRoundedRect(label_x, label_y, label_w, label_h, label_h / 2, label_h / 2)
                    p.setPen(Qt.NoPen)
                    p.setBrush(QColor(0, 0, 0, 220))
                    p.drawPath(label_path)
                    p.setPen(QColor(255, 255, 255, 255))
                    p.setFont(label_font)
                    text_rect = QRectF(label_x, label_y, label_w, label_h)
                    p.drawText(text_rect, Qt.AlignCenter | Qt.TextSingleLine, name)

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
