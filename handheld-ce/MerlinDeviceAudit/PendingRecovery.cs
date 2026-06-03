using System;
using System.Windows.Forms;

namespace MerlinAudit
{
    internal static class PendingRecovery
    {
        public static string FlushAll(AuditConfig cfg, AuditClient client)
        {
            if (cfg == null || client == null) return "";
            var notes = new System.Text.StringBuilder();

            if (AuditLocalErrorStore.HasPending)
            {
                HttpResult err = AuditErrorReporter.FlushPending(cfg);
                if (err.Ok)
                {
                    notes.Append("Prior error uploaded.\r\n");
                    AuditPendingQueue.ClearEntry("error");
                }
                else
                {
                    notes.Append("Error pending (");
                    notes.Append(err.Error ?? "upload failed");
                    notes.Append(").\r\n");
                }
            }

            string labJson = AuditLocalLabStore.LoadSnapshot();
            if (labJson.Length > 0 && labJson.IndexOf("\"events\":[") >= 0
                && labJson.IndexOf("\"events\":[]") < 0)
            {
                HttpResult lab = client.UploadLabSession(labJson);
                if (lab.Ok)
                {
                    notes.Append("Lab log uploaded.\r\n");
                    AuditLocalLabStore.Clear();
                }
                else
                {
                    notes.Append("Lab log pending.\r\n");
                }
            }

            string traceJson = RssiTraceRecorder.LoadPendingJson();
            if (traceJson.Length > 0 && traceJson.IndexOf("\"samples\":[") >= 0)
            {
                HttpResult tr = client.UploadRssiTrace(traceJson);
                if (tr.Ok)
                {
                    notes.Append("RSSI trace uploaded.\r\n");
                    RssiTraceRecorder.ClearPending();
                }
                else
                {
                    notes.Append("RSSI trace pending.\r\n");
                }
            }

            return notes.ToString().Trim();
        }

        public static void NotifyUser(string message)
        {
            if (message == null || message.Length == 0) return;
            try
            {
                MessageBox.Show(
                    message,
                    "Merlin Audit",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Asterisk,
                    MessageBoxDefaultButton.Button1);
            }
            catch { }
        }
    }
}
