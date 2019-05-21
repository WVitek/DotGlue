using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using W.Common;
using System.Text;

namespace W.Expressions.Sql
{
    public static partial class Impl
    {
        const string sMinTime = ":MIN_TIME"; // minimal time (low bound for possible time values)
        const string sATime = ":A_TIME"; // start time of interval
        const string sBTime = ":B_TIME"; // end time of interval
        const string sAtTime = ":AT_TIME"; // time of slice

        static Expr Cond_TimeIntervalHalfOpen(QueryTimeInfo startTime, string sATime, string sBTime)
        {   // sATime<startTime AND startTime<=sBTime
            var cond = BinaryExpr.Create(ExprType.LogicalAnd,
                new BinaryExpr(ExprType.LessThan, startTime.Time2Value(sATime), startTime.valueExpr),
                new BinaryExpr(ExprType.LessThanOrEqual, startTime.valueExpr, startTime.Time2Value(sBTime)));
            return cond;
        }

        static Expr Cond_TimeSlice(QueryTimeInfo startTime, string sMinTime, string sAtTime)
        {   // sMinTime<=startTime AND startTime<=sAtTime
            var cond = new BinaryExpr(ExprType.LogicalAnd,
                new BinaryExpr(ExprType.LessThanOrEqual, startTime.Time2Value(sMinTime), startTime.valueExpr),
                new BinaryExpr(ExprType.LessThanOrEqual, startTime.valueExpr, startTime.Time2Value(sAtTime))
                );
            return cond;
        }

        static Expr Cond_TimeInterval(QueryTimeInfo startTime, QueryTimeInfo endTime, string sMinTime)
        {   //  sMinTime<=startTime AND startTime<=sBTime AND (endTime is null OR sATime<endTime) 
            var cond_min = new BinaryExpr(ExprType.LessThanOrEqual, startTime.Time2Value(sMinTime), startTime.valueExpr);
            var cond_0 = new BinaryExpr(ExprType.LessThanOrEqual, startTime.valueExpr, startTime.Time2Value(sBTime));
            var cond_1 = new BinaryExpr(ExprType.LogicalOr,
                    new SequenceExpr(endTime.valueExpr, new ReferenceExpr("IS NULL")),
                    new BinaryExpr(ExprType.LessThan, endTime.Time2Value(sATime), endTime.valueExpr)
                );
            var cond = BinaryExpr.Create(ExprType.LogicalAnd, cond_min, cond_0, cond_1);
            return cond;
        }

        static Expr Cond_TimeSlice(QueryTimeInfo startTime, QueryTimeInfo endTime, string sMinTime)
        {   // sMinTime<=startTime AND startTime<=sAtTime AND (endTime is null OR sAtTime<=endTime)
            var cond_min = new BinaryExpr(ExprType.LessThanOrEqual, startTime.Time2Value(sMinTime), startTime.valueExpr);
            var cond_0 = new BinaryExpr(ExprType.LessThanOrEqual, startTime.valueExpr, startTime.Time2Value(sAtTime));
            var cond_1 = new BinaryExpr(ExprType.LogicalOr,
                new SequenceExpr(endTime.valueExpr, new ReferenceExpr("IS NULL")),
                new BinaryExpr(ExprType.LessThan, endTime.Time2Value(sAtTime), endTime.valueExpr)
                );
            var cond = BinaryExpr.Create(ExprType.LogicalAnd, cond_min, cond_0, cond_1);
            return cond;
        }

        public delegate T SqlFuncDefAction<T>(string funcNamePrefix, int actualityInDays, string queryText, bool arrayResults, IDictionary<string, object> xtraAttrs);

        static readonly string[] StrEmpty = new string[0];

