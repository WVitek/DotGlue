using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using Nito.AsyncEx;

namespace W.Common
{
    public static class Trace
    {
        public static Func<string, System.Diagnostics.SourceLevels, System.Diagnostics.TraceSource> FuncGetLogger
            = (name, levels) => new System.Diagnostics.TraceSource(name);

        public static System.Diagnostics.TraceSource GetLogger(string name) { return FuncGetLogger(name, System.Diagnostics.SourceLevels.All); }

        public static System.Diagnostics.TraceSource GetLogger(string name, System.Diagnostics.SourceLevels levels) { return FuncGetLogger(name, levels); }
    }

    public static class Diagnostics
    {
        public static System.Diagnostics.TraceSource Logger = Trace.GetLogger("WCommon");
    }

    public interface IAsyncLock : IDisposable
    {
        Task WaitAsync(CancellationToken ct);
        void Release();
    }

    public interface IAsyncSemaphore : IDisposable
    {
        Task WaitAsync(CancellationToken ct);
        void Release(int releaseCount = 1);
    }

    public static class Cmp
    {
        public static int cmp(object x, object y)
        {
            int r;
            if (W.Common.NumberUtils.IsNumber(x) && W.Common.NumberUtils.IsNumber(y))
                r = Comparer<double>.Default.Compare(Convert.ToDouble(x), Convert.ToDouble(y));
            else
                r = string.CompareOrdinal(Convert.ToString(x), Convert.ToString(y));
            if (r < 0)
                return -1;
            else if (r > 0)
                return +1;
            else return 0;
        }

        public static int IndexOfDiffTimed(this object[] va, object[] vb)
        {
            for (int i = 0; i < va.Length; i++)
            {
                int r = CmpObj(va[i], vb[i]);
                if (r != 0)
                    return i;
                r = CmpTimeOf(va[i], vb[i]);
                if (r != 0)
                    return i;
            }
            return -1;
        }

        public static int Compare(this object[] va, object[] vb)
        {
            for (int i = 0; i < va.Length; i++)
            {
                int r = CmpObj(va[i], vb[i]);
                if (r != 0)
                    return r;
            }
            return 0;
        }

        public static int CompareTimed(this object[] va, object[] vb)
        {
            for (int i = 0; i < va.Length; i++)
            {
                int r = CmpObj(va[i], vb[i]);
                if (r != 0)
                    return r;
                r = CmpTimeOf(va[i], vb[i]);
                if (r != 0)
                    return r;
            }
            return 0;
        }

        public static int CompareTimedKeys(object[] values, int[] valuesNdxs, IList keys)
        {
            for (int i = 0; i < valuesNdxs.Length; i++)
            {
                var va = values[valuesNdxs[i]];
                int r = CmpTimedKeys(va, keys[i]);
                if (r != 0)
                    return r;
            }
            return 0;
        }

        public static int CompareKeysWithTimeRange(object[] values, int[] valuesNdxs, IList keys, ITimedObject timeRange)
        {
            for (int i = 0; i < valuesNdxs.Length; i++)
            {
                var va = values[valuesNdxs[i]];
                int r = CmpKeysWithTimeRange(va, keys[i], timeRange);
                if (r != 0)
                    return r;
            }
            return 0;
        }

        public static bool IsLowerVowel(this char c)
        {
            switch (c)
            {
                case 'a':
                case 'e':
                case 'i':
                case 'o':
                case 'u':
                    return true;
                default:
                    return false;
            }
            //long x = (long)(char.ToUpper(c)) - 64;
            //if (x * x * x * x * x - 51 * x * x * x * x + 914 * x * x * x - 6894 * x * x + 20205 * x - 14175 == 0) return true;
            //else return false;
        }

        /// <summary>
        /// Remove lowercase vowels from string to reach length not greater than specified maximum
        /// </summary>
        public static string DeLowerVowel(this string s, int maxLen)
        {
            if (s == null || s.Length < maxLen)
                return s;
            var sb = new StringBuilder(s);
            for (int i = sb.Length - 1; i >= 0 && sb.Length > maxLen; i--)
                if (IsLowerVowel(sb[i]))
                    sb.Remove(i, 1);
            if (sb.Length > maxLen)
                sb.Remove(maxLen, sb.Length - maxLen);
            return sb.ToString();
        }

