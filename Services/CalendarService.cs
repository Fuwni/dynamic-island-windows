using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

/// <summary>Upcoming events: the user's own reminders + any calendar's secret ICS link (Google, Outlook, Яндекс…).</summary>
public sealed partial class CalendarService
{
    public sealed record Event(string Title, DateTime Start, bool AllDay, bool IsReminder);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private List<Event> _ics = [];
    private DateTime _icsLoaded;

    public string? Error { get; private set; }

    public async Task RefreshAsync(bool force = false)
    {
        var url = Settings.Current.CalendarIcs.Trim();
        if (url.Length == 0) { _ics = []; Error = null; return; }
        if (!force && DateTime.Now - _icsLoaded < TimeSpan.FromMinutes(15)) return;
        try
        {
            if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
            var text = await Http.GetStringAsync(url);
            _ics = Parse(text, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(14));
            _icsLoaded = DateTime.Now;
            Error = null;
        }
        catch (Exception e) { Error = e.Message; }
    }

    public List<Event> Upcoming(int days = 7)
    {
        var now = DateTime.Now;
        var reminders = Settings.Current.Reminders
            .Where(r => r.When > now.AddMinutes(-30))
            .Select(r => new Event(r.Title, r.When, false, true));
        return _ics.Concat(reminders)
            .Where(e => e.AllDay ? e.Start.Date >= now.Date : e.Start > now.AddMinutes(-15))
            .Where(e => e.Start < now.AddDays(days))
            .OrderBy(e => e.Start)
            .ToList();
    }

    // -------------------------------------------------------------- "when" parsing for quick reminders

    /// <summary>"18:30", "завтра 9:00", "через 20 мин", "через 2 ч", "15.11 14:00".</summary>
    public static DateTime? ParseWhen(string s)
    {
        s = s.Trim().ToLowerInvariant();
        var now = DateTime.Now;

        // "через 20 мин", "in 20 min", "en 2 h", "em 20 min", "20分钟后"
        var rel = Regex.Match(s, @"(?:через|in|en|em|dentro de)\s+(\d+)\s*(мин|м|хв|min|m|ч|час|год|h|hr|hour|std|stunde|hora)");
        if (!rel.Success) rel = Regex.Match(s, @"(\d+)\s*(分钟|小时)后?");
        if (rel.Success)
        {
            int n = int.Parse(rel.Groups[1].Value);
            var unit = rel.Groups[2].Value;
            bool minutes = unit.StartsWith('м') || unit.StartsWith('m') || unit == "хв" || unit == "分钟";
            return minutes ? now.AddMinutes(n) : now.AddHours(n);
        }

        string[] dayAfter = ["послезавтра", "післязавтра", "day after tomorrow", "pasado mañana", "übermorgen", "depois de amanhã", "后天"];
        string[] tomorrow = ["завтра", "tomorrow", "mañana", "morgen", "amanhã", "明天"];
        var day = now.Date;
        bool isTomorrow = false;
        if (dayAfter.Any(s.Contains)) day = day.AddDays(2);
        else if (tomorrow.Any(s.Contains)) { day = day.AddDays(1); isTomorrow = true; }

        var date = Regex.Match(s, @"(\d{1,2})\.(\d{1,2})(?:\.(\d{2,4}))?");
        if (date.Success)
        {
            int d = int.Parse(date.Groups[1].Value), m = int.Parse(date.Groups[2].Value);
            int y = date.Groups[3].Success ? int.Parse(date.Groups[3].Value) : now.Year;
            if (y < 100) y += 2000;
            try { day = new DateTime(y, m, d); } catch { return null; }
            s = s.Remove(date.Index, date.Length);
        }

        var time = Regex.Match(s, @"(\d{1,2})[:.](\d{2})");
        if (!time.Success) time = Regex.Match(s, @"\b(\d{1,2})\b()");
        if (!time.Success) return date.Success ? day.AddHours(9) : null;

        int h = int.Parse(time.Groups[1].Value);
        int min = time.Groups[2].Value.Length > 0 ? int.Parse(time.Groups[2].Value) : 0;
        if (h > 23 || min > 59) return null;
        var result = day.AddHours(h).AddMinutes(min);
        if (!date.Success && !isTomorrow && result < now) result = result.AddDays(1); // "9:00" later today or tomorrow
        return result;
    }

    // -------------------------------------------------------------- ICS

