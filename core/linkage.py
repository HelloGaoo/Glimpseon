# ClassLively
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

"""
联动模块
"""

import configparser
import json
import logging
import os
import threading
import time as _time
from dataclasses import dataclass, field
from datetime import datetime, time as _dt_time, timedelta
from enum import IntEnum
from typing import Optional

from core.utils import precise_now
from qfluentwidgets import qconfig

from PyQt6.QtCore import QObject, pyqtSignal

logger = logging.getLogger("ClassLively.core.linkage")

_PROFILE_FILE = "Profiles\\Default.json"
_SETTINGS_FILE = "Settings.json"


class TimeState(IntEnum):
    """时间状态枚举"""
    NONE = 0
    PREPARE_ON_CLASS = 1
    ON_CLASS = 2
    BREAKING = 3
    AFTER_SCHOOL = 4

    @classmethod
    def display_name(cls, state):
        return {
            cls.NONE: "今天没有课程",
            cls.PREPARE_ON_CLASS: "准备上课",
            cls.ON_CLASS: "上课中",
            cls.BREAKING: "课间休息",
            cls.AFTER_SCHOOL: "放学",
        }.get(state, str(state))


@dataclass
class LessonInfo:
    """单节课信息"""
    subject_name: str = ""
    teacher_name: str = ""
    initial: str = ""
    start_time: str = ""
    end_time: str = ""
    index: int = -1

    def to_dict(self):
        return {k: v for k, v in self.__dict__.items()}

    @staticmethod
    def from_subject_data(data):
        if not isinstance(data, dict):
            return LessonInfo()
        return LessonInfo(
            subject_name=data.get("Name", ""),
            teacher_name=data.get("TeacherName", ""),
            initial=data.get("Initial", ""),
        )


@dataclass
class LinkageState:
    """联动状态快照"""
    time_state: TimeState = TimeState.NONE
    current_subject: str = ""
    current_lesson: Optional[LessonInfo] = None
    next_lesson: Optional[LessonInfo] = None
    is_connected: bool = False
    last_update: Optional[datetime] = None
    on_class_left: str = ""
    on_breaking_left: str = ""
    current_index: int = -1
    is_class_plan_loaded: bool = False

    def to_dict(self):
        d = {}
        for k, v in self.__dict__.items():
            if isinstance(v, LessonInfo):
                d[k] = v.to_dict()
            elif isinstance(v, datetime):
                d[k] = v.isoformat()
            elif isinstance(v, IntEnum):
                d[k] = int(v)
            else:
                d[k] = v
        return d


@dataclass
class _TimeSlot:
    """ci 作息时间段"""
    start_time: datetime.time
    end_time: datetime.time
    time_type: int         # 0=上课, 1=课间
    break_name: str = ""
    index: int = 0


@dataclass
class _DayPlan:
    """ci 某天课表"""
    week_day: int
    name: str = ""
    class_ids: list = field(default_factory=list)
    layout_id: str = ""


@dataclass
class _CWTimeSlot:
    """cw 解析后的时间段"""
    start_time: _dt_time
    end_time: _dt_time
    subject: str
    teacher: str
    index: int
    is_break: bool







def _time_from_str(s: str) -> Optional[datetime.time]:
    """HH:MM 或 HH:MM:SS → time"""
    if not s:
        return None
    parts = s.strip().split(":")
    try:
        h = int(parts[0])
        m = int(parts[1]) if len(parts) > 1 else 0
        sec = int(parts[2]) if len(parts) > 2 else 0
        return _dt_time(h, m, sec)
    except (ValueError, IndexError):
        return None


def _fmt_delta(td: timedelta) -> str:
    """timedelta → "HH:MM" 或 "MM:SS" """
    total_sec = max(0, int(td.total_seconds()))
    h, rem = divmod(total_sec, 3600)
    m, s = divmod(rem, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m:02d}:{s:02d}"


def _python_weekday_to_dotnet(weekday: int) -> int:
    """Mon=1..Sun=7 → Sun=0..Sat=6"""
    return 0 if weekday == 7 else weekday





# ClassIsland 联动
def _find_exe_by_psutil(process_names: list[str]) -> Optional[str]:
    """查找进程的路径"""
    try:
        import psutil
        for proc in psutil.process_iter(['name', 'exe']):
            try:
                name = proc.info['name']
                if name and name in process_names:
                    exe = proc.info['exe']
                    if exe:
                        return exe
            except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
                continue
    except ImportError:
        pass
    return None


