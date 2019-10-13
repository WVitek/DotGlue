using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Data;

namespace W.Expressions
{

    public class FuncDefs_TableData
    {
        public static object SelectRows(object table)
        { return ((DataTable)table).Select(); }

        public static object SelectRows(object table, object filter)
        { return ((DataTable)table).Select(Convert.ToString(filter)); }

        public static object RowAsDict(object row)
        { return new RowAsDictionary((DataRow)row); }

        public static object RowAsList(object row)
        { return ((DataRow)row).ItemArray; }
    }

    public class RowAsDictionary : IDictionary<string, object>
    {
        public readonly DataRow row;

        public RowAsDictionary(DataRow row) { this.row = row; }

        #region IDictionary<string,object> Members

        public void Add(string key, object value) { throw new NotSupportedException(); }
        public bool ContainsKey(string key) { return row.Table.Columns.Contains(key); }

        public ICollection<string> Keys
        {
            get
            {
                var cols = row.Table.Columns;
                var keys = new string[cols.Count];
                for (int i = keys.Length - 1; i >= 0; i--) keys[i] = cols[i].ColumnName;
                return keys;
            }
        }

        public bool Remove(string key) { throw new NotSupportedException(); }

        public bool TryGetValue(string key, out object value)
        {
            try { value = row[key]; return true; }
            catch { value = null; return false; }
        }

        public ICollection<object> Values { get { return row.ItemArray; } }

        public object this[string key]
        {
            get { return row[key]; }
            set { throw new NotImplementedException(); }
        }
        #endregion

        #region ICollection<KeyValuePair<string,object>> Members
        public void Add(KeyValuePair<string, object> item) { throw new NotSupportedException(); }
        public void Clear() { throw new NotSupportedException(); }
        public bool Contains(KeyValuePair<string, object> item) { throw new NotSupportedException(); }
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) { throw new NotSupportedException(); }
        public int Count { get { return row.ItemArray.Length; } }
        public bool IsReadOnly { get { return true; } }
        public bool Remove(KeyValuePair<string, object> item) { throw new NotSupportedException(); }
        #endregion

        #region IEnumerable<KeyValuePair<string,object>> Members
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            var cols = row.Table.Columns;
            for (int i = 0; i < cols.Count; i++)
                yield return new KeyValuePair<string, object>(cols[i].ColumnName, row.ItemArray[i]);
        }
        #endregion

        #region IEnumerable Members
        IEnumerator IEnumerable.GetEnumerator() { throw new NotImplementedException(); }
        #endregion
    }
}
