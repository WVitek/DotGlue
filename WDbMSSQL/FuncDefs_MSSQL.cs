using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq;
using W.Expressions;
using W.Expressions.Sql;
using System.Data.SqlClient;

namespace W.Expressions.Sql
{
    public static class FuncDefs_MsSql
    {
        [IsNotPure]
        [Arity(2, 3)]
        public static object MsSqlNewConnection(IList args)
        {
            object connStr = args[0];
            object nPoolSize = args[1];
            var cs = Convert.ToString(connStr);
            var parts = cs.Split('/', '\\', '@', ':');
            if (parts.Length != 5)
                new ArgumentException("OraNewConnection: connStr must be in format 'username/password@host:port/sid' instead of '" + cs + "'");
            var username = parts[0];
            var password = parts[1];
            var host = parts[2];
            var port = parts[3];
            var sid = parts[4];
            var csb = new SqlConnectionStringBuilder();
            csb.DataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={sid})))";
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

        static SqlDbType ToOraDbType(DbType t)
        {
            switch (t)
            {
                case DbType.DateTime2: return SqlDbType.DateTime2;
                case DbType.Int32: return SqlDbType.Int;
                case DbType.Int64: return SqlDbType.BigInt;
                case DbType.Single: return SqlDbType.Real;
                case DbType.String: return SqlDbType.VarChar;
                default: throw new NotImplementedException($"ToSqlDbType({t})");
            }
        }

        public void AddCmdParams(DbCommand dbCmd, SqlCommandData data)
        {
            var sqlCmd = (SqlCommand)dbCmd;

            if (data.ArrayBindCount > 0)
                //sqlCmd.ArrayBindCount = data.ArrayBindCount;
                throw new NotImplementedException();
            if(!data.BindByName)
                throw new NotSupportedException("DbmsSpecificMsSql: only BindByName parameters binding supported");
            foreach (var prm in data.Params)
            {
                var spa = sqlCmd.Parameters.Add(prm.name, ToOraDbType(prm.type));
                spa.Value = prm.value;
                //var lstF = prm.value as float[];
                //if (lstF != null)
                //{
                //    var sts = new OracleParameterStatus[lstF.Length];
                //    for (int i = lstF.Length - 1; i >= 0; i--)
                //        sts[i] = float.IsNaN(lstF[i]) ? OracleParameterStatus.NullInsert : OracleParameterStatus.Success;
                //    spa.ArrayBindStatus = sts;
                //}
                //else 
                if (Common.Utils.IsEmpty(prm.value))
                    spa.SqlValue = DBNull.Value;// Status = OracleParameterStatus.NullInsert;
            }
        }

        public DbConnection GetConnection(string connString) => new SqlConnection(connString);
    }
}
