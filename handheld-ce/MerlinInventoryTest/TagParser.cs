using System;
using System.Collections;

namespace MerlinHandheld
{
    public static class TagParser
    {
        public static ArrayList ParseText(string raw)
        {
            var list = new ArrayList();
            if (raw == null) return list;
            string[] parts = raw.Split(new char[] { ',', '\n', '\r', '\t', ' ' });
            for (int i = 0; i < parts.Length; i++)
            {
                string epc = parts[i] != null ? parts[i].Trim() : "";
                if (epc.Length == 0) continue;
                int pipe = epc.IndexOf('|');
                var tag = new TagRead();
                if (pipe > 0)
                {
                    tag.Epc = epc.Substring(0, pipe).Trim();
                    string rssiStr = epc.Substring(pipe + 1).Trim();
                    int rssi;
                    if (CfCompat.TryParseInt(rssiStr, out rssi)) tag.Rssi = rssi;
                }
                else
                {
                    tag.Epc = epc;
                }
                if (tag.Epc.Length > 0) list.Add(tag);
            }
            return list;
        }
    }
}
