using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExcelDataReader;

namespace W.Files
{
    public class ExcelDataReader : ITabbedFileReader
    {
        readonly string fileName;

        public ExcelDataReader(string ExcelFileName)
        {
            fileName = ExcelFileName;
        }

        public string Filename => fileName;

        public void Dispose() { }

        IExcelDataReader GetReader(string ext, Stream stream)
        {
            switch (ext)
            {
                case ".xls":
                    return ExcelReaderFactory.CreateBinaryReader(stream);
                case ".xlsm":
                case ".xlsx":
                    return ExcelReaderFactory.CreateOpenXmlReader(stream);
                default:
                    return ExcelReaderFactory.CreateCsvReader(stream);
            }
        }

        public IEnumerable<ITabReader> EnumerateTabs()
        {
            using (var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = GetReader(Path.GetExtension(fileName).ToLower(), stream))
            {
                do
                    yield return new Sheet(reader);
                while (reader.NextResult());
            }
        }

        class Sheet : ITabReader
        {
            readonly string sheetName;
            readonly IExcelDataReader reader;

            public Sheet(IExcelDataReader reader)
            {
                this.reader = reader;
                this.sheetName = reader.Name;
            }

            public string SheetName => sheetName;

            public IEnumerable<RowInfo> EnumerateRows()
            {
                int nRows = 0;
                int nCols = reader.FieldCount;
                var names = Enumerable.Range(1, nCols).Select(CellRange.ColumnIndexToName).ToArray();
                while (reader.Read())
                {
                    var cells = new CellInfo[nCols];
                    nRows++;
                    var sRow = nRows.ToString();
                    for (int i = 0; i < nCols; i++)
                    {
                        cells[i].name = names[i] + sRow;
                        cells[i].value = reader.GetValue(i);
                    }
                    yield return new RowInfo() { num = nRows, cells = cells };
                }
            }
        }
    }
}
