using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Data;

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

    /// <summary>
    /// "Attributes" names for SQL-defined functions
    /// </summary>
    public static class Attr
    {
        /// <summary>
        /// Prefix for generated function(s) names
        /// </summary>
        public static class funcPrefix { }
        /// <summary>
        /// Result values grouped in arrays by key
        /// </summary>
        public static class arrayResults { }
        /// <summary>
        /// Actuality period in days for historical data
        /// </summary>
        public static class actuality { }
        /// <summary>
        /// Simple comments collected into "description" attribute
        /// </summary>
        public static class description { }
        /// <summary>
        /// Array of inner attributes, one Dictionary[string,object] item for each row of SQL query, can be null.
        /// </summary>
        public static class innerAttrs { }
    }

    [Flags]
    public enum QueryKind
    {
        None = 0,
        /// <summary>
        /// Values: one with timestamp between MIN and A, and all with timestamps between A and B (A not included)
        /// </summary>
        TimeInterval = 1,
        /// <summary>
        /// One value with timestamp between MIN and AT
        /// </summary>
        TimeSlice = 2,
        /// <summary>
        /// values with timestamp between A and B (A not included)
        /// </summary>
        TimeRawInterval = 4,
        /// <summary>
        /// get columns types
        /// </summary>
        GetSchemaOnly = 8,
        /// <summary>
        /// tables definition
        /// </summary>
        DDL = 9
    };

}