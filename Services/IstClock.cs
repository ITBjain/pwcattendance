using System;
using System.Globalization;

namespace PwcApi.Services
{
    /// <summary>
    /// Single source of "India time" for the whole API (works on Windows, Linux and Railway containers).
    /// All attendance dates/times are stored in IST, so every controller should use this.
    /// </summary>
    public static class IstClock
    {
        private static readonly TimeZoneInfo Zone = ResolveZone();

        private static TimeZoneInfo ResolveZone()
        {
            foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch { /* try next id */ }
            }
            // Last-resort fallback for containers without tzdata
            return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "IST");
        }

        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

        public static DateTime Today => Now.Date;

        /// <summary>ISO-8601 string with the +05:30 offset, e.g. 2026-10-07T09:12:30+05:30 (parsed directly by iOS ISO8601DateFormatter).</summary>
        public static string ToIso(DateTime istLocal) =>
            istLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "+05:30";
    }
}
