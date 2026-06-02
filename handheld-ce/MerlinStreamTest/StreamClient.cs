using System;
using System.Collections;
using System.Text;
using System.Threading;

namespace MerlinStream
{
    /// <summary>Every scanner event POSTs to the server — no local storage.</summary>
    public sealed class StreamClient
    {
        private readonly StreamConfig _cfg;
        private const int TimeoutMs = 20000;
        private long _lastScanPostTicks;
        private const long ScanDebounceTicks = 500 * 10000L;

        public StreamClient(StreamConfig cfg)
        {
            _cfg = cfg;
        }

        private string Base
        {
            get { return HttpHelper.NormalizeBaseUrl(_cfg.ServerUrl); }
        }

        public void PushSession(string note)
        {
            PushEventAsync("session", null, null, null, 0, note, null, true);
        }

        public void PushRfid(string source, string raw, ArrayList tags)
        {
            int n = tags == null ? 0 : tags.Count;
            string action = ScreenAction();
            PushEventAsync("rfid", source, raw, tags, n, action, null, false);

            if (action == "scan" && n > 0)
            {
                long now = DateTime.UtcNow.Ticks;
                if (now - _lastScanPostTicks >= ScanDebounceTicks)
                {
                    _lastScanPostTicks = now;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        HttpResult scan = PostScan(tags, _cfg.ScanBinId);
                        PushEventAsync(
                            "inventory_scan",
                            source,
                            null,
                            tags,
                            n,
                            scan.Ok ? "scan_ok" : "scan_fail",
                            scan,
                            false);
                    });
                }
            }
        }

        private string ScreenAction()
        {
            string m = _cfg.ScreenMode ?? "watch";
            if (m == "ignore") return "ignored";
            if (m == "scan") return "scan";
            return "watch";
        }

        private void PushEventAsync(
            string eventType,
            string source,
            string raw,
            ArrayList tags,
            int tagCount,
            string action,
            HttpResult httpResult,
            bool announce)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                PushEventSync(eventType, source, raw, tags, tagCount, action, httpResult, announce);
            });
        }

        private HttpResult PushEventSync(
            string eventType,
            string source,
            string raw,
            ArrayList tags,
            int tagCount,
            string action,
            HttpResult httpResult,
            bool announce)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scanner_id\":\"");
            sb.Append(SimpleJson.Escape(_cfg.ScannerId));
            sb.Append("\",\"event_type\":\"");
            sb.Append(SimpleJson.Escape(eventType ?? "event"));
            sb.Append("\",\"screen\":\"");
            sb.Append(SimpleJson.Escape(_cfg.ScreenMode ?? "watch"));
            sb.Append("\",\"action\":\"");
            sb.Append(SimpleJson.Escape(action ?? ""));
            sb.Append("\",\"app_version\":\"");
            sb.Append(SimpleJson.Escape(StreamConfig.AppVersion));
            sb.Append("\",\"source\":\"");
            sb.Append(SimpleJson.Escape(source ?? ""));
            sb.Append("\",\"tag_count\":");
            sb.Append(tagCount);
            if (announce) sb.Append(",\"announce\":true");
            if (raw != null && raw.Length > 0)
            {
                sb.Append(",\"raw\":\"");
                sb.Append(SimpleJson.Escape(ScanLimits.TrimWedge(raw)));
                sb.Append("\"");
            }
            if (tags != null && tags.Count > 0)
            {
                sb.Append(",\"tags\":[");
                for (int i = 0; i < tags.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(((TagRead)tags[i]).ToJsonFragment());
                }
                sb.Append("]");
            }
            if (httpResult != null)
            {
                sb.Append(",\"http_ok\":");
                sb.Append(httpResult.Ok ? "true" : "false");
                if (httpResult.Error != null && httpResult.Error.Length > 0)
                {
                    sb.Append(",\"http_error\":\"");
                    sb.Append(SimpleJson.Escape(httpResult.Error));
                    sb.Append("\"");
                }
            }
            sb.Append("}");
            return HttpHelper.PostJson(Base + "/api/scanner/stream/event", sb.ToString(), TimeoutMs);
        }

        public HttpResult PostScan(ArrayList tags, string targetBinId)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scanner_id\":\"");
            sb.Append(SimpleJson.Escape(_cfg.ScannerId));
            sb.Append("\",\"target_container_epc\":\"");
            sb.Append(SimpleJson.Escape(targetBinId ?? ""));
            sb.Append("\",\"scanned_tags\":[");
            for (int i = 0; i < tags.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(((TagRead)tags[i]).ToJsonFragment());
            }
            sb.Append("]}");
            return HttpHelper.PostJson(Base + "/api/scan", sb.ToString(), TimeoutMs);
        }

        public HttpResult Ping()
        {
            return HttpHelper.Get(Base + "/api/ping", TimeoutMs);
        }
    }
}
