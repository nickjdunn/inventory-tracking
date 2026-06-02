using System;

namespace MerlinHandheld
{
    /// <summary>Compare versions like 1.0.42+abc1234 (patch = git commit count).</summary>
    public static class VersionCompare
    {
        public static int[] Parse(string version)
        {
            int[] parts = { 0, 0, 0 };
            if (version == null || version.Length == 0) return parts;
            string main = version;
            int plus = main.IndexOf('+');
            if (plus >= 0) main = main.Substring(0, plus);
            int dash = main.IndexOf('-');
            if (dash >= 0) main = main.Substring(0, dash);
            string[] split = main.Split('.');
            for (int i = 0; i < split.Length && i < 3; i++)
            {
                int n;
                if (CfCompat.TryParseInt(split[i], out n)) parts[i] = n;
            }
            return parts;
        }

        /// <summary>Negative if a &lt; b, positive if a &gt; b, 0 if equal.</summary>
        public static int Compare(string a, string b)
        {
            int[] pa = Parse(a);
            int[] pb = Parse(b);
            for (int i = 0; i < 3; i++)
            {
                if (pa[i] != pb[i]) return pa[i] < pb[i] ? -1 : 1;
            }
            return 0;
        }

        public static bool IsNewer(string serverVersion, string clientVersion)
        {
            return Compare(serverVersion, clientVersion) > 0;
        }
    }
}
