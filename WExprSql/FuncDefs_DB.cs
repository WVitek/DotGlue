﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using W.Expressions;

namespace W.Expressions
{
    using System.IO;
    using W.Common;
    using W.Expressions.Sql;

    //[DefineQuantities(
    //    "id", "id", "code",
    //    "name", "name", "string"
    //)]
    public static class FuncDefs_DB
    {
        [Arity(0, 0)]
        [IsNotPure]
        public static object GetStatMsg(IList args)
        { return QueryTemplate.GetStatisticsTxt(); }

        [Arity(0, 0)]
        [IsNotPure]
        public static object GetStatArr(IList args)
        { return QueryTemplate.GetStatisticsArr(); }

        public static object DbPrepareQuery(object queryText)
        {
            return new SqlCommandData() { Kind = CommandKind.Query, SqlText = Convert.ToString(queryText) };
        }

        [CanBeLazy]
        public static object ExecCommand(object oraConn, object oraCommandData)
        {
            var ocd = (IDbConn)oraConn;
            var data = (SqlCommandData)oraCommandData;
            return (LazyAsync)(aec =>
                ocd.ExecCmd(data, aec.Cancellation));
        }

        [CanBeLazy]
        public static object Commit(object oraConn)
        {
            var ocd = (IDbConn)oraConn;
            return (LazyAsync)(aec =>
                ocd.Commit(aec.Cancellation));
        }

        [CanBeLazy]
        public static object ExecText(object oraConn, object insertText)
        {
            var conn = oraConn as IDbConn;
            var text = insertText as String;

            if (conn == null || text == null)
                return null;

            var cmd = new SqlCommandData
            {
                Kind = CommandKind.NonQuery,
                SqlText = text.Replace("\"", "'"),
            };
            return ExecCommand(conn, cmd);
        }

        public const string DefaultDbConnName = "$dbConn";

        public static async Task<object> Exec(AsyncExprCtx ctx, string oraConnName, SqlCommandData oraCmd)
        {
            var conn = (IDbConn)await ctx.GetValue(oraConnName);
            return await conn.ExecCmd(oraCmd, ctx.Cancellation);
        }

        public static async Task<object> ExecQuery(AsyncExprCtx ctx, string query, string oraConnName, bool arrayResults)
        {
            var conn = (IDbConn)await ctx.GetValue(oraConnName);
            return await conn.ExecCmd(new SqlCommandData() { Kind = CommandKind.Query, SqlText = query, ConvertMultiResultsToLists = arrayResults }, ctx.Cancellation);
        }

        public static async Task<IIndexedDict[]> GetSchema(AsyncExprCtx ctx, QueryTemplate qt)
        {
            var conn = (IDbConn)await ctx.GetValue(qt.connName);

            var schema = (System.Data.DataTable)await conn.ExecCmd(new SqlCommandData()
            {
                Kind = CommandKind.GetSchema,
                SqlText = qt.queryTemplateText.Replace("{0}", string.Empty),
                ConvertMultiResultsToLists = qt.arrayResults
            }, ctx.Cancellation);
            int nCols = schema.Columns.Count;
            var key2ndx = new Dictionary<string, int>(nCols);
            for (int i = 0; i < nCols; i++)
                key2ndx[schema.Columns[i].ColumnName] = i;
            int nRows = schema.Rows.Count;
            var res = new IIndexedDict[nRows];
            for (int i = 0; i < nRows; i++)
                res[i] = ValuesDictionary.New(schema.Rows[i].ItemArray, key2ndx);
            return res; // todo
        }

        //public static async Task CommitPreferredConn(AsyncExprCtx ctx)
        //{
        //    var conn = (IDbConn)await ctx.GetValue(DefaultDbConnName);
        //    await conn.Commit(ctx.Cancellation);
        //}

        //public static async Task<object> ExecNonQuery(AsyncExprCtx ctx, string cmdText, string oraConnName)
        //{
        //	var conn = (IOraConn)await ctx.GetValue(ctx, oraConnName);
        //	return await conn.ExecCmd(new OracleCommandData() { Kind = CommandKind.NonQuery, SqlText = cmdText }, ctx.Cancellation);
        //}

