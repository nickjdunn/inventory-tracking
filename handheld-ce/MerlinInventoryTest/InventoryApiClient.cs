using System;
using System.Collections;
using System.Text;

namespace MerlinHandheld
{
    public sealed class InventoryApiClient
    {
        private readonly AppConfig _cfg;
        private const int TimeoutMs = 20000;

        public InventoryApiClient(AppConfig cfg)
        {
            _cfg = cfg;
        }

        private string Base
        {
            get { return HttpHelper.NormalizeBaseUrl(_cfg.ServerUrl); }
        }

        public HttpResult Ping()
        {
            return HttpHelper.Get(Base + "/api/ping", TimeoutMs);
        }

        public HttpResult ScannerPing()
        {
            string url = Base + "/api/scanner/ping?scanner_id=" +
                         UrlCodec.Encode(_cfg.ScannerId) + "&mode=native";
            return HttpHelper.Get(url, TimeoutMs);
        }

        public HttpResult GetDeployInfo()
        {
            return HttpHelper.Get(Base + "/api/deploy/info", TimeoutMs);
        }

        public HttpResult SyncSummary()
        {
            return HttpHelper.Get(Base + "/api/handheld/sync-summary", TimeoutMs);
        }

        public bool TryApplySync(string json, HandheldState state, out string error)
        {
            error = "";
            if (json == null || json.IndexOf("\"error\"") >= 0)
            {
                error = SimpleJson.ExtractString(json, "error");
                if (error.Length == 0) error = "Sync failed";
                return false;
            }

            state.ClearInventory();
            state.RssiNearGate = SimpleJson.ExtractInt(json, "rssi_near_gate", -55);
            state.SyncedAt = DateTime.UtcNow.Ticks;

            ParseObjectArray(json, "containers", state.Bins, ParseBin);
            ParseObjectArray(json, "items", state.Items, ParseItem);

            string huntJson = ExtractArrayBody(json, "activeSearchQueue");
            if (huntJson != null)
            {
                ParseStringArray(huntJson, state.HuntQueue);
            }

            int ic = SimpleJson.ExtractInt(json, "item_count", state.Items.Count);
            int bc = SimpleJson.ExtractInt(json, "bin_count", state.Bins.Count);
            if (state.Items.Count == 0 && ic > 0)
            {
                state.LastMessage = "Summary: " + ic + " items (full list not in summary)";
            }
            else
            {
                state.LastMessage = "Synced " + state.Items.Count + " items, " + state.Bins.Count + " bins";
            }
            return true;
        }

        public HttpResult FullSync()
        {
            return HttpHelper.Get(Base + "/api/handheld/sync", 60000);
        }

        public bool TryApplyFullSync(string json, HandheldState state, out string error)
        {
            return TryApplySync(json, state, out error);
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

        public HttpResult PostHuntQueue(ArrayList epcList)
        {
            var sb = new StringBuilder();
            sb.Append("{\"epc_ids\":[");
            for (int i = 0; i < epcList.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"");
                sb.Append(SimpleJson.Escape((string)epcList[i]));
                sb.Append("\"");
            }
            sb.Append("]}");
            return HttpHelper.PostJson(Base + "/api/search/target", sb.ToString(), TimeoutMs);
        }

        public HttpResult GetSearchTarget()
        {
            return HttpHelper.Get(Base + "/api/search/target", TimeoutMs);
        }

        public HttpResult LookupUpc(string upc)
        {
            string code = (upc ?? "").Trim();
            return HttpHelper.Get(Base + "/api/upc/lookup/" + UrlCodec.Encode(code), TimeoutMs);
        }

        public HttpResult ValidateEpc(string epc)
        {
            return HttpHelper.Get(
                Base + "/api/epc/validate?epc=" + UrlCodec.Encode(epc) + "&role=item",
                TimeoutMs);
        }

        public HttpResult RegisterItem(string epc, string name, string category, string upc, string homeBinId)
        {
            var sb = new StringBuilder();
            sb.Append("{\"epc_id\":\"");
            sb.Append(SimpleJson.Escape(epc));
            sb.Append("\",\"name\":\"");
            sb.Append(SimpleJson.Escape(name));
            sb.Append("\",\"category\":");
            if (category != null && category.Length > 0)
            {
                sb.Append("\"");
                sb.Append(SimpleJson.Escape(category));
                sb.Append("\"");
            }
            else sb.Append("null");
            sb.Append(",\"upc\":");
            if (upc != null && upc.Length > 0)
            {
                sb.Append("\"");
                sb.Append(SimpleJson.Escape(upc));
                sb.Append("\"");
            }
            else sb.Append("null");
            sb.Append(",\"home_container_id\":");
            if (homeBinId != null && homeBinId.Length > 0)
            {
                sb.Append("\"");
                sb.Append(SimpleJson.Escape(homeBinId));
                sb.Append("\"");
            }
            else sb.Append("null");
            sb.Append("}");
            return HttpHelper.PostJson(Base + "/api/items", sb.ToString(), TimeoutMs);
        }

