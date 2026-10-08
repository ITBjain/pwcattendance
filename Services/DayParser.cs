using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PwcApi.Services
{
    /// <summary>
    /// Turns the free-text GroupVariations.Days column ("Mon, Wed", "Monday & Friday", "Mon-Fri",
    /// "Weekdays", "Daily", "SAT/SUN") into a clean ordered list of day codes: ["MON","WED"].
    /// The app uses these codes to decide which batches run on a given date.
    /// </summary>
    public static class DayParser
    {
        public static readonly string[] Codes = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };

        private const string DayAlt =
            "monday|mon|tuesday|tues|tue|wednesday|weds|wed|thursday|thurs|thur|thu|friday|fri|saturday|sat|sunday|sun";

        private static readonly Regex RangeRx = new Regex(
            $@"\b({DayAlt})\b\.?\s*(?:-|–|to|till|until)\s*\b({DayAlt})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DayRx = new Regex(
            $@"\b({DayAlt})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<string> Parse(string? raw)
        {
            var found = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

            var s = raw.ToLowerInvariant();

            if (s.Contains("daily") || s.Contains("everyday") || s.Contains("every day") || s.Contains("all days"))
                return Codes.ToList();
            if (s.Contains("weekday")) AddRange(found, 0, 4);
            if (s.Contains("weekend")) AddRange(found, 5, 6);

            foreach (Match m in RangeRx.Matches(s))
                AddRange(found, IndexOf(m.Groups[1].Value), IndexOf(m.Groups[2].Value));

            foreach (Match m in DayRx.Matches(s))
                found.Add(Codes[IndexOf(m.Groups[1].Value)]);

            return Codes.Where(found.Contains).ToList();
        }

        private static int IndexOf(string token)
        {
            switch (token.ToLowerInvariant().Substring(0, 3))
            {
                case "mon": return 0;
                case "tue": return 1;
                case "wed": return 2;
                case "thu": return 3;
                case "fri": return 4;
                case "sat": return 5;
                default: return 6; // sun
            }
        }

        private static void AddRange(HashSet<string> set, int from, int to)
        {
            for (int i = from; ; i = (i + 1) % 7)
            {
                set.Add(Codes[i]);
                if (i == to) break;
            }
        }
    }
}
