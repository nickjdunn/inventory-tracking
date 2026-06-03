using System;
using System.Collections;
using System.Text;

namespace MerlinAudit
{
    internal sealed class LabEvent
    {
        public string Test = "";
        public string Message = "";
        public string Detail = "";
        public long TimestampUtc;
    }

    /// <summary>Collects scanner lab results for upload / full audit embed.</summary>
    internal static class TestSessionLog
    {
        private static readonly ArrayList Events = new ArrayList();
        private static string _summary = "";

        public static void Clear()
        {
            Events.Clear();
            _summary = "";
        }

        public static void Add(string test, string message, string detail)
        {
            var ev = new LabEvent();
            ev.Test = test ?? "";
            ev.Message = message ?? "";
            ev.Detail = detail ?? "";
            ev.TimestampUtc = DateTime.UtcNow.Ticks / 10000L;
            Events.Add(ev);
            if (_summary.Length > 0) _summary += "\r\n";
            _summary += ev.Test + ": " + ev.Message;
        }

        public static string SummaryText
        {
            get { return _summary; }
        }

        public static string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"format\":\"merlin-lab-v1\",\"events\":[");
            for (int i = 0; i < Events.Count; i++)
            {
                if (i > 0) sb.Append(",");
                LabEvent ev = (LabEvent)Events[i];
                sb.Append("{\"test\":\"");
                sb.Append(SimpleJson.Escape(ev.Test));
                sb.Append("\",\"message\":\"");
                sb.Append(SimpleJson.Escape(ev.Message));
                sb.Append("\",\"detail\":\"");
                sb.Append(SimpleJson.Escape(ShortDetail(ev.Detail)));
                sb.Append("\",\"timestamp_utc\":");
                sb.Append(ev.TimestampUtc);
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string ShortDetail(string d)
        {
            if (d == null) return "";
            if (d.Length <= 800) return d;
            return d.Substring(0, 799) + "~";
        }
    }
}
