using System;
using System.Text;
using System.Threading;

namespace MerlinAudit
{
    internal static class AuditErrorReporter
    {
        public static HttpResult ReportSync(AuditConfig cfg, string context, Exception ex, string extra)
        {
            if (cfg == null) return new HttpResult { Ok = false, Error = "no config" };
            string detail = FormatDetail(ex, extra);
            string message = ex != null ? (ex.Message ?? "error") : "error";
            return new AuditClient(cfg).UploadError(context, message, detail);
        }

        public static void ReportAsync(AuditConfig cfg, string context, Exception ex, string extra)
        {
            if (cfg == null) return;
            string detail = FormatDetail(ex, extra);
            string message = ex != null ? (ex.Message ?? "error") : "error";
            string ctx = context ?? "";
            AuditConfig cfgCopy = cfg;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    new AuditClient(cfgCopy).UploadError(ctx, message, detail);
                }
                catch { }
            });
        }

        public static string FormatDetail(Exception ex, string extra)
        {
            var sb = new StringBuilder();
            if (extra != null && extra.Length > 0)
            {
                sb.Append(extra);
                sb.Append("\r\n\r\n");
            }
            if (ex != null)
            {
                sb.Append(ex.ToString());
            }
            string text = sb.ToString();
            if (text.Length > 12000) text = text.Substring(0, 12000) + "\r\n...[truncated]";
            return text;
        }
    }
}
