using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using W.Common;
using W.Expressions;

namespace W.Files
{
    public static class Source
    {
        public class Exception : System.Exception { public Exception(string msg) : base(msg) { } }

        const string sTemplateParameterValuesPrefix = "TemplateParameterValues:";

        const string sMetadataFile = "MetadataFile";

        const string sSourceName = "SourceName";
        const string sSourceKey = "SourceKey";
        const string sCondition = "Condition";
        const string sFirstDataRowNum = "FirstDataRowNum";

        const string sFileContentKind = "FileContentKind";
        const string sFileNameExtension = "FileNameExtension";

        public const string sDataKey = "DataKey";
        public const string sDataSubkey = "DataSubkey";

        static readonly ReferenceExpr HdrCellsRef = new ReferenceExpr(":HDR");
        static readonly ReferenceExpr CellValsRef = new ReferenceExpr("$CELL");
        static readonly ReferenceExpr PrevValsRef = new ReferenceExpr("PREV");

        public const int iKey = 0;
        public const int iSubkey = 1;
        public const int iRowVals = 2;

        public class Metadata
        {
            const string sHdr = "Hdr";
            const string sRow = "Row";
            const string sRes = "Res";

            int iHdrMaxUsedRow = 1, iHdrMinUsedRow = int.MaxValue;

            public int MaxUsedHeaderRows => iHdrMaxUsedRow;

            public readonly Generator.Ctx ctxHdr;
            public readonly Generator.Ctx ctxRow;
            public readonly Generator.Ctx ctxRes;

            public readonly string[] FileNameExtension;
            public readonly string FileContentKind;


            static bool IsFullRelativeCell(CellRange rng) => rng != null && rng.row > 0 && rng.col > 0;

            bool ConsiderCellRef(CellRange rng)
            {
                if (IsFullRelativeCell(rng))
                {
                    if (iHdrMaxUsedRow < rng.row2)
                        iHdrMaxUsedRow = rng.row2;
                    else if (iHdrMaxUsedRow < rng.row)
                        iHdrMaxUsedRow = rng.row;
                    if (iHdrMinUsedRow > rng.row)
                        iHdrMinUsedRow = rng.row;
                    return true;
                }
                return false;
            }

            Expr FixHdrCellRef(ReferenceExpr re)
            {
                var rng = CellRange.TryFromName(re.name);
                if (ConsiderCellRef(rng))
                    return new IndexExpr(HdrCellsRef, new ConstExpr(re.name));
                return re;
            }

            Func<Expr, Expr> FixSheetExpr = null;
            Func<Expr, Expr> FixRowExpr = null;

            Expr FixSheetImpl(Expr e)
            {
                switch (e.nodeType)
                {
                    case ExprType.Call:
                        var ce = (CallExpr)e;
                        if (ce.args.Count == 1)
                        {
                            if (ce.funcName == nameof(FuncDefs_Xlsx.COLUMN))
                                // avoid fixing arg of COLUMN function
                                return new CallExpr(nameof(FuncDefs_Xlsx.COLUMN), ce.args);
                            if (ce.funcName == "INDIRECT")
                                // just don't want to implement INDIRECT for header formulas :)
                                return FixRowImpl(e);
                        }
                        return e;
                    case ExprType.Reference:
                        break;
                    default:
                        return e;
                }
                return FixHdrCellRef((ReferenceExpr)e);
            }

            static bool IsColumnName(string s) => s.Length <= 3 && s.All(c => 'A' <= c && c <= 'Z');

