using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using W.Expressions;
using W.Expressions.Sql;
using Microsoft.Data.SqlClient;

namespace W.Expressions.Sql
{
    public static class FuncDefs_MsSql
    {
        [IsNotPure]
        [Arity(2, 3)]
        public static object NewConnection(IList args)
        {
            object connStr = args[0];
            object nPoolSize = args[1];
            var cs = Convert.ToString(connStr);
            var parts = cs.Split('/', '\\', '@');
            if (parts.Length != 4)
                new ArgumentException("MsSql.NewConnection: connStr must be in format 'username/password@host/db' instead of '" + cs + "'");
            var username = parts[0];
            var password = parts[1];
            var host = parts[2];
            var db = parts[3];
            var csb = new SqlConnectionStringBuilder();
            csb.DataSource = host;
            csb.InitialCatalog = db;
            csb.UserID = username;
            csb.Password = password;
            csb.Pooling = false;
            string[] initCmds;
            if (args.Count > 2)
            {
                var lst = args[2] as IList;
                if (lst == null)
                    initCmds = new string[] { Convert.ToString(args[2]) };
                else initCmds = lst.Cast<object>().Select(x => Convert.ToString(x)).ToArray();
            }
            else initCmds = new string[0];
            return new DbConnPool(DbmsSpecificMsSql.Instance, Convert.ToInt32(nPoolSize), csb.ConnectionString, TimeSpan.FromSeconds(10), initCmds);
        }
    }

    class DbmsSpecificMsSql : IDbmsSpecific
    {
        public static readonly IDbmsSpecific Instance = new DbmsSpecificMsSql();

        private DbmsSpecificMsSql() { }

        static SqlDbType ToMsSqlDbType(DbType t)
        {
            switch (t)
            {
                case DbType.DateTime2: return SqlDbType.DateTime2;
                case DbType.Int32: return SqlDbType.Int;
                case DbType.Int64: return SqlDbType.BigInt;
                case DbType.Single: return SqlDbType.Real;
                case DbType.String: return SqlDbType.NVarChar;
                default: throw new NotImplementedException($"ToSqlDbType({t})");
            }
        }

        public void AddCmdParams(DbCommand dbCmd, SqlCommandData data)
        {
            var sqlCmd = (SqlCommand)dbCmd;

            if (data.ArrayBindCount > 0)
                throw new NotImplementedException();
            if (data.Params.Count > 0 && !data.BindByName)
                throw new NotSupportedException("DbmsSpecificMsSql: only BindByName parameters binding supported");
            foreach (var prm in data.Params)
            {
                var spa = sqlCmd.Parameters.Add(prm.name, ToMsSqlDbType(prm.type));
                spa.Value = prm.value;
                if (Common.Utils.IsEmpty(prm.value))
                    spa.SqlValue = DBNull.Value;// Status = OracleParameterStatus.NullInsert;
            }
        }

        public string TimeToSqlText(DateTime dt)
        {
            return string.Format("CONVERT(datetime2(3), {0}, 21)", dt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        }

        public IEnumerable<DbCommand> GetSpecificCommands(DbConnection dbConn, SqlCommandData data)
        {
            int n = data.ArrayBindCount;
            SqlCommand sqlCmd;
            if (n == 0)
            {
                sqlCmd = ((SqlConnection)dbConn).CreateCommand();
                sqlCmd.CommandText = data.SqlText;
                yield return sqlCmd;
                yield break;
            }
            if (!data.SqlText.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Only INSERT is supported by GetSpecificCommands for ArrayBindCount>0");
            var prms = data.Params;
            var sb = new System.Text.StringBuilder();
            var lsts = prms.Select(p => p.value as IList).ToList();
            int j = 0;
            int k = 0;
            sqlCmd = ((SqlConnection)dbConn).CreateCommand();
            while (n > 0)
            {
                if (sb.Length == 0)
                    sb.Append(data.SqlText);
                else
                    sb.AppendLine(",");
                sb.Append('(');
                for (int i = 0; i < prms.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    var v = lsts[i][j];
                    var ic = v as IConvertible;
                    if (ic != null)
                    {
                        var s = ic.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace("'", "''");
                        switch (ic.GetTypeCode())
                        {
                            case TypeCode.String:
                                sb.Append('\''); sb.Append(s); sb.Append('\'');
                                break;
                            case TypeCode.Empty:
                            case TypeCode.DBNull:
                                sb.Append("NULL");
                                break;
                            default:
                                sb.Append(s);
                                break;
                        }
                    }
                    else if (v == null || v == DBNull.Value)
                        sb.Append("NULL");
                    else
                        sb.Append(v.ToString().Replace("'", "''"));
                }
                sb.Append(")");
                k++;
                j++;
                if (k == 10000 || sb.Length >= 32768)
                {
                    sqlCmd.CommandText = sb.ToString();
                    k = 0;
                    yield return sqlCmd;
                }
            }
            if (k > 0)
            {
                sqlCmd.CommandText = sb.ToString();
                sb.Clear();
                yield return sqlCmd;
            }
        }

        public DbConnection GetConnection(string connString) => new SqlConnection(connString);
    }
}