def _find_classisland_exe() -> Optional[str]:
    """查找 ci 进程路径"""
    return _find_exe_by_psutil(["ClassIsland.Desktop.exe", "ClassIsland.exe"])


def _find_classisland_data() -> str:
    """查找 ClassIsland\\data 目录"""
    exe_path = _find_classisland_exe()
    if exe_path:
        base = os.path.dirname(exe_path)
        for _ in range(3):
            for candidate in [base, os.path.join(base, "data")]:
                if os.path.isfile(os.path.join(candidate, _PROFILE_FILE)):
                    logger.info(f"[Linkage] 从进程发现 ClassIsland: {candidate}")
                    return candidate
            parent = os.path.dirname(base)
            if parent == base:
                break
            base = parent
    fallback = r"C:\ClassIsland2\data"
    if os.path.isfile(os.path.join(fallback, _PROFILE_FILE)):
        return fallback
    return ""


class LinkageBridge(QObject):
    """ClassIsland 配置桥接"""

    stateChanged = pyqtSignal(object)
    connectedChanged = pyqtSignal(bool)
    errorOccurred = pyqtSignal(str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self._data_dir = ""
        self._state = LinkageState()
        self._running = False
        self._thread = None
        self._lock = threading.Lock()
        self._poll_interval = 5
        self._prev_state = TimeState.NONE
        self._cached_raw: dict = {}
        self._cached_mtime: float = 0
        self._settings_mtime: float = 0
        self._settings_cached: dict = {}
        self._slots: list[_TimeSlot] = []
        self._day_plans: dict[int, _DayPlan] = {}
        self._subjects: dict[str, dict] = {}
        self._consecutive_failures = 0
        self._max_failures_before_redetect = 3

    @property
    def poll_interval(self):
        return self._poll_interval

    @poll_interval.setter
    def poll_interval(self, v):
        self._poll_interval = max(1, min(30, v))

    @property
    def is_running(self):
        return self._running

    # 生命周期

    def set_data_path(self, path: str):
        self._data_dir = path.strip()
        self._clear_cache()

    def auto_detect(self) -> str:
        path = _find_classisland_data()
        if path:
            self.set_data_path(path)
        return path

    def start(self):
        if self._running:
            return
        self._consecutive_failures = 0
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="linkage-file")
        self._thread.start()
        logger.info(f"[Linkage] 启动 (路径: {self._data_dir or '未设置'})")

    def stop(self):
        self._running = False
        if self._thread:
            self._thread.join(timeout=3)
            self._thread = None
        logger.info("[Linkage] 停止")

    # 对外查询

    def get_state(self) -> LinkageState:
        with self._lock:
            return self._state

    def get_current_lesson(self) -> Optional[LessonInfo]:
        with self._lock:
            return self._state.current_lesson

    def get_today_schedule(self) -> list:
        """返回今日课表"""
        import datetime as _dt
        with self._lock:
            if not self._slots or not self._day_plans:
                return []
            now = precise_now()
            t = now.time()
            dotnet_wd = _python_weekday_to_dotnet(now.isoweekday())
            plan = self._day_plans.get(dotnet_wd)
            if not plan:
                return []
            current_idx = self._state.current_index
            is_breaking = self._state.time_state == TimeState.BREAKING
            result = []
            class_counter = 0
            for slot in self._slots:
                if slot.time_type == 0:  # 上课
                    class_counter += 1
                    ci = class_counter - 1
                    if ci >= len(plan.class_ids):
                        break
                    sid = plan.class_ids[ci]
                    subj = self._subjects.get(sid, {})
                    result.append((
                        subj.get("Name", ""),
                        subj.get("TeacherName", ""),
                        slot.start_time.strftime("%H:%M"),
                        slot.end_time.strftime("%H:%M"),
                        class_counter,
                        class_counter == current_idx,
                        False,
                        "",
                    ))
                else:  # 课间
                    in_this_break = is_breaking and slot.start_time <= t < slot.end_time
                    result.append((
                        "", "",
                        slot.start_time.strftime("%H:%M"),
                        slot.end_time.strftime("%H:%M"),
                        0,
                        in_this_break,
                        True,
                        slot.break_name or "课间",
                    ))
            return result

    def test_connection(self) -> tuple[bool, str]:
        profile = os.path.join(self._data_dir, _PROFILE_FILE)
        if not os.path.isfile(profile):
            return False, f"文件不存在: {profile}"
        try:
            with open(profile, "r", encoding="utf-8") as f:
                data = json.load(f)
            subjects = data.get("Subjects", {})
            plans = data.get("ClassPlans", {})
            layouts = data.get("TimeLayouts", {})
            return True, f"ClassIsland ({len(subjects)}科目/{len(plans)}课表/{len(layouts)}作息)"
        except json.JSONDecodeError as e:
            return False, f"JSON 解析失败: {e}"
        except Exception as e:
            return False, str(e)

    # 内部

    def _clear_cache(self):
        self._cached_raw = {}
        self._cached_mtime = 0
        self._slots.clear()
        self._day_plans.clear()
        self._subjects.clear()

    def _loop(self):
        while self._running:
            try:
                # 没有路径时自动检测
                if not self._data_dir:
                    self._auto_detect_silent()
                self._sync_time_config()
                st = self._compute_state()
                self._commit(st)
            except Exception as e:
                logger.debug(f"[Linkage] 循环异常: {e}")
            _time.sleep(self._poll_interval)

    def _auto_detect_silent(self):
        """静默检测 成功则保存路径"""
        path = _find_classisland_data()
        if path:
            self.set_data_path(path)
            self._consecutive_failures = 0
            try:
                from core.config import cfg
                cfg.linkageDataPath.value = path
            except Exception:
                pass
            logger.info(f"[Linkage] 检测到: {path}")

    def _load_file_if_changed(self) -> bool:
        if not self._data_dir:
            return False
        profile = os.path.join(self._data_dir, _PROFILE_FILE)
        try:
            mtime = os.path.getmtime(profile)
            if mtime <= self._cached_mtime and self._cached_raw:
                return True
            with open(profile, "r", encoding="utf-8") as f:
                raw = json.load(f)
            self._cached_raw = raw
            self._cached_mtime = mtime
            self._parse_all(raw)
            self._consecutive_failures = 0
            logger.info(f"[Linkage] 已加载课表文件 ({datetime.fromtimestamp(mtime):%H:%M:%S})")
            return True
        except FileNotFoundError:
            self._consecutive_failures += 1
            if self._consecutive_failures >= 2:
                self._try_redetect()
            return False
        except Exception as e:
            self._consecutive_failures += 1
            logger.warning(f"[Linkage] 读取文件失败: {e}")
            if self._consecutive_failures >= 3:
                self._try_redetect()
            return False

    def _try_redetect(self):
        new_path = _find_classisland_data()
        if new_path and new_path != self._data_dir:
            logger.info(f"[Linkage] 重检测到新路径: {new_path}")
            self.set_data_path(new_path)
            self._consecutive_failures = 0
            try:
                from core.config import cfg
                cfg.linkageDataPath.value = new_path
            except Exception:
                pass
            self.errorOccurred.emit(f"REDIRECT:{new_path}")

    def _parse_all(self, raw: dict):
        self._subjects = raw.get("Subjects") or {}
        layouts = raw.get("TimeLayouts") or {}
        self._slots = []
        if layouts:
            first_id = next(iter(layouts), None)
            if first_id:
                for i, item in enumerate(layouts[first_id].get("Layouts") or []):
                    try:
                        s = _time_from_str(item.get("StartTime", ""))
                        e = _time_from_str(item.get("EndTime", ""))
                        if s and e:
                            self._slots.append(_TimeSlot(s, e, item.get("TimeType", 1),
                                                         item.get("BreakName", ""), i))
                    except (ValueError, TypeError):
                        pass
        self._day_plans.clear()
        for pid, plan in (raw.get("ClassPlans") or {}).items():
            tr = plan.get("TimeRule", {}) or {}
            wd = tr.get("WeekDay", 0)
            classes = [c["SubjectId"] for c in plan.get("Classes", []) if c.get("IsEnabled", True)]
            self._day_plans[wd] = _DayPlan(week_day=wd, name=plan.get("Name", ""),
                                            class_ids=classes, layout_id=plan.get("TimeLayoutId", ""))

    def _sync_time_config(self):
        from core.config import cfg
        if not cfg.linkageSyncTimeConfig.value or not self._data_dir:
            return
        settings_path = os.path.join(self._data_dir, _SETTINGS_FILE)
        try:
            mtime = os.path.getmtime(settings_path)
            if mtime <= self._settings_mtime and self._settings_cached:
                return
            with open(settings_path, "r", encoding="utf-8") as f:
                raw = json.load(f)
            self._settings_mtime = mtime
            self._settings_cached = raw
            ci_offset = raw.get("TimeOffsetSeconds")
            if ci_offset is not None:
                qconfig.set(cfg.timeOffset, int(ci_offset))
            ci_auto_enabled = raw.get("IsTimeAutoAdjustEnabled")
            if ci_auto_enabled is not None:
                qconfig.set(cfg.autoTimeOffsetEnabled, bool(ci_auto_enabled))
            ci_auto_increment = raw.get("TimeAutoAdjustSeconds")
            if ci_auto_increment is not None:
                qconfig.set(cfg.autoTimeOffsetIncrement, int(ci_auto_increment))
            logger.info(f"[Linkage] 已同步 ClassIsland 时间配置")
        except FileNotFoundError:
            pass
        except Exception as e:
            logger.debug(f"[Linkage] 同步时间配置失败: {e}")

    def _compute_state(self) -> LinkageState:
        now = precise_now()
        today = now.date()
        weekday = today.isoweekday()
        dotnet_wd = _python_weekday_to_dotnet(weekday)
        t = now.time()
        if not self._load_file_if_changed():
            return LinkageState(is_connected=False)
        st = LinkageState(is_connected=True, last_update=now)
        slot_idx, slot = self._find_slot(t)
        plan = self._day_plans.get(dotnet_wd)
        if not plan:
            st.time_state = TimeState.NONE
            return st
        if slot is None:
            st.time_state = TimeState.AFTER_SCHOOL if (self._slots and t >= self._slots[-1].end_time) else TimeState.NONE
            return st
        if slot.time_type == 0:
            st.time_state = TimeState.ON_CLASS
        else:
            st.time_state = TimeState.BREAKING
            st.current_subject = slot.break_name or ""
        class_index = self._slot_to_class_index(slot_idx)
        if 0 <= class_index < len(plan.class_ids):
            sid = plan.class_ids[class_index]
            subj = self._subjects.get(sid, {})
            st.current_subject = subj.get("Name", "") if slot.time_type == 0 else st.current_subject
            st.current_lesson = LessonInfo.from_subject_data(subj)
            st.current_lesson.start_time = slot.start_time.strftime("%H:%M")
            st.current_lesson.end_time = slot.end_time.strftime("%H:%M")
            st.current_lesson.index = class_index + 1
            st.current_index = class_index + 1
        next_idx = self._slot_to_class_index(slot_idx, offset=1)
        if 0 <= next_idx < len(plan.class_ids):
            next_sid = plan.class_ids[next_idx]
            st.next_lesson = LessonInfo.from_subject_data(self._subjects.get(next_sid, {}))
        left = datetime.combine(today, slot.end_time) - now
        attr = "on_class_left" if slot.time_type == 0 else "on_breaking_left"
        setattr(st, attr, _fmt_delta(left))
        logger.debug(f"[Linkage] {TimeState.display_name(st.time_state)} | {st.current_subject or '-'} | "
                     f"{slot.start_time.strftime('%H:%M')}-{slot.end_time.strftime('%H:%M')} | "
                     f"下节:{st.next_lesson.subject_name if st.next_lesson else '-'} | "
                     f"{plan.name}(周{weekday}) 第{slot_idx+1}/{len(self._slots)}段 | 剩余{_fmt_delta(left)}")
        return st

    def _find_slot(self, t: datetime.time) -> tuple[int, Optional[_TimeSlot]]:
        for i, slot in enumerate(self._slots):
            if slot.start_time <= t < slot.end_time:
                return i, slot
        return -1, None

    def _slot_to_class_index(self, slot_idx: int, offset: int = 0) -> int:
        n_classes = sum(1 for s in self._slots[:slot_idx + 1] if s.time_type == 0)
        return max(0, n_classes - 1 + offset)

    def _commit(self, new_state: LinkageState) -> bool:
        with self._lock:
            old = self._state
            old_ts = self._prev_state
            self._state = new_state
            self.stateChanged.emit(new_state)
            if old.is_connected != new_state.is_connected:
                self.connectedChanged.emit(new_state.is_connected)
            changed = new_state.time_state != old_ts
            if changed:
                self._prev_state = new_state.time_state
                logger.info(f"[Linkage] {TimeState.display_name(old_ts)} -> {TimeState.display_name(new_state.time_state)}")
            return changed