        public static int CompareKeys(object[] values, int[] valuesNdxs, IList keys)
        {
            for (int i = 0; i < valuesNdxs.Length; i++)
            {
                var va = values[valuesNdxs[i]];
                int r = CmpKeys(va, keys[i]);
                if (r != 0)
                    return r;
            }
            return 0;
        }

        public static int CompareSameKeys(this object[] va, object[] vb, int[] keysNdxs)
        {
            foreach (int i in keysNdxs)
            {
                int r = Cmp.CmpKeys(va[i], vb[i]);
                if (r != 0)
                    return r;
            }
            return 0;
        }

        public static int CompareSameTimedKeys(this object[] va, object[] vb, int[] keysNdxs)
        {
            try
            {
                foreach (int i in keysNdxs)
                {
                    int r = CmpTimedKeys(va[i], vb[i]);
                    if (r != 0)
                        return r;
                }
                return 0;
            }
            catch { throw; }
        }

        public static int CmpTimeOf(object a, object b)
        {
            var ta = a as ITimedObject ?? TimedObject.FullRangeI;
            var tb = b as ITimedObject ?? TimedObject.FullRangeI;
            if (ta.EndTime < tb.Time)
                return -1;
            if (tb.EndTime < ta.Time)
                return +1;
            if (ta.Time == tb.Time && ta.EndTime == tb.EndTime)
                return 0;
            if (ta.Time < tb.Time)
                return -1;
            if (ta.Time > tb.Time)
                return +1;
            return 0;
        }

        static readonly IFormatProvider fmt = System.Globalization.CultureInfo.InvariantCulture;

        public static int CompareWith(this IConvertible ca, object b)
        {
            var cb = b as IConvertible;
            if (cb == null || cb.GetTypeCode() == TypeCode.Empty)
                return (ca == null || ca.GetTypeCode() == TypeCode.Empty) ? 0 : +1;
            switch (ca.GetTypeCode())
            {
                case TypeCode.DateTime:
                    return DateTime.Compare(ca.ToDateTime(fmt), cb.ToDateTime(fmt));
                case TypeCode.String:
                    return string.Compare(ca.ToString(fmt), cb.ToString(fmt));
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                    return Math.Sign(ca.ToInt64(fmt) - cb.ToInt64(fmt));
                case TypeCode.UInt64:
                case TypeCode.Decimal:
                    return decimal.Compare(ca.ToDecimal(fmt), cb.ToDecimal(fmt));
                case TypeCode.Single:
                case TypeCode.Double:
                    {
                        var va = ca.ToDouble(fmt);
                        var vb = cb.ToDouble(fmt);
                        var d = va - vb;
                        if (d < 0)
                            return -1;
                        else if (d > 0)
                            return +1;
                        return 0;
                    }
                case TypeCode.Object:
                    if (ca == b)
                        return 0;
                    //else if (ca == null) // will never occure
                    //{
                    //    if (b != null) return -1;
                    //    else return ca.GetHashCode() - b.GetHashCode();
                    //}
                    else if (b == null)
                        return +1;
                    else if (ca is TimedGuid)
                        throw new NotSupportedException("TimedGuid comparison with nonempty via IConvertible is not supported.");
                    else return ca.GetHashCode() - b.GetHashCode();
                case TypeCode.Empty:
                    return -1;
                default:
                    throw new InvalidOperationException(nameof(CompareWith) + " not support " + nameof(TypeCode) + "." + ca.GetTypeCode().ToString());
            }
        }

        public static int UnsafeCmpKeys(object a, object b)
        {
            var ca = (IConvertible)a;
            if (ca != null)
                return ca.CompareWith(b);
            else if (a is Guid ga)
            {
                if (b is Guid gb)
                    return ga.CompareTo(gb);
                else
                    return -1;
            }
            else
            {
                var ka = Convert.ToDecimal(a);
                var kb = Convert.ToDecimal(b);
                return decimal.Compare(ka, kb);
            }
        }

        public static int CmpKeys(object a, object b)
        {
            var a_empty = Utils.IsEmpty(a);
            var b_empty = Utils.IsEmpty(b);
            if (a_empty)
            {
                if (b_empty)
                    return 0;
                else return -1;
            }
            else if (b_empty)
                return +1;
            else
                return UnsafeCmpKeys(a, b);
        }