    public static List<Event> Parse(string ics, DateTime from, DateTime to)
    {
        var events = new List<Event>();
        // unfold continuation lines
        var lines = Regex.Replace(ics, @"\r?\n[ \t]", "").Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Dictionary<string, (string Params, string Value)>? cur = null;
        var exdates = new List<DateTime>();
        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") { cur = new(); exdates = []; continue; }
            if (line == "END:VEVENT" && cur != null)
            {
                AddEvent(cur, exdates, from, to, events);
                cur = null;
                continue;
            }
            if (cur == null) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var head = line[..colon];
            var value = line[(colon + 1)..];
            var semi = head.IndexOf(';');
            var name = semi >= 0 ? head[..semi] : head;
            var prms = semi >= 0 ? head[(semi + 1)..] : "";
            if (name == "EXDATE")
            {
                foreach (var v in value.Split(','))
                    if (ParseDate(prms, v, out var ex, out _)) exdates.Add(ex);
            }
            else cur[name] = (prms, value);
        }
        return events;
    }

    private static void AddEvent(Dictionary<string, (string Params, string Value)> e, List<DateTime> exdates,
        DateTime from, DateTime to, List<Event> output)
    {
        if (!e.TryGetValue("DTSTART", out var ds) || !ParseDate(ds.Params, ds.Value, out var start, out bool allDay)) return;
        if (e.TryGetValue("STATUS", out var st) && st.Value == "CANCELLED") return;
        var title = e.TryGetValue("SUMMARY", out var sum) ? Unescape(sum.Value) : L.T("Событие");

        if (!e.TryGetValue("RRULE", out var rr))
        {
            if (start >= from && start <= to) output.Add(new Event(title, start, allDay, false));
            return;
        }

        var rule = rr.Value.Split(';').Select(p => p.Split('=')).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);
        var freq = rule.GetValueOrDefault("FREQ", "");
        int interval = int.TryParse(rule.GetValueOrDefault("INTERVAL", "1"), out var iv) ? Math.Max(1, iv) : 1;
        int count = int.TryParse(rule.GetValueOrDefault("COUNT", "0"), out var c) ? c : 0;
        DateTime until = DateTime.MaxValue;
        if (rule.TryGetValue("UNTIL", out var u) && ParseDate("", u, out var ud, out _)) until = ud;
        var byDay = rule.TryGetValue("BYDAY", out var bd)
            ? bd.Split(',').Select(d => d[^2..]).Select(DayOf).Where(d => d.HasValue).Select(d => d!.Value).ToList()
            : [];

        int n = 0;
        for (var occ = start; occ <= to && occ <= until && (count == 0 || n < count) && n < 2000;)
        {
            if (freq == "WEEKLY" && byDay.Count > 0)
            {
                // walk the days of this week
                var weekStart = occ.Date.AddDays(-(((int)occ.DayOfWeek + 6) % 7));
                foreach (var d in byDay.OrderBy(d => ((int)d + 6) % 7))
                {
                    var o = weekStart.AddDays(((int)d + 6) % 7) + start.TimeOfDay;
                    if (o < start || o > until || (count > 0 && n >= count)) continue;
                    n++;
                    if (o >= from && o <= to && !exdates.Contains(o)) output.Add(new Event(title, o, allDay, false));
                }
                occ = weekStart.AddDays(7 * interval) + start.TimeOfDay;
                continue;
            }

            n++;
            if (occ >= from && !exdates.Contains(occ)) output.Add(new Event(title, occ, allDay, false));
            occ = freq switch
            {
                "DAILY" => occ.AddDays(interval),
                "WEEKLY" => occ.AddDays(7 * interval),
                "MONTHLY" => occ.AddMonths(interval),
                "YEARLY" => occ.AddYears(interval),
                _ => DateTime.MaxValue,
            };
        }
    }

    private static DayOfWeek? DayOf(string d) => d switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null,
    };

    private static bool ParseDate(string prms, string value, out DateTime result, out bool allDay)
    {
        result = default;
        allDay = prms.Contains("VALUE=DATE") && !prms.Contains("DATE-TIME") || value.Length == 8;
        var inv = CultureInfo.InvariantCulture;
        if (allDay)
            return DateTime.TryParseExact(value, "yyyyMMdd", inv, DateTimeStyles.None, out result);

        bool utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", inv, DateTimeStyles.None, out var dt)) return false;
        if (utc) { result = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(); return true; }

        var tzid = Regex.Match(prms, "TZID=\"?([^;\":]+)").Groups[1].Value;
        if (tzid.Length > 0)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzid);
                result = TimeZoneInfo.ConvertTime(dt, tz, TimeZoneInfo.Local);
                return true;
            }
            catch { }
        }
        result = dt; // floating time = local
        return true;
    }

    private static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
