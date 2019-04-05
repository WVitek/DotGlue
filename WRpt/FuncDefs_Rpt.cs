using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using W.Files;

namespace W.Rpt
{
    using W.Common;
    using W.Expressions;

    public static class Diagnostics
    {
        public static System.Diagnostics.TraceSource Logger = W.Common.Trace.GetLogger("WRpt");
    }

    public class WRptException : Exception
    {
        public WRptException(string msg) : base(msg) { }
        public WRptException(string msg, Exception inner) : base(msg, inner) { }
    }

    public static class FuncDefs_Rpt
    {
        public static async Task<object> WRptGetXmlWriter(AsyncExprCtx ae, IList args)
        {
            var outputFiles = (IWriteFiles)await OPs.ConstValueOf(ae, args[0]);
            var parts = new string[args.Count - 1];
            for (int i = args.Count - 1; i >= 1; i--)
                parts[i - 1] = Convert.ToString(await OPs.ConstValueOf(ae, args[i]));
            var stream = await outputFiles.CreateFile(Path.Combine(parts), ae.Cancellation);
            var writer = new XmlTextWriter(stream, Encoding.UTF8);
            return writer;
        }

        public static object WRptNewFilesInDir(object dirName)
        {
            return new FilesInDir(Convert.ToString(dirName));
        }

        [IsNotPure]
        public static object WRptNewZipWriter(object outputTarget, object bufferSize)
        {
            Stream targetStream;
            if (!Utils.TryCastClass(outputTarget, out targetStream))
                targetStream = new FileStream(Convert.ToString(outputTarget), FileMode.Create, FileAccess.Write);
            return new FilesToZip(targetStream, Convert.ToInt32(bufferSize));
        }

        [CanBeLazy]
        public static object WRptCopyFiles(object filesReader, object filesWriter)
        {
            var srcFiles = Utils.Cast<IReadFiles>(filesReader);
            var dstFiles = Utils.Cast<IWriteFiles>(filesWriter);
            return (LazyAsync)(async ae =>
            {
                foreach (var fileName in srcFiles.EnumerateFiles(null))
                    using (var src = srcFiles.OpenReadFile(fileName))
                        await dstFiles.CopyFile(src, fileName, ae.Cancellation, true);
                return string.Empty;
            });
        }

        [CanBeLazy]
        public static object WRptWriterDone(object filesWriter)
        {
            var iWF = Utils.Cast<IWriteFiles>(filesWriter);
            return (LazyAsync)(async ae =>
            {
                await iWF.Done(ae.Cancellation);
                return string.Empty;
            });
        }

        [Arity(2, 2)]
        public static object WRptGenerateReportExpr(CallExpr ce, Generator.Ctx callerCtx)//(object tmplFileName, Expr outputFilesExpr)
        {
            var ctx = new Generator.Ctx(callerCtx);
            ctx.CreateValue(FuncDefs_Core.stateTryGetConst, string.Empty);
            var templateFileName = Convert.ToString(Generator.Generate(ce.args[0], ctx));
            var outputFilesExpr = ce.args[1];
            var sheetExprs = new List<Expr>();
            int iSheet = 0;
            int blockNum = 0;

            var xr = new Xlsx.Reader(templateFileName);

            var lstStrTbl = new OPs.ListOfConst(xr.stringTable);
            ctx.CreateValue(XlsxR.Sheet.sStrTbl, lstStrTbl);

            foreach (var s in xr.EnumSheetInfos())
            {
                var cells = new XlsxR.Sheet(s.sheetCells.ToArray(), xr.styles);
                var sheetExpr = cells.GetSheetExpr(s.sheetFile, ctx, outputFilesExpr, ref blockNum);
                sheetExprs.Add(new CallExpr("_block",
                    sheetExpr,
                    XlsxR.PROGRESS("sheet", new ConstExpr(xr.sheetNames[iSheet]))
                ));
                iSheet++;
            };

            var loopRef = new ReferenceExpr("sheet");

