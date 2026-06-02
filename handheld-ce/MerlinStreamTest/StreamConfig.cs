using System;
using System.IO;

namespace MerlinStream
{
    /// <summary>In-memory only — no log files on the gun.</summary>
    public sealed class StreamConfig
    {
        public const string AppVersion = "stream-1.0.0";

        public static string ConfigDirectory
        {
            get
            {
                string dir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase);
                if (string.IsNullOrEmpty(dir)) dir = @"\";
                return dir;
            }
        }

        public string ServerUrl = "http://10.17.17.17:3000";
        public string ScannerId = "merlin-handheld-01";
        /// <summary>ignore | watch | scan</summary>
        public string ScreenMode = "watch";
        public string ScanBinId = "test";
        public bool HardwareNurEnabled = true;
        public string NurAssemblyPath = "";
    }
}