        static Dictionary<string, object> ParseAttrs(IEnumerable<string> comments, Generator.Ctx ctx, params string[] firstLineDefaultKeys)
        {
            bool firstLine = true;
            var lstDescr = new List<string>();
            Dictionary<string, object> attrs = null;
            int iDefaultKey = (firstLineDefaultKeys.Length > 0) ? 0 : -1;
            foreach (var txt in comments)
            {
                if ((firstLineDefaultKeys.Length == 0 || !firstLine) && !txt.Contains("="))
                {
                    if (txt.Trim().Length == 0)
                        lstDescr.Clear();
                    else
                        lstDescr.Add(txt);
                    continue; // do not parse simple comment lines
                }
                foreach (Expr attr in Parser.ParseSequence(txt, comparison: StringComparison.InvariantCulture))
                {
                    string attrName;
                    Expr attrValue;
                    if (attr.nodeType == ExprType.Equal)
                    {   // named attribute
                        var be = (BinaryExpr)attr;
                        if (be.left.nodeType == ExprType.Reference)
                            attrName = Convert.ToString(be.left);
                        else
                        {
                            var name = Generator.Generate(be.left, ctx);
                            if (OPs.KindOf(name) != ValueKind.Const)
                                ctx.Error("ParseAttrs: attribute name must be constant\t" + be.left.ToString());
                            attrName = Convert.ToString(name);
                        }
                        attrValue = be.right;
                    }
                    else if (firstLine && iDefaultKey >= 0)
                    {   // unnamed attributes possible in first line
                        attrs = attrs ?? new Dictionary<string, object>();
                        attrs.Add(firstLineDefaultKeys[iDefaultKey], attr);
                        if (++iDefaultKey >= firstLineDefaultKeys.Length)
                            iDefaultKey = -1;
                        continue;
                    }
                    else break; // it is simple comment to the end of line?
                    if (attrValue.nodeType != ExprType.Constant)
                        attrValue = new CallExpr(FuncDefs_Core._block, attrValue);
                    var value = Generator.Generate(attrValue, ctx);
                    if (OPs.KindOf(value) != ValueKind.Const)
                        ctx.Error(string.Format("ParseAttrs: attribute value must be constant\t{0}={1}", attrName, attrValue));
                    if (attrName != null)
                    {
                        attrs = attrs ?? new Dictionary<string, object>();
                        if (attrs.TryGetValue(attrName, out var prev))
                        {
                            var lst = prev as IList;
                            if (lst == null)
                            {
                                lst = new ArrayList();
                                attrs[attrName] = lst;
                                lst.Add(prev);
                            }
                            lst.Add(value);
                        }
                        else attrs.Add(attrName, value);
                    }
                }
                firstLine = false;
            }
            if (lstDescr.Count > 0)
            {
                attrs = attrs ?? new Dictionary<string, object>();
                attrs.Add(nameof(Attr.description), lstDescr);
            }
            return attrs;
        }

        public static IEnumerable<T> ParseSqlFuncs<T>(string sqlFileName, SqlFuncDefAction<T> func, Generator.Ctx ctx)
        {
            var seps = new char[] { '\t', ' ' };
            using (var rdr = new System.IO.StreamReader(sqlFileName))
            {
                var uniqFuncName = new Dictionary<string, bool>();
                var queryText = new System.Text.StringBuilder();
                Dictionary<string, object> extraAttrs = null;
                var innerAttrs = new List<Dictionary<string, object>>();
                var comments = new List<string>();
                int lineNumber = 0;
                int lineNumberFirst = -1;
                while (true)
                {
                    var line = rdr.ReadLine();
                    lineNumber++;
                    if (line == null || line.StartsWith(";"))
                    {   // "end of query" line
                        if (queryText.Length > 0)
                        {
                            string funcPrefix = null;
                            int actuality = -1; // 36525;

                            if (extraAttrs == null)
                                extraAttrs = new Dictionary<string, object>();
                            else
                                // Scan function attributes
                                foreach (var attr in extraAttrs)
                                {
                                    switch (attr.Key)
                                    {
                                        case nameof(Attr.funcPrefix):
                                            funcPrefix = attr.Value.ToString();
                                            break;
                                        case nameof(Attr.actuality):
                                            var expr = attr.Value as Expr;
                                            if (expr != null)
                                                actuality = Convert.ToInt32(Generator.Generate(expr, ctx));
                                            else
                                                actuality = Convert.ToInt32(attr.Value);
                                            break;
                                    }
                                }

                            bool arrayResults = false;
                            if (funcPrefix == null)
                            {
                                funcPrefix = "QueryAtLn" + lineNumberFirst.ToString();
                                extraAttrs[nameof(Attr.funcPrefix)] = funcPrefix;
                            }
                            else if (funcPrefix.EndsWith("[]"))
                            {
                                arrayResults = true;
                                funcPrefix = funcPrefix.Substring(0, funcPrefix.Length - 2);
                                extraAttrs[nameof(Attr.arrayResults)] = true;
                            }
                            if (actuality < 0)
                            {
                                actuality = 36525;
                                extraAttrs[nameof(Attr.actuality)] = actuality;
                            }

                            if (innerAttrs != null)
                                extraAttrs[nameof(Attr.innerAttrs)] = innerAttrs.ToArray();

                            try { uniqFuncName.Add(funcPrefix, true); }
                            catch (ArgumentException) { ctx.Error("ParseSqlFuncs: function prefix is not unique\t" + funcPrefix); }
                            yield return func(funcPrefix, actuality, queryText.ToString(), arrayResults, extraAttrs);
                        }
                        lineNumberFirst = -1;
                        extraAttrs = null;
                        innerAttrs.Clear();
                        queryText.Clear();
                        if (line == null)
                            break;
                        continue;
                    }

                    if (line != null)
                    {
                        if (queryText.Length == 0)
                            if (lineNumberFirst < 0)
                                lineNumberFirst = lineNumber;

                        if (line.StartsWith("--"))
                        {   // comment line
                            comments.Add(line.Substring(2));
                            continue;
                        }
                    }

                    // line is null or not comment
                    if (queryText.Length == 0)
                        // first line of query, parse header comments into function attributes
                        extraAttrs = ParseAttrs(comments, ctx, nameof(Attr.funcPrefix), nameof(Attr.actuality));
                    else
                        // not first line of query, parse inner comments into inner attributes
                        innerAttrs.Add(ParseAttrs(comments, ctx));

                    comments.Clear();

                    if (line != null)
                    {
                        // line of query
                        line.Trim();
                        if (line.Length > 0)
                            queryText.AppendLine(line);
                    }
                }
            }
        }