            Expr SpecFuncs(CallExpr ce)
            {
                Expr src, ndx;
                bool forPrev = false, forCell = false;
                int nMinArgs = 1;
                switch (ce.funcName)
                {
                    case "PREVCELL":
                        forCell = true; forPrev = true; break;
                    case "PREV":
                        nMinArgs = 0; forPrev = true; break;
                    case "INDIRECT":
                        forCell = true; break;
                    case nameof(FuncDefs_Xlsx.COLUMN):
                        // avoid COLUMN(args) fixing
                        return new CallExpr(nameof(FuncDefs_Xlsx.COLUMN), ce.args);
                    default:
                        return ce;
                }
                if (ce.args.Count < nMinArgs)
                    return ce;

                Expr arg;
                string name;
                if (ce.args.Count == 0)
                {
                    arg = null;
                    name = FCurrName;
                }
                else
                {
                    arg = ce.args[0];
                    if (arg.nodeType == ExprType.Reference)
                    {
                        name = ((ReferenceExpr)arg).name;
                        if (IsColumnName(name) || !forPrev && IsFullRelativeCell(CellRange.FromName(name)))
                            forCell = true;
                        else
                            name = OPs.TryAsString(arg, ctxRow) ?? name;
                    }
                    else
                    {
                        arg = FixRowExpr(arg);
                        name = OPs.TryAsName(arg, ctxRow);
                    }
                }

                if (name == null)
                {
                    var undefs = string.Join(", ", ctxHdr.NamesOfUndefinedValues().Concat(ctxRow.NamesOfUndefinedValues()));
                    throw new Source.Exception($"Constant expected as value of {ce.args[0]}={arg} // ??? {undefs}");
                }

                if (forCell || IsColumnName(name) || !forPrev && IsFullRelativeCell(CellRange.FromName(name)))
                {
                    if (forPrev)
                        src = new IndexExpr(PrevValsRef, new ConstExpr(ctxRow.IndexOf(CellValsRef.name)));
                    else
                    {
                        var re1 = new ReferenceExpr(name);
                        var re2 = FixHdrCellRef(re1);
                        if (re2 != re1)
                            // ref to header cell
                            return re2;
                        else
                            // ref to row cell
                            src = CellValsRef;
                    }
                    ndx = new ConstExpr(name);
                }
                else
                {
                    int i = ctxRow.IndexOf(name);
                    if (i < 0 || Generator.Ctx.ctxDepthStep <= i)
                        throw new Source.Exception($"Value named '{name}' not found in row context");
                    src = PrevValsRef;
                    ndx = new ConstExpr(i);
                }
                return new IndexExpr(src, ndx);

            }

            Expr FixRowImpl(Expr e)
            {
                switch (e.nodeType)
                {
                    case ExprType.Reference:
                        var re = (ReferenceExpr)e;
                        if (IsColumnName(re.name))
                            return new IndexExpr(CellValsRef, new ConstExpr(re.name));
                        return FixHdrCellRef(re);
                    case ExprType.Call:
                        var ce = (CallExpr)e;
                        return SpecFuncs(ce);
                }
                return e;
            }

            static void Required(Generator.Ctx ctx, string section, string key)
            {
                if (!ctx.name2ndx.TryGetValue(key, out int i) || ctx.values[i] == Generator.Undefined)
                    throw new Exception($"Value named '{key}' required in section '{section}'");
            }

            void RequiredInHdr(string key) => Required(ctxHdr, sHdr, key);
            void RequiredInRow(string key) => Required(ctxRow, sRow, key);

            struct NE
            {
                public string name;
                public Expr expr;
                public NE(string n, Expr e) { name = n; expr = e; }
            }

