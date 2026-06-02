using System;

namespace MerlinHandheld
{
    public sealed class BinInfo
    {
        public string Id = "";
        public string Name = "";

        public string Display
        {
            get
            {
                if (Name != null && Name.Length > 0)
                {
                    string n = Name.Length > 14 ? Name.Substring(0, 13) + "~" : Name;
                    return n + " " + Id;
                }
                return Id;
            }
        }
    }

    public sealed class ItemInfo
    {
        public string EpcId = "";
        public string Name = "";
        public string Status = "";
        public string ContainerId = "";
        public string ContainerName = "";
        public string Category = "";

        public string ListLine
        {
            get
            {
                string n = Name == null || Name.Length == 0 ? EpcId : Name;
                if (n.Length > 16) n = n.Substring(0, 15) + "~";
                if (Status != null && Status.Length > 0)
                {
                    string s = Status.Length > 6 ? Status.Substring(0, 5) : Status;
                    return n + " " + s;
                }
                return n;
            }
        }
    }

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