        static QueryTemplate Values(this SqlExpr sqlExpr, bool arrayResults, string connName, out string[] inputs, out string[] outputs)
        {
            var allColumns = sqlExpr.resNdx.Keys;
            string[] colsNames;
            IEnumerable<AliasExpr> colsExprs;
            Expr[] orderBy;
            int i;
            if (sqlExpr.resNdx.TryGetValue(nameof(INS_OUTS_SEPARATOR), out i))
            {
                inputs = allColumns.Take(i).ToArray();
                outputs = allColumns.Skip(i + 1).ToArray();
                colsNames = inputs.Union(outputs).ToArray();
                var rs = sqlExpr.results;
                colsExprs = rs.Take(i).Union(rs.Skip(i + 1));
                orderBy = rs.Take(i).Select(e => e.right).ToArray();
            }
            else
            {
                inputs = new string[0];
                outputs = allColumns.ToArray();
                colsNames = allColumns.ToArray();
                colsExprs = sqlExpr.results;
                orderBy = null;
            }
            var expr = sqlExpr.CreateQueryExpr(new ReferenceExpr("1=1{0}"), null, orderBy, null, colsNames);
            var lstColsExprs = colsExprs
                //.Where(a => a.alias != nameof(START_TIME) && a.alias != nameof(END_TIME))
                .Select(ae => ae.expr.ToString()).ToArray();
            return QueryTemplate.Get(colsNames, lstColsExprs, null, expr.ToString(), sqlExpr, arrayResults, connName);
        }

        static bool TryExtractTimeFormatString(Expr timeExpr, out Expr fieldExpr, out string timeFmtStr)
        {
            fieldExpr = timeExpr;
            timeFmtStr = null;
            if (timeExpr.nodeType != ExprType.Call)
                return false;
            Expr field = null;
            ConstExpr timeFmt = ConstExpr.Null;
            bool withToDate = timeExpr.Traverse(e =>
            {
                if (e.nodeType != ExprType.Call)
                    return false;
                var ce = e as CallExpr;
                if (ce == null || ce.funcName.ToUpperInvariant() != "TO_DATE" || ce.args.Count < 2)
                    return false;
                field = ce.args[0];
                timeFmt = (ConstExpr)ce.args[1];
                return true;
            }).Any(f => f);
            if (!withToDate)
                return false;
            fieldExpr = field;
            timeFmtStr = Convert.ToString(timeFmt.value);
            return true;
        }

