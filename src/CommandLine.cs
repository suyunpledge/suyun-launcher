using System;
using System.Collections.Generic;
using System.Text;

namespace SuyunCraft
{
    static class CommandLine
    {
        // Windows CRT quoting: backslashes before a quote and before the closing quote must double.
        public static string Quote(string value)
        {
            if (value == null) value = "";
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') result.Append('\\', slashes * 2 + 1).Append(ch);
                else result.Append('\\', slashes).Append(ch);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        public static string Join(IEnumerable<string> values)
        {
            var quoted = new List<string>();
            foreach (string value in values) quoted.Add(Quote(value));
            return string.Join(" ", quoted);
        }

        public static List<string> Split(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return result;
            int index = 0;
            while (index < value.Length)
            {
                while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
                if (index == value.Length) break;
                var item = new StringBuilder();
                bool quoted = false;
                while (index < value.Length)
                {
                    if (!quoted && char.IsWhiteSpace(value[index])) break;
                    int slashes = 0;
                    while (index < value.Length && value[index] == '\\') { slashes++; index++; }
                    if (index < value.Length && value[index] == '"')
                    {
                        item.Append('\\', slashes / 2);
                        if (slashes % 2 != 0) item.Append('"');
                        else quoted = !quoted;
                        index++;
                    }
                    else
                    {
                        item.Append('\\', slashes);
                        if (index == value.Length || (!quoted && char.IsWhiteSpace(value[index]))) break;
                        item.Append(value[index++]);
                    }
                }
                if (quoted) throw new ArgumentException("Unterminated quote in arguments");
                result.Add(item.ToString());
            }
            return result;
        }
    }
}