        public static string FormatScanResult(string json)
        {
            if (json == null) return "No response";
            if (json.IndexOf("\"error\"") >= 0)
            {
                string err = SimpleJson.ExtractString(json, "error");
                if (err.Length > 0) return "Error: " + err;
            }
            int received = SimpleJson.ExtractInt(json, "tags_received", 0);
            int processed = SimpleJson.ExtractInt(json, "tags_processed", 0);
            string zone = SimpleJson.ExtractNestedString(json, "spatial_zone", "container_name");
            if (zone.Length > 0) return processed + " tags → bin " + zone + " (zone)";
            return received + " read, " + processed + " processed";
        }

        private static void ParseBin(string chunk, IList into)
        {
            var b = new BinInfo();
            b.Id = SimpleJson.ExtractString(chunk, "id");
            b.Name = SimpleJson.ExtractString(chunk, "name");
            if (b.Id.Length > 0) into.Add(b);
        }

        private static void ParseItem(string chunk, IList into)
        {
            var it = new ItemInfo();
            it.EpcId = SimpleJson.ExtractString(chunk, "epc_id");
            it.Name = SimpleJson.ExtractString(chunk, "name");
            it.Status = SimpleJson.ExtractString(chunk, "status");
            it.ContainerId = SimpleJson.ExtractString(chunk, "container_id");
            it.ContainerName = SimpleJson.ExtractString(chunk, "container_name");
            it.Category = SimpleJson.ExtractString(chunk, "category");
            if (it.EpcId.Length > 0) into.Add(it);
        }

        public HttpResult PostNearFieldIngest(ArrayList tags)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scanned_tags\":[");
            for (int i = 0; i < tags.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(((TagRead)tags[i]).ToJsonFragment());
            }
            sb.Append("]}");
            return HttpHelper.PostJson(Base + "/api/scan/near-field-ingest", sb.ToString(), TimeoutMs);
        }

        /// <summary>Human-readable hunt RSSI line from GET /api/search/target JSON.</summary>
        public static string FormatHuntSignal(string json)
        {
            if (json == null || json.Length == 0) return "";
            string zone = SimpleJson.ExtractNestedString(json, "hunt_signal", "zone");
            string msg = SimpleJson.ExtractNestedString(json, "hunt_signal", "message");
            int rssi = SimpleJson.ExtractInt(json, "rssi", -999);
            if (rssi == -999)
            {
                rssi = SimpleJson.ExtractNestedInt(json, "hunt_signal", "rssi", -999);
            }
            if (zone.Length > 0)
            {
                if (rssi > -999) return zone + " (" + rssi + " dBm)";
                return zone;
            }
            if (msg.Length > 0) return msg;
            return "";
        }

        private static void ParseObjectArray(string json, string arrayKey, IList into, Action<string, IList> parseOne)
        {
            string body = ExtractArrayBody(json, arrayKey);
            if (body == null) return;
            int i = 0;
            while (i < body.Length)
            {
                int start = body.IndexOf('{', i);
                if (start < 0) break;
                int depth = 0;
                int end = -1;
                for (int j = start; j < body.Length; j++)
                {
                    if (body[j] == '{') depth++;
                    else if (body[j] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            end = j;
                            break;
                        }
                    }
                }
                if (end < 0) break;
                string chunk = body.Substring(start, end - start + 1);
                parseOne(chunk, into);
                i = end + 1;
            }
        }

        private static string ExtractArrayBody(string json, string arrayKey)
        {
            string marker = "\"" + arrayKey + "\"";
            int keyIdx = json.IndexOf(marker);
            if (keyIdx < 0) return null;
            int bracket = json.IndexOf('[', keyIdx);
            if (bracket < 0) return null;
            int depth = 0;
            for (int i = bracket; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']')
                {
                    depth--;
                    if (depth == 0) return json.Substring(bracket + 1, i - bracket - 1);
                }
            }
            return null;
        }

        private static void ParseStringArray(string arrayBody, IList into)
        {
            int i = 0;
            while (i < arrayBody.Length)
            {
                int q = arrayBody.IndexOf('"', i);
                if (q < 0) break;
                int q2 = arrayBody.IndexOf('"', q + 1);
                if (q2 < 0) break;
                into.Add(arrayBody.Substring(q + 1, q2 - q - 1));
                i = q2 + 1;
            }
        }
    }
}
