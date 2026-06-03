using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MerlinAudit
{
    internal static class CeSystemInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatusEx
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
            public byte BackupBatteryFlag;
            public byte BackupBatteryLifePercent;
            public byte Reserved2;
            public int BackupBatteryLifeTime;
            public int BackupBatteryFullLifeTime;
        }

        [DllImport("coredll.dll")]
        private static extern bool GetSystemPowerStatusEx(out SystemPowerStatusEx pss, bool update);

        public static void AppendSystemJson(StringBuilder sb)
        {
            long captured = DateTime.UtcNow.Ticks / 10000L;
            AuditJson.AppendLong(sb, "captured_ticks_utc", captured);
            sb.Append(",");
            AuditJson.AppendString(sb, "machine_name", GetMachineName());
            sb.Append(",");
            AuditJson.AppendString(sb, "os_version", Environment.OSVersion != null ? Environment.OSVersion.ToString() : "");
            sb.Append(",");
            AuditJson.AppendString(sb, "clr_version", Environment.Version != null ? Environment.Version.ToString() : "");
            sb.Append(",");
            AuditJson.AppendString(sb, "app_dir", AuditConfig.ConfigDirectory ?? "");
            sb.Append(",");
            AuditJson.AppendLong(sb, "tick_count", Environment.TickCount);
            sb.Append(",");
            AuditJson.AppendLong(sb, "working_set_bytes", 0);
            sb.Append(",");
            try
            {
                AuditJson.AppendLong(sb, "gc_bytes", GC.GetTotalMemory(false));
            }
            catch
            {
                AuditJson.AppendLong(sb, "gc_bytes", 0);
            }
            AppendBattery(sb);
        }

        private static string GetMachineName()
        {
            try
            {
                return System.Net.Dns.GetHostName();
            }
            catch
            {
                return "merlin";
            }
        }

        private static void AppendBattery(StringBuilder sb)
        {
            sb.Append(",");
            try
            {
                SystemPowerStatusEx pss;
                if (!GetSystemPowerStatusEx(out pss, true))
                {
                    AuditJson.AppendString(sb, "battery", "unavailable");
                    return;
                }
                sb.Append("\"battery_percent\":").Append(pss.BatteryLifePercent);
                sb.Append(",\"ac_line\":").Append(pss.ACLineStatus);
                sb.Append(",\"battery_flag\":").Append(pss.BatteryFlag);
            }
            catch
            {
                AuditJson.AppendString(sb, "battery", "error");
            }
        }
    }
}
