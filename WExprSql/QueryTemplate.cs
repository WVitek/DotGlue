using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace W.Expressions.Sql
{
    public interface IMonitoredQuery : IDisposable
    {
        string QueryText { get; }
    }

    public class QueryTemplate
    {
        static Dictionary<string, QueryTemplate> registry = new Dictionary<string, QueryTemplate>();

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

        public static QueryTemplate Get(string[] colsNames, string[] colsExprs, string[] varsNames, string queryTemplateText, SqlExpr SrcSqlExpr, bool arrayResults, string connName)
        {
            QueryTemplate res;
            lock (registry)
            {
                if (!registry.TryGetValue(queryTemplateText, out res))
                {
                    res = new QueryTemplate(colsNames, colsExprs, varsNames, queryTemplateText, SrcSqlExpr, arrayResults, connName);
                    registry.Add(queryTemplateText, res);
                }
            }
            return res;
        }

        protected QueryTemplate(string[] colsNames, string[] colsExprs, string[] varsNames, string queryTemplateText, SqlExpr SrcSqlExpr, bool arrayResults, string connName)
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
            QueryTemplate sqt;
            System.Diagnostics.Stopwatch sw;
            string qryTxt;

            public Monitored(QueryTemplate parent, string queryText)
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
            QueryTemplate[] queries;
            lock (registry)
                queries = registry.Values.ToArray<QueryTemplate>();
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