        struct QueryTimeInfo
        {
            public Expr timeExpr;
            public Expr valueExpr;
            public string valueFmt;
            public static QueryTimeInfo Get(Expr timeExpr)
            {
                var r = new QueryTimeInfo();
                if (timeExpr == null)
                    return r;
                r.timeExpr = timeExpr;
                TryExtractTimeFormatString(timeExpr, out r.valueExpr, out r.valueFmt);
                return r;
            }

            public Expr Time2Value(Expr time)
            {
                if (valueFmt == null)
                    return time;
                return new CallExpr("TO_CHAR", time, new ConstExpr(valueFmt));
            }
            public Expr Time2Value(string sTimeRef)
            {
                Expr time = new ReferenceExpr(sTimeRef);
                if (valueFmt == null)
                    return time;
                return new CallExpr("TO_CHAR", time, new ConstExpr(valueFmt));
            }
        }

        static readonly ReferenceExpr refStartTime = new ReferenceExpr(nameof(START_TIME));

        static QueryTemplate ValuesTimed(SqlExpr sqlExpr, QueryKind queryKind, bool arrayResults, string connName)
        {
            var rn = sqlExpr.resNdx;
            var rf = sqlExpr.resFields;
            var rs = sqlExpr.results;
            var idValueExpr = rf[0]; // first column of query must be an ID of subject
            var idAlias = rs[0];
            int iStartTimeField = rn[refStartTime.name];
            var startTime = QueryTimeInfo.Get(rf[iStartTimeField]);
            System.Diagnostics.Trace.Assert(startTime.timeExpr != null, "START_TIME column is not found");
            QueryTimeInfo endTime;
            {
                int i;
                endTime = QueryTimeInfo.Get(rn.TryGetValue(nameof(END_TIME), out i) ? rf[i] : null);
            }
            var orderBy = new Expr[] { idAlias.right, refStartTime };
            Expr expr;
            if (endTime.timeExpr != null && queryKind != QueryKind.GetSchemaOnly)
            {   // value has two timestamps - START_TIME and END_TIME
                Expr cond;
                switch (queryKind)
                {
                    case QueryKind.TimeInterval:
                        cond = Cond_TimeInterval(startTime, endTime, sMinTime); break;
                    case QueryKind.TimeRawInterval:
                        cond = Cond_TimeInterval(startTime, endTime, sATime); break;
                    case QueryKind.TimeSlice:
                        cond = Cond_TimeSlice(startTime, endTime, sMinTime); break;
                    default:
                        throw new NotSupportedException($"Unsupported TimedQueryKind value: {queryKind.ToString()}");
                }
                cond = new SequenceExpr(cond, new ReferenceExpr("{0}"));
                expr = sqlExpr.CreateQueryExpr(cond, null, orderBy);
            }
            else
            {   // value has only one timestamp - START_TIME
                Expr cond_aggr = null, cond_simp = null;
                switch (queryKind)
                {
                    case QueryKind.GetSchemaOnly:
                        break;
                    case QueryKind.TimeRawInterval:
                        cond_simp = Cond_TimeIntervalHalfOpen(startTime, sATime, sBTime); break;
                    case QueryKind.TimeInterval:
                        cond_aggr = Cond_TimeSlice(startTime, sMinTime, sATime);
                        cond_simp = Cond_TimeIntervalHalfOpen(startTime, sATime, sBTime); break;
                    case QueryKind.TimeSlice:
                        cond_aggr = Cond_TimeSlice(startTime, sMinTime, sAtTime); break;
                    default:
                        throw new NotSupportedException("Unsupported TimedQueryKind value");
                }
                if (cond_aggr != null)
                {
                    var exprKeep = new CallExpr("KEEP", new SequenceExpr(new ReferenceExpr("DENSE_RANK LAST ORDER BY"), startTime.valueExpr));
                    cond_aggr = new SequenceExpr(cond_aggr, new ReferenceExpr("{0}"));
                    expr = sqlExpr.CreateQueryExpr(cond_aggr, idAlias, orderBy,
                    src =>
                    {
                        Expr res;
                        if (src.expr.Traverse(e => e)
                            .OfType<CallExpr>()
                            .Select(ce => ce.funcName.ToUpperInvariant())
                            .Where(fn => fn == "MIN" || fn == "MAX" || fn == "SUM" || fn == "AVG" || fn == "COUNT").Any())
                        {
                            res = src.expr;
                        }
                        else
                            res = new CallExpr("MAX", src.expr);
                        if (src.alias != nameof(START_TIME))
                            res = new SequenceExpr(res, exprKeep);
                        return new AliasExpr(res, src.right);
                    });
                }
                else expr = null;
                if (cond_simp != null)
                {
                    cond_simp = new SequenceExpr(cond_simp, new ReferenceExpr("{0}"));
                    var expr2 = sqlExpr.CreateQueryExpr(cond_simp, null, orderBy);
                    if (expr == null)
                        expr = expr2;
                    else
                    {
                        //expr = new SequenceExpr(expr, new ReferenceExpr("UNION ALL"), expr2);
                        var sqlA = ((MultiExpr)expr).args.Cast<SqlSectionExpr>();
                        //var order = sqlA.FirstOrDefault(s => s.kind == SqlSectionExpr.Kind.OrderBy);
                        var bodyA = sqlA.Where(s => s.kind != SqlSectionExpr.Kind.OrderBy);
                        //var sqlB = ((MultiExpr)expr2).args.Cast<SqlSectionExpr>();
                        //var orderB = sqlB.FirstOrDefault(s => s.kind == SqlSectionExpr.Kind.OrderBy);
                        //var bodyB = sqlB.Where(s => s.kind != SqlSectionExpr.Kind.OrderBy);
                        var lst = new List<Expr>();
                        lst.AddRange(bodyA);
                        lst.Add(new ReferenceExpr("UNION ALL"));
                        lst.Add(expr2);
                        expr = new SequenceExpr(lst);
                    }
                }
                else if (expr == null)
                    expr = sqlExpr.CreateQueryExpr();
            }
            string[] qryVars = null;
            switch (queryKind)
            {
                case QueryKind.TimeRawInterval:
                    qryVars = new string[] { sMinTime, sATime, sBTime }; // sMinTime here is dummy (not used really and specified only for unification)
                    break;
                case QueryKind.TimeInterval:
                    qryVars = new string[] { sMinTime, sATime, sBTime };
                    break;
                case QueryKind.TimeSlice:
                    qryVars = new string[] { sMinTime, sAtTime };
                    break;
                case QueryKind.GetSchemaOnly:
                    qryVars = new string[0];
                    break;
            }
            return QueryTemplate.Get(
                rs.Select(x => x.alias).ToArray(),
                rs.Select(x => x.expr.ToString()).ToArray(),
                qryVars, expr.ToString(),
                sqlExpr, arrayResults, connName);
        }

