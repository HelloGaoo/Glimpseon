"""
课程表
"""

import json
import os
import re
import sys

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

PROFILES_DIR = os.path.join(_BASE_DIR, "profiles")


class TimetableProfile:

    def __init__(self, name="档案配置-1"):
        self.name = name
        self.default_class_duration = 40   # 默认上课时长（m）
        self.default_break_duration = 10   # 默认课间时长（m）
        self.periods = []                  # [{"type":"上课/课间/活动", "start":"HH:MM", "end":"HH:MM"}, ...]
        self.courses = {}                  # {period_index: {"周一":"科目", "周二":"科目", ...}, ...}

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
        with open(filepath, "w", encoding="utf-8") as f:
            json.dump(self.to_dict(), f, ensure_ascii=False, indent=2)

    @classmethod
    def load(cls, filepath):
        with open(filepath, "r", encoding="utf-8") as f:
            return cls.from_dict(json.load(f))

    def add_period(self, period_type, start, end):
        """添加时间段"""
        self.periods.append({"type": period_type, "start": start, "end": end})
        idx = len(self.periods) - 1
        self.courses[str(idx)] = {}

    def remove_period(self, index):
        """删除时间段"""
        if 0 <= index < len(self.periods):
            self.periods.pop(index)
            # 重新映射 courses
            new_courses = {}
            for i in range(len(self.periods)):
                key = str(i)
                old_key = str(i) if i < index else str(i + 1)
                new_courses[key] = self.courses.pop(old_key, {})
            self.courses = new_courses

    def get_next_start_time(self):
        """获取下一个开始时间"""
        if not self.periods:
            return "08:00"
        return self.periods[-1]["end"]

    def period_count(self):
        return len(self.periods)


def get_profile_path(name):
    return os.path.join(PROFILES_DIR, f"{name}.json")


def list_profiles():
    """返回排序档案列表"""
    os.makedirs(PROFILES_DIR, exist_ok=True)
    pattern = re.compile(r"^档案配置-\d+\.json$")
    files = [f for f in os.listdir(PROFILES_DIR) if pattern.match(f)]
    files.sort(key=lambda x: int(re.search(r"\d+", x).group()))
    return [os.path.splitext(f)[0] for f in files]


def next_profile_name():
    """下一个名称"""
    names = list_profiles()
    if not names:
        return "档案配置-1"
    nums = [int(re.search(r"\d+", n).group()) for n in names]
    return f"档案配置-{max(nums) + 1}"


def ensure_default_profile():
    """至少一个"""
    names = list_profiles()
    if not names:
        name = "档案配置-1"
        profile = TimetableProfile(name)
        profile.save()
        return name
    return names[-1]


def rename_profile(old_name, new_name):
    """重命名档案"""
    old_path = get_profile_path(old_name)
    new_path = get_profile_path(new_name)
    if os.path.exists(old_path):
        os.rename(old_path, new_path)


def delete_profile(name):
    """删除档案文件"""
    path = get_profile_path(name)
    if os.path.exists(path):
        os.remove(path)
