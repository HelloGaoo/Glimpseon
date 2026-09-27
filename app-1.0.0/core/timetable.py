"""课程表"""

import json
import logging
import os
import re

from core.constants import DATA_PROFILE, ensure_data_dirs

ensure_data_dirs()

logger = logging.getLogger("Glimpseon.core.timetable")

PROFILES_DIR = DATA_PROFILE


class TimetableProfile:

    def __init__(self, name="档案配置-1"):
        self.name = name
        logger.debug(f"档案对象创建: {name}")
        self.default_class_duration = 40
        self.default_break_duration = 10
        self.periods = []
        self.courses = {}

    def to_dict(self):
        return {
            "name": self.name,
            "defaultClassDuration": self.default_class_duration,
            "defaultBreakDuration": self.default_break_duration,
            "periods": self.periods,
            "courses": self.courses,
        }

    @classmethod
    def from_dict(cls, d):
        p = cls(d.get("name", "档案配置-1"))
        p.default_class_duration = d.get("defaultClassDuration", 40)
        p.default_break_duration = d.get("defaultBreakDuration", 10)
        p.periods = d.get("periods", [])
        p.courses = d.get("courses", {})
        return p

    def save(self, filepath=None):
        if filepath is None:
            filepath = get_profile_path(self.name)
        os.makedirs(os.path.dirname(filepath), exist_ok=True)
        logger.debug(f"档案写入: {filepath}")
        with open(filepath, "w", encoding="utf-8") as f:
            json.dump(self.to_dict(), f, ensure_ascii=False, indent=2)
        logger.info(f"档案已保存: {self.name}")

    @classmethod
    def load(cls, filepath):
        with open(filepath, "r", encoding="utf-8") as f:
            profile = cls.from_dict(json.load(f))
        logger.info(f"档案已加载: {profile.name}")
        return profile

    def add_period(self, period_type, start, end):
        self.periods.append({"type": period_type, "start": start, "end": end})
        idx = len(self.periods) - 1
        self.courses[str(idx)] = {}
        logger.debug(f"添加时段: {period_type} {start}-{end}")

    def remove_period(self, index):
        if not (0 <= index < len(self.periods)):
            logger.warning(f"移除时段失败: 索引越界 {index}")
            return
        self.periods.pop(index)
        new_courses = {}
        for i in range(len(self.periods)):
            key = str(i)
            old_key = str(i) if i < index else str(i + 1)
            new_courses[key] = self.courses.pop(old_key, {})
        self.courses = new_courses
        logger.debug(f"移除时段: index={index} 剩余{len(self.periods)}个")

    def get_next_start_time(self):
        if not self.periods:
            logger.debug("无时段 返回默认起始时间 08:00")
            return "08:00"
        return self.periods[-1]["end"]

    def period_count(self):
        return len(self.periods)


def get_profile_path(name):
    return os.path.join(PROFILES_DIR, f"{name}.json")


def list_profiles():
    os.makedirs(PROFILES_DIR, exist_ok=True)
    pattern = re.compile(r"^档案配置-\d+\.json$")
    files = [f for f in os.listdir(PROFILES_DIR) if pattern.match(f)]
    files.sort(key=lambda x: int(re.search(r"\d+", x).group()))
    names = [os.path.splitext(f)[0] for f in files]
    logger.debug(f"档案列表: {names}")
    return names


def next_profile_name():
    names = list_profiles()
    if not names:
        logger.debug("无现有档案 下一个名称: 档案配置-1")
        return "档案配置-1"
    nums = [int(re.search(r"\d+", n).group()) for n in names]
    name = f"档案配置-{max(nums) + 1}"
    logger.debug(f"下一个档案名称: {name}")
    return name


def ensure_default_profile():
    names = list_profiles()
    if not names:
        name = "档案配置-1"
        profile = TimetableProfile(name)
        profile.save()
        logger.info(f"已创建默认档案: {name}")
        return name
    logger.debug(f"用现有档案: {names[-1]}")
    return names[-1]


def rename_profile(old_name, new_name):
    old_path = get_profile_path(old_name)
    new_path = get_profile_path(new_name)
    if os.path.exists(old_path):
        os.rename(old_path, new_path)
        logger.info(f"档案重命名: {old_name} -> {new_name}")
    else:
        logger.warning(f"重命名失败 档案不存在: {old_name}")


def delete_profile(name):
    path = get_profile_path(name)
    if os.path.exists(path):
        os.remove(path)
        logger.info(f"档案已删除: {name}")
    else:
        logger.warning(f"删除失败 档案不存在: {name}")
