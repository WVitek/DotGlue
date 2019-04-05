using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;

using System.Threading;
using System.Threading.Tasks;

namespace W.Expressions
{
    using System.Diagnostics;
    using W.Common;

    //[System.Diagnostics.DebuggerStepThrough]
    public static class OPs
    {
        static bool EqualsImpl(this ArrayList a, object b, IEqualityComparer comparer)
        {
            var lst = b as IList;
            if (lst == null || lst.Count != a.Count)
                return false;
            for (int i = a.Count - 1; i >= 0; i--)
                if (!comparer.Equals(lst[i], a[i]))
                    return false;
            return true;
        }

        static int GetHashCodeImpl(this ArrayList a, IEqualityComparer comparer)
        {
            int code = 0x7C1134FD;
            for (int i = a.Count - 1; i >= 0; i--)
            {
                code ^= comparer.GetHashCode(a[i] ?? string.Empty);
                if (code < 0)
                    code = (code << 1) + 1;
                else
                    code = code << 1;
            }
            return code;
        }

        /// <summary>
        /// List to store async lazy values
        /// </summary>
        public class ListOfVals : ArrayList, IStructuralEquatable
        {
            public ListOfVals(ICollection c) : base(c) { }
            public ListOfVals(int capacity) : base(capacity) { }
            public override string ToString() { return W.Common.Utils.IListToString(this); }

            public bool Equals(object other, IEqualityComparer comparer) { return this.EqualsImpl(other, comparer); }
            public int GetHashCode(IEqualityComparer comparer) { return this.GetHashCodeImpl(comparer); }
        }

        /// <summary>
        /// List to store sync lazy values
        /// </summary>
        public class ListOfSync : ArrayList, IStructuralEquatable
        {
            public ListOfSync(int capacity) : base(capacity) { }
            public ListOfSync(ICollection c) : base(c) { }
            public override string ToString() { return W.Common.Utils.IListToString(this); }

            public bool Equals(object other, IEqualityComparer comparer) { return this.EqualsImpl(other, comparer); }
            public int GetHashCode(IEqualityComparer comparer) { return this.GetHashCodeImpl(comparer); }
        }

        /// <summary>
        /// Printable list with constant values
        /// </summary>
        [Serializable]
        public class ListOfConst : ArrayList, IStructuralEquatable
        {
            public static readonly ListOfConst Empty = new ListOfConst(0);
            public ListOfConst() : base() { }
            public ListOfConst(int capacity) : base(capacity) { }
            public ListOfConst(ICollection c) : base(c) { }
            public override string ToString() { return W.Common.Utils.IListToString(this); }

            public bool Equals(object other, IEqualityComparer comparer) { return this.EqualsImpl(other, comparer); }
            public int GetHashCode(IEqualityComparer comparer) { return this.GetHashCodeImpl(comparer); }
        }

        /// <summary>
        /// Printable list with T values
        /// </summary>
        public class ListOf<T> : List<T>
        {
            public static readonly ListOf<T> Empty = new ListOf<T>(0);
            public ListOf() : base() { }
            public ListOf(int capacity) : base(capacity) { }
            public override string ToString() { return W.Common.Utils.IListToString(this); }
        }

        static int nextIdentifierID = 0;
        public static string UniqIdentifier(string prefix)
        { return prefix + ((uint)System.Threading.Interlocked.Increment(ref nextIdentifierID)).ToString(); }

        public static bool TryParseDouble(string s, out double res)
        {
            return double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out res);
        }

