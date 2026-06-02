namespace MerlinStream
{
    public sealed class TagRead
    {
        public string Epc = "";
        public int? Rssi;

        public string ToJsonFragment()
        {
            if (Rssi.HasValue)
                return "{\"epc\":\"" + SimpleJson.Escape(Epc) + "\",\"rssi\":" + Rssi.Value + "}";
            return "{\"epc\":\"" + SimpleJson.Escape(Epc) + "\"}";
        }
    }
}
