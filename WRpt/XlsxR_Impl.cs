using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using ICSharpCode.SharpZipLib.Zip;

namespace W.Rpt
{
    using W.Common;
    using W.Expressions;
    using W.Files;

    /// <summary>
    /// Xlsx Reporting support
    /// </summary>
    internal static class XlsxR
    {
        public class Exception : System.Exception { public Exception(string msg) : base(msg) { } }

        public static Expr PROGRESS(string sectionName, Expr value, Expr extra = null)
        { return new CallExpr("PROGRESS", new ConstExpr(sectionName), value, extra ?? ConstExpr.Null); }

        static Expr ChangeFormulaImpl(Expr formula, int rowdelta)
        {
            var expr = Expr.RecursiveModifier(e =>
            {
                if (e.nodeType != ExprType.Reference)
                    return e;
                var rng = CellRange.TryFromName(e.ToString());
                if (rng == null || rng.row < 0 && rng.row2 <= 0)
                    return e;
                var r = new CellRange(
                    (rng.row > 0) ? rng.row + rowdelta : rng.row,
                    rng.col,
                    (rng.row2 > 0) ? rng.row2 + rowdelta : rng.row2,
                    rng.col2
                    );
                return new ReferenceExpr(r.name);
            })(formula);
            return expr;
        }

        public static string ChangeFormula(object formula, int rowdelta)
        {
            var expr = ChangeFormulaImpl((W.Expressions.Expr)formula, rowdelta);
            var f = expr.ToString().Replace('\'', '\"');
            return f;
        }

        public static Expr RangeRefToRelativeModifier(Expr e)
        {
            if (e.nodeType != ExprType.Reference)
                return e;
            var rname = ((ReferenceExpr)e).name;
            var rng = CellRange.TryFromName(rname);
            if (rng == null)
                return e;
            var rel = rng.AsRelative();
            return (rel == rng) ? e : new ReferenceExpr(rel.name);
        }

        public static Xlsx.ValueToStrInfo GetStrValue(object value)
        {
            var r = new Xlsx.ValueToStrInfo();
            var ic = value as IConvertible;
            if (ic == null)
            {
                if (value == null)
                    return r;
                if (value is Expr)
                {
                    r.xlsxType = "fmla";
                    return r;
                }
                else if (value is System.Exception)
                    r.xlsxType = "e";
                else r.xlsxType = "str";
                r.value = Convert.ToString(value) ?? string.Empty;
                return r;
            }
            switch (ic.GetTypeCode())
            {
                case TypeCode.DateTime:
                    r.xlsxType = string.Empty;
                    r.value = ic.ToDateTime(Xlsx.fmt).ToOADate().ToString(Xlsx.fmt);
                    return r;
                case TypeCode.String:
                    r.xlsxType = "str";
                    r.value = ic.ToString();
                    return r;
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                    r.xlsxType = string.Empty;
                    r.value = Convert.ToInt64(value).ToString(Xlsx.fmt);
                    return r;
                case TypeCode.UInt64:
                case TypeCode.Decimal:
                case TypeCode.Single:
                case TypeCode.Double:
                    r.xlsxType = string.Empty;
                    var d = Convert.ToDouble(value);
                    if (double.IsNaN(d))
                    {
                        r.xlsxType = "e";
                        r.value = "#VALUE!";
                        return r;
                    }
                    else if (double.IsInfinity(d))
                    {
                        r.xlsxType = "e";
                        r.value = "#INF!";
                        return r;
                    }
                    r.value = d.ToString(Xlsx.fmt);
                    return r;
                case TypeCode.DBNull:
                    r.xlsxType = string.Empty;
                    r.value = string.Empty;
                    return r;
                default:
                    r.xlsxType = "str";
                    r.value = Convert.ToString(value) ?? string.Empty;
                    return r;
            }
        }

        public class Sheet
        {
            /// <summary>
            /// Calculable formulas
            /// </summary>
            public Dictionary<CellRange, Expr> fmls = new Dictionary<CellRange, Expr>(CellRange.Comparer);
            /// <summary>
            /// Constant values in cells
            /// </summary>
            public Dictionary<CellRange, Expr> vals = new Dictionary<CellRange, Expr>(CellRange.Comparer);
            //
            Xlsx.XmlItem[] sheetCells;
            Dictionary<int, Xlsx.XmlItem> xmlRows = new Dictionary<int, Xlsx.XmlItem>();
            Dictionary<int, SharedFormulaInfo> sharedFmls = new Dictionary<int, SharedFormulaInfo>();
            Xlsx.StyleInfo[] styles;

            public Sheet(Xlsx.XmlItem[] sheetCells, Xlsx.StyleInfo[] styles)
            {
                this.styles = styles;
                this.sheetCells = sheetCells;
                ReadDocument(sheetCells);
            }