        public static int CmpTimedKeys(object a, object b)
        {
            var ia = a as ITimedObject;
            if (ia != null)
                return ia.CompareTimed(b);
            var a_empty = Utils.IsEmpty(a);
            var b_empty = Utils.IsEmpty(b);
            if (a_empty)
            {
                if (b_empty)
                    return 0;
                else return -1;
            }
            else if (b_empty)
                return +1;
            else
            {
                int r = UnsafeCmpKeys(a, b);
                if (r != 0)
                    return r;
                r = CmpTimeOf(a, b);
                return r;
            }
        }

        public static int CmpKeysWithTimeRange(object a, object b, ITimedObject timeRange)
        {
            var a_empty = Utils.IsEmpty(a);
            var b_empty = Utils.IsEmpty(b);
            if (a_empty)
            {
                if (b_empty)
                    return 0;
                else return -1;
            }
            else if (b_empty)
                return +1;
            else
            {
                int r = UnsafeCmpKeys(a, b);
                if (r != 0)
                    return r;
                var ta = (a as ITimedObject) ?? TimedObject.FullRangeI;
                var tb = timeRange;
                if (ta.EndTime <= tb.Time)
                    return -1;
                if (tb.EndTime <= ta.Time)
                    return +1;
                return 0;
            }
        }

        public static int CmpTimed(object a, object b)
        {
            var ca = a as IConvertible;
            if (ca != null)
                return ca.CompareWith(b);
            return CmpTimeOf(a, b);
        }

        public static int CmpObj(object a, object b)
        {
            var ca = a as IConvertible;
            if (ca != null)
                return ca.CompareWith(b);
            // any objects comparison
            if (a == b)
                return 0;
            else if (a == null)
            {
                if (b != null) return -1;
                else return a.GetHashCode() - b.GetHashCode();
            }
            else if (b == null)
                return +1;
            else return a.GetHashCode() - b.GetHashCode();
        }
    }

    public static class Utils
    {

#if NET40
		class AsyncSemaphore : IAsyncSemaphore
		{
			Nito.AsyncEx.AsyncSemaphore sema;
			public AsyncSemaphore(int initialCount) { sema = new Nito.AsyncEx.AsyncSemaphore(initialCount); }
			public Task WaitAsync(CancellationToken ct) { return sema.WaitAsync(ct); }
			public void Release(int releaseCount = 1) { sema.Release(releaseCount); }
			public void Dispose() { }
		}

		class AsyncLock : IAsyncLock
		{
			Nito.AsyncEx.AsyncAutoResetEvent evt;
			public AsyncLock(bool locked) { evt = new Nito.AsyncEx.AsyncAutoResetEvent(!locked); }
			public Task WaitAsync(CancellationToken ct) { return evt.WaitAsync(ct); }
			public void Release() { evt.Set(); }
			public void Dispose() { }
		}

		public static async Task<object> TaskFromResult(object result)
		{
			await Nito.AsyncEx.TaskConstants.Completed;
			return result;
			//var tcs = new TaskCompletionSource<object>();
			//tcs.SetResult(result);
			//return tcs.Task;
		}

#else
        public class AsyncSemaphore : IAsyncSemaphore
        {
            System.Threading.SemaphoreSlim sema;
            public AsyncSemaphore(int initialCount) { sema = new SemaphoreSlim(initialCount, initialCount); }
            public Task WaitAsync(CancellationToken ct) { return sema.WaitAsync(ct); }
            public void Release(int releaseCount = 1) { sema.Release(releaseCount); }
            public void Dispose()
            {
                if (sema == null)
                    return;
                var s = Interlocked.CompareExchange(ref sema, null, sema);
                if (s != null)
                    s.Dispose();
            }
        }

        public class AsyncLock : IAsyncLock
        {
            System.Threading.SemaphoreSlim sema;
            public AsyncLock(bool locked) { sema = new SemaphoreSlim(locked ? 0 : 1, 1); }
            public Task WaitAsync(CancellationToken ct) { return sema.WaitAsync(ct); }
            public void Release() { sema.Release(); }
            public void Dispose()
            {
                if (sema == null)
                    return;
                var s = Interlocked.CompareExchange(ref sema, null, sema);
                if (s != null)
                    s.Dispose();
            }
        }

