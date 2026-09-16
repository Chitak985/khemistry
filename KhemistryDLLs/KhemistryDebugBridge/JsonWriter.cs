using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace Khemistry.DebugBridge
{
    internal static class JsonWriter
    {
        internal static string Serialize(object value)
        {
            StringBuilder output = new StringBuilder();
            Write(value, output);
            return output.ToString();
        }

        private static void Write(object value, StringBuilder output)
        {
            if (value == null) { output.Append("null"); return; }
            if (value is string text) { WriteString(text, output); return; }
            if (value is bool boolean) { output.Append(boolean ? "true" : "false"); return; }
            if (value is IDictionary dictionary)
            {
                output.Append('{');
                bool first = true;
                foreach (DictionaryEntry pair in dictionary)
                {
                    if (!first) output.Append(',');
                    first = false;
                    WriteString(Convert.ToString(pair.Key, CultureInfo.InvariantCulture), output);
                    output.Append(':');
                    Write(pair.Value, output);
                }
                output.Append('}');
                return;
            }
            if (value is IEnumerable sequence)
            {
                output.Append('[');
                bool first = true;
                foreach (object item in sequence)
                {
                    if (!first) output.Append(',');
                    first = false;
                    Write(item, output);
                }
                output.Append(']');
                return;
            }
            if (value is Enum)
            {
                WriteString(value.ToString(), output);
                return;
            }
            if (value is float f && (float.IsNaN(f) || float.IsInfinity(f))
                || value is double d && (double.IsNaN(d) || double.IsInfinity(d)))
            {
                output.Append("null");
                return;
            }
            if (value is IFormattable formattable)
            {
                output.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            }
            WriteString(value.ToString(), output);
        }

        private static void WriteString(string value, StringBuilder output)
        {
            output.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (c < 32) output.Append("\\u").Append(((int)c).ToString("x4"));
                        else output.Append(c);
                        break;
                }
            }
            output.Append('"');
        }
    }
}