            public static IEnumerable<Metadata> Create(TextReader txt, Generator.Ctx ctxParent)
            {
                var exprsHdr = new List<NE>();
                var exprsRow = new List<NE>();
                List<NE> exprsRes = null;
                string tmplParamName = null;

                bool withFileContentKind = false;
                bool withFileNameExtension = false;

                foreach (var (Section, Key, Value) in Ini.Read(txt, true))
                {
                    var expr = Parser.ParseToExpr(Value);
                    List<NE> exprsLst = null;
                    switch (Section)
                    {
                        case sHdr:
                            if (Key.StartsWith(sTemplateParameterValuesPrefix))
                            {
                                if (tmplParamName != null)
                                    throw new Exception($"Template parameter already defined as '{tmplParamName}'");
                                tmplParamName = Key;
                            }
                            else
                                switch (Key)
                                {
                                    case sFileContentKind: withFileContentKind = true; break;
                                    case sFileNameExtension: withFileNameExtension = true; break;
                                }
                            exprsLst = exprsHdr;
                            break;
                        case sRow:
                            exprsLst = exprsRow; break;
                        case sRes:
                            if (exprsRes == null)
                                exprsRes = new List<NE>();
                            exprsLst = exprsRes;
                            break;
                        default:
                            throw new Exception($"Unknown section '{Section}' in source metadata. Expected '{sHdr}' or '{sRow}' sections.");
                    }
                    exprsLst.Add(new NE(Key ?? $".{exprsLst.Count}", expr));
                }

                if (withFileContentKind && !withFileNameExtension)
                    throw new Exception($"{ctxParent.GetConstant(sMetadataFile)} : specified FileContentKind also requires FileNameExtension specification");


                if (tmplParamName != null)
                {
                    var ctx = new Generator.Ctx(ctxParent);
                    object tmplVals = null;
                    Expr tmplExpr = null;
                    for (int i = 0; i < exprsHdr.Count; i++)
                    {
                        var p = exprsHdr[i];
                        var val = Generator.Generate(p.expr, ctx);
                        if (p.name == tmplParamName)
                        {
                            tmplVals = val;
                            exprsHdr.RemoveAt(i);
                            break;
                        }
                        ctx.CreateValue(p.name, val);
                    }
                    var tmplParamVals = tmplVals as IList;
                    if (OPs.KindOf(tmplVals) != ValueKind.Const || tmplParamVals == null)
                        throw new Exception($"Can't interpret template parameter as list of constants //{tmplParamName}={tmplExpr}={tmplVals}");
                    tmplParamName = tmplParamName.Substring(sTemplateParameterValuesPrefix.Length);
                    foreach (var tmplVal in tmplParamVals)
                    {
                        exprsHdr.Insert(0, new NE(tmplParamName, new ConstExpr(tmplVal)));
                        yield return new Metadata(exprsHdr, exprsRow, exprsRes, ctxParent);
                        exprsHdr.RemoveAt(0);
                    }
                }
                else yield return new Metadata(exprsHdr, exprsRow, exprsRes, ctxParent);
            }

            string FCodeText;
            string FCurrName;