            void ReadCells(IEnumerable<Xlsx.XmlItem> cells)
            {
                var strTblRef = new ReferenceExpr(sStrTbl);
                foreach (var c in cells)
                {
                    CellRange crng = null;
                    string ctype = null;
                    int iStyle = -1;
                    foreach (var a in c.Attrs)
                        switch (a.Name)
                        {
                            case "r": crng = CellRange.FromName(a.Value); break;
                            case "t": ctype = a.Value; break;
                            case "s": iStyle = int.Parse(a.Value); break;
                        }
                    Expr fmla = null;
                    bool withFormula = false;
                    bool hidden = false;
                    if (c.subItems == null)
                        goto DoneReadCell;
                    if (iStyle >= 0)
                        hidden = styles[iStyle].hidden;
                    foreach (var x in c.subItems)
                        if (x.Name == "f")
                        {   // formula
                            withFormula = true;
                            if (x.Value != null)
                                fmla = Parser.ParseToExpr(x.Value);
                            if (x.Attrs == null)
                                break;
                            CellRange frng = null;
                            string ftype = null;
                            int sharedIndex = -1;
                            foreach (var a in x.Attrs)
                                if (a.Name == "ref")
                                    frng = CellRange.FromName(a.Value);
                                else if (a.Name == "t")
                                    ftype = a.Value;
                                else if (a.Name == "si")
                                    sharedIndex = int.Parse(a.Value);
                            if (frng == null)
                            {   // formula without range specified
                                if (sharedIndex >= 0)
                                {   // reference to shared formula replaced with real formula
                                    var info = sharedFmls[sharedIndex];
                                    var dstCell = crng;
                                    var expr = Expr.RecursiveModifier(e =>
                                    {
                                        if (e.nodeType != ExprType.Reference)
                                            return e;
                                        var re = (ReferenceExpr)e;
                                        Tuple<int, int> t;
                                        if (!info.relativeRefs.TryGetValue(re.name, out t))
                                            return e;
                                        int ir = t.Item1, ic = t.Item2;
                                        var cell = CellRange.RowAndColToCellName((ir < 0) ? ir : crng.row - info.srcCell.row + ir, (ic < 0) ? ic : crng.col - info.srcCell.col + ic);
                                        return new ReferenceExpr(cell);
                                    })(info.formula);
                                    fmla = expr;
                                }
                            }
                            else if (ftype == "shared")
                            #region Shared formula declaration
                            {   // shared formula
                                var relativeRefs = new Dictionary<string, Tuple<int, int>>();
                                foreach (var s in fmla.EnumerateReferences())
                                {
                                    if (relativeRefs.ContainsKey(s))
                                        continue;
                                    int pos = 0, row, col;
                                    if (!CellRange.ParseCellName(s, ref pos, s.Length, out row, out col) || row < 0 && col < 0)
                                        continue;
                                    relativeRefs.Add(s, new Tuple<int, int>(row, col));
                                }
                                sharedFmls.Add(sharedIndex, new SharedFormulaInfo()
                                {
                                    formula = fmla,
                                    index = sharedIndex,
                                    srcCell = crng,
                                    refRange = frng,
                                    relativeRefs = relativeRefs
                                });
                            }
                            #endregion Shared formula declaration
                            else if (ftype == "array")
                            #region Array formula
                            {   // array formula
                                int rowA = frng.row, colA = frng.col, rowB = frng.row2, colB = frng.col2;
                                fmls.Add(frng, hidden ? new ConstExpr(fmla) : fmla);
                                fmla = null;
                                if (frng.IsOneCell || hidden)
                                    continue;
                                if (rowA != rowB && colA != colB)
                                    throw new Exception("Only 'vector' ranges supported (range must be fitted in one row xor one column): " + frng);
                                var arrRef = new ReferenceExpr(frng.name);
                                int rowStep, colStep, count;
                                if (rowA < rowB)
                                { rowStep = 1; colStep = 0; count = rowB - rowA + 1; }
                                else if (colA < colB)
                                { rowStep = 0; colStep = 1; count = colB - colA + 1; }
                                else throw new Exception("Unsupported kind of range: " + frng);
                                for (int index = 0, row = rowA, col = colA; index < count; index++, row += rowStep, col += colStep)
                                {
                                    var cell = new CellRange(row, col);
                                    fmls.Add(cell, new IndexExpr(arrRef, new ConstExpr(index)));
                                }
                            }
                            #endregion Array formula
                            else throw new Exception(string.Format("Unsupported formula type: {0} [{1}]", ftype, frng));
                            break;
                        }
                        else if (x.Name == "v")
                        {
                            Expr expr = null;
                            switch (ctype)
                            {
                                case "e": // erroneous value in cell
                                    break;
                                case "b": // bool
                                    expr = new ConstExpr(int.Parse(x.Value));
                                    break;
                                case "s": // indexed string
                                    expr = new IndexExpr(strTblRef, new ConstExpr(int.Parse(x.Value)));
                                    break;
                                case "str": // string
                                    expr = new ConstExpr(x.Value);
                                    break;
                                case null:
                                    if (!string.IsNullOrEmpty(x.Value))
                                    {
                                        double dbl;
                                        if (OPs.TryParseDouble(x.Value, out dbl))
                                            expr = new ConstExpr(dbl);
                                        else expr = new ConstExpr(x.Value);
                                    }
                                    break;
                                default:
                                    break;
                            }
                            if (expr != null)
                                vals.Add(crng, expr);
                        }
                    DoneReadCell:
                    if (fmla != null)
                    {
                        Expr prev;
                        if (fmls.TryGetValue(crng, out prev))
                            Diagnostics.Logger.TraceInformation("Formula for cell {0} '{1}' replaced with '{2}", crng.name, prev, fmla);
                        fmls[crng] = hidden ? new ConstExpr(fmla) : fmla;
                    }
                    // no formula can be in this cell, except array formula (ExprType.Index)
                    else if (!withFormula && fmls.TryGetValue(crng, out fmla))
                    {
                        if (fmla.nodeType != ExprType.Index)
                            fmls.Remove(crng);
                    }
                }
            }

