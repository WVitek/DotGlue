using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;

namespace W.Expressions.Sql
{
    public interface IDbmsSpecific
    {
        DbConnection GetConnection(string connString);
        [Obsolete("Use GetSpecificCommands instead")]
        void AddCmdParams(DbCommand dbCmd, SqlCommandData data);
        IEnumerable<DbCommand> GetSpecificCommands(DbConnection conn, SqlCommandData data);
        string TimeToSqlText(DateTime time);
    }
}
