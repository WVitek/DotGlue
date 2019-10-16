using System.Collections.Generic;
using System.IO;
using System.Text;

namespace W.Files
{
    public static class Ini
    {
        public static IEnumerable<(string Section, string Key, string Value)> Read(TextReader txt, bool allowEmptyKeys = false)
        {
            var lineBuf = new StringBuilder();
            var firstLine = string.Empty;
            var section = string.Empty;
            while (true)
            {
                var line = txt.ReadLine();
                if (line == null)
                    break;
                if (line.Length == 0 || line[0] == ';')
                    continue;
                if (line[0] == '[' && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                int tabStarts = line.StartsWith("\t") ? 1 : 0;
                int slashEnds = line.EndsWith(@"\") ? 1 : 0;
                if (tabStarts + slashEnds > 0)
                {   // line starts with tab OR ends with slash
                    if (lineBuf.Length == 0)
                        firstLine = line;
                    if (line.Length > tabStarts + slashEnds)
                        lineBuf.AppendLine(line.Substring(tabStarts, line.Length - tabStarts - slashEnds));
                    continue;
                }
                else if (lineBuf.Length > 0)
                {
                    lineBuf.AppendLine(line);
                    line = lineBuf.ToString();
                    lineBuf.Clear();
                }
                else firstLine = line;
                int i = firstLine.IndexOf('=');
                if (i < 0 && !allowEmptyKeys)
                    throw new InvalidDataException($"Equal-sign expected, each line must be in form 'key=value' // {line}");
                var key = (i < 0) ? null : line.Substring(0, i).Trim();
                var value = (i < 0) ? line : line.Substring(i + 1).Trim();
                yield return (section, key, value);
            }
        }

    }
}
