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
ClassIsland 联动模块

读取Profiles/Default.json：
    - 课表 (ClassPlans)
    - 作息时间表 (TimeLayouts)
    - 科目列表 (Subjects)

"""

import json
import logging
import os
import subprocess
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
    NONE = 0              # 无课程
    PREPARE_ON_CLASS = 1  # 预备上课
    ON_CLASS = 2          # 上课中
    BREAKING = 3          # 课间休息
    AFTER_SCHOOL = 4      # 放学

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
    """作息时间表中的一个时间段"""
    start_time: datetime.time
    end_time: datetime.time
    time_type: int         # 0=上课, 1=课间
    break_name: str = ""   # 课间名称
    index: int = 0


@dataclass
class _DayPlan:
    """某一天的课表"""
    week_day: int                    #  0=周日 1=周一 ... 6=周六
    name: str = ""                   # 名字
    class_ids: list = field(default_factory=list)  # SubjectId 列表
    layout_id: str = ""              # 关联的 TimeLayout ID







def _time_from_str(s: str) -> Optional[datetime.time]:
    """HH:MM 或 HH:MM:SS 转 time"""
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
    """时间差 转HH:MM 或 HH:MM:SS"""
    total_sec = max(0, int(td.total_seconds()))
    h, rem = divmod(total_sec, 3600)
    m, s = divmod(rem, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m:02d}:{s:02d}"


def _python_weekday_to_dotnet(weekday: int) -> int:
    """Mon=1..Sun=7 → Sun=0..Sat=6"""
    # 洋人就是牛逼啊周日为啥是第一天
    return 0 if weekday == 7 else weekday



def _find_classisland_exe() -> Optional[str]:
    """查找ClassIsland 进程路径"""
    process_names = ["ClassIsland.Desktop.exe", "ClassIsland.exe"]

    for proc_name in process_names:
        path = _try_wmic(proc_name)
        if path:
            return path
    for proc_name in process_names:
        path = _try_tasklist(proc_name)
        if path:
            return path
    return None


def _try_wmic(process_name: str) -> Optional[str]:
    """wmic 获取进程路径"""
    try:
        result = subprocess.run(
            ["wmic", "process", 'where', f"name='{process_name}'",
             "get", "ExecutablePath", "/format:csv"],
            capture_output=True, text=True, timeout=5,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        for line in result.stdout.strip().splitlines():
            line = line.strip()
            if line and line.lower().endswith(".exe") and "executablepath" not in line.lower():
                return line
    except (subprocess.SubprocessError, FileNotFoundError, OSError):
        pass
    return None


def _try_tasklist(process_name: str) -> Optional[str]:
    """tasklist 获取进程路径（后备）"""
    try:
        result = subprocess.run(
            ["tasklist", "/FI", f"IMAGENAME eq {process_name}", "/V", "/FO", "CSV"],
            capture_output=True, text=True, timeout=5,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        for line in result.stdout.splitlines():
            if process_name in line and "Window Title" not in line:
                parts = [p.strip('"') for p in line.split('","')]
                if len(parts) >= 8 and parts[7] and parts[7].endswith(".exe"):
                    return parts[7]
    except (subprocess.SubprocessError, OSError):
        pass
    return None


def _find_classisland_data() -> str:
    """自动查找 ClassIsland\data"""
    exe_path = _find_classisland_exe()
    if exe_path:
        base = os.path.dirname(exe_path)
        # 从 exe 目录往上爬 data/Profiles/Default.json
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
    """ClassIsland 配置桥接器"""

    stateChanged = pyqtSignal(object)       # 状态更新
    connectedChanged = pyqtSignal(bool)     # 连接状态变化
    errorOccurred = pyqtSignal(str)         # 错误 / 路径重定向消息

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


    def set_data_path(self, path: str):
        self._data_dir = path.strip()
        self._clear_cache()

    def auto_detect(self) -> str:
        path = _find_classisland_data()
        if path:
            self.set_data_path(path)
        return path

    def start(self):
        """启动轮询循环"""
        if self._running:
            return
        self._consecutive_failures = 0
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="linkage-file")
        self._thread.start()
        logger.info(f"[Linkage] 启动 (路径: {self._data_dir or '未设置'})")

    def stop(self):
        """停止轮询循环"""
        self._running = False
        if self._thread:
            self._thread.join(timeout=3)
            self._thread = None
        logger.info("[Linkage] 停止")

    def get_state(self) -> LinkageState:
        """获取当前状态副本"""
        with self._lock:
            return self._state

    def get_current_lesson(self) -> Optional[LessonInfo]:
        """获取当前课程信息"""
        with self._lock:
            return self._state.current_lesson

    def test_connection(self) -> tuple[bool, str]:
        """测试路径有效否"""
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


    def _clear_cache(self):
        """清除所有缓存"""
        self._cached_raw = {}
        self._cached_mtime = 0
        self._slots.clear()
        self._day_plans.clear()
        self._subjects.clear()


    def _loop(self):
        """后台轮询主循环"""
        while self._running:
            try:
                self._sync_time_config()
                st = self._compute_state()
                self._commit(st)
            except Exception as e:
                logger.debug(f"[Linkage] 循环异常: {e}")
            _time.sleep(self._poll_interval)


    def _load_file_if_changed(self) -> bool:
        """检查文件是否有变更"""
        if not self._data_dir:
            return False

        profile = os.path.join(self._data_dir, _PROFILE_FILE)
        try:
            mtime = os.path.getmtime(profile)
            if mtime <= self._cached_mtime and self._cached_raw:
                return True  # 文件未变，使用缓存
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
            if self._consecutive_failures >= self._max_failures_before_redetect:
                self._try_redetect()
            return False
        except Exception as e:
            self._consecutive_failures += 1
            logger.warning(f"[Linkage] 读取文件失败: {e}")
            return False

    def _try_redetect(self):
        """重新检测路径"""
        new_path = _find_classisland_data()
        if new_path and new_path != self._data_dir:
            logger.info(f"[Linkage] 新路径: {new_path}")
            self.set_data_path(new_path)
            self._consecutive_failures = 0
            self.errorOccurred.emit(f"REDIRECT:{new_path}")

    def _parse_all(self, raw: dict):
        """解析 json"""
        self._subjects = raw.get("Subjects") or {}

        # 作息时间表
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
                            self._slots.append(_TimeSlot(
                                s, e,
                                item.get("TimeType", 1),
                                item.get("BreakName", ""),
                                i,
                            ))
                    except (ValueError, TypeError):
                        pass

        # 课表
        self._day_plans.clear()
        for pid, plan in (raw.get("ClassPlans") or {}).items():
            tr = plan.get("TimeRule", {}) or {}
            wd = tr.get("WeekDay", 0)
            classes = [c["SubjectId"] for c in plan.get("Classes", []) if c.get("IsEnabled", True)]
            self._day_plans[wd] = _DayPlan(
                week_day=wd,
                name=plan.get("Name", ""),
                class_ids=classes,
                layout_id=plan.get("TimeLayoutId", ""),
            )

    def _sync_time_config(self):
        """从c同步时间偏移"""
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

            logger.info(
                f"[Linkage] 已同步 ClassIsland 时间配置: "
                f"TimeOffset={ci_offset}, AutoEnabled={ci_auto_enabled}, "
                f"AutoIncrement={ci_auto_increment}"
            )
        except FileNotFoundError:
            pass
        except Exception as e:
            logger.debug(f"[Linkage] 同步时间配置失败: {e}")

    def _compute_state(self) -> LinkageState:
        """此时状态"""
        now = precise_now()
        today = now.date()
        weekday = today.isoweekday()
        dotnet_wd = _python_weekday_to_dotnet(weekday) 
        t = now.time()

        if not self._load_file_if_changed():
            return LinkageState(is_connected=False)

        st = LinkageState(is_connected=True, last_update=now)

        # 找到当前时间段
        slot_idx, slot = self._find_slot(t)
        plan = self._day_plans.get(dotnet_wd)

        # 没课表就今天没有课程
        if not plan:
            st.time_state = TimeState.NONE
            return st

        # 不在任何时间段内
        if slot is None:
            st.time_state = TimeState.AFTER_SCHOOL if (self._slots and t >= self._slots[-1].end_time) else TimeState.NONE
            return st

        # 判断状态
        if slot.time_type == 0:
            st.time_state = TimeState.ON_CLASS
        else:
            st.time_state = TimeState.BREAKING
            st.current_subject = slot.break_name or ""

        # 填充科目信息
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

        # 下节课
        next_idx = self._slot_to_class_index(slot_idx, offset=1)
        if 0 <= next_idx < len(plan.class_ids):
            next_sid = plan.class_ids[next_idx]
            st.next_lesson = LessonInfo.from_subject_data(self._subjects.get(next_sid, {}))

        # 剩余时间
        left = datetime.combine(today, slot.end_time) - now
        attr = "on_class_left" if slot.time_type == 0 else "on_breaking_left"
        setattr(st, attr, _fmt_delta(left))

        # 日志
        logger.debug(
            f"[Linkage] {TimeState.display_name(st.time_state)} | "
            f"{st.current_subject or '-'} | "
            f"{slot.start_time.strftime('%H:%M')}-{slot.end_time.strftime('%H:%M')} | "
            f"下节:{st.next_lesson.subject_name if st.next_lesson else '-'} | "
            f"{plan.name}(周{weekday}) 第{slot_idx+1}/{len(self._slots)}段 | 剩余{_fmt_delta(left)}"
        )
        return st

    def _find_slot(self, t: datetime.time) -> tuple[int, Optional[_TimeSlot]]:
        """查找当前进行的时间段"""
        for i, slot in enumerate(self._slots):
            if slot.start_time <= t < slot.end_time:
                return i, slot
        return -1, None

    def _slot_to_class_index(self, slot_idx: int, offset: int = 0) -> int:
        """TimeLayout slot 索引 到 课表课程序号"""
        n_classes = sum(1 for s in self._slots[:slot_idx + 1] if s.time_type == 0)
        return max(0, n_classes - 1 + offset)
        # 看不懂啥幌子


    def _commit(self, new_state: LinkageState) -> bool:
        """提交新状态"""
        # return time_state 是否变化
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
