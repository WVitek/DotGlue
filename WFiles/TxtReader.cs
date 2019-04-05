using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace W.Files
{
    public class TxtReader : ITabbedFileReader, ITabReader
    {
        string filename;
        TextReader text;

        public TxtReader(TextReader text, string filename)
        {
            this.text = text;
            this.filename = filename;
        }

        public string Filename => filename;

        public IEnumerable<ITabReader> EnumerateTabs()
        {
            yield return this;
        }

        string ITabReader.SheetName => filename;

        IEnumerable<RowInfo> ITabReader.EnumerateRows()
        {
            int num = 1;
            while (true)
            {
                var line = text.ReadLine();
                if (line == null)
                    break;
                yield return new RowInfo() { num = num, cells = new[] { new CellInfo() { name = $"A{num}", value = line } } };
                num++;
            }
        }

        public void Dispose()
        {
            text.Close();
        }
    }
}
