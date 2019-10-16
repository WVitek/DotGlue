using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using ICSharpCode.SharpZipLib.Zip;
using W.Files;

namespace W.Files
{
    /// <summary>
    /// Xlsx reading
    /// </summary>
    public static class Xlsx
    {
        public static readonly IFormatProvider fmt = System.Globalization.CultureInfo.InvariantCulture;

        public class Exception : System.Exception { public Exception(string msg) : base(msg) { } }

        public class StyleInfo
        {
            public bool hidden { get; protected set; }

            public static StyleInfo[] ReadStyles(IEnumerable<XmlItem> xml)
            {
                var styleSheet = xml.Where(x => x.Name == "styleSheet").First();
                var cellStyleXfs = styleSheet.subItems.Where(x => x.Name == "cellStyleXfs").First();
                var cellXfs = styleSheet.subItems.Where(x => x.Name == "cellXfs").First();
                var lst = new List<StyleInfo>();
                foreach (var xfs in cellXfs.subItems)
                {
                    bool hidden = false;
                    if (xfs.subItems != null)
                    {
                        var p = xfs.subItems.FirstOrDefault(x => x.Name == "protection");
                        if (!p.IsEmpty && p.Attrs != null)
                            hidden = p.Attrs.FirstOrDefault(a => a.Name == "hidden").Value == "1";
                    }
                    lst.Add(new StyleInfo() { hidden = hidden });
                }
                return lst.ToArray();
            }

            public override string ToString()
            {
                return string.Format("hidden={0}", hidden ? 1 : 0);
            }

            public static readonly StyleInfo Empty = new StyleInfo();
        }

        public static string ChangedCellName(string cellName, int rowdelta)
        {
            if (rowdelta == 0)
                return cellName;
            int i = 0, row, col;
            if (!CellRange.ParseCellName(cellName, ref i, cellName.Length, out row, out col))
                throw new Exception("Can't parse cell name: " + cellName);
            return CellRange.RowAndColToCellName(row + rowdelta, col);
        }

