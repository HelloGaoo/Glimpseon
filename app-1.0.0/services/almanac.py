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

"""黄历服务"""

import datetime

from core.logger import logger

_ALMANAC_CACHE = {}  # 'y-m-d' -> dict/None


class AlmanacService:

    @staticmethod
    def get_today(day: datetime.date = None):
        """当日农历 干支 宜忌"""
        day = day or datetime.date.today()
        key = day.isoformat()
        if key in _ALMANAC_CACHE:
            return _ALMANAC_CACHE[key]
        data = None
        try:
            import cnlunar
            lunar = cnlunar.Lunar(datetime.datetime(day.year, day.month, day.day))
            term = str(lunar.todaySolarTerms or "").strip()
            data = {
                "date": key,
                "lunar_month": lunar.lunarMonthCn.replace("大", "").replace("小", ""),
                "lunar_day": lunar.lunarDayCn,
                "year_gz": lunar.year8Char,
                "month_gz": lunar.month8Char,
                "day_gz": lunar.day8Char,
                "zodiac": lunar.chineseYearZodiac,
                "solar_term": "" if term in ("", "无") else term,
                "good": [str(x) for x in (lunar.goodThing or [])],
                "bad": [str(x) for x in (lunar.badThing or [])],
            }
            logger.debug(f"黄历数据 {key} 宜{len(data['good'])} 忌{len(data['bad'])}")
        except Exception as e:
            logger.error(f"黄历数据生成失败 {e}")
        _ALMANAC_CACHE[key] = data
        return data
