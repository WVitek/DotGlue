using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace W.Common
{
    public class ValuesDictionary : IIndexedDict, IReadOnlyDictionary<string, object>
    {
        public static readonly IIndexedDict[] Empties = new IIndexedDict[0];

        public readonly object[] values;
        public readonly IDictionary<string, int> key2ndx;
        public readonly DateTime time;
        public readonly DateTime endTime;

        public static ValuesDictionary New(string[] keys, object[] values)
        {
            var rng = new TimeRange(values);
            return New(values, Enumerable.Range(0, keys.Length).ToDictionary(i => keys[i]));
        }

        public ValuesDictionary(string[] keys, object[] values, DateTime time, DateTime endTime)
        {
#if DEBUG
            if (keys.Length != values.Length)
                throw new IndexOutOfRangeException("ValuesDictionary.New(keys, values): wrong keys.Length");
#endif
            this.values = values;
            this.key2ndx = Enumerable.Range(0, keys.Length).ToDictionary(i => keys[i], StringComparer.OrdinalIgnoreCase);
            this.time = time;
            this.endTime = endTime;
        }

        public static ValuesDictionary New(object[] values, IDictionary<string, int> key2ndx)
        {
            var rng = new TimeRange(values);
            return new ValuesDictionary(values, key2ndx, rng.time, rng.endTime);
        }

        public ValuesDictionary(object[] values, IDictionary<string, int> key2ndx, DateTime time, DateTime endTime)
        {
#if DEBUG
            var ndxs = key2ndx.Select(p => p.Value).Distinct().ToArray();
            var keys = key2ndx.Select(p => p.Key).Distinct().ToArray();
            if (ndxs.Length != values.Length
                || ndxs.Length > 0 && (ndxs.Min() < 0 || ndxs.Max() >= values.Length)
                || keys.Length < values.Length)
                throw new IndexOutOfRangeException("ValuesDictionary.New(values, key2ndx): corrupted index dictionary");
#endif
            this.values = values;
            this.key2ndx = key2ndx;
            this.time = time;
            this.endTime = endTime;
        }

        public ValuesDictionary(object[] values, IDictionary<string, int> key2ndx, bool withStrictValidation)
        {
            var ndxs = key2ndx.Select(p => p.Value).Distinct().ToArray();
            if (withStrictValidation)
            {
                var keys = key2ndx.Select(p => p.Key).Distinct().ToArray();
                if (ndxs.Length != values.Length || ndxs.Min() < 0 || values.Length <= ndxs.Max() || keys.Length < values.Length)
                    throw new IndexOutOfRangeException("ValuesDictionary.New(values, key2ndx, true): incorrect index dictionary");
            }
            else if (ndxs.Length > values.Length || ndxs.Min() < 0 || values.Length <= ndxs.Max())
                throw new IndexOutOfRangeException("ValuesDictionary.New(values, key2ndx, false): incorrect index dictionary");
            this.values = values;
            this.key2ndx = key2ndx;
            var rng = new TimeRange(values);
            time = rng.time;
            endTime = rng.endTime;
        }

        #region IDictionary implementation
        public void Add(string key, object value) { throw new NotSupportedException(); }
        public bool ContainsKey(string key) { return key2ndx.ContainsKey(key); }
        public ICollection<string> Keys { get { return key2ndx.Keys; } }
        public bool Remove(string key) { throw new NotSupportedException(); }
        public bool TryGetValue(string key, out object value)
        {
            int i;
            if (key2ndx.TryGetValue(key, out i))
            { value = values[i]; return true; }
            else
            { value = null; return false; }
        }
        public ICollection<object> Values { get { return values; } }
        public object this[string key] { get { return values[key2ndx[key]]; } set { throw new NotSupportedException(); } }
        public void Add(KeyValuePair<string, object> item) { throw new NotSupportedException(); }
        public void Clear() { throw new NotSupportedException(); }
        public bool Contains(KeyValuePair<string, object> item) { throw new NotImplementedException(); }
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) { throw new NotImplementedException(); }
        public int Count { get { return values.Length; } }
        public bool IsReadOnly { get { return true; } }
        public bool Remove(KeyValuePair<string, object> item) { throw new NotSupportedException(); }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var p in key2ndx)
                yield return new KeyValuePair<string, object>(p.Key, (p.Value < values.Length) ? values[p.Value] : null);
        }

        IEnumerator IEnumerable.GetEnumerator()
        { throw new NotImplementedException(); }
        #endregion

        #region IIndexedDict implementation
        public IDictionary<string, int> Key2Ndx { get { return key2ndx; } }
        public object[] ValuesList { get { return values; } }
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        #endregion

        #region IReadOnlyDictionary
        IEnumerable<string> IReadOnlyDictionary<string, object>.Keys { get { return key2ndx.Keys; } }
        IEnumerable<object> IReadOnlyDictionary<string, object>.Values { get { return values; } }
        #endregion

        public override string ToString()
        {
            var text = string.Join("; ", key2ndx.OrderBy(p => p.Key).Select(p =>
                {
                    var v = (p.Value < values.Length) ? values[p.Value] : null;
                    var lst = v as IList;
                    return string.Format("{0}={1}", p.Key, (lst == null) ? (v == null ? string.Empty : v.ToString()) : '[' + lst.Count.ToString() + ']');
                }
            ));
            return text;
        }

        #region Static methods
        public static object IDictsToTimedStr(IList dicts)
        {
            if (dicts.Count == 0)
                return "IIndexedDict[0]";
            var sb = new StringBuilder();
            sb.AppendLine();
            var headers = ((IIndexedDict)dicts[0]).Key2Ndx.GroupBy(p => p.Value, p => p.Key, (ndx, names) => string.Join(",", names)).ToArray();
            sb.AppendLine(string.Join("\t\t\t", headers));
            foreach (IIndexedDict row in dicts)
                sb.AppendLine(string.Join("\t", row.ValuesList.Select(v =>
                {
                    var to = v as ITimedObject;
                    if (to != null)
                        return string.Format("{0}\t{1}\t{2}", to.Object, to.Time.ToString("yyyy-MM-dd HH:mm:ss"), to.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    return string.Format("{0}\t\t", v);
                })));
            return sb.ToString();
        }

        public static object IDictsToStr(IList dicts)
        {
            if (dicts.Count == 0)
                return "IIndexedDict[0]";
            var sb = new StringBuilder();
            sb.AppendLine();
            var k2n = ((IIndexedDict)dicts[0]).Key2Ndx;
            int n = k2n.Values.Max() + 1;
            var columns = new string[n];
            foreach (var p in k2n)
                columns[p.Value] = p.Key;
            sb.AppendLine(string.Join("\t", columns));
            foreach (IIndexedDict row in dicts)
                sb.AppendLine(string.Join("\t", row.ValuesList.Select(v => Convert.ToString(v))));
            return sb.ToString();
        }
        #endregion

#if DEBUG
        public Dictionary<string, object> DbgDict { get { return new Dictionary<string, object>(this, StringComparer.OrdinalIgnoreCase); } }
#endif

    }

    public static class IIndexedDictExtensions
    {
        public static void SortByKeyValue(this IIndexedDict[] items, params int[] keyFields)
        {
            Array.Sort<IIndexedDict>(items, (Comparison<IIndexedDict>)(
                (a, b) =>
                {
                    var lsta = a.ValuesList;
                    var lstb = b.ValuesList;
                    foreach (int i in keyFields)
                    {
                        var va = lsta[i];
                        var ca = va as IComparable;
                        int r = (ca != null) ? ca.CompareTo(lstb[i]) : Cmp.cmp(va, lstb[i]);
                        if (r != 0)
                            return r;
                    }
                    return 0;
                }
            ));
        }

        public static void SortByKeyTimed(this IIndexedDict[] items, params int[] keyFields)
        {
            Array.Sort<IIndexedDict>(items,
                (a, b) => a.ValuesList.CompareSameTimedKeys(b.ValuesList, keyFields)
            );
        }

        public static int BinarySearch(this IList<IIndexedDict> items, int[] keysNdxs, object[] keysValues)
        {
            int n = items.Count;
            int ia = 0, ib = n - 1;
            while (ia <= ib)
            {
                int i = (ia + ib) / 2;
                int r = Cmp.CompareKeys(items[i].ValuesList, keysNdxs, keysValues);
                if (r > 0)
                {
                    ib = i - 1;
                    if (ib < ia)
                        return ~i;
                }
                else if (r < 0)
                {
                    ia = i + 1;
                    if (ib < ia)
                        return ~ia;
                }
                else return i;
            }
            return ~0;
        }

        public static int BinarySearchTimed(this IList<IIndexedDict> items, int[] keysNdxs, object[] keysValues)
        {
            int n = items.Count;
            int ia = 0, ib = n - 1;
            while (ia <= ib)
            {
                int i = (ia + ib) / 2;
                int r = Cmp.CompareTimedKeys(items[i].ValuesList, keysNdxs, keysValues);
                if (r > 0)
                {
                    ib = i - 1;
                    if (ib < ia)
                        return ~i;
                }
                else if (r < 0)
                {
                    ia = i + 1;
                    if (ib < ia)
                        return ~ia;
                }
                else return i;
            }
            return ~0;
        }

        public static int BinarySearchWithTimeRange(this IList<IIndexedDict> items, int[] keysNdxs, object[] keysValues, ITimedObject timeRange)
        {
            int n = items.Count;
            int ia = 0, ib = n - 1;
            while (ia <= ib)
            {
                int i = (ia + ib) / 2;
                int r = Cmp.CompareKeysWithTimeRange(items[i].ValuesList, keysNdxs, keysValues, timeRange);
                if (r > 0)
                {
                    ib = i - 1;
                    if (ib < ia)
                        return ~i;
                }
                else if (r < 0)
                {
                    ia = i + 1;
                    if (ib < ia)
                        return ~ia;
                }
                else return i;
            }
            return ~0;
        }

        static void DupDataError(IIndexedDict data)
        {
            throw new Exception(string.Format("Duplicated data detected!\n\r{0}", data));
        }

        public static IEnumerable<int> BinarySearchAllWithTimeRange(this IList<IIndexedDict> items, int[] keysNdxs, object[] keysValues, ITimedObject timeRange)
        {
            if (items == null) yield break;
            int n = items.Count;
            if (n == 0) yield break;
            int i = BinarySearchWithTimeRange(items, keysNdxs, keysValues, timeRange);
            if (i < 0)
            { i = 0; yield break; }
            var Imax = i;
            while (i > 0 && Cmp.CompareKeysWithTimeRange(items[i - 1].ValuesList, keysNdxs, keysValues, timeRange) == 0)
                i--;
            var prevEndTime = DateTime.MinValue;
            while (i <= Imax)
            {
#if CHECK_DATA_DUPS
                if (i < Imax && Cmp.CompareTimed(items[i].ValuesList, items[i + 1].ValuesList) == 0)
                {
                    DupDataError(items[i]);
                    i++; continue;
                }
#endif
                yield return i++;
            }
            while (i < n)
            {
                if (Cmp.CompareKeysWithTimeRange(items[i].ValuesList, keysNdxs, keysValues, timeRange) == 0)
                {
#if CHECK_DATA_DUPS
                    if (Cmp.CompareTimed(items[i - 1].ValuesList, items[i].ValuesList) == 0)
                    {
                        DupDataError(items[i]);
                        i++; continue;
                    }
#endif
                    yield return i++;
                }
                else yield break;
            }
        }

        public static IEnumerable<int> BinarySearchAllTimed(this IList<IIndexedDict> items, int[] keysNdxs, object[] keysValues)
        {
            if (items == null) yield break;
            int n = items.Count;
            if (n == 0) yield break;
            int i = BinarySearchTimed(items, keysNdxs, keysValues);
            if (i < 0)
            { i = 0; yield break; }
            var Imax = i;
            while (i > 0 && Cmp.CompareTimedKeys(items[i - 1].ValuesList, keysNdxs, keysValues) == 0)
                i--;
            while (i <= Imax)
            {
#if CHECK_DATA_DUPS
                if (i < Imax && Cmp.CompareTimed(items[i].ValuesList, items[i + 1].ValuesList) == 0)
                {
                    DupDataError(items[i]);
                    i++; continue;
                }
#endif
                yield return i++;
            }
            while (i < n)
            {
                if (Cmp.CompareTimedKeys(items[i].ValuesList, keysNdxs, keysValues) == 0)
                {
#if CHECK_DATA_DUPS
                    if (Cmp.CompareTimed(items[i - 1].ValuesList, items[i].ValuesList) == 0)
                    {
                        DupDataError(items[i]);
                        i++; continue;
                    }
#endif
                    yield return i++;
                }
                else yield break;
            }
        }

        public static IEnumerable<int> BinarySearchAll(this IList<IIndexedDict> items, int[] keysNdxs, object[] keysValues)
        {
            if (items == null) yield break;
            int n = items.Count;
            if (n == 0) yield break;
            int i = BinarySearch(items, keysNdxs, keysValues);
            if (i < 0)
            { i = 0; yield break; }
            var Imax = i;
            while (i > 0 && Cmp.CompareKeys(items[i - 1].ValuesList, keysNdxs, keysValues) == 0)
                i--;
            while (i <= Imax)
            {
#if CHECK_DATA_DUPS
                if (i < Imax && Cmp.CompareTimed(items[i].ValuesList, items[i + 1].ValuesList) == 0)
                {
                    DupDataError(items[i]);
                    i++; continue;
                }
#endif
                yield return i++;
            }
            while (i < n)
            {
                if (Cmp.CompareKeys(items[i].ValuesList, keysNdxs, keysValues) == 0)
                {
#if CHECK_DATA_DUPS
                    if (Cmp.CompareTimed(items[i - 1].ValuesList, items[i].ValuesList) == 0)
                    {
                        DupDataError(items[i]);
                        i++; continue;
                    }
#endif
                    yield return i++;
                }
                else yield break;
            }
        }
    }

}