        public static Task<object> TaskFromResult(object result)
        {
            return Task.FromResult(result);
        }
#endif

        public static IAsyncLock NewAsyncLock(bool locked = false)
        {
            return new AsyncLock(locked);
        }

        public static IAsyncSemaphore NewAsyncSemaphore(int initialCount)
        {
            return new AsyncSemaphore(initialCount);
        }

        public static bool NotNullsFirstN(this object[] args, int n)
        {
            for (int i = n - 1; i >= 0; i--)
                if (IsEmpty(args[i]))
                    return false;
            return true;
        }

        public static bool NotNullsAt(this object[] args, params int[] atIndexes)
        {
            foreach (int i in atIndexes)
                if (IsEmpty(args[i]))
                    return false;
            return true;
        }

        public static bool Once(ref int flag)
        { return Interlocked.CompareExchange(ref flag, 1, 0) == 0; }

        public static int InterlockedCaptureIndex(ref int indexBits, int maxIndex)
        {
            int index;
            int bits = indexBits;
            while (true)
            {
                index = maxIndex;
                while (index >= 0 && (bits & (1 << index)) != 0)
                    index--;
                System.Diagnostics.Trace.Assert(index >= 0, "No free index to capture!");
                int newBits = bits | (1 << index);
                int tmp = Interlocked.CompareExchange(ref indexBits, newBits, bits);
                if (tmp == bits)
                    break;
                bits = tmp;
            }
            return index;
        }

        public static bool InterlockedTryCaptureIndex(ref int indexBits, int index)
        {
            int oldBits = indexBits;
            int newBits = oldBits | (1 << index);
            if (oldBits == newBits)
                return false;
            int tmp = Interlocked.CompareExchange(ref indexBits, newBits, oldBits);
            return (tmp == oldBits);
        }

        public static void InterlockedReleaseIndex(ref int indexBits, int index)
        {
            int bits = indexBits;
            if ((bits & (1 << index)) == 0)
                throw new InvalidOperationException("InterlockedReleaseIndex: can't release free index");
            while (true)
            {
                int newBits = bits & ~(1 << index);
                int tmp = Interlocked.CompareExchange(ref indexBits, newBits, bits);
                if (tmp == bits)
                    break;
                bits = tmp;
            }
        }

        public static IList ToIList(object value)
        {
            var to = value as ITimedObject;
            if (to != null)
                return (IList)to.Object;
            else
                return (IList)value;
        }

        public static IList AsIList(object value)
        {
            var to = value as ITimedObject;
            if (to != null)
                value = to.Object;
            var lst = value as IList;
            if (lst == null)
                return new object[] { value };
            return lst;
        }

        public static IList TryAsIList(object value)
        {
            var to = value as ITimedObject;
            if (to != null)
                value = to.Object;
            var lst = value as IList;
            return lst;
        }

        public static T Cast<T>(object value)
        {
            var to = value as ITimedObject;
            if (to != null)
                return (T)to.Object;
            else
                return (T)value;
        }

        public static bool TryCastStruct<T>(object value, out T res) where T : struct
        {
            var to = value as ITimedObject;
            var v = (to != null) ? to.Object : value;
            if (v is T)
            { res = (T)v; return true; }
            else
            { res = default(T); return false; }
        }

        public static bool TryCastClass<T>(object value, out T res) where T : class
        {
            var to = value as ITimedObject;
            var v = (to != null) ? to.Object : value;
            res = v as T;
            return res != null;
        }

        public static Double? GetDouble(object value)
        {
            var to = value as ITimedObject;
            var v = value;
            if (to != null)
                v = to.Object;

            if (v == null)
                return null;

            if (v is Double)
                return (Double)v;
            else if (v is Single)
                return (Double)(Single)v;
            else if (v is Decimal)
                return (Double)(Decimal)v;
            else if (v is Int32)
                return (Double)(Int32)v;
            else if (v is Int64)
                return (Double)(Int64)v;

            return null;
        }

        public static Decimal? GetDecimal(object value)
        {
            var to = value as ITimedObject;
            var v = value;
            if (to != null)
                v = to.Object;

            if (v == null)
                return null;

            if (v is Double)
                return (Decimal)(Double)v;
            else if (v is Single)
                return (Decimal)(Single)v;
            else if (v is Decimal)
                return (Decimal)v;
            else if (v is Int32)
                return (Decimal)(Int32)v;
            else if (v is Int64)
                return (Decimal)(Int64)v;

            return null;
        }

