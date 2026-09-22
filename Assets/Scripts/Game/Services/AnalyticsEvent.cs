using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Services.Analytics;

namespace TTTXO.Game.Services
{
    /// <summary>
    /// A recorded event before it reaches any sink - name plus ordered parameters. It exists so
    /// <see cref="GameAnalytics"/>'s typed methods can be written once and routed to either Unity
    /// Analytics (<see cref="ToCustomEvent"/>) or the local beta sink (<see cref="ToJsonLine"/>)
    /// without touching a single call site: they still call <c>evt.Add("board_size", size)</c>
    /// exactly as they did when the lambda received a <see cref="CustomEvent"/> directly.
    ///
    /// Deliberately NOT behind the <c>TTTXO_LOCAL_ANALYTICS</c> define, unlike
    /// <c>LocalAnalyticsSink</c>. The file I/O is what must be strippable from a production build;
    /// the serialization below is the part that is easy to get subtly wrong (escaping, culture,
    /// timestamp format), so it stays compiled and covered by tests at all times.
    /// </summary>
    public sealed class AnalyticsEvent
    {
        private readonly List<KeyValuePair<string, object>> parameters = new();

        public AnalyticsEvent(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public IReadOnlyList<KeyValuePair<string, object>> Parameters => parameters;

        public void Add(string key, string value) => parameters.Add(new KeyValuePair<string, object>(key, value));

        public void Add(string key, int value) => parameters.Add(new KeyValuePair<string, object>(key, value));

        public void Add(string key, bool value) => parameters.Add(new KeyValuePair<string, object>(key, value));

        public void Add(string key, float value) => parameters.Add(new KeyValuePair<string, object>(key, value));

        /// <summary>Materializes the Unity Analytics event. Only reached when the local sink is compiled out - see <see cref="GameAnalytics"/>.</summary>
        public CustomEvent ToCustomEvent()
        {
            var evt = new CustomEvent(Name);
            foreach (var pair in parameters)
            {
                switch (pair.Value)
                {
                    case string s:
                        evt.Add(pair.Key, s);
                        break;
                    case int i:
                        evt.Add(pair.Key, i);
                        break;
                    case bool b:
                        evt.Add(pair.Key, b);
                        break;
                    case float f:
                        evt.Add(pair.Key, f);
                        break;
                }
            }

            return evt;
        }

        /// <summary>
        /// One JSON Lines record: <c>{"ts":"...","event":"...","params":{...}}</c>, no trailing
        /// newline. JSON Lines rather than one big JSON array so an append never has to rewrite or
        /// re-close the file - a crash mid-beta costs at most the last line instead of the session.
        ///
        /// <paramref name="timestampUtc"/> is passed in rather than read from the clock so the
        /// output is testable.
        /// </summary>
        public string ToJsonLine(DateTime timestampUtc)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"ts\":\"")
              .Append(timestampUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
              .Append("\",\"event\":");
            AppendJsonString(sb, Name);
            sb.Append(",\"params\":{");

            for (int i = 0; i < parameters.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                AppendJsonString(sb, parameters[i].Key);
                sb.Append(':');
                AppendJsonValue(sb, parameters[i].Value);
            }

            return sb.Append("}}").ToString();
        }

        private static void AppendJsonValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case string s:
                    AppendJsonString(sb, s);
                    break;
                case int i:
                    // Invariant culture throughout: a locale using ',' as the decimal separator
                    // would otherwise emit numbers that are not valid JSON.
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append("null");
                    break;
            }
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        // Control characters are the only remaining thing JSON forbids raw. Everything
                        // above U+001F (including the accented and non-Latin glyphs the 10 localized
                        // languages can put into a player name) is emitted as-is, since the file is
                        // written as UTF-8.
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }
    }
}