            private Metadata(IEnumerable<NE> exprsHdr, IEnumerable<NE> exprsRow, IEnumerable<NE> exprsRes, Generator.Ctx parentCtx)
            {
                var sb = new StringBuilder();

                FixSheetExpr = Expr.RecursiveModifier(FixSheetImpl);
                FixRowExpr = Expr.RecursiveModifier(FixRowImpl);

                #region generate header code
                ctxHdr = new Generator.Ctx(parentCtx);
                ctxHdr.CreateValue(HdrCellsRef.name, Generator.LazyDummy);
                ctxHdr.CreateValue(PrevValsRef.name, Generator.LazyDummy);

                foreach (var p in exprsHdr)
                {
                    FCurrName = p.name;
                    var expr = FixSheetExpr(p.expr);
                    var val = Generator.Generate(expr, ctxHdr);
                    ctxHdr.CreateValue(p.name, val);
                    if (OPs.KindOf(val) == ValueKind.Const)
                        expr = new ConstExpr(val);
                    sb.AppendLine($"{p.name} = {expr}");
                }
                sb.AppendLine();

                RequiredInHdr(sSourceKey);
                RequiredInHdr(sSourceName);
                RequiredInHdr(sCondition);
                RequiredInHdr(sFirstDataRowNum);

                {
                    if (ctxHdr.name2ndx.TryGetValue(sFileContentKind, out int i))
                        FileContentKind = Convert.ToString(ctxHdr.GetConstant(sFileContentKind));
                    else
                        FileContentKind = "XLSX";
                }
                {
                    if (ctxHdr.name2ndx.TryGetValue(sFileNameExtension, out int i))
                    {
                        var val = ctxHdr.GetConstant(sFileNameExtension);
                        FileNameExtension = Utils.AsIList(val).Cast<object>().Select(Convert.ToString).ToArray();
                    }
                    else FileNameExtension = new string[] { "xls", "xlsx", "xlsm" };
                }
                #endregion

                #region generate row code
                ctxRow = new Generator.Ctx(ctxHdr);
                ctxRow.CreateValue(sDataKey, Generator.LazyDummy);    // [0] - iKey
                ctxRow.CreateValue(sDataSubkey, Generator.LazyDummy); // [1] - iSubkey
                ctxRow.CreateValue(CellValsRef.name, Generator.LazyDummy); // [2] - iRowVals

                foreach (var p in exprsRow)
                {
                    string name;
                    if (p.name.StartsWith("~"))
                        // calculated name
                        name = OPs.TryAsString(Parser.ParseToExpr(p.name.Substring(1)), ctxHdr) ?? p.name;
                    else
                        name = p.name;
                    FCurrName = name;
                    int i = ctxRow.GetOrCreateIndexOf(name);
                    var expr = FixRowExpr(p.expr);
                    var val = Generator.Generate(expr, ctxRow);
                    ctxRow.values[i] = val;
                    if (OPs.KindOf(val) == ValueKind.Const)
                        expr = new ConstExpr(val);
                    sb.AppendLine($"{name} = {expr}");
                }

                RequiredInRow(sDataKey);
                RequiredInRow(sDataSubkey);

                ctxRow.values[iRowVals] = null;
                ctxRow.CheckUndefinedValues();

                #endregion

                #region generate res code
                if (exprsRes != null)
                {
                    // copy all code from row to res
                    ctxRes = new Generator.Ctx(ctxHdr);
                    foreach (var p in ctxRow.name2ndx.OrderBy(p => p.Value))
                        ctxRes.values[ctxRes.CreateValue(p.Key)] = ctxRow.values[p.Value];

                    foreach (var p in exprsRes)
                    {
                        string name;
                        if (p.name.StartsWith("~"))
                            // calculated name
                            name = OPs.TryAsString(Parser.ParseToExpr(p.name.Substring(1)), ctxHdr) ?? p.name;
                        else
                            name = p.name;
                        FCurrName = name;
                        int i = ctxRes.GetOrCreateIndexOf(name);
                        var expr = FixRowExpr(p.expr);
                        var val = Generator.Generate(expr, ctxRes);
                        ctxRes.values[i] = val;
                        if (OPs.KindOf(val) == ValueKind.Const)
                            expr = new ConstExpr(val);
                    }
                    ctxRes.values[iRowVals] = null;
                    ctxRes.CheckUndefinedValues();
                }
                #endregion

                ctxHdr[HdrCellsRef.name] = null;
                ctxHdr[PrevValsRef.name] = null;
                ctxHdr.CheckUndefinedValues();

                FCodeText = sb.ToString();

                if (iHdrMinUsedRow > iHdrMaxUsedRow)
                    iHdrMinUsedRow = iHdrMaxUsedRow;
            }

            public static string ErrorMsg(string msg, string srcName)
            { return string.Format(msg + " //for source named'{0}'", srcName); }

            public override string ToString() => FCodeText;

            public SheetCtx TryGetSheetCtx(AsyncExprCtx parent, Dictionary<string, object> headerValues, int nHeaderRows)
            {
                if (nHeaderRows == iHdrMaxUsedRow)
                    return SheetCtx.New(this, headerValues, parent);
                if (nHeaderRows > iHdrMaxUsedRow)
                    return SheetCtx.Empty;
                else
                    return null;
            }

            public static IEnumerable<Metadata> Load(IReadFiles storage, IEnumerable<string> files, Generator.Ctx parent)
            {
                foreach (var fname in files)
                    using (var stream = storage.OpenReadFile(fname))
                    using (var reader = new StreamReader(stream))
                    {
                        var ctx = new Generator.Ctx(parent);
                        ctx.CreateValue(sMetadataFile, fname);
                        foreach (var m in Metadata.Create(reader, ctx))
                            yield return m;
                    }
            }

        }

        public class SheetCtx : AsyncExprCtx
        {
            public static readonly SheetCtx Empty = new SheetCtx();

            public readonly Metadata meta;
            public readonly string SourceKey;

            public string SourceName => Convert.ToString(GetValue(sSourceName).Result);
            public bool Condition => Convert.ToBoolean(GetValue(sCondition).Result);

            int firstDataRowNum = -1;
            public int FirstDataRowNum
            {
                get
                {
                    if (firstDataRowNum < 0)
                        firstDataRowNum = Convert.ToInt32(GetValue(sFirstDataRowNum).Result);
                    return firstDataRowNum;
                }
            }

            SheetCtx() { }

