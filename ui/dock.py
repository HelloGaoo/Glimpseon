import os
import time

from PyQt5.QtCore import (
    QPoint,
    QPointF,
    QRectF,
    Qt,
    QTimer,
    pyqtProperty,
    QSize,
)
from PyQt5.QtGui import (
    QBrush,
    QColor,
    QLinearGradient,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
)
from PyQt5.QtWidgets import QLabel, QSizePolicy, QVBoxLayout, QWidget

from core.quick_launch_config import ql_cfg
from data.software_list import get_software_icon_path


class QLTooltip(QWidget):
    def __init__(self):
        super().__init__(None)
        self.setObjectName("qlTooltip")
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setAttribute(Qt.WA_ShowWithoutActivating)
        self.setWindowFlags(Qt.ToolTip | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
        self._label = QLabel(self)
        self._label.setAlignment(Qt.AlignCenter)
        self._label.setObjectName("qlTooltipLabel")
        layout = QVBoxLayout(self)
        layout.setContentsMargins(12, 6, 12, 6)
        layout.addWidget(self._label)
        self.hide()

    def show_tooltip(self, text, global_pos):
        self._label.setText(text)
        self.adjustSize()
        w = max(self.width(), self._label.sizeHint().width() + 24)
        self.setFixedSize(w, self.height())
        self.move(global_pos.x() - self.width() // 2, global_pos.y() - self.height() - 10)
        self.show()
        self.raise_()

    def hide_tooltip(self):
        self.hide()


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
    ICON_GAP = 4
    RADIUS = 16
    FPS = 120

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("quickLaunchDock")
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setSizePolicy(QSizePolicy.Maximum, QSizePolicy.Maximum)
        self.setMouseTracking(True)

        self._apps = []
        self._pixmaps = []
        self._scales = []
        self._target_scales = []
        self._hover_idx = -1
        self._tooltip = QLTooltip()
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
        return ql_cfg.icon_size

    def set_apps(self, apps):
        self._apps = list(apps)
        self._pixmaps = []
        cache_sz = int(self._sz() * self.MAX_SCALE * 2)
        for a in apps:
            fn = a.get("icon", "CY.png")
            p = get_software_icon_path(fn)
            pm = None
            if p and os.path.exists(p):
                raw = QPixmap(p)
                if not raw.isNull():
                    pm = raw.scaled(cache_sz, cache_sz, Qt.KeepAspectRatio, Qt.SmoothTransformation)
            self._pixmaps.append(pm)
        n = len(apps)
        self._scales = [self.BASE_SCALE] * n
        self._target_scales = [self.BASE_SCALE] * n
        self._fix_size()
        self.update()

    def update_icon_size(self, size):
        cache_sz = int(size * self.MAX_SCALE * 2)
        self._pixmaps = []
        for a in self._apps:
            fn = a.get("icon", "CY.png")
            p = get_software_icon_path(fn)
            pm = None
            if p and os.path.exists(p):
                raw = QPixmap(p)
                if not raw.isNull():pm = raw.scaled(cache_sz, cache_sz, Qt.KeepAspectRatio, Qt.SmoothTransformation)
            self._pixmaps.append(pm)
        self._fix_size()
        self.update()

    def _bg_rect(self):
        sz = self._sz()
        n = len(self._apps)
        if n == 0:return QRectF()
        w = n * sz + (n - 1) * self.ICON_GAP + self.PAD_X * 2
        h = sz + self.PAD_Y_TOP + self.PAD_Y_BOTTOM
        x = (self.width() - w) / 2
        y = self.height() - h
        return QRectF(x, y, w, h)

    def _fix_size(self):
        bg = self._bg_rect()
        if bg.width() > 0:
            overflow = int(self._sz() * (self.MAX_SCALE - self.BASE_SCALE))
            self.setFixedSize(int(bg.width()), int(bg.height()) + overflow)

    def _icon_positions(self):
        sz = self._sz()
        n = len(self._scales)
        if n == 0:return []

        widths = [sz * sc for sc in self._scales]
        total = sum(widths) + (n - 1) * self.ICON_GAP
        bg = self._bg_rect()
        content_w = bg.width() - self.PAD_X * 2
        start_x = bg.x() + self.PAD_X + (content_w - total) / 2

        pos = []
        cx = start_x
        for i in range(n):
            pos.append(cx + widths[i] / 2)
            cx += widths[i] + self.ICON_GAP
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
        self._tooltip.hide_tooltip()
        super().leaveEvent(e)

    def _calc_targets(self, pos):
        if not self._apps:return
        mx = pos.x()
        pos_list = self._icon_positions()
        new_hover = -1

        for i in range(len(self._apps)):
            cx = pos_list[i]
            d = abs(mx - cx)
            if d < self.MAGNIFY_RANGE:
                t = self._smoothstep(1.0 - d / self.MAGNIFY_RANGE)
                sc = self.BASE_SCALE + (self.MAX_SCALE - self.BASE_SCALE) * t
            else:
                sc = self.BASE_SCALE
            self._target_scales[i] = sc
            if new_hover < 0:
                r = self._icon_rect(i, pos_list)
                if r.contains(QPointF(pos)):
                    new_hover = i

        if new_hover != self._hover_idx:
            self._hover_idx = new_hover
            if new_hover >= 0:
                name = self._apps[new_hover].get("name", "")
                if name:
                    pl = self._icon_positions()
                    r = self._icon_rect(new_hover, pl)
                    gp = self.mapToGlobal(QPoint(int(r.center().x()), int(r.top())))
                    self._tooltip.show_tooltip(name, gp)
            else:
                self._tooltip.hide_tooltip()

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
            if self._hover_idx >= 0:
                pl = self._icon_positions()
                r = self._icon_rect(self._hover_idx, pl)
                gp = self.mapToGlobal(QPoint(int(r.center().x()), int(r.top())))
                self._tooltip.show_tooltip(
                    self._apps[self._hover_idx].get("name", ""), gp
                )

    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(QPointF(e.pos())):
                    self._click(i)
                    break
        super().mousePressEvent(e)

    def _click(self, idx):
        a = self._apps[idx]
        path = a.get("path", "")
        name = a.get("name", "")
        self._start_bounce(idx)
        if path and hasattr(self.window(), "_MainWindow__launchApp"):
            self.window()._MainWindow__launchApp(path, name)

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
            if not pm or pm.isNull():
                continue
            sc = self._scales[i]
            s = sz * sc
            cx = pl[i]
            top = baseline_y - s
            if i == self._bounce_idx:
                top += self._bounce_y
            p.drawPixmap(
                QRectF(cx - s / 2, top, s, s),
                pm,
                QRectF(0, 0, pm.width(), pm.height()),
            )

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