            var res = CallExpr.Eval(
                CallExpr.let(new ConstExpr(XlsxR.Sheet.sTmplFiles), new ConstExpr(xr.xmlFiles)),
                CallExpr.let(new ConstExpr(XlsxR.Sheet.sStrTbl), new ConstExpr(lstStrTbl)),
                XlsxR.PROGRESS("sheets", new ConstExpr(new OPs.ListOfConst(xr.sheetNames))),
                new CallExpr("_ForEach", loopRef, new ArrayExpr(sheetExprs), loopRef),
                new CallExpr("WRptCopyFiles", new ConstExpr(xr.xmlFiles), outputFilesExpr),
                new CallExpr("WRptWriterDone", outputFilesExpr)
            );
            return res;
        }

        class DepsHelper
        {
            XlsxR.Sheet sheet;
            string[] stringTable;
            Dictionary<string, bool> already = new Dictionary<string, bool>();
            Generator.Ctx parentCtx;
            int maxNdxRowVal, maxNdxColVal;

            private DepsHelper() { }

            IEnumerable<string> CollectComments(CellRange a, CellRange b)
            {
                bool valFound = false;
                foreach (var r in CellRange.EnumBetween(a, b))
                {
                    Expr val;
                    sheet.vals.TryGetValue(r, out val);
                    val = val as IndexExpr;
                    if (valFound)
                    {
                        if (val == null)
                            yield break;
                    }
                    else if (val == null)
                        continue;
                    valFound = true;
                    string res;
                    if (val.nodeType == ExprType.Index)
                    {
                        var ie = (ConstExpr)((IndexExpr)val).index;
                        res = stringTable[Convert.ToInt32(ie.value)];
                    }
                    else res = val.ToString();
                    if (!string.IsNullOrWhiteSpace(res))
                        yield return res;
                }
            }

            [Flags]
            enum RCBits { None = 0x0, Row = 0x1, Col = 0x2 }

            struct RngExprInfo
            {

                public string rowComm, colComm, rngExpr;

                //public string GetCodeIfNextIs(Stack<(RCBits bit,string name)> regions, ref RCBits regionsActive,
                //    RngExprInfo next)
                //{
                //    if (rngExpr == null)
                //        return string.Empty;
                //    var sb = new StringBuilder();
                //    var regionsNext = Eq(this, next);

                //    // end of regions, if needed
                //    while((regionsNext ^ regionsActive) != 0)
                //    {
                //        var region = regions.Pop();
                //        regionsActive &= ~region.bit;
                //        sb.AppendLine($"#endregion // {region.bit}: {region.name}");
                //    }

                //    var delta = regionsNext ^ regionsActive;
                //    if ((delta & RCBits.Row) == 0)
                //    {
                //        sb.AppendLine($"// Row: {rowComm}");
                //    }
                //    else
                //    {
                //        sb.AppendLine($"#region Row: {rowComm}");
                //        regionsActive |= RCBits.Row;
                //    }
                //    foreach (RCBits bit in { RCBits.Row, RCBits.Col}){

                //    }
                //    switch(regionsNext)
                //    while(regionsNext != regionsActive)
                //    switch (delta)
                //    {
                //        case RCBits.None:
                //            // activity of regions is not changed
                //            break;
                //        case RCBits.Col:
                //            // column region change
                //            if ((regionsActive & RCBits.Col) != 0)
                //            {

                //            }
                //            break;
                //        case RCBits.Row:
                //            // row region change
                //            break;
                //        case RCBits.Row | RCBits.Col:
                //            // both regions change
                //            break;
                //    }
                //}

                //public static RCBits Eq(RngExprInfo a, RngExprInfo b)
                //{
                //    return
                //        ((a.rowComm == b.rowComm) ? RCBits.Row : 0)
                //        |
                //        ((a.colComm == b.colComm) ? RCBits.Col : 0);
                //}
            }