        static double ic2dbl(IConvertible c)
        {
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
                case TypeCode.Single:
                case TypeCode.Double:
                    return c.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                case TypeCode.Boolean:
                    return c.ToBoolean(System.Globalization.CultureInfo.InvariantCulture) ? 1 : 0;
                case TypeCode.String:
                    {
                        double res;
                        if (double.TryParse(
                            c.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out res)
                        )
                            return res;
                        else return double.NaN;
                    }
                default:
                    return double.NaN;
            }
        }

        /// <summary>
        /// Значения в число (пустые значения -> NaN)
        /// </summary>
        /// <param name="v">значение</param>
        /// <returns>число</returns>
        public static double xl2dbl(object v)
        {
            if (v == null)
                return double.NaN; //0
            string s = v as string;
            if (s != null)
            {
                if (s.Length == 0)
                    return double.NaN; //0
                double tmp;
                if (TryParseDouble(s.ToString(), out tmp))
                    return tmp;
            }
            var c = v as IConvertible;
            if (c != null)
                return ic2dbl(c);
            return double.NaN;
        }

        /// <summary>
        /// Конвертирование значения в число в Excel-стиле (пустые значения -> null)
        /// </summary>
        /// <param name="v">значение</param>
        /// <returns>число</returns>
        public static double? xl2dbl2(object v)
        {
            if (v == null)
                return null;
            string s = v as string;
            if (s != null)
            {
                if (s.Length == 0)
                    return null;
                double tmp;
                if (TryParseDouble(s.ToString(), out tmp))
                    return tmp;
            }
            var c = v as IConvertible;
            if (c != null)
                return ic2dbl(c);
            return double.NaN;
        }

        public static double ToExcelDate(DateTime dt) { return dt.ToOADate(); }
        public static DateTime FromExcelDate(double xlDate) { return DateTime.FromOADate(xlDate); }

        public static bool xl2bool(object v)
        {
            return Convert.ToBoolean(v);
        }

        public static ValueKind KindOf(object value)
        {
            if (value is LazyAsync)
                return ValueKind.Async; // asynchronous function without args
            if (value is LazySync || value is ListOfSync)
                return ValueKind.Sync; // synchronous function without args
            else if (value is ListOfConst) // special ListOfConst may contains only constants
                return ValueKind.Const;
            else if (value is ListOfVals) // IList may contains LazyAsync values
                return ValueKind.Async;
            else if (value == Generator.Undefined)
                return ValueKind.Undef;
            else
                return ValueKind.Const;  // any object with other type we treat as constant
        }

        public static ValueKind MaxKindOf(params object[] values)
        {
            int max = 0;
            foreach (var v in values)
            {
                int vk = (int)KindOf(v);
                if (max < vk)
                {
                    max = vk;
                    if (vk == (int)ValueKind.MaxValue)
                        break;
                }
            }
            return (ValueKind)max;
        }

#if NET40
        public static readonly LazyAsync EmptyLazyAsync = ae => TaskEx.FromResult<object>(null);
#else
        public static readonly LazyAsync EmptyLazyAsync = ae => Task.FromResult<object>(null);
