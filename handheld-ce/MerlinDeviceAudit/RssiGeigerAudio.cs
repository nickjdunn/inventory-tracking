using System;
using System.Windows.Forms;

namespace MerlinAudit
{
    /// <summary>Geiger-style clicks; faster when RSSI is stronger.</summary>
    internal sealed class RssiGeigerAudio : IDisposable
    {
        private readonly System.Windows.Forms.Timer _timer;
        private bool _enabled;
        private bool _armed;
        private int _intervalMs = 900;

        public RssiGeigerAudio()
        {
            _timer = new System.Windows.Forms.Timer();
            _timer.Tick += delegate
            {
                if (!_enabled || !_armed) return;
                CeAudio.Click();
            };
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!_enabled) Stop();
        }

        public void NotifyFound()
        {
            if (!_enabled) return;
            CeAudio.FoundTone();
        }

        public void NotifyLost()
        {
            if (!_enabled) return;
            CeAudio.LostTone();
        }

        public void UpdateLiveSignal(bool live, int rssiDbm)
        {
            if (!_enabled)
            {
                Stop();
                return;
            }

            if (!live)
            {
                _armed = false;
                _timer.Enabled = false;
                return;
            }

            _armed = true;
            _intervalMs = IntervalForRssi(rssiDbm);
            if (_intervalMs < 60) _intervalMs = 60;
            if (_intervalMs > 1200) _intervalMs = 1200;
            _timer.Interval = _intervalMs;
            if (!_timer.Enabled) _timer.Enabled = true;
        }

        private static int IntervalForRssi(int rssiDbm)
        {
            int pct = RssiProximity.Percent(rssiDbm);
            return 1100 - ((pct * 1000) / 100);
        }

        public void Stop()
        {
            _armed = false;
            _timer.Enabled = false;
        }

        public void Dispose()
        {
            _timer.Enabled = false;
            _timer.Dispose();
        }
    }
}