        public static List<string> ReadStringTable(Stream input)
        {
            var stringTable = new List<string>();
            var sb = new StringBuilder();
            bool hasText = false;
            using (var reader = XmlReader.Create(input))
                for (reader.MoveToContent(); reader.Read();)
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.LocalName == "t")
                        {
                            sb.Append(reader.ReadElementString());
                            hasText = true;
                        }
                        else if (reader.LocalName == "si")
                            if (hasText)
                            {
                                stringTable.Add(sb.ToString());
                                sb.Clear();
                                hasText = false;
                            }
                            else { }
                    }
            if (sb.Length > 0)
                stringTable.Add(sb.ToString());
            return stringTable;
        }

        public class Reader : ITabbedFileReader
        {
            public readonly IReadFiles xmlFiles;
            public readonly StyleInfo[] styles;
            public readonly string[] stringTable;
            public readonly string[] sheetNames;
            public readonly string[] sheetFiles;

            readonly string filename;
            public string Filename => filename;

            readonly IDisposable resource;

            public Reader(string xlsxFileName)
            {
                filename = xlsxFileName;

                var zipFiles = new FilesFromZip(new ZipFile(new FileStream(xlsxFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)));
                xmlFiles = zipFiles;
                resource = zipFiles;

                const string xlDir = "xl";
                const string sCalcChainFile = "calcChain.xml";
                if (xmlFiles.FileExists(Path.Combine(xlDir, sCalcChainFile)))
                {
                    xmlFiles.RewriteFile(Path.Combine(xlDir, sCalcChainFile), null); // "delete" file
                    XmlItem rel;
                    var sRelsFile = Path.Combine(xlDir, "_rels", "workbook.xml.rels");
                    long nRelsFileSize;
                    using (var stream = xmlFiles.OpenReadFile(sRelsFile))
                    {
                        nRelsFileSize = stream.Length;
                        using (var reader = XmlReader.Create(stream))
                            rel = XmlItem.Read(reader).First();
                    }
                    using (var stream = new MemoryStream((int)nRelsFileSize))
                    {
                        using (var writer = new XmlTextWriter(stream, Encoding.UTF8))
                        {
                            writer.WriteStartDocument();
                            rel.WriteStartElementAndAttrs(writer);
                            foreach (var r in rel.subItems)
                                if (!r.Attrs.Any(xa => xa.Name == "Target" && xa.Value == sCalcChainFile))
                                    r.WritePart(writer);
                            writer.WriteEndElement();
                            writer.WriteEndDocument();
                        }
                        xmlFiles.RewriteFile(sRelsFile, stream.GetBuffer());
                    }
                }

                using (var stream = xmlFiles.OpenReadFile(Path.Combine(xlDir, "styles.xml")))
                using (var reader = XmlReader.Create(stream))
                    styles = StyleInfo.ReadStyles(XmlItem.Read(reader));

                using (var stream = xmlFiles.OpenReadFile(Path.Combine(xlDir, "sharedStrings.xml")))
                    stringTable = ReadStringTable(stream).ToArray();

                var sheetsDir = Path.Combine(xlDir, "worksheets");

                var lstSheetsNames = new List<string>(8);
                using (var stream = xmlFiles.OpenReadFile(Path.Combine(xlDir, "workbook.xml")))
                using (var reader = XmlReader.Create(stream))
                {
                    var wbook = XmlItem.Read(reader).ToArray();
                    var sheets = wbook.First(x => x.Name == "workbook").subItems.First(x => x.Name == "sheets").subItems.Where(x => x.Name == "sheet");
                    foreach (var name in sheets.Select(x => x.Attrs.First(a => a.Name == "name").Value))
                        lstSheetsNames.Add(name);
                }
                sheetNames = lstSheetsNames.ToArray();

                sheetFiles = xmlFiles.EnumerateFiles(sheetsDir)
                    // sort filenames like "xl/worksheets/sheet1.xml", ..., "xl/worksheets/sheet10.xml" in numerical order
                    .OrderBy(sf =>
                    {
                        var s = sf.Substring(19, sf.Length - 23);
                        return int.Parse(s);
                    }).ToArray();

            }

            public class SheetInfo : ITabReader
            {
                public Reader reader;
                public string sheetName;
                public string sheetFile;
                public IEnumerable<XmlItem> sheetCells;

                public string SheetName => sheetName;
                public IEnumerable<RowInfo> EnumerateRows() => reader.ReadDocumentRows(sheetCells);
            }

            public IEnumerable<SheetInfo> EnumSheetInfos()
            {
                for (int i = 0; i < sheetFiles.Length; i++)
                {
                    var sheetFile = sheetFiles[i];
                    using (var stream = xmlFiles.OpenReadFile(sheetFile))
                    using (var reader = XmlReader.Create(stream))
                    {
                        var sheetCells = XmlItem.Read(reader);
                        yield return new SheetInfo() { reader = this, sheetFile = sheetFile, sheetCells = sheetCells, sheetName = sheetNames[i] };
                    }
                }
            }

            public IEnumerable<ITabReader> EnumerateTabs() => EnumSheetInfos();

            CellInfo ReadCell(XmlItem c)
            {
                var ci = new CellInfo();
                foreach (var a in c.Attrs)
                    switch (a.Name)
                    {
                        case "r": ci.name = a.Value; break;
                        case "t": ci.type = a.Value; break;
                    }
                if (c.subItems != null)
                    foreach (var x in c.subItems)
                        if (x.Name == "f")
                        {   // formula
                        }
                        else if (x.Name == "v")
                        {
                            if (ci.type == "s")
                                ci.value = stringTable[int.Parse(x.Value)];
                            else
                                ci.value = x.Value;
                        }
                return ci;
            }

            public IEnumerable<RowInfo> ReadDocumentRows(IEnumerable<XmlItem> sheetXml)
            {
                var worksheet = sheetXml.Where(x => x.Name == "worksheet").First();
                var sheetData = worksheet.subItems.Where(x => x.Name == "sheetData").First();
                if (sheetData.subItems == null)
                    yield break;
                foreach (var r in sheetData.subItems)
                {
                    var sRow = r.Attrs.Where(a => a.Name == "r").First().Value;
                    IEnumerable<CellInfo> rowCells = (r.subItems == null) ? Enumerable.Empty<CellInfo>() : r.subItems.Select(ReadCell);
                    yield return new RowInfo() { num = int.Parse(sRow), cells = rowCells.ToArray() };
                }
            }

            public void Dispose()
            {
                if (resource != null)
                    resource.Dispose();
            }
        }

        public struct ValueToStrInfo
        {
            public string value;
            public string xlsxType;
        }

        public struct XmlItem : IEquatable<XmlItem>
        {
            public static readonly XmlItem Empty = new XmlItem();

            public struct Attr
            {
                public string Name, Value;
                public override string ToString() { return Name + "=\"" + Value + '"'; }
            }

            public string Name;
            public string Value;
            public Attr[] Attrs;
            public XmlItem[] subItems;
            public bool IsEmpty { get { return Name == null; } }

            public override string ToString()
            {
                var sAttrs = (Attrs != null) ? ' ' + string.Join(" ", Attrs.Select(a => a.ToString())) : null;
                var sSubs = (subItems != null) ? "[" + subItems.Length.ToString() + ']' : null;
                if (Value != null || sSubs != null)
                    return string.Format("<{0}{1}>{2}{3}</{0}>", Name, sAttrs, Value, sSubs);
                else
                    return string.Format("<{0}{1}/>", Name, sAttrs);
            }

            public bool Equals(XmlItem b) { return Name == b.Name && Value == b.Value && Attrs == b.Attrs && subItems == b.subItems; }

            public void Traverse(Action<XmlItem> visitor)
            {
                if (subItems == null)
                    return;
                foreach (var x in subItems)
                    visitor(x);
            }

            public static IEnumerable<XmlItem> Read(XmlReader rdr)
            {
                int curDepth = rdr.Depth;
                var x = default(XmlItem);
                while (true)
                {
                    var d = rdr.Depth;
                    if (d < curDepth)
                        break;
                    if (d > curDepth)
                    {
                        if (rdr.NodeType == XmlNodeType.Text)
                            x.Value = rdr.Value;
                        else { }
                        // read "children"
                        if (x.IsEmpty)
                            throw new Exception("XmlItem.Read: x.IsEmpty");
                        var subItems = Read(rdr).ToArray();
                        if (subItems.Length > 0)
                            x.subItems = subItems;
                        if (rdr.Depth < curDepth)
                            break;
                    }
                    else if (rdr.NodeType == XmlNodeType.Element)
                    {
                        if (!x.IsEmpty)
                            yield return x;
                        x = new XmlItem() { Name = rdr.Name };
                        if (rdr.MoveToFirstAttribute())
                        {
                            var attrs = new List<Attr>();
                            do { attrs.Add(new Attr() { Name = rdr.Name, Value = rdr.Value }); }
                            while (rdr.MoveToNextAttribute());
                            if (attrs.Count > 0)
                                x.Attrs = attrs.ToArray();
                        }
                    }
                    else { }
                    if (!rdr.Read())
                        break;
                }
                if (!x.IsEmpty)
                    yield return x;
            }

            public void WriteStartElementAndAttrs(XmlWriter wr)
            {
                wr.WriteStartElement(Name);
                if (Attrs != null)
                    foreach (var a in Attrs)
                        wr.WriteAttributeString(a.Name, a.Value);
                if (Value != null)
                    wr.WriteValue(Value);
            }

            public void WritePart(XmlWriter wr)
            {
                WriteStartElementAndAttrs(wr);
                if (subItems != null)
                    foreach (var s in subItems)
                        s.WritePart(wr);
                wr.WriteEndElement();
            }

            public class RowWriterContext
            {
                public readonly XmlWriter wr;
                public readonly IDictionary<string, object> values;
                public readonly IDictionary<string, int> sharedIndex;
                public readonly Func<object, int, string> ChangeFormula;
                public readonly Func<object, ValueToStrInfo> ValueToStr;

                public RowWriterContext(XmlWriter wr, IDictionary<string, object> values,
                    IDictionary<string, int> sharedIndex, Func<object, int, string> ChangeFormula,
                    Func<object, ValueToStrInfo> ValueToStr)
                {
                    this.wr = wr;
                    this.values = values;
                    this.sharedIndex = sharedIndex;
                    this.ChangeFormula = ChangeFormula;
                    this.ValueToStr = ValueToStr;
                }
            }

            void WriteCell(RowWriterContext c, int rowdelta)
            {
                var wr = c.wr;
                wr.WriteStartElement(Name);
                string t = null, v = null, f = null, fmla = null;
                int si = -1;
                foreach (var a in Attrs)
                {
                    if (a.Name == "r")
                    {
                        object value;
                        if (c.values.TryGetValue(a.Value, out value))
                        {
                            if (value == null)
                                value = DBNull.Value;
                            var nfo = c.ValueToStr(value);
                            v = nfo.value;
                            t = nfo.xlsxType;
                            if (t == "fmla")
                            {
                                t = null;
                                fmla = value.ToString();
                                if (!c.sharedIndex.TryGetValue(fmla, out si))
                                {
                                    si = -1;
                                    f = c.ChangeFormula(value, rowdelta);
                                    v = null;
                                }
                                else f = string.Empty;
                            }
                        }
                        wr.WriteAttributeString(a.Name, Xlsx.ChangedCellName(a.Value, rowdelta));
                    }
                    else if (a.Name == "t")
                    {
                        if (t != null)
                        {
                            if (t != string.Empty && v != string.Empty)
                                wr.WriteAttributeString(a.Name, t);
                        }
                        else if (t == null && f == null)
                            wr.WriteAttributeString(a.Name, a.Value);
                    }
                    else wr.WriteAttributeString(a.Name, a.Value);
                }
                if (v != null)
                {   // write value
                    if (v != string.Empty)
                    { wr.WriteStartElement("v"); wr.WriteValue(v); wr.WriteEndElement(); }
                }
                else if (f != null)
                {   // write formula
                    //var xf = (subItems == null) ? XmlItem.Empty : subItems.FirstOrDefault(x => x.Name == "f");
                    wr.WriteStartElement("f");
                    //if (!xf.IsEmpty)
                    //{
                    //	wr.WriteStartElement("f");
                    //	if (xf.Attrs != null)
                    //	{
                    //		foreach (var a in xf.Attrs)
                    //			switch (a.Name)
                    //			{
                    //				case "ca":
                    //					wr.WriteAttributeString(a.Name, a.Value);
                    //					break;
                    //				case "shared":
                    //				case "si": // need for t="shared"
                    //				case "ref": // need for t="array" or t="shared"
                    //				case "t": //
                    //				default:
                    //					break;
                    //			}
                    //	}
                    //}
                    wr.WriteValue(f);
                    wr.WriteEndElement();
                }
                else if (subItems != null)
                    foreach (var item in subItems) item.WritePart(wr);
                wr.WriteEndElement();
            }

            public void WriteRow(XmlWriter wr
                , IDictionary<string, object> values
                , int dstRowNumber
                , IDictionary<string, int> sharedIndex
                , Func<object, int, string> ChangeFormula
                , Func<object, ValueToStrInfo> ValueToStr)
            {
                wr.WriteStartElement(Name);
                int srcnum = 0;
                foreach (var a in Attrs)
                {
                    if (a.Name == "r")
                    {
                        srcnum = Convert.ToInt32(a.Value);
                        wr.WriteAttributeString(a.Name, dstRowNumber.ToString());
                    }
                    else wr.WriteAttributeString(a.Name, a.Value);
                }
                int delta = dstRowNumber - srcnum;
                var c = new RowWriterContext(wr, values, sharedIndex, ChangeFormula, ValueToStr);
                if (subItems != null)
                    foreach (var s in subItems)
                        s.WriteCell(c, delta);
                wr.WriteEndElement();
            }
        }


    }
}
