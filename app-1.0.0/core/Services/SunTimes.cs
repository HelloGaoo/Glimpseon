// Glimpseon
// Copyright (C) 2026 HelloGaoo
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// SunTimes
namespace Glimpseon.Core.Services;

/// <summary>本地日出日落推算</summary>
public static class SunTimes
{
    private const double Zenith = 90.833;
    private const double Deg = Math.PI / 180.0;

    /// <summary>极昼极夜为 null</summary>
    public static (DateTime? Rise, DateTime? Set) Compute(DateTime date, double latitude, double longitude)
    {
        var rise = ComputeOne(date, latitude, longitude, rise: true);
        var set = ComputeOne(date, latitude, longitude, rise: false);
        return (rise, set);
    }

    private static DateTime? ComputeOne(DateTime date, double latitude, double longitude, bool rise)
    {
        try
        {
            var n = date.DayOfYear;
            var lngHour = longitude / 15.0;
            var t = n + ((rise ? 6 : 18) - lngHour) / 24.0;
            var m = 0.9856 * t - 3.289;
            var l = m + 1.916 * Math.Sin(m * Deg) + 0.020 * Math.Sin(2 * m * Deg) + 282.634;
            l = Normalize(l, 360);
            var ra = Normalize(Math.Atan(0.91764 * Math.Tan(l * Deg)) / Deg, 360);
            // 赤经象限校正
            var lQuadrant = Math.Floor(l / 90) * 90;
            var raQuadrant = Math.Floor(ra / 90) * 90;
            ra = (ra + (lQuadrant - raQuadrant)) / 15.0;
            var sinDec = 0.39782 * Math.Sin(l * Deg);
            var cosDec = Math.Cos(Math.Asin(sinDec));
            var cosH = (Math.Cos(Zenith * Deg) - sinDec * Math.Sin(latitude * Deg)) /
                       (cosDec * Math.Cos(latitude * Deg));
            if (cosH > 1 || cosH < -1)
            {
                return null;
            }
            var h = rise ? 360 - Math.Acos(cosH) / Deg : Math.Acos(cosH) / Deg;
            h /= 15.0;
            var localMean = h + ra - 0.06571 * t - 6.622;
            var ut = Normalize(localMean - lngHour, 24);
            var utc = DateTime.SpecifyKind(date.Date.AddHours(ut), DateTimeKind.Utc);
            // 对齐本地日期
            var local = utc.ToLocalTime();
            return local.Date == date.Date
                ? local
                : local.AddDays(local.Date > date.Date ? -1 : 1);
        }
        catch (Exception e)
        {
            Log.Debug($"[SUN] 日照推算失败: {e.Message}");
            return null;
        }
    }

    private static double Normalize(double value, double range)
    {
        var v = value % range;
        return v < 0 ? v + range : v;
    }
}