        public static Int32? GetInt32(object value)
        {
            var to = value as ITimedObject;
            var v = value;
            if (to != null)
                v = to.Object;

            if (v == null)
                return null;

            if (v is Double)
                return (Int32)(Double)v;
            else if (v is Single)
                return (Int32)(Single)v;
            else if (v is Decimal)
                return (Int32)(Decimal)v;
            else if (v is Int32)
                return (Int32)v;
            else if (v is Int64)
                return (Int32)(Int64)v;

            return null;
        }

        public static string IListToString(this ICollection lst)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            sb.Append(lst.Count);
            sb.Append("]{");
            bool first = true;
            foreach (object item in lst)
            {
                if (first)
                    first = false;
                else sb.Append(", ");
                string tmp;
                if (NumberUtils.TryNumberToString(item, out tmp))
                    sb.Append(tmp);
                else sb.Append(item);
            }
            sb.Append('}');
            return sb.ToString();
        }

        public static string IListToText(this ICollection lst)
        {
            var sb = new System.Text.StringBuilder();
            bool first = true;
            var sbTimes = new System.Text.StringBuilder();
            foreach (object item in lst)
            {
                if (first)
                    first = false;
                else
                { sb.Append('\t'); sbTimes.Append('\t'); }
                string tmp;
                if (NumberUtils.TryNumberToString(item, out tmp))
                    sb.Append(tmp);
                else sb.Append(item);
                var to = item as ITimedObject;
                if (to != null)
                    sbTimes.Append(to.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            sb.AppendLine();
            sb.Append(sbTimes);
            return sb.ToString();
        }

        static void ToString(object data,
            Dictionary<object, bool> alreadyProcessed,
            System.Text.StringBuilder sb,
            bool mayLineFeed)
        {
            try
            {
                if (data == null)
                {
                    sb.Append('○');
                    return;
                }
                var type = data.GetType();
                if (type.IsPrimitive || type == typeof(string))
                {
                    sb.Append(Convert.ToString(data));
                    return;
                }
                var cnv = data as IConvertible;
                if (cnv != null)
                {
                    sb.Append(cnv.ToString());
                    return;
                }
                var toStrMethod = type.GetMethod("ToString", System.Reflection.BindingFlags.Instance);
                if (toStrMethod != null)
                {
                    var txt = toStrMethod.Invoke(data, null);
                    sb.Append(txt);
                    return;
                }
                if (alreadyProcessed.ContainsKey(data))
                {
                    sb.Append('↑');
                    return;
                }
                alreadyProcessed.Add(data, true);
                sb.Append('{');
                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var field in fields)
                {
                    var value = field.GetValue(data);
                    sb.Append(field.Name);
                    sb.Append('=');
                    ToString(value, alreadyProcessed, sb, mayLineFeed);
                    sb.Append("; ");
                }
                var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var prop in props)
                {
                    if (!prop.CanRead)
                        continue;
                    var indexes = prop.GetIndexParameters();
                    if (indexes.Length > 0)
                        continue;
                    var value = prop.GetValue(data, null);
                    sb.Append(prop.Name);
                    sb.Append('=');
                    ToString(value, alreadyProcessed, sb, mayLineFeed);
                    sb.Append("; ");
                }
                sb.Append('}');
                if (mayLineFeed)
                    sb.Append('\n');
            }
            catch (Exception ex) { sb.Append(ex.Message); }
        }

        public static string ToString(object data, bool mayLineFeed)
        {
            var sb = new System.Text.StringBuilder();
            var dict = new Dictionary<object, bool>();
            ToString(data, dict, sb, mayLineFeed);
            return sb.ToString();
        }

        public static string ToStr(DateTime time)
        {
            string fmt;
            if (time.Second > 0)
                fmt = "yyyy-MM-dd HH:mm:ss";
            else if (time.Minute > 0 || time.Hour > 0)
                fmt = "yyyy-MM-dd HH:mm";
            else
                fmt = "yyyy-MM-dd";
            return time.ToString(fmt);
        }

        public static void ToLog(string msg, object info)
        {
            Diagnostics.Logger.TraceInformation("{0}\t{1}", msg, info);
        }