            RngExprInfo GetCode(CellRange rng, Expr expr)
            {
                return new RngExprInfo()
                {
                    // row comment
                    rowComm =
                        string.Join("; ", CollectComments(rng.ShiftedCell(0, -1), new CellRange(rng.row, 1)).Reverse())
                        + ", " +
                        string.Join("; ", CollectComments(rng.ShiftedCell(0, 1), new CellRange(rng.row, maxNdxColVal)))
                    ,
                    // col comment
                    colComm =
                        string.Join("; ", CollectComments(rng.ShiftedCell(-1, 0), new CellRange(1, rng.col)).Reverse())
                    ,
                    // cell value or formula
                    rngExpr = $"var {rng} = {expr};"
                };
            }

            IEnumerable<(CellRange cell, Expr expr)> DumpDepsImpl(string forCell)
            {
                if (already.ContainsKey(forCell))
                    yield break;
                already.Add(forCell, true);
                var rng = CellRange.TryFromName(forCell);
                if (rng == null)
                    yield break;
                Expr expr;
                if (sheet.fmls.TryGetValue(rng, out expr))
                {
                    // Make all cell references relative
                    expr = Expr.RecursiveModifier(XlsxR.RangeRefToRelativeModifier)(expr);
                    foreach (var s in expr.EnumerateReferences())
                        foreach (var dep in DumpDepsImpl(s))
                            yield return dep;
                }
                else if (!sheet.vals.TryGetValue(rng, out expr))
                    expr = new ConstExpr("??? (value not found)");
                if (forCell == null)
                { }
                yield return (rng, expr);
            }

            static (string Sheet, string Cell) SheetAndCell(string range)
            {
                int j = range.StartsWith("'") ? range.IndexOf('\'', 1) + 1 : 0;
                int i = range.IndexOf('!', j);
                if (i < 0)
                    return (null, range);
                else
                    return (range.Substring(0, i), range.Substring(i + 1));
            }

            public static string DumpDeps(Generator.Ctx parentCtx,
                Dictionary<string, XlsxR.Sheet> sheets, string[] stringTable, string[] forCells)
            {
                var sb = new StringBuilder();

                foreach (var sheetCells in forCells.Select(SheetAndCell).GroupBy(sc => sc.Sheet))
                {
                    var sheetKey = (sheetCells.Key == null) ? sheets.Keys.First() : sheetCells.Key;
                    var hlp = new DepsHelper()
                    {
                        already = new Dictionary<string, bool>(),
                        sheet = sheets[sheetKey],
                        stringTable = stringTable,
                        parentCtx = parentCtx,
                    };
                    foreach (var r in hlp.sheet.vals.Keys)
                    {
                        if (hlp.maxNdxColVal < r.col)
                            hlp.maxNdxColVal = r.col;
                        if (hlp.maxNdxRowVal < r.row)
                            hlp.maxNdxRowVal = r.row;
                    }

                    var deps = new List<(CellRange cell, Expr expr)>();
                    foreach (var sc in sheetCells)
                        deps.AddRange(hlp.DumpDepsImpl(sc.Cell));

                    deps.Sort((a, b) => CellRange.Compare(a.cell, b.cell));

                    var dict = deps.ToDictionary(d => d.cell.name, d => d.expr);

                    var sorted = TopoSort.DoSort(
                        deps.Select(d => d.cell.name),
                        cell => dict[cell].EnumerateReferences()
                    );

                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.AppendLine($"//********** sheet: {sheetKey}");

                    foreach (var cell in sorted)
                    {
                        var info = hlp.GetCode(CellRange.FromName(cell), dict[cell]);
                        if (!string.IsNullOrEmpty(info.rowComm))
                            sb.AppendLine($"//row: {info.rowComm}");
                        if (!string.IsNullOrEmpty(info.colComm))
                            sb.AppendLine($"//col: {info.colComm}");
                        sb.AppendLine(info.rngExpr);
                    }
                }
                return sb.ToString();
            }
        }

