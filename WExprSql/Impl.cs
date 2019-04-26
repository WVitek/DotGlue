using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Data;
using W.Common;


namespace W.Expressions.Sql
{
    public static class Diagnostics
    {
        public static System.Diagnostics.TraceSource Logger = W.Common.Trace.GetLogger("WExprSql");
    }

    public enum CommandKind { Query, GetSchema, NonQuery };


    /// <summary>
    /// "START_TIME" is an alias for start of time interval in that SQL-query row output values are actual
    /// Is a sign of time-specific data. Always used in WHERE-clause of time-dependent queries.
    /// </summary>
    public sealed class START_TIME { }

    /// <summary>
    /// Optional "END_TIME" is an alias for end of time interval in that SQL-query row output values are actual
    /// Used in WHERE-clause of time-dependent queries.
    /// </summary>
    public sealed class END_TIME { }

    /// <summary>
    /// Optional "END_TIME__DT" is an alias for end of time interval in that SQL-query row output values are actual.
    /// Not used (ignored) in WHERE-clause of time-dependent queries.
    /// </summary>
    public sealed class END_TIME__DT { }

    /// <summary>
    /// "INS_OUTS_SEPARATOR" is a mark used to split input and output values in time-independent SQL-query
    /// </summary>
    public sealed class INS_OUTS_SEPARATOR { }

    public class SqlCommandData
    {
        public struct Param
        {
            public string name;
            public DbType type;
            public object value;
        }
        public CommandKind Kind;
        public string SqlText;
        public List<Param> Params = new List<Param>();
        public int ArrayBindCount;
        public bool ConvertMultiResultsToLists;
        public bool BindByName;

        public void AddDateTime(string name, object dateTime)
        {
            Params.Add(new Param()
            {
                name = name,
                type = DbType.DateTime2,
                value = dateTime
            });
        }
        public void AddInt32(string name, object intValue)
        {
            Params.Add(new Param()
            {
                name = name,
                type = DbType.Int32,
                value = intValue
            });
        }
        public void AddInt64(string name, object longValue)
        {
            Params.Add(new Param()
            {
                name = name,
                type = DbType.Int64,
                value = longValue
            });
        }
        public void AddFloat(string name, object floatValue)
        {
            Params.Add(new Param()
            {
                name = name,
                type = DbType.Single,
                value = floatValue
            });
        }
        public void AddString(string name, object strValue)
        {
            Params.Add(new Param()
            {
                name = name,
                type = DbType.String,
                value = strValue
            });
        }
    }

    public interface IDbConn : IDisposable
    {
        Task<object> ExecCmd(SqlCommandData data, CancellationToken ct);
        Task<object> Commit(CancellationToken ct);
        Task<IDbConn> GrabConn(CancellationToken ct);
    }

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

        [Flags]
        public enum TimedQueryKind
        {
            None = 0,
            /// <summary>
            /// Values: one with timestamp between MIN and A, and all with timestamps between A and B (A not included)
            /// </summary>
            Interval = 1,
            /// <summary>
            /// One value with timestamp between MIN and AT
            /// </summary>
            Slice = 2,
            /// <summary>
            /// values with timestamp between A and B (A not included)
            /// </summary>
            RawInterval = 4,
            /// <summary>
            /// get columns types
            /// </summary>
            GetSchemaOnly = 8
        };

        public delegate T SqlFuncDefAction<T>(string funcNamePrefix, int actualityInDays, string queryText, bool arrayResults, IDictionary<string, object> xtraAttrs);

