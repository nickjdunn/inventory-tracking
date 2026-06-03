using System;
using System.IO;
using System.Net;
using System.Text;

namespace MerlinAudit
{
    public sealed class HttpResult
    {
        public bool Ok;
        public int StatusCode;
        public string Body = "";
        public string Error = "";
    }

    public static class HttpHelper
    {
        public static string NormalizeBaseUrl(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return "http://10.17.17.17:3000";
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                s = "http://" + s;
            }
            while (s.EndsWith("/")) s = s.Substring(0, s.Length - 1);
            return s;
        }

        public static HttpResult Get(string url, int timeoutMs)
        {
            return Request("GET", url, null, timeoutMs);
        }

        public static HttpResult PostJson(string url, string json, int timeoutMs)
        {
            return Request("POST", url, json, timeoutMs);
        }

        public static bool DownloadToFile(string url, string destPath, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;

                using (var res = (HttpWebResponse)req.GetResponse())
                using (var input = res.GetResponseStream())
                using (var output = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[4096];
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        output.Write(buf, 0, n);
                    }
                }
                return File.Exists(destPath) && new FileInfo(destPath).Length > 512;
            }
            catch
            {
                return false;
            }
        }

        private static HttpResult Request(string method, string url, string jsonBody, int timeoutMs)
        {
            var result = new HttpResult();
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.Accept = "application/json";

                if (method == "POST" && jsonBody != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
                    req.ContentType = "application/json";
                    req.ContentLength = bytes.Length;
                    using (Stream s = req.GetRequestStream())
                    {
                        s.Write(bytes, 0, bytes.Length);
                    }
                }

                using (var res = (HttpWebResponse)req.GetResponse())
                using (var stream = res.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    result.StatusCode = (int)res.StatusCode;
                    result.Body = reader.ReadToEnd();
                    result.Ok = result.StatusCode >= 200 && result.StatusCode < 300;
                    return result;
                }
            }
            catch (WebException wex)
            {
                result.Error = wex.Message;
                try
                {
                    var httpRes = (HttpWebResponse)wex.Response;
                    if (httpRes != null)
                    {
                        result.StatusCode = (int)httpRes.StatusCode;
                        using (var stream = httpRes.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            result.Body = reader.ReadToEnd();
                        }
                        string apiErr = SimpleJson.ExtractString(result.Body, "error");
                        if (apiErr.Length > 0) result.Error = apiErr;
                        else if (result.StatusCode >= 400) result.Error = "HTTP " + result.StatusCode;
                    }
                }
                catch { }
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }
    }
}