        /// <summary>
        /// Create definitions of loading functions from specified SQL query
        /// </summary>
        /// <param name="funcNamesPrefix">Optional prefix for functions names. If null or empty, first table from 'FROM' clause is used</param>
        /// <param name="actualityInDays">Used to restrict the minimum queried timestamp // :MIN_TIME = :BEG_TIME-actualityInDays</param>
        /// <param name="queryText">SQL query text. Only some subset of SQL is supported
        /// (all sources in FROM cluase must have aliases, all fields in SELECT must be specified with source aliases, subqueries is not tested, etc)</param>
        /// <returns>Enumeration of pairs (func_name, loading function definition)</returns>
        internal static IEnumerable<FuncDef> FuncDefsForSql(Preprocessing.SqlFuncPreprocessingCtx c)
        {
            var sql = c.PostProc(SqlParse.Do(c.queryText, SqlExpr.Options.EmptyFromPossible));

            if (sql == null)
                yield break;

            var actuality = TimeSpan.FromDays(c.actualityInDays);
            if (string.IsNullOrEmpty(c.funcNamesPrefix))
                c.funcNamesPrefix = sql.sources[0].expr.ToString();
            // generate list of results
            bool timedQuery = false;
            bool withSeparator = false;
            ValueInfo[] resultsInfo;
            {
                int n = sql.results.Length;
                var lst = new List<ValueInfo>(n);
                for (int i = 0; i < n; i++)
                {
                    var d = sql.results[i].alias.ToUpperInvariant();
                    if (d == nameof(START_TIME))
                        timedQuery = timedQuery || i == 1;
                    else if (d == nameof(END_TIME) || d == nameof(END_TIME__DT))
                    {
                        //if (d.Length == nameof(END_TIME).Length)
                        //	timedQuery = timedQuery || i == 2;
                    }
                    else if (d == nameof(INS_OUTS_SEPARATOR))
                        withSeparator = true;
                    else
                    {
                        if (d.Length > 30)
                            throw new Generator.Exception($"SQL identifier too long (max 30, but {d.Length} chars in \"{d}\")");
                        var vi = ValueInfo.Create(d);
                        lst.Add(vi);
                        //var v = vi.ToString().ToUpperInvariant();
                        //if (v != d)
                        //    sql.results[i] = new AliasExpr(sql.results[i].expr, new ReferenceExpr(v));
                    }
                }
                resultsInfo = lst.ToArray();
            }
            #region Some query with INS_OUTS_SEPARATOR column
            if (withSeparator || !timedQuery)
            {   // separator column present
                string[] inputs, outputs;
                var qt = sql.Values(c.arrayResults, c.ldr.dbConnValueName, out inputs, out outputs);
                Fn func = (IList args) =>
                {
                    return (LazyAsync)(async ctx =>
                    {
                        var mq = qt.GetQuery(args);
                        if (mq == null)
                            return ValuesDictionary.Empties;
                        using (mq)
                        {
                            var res = await FuncDefs_DB.ExecQuery(ctx, mq.QueryText, c.ldr.dbConnValueName, c.arrayResults);
                            if (((IList)res).Count == 0)
                                res = ValuesDictionary.Empties;
                            return res;
                        }
                    });
                };
                var colsNames = qt.colsNames.Where(s => s != nameof(START_TIME) && s != nameof(END_TIME) && s != nameof(END_TIME__DT)).ToList();
                for (int i = inputs.Length - 1; i >= 0; i--)
                    if (!ValueInfo.IsID(inputs[i]))
                        colsNames.RemoveAt(i);
                var fd = new FuncDef(func, c.funcNamesPrefix/* + "_Values"*/, inputs.Length, inputs.Length,
                    ValueInfo.CreateManyInLocation(c.ldr.defaultLocationForValueInfo, inputs),
                    ValueInfo.CreateManyInLocation(c.ldr.defaultLocationForValueInfo, colsNames.ToArray()),
                    FuncFlags.Defaults, 0, 0, c.ldr.cachingExpiration, c.ldr.cacheSubdomain,
                    new Dictionary<string, object>(c.xtraAttrs));
                fd.xtraAttrs.Add(nameof(QueryTemplate), qt);
                yield return fd;
                yield break;
            }
            #endregion
            #region Range
            if ((c.ldr.forKinds & QueryKind.TimeInterval) != 0)
            {
                var qt = ValuesTimed(sql, QueryKind.TimeInterval, c.arrayResults, c.ldr.dbConnValueName);
                Fn func = (IList args) =>
                {
                    return (LazyAsync)(async ctx =>
                    {
                        var begTime = OPs.FromExcelDate(Convert.ToDouble(args[1]));
                        var endTime = OPs.FromExcelDate(Convert.ToDouble(args[2]));
                        var mq = qt.GetQuery(new object[] { args[0] }
                            , ToOraDateTime(begTime - actuality)
                            , ToOraDateTime(begTime)
                            , ToOraDateTime(endTime)
                            );
                        if (mq == null)
                            return ValuesDictionary.Empties;
                        var conn = (IDbConn)await ctx.GetValue(qt.connName);
                        var cmd = new SqlCommandData()
                        {
                            Kind = CommandKind.Query,
                            ConvertMultiResultsToLists = qt.arrayResults
                        };
                        using (mq)
                        {
                            cmd.SqlText = mq.QueryText;
                            var res = await conn.ExecCmd(cmd, ctx.Cancellation);
                            if (((IList)res).Count == 0)
                                res = ValuesDictionary.Empties;
                            return res;
                        }
                    });
                };
                var fd = new FuncDef(func, c.funcNamesPrefix + "_Range", 3, 3,
                    ValueInfo.CreateManyInLocation(c.ldr.defaultLocationForValueInfo, qt.colsNames[0], "A_TIME__XT", "B_TIME__XT"),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.ldr.cachingExpiration, c.ldr.cacheSubdomain,
                    new Dictionary<string, object>(c.xtraAttrs));
                fd.xtraAttrs.Add(nameof(QueryTemplate), qt);
                yield return fd;
            }
            #endregion
            #region Slice at AT_TIME
            if ((c.ldr.forKinds & QueryKind.TimeSlice) != 0)
            {
                var qt = ValuesTimed(sql, QueryKind.TimeSlice, c.arrayResults, c.ldr.dbConnValueName);
                Fn func = (IList args) =>
                {
                    return (LazyAsync)(async ctx =>
                    {
                        var lst = args[0] as IList;
                        if (lst != null && lst.Count == 0)
                            return lst;
                        bool range = args.Count > 2;
                        var begTime = OPs.FromExcelDate(Convert.ToDouble(range ? args[2] : args[1]));
                        var minTime = range ? OPs.FromExcelDate(Convert.ToDouble(args[1])) : begTime - actuality;
                        var mq = qt.GetQuery(new object[] { args[0] }, ToOraDateTime(minTime), ToOraDateTime(begTime));
                        if (mq == null)
                            return ValuesDictionary.Empties;
                        var conn = (IDbConn)await ctx.GetValue(qt.connName);
                        var cmd = new SqlCommandData()
                        {
                            Kind = CommandKind.Query,
                            ConvertMultiResultsToLists = qt.arrayResults
                        };
                        using (mq)
                        {
                            cmd.SqlText = mq.QueryText;
                            var res = await conn.ExecCmd(cmd, ctx.Cancellation);
                            if (((IList)res).Count == 0)
                                res = ValuesDictionary.Empties;
                            return res;
                        }
                    });
                };
                var fd = new FuncDef(func, c.funcNamesPrefix + "_Slice", 2, 2,
                    ValueInfo.CreateManyInLocation(c.ldr.defaultLocationForValueInfo, qt.colsNames[0], "AT_TIME__XT"),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.ldr.cachingExpiration, c.ldr.cacheSubdomain, new Dictionary<string, object>(c.xtraAttrs));
                fd.xtraAttrs.Add(nameof(QueryTemplate), qt);
                yield return fd;
            }
            #endregion
            #region Raw interval // START_TIME in range MIN_TIME .. MAX_TIME
            if ((c.ldr.forKinds & QueryKind.TimeRawInterval) != 0)
            {
                var qt = ValuesTimed(sql, QueryKind.TimeRawInterval, c.arrayResults, c.ldr.dbConnValueName);
                Fn func = (IList args) =>
                {
                    return (LazyAsync)(async ctx =>
                    {
                        var begTime = OPs.FromExcelDate(Convert.ToDouble(args[1]));
                        var endTime = OPs.FromExcelDate(Convert.ToDouble(args[2]));
                        var mq = qt.GetQuery(new object[] { args[0] }
                            , ToOraDateTime(begTime)
                            , ToOraDateTime(endTime)
                            );
                        if (mq == null)
                            return ValuesDictionary.Empties;
                        var conn = (IDbConn)await ctx.GetValue(qt.connName);
                        var cmd = new SqlCommandData()
                        {
                            Kind = CommandKind.Query,
                            ConvertMultiResultsToLists = qt.arrayResults
                        };
                        using (mq)
                        {
                            cmd.SqlText = mq.QueryText;
                            var res = await conn.ExecCmd(cmd, ctx.Cancellation);
                            if (((IList)res).Count == 0)
                                res = ValuesDictionary.Empties;
                            return res;
                        }
                    });
                };
                var fd = new FuncDef(func, c.funcNamesPrefix + "_Raw", 3, 3,
                    ValueInfo.CreateManyInLocation(c.ldr.defaultLocationForValueInfo, qt.colsNames[0], "MIN_TIME__XT", "MAX_TIME__XT"),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.ldr.cachingExpiration, c.ldr.cacheSubdomain,
                    new Dictionary<string, object>(c.xtraAttrs));
                fd.xtraAttrs.Add(nameof(QueryTemplate), qt);
                yield return fd;
            }
            #endregion
        }

        public static string ToOraDate(DateTime date)
        {
            return string.Format("TO_DATE('{0:}','YYYY-MM-DD')", date.ToString("yyyy-MM-dd"));
        }

        public static string ToOraDateTime(DateTime dt)
        {
            return string.Format("TO_DATE('{0}','YYYY-MM-DD HH24:MI:SS')", dt.ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }
}