        public static async Task<object> CachedExecQuery(AsyncExprCtx ctx, string query, string oraConnName, bool arrayResults)
        {
            return await FuncDefs_Core._Cached(ctx, query, (LazyAsync)(aec =>
                ExecQuery(aec, query, oraConnName, arrayResults)),
                DateTime.Now + TimeSpan.FromMinutes(5), TimeSpan.Zero);
        }

        static object syncFuncDefsDB = new object();

        /// <summary>
        /// Load SQL queries from specified file as functions definitions
        /// </summary>
        /// <param name="ce">
        /// 0: name of text file containing SQL queries in special format
        /// 1: optional type of (timed) query: Interval, Slice, RawInterval, GetSchemaOnly
        /// 2: optional name for DB connection
        /// 3: optional default location for ValueInfo if _LOCATION part is not specified in SQL columns names
        /// 4: optional prefix for generated functions names
        /// </param>
        /// <returns>IEnumerable[FuncDef]</returns>
        [Arity(1, 4)]
        public static object UseSqlAsFuncsFrom(CallExpr ce, Generator.Ctx ctx)
        {
            var arg0 = Generator.Generate(ce.args[0], ctx);
            if (OPs.KindOf(arg0) != ValueKind.Const)
                ctx.Error("Constant value expected");

            var arg1 = (ce.args.Count < 2) ? null : Generator.Generate(ce.args[1], ctx);

            var sqlFileName = Convert.ToString(arg0);
            QueryKind forKinds;
            if (arg1 == null)
                forKinds = QueryKind.TimeSlice | QueryKind.TimeInterval;
            else
            {
                var lst = arg1 as IList ?? new object[] { arg1 };
                forKinds = default(QueryKind);
                foreach (var v in lst)
                    forKinds |= (QueryKind)Enum.Parse(typeof(QueryKind), Convert.ToString(v));
            }

            var dbConnName = (ce.args.Count < 3) ? DefaultDbConnName : OPs.TryAsName(ce.args[2], ctx);
            if (string.IsNullOrEmpty(dbConnName))
                ctx.Error($"Connection name must be nonempty: {ce.args[2]}");

            var fullFileName = Path.IsPathRooted(sqlFileName)
                ? sqlFileName
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Convert.ToString(sqlFileName));

            var cacheKey = $"FuncDefs:{fullFileName}:{forKinds.ToString()}";
            var lfds = (Lazy<IEnumerable<FuncDef>>)System.Web.HttpRuntime.Cache.Get(cacheKey);
            if (lfds != null)
                return lfds.Value;
            lock (syncFuncDefsDB)
            {
                lfds = (Lazy<IEnumerable<FuncDef>>)System.Web.HttpRuntime.Cache.Get(cacheKey);
                if (lfds == null)
                {
                    var sqlCtx = new W.Expressions.Sql.Preprocessing.PreprocessingContext()
                    {
                        sqlFileName = fullFileName,
                        cacheSubdomain = "DB",
                        dbConnValueName = dbConnName,
                        forKinds = forKinds,
                        ctx = ctx,
                        cachingExpiration = TimeSpan.FromMinutes(5),
                        defaultLocationForValueInfo = (ce.args.Count < 4) ? null : OPs.TryAsString(ce.args[3], ctx)
                    };

                    lfds = new Lazy<IEnumerable<FuncDef>>(() =>
                        sqlCtx.LoadingFuncs(),
                        LazyThreadSafetyMode.ExecutionAndPublication
                    );
                    var obj = System.Web.HttpRuntime.Cache.Add(cacheKey, lfds,
                        null,
                        System.Web.Caching.Cache.NoAbsoluteExpiration,
                        TimeSpan.FromMinutes(5), System.Web.Caching.CacheItemPriority.Normal, null);
                    if (obj != null)
                        throw new InvalidOperationException();
                }
            }
            return lfds.Value;
        }

