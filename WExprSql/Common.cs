using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Collections;
using System.IO;
using System.Linq;

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
        public const int defaultActualityDays = 36525;

        [Flags]
        /// <summary>
        /// Possible attributes of SQL source (SELECT query) / function
        /// </summary>
        public enum Tbl
        {
            /// <summary>
            /// Simple comments collected into "description" attribute
            /// </summary>
            Description = 0x01,
            AbstractTable = 0x02,
            Substance = 0x04,
            LookupTableTemplate = 0x08,
            TemplateDescription = 0x10,
            /// <summary>
            /// Prefix for generated function(s) names
            /// </summary>
            FuncPrefix = 0x20,
            /// <summary>
            /// Result values grouped in arrays by key
            /// </summary>
            ArrayResults = 0x40,
            /// <summary>
            /// Actuality period in days for historical data
            /// </summary>
            ActualityDays = 0x80,
            /// <summary>
            /// Array of columns attributes, one Dictionary[Attr.Col,object] item for each row of SQL query, can be null.
            /// </summary>
            _columns_attrs = 0x100
        };

        [Flags]
        /// <summary>
        /// Possible attributes of SQL source (SELECT query) column
        /// </summary>
        public enum Col
        {
            /// <summary>
            /// Simple comments collected into "description" attribute
            /// </summary>
            Description = 0x01,
            /// <summary>
            /// Do not change field alias during preprocessing for true values of this attrubute
            /// </summary>
            FixedAlias = 0x02,
            Inherits = 0x04,
            NotNull = 0x08,
            InitValues = 0x10,
            Type = 0x20,
            TypeArgs = 0x40
        };

        public static void Add<T>(this Dictionary<T, object> attrs, T attrKey, object attrValue, bool newList) where T : System.Enum
        {
            if (attrs.TryGetValue(attrKey, out var val))
            {
                var lst = val as IList;
                if (lst == null)
                {
                    lst = new ArrayList();
                    lst.Add(val);
                    attrs[attrKey] = lst;
                }
                else if (newList)
                {
                    lst = new ArrayList(lst);
                    attrs[attrKey] = lst;
                }
                lst.Add(attrValue);
            }
            else attrs[attrKey] = attrValue;
        }

        public static bool GetBool<T>(this Dictionary<T, object> attrs, T attrKey, bool defaultValue = false) where T : System.Enum
        {
            if (attrs == null || !attrs.TryGetValue(attrKey, out var val))
                return defaultValue;
            return Convert.ToBoolean(val);
        }

        public static string GetString<T>(this Dictionary<T, object> attrs, T attrKey, string defaultValue = null) where T : System.Enum
        {
            if (attrs == null || !attrs.TryGetValue(attrKey, out var val))
                return defaultValue;
            return Convert.ToString(val);
        }

        static void AttrsToComments<T>(IEnumerable<KeyValuePair<T, object>> attrs, TextWriter wr)
        {
            foreach (var p in attrs)
            {
                wr.Write("--");
                wr.Write(p.Key);
                wr.Write('=');
                ConstExpr.ToText(wr, p.Value);
                wr.WriteLine();
            }
        }

        static void DescrToComment(object objDescription, TextWriter wr)
        {
            var lst = (objDescription as IList) ?? new object[] { objDescription };
            foreach (var s in lst)
            { wr.Write("--"); wr.WriteLine(s); }
        }

        public static void TblAttrsFriendlyText(IDictionary<string, object> attrs, TextWriter wr)
        {
            #region First line

            wr.Write("--");

            // function name prefix | name of SQL query template
            if (attrs.TryGetValue(nameof(Tbl.FuncPrefix), out var objFuncPfx))
                wr.Write(objFuncPfx);

            // function name prefix | name of SQL query template
            if (attrs.TryGetValue(nameof(Tbl.ArrayResults), out var objArrayRes) && Convert.ToBoolean(objArrayRes))
                wr.Write("[]");

            // actuality in days (optional)
            if (attrs.TryGetValue(nameof(Tbl.ActualityDays), out var objActuality) && Convert.ToDouble(objActuality) != defaultActualityDays)
                wr.Write($" {objActuality}");

            wr.WriteLine();

            #endregion

            #region Comments / description
            if (attrs.TryGetValue(nameof(Tbl.Description), out var objDescription))
                DescrToComment(objDescription, wr);
            #endregion

            #region Named attributes
            var u = attrs.Where(a =>
            {
                switch (a.Key)
                {
                    case nameof(Tbl.Description):
                    case nameof(Tbl.Substance):
                    case nameof(Tbl._columns_attrs):
                    case nameof(QueryTemplate):
                        return false;
                    default:
                        return true;
                }
            });
            if (u.Any())
                AttrsToComments(u, wr);

            #endregion
        }

        public static void FriendlyText(this IDictionary<Col, object> attrs, TextWriter wr)
        {
            if (attrs == null)
                return;

            #region Comments / description
            if (attrs.TryGetValue(Col.Description, out var objDescription))
                DescrToComment(objDescription, wr);
            #endregion

            #region Named attributes
            var flags = Col.Description | Col.Inherits | Col.FixedAlias;

            var u = attrs.Where(a => !flags.HasFlag(a.Key));
            if (u.Any())
                AttrsToComments(u, wr);

            #endregion

        }

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