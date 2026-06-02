using System;
using System.Text;

namespace MerlinStream
{
    public static class UrlCodec
    {
        public static string Encode(string value)
        {
            if (value == null) return "";
            var sb = new StringBuilder();
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                char c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                    c == '-' || c == '_' || c == '.' || c == '~')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('%');
                    sb.Append(b.ToString("X2"));
                }
            }
            return sb.ToString();
        }
    }
}
