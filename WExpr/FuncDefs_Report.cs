using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace W.Expressions
{
    using W.Common;

    public class FuncDefs_Report
    {
        public static object _Dict(IList args)
        {
            if (args.Count == 1)
                args = OPs.Flatten((IList)(args[0]), 1).Cast<object>().ToArray();
            if ((args.Count & 1) != 0)
                throw new ArgumentException("_Dict[n]: Number of arguments must be even");
            var dict = new Dictionary<string, object>();
            for (int i = args.Count - 1; i >= 1; i -= 2)
                dict.Add(Convert.ToString(args[i - 1]), args[i]);
            return dict;
        }

        [Arity(1, int.MaxValue)]
        public static object _DictAdd(IList args)
        {
            if (args.Count == 1)
                args = OPs.Flatten((IList)(args[0]), 1).Cast<object>().ToArray();
            if ((args.Count & 1) == 0)
                throw new ArgumentException("_Dict[n]: Number of arguments must be odd");
            var dict = Utils.Cast<IDictionary<string, object>>(args[0]);
            if (dict.IsReadOnly)
            {
                dict = new Dictionary<string, object>(dict);
                for (int i = args.Count - 1; i > 1; i -= 2)
                    dict.Add(Convert.ToString(args[i - 1]), args[i]);
            }
            else
            {
                for (int i = args.Count - 1; i > 1; i -= 2)
                {
                    var key = Convert.ToString(args[i - 1]);
                    var value = args[i];
                    lock (dict) dict.Add(key, value);
                }
            }
            return dict;
        }

        [Arity(2, 2)]
        public static object IDictFromKeysAndValues(IList args)
        {
            var keys = Utils.AsIList(args[0]);
            var values = Utils.AsIList(args[1]);
            int n = keys.Count;
            if (n != values.Count)
                throw new ArgumentException("IDictFromKeysAndValues(keys,values): lengths of keys and values must be equal");
            string[] arrKeys = keys.ToArray(Convert.ToString);
            object[] arrValues = values.ToArray();
            return ValuesDictionary.New(arrKeys, arrValues);
        }

        /// <summary>
        /// Deprecated
        /// </summary>
        [Arity(2, 2)]
        public static object ContainedInDict(IList args)
        {
            var arr = new object[2] { args[1], args[0] };
            return _DictContains(arr);
        }

        [Arity(2, 2)]
        public static object _DictContains(IList args)
        {
            var dict = Utils.Cast<IDictionary<string, object>>(args[0]);
            var keys = Utils.TryAsIList(args[1]);
            if (keys == null)
                return dict.ContainsKey(Convert.ToString(args[1]));
            int n = keys.Count;
            var res = new bool[n];
            for (int i = 0; i < n; i++)
                res[i] = dict.ContainsKey(Convert.ToString(keys[i]));
            return res;
        }

        /// <summary>
        /// Create array of IIndexedDict from list of headers(keys) and data arrays
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        [Arity(2, int.MaxValue)]
        public static object _Dicts(IList args)
        {
            var keys = (IList)(args[0]);
            var key2ndx = new Dictionary<string, int>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
                key2ndx.Add(Convert.ToString(keys[i]), i);
            var dicts = new IIndexedDict[args.Count - 1];
            for (int i = 1; i < args.Count; i++)
            {
                var arg = args[i];
                var values = arg as object[];
                if (values == null)
                {
                    var lst = arg as ArrayList;
                    if (lst != null)
                        values = lst.ToArray();
                    else values = ((IList)arg).Cast<object>().ToArray();
                }
                dicts[i - 1] = ValuesDictionary.New(values, key2ndx);
            }
            return dicts;
        }

        [Arity(2, 2)]
        public static object _Dicts2(IList args)
        {
            var lst = Utils.Cast<IList>(args[1]);
            var prms = new object[lst.Count + 1];
            prms[0] = args[0];
            for (int i = lst.Count - 1; i >= 0; i--)
                prms[i + 1] = lst[i];
            return _Dicts(prms);
        }

        [IsNotPure]
        public static object _NonPureArr(IList args) { return new OPs.ListOfVals(args); }

        [IsNotPure]
        public static object _NewArr(IList args) { return new OPs.ListOfConst(args); }

        public static object _Arr(CallExpr ce, Generator.Ctx ctx)
        { return Generator.Generate(new ArrayExpr(ce.args), ctx); }

        [Arity(2, int.MaxValue)]
        public static object _ArrItems(IList args)
        {
            object arg0 = args[0];
            IList arr = (IList)arg0;
            List<int> ndxs = new List<int>();
            if (args.Count == 2)
            {
                IList tmp = args[1] as IList;
                if (tmp == null)
                    ndxs.Add(Convert.ToInt32(args[1]));
                else
                    foreach (object o in tmp)
                        ndxs.Add(Convert.ToInt32(o));
            }
            else
                for (int i = 1; i < args.Count; i++)
                    ndxs.Add(Convert.ToInt32(args[i]));
            var res = ndxs.ToArray(i => arr[i]);

            return res;
        }

        [Arity(2, int.MaxValue)]
        public static object _ArrAdd(IList args)
        {
            IList list = Utils.ToIList(args[0]);
            if (list.IsReadOnly || list.IsFixedSize)
                list = new OPs.ListOfConst(list);
            for (int i = 1; i < args.Count; i++)
                list.Add(args[i]);
            return list;
        }

        public static object _ArrReverse([CanBeVector]object arr)
        {
            IList list = Utils.ToIList(arr);
            if (list == null || list.Count < 2)
                return arr;
            int n = list.Count;
            var r = new object[n];
            for (int i = 0; i < n; i++)
                r[i] = list[n - i - 1];
            return r;
        }

        [Arity(1, int.MaxValue)]
        public static object _AddRange(IList args)
        {
            IList list = Utils.ToIList(args[0]);
            if (list == null)
                list = new OPs.ListOfConst(8);
            else if (list.IsReadOnly || list.IsFixedSize)
                list = new OPs.ListOfConst(list);
            for (int j = 1; j < args.Count; j++)
            {
                IList what = Utils.ToIList(args[j]);
                if (what != null)
                    for (int i = 0; i < what.Count; i++)
                        list.Add(what[i]);
            }
            return list;
        }

        [Arity(1, 2)]
        public static object Flatten(IList args)
        {
            var list = Utils.ToIList(args[0]);
            int maxDepth = (args.Count > 1) ? Convert.ToInt32(args[1]) : int.MaxValue;
            var res = OPs.Flatten(list, maxDepth).Cast<object>().ToArray();
            return res;
        }

        [Arity(2, 2)]
        public static object _Range(IList args)
        {
            int iBeg = Convert.ToInt32(args[0]);
            int iEnd = Convert.ToInt32(args[1]);
            int n = Math.Abs(iEnd - iBeg) + 1;
            if (n > 100000)
                throw new ArgumentException("FuncDefs_Report:_Range function supports no more than 100000 items");
            int step = (iBeg < iEnd) ? +1 : -1;
            var res = new int[n];
            int iv = iBeg;
            for (int i = 0; i < n; i++, iv += step)
                res[i] = iv;
            return res;
        }

        [Arity(2, 2)]
        public static object _ArrClip(IList args)
        {
            IList list = Utils.ToIList(args[0]);
            int maxCount = Convert.ToInt32(args[1]);
            int n = list.Count;
            if (n <= maxCount)
                return list;
            if (list.IsReadOnly || list.IsFixedSize)
            {
                var res = new object[maxCount];
                for (int i = 0; i < maxCount; i++)
                    res[i] = list[i];
                return res;
            }
            while (n > maxCount) list.RemoveAt(--n);
            return list;
        }

        static readonly int[] ndxsFirstField = new int[1] { 0 };
        /// <summary>
        /// Search over sorted list of IIndexedDict's
        /// </summary>
        /// <param name="args">0: IList[IIndexedDict]; 1: value to search</param>
        /// <returns>found IIndexedDict</returns>
        [Arity(2, 2)]
        public static object BinarySearchByFirstField(IList args)
        {
            var items = Utils.Cast<IList<IIndexedDict>>(args[0]);
            var value = args[1];
            int i = items.BinarySearch(ndxsFirstField, new object[] { value });
            return (i < 0) ? null : items[i];
        }

        static int BinarySearch(IList keys, double keyToSearch)
        {
            int n = keys.Count;
            int ia = 0, ib = n - 1;
            int ndx = ~0;
            while (ia <= ib)
            {
                int i = (ia + ib) / 2;
                double tmp = Convert.ToDouble(keys[i]);
                if (keyToSearch < tmp)
                {
                    ib = i - 1;
                    if (ib < ia)
                    { ndx = ~i; break; }
                }
                else if (tmp < keyToSearch)
                {
                    ia = i + 1;
                    if (ib < ia)
                    { ndx = ~ia; break; }
                }
                else { ndx = i; break; }
            }
            return ndx;
        }

        [Arity(5, 5)]
        /// <summary>
        /// Add key to sorted keys array (and associated value to values array)
        /// _AddToSorted(double[] sortedKeys, double newKey, object[] values, object([]) newValue(s), bool unique)
        /// Modify sortedKeys and values
        /// </summary>
        /// <param name="args">0:sortedKeys, 1:newKey; 2:values; 3:newValue; 4:unique</param>
        /// <returns>sortedKeys</returns>
        public static object _AddToSorted(IList args)
        {
            object arg0 = args[0];
            IList keys = (IList)arg0;
            lock (keys)
            {
                double newKey = Convert.ToDouble(args[1]);
                IList values = (args.Count > 2) ? Utils.ToIList(args[2]) : null;
                if (keys.Count != values.Count)
                    throw new ArgumentException("_ArrAddSorted: keys.Count!=values.Count");
                bool unique = (args.Count > 4) ? Convert.ToBoolean(args[4]) : false;
                int ndx = BinarySearch(keys, newKey);
                if (ndx < 0)
                    ndx = ~ndx;
                else if (unique)
                    return arg0;
                else ndx++;
                keys.Insert(ndx, newKey);
                if (values != null)
                {
                    object newValue = (args.Count > 3) ? args[3] : null;
                    IList expLst = newValue as IList;
                    if (expLst != null)
                    {
                        var lst = expLst.ToArray();
                        //IList lst = new OPs.ListOfConst(expLst.Count);
                        //for (int i = 0; i < expLst.Count; i++)
                        //	lst.Add(expLst[i]);
                        newValue = lst;
                    }
                    values.Insert(ndx, newValue);
                }
            }
            return arg0;
        }

        // _Sort(IList<double>)
        // _Sort(IList<double> keys, IList values)
        // return true
        [Arity(1, 2)]
        public static object _Sort(IList args)
        {
            try
            {
                IList argKeys = Utils.ToIList(args[0]);
                if (argKeys == null)
                    return false;
                IList argVals = (args.Count > 1) ? Utils.ToIList(args[1]) : null;

                double[] keys = new double[argKeys.Count];
                for (int i = 0; i < keys.Length; i++)
                {
                    double tmp;
                    try
                    {
                        tmp = Convert.ToDouble(argKeys[i]);
                        if (double.IsNaN(tmp))
                            tmp = double.PositiveInfinity;
                    }
                    catch { tmp = double.PositiveInfinity; }
                    keys[i] = tmp;
                }
                if (argVals != null)
                {
                    object[] vals = new object[argVals.Count];
                    argVals.CopyTo(vals, 0);
                    Array.Sort<double, object>(keys, vals);
                    // store values
                    argVals = vals.ToArray<object>();
                    //if (argVals.IsReadOnly || argVals.IsFixedSize)
                    //	argVals = new OPs.ListOfConst(vals.Length);
                    //else
                    //	argVals.Clear();
                    //foreach (object obj in vals)
                    //	argVals.Add(obj);
                }
                else Array.Sort<double>(keys);

                if (argVals != null)
                    return argVals;
                else
                {
                    // store keys
                    argKeys = keys.ToArray<double>();
                    //if (argKeys.IsReadOnly)
                    //	argKeys = new OPs.ListOfConst(keys.Length);
                    //else
                    //	argKeys.Clear();
                    //foreach (double key in keys)
                    //	argKeys.Add(key);
                    return argKeys;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { throw; }
        }

        public static object _ArrNN(IList args)
        {
            ArrayList list = new OPs.ListOfConst(args.Count);
            for (int i = 0; i < args.Count; i++)
            {
                object obj = args[i];
                if (obj != null) list.Add(obj);
            }
            return list;
        }

        public static object _ArrNoNulls([CanBeVector]object listObj)
        {
            var lst = listObj as IList;
            if (lst == null)
                return null;
            var res = new OPs.ListOfConst(lst.Count);
            for (int i = 0; i < lst.Count; i++)
            {
                object obj = lst[i];
                if (obj != null) res.Add(obj);
            }
            return res;
        }

        public static object _ArrNullIfEmpty([CanBeVector]object listObj)
        {
            var lst = listObj as IList;
            if (lst == null || lst.Count == 0)
                return null;
            return lst;
        }


        /// <summary>
        /// _StairSelect( 
        ///     X,    Y[0]  ' return Y[0] if X is smaller than X[1]
        ///     X[1], Y[1] 
        ///     ...
        ///     X[n], Y[n]) ' return Y[n] if X greater than or equal X[n]
        /// </summary>
        [Arity(4, int.MaxValue)]
        public static object _StairSelect(CallExpr ce, Generator.Ctx ctx)
        {
            IList<Expr> args = ce.args;
            int cnt = args.Count;
            if ((cnt & 1) != 0)
                ctx.Error("_StairSelect must have even number of args (at least 4)");
            string nameX = OPs.UniqIdentifier("x");
            Expr varX = new ReferenceExpr(nameX);
            Expr expr = args[cnt - 1];
            for (int i = cnt / 2 - 1; i > 0; i--)
                expr = new CallExpr("IF", new BinaryExpr(ExprType.LessThan, varX, args[i * 2]), args[i * 2 - 1], expr);
            expr = CallExpr.Eval(CallExpr.let(varX, args[0]), expr);
            return Generator.Generate(expr, ctx);
        }

        [Arity(2, 2)]
        public static object _CountValue(IList args)
        {
            IList list = Utils.ToIList(args[0]);
            double value = Convert.ToDouble(args[1]);
            int n = 0;
            foreach (object obj in list)
                try
                {
                    double v = Convert.ToDouble(obj);
                    if (v == value)
                        n++;
                }
                catch { continue; }
            return n;
        }

        /// <summary>
        /// _TransitionsCnt(listOfNumbers, valueTo)
        /// </summary>
        public static object _TransitionsCnt(IList args)
        {
            IList list = args[0] as IList;
            double valueTo = Convert.ToDouble(args[1]);
            int cnt = 0;
            if (list == null || list.Count == 0)
                return cnt;
            double prevValue = double.NaN;
            for (int i = 0; i < list.Count; i++)
            {
                double value;
                try { value = Convert.ToDouble(list[i]); }
                catch { continue; }
                if (prevValue != value && value == valueTo)
                    cnt++;
                prevValue = value;
            }
            return cnt;
        }

        /// <summary>
        /// _TransitionLengths(listOfTimedNumbers, valueTo)
        /// </summary>
        public static object _TransitionLengths(IList args)
        {
            IList list = args[0] as IList;
            double valueTo = Convert.ToDouble(args[1]);
            int cnt = 0;
            if (list == null || list.Count == 0)
                return cnt;
            double prevValue = double.NaN;
            DateTime prevTime = DateTime.MinValue;
            ArrayList results = new OPs.ListOfConst(list.Count - 1);
            for (int i = 0; i < list.Count; i++)
            {
                ITimedObject tv = list[i] as ITimedObject;
                if (tv == null || tv.IsEmpty)
                    continue;
                double value = Convert.ToDouble(tv.Object);
                if (double.IsNaN(value))
                    continue;
                if (prevValue != value && value == valueTo && prevTime > DateTime.MinValue)
                    results.Add(OPs.ToExcelDate(tv.Time) - OPs.ToExcelDate(prevTime));
                prevValue = value;
                prevTime = tv.Time;
            }
            return results;
        }

        /// <summary>
        /// _CumulativeSums(listOfNumbers [, initial value of sum])
        /// </summary>
        /// <param name="args">0: list of numbers or ITimedObjects[; 1:initial sum]</param>
        /// <returns></returns>
        [Arity(1, 2)]
        public static object _CumulativeSums(IList args)
        {
            var list = Utils.ToIList(args[0]);
            if (list == null || list.Count == 0)
                return new object[0];
            var results = new OPs.ListOfConst(list.Count);
            double sum = (args.Count > 1) ? Convert.ToDouble(args[1]) : 0d;
            foreach (object v in list)
            {
                sum += Convert.ToDouble(v);
                var to = v as ITimedObject;
                if (to != null)
                    results.Add(new TimedDouble(to.Time, to.EndTime, sum));
                else
                    results.Add(sum);
            }
            return results;
        }

        [Arity(1, 3)]
        /// <summary>
        /// Расчёт площадей интервалов ступенчатого графика y = f(t).
        /// S[i] = y[i] * (t[i+1] - t[i])
        /// Интервалы должны быть отсортированы по t, производится отсечение пересекающихся интервалов
        /// </summary>
        /// <param name="args">0:Список ITimedObject-значений; [1:минимальная дата;[2:максимальная дата]]</param>
        /// <returns>Список ITimedObject-площадей</returns>
        public static object _Squares(IList args)
        {
            var list = Utils.ToIList(args[0]);
            if (list == null || list.Count == 0)
                return new object[0];
            var dtMinBorder = (args.Count > 1) ? OPs.FromExcelDate(Convert.ToDouble(args[1])) : DateTime.MinValue;
            var dtMaxBorder = (args.Count > 2) ? OPs.FromExcelDate(Convert.ToDouble(args[2])) : DateTime.MaxValue;
            var results = new OPs.ListOfConst(list.Count - 1);
            var prevTime = dtMinBorder;
            var prevEndTime = dtMinBorder;
            var prevValue = double.NaN;
            foreach (ITimedObject to in list)
            {
                if (to.IsEmpty) continue;
                var time = to.Time;
                if (dtMaxBorder < time) continue;
                var endTime = to.EndTime;
                if (endTime < prevTime) continue;
                if (time <= prevTime)
                    time = prevTime;
                else if (!double.IsNaN(prevValue))
                {
                    var time2 = (prevEndTime < time) ? prevEndTime : time;
                    double dT = time2.ToOADate() - prevTime.ToOADate();
                    results.Add(new TimedDouble(prevTime, time2, dT * prevValue));
                }
                prevValue = Convert.ToDouble(to);
                prevTime = time;
                prevEndTime = endTime;
            }
            if (!double.IsNaN(prevValue))
            {
                var time2 = (prevEndTime < dtMaxBorder) ? prevEndTime : dtMaxBorder;
                double dT = time2.ToOADate() - prevTime.ToOADate();
                results.Add(new TimedDouble(prevTime, time2, dT * prevValue));
            }
            return results;
        }

        public static object _SumNums(IList args)
        {
            double sum = 0;
            foreach (object v in args)
                try
                {
                    double? tmp = OPs.xl2dbl2(v);
                    if (tmp.HasValue)
                        sum += tmp.Value;
                }
                catch { }
            return sum;
        }

        public static object _TimeOf(object obj)
        {
            var to = obj as ITimedObject;
            if (to != null)
                return to.IsEmpty ? null : (object)OPs.ToExcelDate(to.Time);
            else if (obj is DateTime)
                return OPs.ToExcelDate((DateTime)obj);
            else return null;
        }

        public static object _ValueOf(object obj)
        {
            var to = obj as ITimedObject;
            if (to != null)
                return to.IsEmpty ? null : to.Object;
            else return obj;
        }

        public static object ToOADate(object obj)
        {
            return Convert.ToDateTime(obj).ToOADate();
        }

        public static object _EndTimeOf(object obj)
        {
            var to = obj as ITimedObject;
            if (to != null)
                return to.IsEmpty ? null : (object)OPs.ToExcelDate(to.EndTime);
            else return null;
        }

        public static object _iMax(IList args)
        {
            double max = double.MinValue;
            int iMax = -1;
            for (int i = args.Count - 1; i >= 0; i--)
                try
                {
                    double tmp = OPs.xl2dbl(args[i]);
                    if (tmp > max)
                    { max = tmp; iMax = i; }
                }
                catch { }
            return iMax;
        }

        public static object _iMin(IList args)
        {
            double min = double.MaxValue;
            int iMin = -1;
            for (int i = args.Count - 1; i >= 0; i--)
                try
                {
                    double tmp = OPs.xl2dbl(args[i]);
                    if (tmp < min)
                    { min = tmp; iMin = i; }
                }
                catch { }
            return iMin;
        }

        [Arity(2, int.MaxValue)]
        /// <summary>
        /// MixArr(массив1, ..., массивN) - возвращает результат "смешивания" массивов, например
        /// MixArr({1,2,3,4}, {a,b,c}, {d,e,f,g}) = {1,a,d,2,b,e,3,c,f,4,g}
        /// Допустимо использование скалярных объектов (но хотя бы один массив должен присутствовать)
        /// MixArr('N', {1,2,3}) = {'N',1,'N',2,'N',3}
        /// </summary>
        public static object _MixArr(IList args)
        {
            try
            {
                ArrayList srcs = new OPs.ListOfConst(args.Count);
                for (int i = 0; i < args.Count; i++)
                    srcs.Add(args[i]);
                ArrayList result = new OPs.ListOfConst(args.Count);
                bool moreItems = true;
                int ndx = 0;
                while (moreItems)
                {
                    moreItems = false;
                    for (int i = 0; i < srcs.Count; i++)
                    {
                        IList list = srcs[i] as IList;
                        if (list != null)
                        {   // vector of objects
                            if (ndx >= list.Count)
                                result.Add(null);
                            else
                            {
                                result.Add(list[ndx]);
                                moreItems |= ndx < list.Count - 1;
                            }
                        }
                        else // scalar object
                            result.Add(srcs[i]);
                    }
                    ndx++;
                }
                return result;
            }
            catch { }
            return true;
        }

        [Arity(3, 3)]
        public static object _ArrPadRight(IList args)
        {
            try
            {
                object obj = args[0];
                int n = Convert.ToInt32(args[1]);
                object pad = args[2];
                IList src = Utils.TryAsIList(obj);
                ArrayList result;
                if (src != null)
                {
                    if (src.Count >= n)
                    {
                        result = new OPs.ListOfConst(n);
                        while (result.Count < n)
                            result.Add(src[result.Count]);

                        return result;
                    }
                    result = new OPs.ListOfConst(src);
                }
                else
                    result = new OPs.ListOfConst(n);
                result.Capacity = n;
                while (result.Count < n)
                    result.Add(pad);
                return result;
            }
            catch { }
            return true;
        }

        //[Arity(1,2)]
        //        static object _QueryYesNo(AsyncExprCtx ae, IList args)
        //            {
        //                string text = Convert.ToString(args[0]);
        //                string title = (args.Count > 1) ? Convert.ToString(args[1]) : "Генератор отчётов";
        //                return System.Windows.Forms.MessageBox.Show(text, title,
        //                    System.Windows.Forms.MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.Yes;
        //            }

        static System.Globalization.CultureInfo myCI = null;

        [Arity(1, int.MaxValue)]
        public static object _StrFmt(IList args)
        {
            string format = args[0].ToString();
            var fmtArgs = new object[args.Count - 1];
            for (int i = 1; i < args.Count; i++)
                fmtArgs[i - 1] = OPs.ConstValueOf(args[i]);
            if (myCI == null)
                lock (typeof(FuncDefs_Report))
                {
                    if (myCI == null)
                    {
                        myCI = new System.Globalization.CultureInfo(System.Globalization.CultureInfo.InvariantCulture.LCID);
                        myCI.NumberFormat.NumberGroupSeparator = " ";
                    }
                }
            string text = string.Format(myCI, format, fmtArgs);
            return text;
        }

        [Arity(2, 2)]
        /// <summary>
        /// string _StrJoin(string separator, IList valuesToJoin)
        /// </summary>
        public static object _StrJoin(IList args)
        {
            string delimiter = Convert.ToString(args[0]);
            object arg1 = args[1];
            IList list = Utils.TryAsIList(arg1);
            bool skipEmpties = args.Count > 2;
            if (list == null)
                return Convert.ToString(arg1);
            StringBuilder sb = new StringBuilder();
            bool first = true;
            foreach (object obj in list)
            {
                string tmp = (obj == null) ? null : obj.ToString();
                if (skipEmpties && string.IsNullOrEmpty(tmp))
                    continue;
                if (first)
                    first = false;
                else sb.Append(delimiter);
                sb.Append(tmp);
            }
            return sb.ToString();
        }

        [Arity(2, 2)]
        /// <summary>
        /// string _JoinStr(string prefix, IList valuesToJoin)
        /// </summary>
        public static object _JoinStr(IList args)
        {
            var prefix = Convert.ToString(args[0]);
            object arg1 = args[1];
            IList list = arg1 as IList;
            bool skipEmpties = args.Count > 2;
            if (list == null)
                return prefix + Convert.ToString(arg1);
            var sb = new StringBuilder();
            foreach (object obj in list)
            {
                string tmp = (obj == null) ? null : obj.ToString();
                if (skipEmpties && string.IsNullOrEmpty(tmp))
                    continue;
                sb.Append(prefix);
                sb.Append(tmp);
            }
            return sb.ToString();
        }

        public static object _StrOnlyDigits(object arg)
        {
            if (Utils.IsEmpty(arg))
                return arg;
            var s = Convert.ToString(arg);
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i]))
                    sb.Append(s[i]);
            return sb.ToString();
        }

        public static object _StrLeadingDigits(object arg)
        {
            if (Utils.IsEmpty(arg))
                return arg;
            var s = Convert.ToString(arg);
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i]))
                    sb.Append(s[i]);
                else break;
            return sb.ToString();
        }

        [Arity(2, 2)]
        /// <summary>
        /// IList _StrSplit(string separator, string strToSplit)
        /// </summary>
        public static object _StrSplit(object sep, object str)
        {
            string separator = Convert.ToString(sep);
            string strToSplit = Convert.ToString(str);
            string[] parts = strToSplit.Split(new string[] { separator }, StringSplitOptions.None);
            ArrayList lst = new OPs.ListOfConst(parts.Length);
            foreach (string s in parts) lst.Add(s);
            return lst;
        }

        [Arity(1, 1)]
        public static object _StrHash(IList args)
        {
            string str = Convert.ToString(args[0]);
            return str.GetHashCode();
        }

        public static object _IDictsToTimedStr([CanBeVector]object x) { return ValuesDictionary.IDictsToTimedStr((IList)x); }

        public static object _IDictsToStr([CanBeVector]object x) { return ValuesDictionary.IDictsToStr((IList)x); }

        public static object _ToStr([CanBeVector]object x)
        {
            var idct = x as IList<IIndexedDict>;
            if (idct != null)
            {
                if (idct.Count == 0)
                    return "IIndexedDict[0]";
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine(string.Join("\t", idct[0].Keys));
                foreach (var row in idct)
                    sb.AppendLine(string.Join("\t", row.ValuesList.Select(v =>
                    {
                        var to = v as ITimedObject;
                        return (to == null) ? v : to.Object;
                    })));
                return sb.ToString();
            }
            var lst = x as IList;
            if (lst != null)
                return W.Common.Utils.IListToText(lst);
            return Convert.ToString(x);
        }

        public static object _TimeToStr(object arg)
        {
            try
            {
                DateTime time = arg is DateTime ? (DateTime)arg : OPs.FromExcelDate(Convert.ToDouble(arg));
                return W.Common.Utils.ToStr(time);
            }
            catch { return string.Empty; }
        }

        [Arity(2, 2)]
        public static object _TimeToStr(IList args)
        {
            try
            {
                var arg = args[0];
                DateTime time = arg is DateTime ? (DateTime)arg : OPs.FromExcelDate(Convert.ToDouble(arg));
                if (args.Count > 1)
                {
                    string format = Convert.ToString(args[1]);
                    return time.ToString(format);
                }
                else return W.Common.Utils.ToStr(time);
            }
            catch { return string.Empty; }
        }



        /// <summary>
        /// _CT - check time
        /// </summary>
        /// <param name="arg0">ITimedObject obj</param>
        /// <param name="arg1">DateTime refTime</param>
        /// <returns>if (obj.Time less than refTime) string.Empty else obj</returns>
        public static object _CT(object arg0, object arg1)
        {
            try
            {
                object obj = arg0;
                ITimedObject io = obj as ITimedObject;
                //if (io == null)
                //    throw new ArgumentException("_CT: arg is not ITimedObject");
                if (io != null && Math.Abs(OPs.ToExcelDate(io.Time) - Convert.ToDouble(arg1)) < 0.0001)
                    return obj;
                else return string.Empty;
            }
            catch (Exception ex) { throw ex; }
        }

        public static object _HtmlEncode(object text)
        {
            string s = Convert.ToString(text);
            int i = s.IndexOf('\0');
            if (i >= 0)
                s = s.Substring(0, i);
            return System.Web.HttpUtility.HtmlEncode(s);
        }

        static object[] syncAppTxtA = new object[4] { new object(), new object(), new object(), new object() };

        [IsNotPure]
        [Arity(2, 2)]
        public static object _AppendTextA(IList args)
        {
            string path = args[0].ToString();
            string contents = args[1].ToString();
            string p = path;
            foreach (var c in Path.GetInvalidPathChars())
                p = p.Replace(c, '_');
            string sDir = System.IO.Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(sDir) && !System.IO.Directory.Exists(sDir))
                try { System.IO.Directory.CreateDirectory(sDir); }
                catch { }
            int iSync = p.GetHashCode() & 3;
            lock (syncAppTxtA[iSync])
                System.IO.File.AppendAllText(p, contents, Encoding.Default);
            return contents;
        }

        [IsNotPure]
        [Arity(2, 2)]
        public static object _WriteAllText(IList args)
        {
            string path = args[0].ToString();
            string contents = args[1].ToString();
            string sDir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(sDir) && !System.IO.Directory.Exists(sDir))
                try { System.IO.Directory.CreateDirectory(sDir); }
                catch { }
            var p = path.Replace('/', '_').Replace(':', '_').Replace('?', '_');
            System.IO.File.WriteAllText(p, contents, Encoding.UTF8);
            return contents;
        }

        [IsNotPure]
        public static object Print([CanBeVector]object arg)
        {
            Console.Write(arg);
            return arg;
        }

        [IsNotPure]
        public static object PrintLn([CanBeVector]object arg)
        {
            Console.WriteLine(arg);
            return arg;
        }

        [IsNotPure]
        public static object PrintLn([CanBeVector]object a, [CanBeVector]object b)
        {
            Console.WriteLine(((a == null) ? string.Empty : a.ToString()) + "\t" + ((b == null) ? string.Empty : b.ToString()));
            return a;
        }

        public struct ObjWithSync<T>
        {
            public readonly IAsyncLock sema;
            public readonly T value;
            public ObjWithSync(T value)
            {
                this.value = value;
                sema = Utils.NewAsyncLock();
            }
            public override string ToString()
            {
                if (value == null)
                    return "[null]";
                return value.ToString();
            }
            public async Task<object> DoSyncAction(Func<object> action)
            {
                await sema.WaitAsync(CancellationToken.None);
                object result;
                try { result = action(); }
                finally { sema.Release(); }
                return result;
            }

        }

        public static ObjWithSync<T> OwS<T>(T value) { return new ObjWithSync<T>(value); }

        [Arity(1, 4)]
        public static object NewFileStreamWriter(IList args)
        {
            var a0 = args[0];
            int n = args.Count;
            {
                var fn = Convert.ToString(a0);
                if (n == 1)
                    return OwS(new StreamWriter(fn));
                else
                {
                    var append = Convert.ToBoolean(args[1]);
                    if (n == 2)
                        return OwS(new StreamWriter(fn, append));
                    else
                    {
                        var e = Encoding.GetEncoding(Convert.ToString(args[2]));
                        if (n == 3)
                            return OwS(new StreamWriter(fn, append, e));
                        else if (n == 4)
                            return OwS(new StreamWriter(fn, append, e, Convert.ToInt32(args[3])));
                    }
                }
            }
            throw new ArgumentException("NewFileStreamWriter: unsupported arguments");
        }

        [CanBeLazy]
        public static object StreamWriterAppendText(object streamWriterWithSync, object text)
        {
            var swws = (ObjWithSync<StreamWriter>)streamWriterWithSync;
            return (LazyAsync)(ctx => swws.DoSyncAction(() =>
            {
                swws.value.Write(text);
                return string.Empty;
            }));
        }

        [CanBeLazy]
        public static object StreamWriterFlush(object streamWriterWithSync)
        {
            var swws = (ObjWithSync<StreamWriter>)streamWriterWithSync;
            return (LazyAsync)(ctx => swws.DoSyncAction(() =>
            {
                swws.value.Flush();
                return string.Empty;
            }));
        }

        [CanBeLazy]
        public static object StreamWriterClose(object streamWriterWithSync)
        {
            var swws = (ObjWithSync<StreamWriter>)streamWriterWithSync;
            return (LazyAsync)(ctx => swws.DoSyncAction(() =>
            {
                swws.value.Close();
                return string.Empty;
            }));
        }

        [Arity(1, 1)]
        public static object PartsOfLimitedSize(CallExpr ce, Generator.Ctx ctx)
        {
            int minPartSize = Convert.ToInt32(ctx.GetConstant(FuncDefs_Core.optionMinPartSize));
            int maxPartSize = Convert.ToInt32(ctx.GetConstant(FuncDefs_Core.optionMaxPartSize));
            Expr preferredMinCount;
            if (ctx.IndexOf(FuncDefs_Core.optionPreferredMinPartsCount) < 0)
                preferredMinCount = new ConstExpr(-1);
            else
                preferredMinCount = new ReferenceExpr(FuncDefs_Core.optionPreferredMinPartsCount);
            var call = new CallExpr(nameof(PartsOfLimitedSize),
                ce.args[0],
                new ReferenceExpr(FuncDefs_Core.optionMinPartSize),
                new ReferenceExpr(FuncDefs_Core.optionMaxPartSize),
                preferredMinCount
            );
            return Generator.Generate(call, ctx);
        }

        [Arity(2, 4)]
        public static object PartsOfLimitedSize(IList args)
        {
            var srcList = Utils.ToIList(args[0]);
            int minLength = 1, maxLength, prefMinCount = -1;
            if (args.Count == 2)
                maxLength = Convert.ToInt32(args[1]);
            else // args.Count >= 3
            {
                minLength = Convert.ToInt32(args[1]);
                maxLength = Convert.ToInt32(args[2]);
                if (args.Count > 3)
                    prefMinCount = Convert.ToInt32(args[3]);
            }
            if (prefMinCount < 0)
                prefMinCount = Environment.ProcessorCount;
            int n = (srcList == null) ? 0 : srcList.Count;
            if (n == 0)
                return new object[0];
            int d = (n + maxLength - 1) / maxLength;
            if (d < prefMinCount)
            {
                if (n / prefMinCount >= minLength)
                    d = prefMinCount;
                else
                    d = (n + minLength - 1) / minLength;
                if (d < 2 || n <= d)
                    return new object[] { srcList };
            }
            var L = (n + d - 1) / d;
            while (L * (d - 1) > n)
                d--;
            var res = new object[d];
            for (int i = 0; i < d; i++)
            {
                int pos = i * L;
                int size = (pos + L >= n) ? n - pos : L;
                var part = new object[size];
                for (int j = 0; j < size; j++)
                    part[j] = srcList[pos + j];
                res[i] = part;
            }
            return res;
        }

        public static object MergeIndexedDicts([CanBeVector]object dicts)
        {
            var lsts = (IList)dicts;
            var res = new OPs.ListOf<IIndexedDict>();
            foreach (IList<IIndexedDict> lst in lsts)
                res.AddRange(lst);
            return res;
        }

        public static object MergeListsRows([CanBeVector]object lists)
        {
            var lsts = (IList)lists;
            var res = new OPs.ListOfConst();
            foreach (IList lst in lsts)
                res.AddRange(lst);
            return res;
        }

        public static object MergeListsColumns([CanBeVector]object lists)
        {
            var lsts = (IList)lists;
            int n = -1;
            foreach (IList lst in lsts)
                if (n < 0)
                    n = lst.Count;
                else if (lst.Count != n)
                    throw new ArgumentException("MergeListsColumns: all lists must have identical count of items");
            var res = new OPs.ListOfConst[n];
            for (int i = 0; i < n; i++)
                res[i] = new OPs.ListOfConst();
            foreach (IList lst in lsts)
                for (int i = 0; i < n; i++)
                    res[i].AddRange((IList)lst[i]);
            return res;
        }

        public static object DataToExpr(object data)
        {
            return CallExpr.Eval(DataToExpr(data, new Dictionary<object, bool>()));
        }

        static Expr[] DataToExpr(object data, Dictionary<object, bool> alreadyProcessed)
        {
            try
            {
                if (data == null)
                    return new Expr[] { new ConstExpr(null) };
                var type = data.GetType();
                if (type.IsPrimitive || type == typeof(string))
                    return new Expr[] { new ConstExpr(data) };
                var cnv = data as IConvertible;
                if (cnv != null)
                    return new Expr[] { new ConstExpr(cnv.ToString()) };
                var toStrMethod = type.GetMethod("ToString", System.Reflection.BindingFlags.Instance);
                if (toStrMethod != null)
                {
                    var txt = toStrMethod.Invoke(data, null);
                    return new Expr[] { new ConstExpr(txt) };
                }
                if (alreadyProcessed.ContainsKey(data))
                    return new Expr[] { new ReferenceExpr("↑") };
                alreadyProcessed.Add(data, true);
                var lst = new List<Expr>();
                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var field in fields)
                {
                    var value = field.GetValue(data);
                    var args = DataToExpr(value, alreadyProcessed);
                    lst.Add(new CallExpr(field.Name, args));
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
                    var args = DataToExpr(value, alreadyProcessed);
                    lst.Add(new CallExpr(prop.Name, args));
                }
                return lst.ToArray();
            }
            catch (Exception ex) { return new Expr[] { new ConstExpr(ex.Message) }; }
        }

        public static bool Num(object o, out double v)
        {
            v = 0;
            if (!W.Common.NumberUtils.IsNumber(o)) return false;
            v = Convert.ToDouble(o);
            return true;
        }

        public static bool NonZeroNum(object o, out double v) { return Num(o, out v) && v != 0; }
        public static bool PositiveNum(object o, out double v) { return Num(o, out v) && v > 0; }
        public static bool NotNegatNum(object o, out double v) { return Num(o, out v) && v >= 0; }

        [Arity(3, 4)]
        public static object D(IList args)
        {
            try
            {
                double flg, x, y;
                if (NonZeroNum(args[0], out flg) && NotNegatNum(args[1], out x) && NotNegatNum(args[2], out y))
                    return x - y;
                else if (args.Count > 3)
                    return args[3];
                else
                    return string.Empty;
            }
            catch { return string.Empty; }
        }

        [Arity(4, 4)]
        public static object dOdL(IList args)
        {
            double dQs, L1, O1, L2;
            if (NonZeroNum(args[0], out dQs) &&
                PositiveNum(args[1], out L1) && NotNegatNum(args[2], out O1) && NotNegatNum(args[3], out L2))
                return (L2 - L1) * O1 / L1;
            else
                return string.Empty;
        }

        [Arity(5, 5)]
        public static object dOdH(IList args)
        {
            double dQs, L1, O1, L2, O2;
            if (NonZeroNum(args[0], out dQs) &&
                PositiveNum(args[1], out L1) && NotNegatNum(args[2], out O1) &&
                NotNegatNum(args[3], out L2) && NotNegatNum(args[4], out O2))
                return O2 - L2 * (O1 / L1);
            else
                return string.Empty;
        }

        [Arity(2, 2)]
        public static object Where(IList args)
        {
            var srcs = Utils.ToIList(args[0]);
            if (srcs == null)
                return null;
            var cond = Utils.AsIList(args[1]);
            int n = srcs.Count;
            if (cond.Count == 1)
                if (Convert.ToBoolean(cond[0]))
                    return srcs;
                else return new object[0];
            if (n != cond.Count)
                System.Diagnostics.Trace.Assert(false, "Where: srcs.Count!=cond.Count");
            var res = new OPs.ListOfConst(n);
            for (int i = 0; i < n; i++)
                if (Convert.ToBoolean(cond[i]))
                    res.Add(srcs[i]);
            return res;
        }

        public static object Where([CanBeVector]object obj)
        {
            var lst = (IList)obj;
            var res = new OPs.ListOfConst(lst.Count);
            foreach (var tmp in lst)
            {
                var it = (IList)tmp;
                if (Convert.ToBoolean(it[0]))
                    res.Add(it[1]);
            }
            return res;
        }

        [Arity(1, 2)]
        public static object Distinct(IList args)
        {
            var sequence = args[0];
            var dict = new Dictionary<int, List<KeyValuePair<object, int>>>();
            int n = 0;
            var seqLst = sequence as IList;
            if (seqLst == null || seqLst.Count == 0)
                return sequence;
            foreach (var obj in seqLst)
            {
                int hash = obj.GetHashCode();
                List<KeyValuePair<object, int>> lst;
                if (!dict.TryGetValue(hash, out lst))
                {
                    lst = new List<KeyValuePair<object, int>>();
                    dict.Add(hash, lst);
                    lst.Add(new KeyValuePair<object, int>(obj, 1));
                    n++;
                    continue;
                }
                int i;
                for (i = lst.Count - 1; i >= 0; i--)
                    if (lst[i].Key.Equals(obj))
                    {
                        lst[i] = new KeyValuePair<object, int>(lst[i].Key, lst[i].Value + 1);
                        break;
                    }
                if (i < 0)
                {
                    lst.Add(new KeyValuePair<object, int>(obj, 1));
                    n++;
                }
            }
            if (args.Count < 2)
            {
                var res = new OPs.ListOfConst(n);
                foreach (var lst in dict.Values)
                    foreach (var item in lst)
                        res.Add(item.Key);
                return res;
            }
            else
            {
                var res = new OPs.ListOfConst(n);
                foreach (var lst in dict.Values)
                    foreach (var item in lst)
                    {
                        var pair = new OPs.ListOfConst(2);
                        pair.Add(item.Key);
                        pair.Add(item.Value);
                        res.Add(pair);
                    }
                return res;
            }
        }

        internal class AkkumulationItem
        {
            public readonly string Name;
            double sum;
            int count;
            public AkkumulationItem(string Name)
            { this.Name = Name; sum = 0; count = 0; }
            public void Add(double value)
            {
                if (double.IsNaN(value)) return;
                sum += value; count++;
            }
            public object Get(string what)
            {
                switch (what)
                {
                    case "sum": if (count > 0) return sum; else return string.Empty; // сумма значений
                    case "avg": if (count == 0) return string.Empty; else return sum / count; // среднее значение
                    case "cnt": return count; // количество добавленных значений
                    case "name": return Name; // (экранное) имя аккумулятора
                    default: throw new Exception("AkkumulationItem.Get: what?");
                }
            }
        }

        const string sAkkumulatorsDictionaryName = "_akkumulators_Dictionary";

        [Arity(0, 0)]
        [IsNotPure]
        public static object _NewDict(IList args)
        { return new Dictionary<string, object>(); }

        [Arity(4, 4), IsNotPure]
        public static object _Akkum(CallExpr ce, Generator.Ctx ctx)
        {
            int iAD = ctx.IndexOf(sAkkumulatorsDictionaryName);
            Expr init = null;
            if (iAD < 0)
                init = new CallExpr("SSV", new ConstExpr(sAkkumulatorsDictionaryName), new CallExpr("_NewDict"));
            var action = new CallExpr("_Akkum", ce.args[0], ce.args[1], ce.args[2], ce.args[3], new ReferenceExpr(sAkkumulatorsDictionaryName));
            var r = (init == null) ? action : new CallExpr("_Eval", init, action);
            return Generator.Generate(r, ctx);
        }

        [Arity(5, 5), IsNotPure]
        /// <summary>
        /// _Akkum(group, akk_id(s), caption(s), value(s), dictAkks)
        /// В группе "group" добавляет значение(я) "value(s)" в аккумулятор(ы) "akk_id(s)" 
        /// Экранное имя аккумулятора caption(s) определяется при первом добавлении значения
        /// размерности akk_id(s) и value(s) должны совпадать
        /// </summary>
        public static object _Akkum(IList args)
        {
            var groups = args[0] as IList;
            var keys = args[1] as IList;
            var values = args[3] as IList;
            var staticStorage = (Dictionary<string, object>)args[4];

            if (groups != null)
            {
                var key = (keys == null) ? Convert.ToString(args[1]) : null;
                var names = args[2] as IList;
                var name = (names == null) ? Convert.ToString(args[2]) : null;
                var value = (values == null) ? args[3] : null;
                lock (staticStorage)
                {
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var g = Convert.ToString(groups[i]);
                        var k = (keys == null) ? key : Convert.ToString(keys[i]);
                        var n = (names == null) ? name : names[i];
                        var v = (values == null) ? value : values[i];
                        Akkum(staticStorage, g, k, n, v);
                    }
                }
                return true;
            }
            else
                lock (staticStorage)
                {
                    Dictionary<string, AkkumulationItem> akks;
                    object obj;
                    string akkGroup = args[0].ToString();
                    if (!staticStorage.TryGetValue(akkGroup, out obj) ||
                        ((akks = obj as Dictionary<string, AkkumulationItem>) == null))
                    {
                        akks = new Dictionary<string, AkkumulationItem>();
                        staticStorage.Add(akkGroup, akks);
                    }

                    IList names = null;

                    if (keys.Count != values.Count)
                        throw new Exception("Wrong dimension of args[1] or args[3]");

                    for (int i = 0; i < keys.Count; i++)
                    {
                        string key = keys[i].ToString();
                        AkkumulationItem item;
                        if (!akks.TryGetValue(key, out item))
                        {
                            if (names == null)
                            {
                                object arg2 = args[2];
                                names = arg2 as IList;
                                if (names == null)
                                {
                                    if (arg2 == null)
                                        names = new object[0];
                                    else
                                        names = new object[] { arg2 };
                                }
                            }
                            string name;
                            if (i < names.Count)
                                name = Convert.ToString(names[i]);
                            else if (names.Count == 0)
                                name = key;
                            else name = string.Empty;
                            item = new AkkumulationItem(name);
                            akks[key] = item;
                        }
                        object tmp = values[i];
                        if (tmp != null)
                        {
                            double value = OPs.xl2dbl(tmp);
                            if (!double.IsNaN(value))
                                item.Add(value);
                        }
                    }
                }
            return true;
        }

        static void Akkum(Dictionary<string, object> staticStorage, string akkGroup, string key, object name, object value)
        {
            Dictionary<string, AkkumulationItem> akks;
            object obj;
            if (!staticStorage.TryGetValue(akkGroup, out obj) || ((akks = obj as Dictionary<string, AkkumulationItem>) == null))
            {
                akks = new Dictionary<string, AkkumulationItem>();
                staticStorage.Add(akkGroup, akks);
            }
            AkkumulationItem item;
            if (!akks.TryGetValue(key, out item))
            {
                item = new AkkumulationItem(Convert.ToString(name));
                akks[key] = item;
            }
            if (value == null)
                return;
            try
            {
                double v = OPs.xl2dbl(value);
                if (!double.IsNaN(v))
                    item.Add(v);
            }
            catch { }
        }

        [Arity(2, 3), IsNotPure]
        public static object _GetAkk(CallExpr ce, Generator.Ctx ctx)
        {
            //int iAD = ctx.IndexOf(sAkkumulatorsDictionaryName);
            //if (iAD < 0)
            //	throw new WRptException("_GetAkk: '_Akkum' function must be called at least once before _GetAkk");
            var args = new List<Expr>(ce.args);
            args.Add(new ReferenceExpr(sAkkumulatorsDictionaryName));
            return Generator.Generate(new CallExpr("_AkkGet", args), ctx);
        }

        [Arity(3, 4), IsNotPure]
        /// <summary>
        /// Варианты вызова:
        /// _AkkGet(группа, вид, dictAkks) - возвращает массив текущих значений "вид" аккумуляторов "группы".
        /// _AkkGet(группа, аккумулятор, вид, dictAkks) - возвращает значение "вид" "аккумулятора" в "группе".
        /// </summary>
        public static object _AkkGet(IList args)
        {
            try
            {
                string akkGroup = args[0].ToString();
                var staticStorage = (Dictionary<string, object>)args[args.Count - 1];
                Dictionary<string, AkkumulationItem> akks;
                object obj;
                lock (staticStorage)
                    if (!staticStorage.TryGetValue(akkGroup, out obj) ||
                        ((akks = obj as Dictionary<string, AkkumulationItem>) == null))
                    { return string.Empty; }
                if (args.Count == 4)
                {
                    string what = args[2].ToString();
                    string key = args[1].ToString();
                    AkkumulationItem item = akks[key];
                    return item.Get(what);
                }
                else // args.Count == 3
                {
                    string what = args[1].ToString();
                    List<string> keys = new List<string>(akks.Keys);
                    keys.Sort();
                    object[] results = new object[keys.Count];
                    for (int i = 0; i < keys.Count; i++)
                        results[i] = akks[keys[i]].Get(what);
                    return results;
                }
            }
            catch (Exception ex) { return ex.Wrap(); }
        }

        [Arity(0, int.MaxValue)]
        public static object _ImportCmdLineArgs(CallExpr ce, Generator.Ctx ctx)
        {
            var sb = new StringBuilder();
            int phase = 0;
            IList<string> names = null;
            if (ce.args.Count > 0)
            {
                names = new string[ce.args.Count];
                for (int i = ce.args.Count - 1; i >= 0; i--)
                {
                    var arg = ce.args[i];
                    var vn = arg as ReferenceExpr;
                    if (vn != null)
                        names[i] = vn.name;
                    else
                    {
                        var tmp = Generator.Generate(arg, ctx);
                        if (OPs.KindOf(tmp) != ValueKind.Const)
                            throw new Exception("_ImportCmdLineArgs: Constant value expected ('" + Convert.ToString(arg) + "')");
                        names[i] = Convert.ToString(tmp);
                    }
                }
                foreach (var name in names)
                {
                    int ndx = ctx.IndexOf(name);
                    if (ndx < 0 || Generator.Ctx.ctxDepthStep <= ndx)
                        ndx = ctx.CreateValue(name);
                    else if (ctx[ndx] != Generator.Undefined)
                        throw new Exception("_ImportCmdLineArgs: value with name '" + name + "' already defined");
                }
            }
            {
                string name = null;
                foreach (string clArg in System.Environment.GetCommandLineArgs())
                    switch (phase)
                    {
                        case 0:
                            if (clArg == "-pv") phase++;
                            break;
                        case 1:
                            name = clArg; phase++;
                            break;
                        case 2:
                            phase = 0;
                            if (names != null && names.IndexOf(name) < 0)
                                break;
                            int ndx = ctx.IndexOf(name);
                            if (ndx < 0 || Generator.Ctx.ctxDepthStep <= ndx)
                                ndx = ctx.CreateValue(name);
                            else if (ctx[ndx] != Generator.Undefined)
                                throw new Exception("_ImportCmdLineArgs: value with name '" + name + "' already defined");
                            ctx[ndx] = clArg;
                            sb.AppendFormat("{0}=\"{1}\"\n", name, clArg);
                            break;
                    }
            }
            return sb.ToString();
        }

        [Arity(1, int.MaxValue)]
        public static async Task<object> RptDbg(AsyncExprCtx ae, IList args)
        {
            try
            {
                var res = await OPs.ConstValueOf(ae, args[0]);
                for (int i = 1; i < args.Count; i++)
                {
                    var arg = await OPs.ConstValueOf(ae, args[i]);
                    if (i == 1)
                    {
                        var s = Convert.ToString(arg);
                        //if (s.StartsWith("Blk"))
                        Diagnostics.Logger.TraceInformation("{0}={1}", s, res);
                    }
                }
                return res;
            }
            catch (Exception ex)
            { return float.NaN; }
        }

        [Arity(3, 3)]
        public static object BETWEEN(CallExpr ce, Generator.Ctx ctx)
        {
            var vals = ce.args[0]; // new CallExpr("WRptDbg", ce.args[0], new ConstExpr(ce.args[0]));
            var mins = ce.args[1]; // new CallExpr("WRptDbg", ce.args[1], new ConstExpr(ce.args[1]));
            var maxs = ce.args[2]; // new CallExpr("WRptDbg", ce.args[2], new ConstExpr(ce.args[2]));
            var expr = new CallExpr("AND"
                , new BinaryExpr(ExprType.GreaterThanOrEqual, vals, mins)
                , new BinaryExpr(ExprType.LessThanOrEqual, vals, maxs)
                );
            return Generator.Generate(expr, ctx);
        }
    }
}