#endif

        /// <summary>
        /// Flatten all sublists (with IList interface)
        /// </summary>
        /// <param name="args">list to flatten</param>
        /// <returns>All scalar (w/o IList interface) values!=null</returns>
        public static IEnumerable Flatten(IList args, int maxDepth = int.MaxValue)
        {
            foreach (object item in args)
            {
                if (item == null) continue;
                IList lst = item as IList;
                if (lst == null || maxDepth <= 0)
                    yield return item;
                else
                    foreach (object tmp in Flatten(lst, maxDepth - 1))
                        yield return tmp;
            }
        }

        public static int cmp(object x, object y)
        {
            int r;
            if (W.Common.NumberUtils.IsNumber(x) && W.Common.NumberUtils.IsNumber(y))
            {
                var dx = Convert.ToDouble(x);
                var dy = Convert.ToDouble(y);
                r = Comparer<double>.Default.Compare(dx, dy);
            }
            else
                r = string.CompareOrdinal(Convert.ToString(x), Convert.ToString(y));
            if (r < 0)
                return -1;
            else if (r > 0)
                return +1;
            else return 0;
        }

        public static object WithTime(object x, object y, object r)
        {
            var tx = x as ITimedObject;
            var ty = y as ITimedObject;
            if (tx == null && ty == null)
                return r;
            tx = tx ?? TimedObject.FullRangeI;
            ty = ty ?? TimedObject.FullRangeI;
            var time = (tx.Time < ty.Time) ? ty.Time : tx.Time;
            var endTime = (tx.EndTime < ty.EndTime) ? tx.EndTime : ty.EndTime;
            var tr = TimedObject.Timed(time, endTime, r);
            return tr;
        }

        public static object POW(object x, object y) { return Math.Pow(xl2dbl(x), xl2dbl(y)); }
        public static object PLUS(object x, object y) { return xl2dbl(x) + xl2dbl(y); }
        public static object MINUS(object x, object y) { return xl2dbl(x) - xl2dbl(y); }
        public static object MUL(object x, object y) { return xl2dbl(x) * xl2dbl(y); }
        public static object DIV(object x, object y) { return xl2dbl(x) / xl2dbl(y); }
        public static object CONCAT(object x, object y) { return string.Concat(x, y); }
        public static object EQ(object x, object y) { return cmp(x, y) == 0; }
        public static object NE(object x, object y) { return cmp(x, y) != 0; }
        public static object LT(object x, object y) { return cmp(x, y) < 0; }
        public static object GT(object x, object y) { return cmp(x, y) > 0; }
        public static object LE(object x, object y) { return cmp(x, y) <= 0; }
        public static object GE(object x, object y) { return cmp(x, y) >= 0; }

        public static object t_POW(object x, object y) { return WithTime(x, y, Math.Pow(xl2dbl(x), xl2dbl(y))); }
        public static object t_PLUS(object x, object y) { return WithTime(x, y, xl2dbl(x) + xl2dbl(y)); }
        public static object t_MINUS(object x, object y) { return WithTime(x, y, xl2dbl(x) - xl2dbl(y)); }
        public static object t_MUL(object x, object y) { return WithTime(x, y, xl2dbl(x) * xl2dbl(y)); }
        public static object t_DIV(object x, object y) { return WithTime(x, y, xl2dbl(x) / xl2dbl(y)); }
        public static object t_CONCAT(object x, object y) { return WithTime(x, y, string.Concat(x, y)); }
        public static object t_EQ(object x, object y) { return WithTime(x, y, cmp(x, y) == 0); }
        public static object t_NE(object x, object y) { return WithTime(x, y, cmp(x, y) != 0); }
        public static object t_LT(object x, object y) { return WithTime(x, y, cmp(x, y) < 0); }
        public static object t_GT(object x, object y) { return WithTime(x, y, cmp(x, y) > 0); }
        public static object t_LE(object x, object y) { return WithTime(x, y, cmp(x, y) <= 0); }
        public static object t_GE(object x, object y) { return WithTime(x, y, cmp(x, y) >= 0); }

        [DebuggerHidden]
        /// <summary>
		/// Use this method with precaution: only if your code parse result as IList immediately
		/// </summary>
		public static async Task<object> VectorValueOf(AsyncExprCtx ae, object funcOrValue)
        {
            while (true)
            {
                var asyncF = funcOrValue as LazyAsync;
                if (asyncF != null)
                    funcOrValue = await asyncF(ae);
                else
                {
                    var syncF = funcOrValue as LazySync;
                    if (syncF != null)
                        funcOrValue = syncF();
                    else break;
                }
            }
            //IList list = funcOrValue as IList;
            //if (list == null)
            //    funcOrValue = new OneValueList(funcOrValue);
            return funcOrValue;
        }

        public static object VectorValueOf(object funcOrValue)
        {
            while (true)
            {
                LazySync syncF = funcOrValue as LazySync;
                if (syncF != null)
                    funcOrValue = syncF();
                else break;
            }
            IList list = funcOrValue as IList;
            if (list == null)
                return new OneValueList(funcOrValue);
            else return funcOrValue;
        }

        public static async Task<object> _call_func(AsyncExprCtx ae, int funcIndex, IList args)
        {
            var fObj = await ae.GetValue(funcIndex);
            var f = fObj as AsyncFn ?? ((FuncDef)fObj).func as AsyncFn;
            if (f == null)
                throw new InvalidCastException("_call_func: AsyncFn expected");
            return await f(ae, args);
        }

        public static async Task ConstValueOf(AsyncExprCtx ae, object funcOrValue, Action<object, object> onComplete, object state)
        {
            try
            {
                var res = await ConstValueOf(ae, funcOrValue);
                onComplete(res, state);
            }
            catch (Exception ex) { onComplete(ex.Wrap(), state); }
        }

        public static async Task<object> ConstValueOf(AsyncExprCtx ae, object funcOrValue)
        {
            while (true)
            {
                var asyncF = funcOrValue as LazyAsync;
                if (asyncF != null)
                {
                    ae.Cancellation.ThrowIfCancellationRequested();
                    funcOrValue = await asyncF(ae);
                }
                else
                {
                    var syncF = funcOrValue as LazySync;
                    if (syncF != null)
                        funcOrValue = syncF();
                    else break;
                }
            }
            var list = funcOrValue as IList;
            if (list != null && list.Count > 0)
            {
                if (list is ListOfVals)
                {
                    var res = new object[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        object obj = list[i];
                        res[i] = await ConstValueOf(ae, obj);
                    }
                    return res;
                }
                else if (list is ListOfSync)
                    return ConstValueOf(funcOrValue);
                else return funcOrValue;
            }
            else return funcOrValue;
        }

        public static object ConstValueOf(object funcOrValue)
        {
            while (true)
            {
                LazySync syncF = funcOrValue as LazySync;
                if (syncF != null)
                    funcOrValue = syncF();
                else break;
            }
            IList list = funcOrValue as IList;
            if (list != null && list.Count > 0)
            {
                if (list is ListOfSync)
                {
                    var res = new object[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        res[i] = ConstValueOf(list[i]);
                    return res;
                }
            }
            return funcOrValue;
        }

        //public static T Cached<T>(string cacheKey, Func<T> getter, System.Web.Caching.CacheDependency dep,
        //	TimeSpan slidingExpiration, DateTime absoluteExpiration)
        //{
        //	var obj = System.Web.HttpRuntime.Cache.Get(cacheKey);
        //	if (obj == null)
        //	{
        //		obj = getter();
        //		var old = System.Web.HttpRuntime.Cache.Add(cacheKey, obj, dep,
        //			(absoluteExpiration == DateTime.MaxValue) ? System.Web.Caching.Cache.NoAbsoluteExpiration : absoluteExpiration,
        //			(slidingExpiration == TimeSpan.MaxValue) ? System.Web.Caching.Cache.NoSlidingExpiration : slidingExpiration,
        //			System.Web.Caching.CacheItemPriority.Normal, null);
        //		if (old != null)
        //			obj = old;
        //	}
        //	return (T)obj;
        //}

        public static IAsyncSemaphore GlobalMaxParallelismSemaphore = W.Common.Utils.NewAsyncSemaphore(16);

        public static Task<object> GlobalValueOfAny(object funcOrValue, Generator.Ctx ctx, CancellationToken cancellation)
        {
            //if (nMaxParallelism < 1)
            //	nMaxParallelism = 128;
            //var sema = Cached<IAsyncSemaphore>("WExpr:GlobalMaxParallelismSemaphore",
            //	() => W.Common.Utils.NewAsyncSemaphore(nMaxParallelism),
            //	null, TimeSpan.FromMinutes(5), System.Web.Caching.Cache.NoAbsoluteExpiration);
            var sema = GlobalMaxParallelismSemaphore;
            var ae = new AsyncExprRootCtx(ctx.name2ndx, ctx.values, sema);
            ae.cancellationToken = cancellation;
            return ConstValueOf(ae, funcOrValue);
        }

        public static Task<object> GlobalValueOfAny(object funcOrValue, Generator.Ctx ctx, int nMaxParallelism, CancellationToken cancellation)
        {
            if (nMaxParallelism < 1)
                nMaxParallelism = 1024;
            var sema = W.Common.Utils.NewAsyncSemaphore(nMaxParallelism);
            var ae = new AsyncExprRootCtx(ctx.name2ndx, ctx.values, sema);
            ae.cancellationToken = cancellation;

            return ConstValueOf(ae, funcOrValue);
        }

        public static object ValueOfAny(object funcOrValue, int nMaxParallelism, Generator.Ctx ctx, CancellationToken cancellation)
        {
            if (nMaxParallelism < 1)
                nMaxParallelism = 128;
            var ae = new AsyncExprRootCtx(ctx.name2ndx, ctx.values, W.Common.Utils.NewAsyncSemaphore(nMaxParallelism));
            ae.cancellationToken = cancellation;
            var task = ConstValueOf(ae, funcOrValue);
            //task.Start();
            task.Wait();
            return task.Exception ?? task.Result;
        }

        public static void ValueOfAny(object funcOrValue, Generator.Ctx ctx, int nMaxParallelism, Action<object> onComplete, CancellationToken cancellation)
        {
            if (nMaxParallelism < 1)
                nMaxParallelism = 1024;
            var sema = W.Common.Utils.NewAsyncSemaphore(nMaxParallelism);
            var ae = new AsyncExprRootCtx(ctx.name2ndx, ctx.values, sema);
            ae.cancellationToken = cancellation;
            ThreadPool.QueueUserWorkItem(state =>
            {
                var task = ConstValueOf(ae, funcOrValue);
                //task.Start();
                task.ContinueWith(t =>
                {
                    try
                    {
                        onComplete(t.Result);
                    }
                    catch (Exception ex)
                    {
                        onComplete(ex);
                        Console.WriteLine(string.Join("; ", ctx.name2ndx.Keys));
                    }
                });
            });
        }

        public static object binaryOpConst(Fxy op, object constA, object constB, bool resultCanBeLazy)
        {
            IList arrA = constA as IList;
            IList arrB = constB as IList;
            if (arrA == null && arrB == null)
                return op(constA, constB);
            try
            {
                ArrayList result;
                if (arrA == null)// || arrA.Count == 1)
                {
                    object a = constA;// arrA == null ? argA : arrA[0];
                    if (resultCanBeLazy)
                        result = new ListOfVals(arrB.Count);
                    else result = new ListOfConst(arrB.Count);
                    for (int i = 0; i < arrB.Count; i++)
                        result.Add(op(a, arrB[i]));
                }
                else if (arrB == null)// || arrB.Count == 1)
                {
                    object b = constB;// arrB == null ? argB : arrB[0];
                    if (resultCanBeLazy)
                        result = new ListOfVals(arrA.Count);
                    else result = new ListOfConst(arrA.Count);
                    for (int i = 0; i < arrA.Count; i++)
                        result.Add(op(arrA[i], b));
                }
                else if (arrA.Count == arrB.Count)
                {
                    int n = arrA.Count;
                    if (resultCanBeLazy)
                        result = new ListOfVals(n);
                    else result = new ListOfConst(n);
                    for (int i = 0; i < n; i++)
                        result.Add(op(arrA[i], arrB[i]));
                }
                else return new ArgumentOutOfRangeException("binaryOp: arguments dimensions mismatch").Wrap();
                return result;
            }
            catch (Exception ex) { return ex.Wrap(); }// new Exception(string.Format("binaryOp: " + ex.Message)); }
        }

        public static object binaryOpSync(Fxy op, object a, object b, bool resultCanBeLazy)
        { return binaryOpConst(op, ConstValueOf(a), ConstValueOf(b), resultCanBeLazy); }

        public static async Task<object> binaryOpAsync(AsyncExprCtx ae,
            Fxy op, object a, object b, bool resultCanBeLazy)
        {
            var valueA = await ConstValueOf(ae, a);

            var valueB = await ConstValueOf(ae, b);

            return binaryOpConst(op, valueA, valueB, resultCanBeLazy);
        }

        //public static object unaryOpConst(Fx op, object constA, bool acceptVector, bool resultCanBeLazy)
        //{
        //	IList arrA = constA as IList;
        //	if (arrA == null || acceptVector)
        //		return op(constA);
        //	try
        //	{
        //		int n = arrA.Count;
        //		ArrayList result;
        //		if (resultCanBeLazy)
        //			result = new ListOfVals(n);
        //		else result = new ListOfConst(n);
        //		for (int i = 0; i < n; i++)
        //			result.Add(op(arrA[i]));
        //		return result;
        //	}
        //	catch (Exception ex) { return ex.Wrap(); }// new Exception(string.Format("unaryOp: " + ex.Message)); }
        //}

        //public static object unaryOpSync(Fx op, object a, bool acceptVector, bool resultCanBeLazy)
        //{ return unaryOpConst(op, ConstValueOf(a), acceptVector, resultCanBeLazy); }

        //public static async Task<object> unaryOpAsync(AsyncExprCtx ae, Fx op, object a,
        //	bool acceptVector, bool resultCanBeLazy)
        //{
        //	var valueA = await ConstValueOf(ae, a);
        //	return unaryOpConst(op, valueA, acceptVector, resultCanBeLazy);
        //}

        public class FxCall
        {
            readonly Fx func;
            readonly object argCode;
            readonly bool acceptVector, resultCanBeLazy;

            FxCall(Fx func, object argCode, bool acceptVector, bool resultCanBeLazy)
            { this.func = func; this.argCode = argCode; this.acceptVector = acceptVector; this.resultCanBeLazy = resultCanBeLazy; }

            //[DebuggerHidden]
            object Const(object arg)
            {
                IList arrA = arg as IList;
                if (arrA == null || acceptVector)
                    return func(arg);
                try
                {
                    int n = arrA.Count;
                    ArrayList result;
                    if (resultCanBeLazy)
                        result = new ListOfVals(n);
                    else result = new ListOfConst(n);
                    object[] prev = null;
                    for (int i = 0; i < n; i++)
                    {
                        var r = func(arrA[i]);
                        var curr = r as object[];
                        if (curr != null)
                        {
                            if (prev != null && Cmp.CompareTimed(prev, curr) == 0)
                                continue;
                            prev = curr;
                        }
                        result.Add(r);
                    }
                    return result;
                }
                catch (Exception ex) { return ex.Wrap(); }// new Exception(string.Format("unaryOp: " + ex.Message)); }
            }

            object Sync() { return Const(ConstValueOf(argCode)); }

            async Task<object> Async(AsyncExprCtx ae) { return Const(await ConstValueOf(ae, argCode)); }

#if DEBUG
            public CallExpr ce;
            public override string ToString() { return ce.ToString(); }
#endif

            public static FxCall New(Fx func, object arg, bool acceptVector, bool resultCanBeLazy, CallExpr ce = null)
            {
                var call = new FxCall(func, arg, acceptVector, resultCanBeLazy);
#if DEBUG
                call.ce = ce;
#endif
                return call;
            }

            public object Gen(ValueKind kind)
            {
                switch (kind)
                {
                    case ValueKind.Const:
                        return Const(argCode);
                    case ValueKind.Sync:
                        return (LazySync)Sync;
                    case ValueKind.Async:
                        return (LazyAsync)Async;
                }
                throw new InvalidOperationException();
            }


            public static object Gen(FuncDef fd, object arg, CallExpr ce)
            {
                ValueKind kind = KindOf(arg);
                var call = New((Fx)fd.func, arg, fd.flagsArgCanBeVector != 0, fd.resultCanBeLazy, ce);
                return call.Gen((kind == ValueKind.Const && fd.isNotPure) ? ValueKind.Sync : kind);
            }
        }

        public static object GenFnCall(FuncDef fd, IList lst, ValueKind argMaxKind, CallExpr ce)
        {
            Fn fn = (Fn)fd.func;
            Fx fx = delegate (object arg) { return fn((IList)arg); };
            var valueKind = fd.resultCanBeLazy ? ValueKind.Async : (argMaxKind == ValueKind.Const && fd.isNotPure) ? ValueKind.Sync : argMaxKind;
            var call = FxCall.New(arg => fn((IList)arg), lst, true, fd.resultCanBeLazy, ce);
            return call.Gen(valueKind);
        }

        public static object GenFxyCall(Fxy op, object a, object b, ValueKind kind, bool resultCanBeLazy)
        {
            switch (kind)
            {
                case ValueKind.Const:
                    return binaryOpConst(op, a, b, resultCanBeLazy);
                case ValueKind.Sync:
                    return (LazySync)delegate () { return binaryOpSync(op, a, b, resultCanBeLazy); };
                case ValueKind.Async:
                    return (LazyAsync)delegate (AsyncExprCtx ae)
                    { return binaryOpAsync(ae, op, a, b, resultCanBeLazy); };
            }
            throw new InvalidOperationException();
        }

        public static object GenFxyCall(FuncDef fd, object a, object b)
        {
            ValueKind kind = OPs.MaxKindOf(a, b);
            if (fd.resultCanBeLazy)
                kind = ValueKind.Async;
            else if (kind == ValueKind.Const && fd.isNotPure)
                kind = ValueKind.Sync;
            return GenFxyCall((Fxy)fd.func, a, b, kind, fd.resultCanBeLazy);
        }

        public static IList<Expr> ItemsOfArray(Expr arrExpr)
        {
            var a2 = arrExpr as ArrayExpr;
            if (a2 != null)
                return a2.args;
            var a1 = arrExpr as CallExpr;
            if (a1 != null)
            {
                if (a1.funcName != "_Arr")
                    return null;
                return a1.args;
            }
            return null;
        }

        public static string TryAsName(Expr expr, Generator.Ctx ctx)
        {
            var re = expr as ReferenceExpr;
            if (re != null)
                return re.name;
            var ce = expr as ConstExpr;
            if (ce != null)
                return Convert.ToString(ce.value);
            var val = Generator.Generate(expr, ctx);
            if (OPs.KindOf(val) == ValueKind.Const && val != null)
                return Convert.ToString(val);
            return null;
        }

        public static string TryAsString(Expr expr, Generator.Ctx ctx)
        {
            var ce = expr as ConstExpr;
            if (ce != null)
                return Convert.ToString(ce.value);
            var val = Generator.Generate(expr, ctx);
            if (OPs.KindOf(val) == ValueKind.Const && val != null)
                return Convert.ToString(val);
            return null;
        }

        public static object GetConstant(this Generator.Ctx ctx, string valueName)
        {
            int i = ctx.IndexOf(valueName);
            var val = (i < 0) ? null : ctx[i];
            if (val == null)
                return null;
            var expr = val as Expr;
            if (expr != null)
                val = Generator.Generate(expr, ctx);
            if (OPs.KindOf(val) == ValueKind.Const)
                return val;
            else if (expr != null)
                return ctx.Error(string.Format("Constant expected as value of '{0}' // {1}", valueName, expr));
            else
                return ctx.Error(string.Format("Constant expected as value of '{0}'", valueName));
        }

        public static object GetConstant(this Generator.Ctx ctx, Expr expr)
        {
            var val = Generator.Generate(expr, ctx);
            if (OPs.KindOf(val) == ValueKind.Const)
                return val;
            else
                return ctx.Error("Constant expected as value of " + expr.ToString());
        }

        public static async Task<object> CalcAsync(AsyncExprCtx ae, object arg, int nIns, int nOuts, AsyncFn calc, params int[] indexesOfNotNullableArgs)
        {
            var dict = (W.Common.IIndexedDict)arg;
            try
            {
                var data = dict.ValuesList;
                if (data.NotNullsAt(indexesOfNotNullableArgs) == false)
                    return null;
                var maxTime = DateTime.MinValue;
                foreach (ITimedObject to in data)
                    if (to != null && !to.IsEmpty && maxTime < to.Time)
                        maxTime = to.Time;
                var res = await calc(ae, data);
                if (res == null)
                    return null;
                int n = data.Length;
                if (maxTime > DateTime.MinValue)
                {
                    var lst = res as IList;
                    if (lst != null)
                        for (int i = lst.Count - 1; i >= 0; i--)
                            lst[i] = TimedObject.TryAsTimed(lst[i], maxTime, DateTime.MaxValue);
                    else res = TimedObject.TryAsTimed(res, maxTime, DateTime.MaxValue);
                }
                if (n == nIns)
                    return res;
                else
                {
                    var outs = new object[nOuts + n - nIns];
                    var lst = res as IList;
                    if (lst != null)
                        lst.CopyTo(outs, 0);
                    else outs[0] = res;
                    for (int i = n - 1; i >= nIns; i--)
                        outs[nOuts + i - nIns] = data[i];
                    return outs;
                }
            }
            catch (Exception ex)
            {
                Utils.ToLog(ex.Message, dict);
                return ex.Wrap();
            }
        }

        public static object[] ToArray(this IList lst)
        {
            var res = new object[lst.Count];
            for (int i = lst.Count - 1; i >= 0; i--)
                res[i] = lst[i];
            return res;
        }

        public static T[] ToArray<T>(this ICollection<T> lst)
        {
            var res = new T[lst.Count];
            int i = 0;
            foreach (var t in lst)
                res[i++] = t;
            System.Diagnostics.Debug.Assert(i == lst.Count);
            return res;
        }

        public static object[] ToArray(this ICollection lst)
        {
            var res = new object[lst.Count];
            int i = 0;
            foreach (var t in lst)
                res[i++] = t;
            System.Diagnostics.Debug.Assert(i == lst.Count);
            return res;
        }

        public static T[] ToArray<T>(this ICollection lst, Func<object, T> selector)
        {
            var res = new T[lst.Count];
            int i = 0;
            foreach (var t in lst)
                res[i++] = selector(t);
            System.Diagnostics.Debug.Assert(i == lst.Count);
            return res;
        }

        public static TRes[] ToArray<TSrc, TRes>(this ICollection<TSrc> lst, Func<TSrc, TRes> selector)
        {
            var res = new TRes[lst.Count];
            int i = 0;
            foreach (var item in lst)
                res[i++] = selector(item);
            System.Diagnostics.Debug.Assert(i == lst.Count);
            return res;
        }

        public static T[] ToArray<T>(object lst)
        {
            var res = lst as T[];
            if (res == null)
                res = ((ICollection)lst).ToArray(item => (T)item);
            return res;
        }
    }

    class OneValueList : IList
    {
        object value;
        int count;

        public OneValueList(object value, int count) { this.value = value; this.count = count; }
        public OneValueList(object value) { this.value = value; count = int.MaxValue; }

        #region IList Members
        public int Add(object value) { throw new InvalidOperationException(); }
        public void Clear() { throw new InvalidOperationException(); }
        public bool Contains(object value) { return value == this.value; }
        public int IndexOf(object value) { if (value == this.value) return 0; else return -1; }
        public void Insert(int index, object value) { throw new InvalidOperationException(); }
        public bool IsFixedSize { get { return true; } }
        public bool IsReadOnly { get { return true; } }
        public void Remove(object value) { throw new InvalidOperationException(); }
        public void RemoveAt(int index) { throw new InvalidOperationException(); }
        public object this[int index]
        {
            get { if (0 <= index && index < count) return value; else throw new IndexOutOfRangeException(); }
            set { throw new InvalidOperationException(); }
        }
        #endregion

        #region ICollection Members
        public void CopyTo(Array array, int index) { throw new NotImplementedException(); }
        public int Count { get { return count; } }
        public bool IsSynchronized { get { return true; } }
        public object SyncRoot { get { return this; } }
        #endregion

        #region IEnumerable Members
        public IEnumerator GetEnumerator() { for (int i = 0; i < count; i++) yield return value; }
        #endregion
    }
}
