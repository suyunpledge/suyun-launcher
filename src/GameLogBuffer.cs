using System;
using System.Collections.Generic;
using System.Text;

namespace SuyunCraft
{
    // A noisy mod cannot grow the pending UI log without bound.
    sealed class GameLogBuffer
    {
        const int MaxCharacters = 1024 * 1024;
        const int MaxLines = 2000;
        readonly Queue<string> lines = new Queue<string>();
        readonly object gate = new object();
        int characters;
        long dropped;

        public void Append(string line)
        {
            if (line == null) return;
            if (line.Length > 8192) line = line.Substring(0, 8192) + " [Log line too long; truncated]";
            lock (gate)
            {
                while (lines.Count >= MaxLines || characters + line.Length > MaxCharacters)
                { characters -= lines.Dequeue().Length; dropped++; }
                lines.Enqueue(line); characters += line.Length;
            }
        }

        public string Drain(bool all)
        {
            lock (gate)
            {
                var result = new StringBuilder();
                if (dropped > 0)
                { result.Append("[Launch ] Log output too fast; suppressed ").Append(dropped).Append("  older lines.\r\n"); dropped = 0; }
                int count = 0;
                while (lines.Count > 0 && (all || (count < 250 && result.Length + lines.Peek().Length <= 65536)))
                {
                    string line = lines.Dequeue(); characters -= line.Length; count++;
                    result.Append(line).Append("\r\n");
                }
                return result.ToString();
            }
        }
    }
}