        public static object CalcNotNulls(object arg, int nIns, int nOuts, Func<object[], object> calc)
        {
            var dict = (IIndexedDict)arg;
            var data = dict.ValuesList;
            if (data.NotNullsFirstN(nIns) == false)
                return null;
            int n = data.Length;
            object[] outs;
            try
            {
                var res = calc(data);
                if (res == null)
                    return null;
                if (n == nIns)
                    return res;
                outs = new object[nOuts + n - nIns];
                var lst = res as IList;
                if (lst != null)
                    lst.CopyTo(outs, 0);
                else outs[0] = res;
            }
            catch (Exception ex)
            {
                var err = new ErrorWrapper(ex);
                outs = new object[nOuts + n - nIns];
                for (int i = 0; i < nOuts; i++) outs[i] = err;
                ToLog(ex.Message, dict);
            }
            for (int i = n - 1; i >= nIns; i--)
                outs[nOuts + i - nIns] = data[i];
            return outs;
        }

        public static object Calc(object arg, int nIns, int nOuts, Func<object[], object> calc, params int[] indexesOfNotNullableArgs)
        {
            var dict = (IIndexedDict)arg;
            var data = dict.ValuesList;
            if (data.NotNullsAt(indexesOfNotNullableArgs) == false)
                return null;
            int n = data.Length;
            object[] outs;
            try
            {
                var res = calc(data);
                if (res == null)
                    return null;
                if (n == nIns)
                    return res;
                outs = new object[nOuts + n - nIns];
                var lst = res as IList;
                if (lst != null)
                    lst.CopyTo(outs, 0);
                else outs[0] = res;
            }
            catch (Exception ex)
            {
                var err = new ErrorWrapper(ex);
                outs = new object[nOuts + n - nIns];
                for (int i = 0; i < nOuts; i++) outs[i] = err;
                ToLog(ex.Message, dict);
            }
            for (int i = n - 1; i >= nIns; i--)
                outs[nOuts + i - nIns] = data[i];
            return outs;
        }

        public static readonly Func<object, object> Coalesce2 =
            arg => Utils.Calc(arg, 2, 1, data =>
            {
                if (data[0] != null)
                    return data[0];
                else return data[1];
            });

        public static TypeCode GetTypeCode(object x)
        {
            if (x == null || x == DBNull.Value)
                return TypeCode.Empty;
            var ic = x as IConvertible;
            return (ic == null) ? TypeCode.Object : ic.GetTypeCode();
        }

        public static bool IsEmpty(object x)
        {
            if (x == null || x == DBNull.Value)
                return true;
            var ic = x as IConvertible;
            if (ic != null)
            {
                var tc = ic.GetTypeCode();
                if (tc == TypeCode.Empty || tc == TypeCode.DBNull)
                    return true;
                if (tc == TypeCode.String && ic.ToString().Length == 0)
                    return true;
            }
            return false;
        }

        public static object NaN2null(float x) { return float.IsNaN(x) ? null : (object)x; }
        public static object NaN2null(double x) { return double.IsNaN(x) ? null : (object)x; }
        public static float Cnv(object x) { if (IsEmpty(x)) return float.NaN; else return Convert.ToSingle(x); }
        public static float Cnv(object x, float defVal) { if (IsEmpty(x)) return defVal; else return Convert.ToSingle(x); }
        public static double CnvToDbl(object x) { if (IsEmpty(x)) return double.NaN; else return Convert.ToDouble(x); }
        public static double CnvToDbl(object x, double defVal) { if (IsEmpty(x)) return defVal; else return Convert.ToDouble(x); }
        public static int CnvToLogic(object x) { if (IsEmpty(x)) return -1; else return Convert.ToBoolean(x) ? 1 : 0; }

