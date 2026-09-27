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

from core.config import cfg
from core.utils import precise_now
from qfluentwidgets import qconfig

from PyQt6.QtCore import QObject, pyqtSignal

logger = logging.getLogger("Glimpseon.core.linkage")

_PROFILE_FILE = "Profiles\\Default.json"
_SETTINGS_FILE = "Settings.json"
_cw_fixed_miss_logged = False
_lesson_bad_data_logged = False


class TimeState(IntEnum):
    """时间状态"""
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
            global _lesson_bad_data_logged
            if not _lesson_bad_data_logged:
                _lesson_bad_data_logged = True
                logger.debug(f"科目数据类型异常 返回空课程: {type(data).__name__}")
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
    start_time: _dt_time
    end_time: _dt_time
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

def _time_from_str(s: str) -> Optional[_dt_time]:
    """HH:MM/HH:MM:SS > time"""
    if not s:
        return None
    parts = s.strip().split(":")
    try:
        h = int(parts[0])
        m = int(parts[1]) if len(parts) > 1 else 0
        sec = int(parts[2]) if len(parts) > 2 else 0
        return _dt_time(h, m, sec)
    except (ValueError, IndexError) as e:
        logger.debug(f"时间字符串解析失败: {s!r} ({e})")
        return None


def _fmt_delta(td: timedelta) -> str:
    """timedelta > "HH:MM/HH:MM:SS" """
    total_sec = max(0, int(td.total_seconds()))
    h, rem = divmod(total_sec, 3600)
    m, s = divmod(rem, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m:02d}:{s:02d}"


def _python_weekday_to_dotnet(weekday: int) -> int:
    """Mon=1..Sun=7 > Sun=0..Sat=6"""
    return 0 if weekday == 7 else weekday

# ClassIsland 联动
_psutil_warned = False

def _find_exe_by_psutil(process_names: list[str]) -> Optional[str]:
    """查找进程路径
    """
    global _psutil_warned
    try:
        import psutil
        for proc in psutil.process_iter(['name', 'exe']):
            try:
                name = proc.info['name']
                if name and name in process_names:
                    exe = proc.info['exe']
                    if exe:
                        logger.debug(f"进程候选命中: {name} -> {exe}")
                        return exe
            except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
                continue
    except ImportError:
        if not _psutil_warned:
            _psutil_warned = True
            logger.warning("psutil未安装")
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
                    logger.info(f"从进程发现 ClassIsland: {candidate}")
                    return candidate
            parent = os.path.dirname(base)
            if parent == base:
                break
            base = parent
    fallback = r"C:\ClassIsland2\data"
    if os.path.isfile(os.path.join(fallback, _PROFILE_FILE)):
        logger.debug(f"用 ClassIsland 默认路径: {fallback}")
        return fallback
    return ""


