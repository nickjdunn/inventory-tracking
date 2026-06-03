using System;
using System.Text;

namespace MerlinAudit
{
    internal static class AuditJson
    {
        public static void AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append("\"").Append(SimpleJson.Escape(key)).Append("\":\"");
            sb.Append(SimpleJson.Escape(value ?? "")).Append("\"");
        }

        public static void AppendBool(StringBuilder sb, string key, bool value)
        {
            sb.Append("\"").Append(SimpleJson.Escape(key)).Append("\":");
            sb.Append(value ? "true" : "false");
        }

        public static void AppendLong(StringBuilder sb, string key, long value)
        {
            sb.Append("\"").Append(SimpleJson.Escape(key)).Append("\":").Append(value);
        }

        public static void AppendObject(StringBuilder sb, string key, string jsonObjectBody)
        {
            sb.Append("\"").Append(SimpleJson.Escape(key)).Append("\":{");
            sb.Append(jsonObjectBody ?? "");
            sb.Append("}");
        }

        public static void AppendArray(StringBuilder sb, string key, string jsonArrayBody)
        {
            sb.Append("\"").Append(SimpleJson.Escape(key)).Append("\":[");
            sb.Append(jsonArrayBody ?? "");
            sb.Append("]");
        }
    }
}
