using System.Drawing;

namespace MerlinAudit
{
    /// <summary>Maps UHF RSSI (dBm) to proximity percent and red→green color.</summary>
    internal static class RssiProximity
    {
        public const int FarDbm = -82;
        public const int NearDbm = -38;

        public static int Percent(int rssiDbm)
        {
            if (rssiDbm <= FarDbm) return 0;
            if (rssiDbm >= NearDbm) return 100;
            return ((rssiDbm - FarDbm) * 100) / (NearDbm - FarDbm);
        }

        public static Color ColorForRssi(int rssiDbm)
        {
            int pct = Percent(rssiDbm);
            int r = (220 * (100 - pct)) / 100;
            int g = (220 * pct) / 100;
            if (r < 40) r = 40;
            if (g < 40) g = 40;
            return Color.FromArgb(r, g, 32);
        }

        public static string ClosenessLabel(int rssiDbm)
        {
            int pct = Percent(rssiDbm);
            if (pct >= 85) return "Very close";
            if (pct >= 60) return "Close";
            if (pct >= 35) return "Medium";
            if (pct >= 15) return "Far";
            return "Weak";
        }
    }
}