            SheetCtx(Metadata meta, IList values, AsyncExprCtx parent) : base(meta.ctxHdr, values, parent)
            {
                this.meta = meta;
                SourceKey = Convert.ToString(GetValue(sSourceKey).Result);
            }

            public override string ToString() => SourceKey;

            public static SheetCtx New(Metadata meta, Dictionary<string, object> headerValues, AsyncExprCtx parent)
            {
                var sc = meta.ctxHdr;
                var values = new object[sc.values.Count];
                sc.values.CopyTo(values, 0);
                var k2n = sc.name2ndx;
                values[k2n[HdrCellsRef.name]] = headerValues;
                {
                    var prevRowVals = new OPs.ListOfConst();
                    for (int i = meta.ctxRow.values.Count; i > 0; i--)
                        prevRowVals.Add(null);
                    // imitate empty zero row
                    prevRowVals[iRowVals] = new Dictionary<string, object>();
                    values[k2n[PrevValsRef.name]] = prevRowVals;
                }
                // template metadata info context
                var aec = new AsyncExprCtx(sc.parent, sc.parent.values, parent);
                //
                var ctx = new SheetCtx(meta, values, aec);
                return ctx.Condition ? ctx : null;
            }


            public async Task<IReadOnlyDictionary<string, object>> NextRowData(Dictionary<string, object> rowValues, bool forRes = false)
            {
                var rc = forRes ? meta.ctxRes : meta.ctxRow;
                var values = new object[rc.values.Count];
                rc.values.CopyTo(values, 0);
                var k2n = rc.name2ndx;
                values[k2n[CellValsRef.name]] = rowValues;
                var rowCtx = new AsyncExprCtx(rc, values, this);
                var prevVals = (OPs.ListOfConst)await this.GetValue(PrevValsRef.name);

                int nValsInCtx = values.Length;

                values[iKey] = await rowCtx.GetValue(iKey);
                values[iSubkey] = await rowCtx.GetValue(iSubkey);
                values[iRowVals] = rowValues;

                for (int i = iRowVals + 1; i < values.Length; i++)
                    values[i] = await rowCtx.GetValue(i);

                for (int i = 0; i < values.Length; i++)
                    prevVals[i] = values[i];

                if (values[iKey] == null)
                    // skip row if error or empty string datakey
                    return null;

                return ValuesDictionary.New(values, k2n);
            }

        }

        public class ProcessingContext
        {
            const string sFileDir = "FileDir";
            const string sFileName = "FileName";
            const string sSheetName = "SheetName";

            readonly Generator.Ctx ctxFile;
            public readonly Metadata[] metas;

            Dictionary<string, string> dictExt2Kind;

            public ProcessingContext(Generator.Ctx parent, IReadFiles metaStorage)
            {
                ctxFile = new Generator.Ctx(parent);
                ctxFile.CreateValue(sFileDir);
                ctxFile.CreateValue(sFileName);
                ctxFile.CreateValue(sSheetName);

                var metaFiles = metaStorage.EnumerateFiles("").Where(fn => fn.EndsWith(".src.ini"));
                metas = Metadata.Load(metaStorage, metaFiles, ctxFile).ToArray();

                dictExt2Kind = new Dictionary<string, string>();
                foreach (var m in metas)
                    foreach (var Ext in m.FileNameExtension)
                    {
                        var ext = '.' + Ext.ToLowerInvariant();
                        if (!dictExt2Kind.TryGetValue(ext, out var kind))
                            dictExt2Kind[ext] = m.FileContentKind;
                        else if (kind != m.FileContentKind)
                            throw new Exception($"Can't associate extension '{ext}' with two kinds of content ('{kind}' and '{m.FileContentKind}')");
                    }
            }

            public IEnumerable<string> AcceptedExtensions => dictExt2Kind.Keys;
            public bool IsAcceptedExtension(string ext) => dictExt2Kind.ContainsKey(ext);

