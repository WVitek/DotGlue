using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using System.Threading.Tasks;

namespace W.Expressions
{
    public static class FuncDefs_Excel
    {
        public static readonly object False = (object)false;
        public static readonly object True = (object)true;

        public static object SIGN(object x) { return Math.Sign(OPs.xl2dbl(x)); }
        public static object ABS(object x) { return Math.Abs(OPs.xl2dbl(x)); }
        public static object INT(object x) { return Math.Floor(OPs.xl2dbl(x)); }
        public static object SIN(object x) { return Math.Sin(OPs.xl2dbl(x)); }
        public static object COS(object x) { return Math.Cos(OPs.xl2dbl(x)); }
        public static object ATAN(object x) { return Math.Atan(OPs.xl2dbl(x)); }
        public static object ATAN2(object x, object y) { return Math.Atan2(OPs.xl2dbl(x), OPs.xl2dbl(y)); }
        public static object EXP(object x) { return Math.Exp(OPs.xl2dbl(x)); }
        public static object LN(object x) { return Math.Log(OPs.xl2dbl(x)); }
        public static object SQRT(object x) { return Math.Sqrt(OPs.xl2dbl(x)); }
        public static object POWER(object x, object y) { return Math.Pow(OPs.xl2dbl(x), OPs.xl2dbl(y)); }
        public static object ROUND(object x, object nDigits) { return Math.Round(OPs.xl2dbl(x), Convert.ToInt32(nDigits)); }
        public static object LOG(object x, object y) { return Math.Log(OPs.xl2dbl(x), OPs.xl2dbl(y)); }
        public static object MOD(object _x, object _y)
        {
            var x = OPs.xl2dbl(_x);
            var y = OPs.xl2dbl(_y);
            var d = Math.Truncate(x / y);
            return x - d * y;
        }
        [Arity(0, 0)]
        public static object PI(IList args) { return Math.PI; }

        //[Arity(2, 3)]
        //public static object IF(CallExpr ce, Generator.Ctx ctx)
        //{ return FuncDefs_Core.IF(ce, ctx); }

        public static object SUM(IList args)
        {
            double sum = 0;
            foreach (object v in OPs.Flatten(args))
            {
                double? tmp = OPs.xl2dbl2(v);
                if (tmp.HasValue)
                    sum += tmp.Value;
            }
            return sum;
        }

        [Arity(1, 1)]
        public static object OR(CallExpr ce, Generator.Ctx ctx)
        { return Generator.Generate(ce.args[0], ctx); }