class _LinkageBridgeBase(QObject):
    """联动桥基类
    """
    stateChanged = pyqtSignal(object)
    connectedChanged = pyqtSignal(bool)
    errorOccurred = pyqtSignal(str)

    _TAG = "Linkage"
    _THREAD_NAME = "linkage-file"
    _CFG_PATH_ATTR = "linkageDataPath"

    def __init__(self, parent=None):
        super().__init__(parent)
        self._data_dir = ""
        self._state = LinkageState()
        self._running = False
        self._thread = None
        self._lock = threading.Lock()
        self._poll_interval = 5
        self._prev_state = TimeState.NONE
        self._consecutive_failures = 0
        logger.debug(f"[{self._TAG}] 联动桥初始化")

    @property
    def poll_interval(self):
        return self._poll_interval

    @poll_interval.setter
    def poll_interval(self, v):
        self._poll_interval = max(1, min(30, v))
        logger.debug(f"[{self._TAG}] 轮询间隔设置为{self._poll_interval}秒")

    @property
    def is_running(self):
        return self._running

    def set_data_path(self, path: str):
        p = path.strip()
        if p and p != self._data_dir:
            logger.info(f"[{self._TAG}] 数据路径设置: {p}")
        self._data_dir = p

    def auto_detect(self) -> str:
        path = self._find_data()
        if path:
            self.set_data_path(path)
            logger.debug(f"[{self._TAG}] 自动检测命中 {path}")
        else:
            logger.debug(f"[{self._TAG}] 自动检测未找到数据目录")
        return path

    def start(self):
        if self._running:
            logger.debug(f"[{self._TAG}] 已在运行")
            return
        self._consecutive_failures = 0
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name=self._THREAD_NAME)
        self._thread.start()
        logger.info(f"[{self._TAG}] 启动 (路径: {self._data_dir or '未设置'})")

    def stop(self):
        self._running = False
        if self._thread:
            self._thread.join(timeout=3)
            if self._thread.is_alive():
                logger.warning(f"[{self._TAG}] 后台线程未在 3 秒内退出")
            self._thread = None
        logger.info(f"[{self._TAG}] 停止")

    def get_state(self) -> LinkageState:
        with self._lock:
            return self._state

    def _loop(self):
        while self._running:
            try:
                if not self._data_dir:
                    self._auto_detect_silent()
                self._before_compute()
                st = self._compute_state()
                self._commit(st)
            except Exception as e:
                self._consecutive_failures += 1
                if self._consecutive_failures == 1:
                    logger.warning(f"[{self._TAG}] 循环异常: {e}")
                else:
                    logger.debug(f"[{self._TAG}] 循环异常(连续{self._consecutive_failures}次): {e}")
            _time.sleep(self._poll_interval)

    def _before_compute(self):
        """每轮计算前的钩子"""

    def _try_redetect(self):
        new_path = self._find_data()
        if new_path and new_path != self._data_dir:
            logger.info(f"[{self._TAG}] 路径变更: {new_path}")
            self._redetect_fail_logged = False
            self.set_data_path(new_path)
            self._consecutive_failures = 0
            try:
                getattr(cfg, self._CFG_PATH_ATTR).value = new_path
            except Exception as e:
                logger.debug(f"[{self._TAG}] 重检测路径保存失败: {e}")
            self.errorOccurred.emit(f"REDIRECT:{new_path}")
        elif not new_path and not getattr(self, "_redetect_fail_logged", False):
            self._redetect_fail_logged = True
            logger.debug(f"[{self._TAG}] 重检测未发现新路径 (当前: {self._data_dir or '未设置'})")

    def _commit(self, new_state: LinkageState) -> bool:
        # 锁内只做状态替换与 diff;emit 出锁外(同线程直连槽调 get_state 会自死锁)
        with self._lock:
            old = self._state
            old_ts = self._prev_state
            self._state = new_state
            connected_changed = old.is_connected != new_state.is_connected
            changed = new_state.time_state != old_ts
            if changed:
                self._prev_state = new_state.time_state
        self.stateChanged.emit(new_state)
        if connected_changed:
            self.connectedChanged.emit(new_state.is_connected)
            logger.info(f"[{self._TAG}] 连接状态变化: {'已连接' if new_state.is_connected else '已断开'}")
        if changed:
            logger.info(f"[{self._TAG}] {TimeState.display_name(old_ts)} -> {TimeState.display_name(new_state.time_state)}")
        return changed


