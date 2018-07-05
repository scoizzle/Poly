using System;

namespace Poly {

    public static class DateTimeExtensions {
        public static DateTime FromHttpTimeString(this string text) {
            return DateTime.ParseExact(text, "r", System.Globalization.DateTimeFormatInfo.CurrentInfo);
        }

        public static bool TryFromHttpTimeString(string text, out DateTime date_time) {
            try {
                date_time = DateTime.ParseExact(text, "r", System.Globalization.DateTimeFormatInfo.CurrentInfo);
                return true;
            }
            catch {
                date_time = default;
                return false;
            }
        }

        public static DateTime ToHttpTime(this DateTime Time) {
            return new DateTime(Time.Year, Time.Month, Time.Day, Time.Hour, Time.Minute, Time.Second, DateTimeKind.Utc);
        }

        public static string ToHttpTimeString(this DateTime Time) {
            return Time.ToString("r");
        }

        public static bool TryToHttpTimeString(DateTime date_time, out string text) {
            try {
                text = date_time.ToString("r");
                return true;
            }
            catch {
                text = null;
                return false;
            }
        }
    }
}