        static void SqlFuncsToTextImpl(Generator.Ctx ctx, TextWriter wr, string locationCode)
        {
            foreach (var f in ctx.GetFunc(null, 0))
            {
                if (!f.xtraAttrs.TryGetValue(nameof(QueryTemplate), out var objQT))
                    // no QueryTemplate = is not SQL-originated function
                    continue;
                if (f.resultsInfo.All(vi => vi.location != locationCode))
                    // no any output parameter from specified location/origin/source
                    continue;

                Attr.TblAttrsFriendlyText(f.xtraAttrs, wr);
                var colAttrs = (IList<Dictionary<Attr.Col, object>>)f.xtraAttrs[nameof(Attr.Tbl._columns_attrs)];
                var queryTmpl = (QueryTemplate)objQT;
                var sql = queryTmpl.SrcSqlExpr;
                foreach (SqlSectionExpr sec in sql.args)
                {
                    var args = sec.args;
                    var attrs = (sec.kind == SqlSectionExpr.Kind.Select) ? colAttrs : null;
                    bool fromNewLine = args.Count > 1 || attrs != null;
                    wr.Write(sec.sectionName);
                    if (fromNewLine)
                        wr.WriteLine();
                    for (int i = 0; i < args.Count; i++)
                    {
                        if (i > 0)
                            wr.WriteLine(',');
                        if (attrs != null)
                            Attr.FriendlyText(attrs[i], wr);
                        wr.Write(fromNewLine ? '\t' : ' ');
                        wr.Write(args[i]);
                    }
                    wr.WriteLine();
                }
                //wr.WriteLine(sql);
                wr.WriteLine(';');
                wr.WriteLine();
                wr.WriteLine();
            }
        }

        [Arity(1, 1)]
        public static object SqlFuncsToText(CallExpr ce, Generator.Ctx ctx)
        {
            var locationCode = Convert.ToString(ctx.GetConstant(ce.args[0]));
            using (var sw = new StringWriter())
            {
                SqlFuncsToTextImpl(ctx, sw, locationCode);
                return sw.ToString();
            }
        }

        class ValInf
        {
            public string sqlType;
            public string pkTable, pkField;
            public object firstValue;
        }

