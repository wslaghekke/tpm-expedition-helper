using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ExpeditionHelper.Host.Dev;

/// <summary>Minimal JSON writer, to avoid shipping a serializer just for the dev bridge.</summary>
internal static class Json
{
    /// <summary>Wraps a pre-serialized JSON fragment so it's embedded verbatim.</summary>
    public readonly record struct Raw(string Value);

    public static string Obj(params (string Key, object Value)[] fields)
    {
        var sb = new StringBuilder();
        WriteObj(sb, fields);
        return sb.ToString();
    }

    private static void WriteObj(StringBuilder sb, IEnumerable<(string Key, object Value)> fields)
    {
        sb.Append('{');
        bool first = true;
        foreach (var (k, v) in fields)
        {
            if (!first) sb.Append(',');
            first = false;
            WriteString(sb, k);
            sb.Append(':');
            Write(sb, v);
        }
        sb.Append('}');
    }

    public static void Write(StringBuilder sb, object v)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case Raw r: sb.Append(r.Value); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case string s: WriteString(sb, s); break;
            case int or long or short or byte or uint or ulong or ushort or sbyte:
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            case float or double or decimal:
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsFinite(d)) sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                else WriteString(sb, d.ToString(CultureInfo.InvariantCulture));
                break;
            case IDictionary<string, object> dict:
                var pairs = new List<(string, object)>();
                foreach (var kv in dict) pairs.Add((kv.Key, kv.Value));
                WriteObj(sb, pairs);
                break;
            case IEnumerable e:
                sb.Append('[');
                bool first = true;
                foreach (var item in e)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                break;
            default: WriteString(sb, v.ToString() ?? ""); break;
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
