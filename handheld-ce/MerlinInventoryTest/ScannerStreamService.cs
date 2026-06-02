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

        public void OnRfid(string wedgeText, string uiMode)
        {
            if (wedgeText == null || wedgeText.Length == 0) return;
            string mode = uiMode ?? "";

            if (_cfg.DiagnosticLogFile)
            {
                DiagnosticLog.Info("RFID mode=" + mode + " len=" + wedgeText.Length);
            }

            if (_cfg.LiveRawStream || _cfg.LiveScanStream)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    PostRfidToServer(wedgeText, mode);
                });
            }
        }

        public void OnBarcode(string code, string uiMode)
        {
            if (code == null || code.Length == 0) return;
            if (_cfg.DiagnosticLogFile)
            {
                DiagnosticLog.Info("BARCODE mode=" + uiMode + " code=" + code);
            }
            if (!_cfg.LiveRawStream) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                _api.PostScannerLive("barcode", code, null, null, false, null);
            });
        }

        private void PostRfidToServer(string wedgeText, string uiMode)
        {
            ArrayList tags = ScanLimits.ParseTags(wedgeText);
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

            if (_cfg.LiveRawStream || applyScan)
            {
                HttpResult live = _api.PostScannerLive(
                    "rfid",
                    wedgeText,
                    tags,
                    binId,
                    applyScan,
                    uiMode);
                if (_cfg.DiagnosticLogFile && !live.Ok)
                {
                    DiagnosticLog.Warn("live POST fail: " + live.Error);
                }
            }
        }
    }
}
