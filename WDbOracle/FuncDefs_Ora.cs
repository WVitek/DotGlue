using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq;
using Oracle.ManagedDataAccess.Client;
using W.Expressions;
using W.Expressions.Sql;

namespace W.Expressions.Sql
{
    public static class FuncDefs_Ora
    {
        [IsNotPure]
        [Arity(2, 3)]
        public static object NewConnection(IList args)
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
            var ocsb = new OracleConnectionStringBuilder();
            ocsb.DataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={sid})))";
            ocsb.UserID = username;
            ocsb.Password = password;
            ocsb.Pooling = false;
            string[] initCmds;
            if (args.Count > 2)
            {
                var lst = args[2] as IList;
                if (lst == null)
                    initCmds = new string[] { Convert.ToString(args[2]) };
                else initCmds = lst.Cast<object>().Select(x => Convert.ToString(x)).ToArray();
            }
            else initCmds = new string[0];
            return new DbConnPool(DbmsSpecificOracle.Instance, Convert.ToInt32(nPoolSize), ocsb.ConnectionString, TimeSpan.FromSeconds(10), initCmds);
        }
    }

    class DbmsSpecificOracle : IDbmsSpecific
    {
        public static readonly IDbmsSpecific Instance = new DbmsSpecificOracle();

        private DbmsSpecificOracle() { }

        static OracleDbType ToOraDbType(DbType t)
        {
            switch (t)
            {
                case DbType.DateTime2: return OracleDbType.Date;
                case DbType.Int32: return OracleDbType.Int32;
                case DbType.Int64: return OracleDbType.Int64;
                case DbType.Single: return OracleDbType.BinaryFloat;
                case DbType.String: return OracleDbType.Varchar2;
                default: throw new NotImplementedException($"ToOraDbType({t})");
            }
        }

        public void AddCmdParams(DbCommand dbCmd, SqlCommandData data)
        {
            var oraCmd = (OracleCommand)dbCmd;

            if (data.ArrayBindCount > 0)
                oraCmd.ArrayBindCount = data.ArrayBindCount;
            oraCmd.BindByName = data.BindByName;
            foreach (var prm in data.Params)
            {
                var opa = oraCmd.Parameters.Add(prm.name, ToOraDbType(prm.type));
                opa.Value = prm.value;
                var lstF = prm.value as float[];
                if (lstF != null)
                {
                    var sts = new OracleParameterStatus[lstF.Length];
                    for (int i = lstF.Length - 1; i >= 0; i--)
                        sts[i] = float.IsNaN(lstF[i]) ? OracleParameterStatus.NullInsert : OracleParameterStatus.Success;
                    opa.ArrayBindStatus = sts;
                }
                else if (Common.Utils.IsEmpty(prm.value))
                    opa.Status = OracleParameterStatus.NullInsert;
            }
        }

        public DbConnection GetConnection(string connString) => new OracleConnection(connString);
    }
}