            void ReadRows(IEnumerable<Xlsx.XmlItem> rows)
            {
                foreach (var r in rows)
                {
                    var sRow = r.Attrs.Where(a => a.Name == "r").First().Value;
                    if (r.subItems != null)
                        ReadCells(r.subItems);
                    xmlRows.Add(int.Parse(sRow), r);
                }
            }

            void ReadDocument(IEnumerable<Xlsx.XmlItem> sheetXml)
            {
                var worksheet = sheetXml.Where(x => x.Name == "worksheet").First();
                var sheetData = worksheet.subItems.Where(x => x.Name == "sheetData").First();
                ReadRows(sheetData.subItems);
            }

            struct LoopInfo
            {
                public CallExpr begCall;
                public List<Expr> outerBlockExprs;
                public Expr outerRowNum, innerRowNum;
            }

            struct RowInfo
            {
                public Expr visibility;
                public int srcNum;
                public Xlsx.XmlItem xml;
                public ReferenceExpr[] cellsRefs;
            }

            class SharedFormulaInfo
            {
                public Expr formula;
                public CellRange srcCell;
                public CellRange refRange;
                public int index;
                public Dictionary<string, Tuple<int, int>> relativeRefs;
            }

            public const string sTmplFiles = "#tmplFiles";
            public const string sXmlWriter = "#xmlWriter";
            public const string sStrTbl = "#StrTbl";

            static Expr letDbg(Expr name, Expr value) { return CallExpr.let(name, value); }
            //{ return CallExpr.let(name, new CallExpr("RptDbg", value, new ConstExpr(name.ToString()))); }

            static Expr let(Expr name, Expr value) { return CallExpr.let(name, value); }
            static Expr fmax(Expr arg0, Expr arg1) { return new BinaryExpr(ExprType.Fluent, arg0, new CallExpr("MAX", arg1)); }

            public Expr GetSheetExpr(string sheetFile, Generator.Ctx ctx, Expr outputFilesExpr, ref int externalBlockNum)
            {
#if SAFE_PARALLELIZATION
                int iCountParallelization = (ctx.IndexOf(FuncDefs_Core.stateParallelizationAlreadyInvolved) < 0) ? 0 : 1;
#endif
                //var usageDict = new Dictionary<string, bool>(fmls.Count);
                var arrsDict = new Dictionary<CellRange, Expr>(CellRange.Comparer);
                var rowsExprsDict = fmls.GroupBy(pair => pair.Key.row).ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Key, p => p.Value));
                var stack = new Stack<LoopInfo>();
                var defsExprs = new List<Expr>();
                var workExprs = new List<Expr>();
                var rowInfos = new List<RowInfo>();
                var sheetKey = Path.GetFileNameWithoutExtension(sheetFile);
                var xmlWriterRef = new ReferenceExpr(sXmlWriter + sheetKey);
                var sharedIndexRef = new ReferenceExpr("#sharedIndex_" + sheetKey);
                var blockExprs = new List<Expr>();
                blockExprs.Add(xmlWriterRef);
                blockExprs.Add(new ConstExpr(sheetCells));
                Expr prevRowNumbExpr = ConstExpr.Zero;
                int blockNum = externalBlockNum;

                #region flushToRowsLoop()
                Action flushToRowsLoop = () =>
                {
                    if (defsExprs.Count > 0)
                    {
                        defsExprs = ExprsTopoSort.DoSort2(defsExprs, ctx).ToList();
                        defsExprs.Insert(0, ConstExpr.Null);
                        blockExprs.Add(new CallExpr(FuncDefs_Core.Bypass, defsExprs.ToArray()));
                        defsExprs.Clear();
                    }
                    if (workExprs.Count > 0)
                    {
                        blockExprs.AddRange(workExprs);
                        workExprs.Clear();
                    }
                    if (rowInfos.Count > 0)
                    {
                        int n = rowInfos.Count;
                        var vals = new List<ArrayExpr>(n);
                        foreach (var ri in rowInfos)
                        {
                            var crs = ri.cellsRefs;
                            vals.Add(new ArrayExpr(
                                ri.visibility ?? ConstExpr.True,
                                // XmlItem
                                new ConstExpr(ri.xml),
                                // key2ndx
                                new ConstExpr(Enumerable.Range(0, crs.Length).ToDictionary(i => crs[i].name, i => i)),
                                // data[]
                                new ArrayExpr(crs)
                                ));
                        }
                        rowInfos.Clear();
                        var rowNumbRef = new ReferenceExpr("Blk" + blockNum + "_Numb");
                        var rowNextNumbRef = new ReferenceExpr("Blk" + blockNum + "_NextNumb");
                        var loopVarRef = new ReferenceExpr("Blk" + blockNum + "_Vals");
                        var initExpr = new ArrayExpr(
                            let(rowNumbRef, prevRowNumbExpr ?? ConstExpr.One),
                            loopVarRef
                            );
                        Expr bodyExpr;
                        {
                            var visRef = new ReferenceExpr("Blk" + blockNum + "_RowVisible");
                            var rowNumbPlusOne = new BinaryExpr(ExprType.Add, rowNumbRef, ConstExpr.One);
                            var reportRowExpr = new CallExpr(FuncDefs_Rpt._ReportRow, xmlWriterRef
                                , new IndexExpr(loopVarRef, ConstExpr.One)
                                , new IndexExpr(loopVarRef, new ConstExpr(2))
                                , new IndexExpr(loopVarRef, new ConstExpr(3))
                                , rowNumbPlusOne
                                , sharedIndexRef
                                );
                            bodyExpr = CallExpr.Eval(
                                let(visRef, new IndexExpr(loopVarRef, ConstExpr.Zero))
                                , CallExpr.IF(visRef, reportRowExpr, ConstExpr.Null)
                                , let(rowNumbRef, rowNextNumbRef)
                                , let(rowNextNumbRef, CallExpr.IF(visRef, rowNumbPlusOne, rowNumbRef))
                                );
                        }
                        var loopExpr = new CallExpr(FuncDefs_Core._ForEach, initExpr, new ArrayExpr(vals.ToArray()), bodyExpr);
                        var lastExpr = fmax(loopExpr, prevRowNumbExpr);
                        prevRowNumbExpr = new ReferenceExpr("Blk" + blockNum + "_LastNum");
                        blockExprs.Add(let(prevRowNumbExpr, lastExpr));
                    }
                    blockNum++;
                };
                #endregion flushToRowsLoop()

