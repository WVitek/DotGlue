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

        public delegate T SqlFuncDefAction<T>(string funcNamePrefix, int actualityInDays, string queryText, bool arrayResults, IDictionary<Attr.Tbl, object> xtraAttrs);

        static readonly string[] StrEmpty = new string[0];

        static Dictionary<TAttr, object> ParseAttrs<TAttr>(Generator.Ctx ctx, IEnumerable<string> comments,
            params TAttr[] firstLineDefaultKeys
        )
            where TAttr : struct
        {
            bool firstLine = true;
            var lstDescr = new List<string>();
            Dictionary<TAttr, object> attrs = null;
            int iDefaultKey = (firstLineDefaultKeys.Length > 0) ? 0 : -1;
            foreach (var txt in comments)
            {
                bool isKeyValueComment;
                {
                    int iEqualSign = txt.IndexOf('=');
                    if (iEqualSign > 0)
                        isKeyValueComment = !Enumerable.Range(0, iEqualSign - 1).Any(i => char.IsWhiteSpace(txt[i]));
                    else
                        isKeyValueComment = false;
                }
                if ((firstLineDefaultKeys.Length == 0 || !firstLine) && !isKeyValueComment)
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
                        attrs = attrs ?? new Dictionary<TAttr, object>();
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
                        if (!Enum.TryParse<TAttr>(attrName, out var attrKey))
                            throw new Generator.Exception($"Unrecognized attribute name, {attrName}={attrValue} // enum {typeof(TAttr)}");
                        attrs = attrs ?? new Dictionary<TAttr, object>();
                        Attr.Add(attrs, attrKey, value, false);
                    }
                }
                firstLine = false;
            }
            if (lstDescr.Count > 0)
            {
                attrs = attrs ?? new Dictionary<TAttr, object>();
                if (!Enum.TryParse<TAttr>(nameof(Attr.Tbl.Description), out var attrDescription))
                    throw new Generator.Exception($"ParseAttrs: enum '{typeof(TAttr)}' does not contains value named '{nameof(Attr.Tbl.Description)}'");
                attrs.Add(attrDescription, lstDescr);
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
                Dictionary<Attr.Tbl, object> extraAttrs = null;
                var innerAttrs = new List<Dictionary<Attr.Col, object>>();
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
                            int actuality = -1; // negative to use defaultActualityDays value;

                            if (extraAttrs != null)
                                // Scan function attributes
                                foreach (var attr in extraAttrs)
                                    switch (attr.Key)
                                    {
                                        case Attr.Tbl.FuncPrefix:
                                            funcPrefix = attr.Value.ToString();
                                            break;
                                        case Attr.Tbl.ActualityDays:
                                            actuality = Convert.ToInt32(attr.Value);
                                            break;
                                        case Attr.Tbl._columns_attrs:
                                            throw new Generator.Exception($"Attribute name '{attr.Key}' is reserved for inner purposes");
                                    }
                            else
                                extraAttrs = new Dictionary<Attr.Tbl, object>();

                            bool arrayResults = false;
                            if (funcPrefix == null)
                            {
                                funcPrefix = "QueryAtLn" + lineNumberFirst.ToString();
                                extraAttrs[Attr.Tbl.FuncPrefix] = funcPrefix;
                            }
                            else if (funcPrefix.EndsWith("[]"))
                            {
                                arrayResults = true;
                                funcPrefix = funcPrefix.Substring(0, funcPrefix.Length - 2);
                                extraAttrs[Attr.Tbl.ArrayResults] = true;
                            }
                            //if (actuality < 0)
                            //{
                            //    actuality = Attr.defaultActualityDays;
                            //    extraAttrs[Attr.Tbl.ActualityDays] = actuality;
                            //}

                            if (innerAttrs != null)
                                extraAttrs[Attr.Tbl._columns_attrs] = innerAttrs.ToArray();

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

                    if (line.Length == 0)
                        continue;

                    if (line.StartsWith("--"))
                    {   // comment line
                        if (line.StartsWith("----"))
                            // skip commented comments :)
                            continue;
                        if (line.Length == 2)
                            // empty comment line clear previous block of comments
                            comments.Clear();
                        else
                            // akkumulate comments
                            comments.Add(line.Substring(2));
                        continue;
                    }

                    // remember first line of query
                    if (queryText.Length == 0)
                        if (lineNumberFirst < 0)
                            lineNumberFirst = lineNumber;

                    // query line
                    if (queryText.Length == 0)
                        // first line of query, parse header comments into function attributes
                        extraAttrs = ParseAttrs(ctx, comments, Attr.Tbl.FuncPrefix, Attr.Tbl.ActualityDays);
                    else
                        // not first line of query, parse inner comments into inner attributes
                        innerAttrs.Add(ParseAttrs<Attr.Col>(ctx, comments));

                    comments.Clear();

                    // akkumulate lines of query
                    line.Trim();
                    if (line.Length > 0)
                        queryText.AppendLine(line);
                }
            }
        }

        static QueryTemplate SqlQueryNonTimed(this SqlExpr sqlExpr, bool arrayResults, string connName, out string[] inputs, out string[] outputs)
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

        static QueryTemplate SqlCommandInsert(this SqlExpr sql, string connName, string defaultLocation, out string[] outputs)
        {
            var secFrom = sql[SqlSectionExpr.Kind.From];

            outputs = null;

            if (secFrom == null || secFrom.args.Count > 1 || !(secFrom.args[0] is ReferenceExpr reTable))
                // don't generate INSERT for multiple tables or for none
                return null;

            if (
                sql[SqlSectionExpr.Kind.GroupBy] != null ||
                sql[SqlSectionExpr.Kind.OrderBy] != null ||
                sql[SqlSectionExpr.Kind.Where] != null
                )
                // don't generate INSERT from complex query definition
                return null;

            var colsNames = new List<string>();
            var colsExprs = new List<string>();

            var sb = new StringBuilder($"INSERT INTO {secFrom.args[0]} (");

            {
                bool firstCol = true;
                foreach (var colExpr in sql[SqlSectionExpr.Kind.Select].args)
                {
                    if (!(colExpr is AliasExpr ae))
                        return null;
                    if (firstCol)
                        firstCol = false;
                    else
                        sb.Append(',');
                    if (ae.expr is ReferenceExpr re)
                    {
                        if (string.Compare(ae.alias, nameof(INS_OUTS_SEPARATOR)) == 0)
                            continue;
                        colsNames.Add(ae.alias);
                        colsExprs.Add(re.name);
                        sb.Append(re.name);
                    }
                    else
                        return null;
                }
                sb.Append(") VALUES ");
            }
            outputs = new string[] { $"{reTable.name}_OBJ_{defaultLocation}_RowsInserted" };
            var qt = QueryTemplate.Get(colsNames.ToArray(), colsExprs.ToArray(), null, sb.ToString(), sql, false, connName);
            return qt;
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
                if (ce == null || ce.args.Count < 2 || string.Compare(ce.funcName, "TO_DATE", StringComparison.OrdinalIgnoreCase) != 0)
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

        static QueryTemplate SqlQueryTimed(SqlExpr sqlExpr, DbFuncType queryKind, bool arrayResults, string connName)
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
            if (endTime.timeExpr != null && queryKind != DbFuncType.GetSchemaOnly)
            {   // value has two timestamps - START_TIME and END_TIME
                Expr cond;
                switch (queryKind)
                {
                    case DbFuncType.TimeInterval:
                        cond = Cond_TimeInterval(startTime, endTime, sMinTime); break;
                    case DbFuncType.TimeRawInterval:
                        cond = Cond_TimeInterval(startTime, endTime, sATime); break;
                    case DbFuncType.TimeSlice:
                        cond = Cond_TimeSlice(startTime, endTime, sMinTime); break;
                    default:
                        throw new NotSupportedException($"Unsupported DbFuncType value: {queryKind.ToString()}");
                }
                cond = new SequenceExpr(cond, new ReferenceExpr("{0}"));
                expr = sqlExpr.CreateQueryExpr(cond, null, orderBy);
            }
            else
            {   // value has only one timestamp - START_TIME
                Expr cond_aggr = null, cond_simp = null;
                switch (queryKind)
                {
                    case DbFuncType.GetSchemaOnly:
                        break;
                    case DbFuncType.TimeRawInterval:
                        cond_simp = Cond_TimeIntervalHalfOpen(startTime, sATime, sBTime); break;
                    case DbFuncType.TimeInterval:
                        cond_aggr = Cond_TimeSlice(startTime, sMinTime, sATime);
                        cond_simp = Cond_TimeIntervalHalfOpen(startTime, sATime, sBTime); break;
                    case DbFuncType.TimeSlice:
                        cond_aggr = Cond_TimeSlice(startTime, sMinTime, sAtTime); break;
                    default:
                        throw new NotSupportedException($"Unsupported DbFuncType value: {queryKind.ToString()}");
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
                case DbFuncType.TimeRawInterval:
                    qryVars = new string[] { sMinTime, sATime, sBTime }; // sMinTime here is dummy (not used really and specified only for unification)
                    break;
                case DbFuncType.TimeInterval:
                    qryVars = new string[] { sMinTime, sATime, sBTime };
                    break;
                case DbFuncType.TimeSlice:
                    qryVars = new string[] { sMinTime, sAtTime };
                    break;
                case DbFuncType.GetSchemaOnly:
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
            var sql = SqlParse.Do(c.queryText, SqlExpr.Options.EmptyFromPossible);
            sql = c.PostProc(sql);

            if (sql == null)
                yield break;

            var actuality = TimeSpan.FromDays((c.actualityInDays < 0) ? Attr.defaultActualityDays : c.actualityInDays);
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
                    var d = sql.results[i].alias;//.ToUpperInvariant();
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
                        //if (d.Length > 30)
                        //    throw new Generator.Exception($"SQL identifier too long (max 30, but {d.Length} chars in \"{d}\")");
                        var vi = ValueInfo.Create(d, defaultLocation: c.DefaultLocationForValueInfo);
                        int DL = vi.DescriptorLength();
                        if (DL > 30)
                            throw new Generator.Exception($"SQL identifier too long (max 30, but {DL} chars in \"{vi}\")");
                        lst.Add(vi);
                    }
                }
                resultsInfo = lst.ToArray();
            }

            var dbConnName = c.tblAttrs.GetString(Attr.Tbl.DbConnName) ?? c.ldr.dbConnValueName;

            #region Some query with INS_OUTS_SEPARATOR column
            if (withSeparator || !timedQuery || (c.ldr.forKinds & DbFuncType.Raw) != 0)
            {   // separator column present
                string[] inputs, outputs;
                var qt = SqlQueryNonTimed(sql, c.arrayResults, dbConnName, out inputs, out outputs);
                Fn func = FuncNonTimedQuery(qt);
                var colsNames = qt.colsNames.Where(s => s != nameof(START_TIME) && s != nameof(END_TIME) && s != nameof(END_TIME__DT)).ToList();
                for (int i = inputs.Length - 1; i >= 0; i--)
                    if (!ValueInfo.IsID(inputs[i]))
                        colsNames.RemoveAt(i);
                var fd = new FuncDef(func, c.funcNamesPrefix/* + "_Values"*/, inputs.Length, inputs.Length,
                    ValueInfo.CreateMany(inputs),
                    ValueInfo.CreateMany(colsNames.ToArray()),
                    FuncFlags.Defaults, 0, 0, c.ldr.cachingExpiration, c.ldr.cacheSubdomain,
                    c.tblAttrs.ToDictionary(p => p.Key.ToString(), p => p.Value)
                    );
                fd.xtraAttrs.Add(nameof(QueryTemplate), qt);
                yield return fd;
                yield break;
            }
            #endregion
            #region Range
            if ((c.ldr.forKinds & DbFuncType.TimeInterval) != 0)
            {
                var qt = SqlQueryTimed(sql, DbFuncType.TimeInterval, c.arrayResults, dbConnName);
                Fn func = FuncTimedRangeQuery(actuality, qt);
                var fd = new FuncDef(func, c.funcNamesPrefix + "_Range", 3, 3,
                    ValueInfo.CreateMany(qt.colsNames[0], nameof(ValueInfo.A_TIME__XT), nameof(ValueInfo.B_TIME__XT)),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.ldr.cachingExpiration, c.ldr.cacheSubdomain,
                    c.tblAttrs.ToDictionary(p => p.Key.ToString(), p => p.Value)
                    );
                fd.xtraAttrs.Add(nameof(QueryTemplate), qt);
                yield return fd;
            }
            #endregion
            #region Slice at AT_TIME
            if ((c.ldr.forKinds & DbFuncType.TimeSlice) != 0)
            {
                var qt = SqlQueryTimed(sql, DbFuncType.TimeSlice, c.arrayResults, dbConnName);
                Fn func = FuncTimedSliceQuery(actuality, qt);
                var fd = new FuncDef(func, c.funcNamesPrefix + "_Slice", 2, 2,
                    ValueInfo.CreateMany(qt.colsNames[0], nameof(ValueInfo.At_TIME__XT)),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.ldr.cachingExpiration, c.ldr.cacheSubdomain,
                    c.tblAttrs.ToDictionary(p => p.Key.ToString(), p => p.Value)
                    );
                fd.xtraAttrs.Add(nameof(QueryTemplate), qt);
                yield return fd;
            }
            #endregion
            #region Raw interval // START_TIME in range MIN_TIME .. MAX_TIME
            if ((c.ldr.forKinds & DbFuncType.TimeRawInterval) != 0)
            {
                var qt = SqlQueryTimed(sql, DbFuncType.TimeRawInterval, c.arrayResults, dbConnName);
                Fn func = FuncRawIntervalQuery(qt);
                var fd = new FuncDef(func, c.funcNamesPrefix + "_Raw", 3, 3,
                    ValueInfo.CreateMany(qt.colsNames[0], "MIN_TIME__XT", "MAX_TIME__XT"),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.ldr.cachingExpiration, c.ldr.cacheSubdomain,
                    c.tblAttrs.ToDictionary(p => p.Key.ToString(), p => p.Value)
                    );
                fd.xtraAttrs.Add(nameof(QueryTemplate), qt);
                yield return fd;
            }
            #endregion
            #region Insert rows function
            if ((c.ldr.forKinds & DbFuncType.Insert) != 0)
            {
                var qt = SqlCommandInsert(sql, dbConnName, c.DefaultLocationForValueInfo, out var outputs);
                //todo
                //Fn func = FuncInsert(qt);
            }
            #endregion
        }

        private static Fn FuncRawIntervalQuery(QueryTemplate qt)
        {
            return (IList args) =>
            {
                return (LazyAsync)(async ctx =>
                {
                    var begTime = OPs.FromExcelDate(Convert.ToDouble(args[1]));
                    var endTime = OPs.FromExcelDate(Convert.ToDouble(args[2]));
                    var conn = (IDbConn)await ctx.GetValue(qt.connName);
                    var mq = qt.GetQuery(new object[] { args[0] }
                        , conn.dbms.TimeToSqlText(begTime)
                        , conn.dbms.TimeToSqlText(endTime)
                        );
                    if (mq == null)
                        return ValuesDictionary.Empties;
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
        }

        private static Fn FuncTimedSliceQuery(TimeSpan actuality, QueryTemplate qt)
        {
            return (IList args) =>
            {
                return (LazyAsync)(async ctx =>
                {
                    var lst = args[0] as IList;
                    if (lst != null && lst.Count == 0)
                        return lst;
                    bool range = args.Count > 2;
                    var begTime = OPs.FromExcelDate(Convert.ToDouble(range ? args[2] : args[1]));
                    var minTime = range ? OPs.FromExcelDate(Convert.ToDouble(args[1])) : begTime - actuality;
                    var conn = (IDbConn)await ctx.GetValue(qt.connName);
                    var mq = qt.GetQuery(new object[] { args[0] }, conn.dbms.TimeToSqlText(minTime), conn.dbms.TimeToSqlText(begTime));
                    if (mq == null)
                        return ValuesDictionary.Empties;
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
        }

        private static Fn FuncTimedRangeQuery(TimeSpan actuality, QueryTemplate qt)
        {
            return (IList args) =>
            {
                return (LazyAsync)(async ctx =>
                {
                    var begTime = OPs.FromExcelDate(Convert.ToDouble(args[1]));
                    var endTime = OPs.FromExcelDate(Convert.ToDouble(args[2]));
                    var conn = (IDbConn)await ctx.GetValue(qt.connName);
                    var mq = qt.GetQuery(new object[] { args[0] }
                        , conn.dbms.TimeToSqlText(begTime - actuality)
                        , conn.dbms.TimeToSqlText(begTime)
                        , conn.dbms.TimeToSqlText(endTime)
                        );
                    if (mq == null)
                        return ValuesDictionary.Empties;
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
        }

        private static Fn FuncNonTimedQuery(QueryTemplate qt)
        {
            return (IList args) =>
            {
                return (LazyAsync)(async ctx =>
                {
                    var mq = qt.GetQuery(args);
                    if (mq == null)
                        return ValuesDictionary.Empties;
                    using (mq)
                    {
                        var res = await FuncDefs_DB.ExecQuery(ctx, mq.QueryText, qt.connName, qt.arrayResults);
                        if (((IList)res).Count == 0)
                            res = ValuesDictionary.Empties;
                        return res;
                    }
                });
            };
        }

    }
}