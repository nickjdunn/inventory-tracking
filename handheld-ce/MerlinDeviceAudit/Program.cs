using System;
using System.Windows.Forms;

namespace MerlinAudit
{
    static class Program
    {
        [MTAThread]
        static void Main()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            }
            catch { }

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                HandleFatal("main", ex);
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                HandleFatal("unhandled", ex);
            }
        }

        private static void HandleFatal(string context, Exception ex)
        {
            AuditConfig cfg = AuditConfig.Load();
            try
            {
                if (RssiTraceRecorder.IsRecording) RssiTraceRecorder.StopRecording();
            }
            catch { }
            HttpResult res = AuditErrorReporter.ReportSync(cfg, context, ex, "");
            string hint = res.Ok
                ? "Error saved on server.\r\nPC: /deploy/device-audit.html\r\n(Errors section)"
                : ("Upload failed: " + (res.Error ?? "unknown"));
            try
            {
                string detail = AuditErrorReporter.FormatDetail(ex, "");
                string preview = detail.Length > 400 ? detail.Substring(0, 400) + "..." : detail;
                MessageBox.Show(
                    hint + "\r\n\r\n" + preview,
                    "Merlin Audit",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation,
                    MessageBoxDefaultButton.Button1);
            }
            catch { }
        }
    }
}
