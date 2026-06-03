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
                int score = Rssi + 90;
                if (score < 0) score = 0;
                if (score > 60) score = 60;
                int bars = (score * 10) / 60;
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
