using System;

namespace Poly {

    public static class DateTimeExtensions {
        public static DateTime FromHttpTimeString(this string text) =>
            TryFromHttpTimeString(text, out DateTime date_time) ?
                date_time :
                default;

        public static bool TryFromHttpTimeString(string text, out DateTime date_time) =>
            DateTime.TryParseExact(text, "r", System.Globalization.DateTimeFormatInfo.CurrentInfo, System.Globalization.DateTimeStyles.None, out date_time);

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