                foreach (var xmlRow in xmlRows)
                {
                    Dictionary<CellRange, Expr> rowSrcExprs;
                    if (!rowsExprsDict.TryGetValue(xmlRow.Key, out rowSrcExprs))
                        rowSrcExprs = new Dictionary<CellRange, Expr>(0);
                    var reportLoopBegs = new List<CallExpr>();
                    var reportLoopEnds = new List<CallExpr>();
                    var rowDefs = new List<KeyValuePair<ReferenceExpr, Expr>>(rowSrcExprs.Count);
                    var cellsRefs = new List<ReferenceExpr>(rowSrcExprs.Count);
                    ReferenceExpr visibilityRef = null;

                    #region Visitor
                    Func<Expr, Expr> fModifyCellsRefs = e =>
                    {
                        if (e.nodeType != ExprType.Reference)
                            return e;
                        var rname = ((ReferenceExpr)e).name;
                        var rng = CellRange.TryFromName(rname);
                        if (rng == null)
                            return e;
                        var rel = rng.AsRelative();
                        if (!rel.IsOneCell)
                        {
                            if (rel.col2 != 0 && !arrsDict.ContainsKey(rel) && !fmls.ContainsKey(rel))
                            {   // the first occurence of the array reference :-)
                                // create array definition
                                var lst = new List<Expr>();
                                for (int row = rel.row; row <= rel.row2; row++)
                                    for (int col = rel.col; col <= rel.col2; col++)
                                    {
                                        var src = new CellRange(row, col);
                                        if (!fmls.ContainsKey(src) && !vals.ContainsKey(src))
                                            throw new Exception("Cell isn't defined: " + src);
                                        Expr cellExpr;
                                        if (!vals.TryGetValue(src, out cellExpr))
                                            cellExpr = new ReferenceExpr(src.name);
                                        lst.Add(cellExpr);
                                    }
                                var arrExpr = new ArrayExpr(lst);
                                arrsDict.Add(rel, arrExpr);
                                rowDefs.Add(new KeyValuePair<ReferenceExpr, Expr>(new ReferenceExpr(rel.name), arrExpr));
                            }
                            return (rng.name == rel.name) ? e : new ReferenceExpr(rel.name);
                        }
                        else
                        {
                            Expr exprVal;
                            if (vals.TryGetValue(rel, out exprVal))
                                return exprVal; // replace cell reference with cell value
                            var cell = rel.name;
                            if (!fmls.ContainsKey(rel))
                                throw new Exception("Cell isn't defined: " + cell);
                            //usageDict[cell] = true;
                            return (rng.name == cell) ? e : new ReferenceExpr(cell);
                        }
                    };

                    var modifyCellsRefs = Expr.RecursiveModifier(fModifyCellsRefs);

                    Func<Expr, Expr> modifyNonRef = e =>
                    {
                        var ce = (e.nodeType == ExprType.Call) ? (CallExpr)e : null;
                        if (ce == null)
                            return e;
                        switch (ce.funcName)
                        {
                            case "_ReportLoopBegin":
                                {   // _ReportLoopBegin
                                    int n = ce.args.Count;
                                    if (n < 2 || 3 < n)
                                        throw new Exception("Expected 2 or 3 args in " + ce.ToString());
                                    reportLoopBegs.Add((CallExpr)modifyCellsRefs(ce));
                                    return new ConstExpr(string.Empty);
                                }
                            case "_ReportLoopEnd":
                                {   // _ReportLoopEnd
                                    if (ce.args.Count < 1)
                                        throw new Exception("Expected 1 arg in " + ce.ToString());
                                    reportLoopEnds.Add((CallExpr)modifyCellsRefs(ce));
                                    return new ConstExpr(string.Empty);
                                }
                            case "_RowVisible":
                                {
                                    if (ce.args.Count != 1)
                                        throw new Exception("Expected 1 arg in " + ce.ToString());
                                    if (visibilityRef != null)
                                        throw new Exception("Duplicated row visibility condition: " + ce.args[0].ToString());
                                    visibilityRef = new ReferenceExpr(string.Format("_Row{0}Vis", xmlRow.Key));
                                    return let(visibilityRef, modifyCellsRefs(ce.args[0]));
                                }
                            default:
                                return e;
                        }
                    };

                    var modify = Expr.RecursiveModifier(e =>
                    {
                        if (e.nodeType != ExprType.Reference)
                            return modifyNonRef(e);
                        return fModifyCellsRefs(e);
                    });
                    #endregion Visitor

                    #region loop over cells in row
                    bool notInLoop = stack.Count == 0 && reportLoopBegs.Count == 0;
                    foreach (var cellExpr in rowSrcExprs)
                    {
                        var tmp = CallExpr.Eval(cellExpr.Value);
                        var expr = tmp.Visit(modify);
                        expr = ((CallExpr)expr).args[0];
                        var cellRef = new ReferenceExpr(cellExpr.Key.name);
                        rowDefs.Add(new KeyValuePair<ReferenceExpr, Expr>(cellRef, expr));
                        if (cellExpr.Key.IsOneCell)
                            cellsRefs.Add(cellRef);
                    }
                    #endregion //loop over cells in row

                    #region Process calls to _ReportLoopBegin
                    foreach (var beg in reportLoopBegs)
                    {   //***** open a new loop
                        var loopVarRef = beg.args[0] as ReferenceExpr;
                        if (loopVarRef == null)
                            loopVarRef = new ReferenceExpr("&" + beg.args[0].ToString().GetHashCode().ToString("X"));
                        flushToRowsLoop();
                        var li = new LoopInfo()
                        {
                            begCall = beg,
                            outerBlockExprs = blockExprs,
                            outerRowNum = prevRowNumbExpr,
                            innerRowNum = new ReferenceExpr(string.Format("#Blk{0}_Num", loopVarRef)),
                        };
                        stack.Push(li);
                        blockExprs = new List<Expr>();
                        prevRowNumbExpr = li.innerRowNum;
#if SAFE_PARALLELIZATION
                        iCountParallelization++;
#endif
                    }
                    #endregion Process calls to _ReportLoopBegin

                    defsExprs.AddRange(rowDefs.Select(p => let(p.Key, p.Value)));

                    // save row expression
                    rowInfos.Add(new RowInfo() { srcNum = xmlRow.Key, xml = xmlRow.Value, visibility = visibilityRef, cellsRefs = cellsRefs.ToArray() });

                    #region Process calls to _ReportLoopEnd
                    foreach (var end in reportLoopEnds)
                    {   //***** close a loop
#if SAFE_PARALLELIZATION
                        iCountParallelization--;
#endif
                        var li = stack.Pop();
                        var beg = li.begCall;
                        if (beg.args[0].ToString() != end.args[0].ToString())
                            throw new Exception(string.Format("ReportLoopEnd: expected {0} instead of {1}", beg.args[0], end.args[0]));
                        ReferenceExpr loopVarRef = null;
                        string[] loopVars = null;
                        {
                            var vars = Generator.Generate(beg.args[0], ctx);
                            if (OPs.KindOf(vars) == ValueKind.Const)
                            {
                                var lst = vars as System.Collections.IList;
                                if (lst != null)
                                {
                                    loopVars = lst.ToArray(Convert.ToString);
                                    loopVarRef = new ReferenceExpr("#loopVar");
                                }
                            }
                        }
                        if (loopVarRef == null)
                            loopVarRef = beg.args[0] as ReferenceExpr ?? new ReferenceExpr(OPs.TryAsName(beg.args[0], ctx));
                        //var loopVarRef2 = end.args[0] as ReferenceExpr ?? new ReferenceExpr(OPs.TryAsName(end.args[0], ctx));
                        var lstJoinKeys = new List<string>();
                        if (loopVars != null)
                            lstJoinKeys.AddRange(loopVars.Where(ValueInfo.IsID));
                        else
                            lstJoinKeys.Add(loopVarRef.name);
                        lstJoinKeys.AddRange(end.args.Skip(1).Select(e => OPs.TryAsName(e, ctx)));
                        var fullJoinKeys = new HashSet<string>(lstJoinKeys);
                        // Process LV function calls
                        ReferenceExpr LV_ResRef = null;
                        Expr LV_Expr = null;
                        {
                            IDictionary<string, LV_OutInfo> lvOut;
                            if (blockExprs.Count > 0)
                            {
                                System.Diagnostics.Trace.Assert(defsExprs.Count == 0);
                                blockExprs = ProcessLVs(blockExprs, fullJoinKeys, ctx, out lvOut);
                            }
                            else
                                defsExprs = ProcessLVs(defsExprs, fullJoinKeys, ctx, out lvOut);
                            if (lvOut.Count > 0)
                            {
                                var joinerArgs = new List<Expr>();
                                joinerArgs.Add(new ArrayExpr(lstJoinKeys.Select(s => new ConstExpr(s)).ToArray()));
                         
                                joinerArgs.Add(new ReferenceExpr(FuncDefs_Solver.optionSolverAliases));
                                foreach (var lo in lvOut.Values)
                                {
                                    joinerArgs.Add(lo.loadingExpr);
                                    joinerArgs.Add(new ArrayExpr(lo.resultParams));
                                }
                                LV_Expr = new CallExpr(FuncDefs_Solver.Solver_FullJoinIDicts, joinerArgs);
                                LV_ResRef = new ReferenceExpr("#lv");
                            }
                        }
                        flushToRowsLoop();
                        blockExprs.Add(let(li.innerRowNum, prevRowNumbExpr));
                        blockExprs.Add(prevRowNumbExpr);
                        Expr loopExpr;
                        var iRef = new ReferenceExpr("#i");
                        var partsRef = new ReferenceExpr("#parts");
                        var rowNumRef = new ReferenceExpr("#rowNum");
                        var nextRowNumRef = new ReferenceExpr("#nextRowNum");
                        {
                            var inits = new List<Expr>();
                            inits.Add(let(li.innerRowNum, rowNumRef));
                            if (LV_ResRef != null)
                                inits.Add(let(loopVarRef, new IndexExpr(partsRef, iRef)));

                            var varsInits = new List<Expr>();
                            if (loopVars != null)
                            {
                                // if more than one loop variable
                                string sLoopIdPrm = null;
                                foreach (var prm in loopVars)
                                    if (ValueInfo.IsTimeKeyword(prm))
                                        varsInits.Add(let(new ReferenceExpr(prm), new CallExpr((Fx)FuncDefs_Core.GetSingleDate, new IndexExpr(loopVarRef, new ConstExpr(prm)))));
                                    else if (ValueInfo.IsID(prm))
                                    {
                                        if (sLoopIdPrm != null)
                                            continue;
                                        //throw new SolverException($"Now no more than one ID supported in loop variables: {sLoopIdPrm} , {prm} // {string.Join(", ", loopVars)}");
                                        varsInits.Add(let(new ReferenceExpr(prm), new CallExpr(FuncDefs_Report.Distinct, new IndexExpr(loopVarRef, new ConstExpr(prm)))));
                                        sLoopIdPrm = prm;
                                    }
                                if (string.IsNullOrEmpty(sLoopIdPrm))
                                    throw new SolverException($"Loop vars must contains at least one ID // {string.Join(", ", loopVars)}");
                                /*
								foreach(var prm in loopVars.Where(s => !ValueInfo.IsTimeKeyword(s)))
									inits.Add(let(new ReferenceExpr(prm), new IndexExpr(loopVarRef, new ConstExpr(prm))));
								/*/
                                Expr lookupValueFieldsExpr;
                                var lookupDataExpr = new ReferenceExpr("Blk" + blockNum + "_LookupData");
                                {

                                    var lstFields = loopVars
                                        .Where(s => !ValueInfo.IsTimeKeyword(s) && !ValueInfo.IsID(s))
                                        .Select(s => new ConstExpr(s))
                                        .ToList();
                                    lookupValueFieldsExpr = new ArrayExpr(lstFields.ToArray());
                                    lstFields.Add(new ConstExpr(sLoopIdPrm));
                                    var lookupDataFieldsExpr = new ArrayExpr(lstFields.ToArray());
                                    varsInits.Add(let(lookupDataExpr, new CallExpr(FuncDefs_Solver.IDictsToLookup2, loopVarRef, lookupDataFieldsExpr)));
                                }
                                varsInits.Add(new CallExpr(FuncDefs_Solver.letLookupFunc,
                                    // function name
                                    new ReferenceExpr("Blk" + blockNum + "_FuncLookup"),
                                    // key (input/output) parameter
                                    new ArrayExpr(new ConstExpr(sLoopIdPrm)),
                                    // value (output) parameters
                                    lookupValueFieldsExpr,
                                    // data array
                                    lookupDataExpr
                                ));
                                //*/
                            }
                            if (LV_ResRef != null)
                                inits.AddRange(varsInits);
                            else
                                blockExprs.InsertRange(0, varsInits);
                            // last init must be name (reference) of loop value
                            inits.Add(LV_ResRef ?? loopVarRef);
                            loopExpr = new CallExpr(FuncDefs_Core._ForEach,
                                new ArrayExpr(inits),
                                LV_Expr ?? new IndexExpr(partsRef, iRef), //beg.args[1],
                                CallExpr.Eval(blockExprs.ToArray())
                            );
                        }
                        // create expression like "_ForEach(...).MAX(prevRowNum)..let(prevRowNum)"
                        var blkLastNumRef = new ReferenceExpr(string.Format("Blk{0}_LastNum", loopVarRef));
                        {   // loop over parts
                            var nRef = new ReferenceExpr("#n");
                            var loopObjectsList = beg.args[1];
                            var partLimit = (beg.args.Count > 2)
                                ? beg.args[2]
                                : Parser.ParseToExpr(string.Format("IF( COLUMNS({0})>=5000, INT(COLUMNS({0})/100), 50)", loopObjectsList));
#if SAFE_PARALLELIZATION
                            var sLoopFunc = (iCountParallelization > 0) ? (Macro)FuncDefs_Core._For : (Macro)FuncDefs_Core._ParFor;
#else
							var sLoopFunc = (Macro)FuncDefs_Core._ParFor;
#endif
                            loopExpr = new CallExpr(sLoopFunc,
                                // init
                                new ArrayExpr(
                                    //let(partsRef, new CallExpr("PartsOfLimitedSize", loopObjectsList, partLimit)),
                                    //let(nRef, new CallExpr("COLUMNS", partsRef)),
                                    //XlsxRW.PROGRESS(new ConstExpr(loopObjectsList), nRef),
                                    let(iRef, ConstExpr.Zero),
                                    let(rowNumRef, li.outerRowNum)
                                    ),
                                // condition
                                new BinaryExpr(ExprType.LessThan, iRef, nRef),
                                // body
                                CallExpr.Eval(
                                    let(iRef, new BinaryExpr(ExprType.Add, iRef, ConstExpr.One)),
                                    letDbg(nextRowNumRef, fmax(loopExpr, rowNumRef)),
                                    //XlsxRW.PROGRESS("rownum", nextRowNumRef),
                                    PROGRESS("part", iRef),
                                    let(rowNumRef, nextRowNumRef),
                                    nextRowNumRef
                                    )
                                );
                            loopExpr = new CallExpr(FuncDefs_Core._block,
                                let(partsRef, new CallExpr((Macro)FuncDefs_Report.PartsOfLimitedSize, loopObjectsList, partLimit)),
                                let(nRef, new CallExpr(FuncDefs_Core.COLUMNS, partsRef)),
                                PROGRESS("parts", nRef, new ConstExpr(loopObjectsList)),
                                loopExpr
                            );
                            loopExpr = letDbg(blkLastNumRef, fmax(loopExpr, li.outerRowNum));
                        }
                        // switch start to outer loop
                        workExprs.Add(loopExpr);
                        workExprs.Add(PROGRESS("rownum", blkLastNumRef));
                        workExprs.Add(blkLastNumRef);
                        blockExprs = li.outerBlockExprs;
                        prevRowNumbExpr = blkLastNumRef;
                        flushToRowsLoop();
                    }
                    #endregion Process calls to _ReportLoopEnd
                }
                if (stack.Count > 0)
                    throw new Exception("Not all ReportLoops closed: " + string.Join(", ", stack.Select(li => ((ReferenceExpr)li.begCall.args[0]).name).Reverse()));
                flushToRowsLoop();
                externalBlockNum = blockNum;
                return CallExpr.Eval(
                    let(xmlWriterRef, new CallExpr(FuncDefs_Rpt.WRptGetXmlWriter, outputFilesExpr, new ConstExpr(sheetFile))),
                    let(sharedIndexRef, new CallExpr(FuncDefs_Rpt._ReportNewSharedIndex)),
                    new CallExpr(FuncDefs_Rpt._ReportSheet, blockExprs.ToArray())
                    );
            }

