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
            AuditLocalErrorStore.Save(context, message, detail);
            AuditPendingQueue.MarkError();
            HttpResult res = new AuditClient(cfg).UploadError(context, message, detail);
            if (res.Ok)
            {
                AuditLocalErrorStore.Clear();
                AuditPendingQueue.ClearEntry("error");
            }
            return res;
        }

        /// <summary>Upload error saved from a prior session (crash before HTTP completed).</summary>
        public static HttpResult FlushPending(AuditConfig cfg)
        {
            if (cfg == null || !AuditLocalErrorStore.HasPending)
            {
                return new HttpResult { Ok = false, Error = "none" };
            }

            string context;
            string message;
            string detail;
            if (!AuditLocalErrorStore.TryLoad(out context, out message, out detail))
            {
                AuditLocalErrorStore.Clear();
                return new HttpResult { Ok = false, Error = "corrupt pending file" };
            }

            HttpResult res = new AuditClient(cfg).UploadError(
                context.Length > 0 ? context : "pending",
                message.Length > 0 ? message : "prior error",
                detail);
            if (res.Ok)
            {
                AuditLocalErrorStore.Clear();
                AuditPendingQueue.ClearEntry("error");
            }
            return res;
        }

        public static void ReportAsync(AuditConfig cfg, string context, Exception ex, string extra)
        {
            if (cfg == null) return;
            string detail = FormatDetail(ex, extra);
            string message = ex != null ? (ex.Message ?? "error") : "error";
            string ctx = context ?? "";
            AuditConfig cfgCopy = cfg;
            AuditLocalErrorStore.Save(ctx, message, detail);
            AuditPendingQueue.MarkError();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    HttpResult res = new AuditClient(cfgCopy).UploadError(ctx, message, detail);
                    if (res.Ok)
                    {
                        AuditLocalErrorStore.Clear();
                        AuditPendingQueue.ClearEntry("error");
                    }
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