            public IEnumerable<(string SourceKey, IReadOnlyDictionary<string, object> SourceRow)>
                LoadSourceData(string fileName, AsyncExprCtx aecParent, Action<string> msgReceiver = null)
            {
                ITabbedFileReader rdr;
                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                if (!dictExt2Kind.TryGetValue(ext, out var kind))
                    throw new Exception($"{nameof(LoadSourceData)}: Filename extension '{ext}' is not suitable for any source template // {fileName}");
                switch (kind)
                {
                    //case "XLSX":
                    //    rdr = new Xlsx.Reader(fileName);
                    //    break;
                    case "XLSX":
                    case "XLS":
                        rdr = new ExcelDataReader(fileName);
                        break;
                    case "TEXT":
                        rdr = new TxtReader(new StreamReader(fileName), fileName);
                        break;
                    default:
                        throw new Exception($"{nameof(LoadSourceData)}: No reader implemented for content kind '{kind}'");
                }

                using (rdr)
                {
                    var values = new object[ctxFile.values.Count];
                    ctxFile.values.CopyTo(values, 0);
                    values[ctxFile.name2ndx[sFileDir]] = Path.GetDirectoryName(fileName);
                    values[ctxFile.name2ndx[sFileName]] = Path.GetFileName(fileName);

                    int MaxUsedHeaderRows = metas.Max(m => m.MaxUsedHeaderRows);

                    foreach (var si in rdr.EnumerateTabs())
                    {
                        var headingValues = new Dictionary<string, object>();
                        values[ctxFile.name2ndx[sSheetName]] = si.SheetName;

                        var aecSheet = new AsyncExprCtx(ctxFile, values, aecParent);

                        List<SheetCtx> infos = null;
                        int prevRowNum = 0;

                        foreach (var row in si.EnumerateRows())
                        {
                            int iRowNum = row.num;
                            if (infos == null)
                            {
                                if (iRowNum > MaxUsedHeaderRows)
                                    break;
                                foreach (var c in row.cells)
                                    headingValues.Add(c.name, c.value);
                                infos = metas
                                    .Select(m => m.TryGetSheetCtx(aecSheet, headingValues, iRowNum))
                                    .ToList();
                                bool withNonEmpty = false;
                                bool withEmpty = false;
                                for (int i = infos.Count - 1; i >= 0; i--)
                                {
                                    var sc = infos[i];
                                    if (sc == SheetCtx.Empty)
                                    { withEmpty = true; infos.RemoveAt(i); }
                                    else if (sc == null)
                                    { withNonEmpty = true; infos.RemoveAt(i); }
                                    else withNonEmpty = true;
                                }
                                if (withEmpty && !withNonEmpty)
                                    break;
                                if (infos.Count == 0)
                                    infos = null;
                                if (infos == null)
                                {   // appropriate metadata not found
                                    prevRowNum = iRowNum;
                                    continue;
                                }
                                else
                                    msgReceiver?.Invoke(string.Join(", ", infos.Select(i => i.SourceName)));
                            }

                            var dictRowValues = new Dictionary<string, object>(row.cells.Length);

                            // process missed empty rows if needed
                            while (++prevRowNum < iRowNum)
                                foreach (var info in infos)
                                {
                                    if (prevRowNum < info.FirstDataRowNum)
                                        continue;
                                    var data = info.NextRowData(dictRowValues).Result;
                                    if (data == null)
                                        continue;
                                    yield return (info.SourceKey, data);
                                }

                            // fill in dictionary with row data
                            foreach (var c in row.cells)
                            {
                                int i = 0;
                                var s = c.name;
                                for (; i < s.Length && char.IsLetter(s[i]); i++) ;
                                var colName = s.Substring(0, i);
                                dictRowValues.Add(colName, c.value);
                            }

                            // process row
                            foreach (var info in infos)
                            {
                                if (iRowNum < info.FirstDataRowNum)
                                    continue;
                                var data = info.NextRowData(dictRowValues).Result;
                                if (data == null)
                                    continue;
                                yield return (info.SourceKey, data);
                            }
                        }

                        // process res, if needed
                        if (infos != null)
                        {
                            var dictRowValues = new Dictionary<string, object>(0);
                            foreach (var info in infos)
                            {
                                if (info.meta.ctxRes == null)
                                    continue;
                                var data = info.NextRowData(dictRowValues, true).Result;
                                if (data == null)
                                    continue;
                                yield return (info.SourceKey, data);
                            }
                        }
                    }
                }
            }
        }

    }
}