            /// <summary>
            /// Группа получаемых данных с единым временным диапазоном 
            /// </summary>
            struct LV_GrpInfo
            {
                public Expr timeExpr;
                public ReferenceExpr resExpr;
                public ConstExpr prefixExpr;
                public Dictionary<string, LV_PrmInfo> pname2expr;
            }

            /// <summary>
            /// Описание параметра получаемых данных 
            /// </summary>
            struct LV_PrmInfo
            {
                public string paramName;
                public Expr paramNameExpr;
                public Expr prefixedName;
            }

            /// <summary>
            /// Выходные данные ProcessLVs: выражение загрузки и выражения результирующих показателей
            /// </summary>
            struct LV_OutInfo
            {
                public Expr loadingExpr;
                public Expr[] resultParams;
            }

            static List<Expr> ProcessLVs(IList<Expr> blockExprs, HashSet<string> fullJoinKeys, Generator.Ctx ctx, out IDictionary<string, LV_OutInfo> outInfo)
            {
                // Собираем данные об используемых временных диапазонах и группах соответствующих им показателей
                var dict = new Dictionary<string, LV_GrpInfo>();
                var modify = Expr.RecursiveModifier(e =>
                {
                    if (e.nodeType != ExprType.Call)
                        return e;
                    var ce = (CallExpr)e;
                    var args = ce.args;
                    if (ce.funcName == "LV" || ce.funcName == "LVG")
                    {
                        var fn = ce.funcName;
                        var sFirstArg = OPs.TryAsString(args[0], ctx);
                        if (sFirstArg == null)
                            throw new Exception(fn + "(...): expression can't be evaluated at compile time: " + args[0].ToString());
                        Expr nParts = null;
                        Expr timeArg = null;
                        Expr timeRng = null;
                        string sParamName = null;
                        string sKeyPrefix = null;
                        if (fn == "LV")
                        {
                            switch (args.Count)
                            {
                                case 1: timeArg = ConstExpr.Null; break;
                                case 2: timeArg = args[1]; break;
                                case 3: timeArg = timeRng = new ArrayExpr(args[1], args[2]); break;
                                case 4: nParts = args[3]; goto case 3;
                                default: throw new Exception(string.Format("LV(...) must be in one form of:\r\n•LV(param, time)\r\n•LV(param, timeA, timeB)\r\n•LV(param, timeA, timeB, nParts)\r\ninstead of {0}", ce));
                            }
                            if (timeRng != null)
                                sKeyPrefix = sFirstArg + '@';
                            sParamName = sFirstArg;
                        }
                        else if (fn == "LVG")
                        {
                            switch (args.Count)
                            {
                                case 3: timeArg = args[2]; break;
                                case 4: timeArg = timeRng = new ArrayExpr(args[2], args[3]); break;
                                case 5: nParts = args[4]; goto case 4;
                                default: throw new Exception(string.Format("LVG(...) must be in one form of:\r\n•LVG(group, param, time)\r\n•LVG(group, param, timeA, timeB)\r\n•LVG(group, param, timeA, timeB, nParts)\r\ninstead of {0}", ce));
                            }
                            var sSecondArg = OPs.TryAsString(args[1], ctx);
                            if (sSecondArg == null)
                                throw new Exception(fn + "(...): expression can't be evaluated at compile time: " + args[1].ToString());
                            sParamName = sSecondArg;
                            sKeyPrefix = '#' + sFirstArg + '@';
                        }
                        var sTimeExpr = timeArg.ToString();
                        if (sKeyPrefix != null)
                            sTimeExpr = sKeyPrefix + sTimeExpr;
                        LV_GrpInfo info;
                        if (!dict.TryGetValue(sTimeExpr, out info))
                        {
                            info = new LV_GrpInfo()
                            {
                                timeExpr = timeArg,
                                resExpr = new ReferenceExpr("#lv"),
                                prefixExpr = new ConstExpr(dict.Count.ToString() + '_'),
                                pname2expr = new Dictionary<string, LV_PrmInfo>()
                            };
                            dict.Add(sTimeExpr, info);
                        }
                        LV_PrmInfo lvri;
                        if (!info.pname2expr.TryGetValue(sParamName, out lvri))
                        {
                            if (!ValueInfo.IsDescriptor(sParamName))
                                throw new Exception("LV(...): parameter descriptor expected instead of " + args[0].ToString());
                            Expr prmNameRef = new ConstExpr(sParamName);
                            lvri = new LV_PrmInfo()
                            {
                                paramName = sParamName,
                                paramNameExpr = prmNameRef,
                                prefixedName = fullJoinKeys.Contains(sParamName) ? prmNameRef : new BinaryExpr(ExprType.Concat, info.prefixExpr, prmNameRef)
                            };
                            info.pname2expr.Add(sParamName, lvri);
                        }
                        var rawRes = new IndexExpr(info.resExpr, lvri.prefixedName);
                        if (nParts == null)
                            return rawRes;
                        else return new CallExpr(FuncDefs_Solver.SolverTimedTabulation, rawRes, args[1], args[2], nParts);
                    }
                    else return e;
                });
                var resLst = new List<Expr>(blockExprs.Count);
                foreach (var expr in blockExprs)
                    resLst.Add(modify(expr));
                //
                var atTimeExpr = new ReferenceExpr("AT_TIME__XT");
                var exprTimeA = new ReferenceExpr(nameof(ValueInfo.A_TIME__XT));
                var exprTimeB = new ReferenceExpr(nameof(ValueInfo.B_TIME__XT));
                var lstJoinKeys = fullJoinKeys.Select(s => new ConstExpr(s)).ToArray();
                outInfo = new Dictionary<string, LV_OutInfo>(dict.Count);
                foreach (var t in dict.Values)
                {
                    var r = new LV_OutInfo();
                    var exprs = new List<Expr>(4);
                    var timeArr = t.timeExpr as ArrayExpr;
                    var extraInps = new List<Expr>(2);
                    if (timeArr != null)
                    {   // for time range
                        extraInps.Add(new ConstExpr('-' + atTimeExpr.name));
                        var teA = timeArr.args[0] as ReferenceExpr;
                        if (teA == null || teA.name != exprTimeA.name)
                            exprs.Add(let(exprTimeA, timeArr.args[0]));
                        var teB = timeArr.args[1] as ReferenceExpr;
                        if (teB == null || teB.name != exprTimeB.name)
                            exprs.Add(let(exprTimeB, timeArr.args[1]));
                    }
                    else
                    {   // for time slice or without time
                        extraInps.Add(new ConstExpr('-' + exprTimeA.name));
                        extraInps.Add(new ConstExpr('-' + exprTimeB.name));
                        if (t.timeExpr == ConstExpr.Null)
                            extraInps.Add(new ConstExpr('-' + atTimeExpr.name));
                        else
                        {
                            var teAT = t.timeExpr as ReferenceExpr;
                            if (teAT == null || teAT.name != atTimeExpr.name)
                                exprs.Add(let(atTimeExpr, t.timeExpr));
                        }
                    }
                    // prepare list of needed parameters
                    r.resultParams =
                        // key parameters at the begin of list
                        lstJoinKeys
                        // value parameters at the end
                        .Concat(
                            t.pname2expr.Values
                            .Where(lvri => !fullJoinKeys.Contains(lvri.paramName))
                            .Select(lvri => lvri.paramNameExpr)
                        ).ToArray();
                    {
                        var lstSolverArgs = new List<Expr>(2);
                        lstSolverArgs.Add(new ArrayExpr(extraInps));
                        lstSolverArgs.Add(new ArrayExpr(r.resultParams));
                        exprs.Add(new CallExpr(FuncDefs_Solver.ExprToExecutable, new CallExpr(FuncDefs_Solver.FindSolutionExpr, lstSolverArgs)));
                    }
                    r.loadingExpr = new CallExpr(FuncDefs_Core._block, exprs);
                    r.loadingExpr = new IndexExpr(r.loadingExpr, new ConstExpr(0));
                    if (timeArr != null)
                    {
                        r.loadingExpr = new CallExpr(FuncDefs_Solver.SolverIDictsGroupBy, r.loadingExpr, new ArrayExpr(lstJoinKeys));
                        //var loopVal = new ReferenceExpr("@r");
                        //var loopBody = new CallExpr("SolverIDictsGroupBy", loopVal, new ArrayExpr(lstJoinKeys));
                        //r.loadingExpr = new CallExpr("_ForEach", loopVal, r.loadingExpr, loopBody);
                    }
                    outInfo.Add(t.prefixExpr.value.ToString(), r);
                }
                return resLst;
            }
        }
    }
}
