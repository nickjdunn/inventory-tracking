using System;

namespace MerlinAudit
{
    public sealed class NurTagReading
    {
        public string Epc = "";
        public int Rssi;
        public bool HasRssi;
        public long TimestampUtc;

        public string RssiLabel
        {
            get
            {
                if (!HasRssi) return "n/a";
                return Rssi.ToString() + " dBm";
            }
        }

        public string StrengthBar
        {
            get
            {
                if (!HasRssi) return "----------";
                int pct = RssiProximity.Percent(Rssi);
                int bars = (pct * 10) / 100;
                if (bars < 1) bars = 1;
                if (bars > 10) bars = 10;
                var pad = new char[10];
                for (int i = 0; i < 10; i++) pad[i] = i < bars ? '#' : '-';
                return new string(pad);
            }
        }
    }

    public sealed class NurTagReadingsEventArgs : EventArgs
    {
        public readonly NurTagReading[] Readings;
        public readonly string EpcCsv;

        public NurTagReadingsEventArgs(NurTagReading[] readings, string epcCsv)
        {
            Readings = readings ?? new NurTagReading[0];
            EpcCsv = epcCsv ?? "";
        }
    }
}
