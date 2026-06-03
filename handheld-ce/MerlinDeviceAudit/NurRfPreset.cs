namespace MerlinAudit
{
    /// <summary>Named NUR RF profile for bench sweeps and manual cycling.</summary>
    public sealed class NurRfPreset
    {
        public readonly string Id;
        public readonly string Label;
        public readonly int LinkFreqHz;
        public readonly int TxLevel;
        public readonly bool UseEpcSelect;
        /// <summary>0=FM-0, 1=Miller-2, 2=Miller-4, 3=Miller-8; -1=leave unchanged.</summary>
        public readonly int RxDecoding;
        /// <summary>0=ASK, 1=PR-ASK; -1=leave unchanged.</summary>
        public readonly int TxModulation;

        public NurRfPreset(
            string id, string label, int linkFreqHz, int txLevel, bool useEpcSelect)
            : this(id, label, linkFreqHz, txLevel, useEpcSelect, -1, -1)
        {
        }

        public NurRfPreset(
            string id, string label, int linkFreqHz, int txLevel, bool useEpcSelect,
            int rxDecoding, int txModulation)
        {
            Id = id ?? "";
            Label = label ?? "";
            LinkFreqHz = linkFreqHz;
            TxLevel = txLevel;
            UseEpcSelect = useEpcSelect;
            RxDecoding = rxDecoding;
            TxModulation = txModulation;
        }

        public string ShortLabel()
        {
            int k = LinkFreqHz / 1000;
            string tx = TxLevel == 0 ? "TXmax" : ("TX" + TxLevel);
            string rx = RxDecoding >= 0
                ? NurModuleSetupSnapshot.RxDecodingLabel(RxDecoding)
                : "";
            string mod = TxModulation >= 0
                ? NurModuleSetupSnapshot.TxModulationLabel(TxModulation)
                : "";
            string sel = UseEpcSelect ? "epc" : "open";
            string part = k + "k " + tx;
            if (rx.Length > 0) part += " " + rx;
            if (mod.Length > 0) part += " " + mod;
            return part + " " + sel;
        }
    }

    public static class NurRfPresets
    {
        /// <summary>
        /// Pile bench: link frequency × RX Miller encoding (open inventory, max TX).
        /// Matches reader control program RX decoding options.
        /// </summary>
        public static readonly NurRfPreset[] BenchSweep = BuildBenchSweep();

        private static NurRfPreset[] BuildBenchSweep()
        {
            int[] links = new int[] { 160000, 256000, 320000 };
            string[] linkIds = new string[] { "160", "256", "320" };
            int[] rxCodes = new int[] { 0, 1, 2, 3 };
            string[] rxIds = new string[] { "fm0", "m2", "m4", "m8" };
            string[] rxNames = new string[] { "FM-0", "Miller-2", "Miller-4", "Miller-8" };

            var list = new System.Collections.ArrayList();
            for (int li = 0; li < links.Length; li++)
            {
                for (int ri = 0; ri < rxCodes.Length; ri++)
                {
                    string id = linkIds[li] + "k_" + rxIds[ri] + "_tx0";
                    string label = (links[li] / 1000) + " kHz · " + rxNames[ri] + " · max TX · open";
                    list.Add(new NurRfPreset(
                        id, label, links[li], 0, false, rxCodes[ri], -1));
                }
            }

            // TX modulation variants at 256 kHz + Miller-4 (common tag default).
            list.Add(new NurRfPreset(
                "256k_m4_ask", "256 kHz · Miller-4 · ASK · max TX",
                256000, 0, false, 2, 0));
            list.Add(new NurRfPreset(
                "256k_m4_prask", "256 kHz · Miller-4 · PR-ASK · max TX",
                256000, 0, false, 2, 1));

            var arr = new NurRfPreset[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                arr[i] = (NurRfPreset)list[i];
            }
            return arr;
        }

        public static int Count
        {
            get { return BenchSweep.Length; }
        }

        public static NurRfPreset Get(int index)
        {
            if (index < 0) index = 0;
            if (index >= BenchSweep.Length) index = BenchSweep.Length - 1;
            return BenchSweep[index];
        }

        public static int NormalizeIndex(int index)
        {
            if (BenchSweep.Length == 0) return 0;
            int n = index % BenchSweep.Length;
            if (n < 0) n += BenchSweep.Length;
            return n;
        }

        /// <summary>Default preset: 256 kHz Miller-4 (typical Gen2 tag encoding).</summary>
        public static int DefaultPresetIndex
        {
            get
            {
                for (int i = 0; i < BenchSweep.Length; i++)
                {
                    if (BenchSweep[i].Id == "256k_m4_tx0") return i;
                }
                return 0;
            }
        }

        public static NurRfPreset FromBaseline(NurModuleSetupSnapshot snap)
        {
            int link = snap != null && snap.LinkFreqHz > 0 ? snap.LinkFreqHz : 256000;
            int tx = snap != null && snap.TxLevel >= 0 ? snap.TxLevel : 0;
            int rx = snap != null && snap.RxDecoding >= 0 ? snap.RxDecoding : -1;
            int mod = snap != null && snap.TxModulation >= 0 ? snap.TxModulation : -1;
            string rxLabel = rx >= 0
                ? NurModuleSetupSnapshot.RxDecodingLabel(rx)
                : "current";
            string label = "Current RF · " + (link / 1000) + " kHz · " + rxLabel + " (same as key 3)";
            return new NurRfPreset(
                "baseline_current", label, link, tx, false, rx, mod);
        }

        /// <summary>Order sweep: exact baseline match first, FM-0 last when using Miller.</summary>
        public static int[] BuildSweepOrder(NurModuleSetupSnapshot baseline)
        {
            int baseLink = baseline != null ? baseline.LinkFreqHz : -1;
            int baseRx = baseline != null ? baseline.RxDecoding : -1;
            var exact = new System.Collections.ArrayList();
            var sameLink = new System.Collections.ArrayList();
            var other = new System.Collections.ArrayList();
            var fm0 = new System.Collections.ArrayList();

            for (int i = 0; i < BenchSweep.Length; i++)
            {
                NurRfPreset p = BenchSweep[i];
                bool isFm0 = p.RxDecoding == 0;
                bool exactMatch = baseLink > 0 && p.LinkFreqHz == baseLink
                    && baseRx >= 0 && p.RxDecoding == baseRx;
                bool linkMatch = baseLink > 0 && p.LinkFreqHz == baseLink && !exactMatch;

                if (exactMatch)
                {
                    exact.Add(i);
                }
                else if (isFm0 && baseRx > 0)
                {
                    fm0.Add(i);
                }
                else if (linkMatch)
                {
                    sameLink.Add(i);
                }
                else
                {
                    other.Add(i);
                }
            }

            var order = new System.Collections.ArrayList();
            AppendIndices(order, exact);
            AppendIndices(order, sameLink);
            AppendIndices(order, other);
            AppendIndices(order, fm0);

            var arr = new int[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                arr[i] = (int)order[i];
            }
            return arr;
        }

        private static void AppendIndices(System.Collections.ArrayList dest, System.Collections.ArrayList src)
        {
            for (int i = 0; i < src.Count; i++)
            {
                dest.Add(src[i]);
            }
        }

        public static NurRfPreset GetByBenchIndex(int benchIndex)
        {
            if (benchIndex < 0) benchIndex = 0;
            if (benchIndex >= BenchSweep.Length) benchIndex = BenchSweep.Length - 1;
            return BenchSweep[benchIndex];
        }
    }
}
