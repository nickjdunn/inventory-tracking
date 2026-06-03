using System.Text;

namespace MerlinAudit
{
    /// <summary>Read-back of NUR_MODULESETUP fields (matches reader control program).</summary>
    public sealed class NurModuleSetupSnapshot
    {
        public bool ReadOk;
        public string ReadError = "";

        public int LinkFreqHz = -1;
        public int RxDecoding = -1;
        public int TxLevel = -1;
        public int TxModulation = -1;
        public int RegionId = -1;
        public int InventoryQ = -1;
        public int InventorySession = -1;
        public int InventoryRounds = -1;
        public int AntennaMask = -1;
        public long AntennaMaskEx = -1;
        public int SelectedAntenna = -999;
        public int InventoryTarget = -1;
        public int InventoryEpcLength = -1;
        public int RxSensitivity = -1;
        public int RfProfile = -1;
        public long OpFlags = -1;

        public static string RxDecodingLabel(int code)
        {
            switch (code)
            {
                case 0: return "FM-0";
                case 1: return "Miller-2";
                case 2: return "Miller-4";
                case 3: return "Miller-8";
                default: return code >= 0 ? ("RX?" + code) : "RX?";
            }
        }

        public static string TxModulationLabel(int code)
        {
            switch (code)
            {
                case 0: return "ASK";
                case 1: return "PR-ASK";
                default: return code >= 0 ? ("TXmod?" + code) : "TXmod?";
            }
        }

        public static string RegionLabel(int id)
        {
            switch (id)
            {
                case 0: return "EU";
                case 1: return "FCC";
                case 2: return "PRC";
                case 254: return "Custom";
                default: return id >= 0 ? ("R" + id) : "?";
            }
        }

        public string ToCompactLine()
        {
            if (!ReadOk)
            {
                return ReadError.Length > 0 ? ReadError : "module setup unreadable";
            }
            string lf = LinkFreqHz > 0 ? (LinkFreqHz / 1000) + "k" : "?k";
            string rx = RxDecodingLabel(RxDecoding);
            string tx = TxLevel >= 0 ? ("TX" + TxLevel) : "TX?";
            string mod = TxModulation >= 0 ? TxModulationLabel(TxModulation) : "";
            string ses = InventorySession >= 0 ? ("S" + InventorySession) : "";
            string q = InventoryQ >= 0 ? ("Q" + InventoryQ) : "";
            string ant = SelectedAntenna >= -1 ? ("A" + SelectedAntenna) : "";
            return lf + " " + rx + " " + tx
                + (mod.Length > 0 ? (" " + mod) : "")
                + (ses.Length > 0 ? (" " + ses) : "")
                + (q.Length > 0 ? (" " + q) : "")
                + (ant.Length > 0 ? (" " + ant) : "");
        }

        public string ToDetailLine()
        {
            if (!ReadOk) return ToCompactLine();
            var sb = new StringBuilder(160);
            sb.Append(ToCompactLine());
            if (RegionId >= 0) sb.Append(" · ").Append(RegionLabel(RegionId));
            if (InventoryRounds >= 0) sb.Append(" rnd").Append(InventoryRounds);
            if (RxSensitivity >= 0) sb.Append(" rxSens").Append(RxSensitivity);
            if (RfProfile >= 0) sb.Append(" rfProf").Append(RfProfile);
            if (InventoryEpcLength >= 0 && InventoryEpcLength != 255)
            {
                sb.Append(" epcLen").Append(InventoryEpcLength);
            }
            return sb.ToString();
        }

        public void AppendJson(StringBuilder sb, string prefix)
        {
            sb.Append('"').Append(prefix).Append("\":{");
            sb.Append("\"read_ok\":").Append(ReadOk ? "true" : "false");
            if (ReadError.Length > 0)
            {
                sb.Append(",\"read_error\":\"").Append(SimpleJson.Escape(ReadError)).Append('"');
            }
            AppendJsonInt(sb, "link_freq_hz", LinkFreqHz);
            AppendJsonInt(sb, "rx_decoding", RxDecoding);
            sb.Append(",\"rx_decoding_label\":\"").Append(SimpleJson.Escape(RxDecodingLabel(RxDecoding))).Append('"');
            AppendJsonInt(sb, "tx_level", TxLevel);
            AppendJsonInt(sb, "tx_modulation", TxModulation);
            sb.Append(",\"tx_modulation_label\":\"").Append(SimpleJson.Escape(TxModulationLabel(TxModulation))).Append('"');
            AppendJsonInt(sb, "region_id", RegionId);
            sb.Append(",\"region_label\":\"").Append(SimpleJson.Escape(RegionLabel(RegionId))).Append('"');
            AppendJsonInt(sb, "inventory_q", InventoryQ);
            AppendJsonInt(sb, "inventory_session", InventorySession);
            AppendJsonInt(sb, "inventory_rounds", InventoryRounds);
            AppendJsonInt(sb, "antenna_mask", AntennaMask);
            if (AntennaMaskEx >= 0)
            {
                sb.Append(",\"antenna_mask_ex\":").Append(AntennaMaskEx);
            }
            AppendJsonInt(sb, "selected_antenna", SelectedAntenna);
            AppendJsonInt(sb, "inventory_target", InventoryTarget);
            AppendJsonInt(sb, "inventory_epc_length", InventoryEpcLength);
            AppendJsonInt(sb, "rx_sensitivity", RxSensitivity);
            AppendJsonInt(sb, "rf_profile", RfProfile);
            if (OpFlags >= 0) sb.Append(",\"op_flags\":").Append(OpFlags);
            sb.Append('}');
        }

        private static void AppendJsonInt(StringBuilder sb, string key, int value)
        {
            if (value < -900 && key != "selected_antenna") return;
            if (key == "selected_antenna" && value <= -999) return;
            sb.Append(",\"").Append(key).Append("\":").Append(value);
        }
    }
}