        public static DateTime CnvToDateTime(object x)
        {
            if (IsEmpty(x))
                return DateTime.MinValue;
            var to = x as ITimedObject;
            if (to != null)
                return to.Time;
            return Convert.ToDateTime(x);
        }
        public static DateTime CnvToDateTimeMax(object x)
        {
            if (IsEmpty(x))
                return DateTime.MaxValue;
            var to = x as ITimedObject;
            if (to != null)
                return to.Time;
            return Convert.ToDateTime(x);
        }
    }

    public class KeysComparer : IEqualityComparer<object[]>, IComparer<object[]>
    {
        int[] ndxs;
        public KeysComparer(params int[] ndxs) { this.ndxs = ndxs; }
        public int Compare(object[] x, object[] y)
        {
            foreach (int i in ndxs)
            {
                int r = Cmp.CmpKeys(x[i], y[i]);
                if (r != 0) return r;
            }
            return 0;
        }
        public bool Equals(object[] x, object[] y) { return Compare(x, y) == 0; }
        public int GetHashCode(object[] obj) { return obj[0].GetHashCode(); }
    }

    public class TimedDataComparer : IEqualityComparer<object[]>, IComparer<object[]>
    {
        int[] ndxs;
        public TimedDataComparer(params int[] keysNdxs) { this.ndxs = keysNdxs; }
        public int Compare(object[] x, object[] y)
        {
            int r = x.CompareSameTimedKeys(y, ndxs);
            if (r != 0)
                return r;
            else return x.CompareTimed(y);
        }
        public bool Equals(object[] x, object[] y) { return Compare(x, y) == 0; }
        public int GetHashCode(object[] obj)
        {
            int hash = 0;
            int n = 3;
            foreach (int i in ndxs)
                if (obj[i] != null)
                {
                    var to = obj[i] as ITimedObject;
                    if (to != null)
                        hash ^= to.GetHashCode() ^ to.Time.GetHashCode();
                    else
                        hash ^= obj[i].GetHashCode();
                    if (--n == 0) break;
                }
            return hash;
        }
    }

    public static class NumberUtils
    {
        // По статье "Как проверить, является ли строка числом, e-mail'ом?"
        // http://www.rsdn.ru/article/alg/checkStr.xml
        //Число с плавающей точкой
        //***** БНФ
        //<Real_number> ::= <Mantissa> [ <E> <Exponent> ]
        //<Mantissa> :: = [<sign>] <Integer_part> [ <dot> [<Fractional_part>] ]
        //<Exponent> ::= [<sign>] <Unsigned_number>
        //<Integer_part> ::= <Unsigned_number>
        //<Fractional_part> ::= <Unsigned_number>
        //<Unsigned_number> ::= <digit> <digit>*
        //<sign> ::= + | -
        //<e> ::= E | e
        //<dot> ::= .
        //***** Регулярное выражение
        //[<sign>] <digit> <digit>* [<dot> <digit>* ] [ <e> [<sign>] <digit> <digit>* ]
        //***** Правила для автомата
        //Start = Mantissa1
        //Mantissa1 - начало мантиссы
        //    Mantissa1, <sign> -> Mantissa2
        //    Mantissa1, <digit> -> Mantissa3
        //Mantissa2 - начало целой части мантиссы
        //    Mantissa2, <digit> -> Mantissa3
        //    Mantissa2, <end> -> Success
        //Mantissa3 - наличие хотя бы одной цифры
        //    Mantissa3, <digit> -> Mantissa3
        //    Mantissa3, <dot> -> Mantissa4
        //    Mantissa3, <e> -> Exponent1
        //    Mantissa3, <end> -> Success
        //    Mantissa3, <sign> -> Back
        //Mantissa4 - дробная часть
        //    Mantissa4, <digit> -> Mantissa4
        //    Mantissa4, <e> -> Exponent1
        //    Mantissa4, <end> -> Success
        //    Mantissa4, <sign> -> Back
        //Exponent1 - начало порядка
        //    Exponent1, <sign> -> Exponent2
        //    Exponent1, <digit> -> Exponent3
        //Exponent2 - начало числа в порядке
        //    Exponent2, <digit> -> Exponent3
        //    Exponent2, <end> -> Success
        //Exponent3 - продолжение числа в порядке
        //    Exponent3, <digit> -> Exponent3
        //    Exponent3, <end> -> Success
        //    Exponent3, <sign> -> Back
        //(Во всех остальных случаях -> Error)
        // bk - откат на символ и ok

        enum S { m1, m2, m3, m4, e1, e2, e3, maxCount, ok, er, bk };
        //                                                  m1    m2    m3    m4    e1    e2    e3
        static readonly S[] tEnd = new S[(int)S.maxCount] { S.er, S.er, S.ok, S.ok, S.er, S.er, S.ok };
        static readonly S[] tDgt = new S[(int)S.maxCount] { S.m3, S.m3, S.m3, S.m4, S.e3, S.e3, S.e3 };
        static readonly S[] tSgn = new S[(int)S.maxCount] { S.m2, S.er, S.bk, S.bk, S.e2, S.er, S.bk };
        static readonly S[] tDot = new S[(int)S.maxCount] { S.m4, S.er, S.m4, S.er, S.er, S.er, S.er };
        static readonly S[] tExp = new S[(int)S.maxCount] { S.er, S.er, S.e1, S.e1, S.er, S.er, S.er };

        public static string GetFloatStr(string str, int fromNdx)
        {
            S state = S.m1;
            int i = fromNdx;
            do
            {
                S[] sv; // state vector
                if (i >= str.Length)
                    sv = tEnd;
                else
                {
                    char c = str[i++];
                    if (char.IsDigit(c))
                        sv = tDgt;
                    else if (c == '-')
                        sv = tSgn;
                    else if (c == '.')
                        sv = tDot;
                    else if (char.ToUpper(c) == 'E')
                        sv = tExp;
                    else
                    {
                        i--;
                        sv = tEnd;
                    }
                }
                state = sv[(int)state];
            } while (state < S.maxCount);
            if (state == S.ok || (state == S.bk && --i > fromNdx))
                return str.Substring(fromNdx, i - fromNdx);
            else
                return null;
        }

        public static bool IsNumber(object v)
        {
            if (v is double)
                return !double.IsNaN((double)v);
            if (v is float)
                return !float.IsNaN((float)v);
            if (v is int)
                return true;
            var c = v as IConvertible;
            if (c != null)
                switch (c.GetTypeCode())
                {
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                    case TypeCode.Decimal:
                        return true;
                    case TypeCode.Single:
                        return !float.IsNaN(c.ToSingle(System.Globalization.CultureInfo.InvariantCulture));
                    case TypeCode.Double:
                        return !double.IsNaN(c.ToDouble(System.Globalization.CultureInfo.InvariantCulture));
                    default:
                        return false;
                }
            return false;
        }

        static readonly char[] pointNotNeededChars = { '.', 'E', 'e' };

        static string WithFloatPoint(string s)
        {
            if (s.IndexOfAny(pointNotNeededChars) >= 0)
                return s;
            return s + ".0";
        }

        public static bool TryNumberToString(object v, out string s)
        {
            var to = v as ITimedObject;
            if (to != null)
                v = to.Object;
            if (v is double)
                s = WithFloatPoint(((double)v).ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (v is float)
                s = WithFloatPoint(((float)v).ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (v is int)
                s = ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else if (v is long)
                s = ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else { s = null; return false; }
            return true;
        }
    }

    public static class ExceptionExtensions
    {
#if NET40
		static readonly System.Reflection.MethodInfo s_InternalPreserveStackTrace =
			typeof(Exception).GetMethod("InternalPreserveStackTrace",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

		public static Exception PrepareForRethrow(this Exception exception)
		{
			if (exception == null)
				return null;
			if (s_InternalPreserveStackTrace != null)
				try
				{
					s_InternalPreserveStackTrace.Invoke(exception, null);
					return exception;
				}
				catch (MethodAccessException) { }
			using (System.IO.MemoryStream serializationStream = new System.IO.MemoryStream(0x3e8))
			{
				var formatter =
					new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter(
						null, new System.Runtime.Serialization.StreamingContext(
							System.Runtime.Serialization.StreamingContextStates.CrossAppDomain));
				formatter.Serialize(serializationStream, exception);
				serializationStream.Seek((long)0, System.IO.SeekOrigin.Begin);
				return (Exception)formatter.Deserialize(serializationStream);
			}
		}
#else
        public static Exception PrepareForRethrow(this Exception ex)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
            return ex;
        }
#endif

        public static object Wrap(this Exception ex) { return new ErrorWrapper(ex); }
    }
}

#if !NET40
namespace System.Threading.Tasks
{
    public static class TaskEx
    {
        public static Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks) { return Task.WhenAll(tasks); }
        public static Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks) { return Task.WhenAll(tasks); }
        public static Task WhenAll(IEnumerable<Task> tasks) { return Task.WhenAll(tasks); }
    }
}
#endif