        public static IEnumerable<T> ParseSqlFuncs<T>(string sqlFileName, SqlFuncDefAction<T> func, Generator.Ctx ctx)
        {
            var seps = new char[] { '\t', ' ' };
            using (var rdr = new System.IO.StreamReader(sqlFileName))
            {
                var uniqFuncName = new Dictionary<string, bool>();
                var queryText = new System.Text.StringBuilder();
                var headerComments = new List<string>();
                int lineNumber = 0;
                int lineNumberFirst = -1;
                while (true)
                {
                    var line = rdr.ReadLine();
                    lineNumber++;
                    if (line == null)
                        yield break;
                    if (line.StartsWith(";"))
                    {   // "end of query" line
                        if (queryText.Length > 0 && headerComments.Count > 0)
                        {
                            string funcPrefix = null;
                            int actuality = -1; // 36525;
                            var xtraAttrs = new Dictionary<string, object>();

                            #region Parse header comments and found query attributes
                            bool firstLine = true;
                            foreach (var txt in headerComments)
                            {
                                if (!firstLine && !txt.Contains("="))
                                    continue; // do not parse simple comment lines
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
                                                ctx.Error("ParseSqlFuncs: attribute name must be constant\t" + be.left.ToString());
                                            attrName = Convert.ToString(name);
                                        }
                                        attrValue = be.right;
                                    }
                                    else if (firstLine)
                                    {   // unnamed attributes possible in first line
                                        if (funcPrefix == null)
                                        {
                                            funcPrefix = attr.ToString();
                                            xtraAttrs.Add("funcPrefix", funcPrefix);
                                            continue;
                                        }
                                        attrName = null;
                                        attrValue = attr;
                                    }
                                    else break; // it is simple comment to the end of line?
                                    if (attrValue.nodeType != ExprType.Constant)
                                        attrValue = new CallExpr(FuncDefs_Core._block, attrValue);
                                    var value = Generator.Generate(attrValue, ctx);
                                    if (OPs.KindOf(value) != ValueKind.Const)
                                        ctx.Error(string.Format("ParseSqlFuncs: attribute value must be constant\t{0}={1}", attrName, attrValue));
                                    switch (attrName)
                                    {
                                        case null:
                                            if (firstLine && actuality < 0) { attrName = "actuality"; actuality = Convert.ToInt32(value); }
                                            break;
                                        case "funcPrefix":
                                            funcPrefix = Convert.ToString(attr);
                                            break;
                                        case "actuality":
                                            actuality = Convert.ToInt32(value);
                                            break;
                                    }
                                    if (attrName != null)
                                        xtraAttrs.Add(attrName, value);
                                }
                                firstLine = false;
                            }
                            #endregion
                            bool arrayResults = false;
                            if (funcPrefix == null)
                            {
                                funcPrefix = "QueryAtLn" + lineNumber.ToString();
                                xtraAttrs["funcPrefix"] = funcPrefix;
                            }
                            else if (funcPrefix.EndsWith("[]"))
                            {
                                arrayResults = true;
                                funcPrefix = funcPrefix.Substring(0, funcPrefix.Length - 2);
                                xtraAttrs["arrayResults"] = true;
                            }
                            if (actuality < 0)
                            {
                                actuality = 36525;
                                xtraAttrs["actuality"] = actuality;
                            }
                            try { uniqFuncName.Add(funcPrefix, true); }
                            catch (ArgumentException) { ctx.Error("ParseSqlFuncs: function prefix is not unique\t" + funcPrefix); }
                            yield return func(funcPrefix, actuality, queryText.ToString(), arrayResults, xtraAttrs);
                        }
                        lineNumberFirst = -1;
                        headerComments.Clear();
                        queryText.Clear();
                        continue;
                    }
                    if (line.StartsWith("--"))
                    {   // comment line
                        if (queryText.Length == 0)
                        {
                            if (lineNumberFirst < 0)
                                lineNumberFirst = lineNumber;
                            headerComments.Add(line.Substring(2));
                        }
                        continue;
                    }
                    // line of query
                    line.Trim();
                    if (line.Length > 0)
                        queryText.AppendLine(line);
                }
            }
        }

        public static SqlQueryTemplate Values(this SqlExpr sqlExpr, bool arrayResults, string connName, out string[] inputs, out string[] outputs)
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
            return SqlQueryTemplate.Get(colsNames, lstColsExprs, null, expr.ToString(), sqlExpr, arrayResults, connName);
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

        static SqlQueryTemplate ValuesTimed(SqlExpr sqlExpr, TimedQueryKind queryKind, bool arrayResults, string connName)
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
            if (endTime.timeExpr != null && queryKind != TimedQueryKind.GetSchemaOnly)
            {   // value has two timestamps - START_TIME and END_TIME
                Expr cond;
                switch (queryKind)
                {
                    case TimedQueryKind.Interval:
                        cond = Cond_TimeInterval(startTime, endTime, sMinTime); break;
                    case TimedQueryKind.RawInterval:
                        cond = Cond_TimeInterval(startTime, endTime, sATime); break;
                    case TimedQueryKind.Slice:
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
                    case TimedQueryKind.GetSchemaOnly:
                        break;
                    case TimedQueryKind.RawInterval:
                        cond_simp = Cond_TimeIntervalHalfOpen(startTime, sATime, sBTime); break;
                    case TimedQueryKind.Interval:
                        cond_aggr = Cond_TimeSlice(startTime, sMinTime, sATime);
                        cond_simp = Cond_TimeIntervalHalfOpen(startTime, sATime, sBTime); break;
                    case TimedQueryKind.Slice:
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
                case TimedQueryKind.RawInterval:
                    qryVars = new string[] { sMinTime, sATime, sBTime }; // sMinTime here is dummy (not used really and specified only for unification)
                    break;
                case TimedQueryKind.Interval:
                    qryVars = new string[] { sMinTime, sATime, sBTime };
                    break;
                case TimedQueryKind.Slice:
                    qryVars = new string[] { sMinTime, sAtTime };
                    break;
                case TimedQueryKind.GetSchemaOnly:
                    qryVars = new string[0];
                    break;
            }
            return SqlQueryTemplate.Get(
                rs.Select(x => x.alias).ToArray(),
                rs.Select(x => x.expr.ToString()).ToArray(),
                qryVars, expr.ToString(),
                sqlExpr, arrayResults, connName);
        }

        public const string sSqlQueryTemplate = "SqlQueryTemplate";

        public class SqlFuncDefinitionContext
        {
            public string funcNamesPrefix;
            public double actualityInDays;
            public string queryText;
            public string connName;
            public bool arrayResults;
            public IDictionary<string, object> xtraAttrs;
            public TimedQueryKind forKinds;
            public TimeSpan cachingExpiration;
            public string cacheSubdomain;
            public string defaultLocationForValueInfo;

            public Func<SqlFuncDefinitionContext, Expr, Expr> postProc;

            public Expr PostProc(Expr e)
            {
                if (postProc != null)
                    e = postProc(this, e);

                if (e.nodeType != ExprType.Alias)
                    return e;
                var r = ((AliasExpr)e).right as ReferenceExpr;
                if (r == null)
                    return e;
                var d = r.name.ToUpperInvariant();
                switch (d)
                {
                    case nameof(START_TIME):
                    case nameof(END_TIME):
                    case nameof(END_TIME__DT):
                    case nameof(INS_OUTS_SEPARATOR):
                        // skip special fields
                        return e;
                }
                var vi = ValueInfo.Create(d, true, defaultLocationForValueInfo);
                if (vi == null) return e;
                var v = vi.ToString();
                if (v == d)
                    return e;
                return new ReferenceExpr(v);
            }
        }

        /// <summary>
        /// Create definitions of loading functions from specified SQL query
        /// </summary>
        /// <param name="funcNamesPrefix">Optional prefix for functions names. If null or empty, first table from 'FROM' clause is used</param>
        /// <param name="actualityInDays">Used to restrict the minimum queried timestamp // :MIN_TIME = :BEG_TIME-actualityInDays</param>
        /// <param name="queryText">SQL query text. Only some subset of SQL is supported
        /// (all sources in FROM cluase must have aliases, all fields in SELECT must be specified with source aliases, subqueries is not tested, etc)</param>
        /// <returns>Enumeration of pairs (func_name, loading function definition)</returns>
        public static IEnumerable<FuncDef> DefineLoaderFuncs(SqlFuncDefinitionContext c)
        {
            var sql = SqlParse.Do(c.queryText, c.PostProc);

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
                var qt = sql.Values(c.arrayResults, c.connName, out inputs, out outputs);
                Fn func = (IList args) =>
                {
                    return (LazyAsync)(async ctx =>
                    {
                        var mq = qt.GetQuery(args);
                        if (mq == null)
                            return ValuesDictionary.Empties;
                        using (mq)
                        {
                            var res = await FuncDefs_DB.ExecQuery(ctx, mq.QueryText, c.connName, c.arrayResults);
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
                    ValueInfo.CreateManyInLocation(c.defaultLocationForValueInfo, inputs),
                    ValueInfo.CreateManyInLocation(c.defaultLocationForValueInfo, colsNames.ToArray()),
                    FuncFlags.Defaults, 0, 0, c.cachingExpiration, c.cacheSubdomain,
                    new Dictionary<string, object>(c.xtraAttrs));
                fd.xtraAttrs.Add(sSqlQueryTemplate, qt);
                yield return fd;
                yield break;
            }
            #endregion
            #region Range
            if ((c.forKinds & TimedQueryKind.Interval) != 0)
            {
                var qt = ValuesTimed(sql, TimedQueryKind.Interval, c.arrayResults, c.connName);
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
                    ValueInfo.CreateManyInLocation(c.defaultLocationForValueInfo, qt.colsNames[0], "A_TIME__XT", "B_TIME__XT"),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.cachingExpiration, c.cacheSubdomain,
                    new Dictionary<string, object>(c.xtraAttrs));
                fd.xtraAttrs.Add(sSqlQueryTemplate, qt);
                yield return fd;
            }
            #endregion
            #region Slice at AT_TIME
            if ((c.forKinds & TimedQueryKind.Slice) != 0)
            {
                var qt = ValuesTimed(sql, TimedQueryKind.Slice, c.arrayResults, c.connName);
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
                    ValueInfo.CreateManyInLocation(c.defaultLocationForValueInfo, qt.colsNames[0], "AT_TIME__XT"),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.cachingExpiration, c.cacheSubdomain, new Dictionary<string, object>(c.xtraAttrs));
                fd.xtraAttrs.Add(sSqlQueryTemplate, qt);
                yield return fd;
            }
            #endregion
            #region Raw interval // START_TIME in range MIN_TIME .. MAX_TIME
            if ((c.forKinds & TimedQueryKind.RawInterval) != 0)
            {
                var qt = ValuesTimed(sql, TimedQueryKind.RawInterval, c.arrayResults, c.connName);
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
                    ValueInfo.CreateManyInLocation(c.defaultLocationForValueInfo, qt.colsNames[0], "MIN_TIME__XT", "MAX_TIME__XT"),
                    resultsInfo, FuncFlags.Defaults, 0, 0, c.cachingExpiration, c.cacheSubdomain,
                    new Dictionary<string, object>(c.xtraAttrs));
                fd.xtraAttrs.Add(sSqlQueryTemplate, qt);
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

    public interface IMonitoredQuery : IDisposable
    {
        string QueryText { get; }
    }

    public class SqlQueryTemplate
    {
        static Dictionary<string, SqlQueryTemplate> registry = new Dictionary<string, SqlQueryTemplate>();

        /// <summary>
        /// Names of columns in query result
        /// </summary>
        public readonly string[] colsNames;
        /// <summary>
        /// Expressions of columns in query result
        /// </summary>
        public readonly string[] colsExprs;
        /// <summary>
        /// Names of bind variables
        /// </summary>
        public readonly string[] varsNames;
        /// <summary>
        /// Query text template
        /// </summary>
        public readonly string queryTemplateText;
        /// <summary>
        /// Source/template query expression
        /// </summary>
        public readonly SqlExpr SrcSqlExpr;
        public readonly bool arrayResults;
        public readonly string connName;

        public static int CountInRegistry { get { lock (registry) return registry.Count; } }

        public static SqlQueryTemplate Get(string[] colsNames, string[] colsExprs, string[] varsNames, string queryTemplateText, SqlExpr SrcSqlExpr, bool arrayResults, string connName)
        {
            SqlQueryTemplate res;
            lock (registry)
            {
                if (!registry.TryGetValue(queryTemplateText, out res))
                {
                    res = new SqlQueryTemplate(colsNames, colsExprs, varsNames, queryTemplateText, SrcSqlExpr, arrayResults, connName);
                    registry.Add(queryTemplateText, res);
                }
            }
            return res;
        }
        protected SqlQueryTemplate(string[] colsNames, string[] colsExprs, string[] varsNames, string queryTemplateText, SqlExpr SrcSqlExpr, bool arrayResults, string connName)
        {
            this.colsNames = colsNames;
            this.colsExprs = colsExprs;
            this.varsNames = varsNames;
            this.queryTemplateText = queryTemplateText;
            this.SrcSqlExpr = SrcSqlExpr;
            this.arrayResults = arrayResults;
            this.connName = connName;
        }

        public IMonitoredQuery GetQuery(IList columnsValues, params string[] varsSubstitutions)
        {
            int nColumns = 0;
            int nConds = 0;
            var sb = new System.Text.StringBuilder();
            for (int iCol = 0; iCol < columnsValues.Count; iCol++)
            {
                var colValue = columnsValues[iCol];
                var expr = colValue as Expr;
                if (expr != null)
                {
                    sb.Append(" AND ");
                    sb.Append(expr);
                    break;
                }
                var name = colsExprs[iCol].ToString();
                var lst = colValue as IList;
                if (lst != null)
                {
                    nColumns++;
                    if (lst.Count == 0)
                        continue;
                    sb.Append(" AND ");
                    var dict = new Dictionary<string, object>();
                    foreach (var v in lst)
                        if (v != null)
                            dict[v.ToString()] = v;
                    sb.Append('(');
                    sb.Append(name);
                    sb.Append(" IN (");
                    bool first = true;
                    int n = 0;
                    foreach (var v in dict.Values)
                    {
                        var s = Convert.ToString(v);
                        if (string.IsNullOrEmpty(s))
                            continue;
                        if (n == 1000)
                        {
                            sb.Append(") OR "); sb.Append(name); sb.Append(" IN (");
                            n = 0; first = true;
                        }
                        if (first)
                            first = false;
                        else sb.Append(',');
                        var ic = v as IConvertible;
                        if (ic != null && ic.GetTypeCode() == TypeCode.String)
                        {
                            sb.Append('\''); sb.Append(s); sb.Append('\'');
                        }
                        else
                            sb.Append(s);
                        n++;
                    }
                    sb.Append("))");
                    nConds += n;
                }
                else if (colValue != null)
                {
                    var s = Convert.ToString(colValue);
                    if (!string.IsNullOrEmpty(s))
                    {   // empty value means no condition
                        sb.Append(" AND ");
                        sb.Append(name);
                        sb.Append('=');
                        sb.Append(s);
                    }
                }
            }
            var tmpl = queryTemplateText;
            for (int iVar = 0; iVar < varsSubstitutions.Length; iVar++)
            {
                var s = varsSubstitutions[iVar];
                if (s != null)
                    tmpl = tmpl.Replace(varsNames[iVar], s);
            }
            if (nColumns > 0 && nConds == 0)
                return null;
            else
                return new Monitored(this, string.Format(tmpl, sb.ToString()));
        }

        long totalDurationMs;
        int totalCounter;
        long slowestQueryDuration;
        string slowestQueryText;

        void AddStatistics(long duration, string query)
        {
            Interlocked.Add(ref totalDurationMs, duration);
            Interlocked.Increment(ref totalCounter);
            lock (this)
            {
                if (slowestQueryDuration < duration)
                {
                    slowestQueryDuration = duration;
                    slowestQueryText = query;
                }
            }
        }

        class Monitored : IMonitoredQuery
        {
            SqlQueryTemplate sqt;
            System.Diagnostics.Stopwatch sw;
            string qryTxt;

            public Monitored(SqlQueryTemplate parent, string queryText)
            {
                sqt = parent;
                qryTxt = queryText;
                sw = new System.Diagnostics.Stopwatch();
                sw.Start();
            }

            public string QueryText { get { return qryTxt; } }

            public void Dispose()
            {
                System.Diagnostics.Stopwatch tmp = null;
                if (sw != null)
                    lock (this)
                        if (sw != null)
                        { tmp = sw; sw = null; }
                        else return;
                sqt.AddStatistics(tmp.ElapsedMilliseconds, qryTxt);
                //if (tmp != null)
                //{
                //	Console.WriteLine(CompactQry(sqt.queryTemplateText));
                //	Console.WriteLine("{0} ms", tmp.ElapsedMilliseconds);
                //}
            }
        }

        static string CompactQry(string q)
        {
            var s = q.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
            int pl;
            do { pl = s.Length; s = s.Replace("  ", " "); } while (s.Length < pl);
            return s;
        }

        public static object[][] GetStatisticsArr()
        {
            SqlQueryTemplate[] queries;
            lock (registry)
                queries = registry.Values.ToArray<SqlQueryTemplate>();
            var lst = new List<object[]>(queries.Length);
            for (int i = 0; i < queries.Length; i++)
            {
                var q = queries[i];
                int cnt;
                long ms;
                lock (q) { cnt = q.totalCounter; ms = q.totalDurationMs; }
                if (cnt == 0)
                    continue;
                lst.Add(new object[3] { ms, cnt, CompactQry(q.slowestQueryText) });
            };
            lst.Add(new object[3] { string.Empty, DbConnPool.UnusedConnections, "'=OraConnPool.UnusedConnections" });
            return lst.ToArray();
        }

        public static string GetStatisticsTxt()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Duration,ms\tCount\tQueryText");
            var stat = GetStatisticsArr();
            foreach (var s in stat)
                sb.AppendLine(string.Join(" \t", s));
            //sb.AppendLine(string.Format("OraConnPool.UnusedConnections = {0}", OraConnPool.UnusedConnections));
            return sb.ToString();
        }
    }
}