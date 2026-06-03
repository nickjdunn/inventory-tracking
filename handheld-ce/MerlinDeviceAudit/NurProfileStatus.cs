namespace MerlinAudit
{
    /// <summary>Result of applying and reading back NUR module RF settings.</summary>
    public sealed class NurProfileStatus
    {
        public bool ApplyCalled;
        public bool ApplyOk;
        public string ApplyError = "";

        public int ReadLinkFreqHz = -1;
        public int ReadTxLevel = -1;
        public int ReadRxDecoding = -1;
        public bool LinkFreqOk;
        public bool TxLevelOk;
        public bool RxDecodingOk;

        public bool EpcSelectMethodFound;
        public bool LastEpcSelectOk;

        public const int TargetLinkFreqHz = 160000;
        public const int TargetTxLevel = 0;

        public string ToDisplayLine()
        {
            if (!ApplyCalled)
            {
                return "RF: NUR not connected";
            }
            if (!ApplyOk)
            {
                return "RF: apply failed " + ApplyError;
            }

            string lf = LinkFreqOk
                ? "160kHz OK"
                : (ReadLinkFreqHz > 0 ? "freq " + (ReadLinkFreqHz / 1000) + "k" : "freq ?");
            string tx = TxLevelOk
                ? "TX max OK"
                : (ReadTxLevel >= 0 ? "TX lvl " + ReadTxLevel : "TX ?");
            string rx = ReadRxDecoding >= 0
                ? NurModuleSetupSnapshot.RxDecodingLabel(ReadRxDecoding)
                : "";
            string sel = EpcSelectMethodFound ? "EPC-filter" : "no EPC-filter";
            string line = "RF: " + lf + " · " + tx;
            if (rx.Length > 0) line += " · " + rx;
            line += " · " + sel;
            return line;
        }
    }
}