        [Arity(2, 2)]
        public static object WRptExploreDependencies(CallExpr ce, Generator.Ctx callerCtx)//(object tmplFileName, string[] forCells)
        {
            var ctx = new Generator.Ctx(callerCtx);
            ctx.CreateValue(FuncDefs_Core.stateTryGetConst, string.Empty);
            var templateFileName = Convert.ToString(Generator.Generate(ce.args[0], ctx));
            var sheets = new Dictionary<string, XlsxR.Sheet>();

            var xr = new Xlsx.Reader(templateFileName);

            foreach (var s in xr.EnumSheetInfos())
                sheets.Add(s.sheetName, new XlsxR.Sheet(s.sheetCells.ToArray(), xr.styles));

            var forCells = Utils.AsIList(Generator.Generate(ce.args[1], ctx))
                .Cast<object>()
                .Select(o => Convert.ToString(o))
                .ToArray();

            var code = DepsHelper.DumpDeps(ctx, sheets, xr.stringTable, forCells);
            return code;
        }

        [Arity(6, 6)]
        /// <summary>
        /// Расчёт гидравлического сопротивления линейного участка трубопровода (на чистой воде)
        /// Алгоритм расчёта импортирован из gidravlicheskiy-raschet-truboprovodov.xlsm 
        /// http://al-vo.ru/teplotekhnika/gidravlicheskiy-raschet-truboprovodov.html
        /// </summary>
        public static object WRptCalcWaterFlowResistanceDP(IList args)
        {
            //row: Расход воды через трубопровод; G=, т/час
            var D4 = Utils.Cnv(args[0]) / 24; // м3/сут -> т/час
            //row: Температура воды на входе; tвх=, °C
            var D5 = Utils.Cnv(args[1]);
            //row: Температура воды на выходе; tвых=, °C
            var D6 = Utils.Cnv(args[2]);
            //row: Внутренний диаметр трубопровода; d=, мм
            var D7 = Utils.Cnv(args[3]);
            //row: Длина трубопровода; L=, м
            var D8 = Utils.Cnv(args[4]);
            //row: Экв. шероховатость внутр. поверхностей труб; ∆=, мм
            var D9 = Utils.Cnv(args[5]);
            //row: Средняя температура воды; tср=, °C
            var D12 = (D5 + D6) / 2;
            //row: Кинематический к-т вязкости воды (при tср); n=, cм2/с
            var D13 = 0.0178 / (1 + 0.0337 * D12 + Math.Pow(0.000221 * D12, 2));
            //row: Средняя плотность воды (при tср); r=, т/м3
            var D14 = (Math.Pow(-0.003 * D12, 2) - 0.1511 * D12 + 1003.1) / 1000;
            //row: Скорость воды; v=, м/с
            var D16 = 4 * D4 / D14 / Math.PI / Math.Pow(D7 / 1000, 2) / 3600;
            //row: Число Рейнольдса; Re=, -
            var D17 = D16 * D7 / D13 * 10;
            //row: К-т гидравлического трения; λ=, -
            var D18 =
                (D17 <= 2320)
                ? 64 / D17
                : (D17 <= 4000)
                    ? 1.47E-05 * D17
                    : 0.11 * Math.Pow(68 / D17 + D9 / D7, 0.25);
            //row: Удельные потери давления на трение; R=, кг/(см2*м)
            var D19 = D18 * Math.Pow(D16, 2) * D14 / 2 / 9.81 / D7 * 100;
            //row: Потери давления на трение; dPтр=, кг/см2
            var D20 = D19 * D8;
            //
            return D20;
        }

