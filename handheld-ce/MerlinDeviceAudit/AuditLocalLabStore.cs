using System.IO;

namespace MerlinAudit
{
    internal static class AuditLocalLabStore
    {
        public static string FilePath
        {
            get { return Path.Combine(AuditConfig.ConfigDirectory, "pending-lab-session.json"); }
        }

        public static void SaveSnapshot(string json)
        {
            try
            {
                if (json != null && json.Length > 0)
                {
                    CfCompat.WriteAllText(FilePath, json);
                    AuditPendingQueue.MarkLabSnapshot();
                }
            }
            catch { }
        }

        public static string LoadSnapshot()
        {
            try
            {
                if (!File.Exists(FilePath)) return "";
                return CfCompat.ReadAllText(FilePath);
            }
            catch
            {
                return "";
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                AuditPendingQueue.ClearEntry("lab");
            }
            catch { }
        }
    }
}
