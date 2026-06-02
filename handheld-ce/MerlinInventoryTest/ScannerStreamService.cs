using System;
using System.Collections;
using System.Threading;

namespace MerlinHandheld
{
    /// <summary>Streams RFID/barcode reads to the server for live browser view and optional real-time inventory scan.</summary>
    public sealed class ScannerStreamService
    {
        private readonly AppConfig _cfg;
        private readonly InventoryApiClient _api;
        private long _lastScanPostTicks;
        private const long ScanDebounceTicks = 400 * 10000L;

        public ScannerStreamService(AppConfig cfg, InventoryApiClient api)
        {
            _cfg = cfg;
            _api = api;
        }

        public void OnRfid(string wedgeText, string uiMode, string source)
        {
            if (wedgeText == null || wedgeText.Length == 0) return;
            string mode = uiMode ?? "";
            ArrayList tags = ScanLimits.ParseTags(wedgeText);
            DiagnosticLog.LogParsedTags("stream " + (source ?? ""), tags, wedgeText.Length);

            if (_cfg.LiveRawStream || _cfg.LiveScanStream)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    PostRfidToServer(wedgeText, mode, tags);
                });
            }
            else if (DiagnosticLog.IsEnabled)
            {
                DiagnosticLog.LogLivePost(mode, wedgeText.Length, tags.Count, false, false, null);
            }
        }

        public void OnBarcode(string code, string uiMode)
        {
            if (code == null || code.Length == 0) return;
            DiagnosticLog.Info("BARCODE ui=" + (uiMode ?? "") + " code=" + code);
            if (!_cfg.LiveRawStream) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult live = _api.PostScannerLive("barcode", code, null, null, false, uiMode);
                DiagnosticLog.LogLivePost(uiMode, code.Length, 0, false, true, live);
            });
        }

        private void PostRfidToServer(string wedgeText, string uiMode, ArrayList tags)
        {
            string binId = _cfg.LiveScanBinId;
            if (binId == null) binId = "";
            if (binId.Length == 0) binId = _cfg.LastBinId ?? "";

            bool applyScan = false;
            if (_cfg.LiveScanStream && tags.Count > 0)
            {
                long now = DateTime.UtcNow.Ticks;
                if (now - _lastScanPostTicks >= ScanDebounceTicks)
                {
                    _lastScanPostTicks = now;
                    applyScan = true;
                }
            }

            bool attempted = _cfg.LiveRawStream || applyScan;
            HttpResult live = null;
            if (attempted)
            {
                live = _api.PostScannerLive(
                    "rfid",
                    wedgeText,
                    tags,
                    binId,
                    applyScan,
                    uiMode);
            }
            DiagnosticLog.LogLivePost(
                uiMode,
                wedgeText.Length,
                tags.Count,
                applyScan,
                attempted,
                live);
        }
    }
}