        /// <summary>
        /// Write xlsx-sheet xml
        /// </summary>
        /// <param name="ae"></param>
        /// <param name="args">0:XmlWriter, 1:IEnumerable of XmlItem, 2:rows_body</param>
        /// <returns></returns>
        [Arity(3, int.MaxValue)]
        public static async Task<object> _ReportSheet(AsyncExprCtx ae, IList args)
        {
            using (var wr = (XmlWriter)await OPs.ConstValueOf(ae, args[0]))
            {
                var xmls = (IEnumerable<Xlsx.XmlItem>)await OPs.ConstValueOf(ae, args[1]);
                wr.WriteStartDocument();
                object result = string.Empty;

                foreach (var xml in xmls)
                {
                    xml.WriteStartElementAndAttrs(wr);

                    var xmlSheetData = new Xlsx.XmlItem();
                    {   // write xml before "sheetData"
                        bool stop = false;
                        xml.Traverse(x =>
                        {
                            if (stop) return;
                            if (x.Name != "sheetData")
                            {
                                if (x.Name == "dimension")
                                    return;
                                x.WritePart(wr);
                                return;
                            }
                            x.WriteStartElementAndAttrs(wr);
                            xmlSheetData = x;
                            stop = true;
                        });
                    }
                    if (xmlSheetData.IsEmpty)
                        continue; // no sheet data

                    // write sheet data (rows)
                    for (int i = 2; i < args.Count; i++)
                    {
                        result = await OPs.ConstValueOf(ae, args[i]);
                        //Console.WriteLine(res); // debug
                    }

                    {   // write xml after "sheetData"
                        bool skip = true;
                        xml.Traverse(x =>
                        {
                            if (!skip)
                            { x.WritePart(wr); return; }
                            else if (x.Name == "sheetData")
                            {
                                skip = false;
                                wr.WriteEndElement();
                            }
                        });
                    }
                    wr.WriteEndElement();
                }
                wr.WriteEndDocument();
                return result;
            }
        }

        /// <summary>
        /// Write xlsx-row xml
        /// </summary>
        /// <param name="ae"></param>
        /// <param name="args">0:XmlWriter, 1:XmlItem, 2:Dictionary<string, int> key2ndx, 3:IList data, 4:Destination row number, 5: IDictionary[int, bool] sharedIndex</param>
        /// <returns></returns>
        [Arity(6, 6)]
        public static async Task<object> _ReportRow(AsyncExprCtx ae, IList args)
        {
            // get/calculate values
            var lstVals = (IList)await OPs.VectorValueOf(ae, args[3]);
            int n = lstVals.Count;
            var vals = new object[n];
            for (int i = n - 1; i >= 0; i--)
                try { vals[i] = await OPs.ConstValueOf(ae, lstVals[i]); }
                catch (TaskCanceledException) { throw; }
                catch (Exception ex) { vals[i] = ex; }
            // write result
            var wr = (XmlWriter)await OPs.ConstValueOf(ae, args[0]);
            var xml = (Xlsx.XmlItem)await OPs.ConstValueOf(ae, args[1]);
            var key2ndx = (Dictionary<string, int>)await OPs.ConstValueOf(ae, args[2]);
            var dict = W.Common.ValuesDictionary.New(vals, key2ndx);
            var arg4 = await OPs.ConstValueOf(ae, args[4]);
            var dstRowNumber = Convert.ToInt32(arg4);
            var sharedIndex = (IDictionary<string, int>)args[5];
            xml.WriteRow(wr, dict, dstRowNumber, sharedIndex, XlsxR.ChangeFormula, XlsxR.GetStrValue);
            return dstRowNumber;
        }

        [Arity(0, 0)]
        public static object _ReportNewSharedIndex(IList args)
        {
            return new Dictionary<string, int>();
        }

        //static readonly DateTime startTime = DateTime.Now;

        //public static object LV(IList args)
        //{
        //	return new TimedString(startTime, DateTime.MaxValue, string.Join(":", args));
        //}

        //[Arity(1, 2)]
        //public static object _QueryYesNo(IList args)
        //{
        //	string text = Convert.ToString(args[0]);
        //	string title = (args.Count > 1) ? Convert.ToString(args[1]) : "Генератор отчётов";
        //	return System.Windows.Forms.MessageBox.Show(text, title,
        //		System.Windows.Forms.MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.Yes;
        //}

        [Arity(1, 100)]
        public static object WRptDbg(IList args)
        { return args[0]; }
    }

}