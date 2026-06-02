using System;
using System.Windows.Forms;

namespace MerlinHandheld
{
    public sealed class UpdateCheckResult
    {
        public bool UpdateAvailable;
        public string ServerVersion = "";
        public string ClientVersion = "";
        public bool CabAvailable;
        public string CabUrl = "";
        public string DeployPage = "/deploy/";
    }

    public static class UpdateChecker
    {
        public static UpdateCheckResult Check(InventoryApiClient api, string clientVersion)
        {
            var result = new UpdateCheckResult();
            result.ClientVersion = clientVersion ?? "";
            HttpResult res = api.GetDeployInfo();
            if (!res.Ok)
            {
                return result;
            }
            result.ServerVersion = SimpleJson.ExtractString(res.Body, "version");
            result.CabAvailable = SimpleJson.ExtractBool(res.Body, "cab_available", false);
            result.CabUrl = SimpleJson.ExtractString(res.Body, "cab_url");
            string page = SimpleJson.ExtractString(res.Body, "deploy_page");
            if (page.Length > 0) result.DeployPage = page;
            result.UpdateAvailable = VersionCompare.IsNewer(result.ServerVersion, result.ClientVersion);
            return result;
        }

        public static void PromptIfUpdateAvailable(UpdateCheckResult result, string serverBaseUrl)
        {
            if (result == null || !result.UpdateAvailable) return;

            string msg = "A newer version is available.\r\n\r\n";
            msg += "This device: " + result.ClientVersion + "\r\n";
            msg += "On server:  " + result.ServerVersion + "\r\n\r\n";
            if (result.CabAvailable)
            {
                msg += "Download and install the new .cab from the deploy page.";
            }
            else
            {
                msg += "Ask your admin to publish a new .cab to the server.";
            }

            MessageBox.Show(msg, "Merlin Inventory update");
            DialogResult dr = DialogResult.OK;

            if (dr == DialogResult.OK && result.CabAvailable)
            {
                string baseUrl = HttpHelper.NormalizeBaseUrl(serverBaseUrl);
                string url = baseUrl + result.DeployPage;
                try
                {
                    System.Diagnostics.Process.Start(url, null);
                }
                catch
                {
                    MessageBox.Show("Open in browser:\r\n" + url, "Deploy page");
                }
            }
        }
    }
}
