using System;
using System.Text;

namespace MerlinAudit
{
    public sealed class AuditClient
    {
        private readonly AuditConfig _cfg;

        public AuditClient(AuditConfig cfg)
        {
            _cfg = cfg;
        }

        public string Base
        {
            get { return HttpHelper.NormalizeBaseUrl(_cfg.ServerUrl); }
        }

        public HttpResult Ping()
        {
            return HttpHelper.Get(Base + "/api/ping", 12000);
        }

        public HttpResult UploadReport(string reportJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scanner_id\":\"");
            sb.Append(SimpleJson.Escape(_cfg.ScannerId));
            sb.Append("\",\"app_version\":\"");
            sb.Append(SimpleJson.Escape(AuditConfig.AppVersion));
            sb.Append("\",\"captured_at\":");
            sb.Append(DateTime.UtcNow.Ticks / 10000L);
            sb.Append(",\"report\":");
            sb.Append(reportJson != null && reportJson.Length > 0 ? reportJson : "{}");
            sb.Append("}");
            return HttpHelper.PostJson(Base + "/api/handheld/device-audit", sb.ToString(), 60000);
        }

        public HttpResult UploadError(string context, string message, string detail)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scanner_id\":\"");
            sb.Append(SimpleJson.Escape(_cfg.ScannerId));
            sb.Append("\",\"app_version\":\"");
            sb.Append(SimpleJson.Escape(AuditConfig.AppVersion));
            sb.Append("\",\"context\":\"");
            sb.Append(SimpleJson.Escape(context ?? ""));
            sb.Append("\",\"message\":\"");
            sb.Append(SimpleJson.Escape(message ?? ""));
            sb.Append("\",\"detail\":\"");
            sb.Append(SimpleJson.Escape(detail ?? ""));
            sb.Append("\",\"captured_at\":");
            sb.Append(DateTime.UtcNow.Ticks / 10000L);
            sb.Append("}");
            return HttpHelper.PostJson(Base + "/api/handheld/audit-error", sb.ToString(), 45000);
        }

        public HttpResult UploadLabSession(string sessionJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scanner_id\":\"");
            sb.Append(SimpleJson.Escape(_cfg.ScannerId));
            sb.Append("\",\"app_version\":\"");
            sb.Append(SimpleJson.Escape(AuditConfig.AppVersion));
            sb.Append("\",\"captured_at\":");
            sb.Append(DateTime.UtcNow.Ticks / 10000L);
            sb.Append(",\"session\":");
            sb.Append(sessionJson != null && sessionJson.Length > 0 ? sessionJson : "{}");
            sb.Append("}");
            return HttpHelper.PostJson(Base + "/api/handheld/audit-lab", sb.ToString(), 45000);
        }

        public HttpResult UploadRssiTrace(string sessionJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scanner_id\":\"");
            sb.Append(SimpleJson.Escape(_cfg.ScannerId));
            sb.Append("\",\"app_version\":\"");
            sb.Append(SimpleJson.Escape(AuditConfig.AppVersion));
            sb.Append("\",\"captured_at\":");
            sb.Append(DateTime.UtcNow.Ticks / 10000L);
            sb.Append(",\"session\":");
            sb.Append(sessionJson != null && sessionJson.Length > 0 ? sessionJson : "{}");
            sb.Append("}");
            return HttpHelper.PostJson(Base + "/api/handheld/audit-rssi-trace", sb.ToString(), 60000);
        }
    }
}