# ClassWidgets 联动

def _find_classwidgets_exe() -> str:
    """查找 ClassWidgets.exe 进程路径"""
    path = _find_exe_by_psutil(["ClassWidgets.exe"])
    return path or ""


def _find_classwidgets_data() -> str:
    """自动查找 ClassWidgets config 目录"""
    exe_path = _find_classwidgets_exe()
    if exe_path:
        exe_dir = os.path.dirname(exe_path)
        if os.path.isfile(os.path.join(exe_dir, "config", "config.ini")):
            logger.info(f"[CW-Linkage] 从进程路径发现 ClassWidgets: {os.path.join(exe_dir, 'config')}")
            return os.path.join(exe_dir, "config")
    fixed_exe = r"C:\ClassWidgets\ClassWidgets.exe"
    if os.path.isfile(fixed_exe):
        exe_dir = os.path.dirname(fixed_exe)
        if os.path.isfile(os.path.join(exe_dir, "config", "config.ini")):
            logger.info(f"[CW-Linkage] 从 C:\\ClassWidgets 发现 ClassWidgets: {os.path.join(exe_dir, 'config')}")
            return os.path.join(exe_dir, "config")
    return ""


class ClassWidgetsBridge(QObject):
    """ClassWidgets 配置桥接"""

    stateChanged = pyqtSignal(object)
    connectedChanged = pyqtSignal(bool)
    errorOccurred = pyqtSignal(str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self._config_dir = ""
        self._state = LinkageState()
        self._running = False
        self._thread = None
        self._lock = threading.Lock()
        self._poll_interval = 5
        self._prev_state = TimeState.NONE
        self._consecutive_failures = 0
        self._max_failures_before_redetect = 3

    @property
    def poll_interval(self):
        return self._poll_interval

    @poll_interval.setter
    def poll_interval(self, v):
        self._poll_interval = max(1, min(30, v))

    @property
    def is_running(self):
        return self._running

    # 生命周期

    def set_data_path(self, path: str):
        self._config_dir = path.strip()

    def auto_detect(self) -> str:
        path = _find_classwidgets_data()
        if path:
            self.set_data_path(path)
        return path

    def start(self):
        if self._running:
            return
        self._consecutive_failures = 0
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="cw-linkage")
        self._thread.start()
        logger.info(f"[CW-Linkage] 启动 (路径: {self._config_dir or '未设置'})")

    def stop(self):
        self._running = False
        if self._thread:
            self._thread.join(timeout=3)
            self._thread = None
        logger.info("[CW-Linkage] 停止")

    # 对外查询

    def test_connection(self) -> tuple[bool, str]:
        sched_file = self._resolve_schedule_path()
        if sched_file:
            return True, f"ClassWidgets (课表: {os.path.basename(sched_file)})"
        return False, "未找到课表文件"

    def get_today_schedule(self) -> list:
        """返回今日课表"""
        now = precise_now()
        t = now.time()
        data = self._read_schedule()
        if not data:
            return []
        slots = self._parse_schedule(data, now)
        current_idx = -1
        is_breaking = False
        with self._lock:
            current_idx = self._state.current_index
            is_breaking = self._state.time_state == TimeState.BREAKING
        result = []
        for slot in slots:
            if slot.is_break:
                in_this_break = is_breaking and slot.start_time <= t < slot.end_time
                result.append((
                    "", "",
                    slot.start_time.strftime("%H:%M"),
                    slot.end_time.strftime("%H:%M"),
                    0,
                    in_this_break,
                    True,
                    slot.subject or "课间",
                ))
            else:
                result.append((
                    slot.subject,
                    slot.teacher,
                    slot.start_time.strftime("%H:%M"),
                    slot.end_time.strftime("%H:%M"),
                    slot.index,
                    slot.index == current_idx,
                    False,
                    "",
                ))
        return result

    # 课表文件解析

    def _resolve_schedule_path(self) -> str:
        """config.ini 读课表名 去 schedule 目录找课表"""
        config_path = os.path.join(self._config_dir, "config.ini")
        name = ""
        if os.path.isfile(config_path):
            try:
                cp = configparser.ConfigParser()
                with open(config_path, "r", encoding="utf-8", errors="ignore") as f:
                    cp.read_file(f)
                name = cp.get('General', 'schedule', fallback='').strip()
            except Exception:
                try:
                    with open(config_path, "r", encoding="gbk", errors="ignore") as f:
                        cp.read_file(f)
                    name = cp.get('General', 'schedule', fallback='').strip()
                except Exception:
                    pass
        sched_dir = os.path.join(self._config_dir, "schedule")
        if not os.path.isdir(sched_dir):
            return ""
        if name and os.path.isfile(os.path.join(sched_dir, name)):
            return os.path.join(sched_dir, name)
        if name and os.path.isfile(os.path.join(sched_dir, f"{name}.json")):
            return os.path.join(sched_dir, f"{name}.json")
        logger.warning(f"[CW-Linkage] '{name}' 不存在, 扫描兜底")
        for f in sorted(os.listdir(sched_dir)):
            if f.endswith(".json") and os.path.isfile(os.path.join(sched_dir, f)):
                return os.path.join(sched_dir, f)
        return ""

    def _read_schedule(self) -> dict:
        sched_file = self._resolve_schedule_path()
        if not sched_file:
            return {}
        try:
            with open(sched_file, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            return {}

    # 状态计算

    def _loop(self):
        while self._running:
            try:
                # 没有路径时自动检测
                if not self._config_dir:
                    self._auto_detect_silent()
                st = self._compute_state()
                self._commit(st)
            except Exception as e:
                logger.debug(f"[CW-Linkage] 循环异常: {e}")
            _time.sleep(self._poll_interval)

    def _auto_detect_silent(self):
        """静默检测 成功则保存路径"""
        path = _find_classwidgets_data()
        if path:
            self.set_data_path(path)
            self._consecutive_failures = 0
            try:
                from core.config import cfg
                cfg.classWidgetsDataPath.value = path
            except Exception:
                pass
            logger.info(f"[CW-Linkage] 检测到: {path}")

    def _compute_state(self) -> LinkageState:
        now = precise_now()
        today = now.date()
        t = now.time()
        data = self._read_schedule()
        if not data:
            self._consecutive_failures += 1
            if self._consecutive_failures >= 2:
                self._try_redetect()
            return LinkageState(is_connected=False)
        self._consecutive_failures = 0
        st = LinkageState(is_connected=True, last_update=now)
        slots = self._parse_schedule(data, now)
        if not slots:
            st.time_state = TimeState.NONE
            return st

        # 查找当前时间段
        current_slot = None
        for slot in slots:
            if slot.start_time <= t < slot.end_time:
                current_slot = slot
                break

        # 当前不在任何时间段内
        if current_slot is None:
            if slots and t >= slots[-1].end_time:
                st.time_state = TimeState.AFTER_SCHOOL
                return st
            st.time_state = TimeState.BREAKING
            st.current_subject = "课间"
            for next_slot in slots:
                if not next_slot.is_break and next_slot.start_time > t:
                    st.next_lesson = LessonInfo(
                        subject_name=next_slot.subject, teacher_name=next_slot.teacher,
                        start_time=next_slot.start_time.strftime("%H:%M"),
                        end_time=next_slot.end_time.strftime("%H:%M"), index=next_slot.index,
                    )
                    break
            if st.next_lesson:
                try:
                    ns = _dt_time(int(st.next_lesson.start_time.split(":")[0]),
                                  int(st.next_lesson.start_time.split(":")[1]))
                    left = datetime.combine(today, ns) - now
                    if left.total_seconds() > 0:
                        st.on_breaking_left = _fmt_delta(left)
                except Exception:
                    pass
            return st

        # 当前在某个时间段内
        if current_slot.is_break:
            st.time_state = TimeState.BREAKING
            st.current_subject = current_slot.subject or "课间"
        else:
            st.time_state = TimeState.ON_CLASS
            st.current_subject = current_slot.subject
            st.current_lesson = LessonInfo(
                subject_name=current_slot.subject, teacher_name=current_slot.teacher,
                start_time=current_slot.start_time.strftime("%H:%M"),
                end_time=current_slot.end_time.strftime("%H:%M"), index=current_slot.index,
            )
            st.current_index = current_slot.index

        left = datetime.combine(today, current_slot.end_time) - now
        if current_slot.is_break:
            st.on_breaking_left = _fmt_delta(left)
        else:
            st.on_class_left = _fmt_delta(left)

        # 下一节课
        current_idx = slots.index(current_slot)
        for next_slot in slots[current_idx + 1:]:
            if not next_slot.is_break:
                st.next_lesson = LessonInfo(
                    subject_name=next_slot.subject, teacher_name=next_slot.teacher,
                    start_time=next_slot.start_time.strftime("%H:%M"),
                    end_time=next_slot.end_time.strftime("%H:%M"), index=next_slot.index,
                )
                break

        logger.debug(f"[CW-Linkage] {TimeState.display_name(st.time_state)} | {st.current_subject or '-'} | "
                     f"{current_slot.start_time.strftime('%H:%M')}-{current_slot.end_time.strftime('%H:%M')} | "
                     f"下节:{st.next_lesson.subject_name if st.next_lesson else '-'}")
        return st

    def _parse_schedule(self, data: dict, now: datetime) -> list:
        """解析 cw 课表 json → _CWTimeSlot 列表"""
        # cw到底怎么想的 这时间段弄得什么幌子啊 为啥按照添加顺序写json 
        # 为啥cw不整个hh mm ss-hh mm ss写json里 ci那样多好  
        slots = []
        week_type = self._get_week_type(data, now)
        wd_key = str(now.weekday())  # Mon=0 .. Sun=6
        timeline_key = "timeline_even" if week_type == 1 else "timeline"
        timeline = data.get(timeline_key, {})
        day_timeline = timeline.get(wd_key) or timeline.get("default", [])
        if not day_timeline:
            return slots
        sched_key = "schedule_even" if week_type == 1 else "schedule"
        schedule = data.get(sched_key, {})
        day_schedule = schedule.get(wd_key, [])
        parts = data.get("part", {})

        # 首条 timeline 的 part 基准时间初始化
        current_time = _dt_time(0, 0)
        if day_timeline and len(day_timeline[0]) >= 2:
            fp_key = str(day_timeline[0][1])
            fp_info = parts.get(fp_key)
            if fp_info and isinstance(fp_info, (list, tuple)) and len(fp_info) >= 2:
                current_time = _dt_time(int(fp_info[0]), int(fp_info[1]))

        # 顺序衔接
        class_counter = 0
        for unit in day_timeline:
            if not isinstance(unit, (list, tuple)) or len(unit) < 4:
                continue
            unit_type = unit[0]
            duration_min = int(unit[3])
            start_t = current_time
            end_h = start_t.hour + (start_t.minute + duration_min) // 60
            end_m = (start_t.minute + duration_min) % 60
            end_t = _dt_time(min(end_h, 23), end_m)
            current_time = end_t
            if unit_type == 0:  # 上课
                class_counter += 1
                cidx = int(unit[2])
                idx = cidx - 1  # class_idx → schedule 索引
                subject_name = day_schedule[idx] if 0 <= idx < len(day_schedule) else ""
                slots.append(_CWTimeSlot(start_t, end_t, subject_name, "", class_counter, False))
            else:  # 课间
                slots.append(_CWTimeSlot(start_t, end_t, "课间", "", 0, True))
        return slots

    def _get_week_type(self, data: dict, now: datetime) -> int:
        """0=单周, 1=双周"""
        try:
            start_date_str = data.get("start_date", "")
            if start_date_str:
                start_date = datetime.strptime(start_date_str, "%Y-%m-%d").date()
                week_num = (now.date() - start_date).days // 7 + 1
                return 1 if week_num % 2 == 0 else 0
        except Exception:
            pass
        return 0

    def _try_redetect(self):
        new_path = _find_classwidgets_data()
        if new_path and new_path != self._config_dir:
            logger.info(f"[CW-Linkage] 检测到新路径: {new_path}")
            self.set_data_path(new_path)
            self._consecutive_failures = 0
            try:
                from core.config import cfg
                cfg.classWidgetsDataPath.value = new_path
            except Exception:
                pass
            self.errorOccurred.emit(f"REDIRECT:{new_path}")

    def _commit(self, new_state: LinkageState) -> bool:
        with self._lock:
            old = self._state
            old_ts = self._prev_state
            self._state = new_state
            self.stateChanged.emit(new_state)
            if old.is_connected != new_state.is_connected:
                self.connectedChanged.emit(new_state.is_connected)
            changed = new_state.time_state != old_ts
            if changed:
                self._prev_state = new_state.time_state
                logger.info(f"[CW-Linkage] {TimeState.display_name(old_ts)} -> {TimeState.display_name(new_state.time_state)}")
            return changed
