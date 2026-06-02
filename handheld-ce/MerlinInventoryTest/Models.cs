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
                if (Name != null && Name.Length > 0) return Name + " (" + Id + ")";
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
                if (Status != null && Status.Length > 0) return n + " [" + Status + "]";
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
