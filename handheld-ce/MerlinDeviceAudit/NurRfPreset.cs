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

        public NurRfPreset(string id, string label, int linkFreqHz, int txLevel, bool useEpcSelect)
        {
            Id = id ?? "";
            Label = label ?? "";
            LinkFreqHz = linkFreqHz;
            TxLevel = txLevel;
            UseEpcSelect = useEpcSelect;
        }

        public string ShortLabel()
        {
            int k = LinkFreqHz / 1000;
            string tx = TxLevel == 0 ? "TXmax" : ("TX" + TxLevel);
            string sel = UseEpcSelect ? "epc" : "open";
            return k + "k " + tx + " " + sel;
        }
    }

    public static class NurRfPresets
    {
        public static readonly NurRfPreset[] BenchSweep = new NurRfPreset[]
        {
            new NurRfPreset("160_tx0_epc", "160 kHz · max TX · EPC filter", 160000, 0, true),
            new NurRfPreset("160_tx0_open", "160 kHz · max TX · open inv", 160000, 0, false),
            new NurRfPreset("160_tx1_epc", "160 kHz · TX lvl 1 · EPC", 160000, 1, true),
            new NurRfPreset("160_tx2_epc", "160 kHz · TX lvl 2 · EPC", 160000, 2, true),
            new NurRfPreset("256_tx0_epc", "256 kHz · max TX · EPC", 256000, 0, true),
            new NurRfPreset("256_tx0_open", "256 kHz · max TX · open", 256000, 0, false),
            new NurRfPreset("256_tx1_epc", "256 kHz · TX lvl 1 · EPC", 256000, 1, true),
            new NurRfPreset("320_tx0_epc", "320 kHz · max TX · EPC", 320000, 0, true),
            new NurRfPreset("320_tx0_open", "320 kHz · max TX · open", 320000, 0, false),
        };

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
    }
}
