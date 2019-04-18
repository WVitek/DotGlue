using System;
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
        { return SqlQueryTemplate.GetStatisticsTxt(); }

        [Arity(0, 0)]
        [IsNotPure]
        public static object GetStatArr(IList args)
        { return SqlQueryTemplate.GetStatisticsArr(); }

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

        public static async Task<IIndexedDict[]> GetSchema(AsyncExprCtx ctx, SqlQueryTemplate tmpl)
        {
            var conn = (IDbConn)await ctx.GetValue(tmpl.connName);

            var schema = (System.Data.DataTable)await conn.ExecCmd(new SqlCommandData()
            {
                Kind = CommandKind.GetSchema,
                SqlText = tmpl.queryTemplateText.Replace("{0}", string.Empty),
                ConvertMultiResultsToLists = tmpl.arrayResults
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
            Impl.TimedQueryKind forKinds;
            if (arg1 == null)
                forKinds = Impl.TimedQueryKind.Slice | Impl.TimedQueryKind.Interval;
            else
            {
                var lst = arg1 as IList ?? new object[] { arg1 };
                forKinds = default(Impl.TimedQueryKind);
                foreach (var v in lst)
                    forKinds |= (Impl.TimedQueryKind)Enum.Parse(typeof(Impl.TimedQueryKind), Convert.ToString(v));
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
                    var sqlCtx = new LoadingSqlFuncsContext()
                    {
                        sqlFileName = fullFileName,
                        cacheSubdomain = "DB",
                        oraConnValueName = dbConnName,
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

        class LoadingSqlFuncsContext
        {
            public string sqlFileName;
            public string oraConnValueName;
            public Impl.TimedQueryKind forKinds;
            public TimeSpan cachingExpiration;
            public string cacheSubdomain;
            public string defaultLocationForValueInfo;
            public Generator.Ctx ctx;

            IEnumerable<FuncDef> Func(string funcNamePrefix, int actualityInDays, string queryText, bool arrayResults, IDictionary<string, object> xtraAttrs)
            {
                return Impl.DefineLoaderFuncs(funcNamePrefix, actualityInDays, queryText, 
                    oraConnValueName, arrayResults, xtraAttrs, forKinds, cachingExpiration, cacheSubdomain, defaultLocationForValueInfo);
            }

            public IEnumerable<FuncDef> LoadingFuncs()
            {
                foreach (var fdEnum in Impl.ParseSqlFuncs(sqlFileName, Func, ctx))
                    foreach (var fd in fdEnum)
                        yield return fd;
            }
        }
    }
}
