using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// A separate file per game run. Errors in diagnostic output must not affect translation.
public sealed class SakuraTraceLog
{
    private readonly object gate = new object();
    private readonly Action<string> warning;
    private readonly string basePath;
    private bool failed;
    public string JsonPath { get { return basePath + ".jsonl"; } }
    public string TextPath { get { return basePath + ".txt"; } }

    public SakuraTraceLog(string directory, string session, Action<string> onWarning)
    {
        warning = onWarning;
        Directory.CreateDirectory(directory);
        basePath = Path.Combine(directory, "Sakura-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + session);
        using (File.Create(JsonPath)) { }
        using (File.Create(TextPath)) { }
    }

    public void Write(Dictionary<string, object> record, string readable)
    {
        lock (gate)
        {
            if (failed) return;
            try
            {
                record["timestamp_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                File.AppendAllText(JsonPath, Encode(record) + "\n", new UTF8Encoding(false));
                File.AppendAllText(TextPath, readable + "\r\n\r\n", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                failed = true;
                if (warning != null) warning("Detailed Sakura log stopped after write error: " + ex.Message);
            }
        }
    }

    public static string Encode(object value)
    {
        if (value == null) return "null";
        string text = value as string;
        if (text != null)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32 || char.IsSurrogate(c)) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
        if (value is bool) return (bool)value ? "true" : "false";
        IDictionary<string, object> dictionary = value as IDictionary<string, object>;
        if (dictionary != null)
        {
            List<string> entries = new List<string>();
            foreach (KeyValuePair<string, object> item in dictionary) entries.Add(Encode(item.Key) + ":" + Encode(item.Value));
            return "{" + string.Join(",", entries.ToArray()) + "}";
        }
        IEnumerable sequence = value as IEnumerable;
        if (sequence != null)
        {
            List<string> entries = new List<string>();
            foreach (object item in sequence) entries.Add(Encode(item));
            return "[" + string.Join(",", entries.ToArray()) + "]";
        }
        if (value is int || value is long || value is double || value is float)
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        throw new ArgumentException("Unsupported diagnostic JSON value: " + value.GetType().FullName);
    }
}