class LinkageBridge(_LinkageBridgeBase):
    """ClassIsland 配置桥接"""

    _TAG = "Linkage"
    _THREAD_NAME = "linkage-file"
    _CFG_PATH_ATTR = "linkageDataPath"

    def __init__(self, parent=None):
        super().__init__(parent)
        self._cached_raw: dict = {}
        self._cached_mtime: float = 0
        self._settings_mtime: float = 0
        self._settings_cached: dict = {}
        self._slots: list[_TimeSlot] = []
        self._day_plans: dict[int, _DayPlan] = {}
        self._subjects: dict[str, dict] = {}

    def set_data_path(self, path: str):
        p = path.strip()
        if p and p != self._data_dir:
            logger.info(f"[Linkage] 数据路径设置: {p}")
        self._data_dir = p
        self._clear_cache()

    def _find_data(self) -> Optional[str]:
        return _find_classisland_data()

    def _auto_detect_silent(self):
        """静默检测 成功则保存路径"""
        path = _find_classisland_data()
        if path:
            self.set_data_path(path)
            self._consecutive_failures = 0
            self._detect_fail_logged = False
            try:
                cfg.linkageDataPath.value = path
            except Exception as e:
                logger.warning(f"[Linkage] 检测路径保存失败: {e}")
            logger.info(f"[Linkage] 数据目录 {path}")
        elif not getattr(self, "_detect_fail_logged", False):
            self._detect_fail_logged = True
            logger.warning(f"[Linkage] 无 ClassIsland 数据目录")

    def _before_compute(self):
        self._sync_time_config()

    def get_today_schedule(self) -> list:
        """返回今日课表"""
        with self._lock:
            if not self._slots or not self._day_plans:
                logger.debug(f"[Linkage] 今日课表未加载")
                return []
            now = precise_now()
            t = now.time()
            dotnet_wd = _python_weekday_to_dotnet(now.isoweekday())
            plan = self._day_plans.get(dotnet_wd)
            if not plan:
                logger.debug(f"[Linkage] 今日(周{dotnet_wd}) 无课程安排")
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
                        if not getattr(self, "_sched_trunc_logged", False):
                            self._sched_trunc_logged = True
                            logger.debug(f"[Linkage] 课程序号超出当日计划 (第{class_counter}节) 截断今日课表")
                        break
                    sid = plan.class_ids[ci]
                    subj = self._subjects.get(sid, {})
                    result.append((
                        subj.get("Name", ""),
                        subj.get("TeacherName", ""),
                        slot.start_time.strftime("%H:%M"),
                        slot.end_time.strftime("%H:%M"),
                        class_counter,
                        class_counter == current_idx and not is_breaking,
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
            logger.debug(f"[Linkage] 今日课表{len(result)}条 (周{dotnet_wd})")
            return result

    def get_schedule_by_weekday(self, dotnet_weekday: int) -> list:
        """取指定日的课表"""
        with self._lock:
            if not self._slots or not self._day_plans:
                logger.debug(f"[Linkage] 周{dotnet_weekday}课表未加载")
                return []
            plan = self._day_plans.get(dotnet_weekday)
            if not plan:
                logger.debug(f"[Linkage] 周{dotnet_weekday} 无课程安排")
                return []
            result = []
            class_counter = 0
            for slot in self._slots:
                if slot.time_type == 0:          # 上课
                    class_counter += 1
                    ci = class_counter - 1
                    if ci >= len(plan.class_ids):
                        if not getattr(self, "_sched_trunc_wd_logged", False):
                            self._sched_trunc_wd_logged = True
                            logger.debug(f"[Linkage] 课程序号超出周{dotnet_weekday}计划 (第{class_counter}节) 截断课表")
                        break
                    sid = plan.class_ids[ci]
                    subj = self._subjects.get(sid, {})
                    result.append((
                        subj.get("Name", ""),
                        subj.get("TeacherName", ""),
                        slot.start_time.strftime("%H:%M"),
                        slot.end_time.strftime("%H:%M"),
                        class_counter,
                        False,   # is_current
                        False,   # is_break
                        "",
                    ))
                else:                            # 课间
                    result.append((
                        "", "",
                        slot.start_time.strftime("%H:%M"),
                        slot.end_time.strftime("%H:%M"),
                        0,
                        False,
                        True,
                        slot.break_name or "课间",
                    ))
            logger.debug(f"[Linkage] 周{dotnet_weekday} 课表{len(result)}条")
            return result

    def get_week_schedule(self) -> dict:
        """返回一周课表"""
        result = {}
        for py_wd in range(1, 8):
            dotnet_wd = _python_weekday_to_dotnet(py_wd)
            sched = self.get_schedule_by_weekday(dotnet_wd)
            result[py_wd] = sched
        logger.debug(f"[Linkage] 一周课表{sum(len(v) for v in result.values())}条")
        return result

    def _clear_cache(self):
        with self._lock:
            self._cached_raw = {}
            self._cached_mtime = 0
            self._slots = []
            self._day_plans = {}
            self._subjects = {}
        logger.debug("[Linkage] 课表缓存已清空")

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
            logger.debug(f"[Linkage] 课表文件已变更 (mtime={mtime:.0f})")
            return True
        except FileNotFoundError:
            self._consecutive_failures += 1
            if self._consecutive_failures == 1:
                logger.warning(f"[Linkage] 课表文件不存在: {profile}")
            if self._consecutive_failures >= 2:
                self._try_redetect()
            return False
        except Exception as e:
            self._consecutive_failures += 1
            logger.warning(f"[Linkage] 读取文件失败: {e}")
            if self._consecutive_failures >= 3:
                self._try_redetect()
            return False

    def _parse_all(self, raw: dict):
        # 先在局部构建完整结构,最后持锁一次性换引用(worker 写 / 主线程读)
        subjects = raw.get("Subjects") or {}
        layouts = raw.get("TimeLayouts") or {}
        if not subjects:
            logger.warning("[Linkage] 课表缺Subjects")
        if not layouts:
            logger.warning("[Linkage] 课表缺TimeLayouts")
        slots = []
        if layouts:
            first_id = next(iter(layouts), None)
            if first_id:
                for item in (layouts[first_id].get("Layouts") or []):
                    try:
                        s = _time_from_str(item.get("StartTime", ""))
                        e = _time_from_str(item.get("EndTime", ""))
                        if s and e:
                            slots.append(_TimeSlot(s, e, item.get("TimeType", 1),
                                                   item.get("BreakName", "")))
                    except (ValueError, TypeError):
                        logger.debug(f"[Linkage] 时段解析跳过: {item}")
        day_plans = {}
        for pid, plan in (raw.get("ClassPlans") or {}).items():
            time_rule = plan.get("TimeRule", {}) or {}
            wd = time_rule.get("WeekDay", 0)
            classes = [c["SubjectId"] for c in plan.get("Classes", []) if c.get("IsEnabled", True)]
            day_plans[wd] = _DayPlan(week_day=wd, name=plan.get("Name", ""),
                                     class_ids=classes, layout_id=plan.get("TimeLayoutId", ""))
        if not day_plans:
            logger.warning("[Linkage] 课表缺ClassPlans")
        with self._lock:
            self._subjects = subjects
            self._slots = slots
            self._day_plans = day_plans
        logger.info(f"[Linkage] 课表已解析 {len(slots)}时段 {len(subjects)}科目 {len(day_plans)}天")

    def _sync_time_config(self):
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
            synced = []
            ci_offset = raw.get("TimeOffsetSeconds")
            if ci_offset is not None:
                qconfig.set(cfg.timeOffset, int(ci_offset))
                synced.append("TimeOffsetSeconds")
            ci_auto_enabled = raw.get("IsTimeAutoAdjustEnabled")
            if ci_auto_enabled is not None:
                qconfig.set(cfg.autoTimeOffsetEnabled, bool(ci_auto_enabled))
                synced.append("IsTimeAutoAdjustEnabled")
            ci_auto_increment = raw.get("TimeAutoAdjustSeconds")
            if ci_auto_increment is not None:
                qconfig.set(cfg.autoTimeOffsetIncrement, int(ci_auto_increment))
                synced.append("TimeAutoAdjustSeconds")
            if synced:
                logger.info(f"[Linkage] 已同步 ClassIsland 时间配置: {', '.join(synced)}")
            else:
                logger.debug(f"[Linkage] Settings.json 无可同步的时间键")
        except FileNotFoundError:
            if not getattr(self, "_settings_missing_logged", False):
                self._settings_missing_logged = True
                logger.debug(f"[Linkage] Settings.json 不存在: {settings_path}")
        except Exception as e:
            logger.debug(f"[Linkage] 同步时间配置失败 ({settings_path}): {e}")

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
            if not getattr(self, "_no_plan_logged", False):
                self._no_plan_logged = True
                logger.debug(f"[Linkage] 周{dotnet_wd} 无课程计划")
            return st
        if slot is None:
            st.time_state = TimeState.AFTER_SCHOOL if (self._slots and t >= self._slots[-1].end_time) else TimeState.NONE
            if not getattr(self, "_no_slot_logged", False):
                self._no_slot_logged = True
                logger.debug(f"[Linkage] 当前时间不在任何时段内 ({t})")
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
        else:
            if not getattr(self, "_class_idx_oob_logged", False):
                self._class_idx_oob_logged = True
                logger.warning(f"[Linkage] 课程序号 {class_index + 1} 超出当日计划 ({len(plan.class_ids)}节) 当前课程信息缺失")
        next_idx = self._slot_to_class_index(slot_idx, offset=1)
        if 0 <= next_idx < len(plan.class_ids):
            next_sid = plan.class_ids[next_idx]
            st.next_lesson = LessonInfo.from_subject_data(self._subjects.get(next_sid, {}))
        left = datetime.combine(today, slot.end_time) - now
        attr = "on_class_left" if slot.time_type == 0 else "on_breaking_left"
        setattr(st, attr, _fmt_delta(left))
        logger.debug(f"[Linkage] {TimeState.display_name(st.time_state)}|{st.current_subject or '-'}|"
                     f"{slot.start_time.strftime('%H:%M')}-{slot.end_time.strftime('%H:%M')}|"
                     f"下节:{st.next_lesson.subject_name if st.next_lesson else '-'}|"
                     f"{plan.name}(周{weekday}) 第{slot_idx+1}/{len(self._slots)}段|剩余{_fmt_delta(left)}")
        return st

    def _find_slot(self, t: datetime.time) -> tuple[int, Optional[_TimeSlot]]:
        for i, slot in enumerate(self._slots):
            if slot.start_time <= t < slot.end_time:
                return i, slot
        return -1, None

    def _slot_to_class_index(self, slot_idx: int, offset: int = 0) -> int:
        n_classes = sum(1 for s in self._slots[:slot_idx + 1] if s.time_type == 0)
        return max(0, n_classes - 1 + offset)


# ClassWidgets 联动

def _find_classwidgets_exe() -> str:
    """查找 ClassWidgets.exe 进程路径"""
    return _find_exe_by_psutil(["ClassWidgets.exe"]) or ""


def _find_classwidgets_data() -> str:
    """自动查找 ClassWidgets config 目录"""
    global _cw_fixed_miss_logged
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
        if not _cw_fixed_miss_logged:
            _cw_fixed_miss_logged = True
            logger.debug(f"[CW-Linkage] 固定路径缺config.ini {os.path.join(exe_dir, 'config')}")
    return ""


class ClassWidgetsBridge(_LinkageBridgeBase):
    """ClassWidgets 配置桥接"""

    _TAG = "CW-Linkage"
    _THREAD_NAME = "cw-linkage"
    _CFG_PATH_ATTR = "classWidgetsDataPath"
    _cw_cache_file = None
    _cw_cache_mtime = -1.0
    _cw_cache_data = None
    _last_resolved = None
    _last_read_fail = None
    _last_parse_sig = None
    _week_type_err_logged = False
    _cfg_fail_logged = False

    def _find_data(self) -> str:
        return _find_classwidgets_data()

    def _auto_detect_silent(self):
        """静默检测 成功则保存路径"""
        path = _find_classwidgets_data()
        if path:
            self.set_data_path(path)
            self._consecutive_failures = 0
            self._detect_fail_logged = False
            try:
                cfg.classWidgetsDataPath.value = path
            except Exception as e:
                logger.warning(f"[CW-Linkage] 检测路径保存失败: {e}")
            logger.info(f"[CW-Linkage] 数据目录 {path}")
        elif not getattr(self, "_detect_fail_logged", False):
            self._detect_fail_logged = True
            logger.warning(f"[CW-Linkage] 无 ClassWidgets 数据目录")


    def get_today_schedule(self) -> list:
        """返回今日课表"""
        now = precise_now()
        t = now.time()
        data = self._read_schedule()
        if not data:
            logger.debug(f"[CW-Linkage] 今日无课表数据")
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
        logger.debug(f"[CW-Linkage] 今日课表{len(result)}条")
        return result

    def get_schedule_by_weekday(self, python_weekday: int) -> list:
        """取指定日的课表"""
        data = self._read_schedule()
        if not data:
            logger.debug(f"[CW-Linkage] 周{python_weekday}无课表数据")
            return []
        now = precise_now()
        today = now.date()
        today_wd = today.weekday()  # Mon=0
        delta = python_weekday - today_wd
        target_date = today + timedelta(days=delta)
        target_dt = datetime.combine(target_date, _dt_time(0, 0))
        slots = self._parse_schedule(data, target_dt)
        result = []
        for slot in slots:
            if slot.is_break:
                result.append((
                    "", "",
                    slot.start_time.strftime("%H:%M"),
                    slot.end_time.strftime("%H:%M"),
                    0, False, True,
                    slot.subject or "课间",
                ))
            else:
                result.append((
                    slot.subject, slot.teacher,
                    slot.start_time.strftime("%H:%M"),
                    slot.end_time.strftime("%H:%M"),
                    slot.index, False, False, "",
                ))
        logger.debug(f"[CW-Linkage] 周{python_weekday} 课表{len(result)}条")
        return result

    def get_week_schedule(self) -> dict:
        """返回一周课表"""
        result = {}
        for py_wd in range(7):
            result[py_wd] = self.get_schedule_by_weekday(py_wd)
        logger.debug(f"[CW-Linkage] 一周课表{sum(len(v) for v in result.values())}条")
        return result

    def _resolve_schedule_path(self) -> str:
        """config.ini 读课表名 去 schedule 目录找课表"""
        config_path = os.path.join(self._data_dir, "config.ini")
        name = ""
        if os.path.isfile(config_path):
            try:
                cp = configparser.ConfigParser()
                with open(config_path, "r", encoding="utf-8", errors="ignore") as f:
                    cp.read_file(f)
                name = cp.get('General', 'schedule', fallback='').strip()
            except Exception as e:
                logger.debug(f"[CW-Linkage] config.ini utf-8 读取失败 改用 gbk 重试: {e}")
                try:
                    with open(config_path, "r", encoding="gbk", errors="ignore") as f:
                        cp.read_file(f)
                    name = cp.get('General', 'schedule', fallback='').strip()
                except Exception as e:
                    if not self._cfg_fail_logged:
                        self._cfg_fail_logged = True
                        logger.debug(f"[CW-Linkage] config.ini 读取失败: {e} ({config_path})")
        if not name and not getattr(self, "_cfg_name_missing_logged", False):
            self._cfg_name_missing_logged = True
            logger.debug(f"[CW-Linkage] config.ini 缺失或未读到 schedule 名: {config_path}")
        sched_dir = os.path.join(self._data_dir, "schedule")
        if not os.path.isdir(sched_dir):
            if self._last_resolved != "":
                logger.warning(f"[CW-Linkage] schedule 目录不存在: {sched_dir}")
                self._last_resolved = ""
            return ""
        if name and os.path.isfile(os.path.join(sched_dir, name)):
            result = os.path.join(sched_dir, name)
            if result != self._last_resolved:
                logger.debug(f"[CW-Linkage] 用课表文件: {result}")
                self._last_resolved = result
            return result
        if name and os.path.isfile(os.path.join(sched_dir, f"{name}.json")):
            result = os.path.join(sched_dir, f"{name}.json")
            if result != self._last_resolved:
                logger.debug(f"[CW-Linkage] 用课表文件: {result}")
                self._last_resolved = result
            return result
        logger.warning(f"[CW-Linkage] '{name}' 不存在 扫描目录")
        for f in sorted(os.listdir(sched_dir)):
            if f.endswith(".json") and os.path.isfile(os.path.join(sched_dir, f)):
                result = os.path.join(sched_dir, f)
                if result != self._last_resolved:
                    logger.debug(f"[CW-Linkage] 用课表文件: {result}")
                    self._last_resolved = result
                return result
        if self._last_resolved != "":
            logger.warning(f"[CW-Linkage] schedule 目录无可用课表文件: {sched_dir}")
            self._last_resolved = ""
        return ""

    def _read_schedule(self) -> dict:
        sched_file = self._resolve_schedule_path()
        if not sched_file:
            return {}
        try:
            mtime = os.path.getmtime(sched_file)
        except OSError as e:
            if self._last_read_fail != sched_file:
                self._last_read_fail = sched_file
                logger.debug(f"[CW-Linkage] 获取课表文件信息失败: {e} ({sched_file})")
            return {}
        if self._cw_cache_file == sched_file and self._cw_cache_mtime == mtime \
                and self._cw_cache_data is not None:
            return self._cw_cache_data
        try:
            with open(sched_file, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception as e:
            if self._last_read_fail != sched_file:
                self._last_read_fail = sched_file
                logger.warning(f"[CW-Linkage] 课表文件读取/解析失败: {e} ({sched_file})")
            return {}
        self._cw_cache_file = sched_file
        self._cw_cache_mtime = mtime
        self._cw_cache_data = data
        self._last_read_fail = None
        logger.debug(f"[CW-Linkage] 已读取课表: {os.path.basename(sched_file)} ({len(data)} 顶层键)")
        return data

    def _compute_state(self) -> LinkageState:
        now = precise_now()
        today = now.date()
        t = now.time()
        data = self._read_schedule()
        if not data:
            self._consecutive_failures += 1
            if self._consecutive_failures == 1:
                logger.warning(f"[CW-Linkage] 课表数据为空 (路径: {self._data_dir or '未设置'})")
            if self._consecutive_failures >= 2:
                self._try_redetect()
            return LinkageState(is_connected=False)
        self._consecutive_failures = 0
        st = LinkageState(is_connected=True, last_update=now)
        slots = self._parse_schedule(data, now)
        if not slots:
            st.time_state = TimeState.NONE
            if not getattr(self, "_no_slots_logged", False):
                self._no_slots_logged = True
                logger.debug("[CW-Linkage] 今日未解析出任何时间段")
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
                except Exception as e:
                    if not getattr(self, "_break_left_err_logged", False):
                        self._break_left_err_logged = True
                        logger.debug(f"[CW-Linkage] 课间剩余时间计算失败: {e}")
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

        logger.debug(f"[CW-Linkage] {TimeState.display_name(st.time_state)}|{st.current_subject or '-'}|"
                     f"{current_slot.start_time.strftime('%H:%M')}-{current_slot.end_time.strftime('%H:%M')}|"
                     f"下节:{st.next_lesson.subject_name if st.next_lesson else '-'}")
        return st

    def _parse_schedule(self, data: dict, now: datetime) -> list:
        """解析 cw 课表 json > _CWTimeSlot 列表"""
        # cw到底怎么想的 这时间段弄得什么幌子啊 为啥按照添加顺序写json 
        # 为啥cw不整个hh mm ss-hh mm ss写json里 ci那样多好  
        slots = []
        week_type = self._get_week_type(data, now)
        wd_key = str(now.weekday())  # Mon=0 .. Sun=6
        timeline_key = "timeline_even" if week_type == 1 else "timeline"
        timeline = data.get(timeline_key, {})
        day_timeline = timeline.get(wd_key) or timeline.get("default", [])
        if not day_timeline:
            if self._last_parse_sig != ("empty", wd_key):
                self._last_parse_sig = ("empty", wd_key)
                logger.debug(f"[CW-Linkage] 周{wd_key} 无时间线数据")
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
            else:
                if not getattr(self, "_fp_missing_logged", False):
                    self._fp_missing_logged = True
                    logger.warning(f"[CW-Linkage] 首条时段缺part基准 按00:00起算")

        # 顺序衔接
        class_counter = 0
        for unit in day_timeline:
            if not isinstance(unit, (list, tuple)) or len(unit) < 4:
                if not getattr(self, "_bad_unit_logged", False):
                    self._bad_unit_logged = True
                    logger.debug(f"[CW-Linkage] 时间线单元格式异常 {unit!r}")
                continue
            unit_type = unit[0]
            duration_min = int(unit[3])
            start_t = current_time
            end_h = start_t.hour + (start_t.minute + duration_min) // 60
            end_m = (start_t.minute + duration_min) % 60
            if end_h >= 24:
                # 跨天时段无法用 time 表示,钳到 23:59 保持 end>=start
                end_t = _dt_time(23, 59)
            else:
                end_t = _dt_time(end_h, end_m)
            current_time = end_t
            if unit_type == 0:  # 上课
                class_counter += 1
                cidx = int(unit[2])
                idx = cidx - 1  # class_idx -> schedule 索引
                subject_name = day_schedule[idx] if 0 <= idx < len(day_schedule) else ""
                slots.append(_CWTimeSlot(start_t, end_t, subject_name, "", class_counter, False))
            else:  # 课间
                slots.append(_CWTimeSlot(start_t, end_t, "课间", "", 0, True))
        sig = (self._cw_cache_mtime, wd_key, week_type)
        if sig != self._last_parse_sig:
            self._last_parse_sig = sig
            class_n = sum(1 for s in slots if not s.is_break)
            logger.debug(f"[CW-Linkage] 解析{len(slots)}时段 周{wd_key} "
                         f"{'双周' if week_type else '单周'} {class_n}节)")
        return slots

    def _get_week_type(self, data: dict, now: datetime) -> int:
        """0=单周, 1=双周"""
        try:
            start_date_str = data.get("start_date", "")
            if start_date_str:
                start_date = datetime.strptime(start_date_str, "%Y-%m-%d").date()
                week_num = (now.date() - start_date).days // 7 + 1
                return 1 if week_num % 2 == 0 else 0
            if not getattr(self, "_no_start_date_logged", False):
                self._no_start_date_logged = True
                logger.debug("课表缺start_date 按单周")
        except Exception as e:
            if not self._week_type_err_logged:
                self._week_type_err_logged = True
                logger.debug(f"[CW-Linkage] 单双周计算失败: {e}")
        return 0