        static void SqlFuncsToDDL_Impl(Generator.Ctx ctx, TextWriter wr, string locationCode)
        {
            var dictTypes = new Dictionary<string, ValInf>();

            foreach (var f in ctx.GetFunc(null, 0))
            {
                if (!f.xtraAttrs.TryGetValue(nameof(QueryTemplate), out var objQT))
                    // no QueryTemplate = is not SQL-originated function
                    continue;

                var queryTmpl = (QueryTemplate)objQT;
                var sql = queryTmpl.SrcSqlExpr;
                var secFrom = sql[SqlSectionExpr.Kind.From];

                if (secFrom.args.Count > 1)
                    // can't generate DDL for join
                    continue;

                if (f.resultsInfo.All(vi => vi.location != locationCode))
                    // no any output parameter from specified location/origin/source
                    continue;

                Attr.TblAttrsFriendlyText(f.xtraAttrs, wr);
                var colAttrs = (IList<Dictionary<Attr.Col, object>>)f.xtraAttrs[nameof(Attr.Tbl._columns_attrs)];

                var secSelect = sql[SqlSectionExpr.Kind.Select];
                var tableName = secFrom.args[0].ToString();

                var extraDDL = new StringBuilder();

                wr.WriteLine($"CREATE TABLE {tableName} (");

                var columns = secSelect.args;
                // columns
                for (int i = 0; i < columns.Count; i++)
                {
                    AliasExpr ae; // column is presented as pair of field name and globally unique alias of value
                    {
                        var colExpr = columns[i];
                        ae = colExpr as AliasExpr;

                        // Skip improper fields
                        if (ae == null)
                        {
                            if (colExpr is ReferenceExpr re)
                                ae = new AliasExpr(re, re);
                            else
                            {
                                wr.WriteLine($"--???\t{colExpr}");
                                continue;
                            }
                        }
                        switch (ae.left.nodeType)
                        {
                            case ExprType.Reference: break;
                            case ExprType.Constant:
                                wr.WriteLine($"--\t{ae.left}\t{ae.right}");
                                continue;
                            default:
                                wr.WriteLine($"--???\t{ae.left}\t{ae.right}");
                                continue;
                        }
                    }

                    var attrs = colAttrs[i];

                    var fieldName = ae.left.ToString().ToUpperInvariant();
                    var fieldAlias = ae.right.ToString();

                    string type, trail;
                    {
                        bool isPK = attrs.GetBool(Attr.Col.PK, false);
                        bool notNull = isPK || attrs.GetBool(Attr.Col.NotNull, false);

                        var curr = new ValInf() { sqlType = attrs.GetString(Attr.Col.Type) };

                        if (dictTypes.TryGetValue(fieldAlias, out var prev))
                        {
                            if (prev.pkTable != null)
                            {
                                if (isPK)
                                    wr.WriteLine($"--WARNING! Value named '{fieldAlias}' is already used as PK in table '{prev.pkTable}'");
                                else
                                {   // create foreign key constraint
                                    var hash = $"{tableName}:{fieldAlias}".GetHashCode().ToString("X").Substring(0, 4);
                                    var fk = string.Format("fk_{0}_{1}",
                                        (tableName + '_' + prev.pkTable.Split('_')[0]).DeVowel(22),
                                        hash
                                    );
                                    extraDDL.AppendLine($"ALTER TABLE {tableName} ADD CONSTRAINT {fk} FOREIGN KEY ({fieldName}) REFERENCES {prev.pkTable};");
                                }
                            }
                            if (curr.sqlType == null)
                                curr = prev;
                            else
                            {
                                if (curr.sqlType != prev.sqlType)
                                    wr.WriteLine($"--WARNING! Type mismatch for value named '{fieldAlias}', first declaration has type '{prev.sqlType}'");
                                curr.firstValue = prev.firstValue;
                                curr.pkTable = prev.pkTable;
                                curr.pkField = prev.pkField;
                            }
                        }
                        else if (curr.sqlType != null)
                        {
                            if (isPK)
                            {
                                curr.pkTable = tableName;
                                curr.pkField = fieldName;
                                var initVals = attrs.Get(Attr.Col.InitValues);
                                if (initVals is IList lst)
                                    curr.firstValue = lst[0];
                                else
                                    curr.firstValue = initVals;
                            }
                            dictTypes.Add(fieldAlias, curr);
                        }

                        object defVal = attrs.Get(Attr.Col.Default);

                        if (isPK)
                            trail = " NOT NULL PRIMARY KEY";
                        else if (notNull)
                        {
                            var def = attrs.Get(Attr.Col.Default) ?? curr.firstValue;
                            if (def != null)
                                trail = $" DEFAULT {new ConstExpr(def)} NOT NULL";
                            else
                                trail = " NOT NULL";
                        }
                        else trail = null;

                        if (curr.sqlType == null)
                        {
                            var info = ValueInfo.Create(fieldAlias, true);
                            curr.sqlType = info?.quantity.DefaultDimensionUnit.Name ?? fieldAlias;
                        }

                        type = curr.sqlType;
                    }

                    var typeArgs = attrs.GetString(Attr.Col.TypeArgs);
                    if (!string.IsNullOrEmpty(typeArgs))
                        typeArgs = '(' + typeArgs + ')';

                    if (attrs == null || !attrs.TryGetValue(Attr.Col.Description, out var descr))
                        descr = null;

                    wr.WriteLine($"\t{fieldName} {type}{typeArgs}{trail},\t--{fieldAlias}\t{Attr.OneLineText(descr)}");
                }

                wr.WriteLine(')');
                wr.WriteLine(';');
                wr.WriteLine();

                if (extraDDL.Length > 0)
                {
                    wr.WriteLine(extraDDL);
                    wr.WriteLine();
                    extraDDL.Clear();
                }

            }
        }

        [Arity(1, 1)]
        public static object SqlFuncsToDDL(CallExpr ce, Generator.Ctx ctx)
        {
            var locationCode = Convert.ToString(ctx.GetConstant(ce.args[0]));
            using (var sw = new StringWriter())
            {
                SqlFuncsToDDL_Impl(ctx, sw, locationCode);
                return sw.ToString();
            }
        }
    }
}
