using System;
using System.Collections;

namespace MerlinHandheld
{
    public sealed class HandheldState
    {
        public readonly ArrayList Bins = new ArrayList();
        public readonly ArrayList Items = new ArrayList();
        public readonly ArrayList HuntQueue = new ArrayList();
        public long SyncedAt;
        public int RssiNearGate = -55;
        public string LastMessage = "Not synced";
        public string LastScanSummary = "";

        public void ClearInventory()
        {
            Bins.Clear();
            Items.Clear();
            HuntQueue.Clear();
        }

        public BinInfo FindBin(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < Bins.Count; i++)
            {
                var b = (BinInfo)Bins[i];
                if (string.Compare(b.Id, id, StringComparison.OrdinalIgnoreCase) == 0) return b;
            }
            return null;
        }

        public ItemInfo FindItem(string epc)
        {
            if (epc == null) return null;
            for (int i = 0; i < Items.Count; i++)
            {
                var it = (ItemInfo)Items[i];
                if (string.Compare(it.EpcId, epc, StringComparison.OrdinalIgnoreCase) == 0) return it;
            }
            return null;
        }

        public ArrayList FilterItems(string query)
        {
            var list = new ArrayList();
            string q = (query ?? "").Trim().ToLower();
            for (int i = 0; i < Items.Count; i++)
            {
                var it = (ItemInfo)Items[i];
                if (q.Length == 0)
                {
                    list.Add(it);
                    continue;
                }
                string blob = (it.ListLine + " " + it.EpcId + " " + it.ContainerName + " " + it.Category).ToLower();
                if (blob.IndexOf(q) >= 0) list.Add(it);
            }
            return list;
        }
    }
}