        [Arity(2, int.MaxValue)]
        public static async Task<object> OR(AsyncExprCtx ae, IList args)
        {
            bool[] results = null;
            int cnt = args.Count;
            for (int j = 0; j < cnt; j++)
            {
                object v = await OPs.ConstValueOf(ae, args[j]);
                try
                {
                    IList lst = v as IList;
                    if (lst == null)
                    {
                        if (OPs.xl2bool(v))
                        {
                            if (results == null)
                                return True; // scalar, scalar
                            else
                            {   // vector, scalar
                                for (int i = results.Length - 1; i >= 0; i--)
                                    results[i] = true;
                                return results;
                            }
                        }
                    }
                    else
                    {
                        bool allTrue = true;
                        if (results == null)
                        {   // scalar, vector
                            int n = lst.Count;
                            results = new bool[n];
                            for (int i = n - 1; i >= 0; i--)
                            {
                                bool tmp = OPs.xl2bool(lst[i]);
                                allTrue = allTrue && tmp;
                                results[i] = tmp;
                            }
                        }
                        else
                        {   // vector, vector
                            int n = Math.Min(results.Length, lst.Count);
                            if (n < results.Length)
                                Array.Resize<bool>(ref results, n);
                            for (int i = n - 1; i >= 0; i--)
                            {
                                if (results[i]) continue;
                                bool tmp = OPs.xl2bool(lst[i]);
                                allTrue = allTrue && tmp;
                                results[i] = tmp;
                            }
                        }
                        if (allTrue)
                            return results;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            return results ?? False;
        }

        [Arity(1, 1)]
        public static object AND(CallExpr ce, Generator.Ctx ctx)
        { return Generator.Generate(ce.args[0], ctx); }

        [Arity(2, int.MaxValue)]
        public static async Task<object> AND(AsyncExprCtx ae, IList args)
        {
            bool[] results = null;
            int cnt = args.Count;
            for (int j = 0; j < cnt; j++)
            {
                object v = await OPs.ConstValueOf(ae, args[j]);
                try
                {
                    IList lst = v as IList;
                    if (lst == null)
                    {
                        if (!OPs.xl2bool(v))
                        {
                            if (results == null)
                                return False; // scalar, scalar
                            else
                            {   // vector, scalar
                                for (int i = results.Length - 1; i >= 0; i--)
                                    results[i] = false;
                                return results;
                            }
                        }
                    }
                    else
                    {
                        bool allFalse = true;
                        if (results == null)
                        {   // scalar, vector
                            int n = lst.Count;
                            results = new bool[n];
                            for (int i = n - 1; i >= 0; i--)
                            {
                                bool tmp = OPs.xl2bool(lst[i]);
                                allFalse = allFalse && !tmp;
                                results[i] = tmp;
                            }
                        }
                        else
                        {   // vector, vector
                            int n = Math.Min(results.Length, lst.Count);
                            if (n < results.Length)
                                Array.Resize<bool>(ref results, n);
                            for (int i = n - 1; i >= 0; i--)
                            {
                                if (!results[i]) continue;
                                bool tmp = OPs.xl2bool(lst[i]);
                                allFalse = allFalse && !tmp;
                                results[i] = tmp;
                            }
                        }
                        if (allFalse)
                            return results;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            return results ?? True;
        }

        public static object MIN(IList args)
        {
            double min = double.MaxValue;
            foreach (object v in OPs.Flatten(args))
            {
                double? tmp = OPs.xl2dbl2(v);
                if (tmp.HasValue && tmp < min)
                    min = tmp.Value;
            }
            return min;
        }

        public static object MAX(IList args)
        {
            double max = double.MinValue;
            foreach (object v in OPs.Flatten(args))
            {
                double? tmp = OPs.xl2dbl2(v);
                if (tmp.HasValue && tmp > max)
                    max = tmp.Value;
            }
            return max;
        }

        public static object ISBLANK(object x) { return W.Common.Utils.IsEmpty(x); }

        [Arity(0, 0)]
        [IsNotPure]
        public static object TODAY(IList args) { return OPs.ToExcelDate(DateTime.Now.Date); }

        [Arity(0, 0)]
        [IsNotPure]
        public static object NOW(IList args) { return OPs.ToExcelDate(DateTime.Now); }

        public static object YEAR(object date)
        {
            if (W.Common.Utils.IsEmpty(date))
                return DBNull.Value;
            return OPs.FromExcelDate(OPs.xl2dbl(date)).Year;
        }
        public static object MONTH(object date)
        {
            if (W.Common.Utils.IsEmpty(date))
                return DBNull.Value;
            return OPs.FromExcelDate(OPs.xl2dbl(date)).Month;
        }
        public static object DAY(object date)
        {
            if (W.Common.Utils.IsEmpty(date))
                return DBNull.Value;
            return OPs.FromExcelDate(OPs.xl2dbl(date)).Day;
        }

        [Arity(1, 1)]
        public static async Task<object> ISERR(AsyncExprCtx ctx, IList args)
        {
            try
            {
                object value = await OPs.ConstValueOf(ctx, args[0]);
                var lst = value as IList;
                if (lst != null)
                {
                    var res = lst.ToArray(item => item is Exception || item is W.Common.ErrorWrapper);
                    return res;
                }
                else return value is Exception || value is W.Common.ErrorWrapper;
            }
            catch (OperationCanceledException) { throw; }
            catch { return True; }
        }

        public static object IFERROR(object a, object b)
        {
            if (W.Common.Utils.IsEmpty(a) || a is Exception || a is W.Common.ErrorWrapper)
                return b;
            var ic = a as IConvertible;
            if (ic != null)
                switch (ic.GetTypeCode())
                {
                    case TypeCode.Double:
                    case TypeCode.Single:
                        var d = Convert.ToDouble(a);
                        if (double.IsNaN(d) || double.IsInfinity(d))
                            return b;
                        break;
                }
            return a;
        }

        public static object ISNUMBER(object v) { return W.Common.NumberUtils.IsNumber(v); }

        public static object UPPER(object v) { return Convert.ToString(v).ToUpperInvariant(); }
        public static object LOWER(object v) { return Convert.ToString(v).ToLowerInvariant(); }

        public static object NOT(object v) { return !OPs.xl2bool(v); }

        [Arity(3, 3)]
        public static object DATE(IList args)
        {
            var dt = new DateTime(Convert.ToInt32(args[0]), Convert.ToInt32(args[1]), Convert.ToInt32(args[2]));
            return dt.ToOADate();
        }

        static bool TryCastAsDateTime(object arg, out DateTime dt)
        {
            return W.Common.Utils.TryCastStruct<DateTime>(arg, out dt)
                || DateTime.TryParse(Convert.ToString(arg, System.Globalization.CultureInfo.InvariantCulture), out dt);
        }

        [Arity(0, 0)]
        public static object DATEVALUE(IList args)
        { return OPs.ToExcelDate(DateTime.Now); }

        public static object DATEVALUE(object arg)
        {
            if (arg == null)
                return null;
            DateTime dt;
            if (!TryCastAsDateTime(arg, out dt))
                return new ArgumentException("DATEVALUE");

            return OPs.ToExcelDate(dt.Date);
        }

        [Arity(0, 0)]
        public static object TIMEVALUE(IList args)
        {
            double v = OPs.ToExcelDate(DateTime.Now);
            return v - Math.Truncate(v);
        }

        public static object TIMEVALUE(object arg)
        {
            DateTime dt;
            if (!TryCastAsDateTime(arg, out dt))
                return new ArgumentException("TIMEVALUE");
            double v = OPs.ToExcelDate(dt);
            return v - Math.Truncate(v);
        }

        public static object CONCATENATE(IList args)
        {
            StringBuilder result = new StringBuilder();
            foreach (object obj in args)
                result.Append(obj);
            return result.ToString();
        }

        [Arity(2, 3)]
        public static object FIND(IList args)
        {
            string what = Convert.ToString(args[0]);
            string where = Convert.ToString(args[1]);
            int from = 0;
            if (args.Count > 2)
                from = Convert.ToInt32(args[2]) - 1;
            int i = where.IndexOf(what, from, StringComparison.Ordinal);
            if (i < 0)
                return double.NaN;
            return i + 1;
        }

        [Arity(4, 4)]
        public static object REPLACE(IList args)
        {
            string text = Convert.ToString(args[0]);
            int from = Convert.ToInt32(args[1]);
            int L = text.Length;
            int ia = from - 1;
            if (ia >= L)
                return text;
            string sa = (0 <= ia) ? text.Substring(0, ia) : string.Empty;
            int count = Convert.ToInt32(args[2]);
            int ib = ia + count;
            string sb = (ib < L) ? text.Substring(ib, L - ib) : string.Empty;
            string what = Convert.ToString(args[3]);
            return string.Concat(sa, what, sb);
        }

        [Arity(3, 3)]
        public static object SUBSTITUTE(IList args)
        {
            var text = Convert.ToString(args[0]);
            var from = Convert.ToString(args[1]);
            var to = Convert.ToString(args[2]);
            return text.Replace(from, to);
        }

        public static object CHAR(object v)
        {
            ushort code = Convert.ToUInt16(v);
            string s = ((char)code).ToString();
            return s;
        }


        public static object SEARCH(object argWhat, object argWhere)
        {
            string what = Convert.ToString(argWhat);
            string where = Convert.ToString(argWhere);
            int i = where.IndexOf(what);
            if (i >= 0)
                return i + 1;
            else
                return double.NaN;
        }

        public static object TRIM(object v)
        {
            if (W.Common.Utils.IsEmpty(v))
                return v;
            var sb = new StringBuilder(Convert.ToString(v));
            int i;
            // trim leading spaces
            for (i = 0; i < sb.Length && char.IsWhiteSpace(sb[i]); i++) ;
            if (i > 0)
                sb.Remove(0, i);
            // trim trailing spaces
            for (i = sb.Length - 1; i >= 0 && char.IsWhiteSpace(sb[i]); i--) ;
            if (i < sb.Length - 1)
                sb.Remove(i + 1, sb.Length - i - 1);
            // remove doubled spaces
            for (i = sb.Length - 2; i >= 2; i--)
                if (char.IsWhiteSpace(sb[i]) && char.IsWhiteSpace(sb[i - 1]))
                    sb.Remove(i, 1);
            // done
            return sb.ToString();
        }

        public static object LEN(object arg)
        {
            string s = Convert.ToString(arg);
            return string.IsNullOrEmpty(s) ? 0 : s.Length;
        }

        [Arity(3, 3)]
        public static object MID(IList args)
        {
            string s = Convert.ToString(args[0]);
            int fromPos = Convert.ToInt32(args[1]);
            int count = Convert.ToInt32(args[2]);
            return s.Substring(fromPos - 1, count);
        }
    }
}
