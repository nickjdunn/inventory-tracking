using System;
using System.Collections;

namespace MerlinAudit
{
    internal sealed class RssiListEntry
    {
        public string Epc = "";
        public int Rssi;
        public bool HasRssi;

        public string DisplayLine
        {
            get { return EpcDisplay.ListLine(Rssi, HasRssi, Epc); }
        }
    }

    internal static class RssiTagList
    {
        public const int DefaultAccumulateMax = 200;

        /// <summary>All tags in this read batch — no count limit (for merge input).</summary>
        public static RssiListEntry[] FromReadings(NurTagReading[] readings)
        {
            if (readings == null || readings.Length == 0) return new RssiListEntry[0];

            var byEpc = new Hashtable();
            for (int i = 0; i < readings.Length; i++)
            {
                NurTagReading r = readings[i];
                if (r == null) continue;
                string epc = RssiTraceRecorder.NormalizeEpc(r.Epc);
                if (epc.Length == 0) continue;

                RssiListEntry existing = (RssiListEntry)byEpc[epc];
                if (existing == null)
                {
                    existing = new RssiListEntry();
                    existing.Epc = epc;
                    byEpc[epc] = existing;
                }
                if (r.HasRssi)
                {
                    existing.Rssi = r.Rssi;
                    existing.HasRssi = true;
                }
            }

            var arr = new RssiListEntry[byEpc.Count];
            int idx = 0;
            foreach (DictionaryEntry de in byEpc)
            {
                arr[idx++] = (RssiListEntry)de.Value;
            }
            return arr;
        }

        public static RssiListEntry[] BuildSorted(NurTagReading[] readings, int maxItems)
        {
            return SortAndCap(FromReadings(readings), maxItems);
        }

        public static RssiListEntry[] SortAndCap(RssiListEntry[] entries, int maxItems)
        {
            if (entries == null || entries.Length == 0) return new RssiListEntry[0];
            if (maxItems < 1) maxItems = DefaultAccumulateMax;

            var list = new ArrayList();
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null) list.Add(entries[i]);
            }
            SortByRssi(list);
            while (list.Count > maxItems) list.RemoveAt(list.Count - 1);

            var arr = new RssiListEntry[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = (RssiListEntry)list[i];
            return arr;
        }

        public static RssiListEntry[] MergeLists(RssiListEntry[] existing, RssiListEntry[] incoming, int maxItems)
        {
            if (incoming == null || incoming.Length == 0) return existing ?? new RssiListEntry[0];
            var byEpc = new Hashtable();
            if (existing != null)
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i] != null && existing[i].Epc.Length > 0)
                    {
                        byEpc[existing[i].Epc] = existing[i];
                    }
                }
            }
            for (int i = 0; i < incoming.Length; i++)
            {
                RssiListEntry n = incoming[i];
                if (n == null || n.Epc.Length == 0) continue;
                RssiListEntry old = (RssiListEntry)byEpc[n.Epc];
                if (old == null)
                {
                    byEpc[n.Epc] = n;
                    continue;
                }
                if (n.HasRssi)
                {
                    old.Rssi = n.Rssi;
                    old.HasRssi = true;
                }
                else if (!old.HasRssi)
                {
                    old.HasRssi = false;
                }
            }
            var list = new ArrayList();
            foreach (DictionaryEntry de in byEpc) list.Add(de.Value);
            SortByRssi(list);
            if (maxItems < 1) maxItems = DefaultAccumulateMax;
            while (list.Count > maxItems) list.RemoveAt(list.Count - 1);
            var arr = new RssiListEntry[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = (RssiListEntry)list[i];
            return arr;
        }

        private static void SortByRssi(ArrayList list)
        {
            for (int i = 0; i < list.Count - 1; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    RssiListEntry a = (RssiListEntry)list[i];
                    RssiListEntry b = (RssiListEntry)list[j];
                    if (Compare(a, b) < 0)
                    {
                        list[i] = b;
                        list[j] = a;
                    }
                }
            }
        }

        private static int Compare(RssiListEntry a, RssiListEntry b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            if (a.HasRssi && !b.HasRssi) return 1;
            if (!a.HasRssi && b.HasRssi) return -1;
            if (a.HasRssi && b.HasRssi)
            {
                if (a.Rssi > b.Rssi) return 1;
                if (a.Rssi < b.Rssi) return -1;
            }
            return string.Compare(a.Epc, b.Epc, StringComparison.Ordinal);
        }
    }
}
