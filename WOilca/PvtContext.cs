using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace W.Oilca
{
    public static partial class PVT
    {
        /// <summary>
        /// PVT arguments (can be associated only with constant values)
        /// </summary>
        public enum Arg
        {
            /// <summary>
            /// Special 'always empty/unknown value' argument
            /// </summary>
            None,
            /// <summary>
            /// Давление в стандартных условиях, МПа
            /// </summary>
            P_SC,
            /// <summary>
            /// Давление в пластовых условиях, МПа
            /// </summary>
            P_RES,
            P_SEP,
            /// <summary>
            /// Температура в стандартных условиях, °K
            /// </summary>
            T_SC,
            /// <summary>
            /// Температура в пластовых условиях, °K
            /// </summary>
            T_RES,
            T_SEP,
            /// <summary>
            /// соленость, мг/л
            /// </summary>
            S,
            /// <summary>
            /// Газосодержание
            /// </summary>
            Rsb,
            /// <summary>
            /// Давление насыщения воды, МРа
            /// </summary>
            Pb_w,
            Rho_w_sc,
            GAMMA_G,
            GAMMA_G_CORR, GAMMA_G_SEP,
            /// <summary>
            /// Относительная плотность нефти
            /// </summary>
            GAMMA_O,
            GAMMA_W,
            /// <summary>
            /// сжимаемость воды дистилированной
            /// </summary>
            CWD,
            MaxValue
        }

        /// <summary>
        /// PVT parameters (can be variable or calculable)
        /// </summary>
        public enum Prm
        {
            /// <summary>
            /// Special 'always empty/unknown value' parameter
            /// </summary>
            None,
            Pb,
            /// <summary>
            /// SolutionGOR
            /// </summary>
            Rs,
            Bo,
            Co,
            Bg,
            Bob,
            Rho_o, Rho_g, Rho_w,
            Mu_o,
            Mu_os,
            /// <summary>
            /// Вязкость дегазированной нефти (dead oil), сПз
            /// </summary>
            Mu_od,
            Mu_w, Mu_g,
            Cpo,
            Sigma_og,
            Sigma_wg,
            Tpc, Ppc,
            Z,
            Rsw,
            Bw,
            Cw,
            /// <summary>
            /// Давление, МПa
            /// </summary>
            P,
            /// <summary>
            /// Температура, °K
            /// </summary>
            T,
            MaxValue
        }

        public abstract class Context
        {
            #region Virtual methods
            [DebuggerHidden]
            public abstract double Get(Prm what, Context leaf, bool canThrow = true);
            [DebuggerHidden]
            public abstract double Get(Arg what, bool canThrow = true);
#if DEBUG
            protected abstract void FillDbgDict(Dictionary<string, string> dbgDict);
            public Dictionary<string, string> _DbgDict { get { var d = new Dictionary<string, string>(); FillDbgDict(d); return d; } }
#endif
            #endregion

            #region Utilities
            public double this[Prm what]
            {
                [DebuggerHidden]
                get => Get(what, this, true);
            }
            public double this[Arg what]
            {
                [DebuggerHidden]
                get => Get(what, true);
            }

            public bool TryGet(Prm what, out double value) => IsKnown(value = Get(what, this, false));
            public bool TryGet(Arg what, out double value) => IsKnown(value = Get(what, false));
            void CheckWith(Prm what)
            {
                if (what == Prm.None)
                    throw new ArgumentException($"'{nameof(Prm)}.{nameof(Prm.None)}' can't be associated with value or function");
                var v = values[(int)what];
                if (IsKnown(v))
                    throw new ArgumentException(FormattableString.Invariant($"'{nameof(Prm)}.{what}' is already associated with value '{v}'"));
            }
            #endregion

            #region Common parts of implementation
            protected readonly double[] values = new double[(int)Prm.MaxValue];
            protected void Set(Prm what, double value) { values[(int)what] = AsKnown(value); }
            public abstract Root root { get; }
            #endregion

            public delegate double Func(Context ctx);

            #region Root context (constants or functions) implementation
            public class Root : Context
            {
                readonly Func[] funcs = new Func[(int)Prm.MaxValue];
                protected readonly double[] args = new double[(int)Arg.MaxValue];

                [DebuggerHidden]
                new void CheckWith(Prm what)
                {
                    base.CheckWith(what);
                    var f = funcs[(int)what];
                    if (f != null)
                    {
                        var fn = (f.Target is Rescaler r) ? r.ToString() : f.Method.Name;
                        throw new ArgumentException(FormattableString.Invariant($"'{nameof(Prm)}.{what}' is already associated with function '{fn}'"));
                    }
                }

                [DebuggerHidden]
                public override double Get(Prm what, Context leaf, bool canThrow = true)
                {
                    int i = (int)what;
                    var v = values[i];
                    if (IsKnown(v))
                        return v;
                    if (funcs[i] == null)
                        if (canThrow && what != Prm.None)
                            throw new KeyNotFoundException($"{nameof(PVT)}.{nameof(Context)}.{nameof(Root)}: value or function for '{nameof(Prm)}.{what}' is not defined");
                        else return 0d;
                    // set temporary nonvalid value to avoid recursion
                    leaf.values[i] = double.NegativeInfinity;
                    // calc
                    v = funcs[i](leaf);
                    // set result value
                    leaf.Set(what, v);
                    return v;
                }

                [DebuggerHidden]
                public override double Get(Arg what, bool canThrow = true)
                {
                    int i = (int)what;
                    var v = args[i];
                    if (IsKnown(v))
                        return v;
                    if (canThrow)
                        throw new KeyNotFoundException($"{nameof(PVT)}.{nameof(Context)}.{nameof(Root)}: value for '{nameof(Arg)}.{what}' is not defined");
                    else return 0d;
                }

                public override Root root => this;

#if DEBUG
                protected override void FillDbgDict(Dictionary<string, string> dbgDict)
                {
                    for (int i = 0; i < (int)Arg.MaxValue; i++)
                    {
                        var v = args[i];
                        if (!IsKnown(v))
                            continue;
                        var key = ((Arg)i).ToString();
                        dbgDict['.' + key] = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    for (int i = 0; i < (int)Prm.MaxValue; i++)
                    {
                        var v = values[i];
                        var f = funcs[i];
                        if (f == null && !IsKnown(v))
                            continue;
                        var key = ((Prm)i).ToString();
                        if (!dbgDict.TryGetValue(key, out var s) && IsKnown(v))
                            s = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (f != null)
                        {
                            var fn = (f.Target is Rescaler r) ? r.ToString() : f.Method.Name;
                            dbgDict[key] = $"{s}//{fn}";
                        }
                        else
                            dbgDict[key] = s;
                    }
                }
#endif
                class Rescaler
                {
                    readonly Ref target;
                    readonly Ref[] refs;
                    readonly Func calc;
                    /// <summary>
                    /// cached fast rescale function
                    /// </summary>
                    Func resc;
                    public Rescaler(Context.Func calc, Ref target, params Ref[] refs)
                    {
                        this.calc = calc;
                        {
                            var uniq = new HashSet<Prm>();
                            uniq.Add(target.prm);
                            foreach (var r in refs)
                                if (!uniq.Add(r.prm))
                                    throw new ArgumentException($"Rescaler('{target.prm}'): reuse of the '{nameof(Prm)}.{r.prm}' is not allowed", nameof(refs));
                        }
                        this.target = target; this.refs = refs;
                    }

                    Func rscl1(double x1, double y1) { var f = calc; return ctx => f(ctx) - x1 + y1; }

                    Func rscl2(double x1, double x2, double y1, double y2)
                    {
                        if (U.isZero(x2 - x1))
                            return rscl1(x1, y1);
                        // (y1 + (x - x1) / (x2 - x1) * (y2 - y1))
                        var k = (y2 - y1) / (x2 - x1);
                        var b = y1 - x1 * k;
                        var f = calc;
                        return ctx => k * f(ctx) + b;
                    }

                    double RecursionError(Context _) => throw new InvalidOperationException($"Recursive dependency detected while rescaling '{target.prm}'");

                    public double Rescale(Context ctx)
                    {
                        if (resc != null)
                            // if fast rescale function already created, use it
                            return resc(ctx);

                        // prevent recursive calculation
                        resc = RecursionError;

                        // determine rescaling arguments
                        var root = ctx.root;
                        int i = (int)target.prm;
                        var val1 = 0d;
                        if (target.With1)
                        {   // calculate first reference value of target parameter
                            var bldr = root.NewCtx();
                            foreach (var r in refs)
                                if (r.With1) bldr.With(r.prm, r.Val1(root));
                            val1 = AsKnown(calc(bldr.Done()));
                        }
                        var val2 = 0d;
                        if (target.With2)
                        {   // calculate second reference value of target parameter
                            var bldr = root.NewCtx();
                            foreach (var r in refs)
                                if (r.With2) bldr.With(r.prm, r.Val2(root));
                            val2 = AsKnown(calc(bldr.Done()));
                        }

                        // create fast rescale function
                        if (IsKnown(val1) && IsKnown(val2))
                            resc = rscl2(val1, val2, target.Val1(root), target.Val2(root));
                        else if (IsKnown(val1))
                            resc = rscl1(val1, target.Val1(root));
                        else //if (IsKnown(val2))
                            resc = rscl1(val2, target.Val2(root));

                        // return result
                        return resc(ctx);
                    }

                    public override string ToString() => $"Rescale({calc.Method.Name})";
                }

                public class Builder
                {
                    Root root;
                    public Builder() { root = new Root(); }

                    [DebuggerHidden]
                    public Builder With(Arg what, double value)
                    {
                        if (what == Arg.None)
                            throw new ArgumentException($"'{nameof(Arg)}.{nameof(Arg.None)}' can't be associated with value");
                        int i = (int)what;
                        var v = root.args[i];
                        if (IsKnown(v))
                            throw new ArgumentException(FormattableString.Invariant($"'{nameof(Arg)}.{what}' is already associated with value '{v}'"));
                        root.args[i] = AsKnown(value);
                        return this;
                    }
                    [DebuggerHidden]
                    public Builder With(Prm what, double value)
                    {
                        root.CheckWith(what);
                        root.Set(what, value);
                        return this;
                    }
                    [DebuggerHidden]
                    public Builder With(Prm what, Func calc)
                    {
                        System.Diagnostics.Debug.Assert(calc != null);
                        root.CheckWith(what);
                        root.funcs[(int)what] = calc;
                        return this;
                    }
                    [DebuggerHidden]
                    public Builder WithRescale(Ref target, Func calc, params Ref[] refs)
                    {
                        return With(target.prm, new Rescaler(calc, target, refs).Rescale);
                    }
                    public Context Done()
                    {
                        System.Diagnostics.Debug.Assert(root != null);
                        var tmp = root; root = null;
                        return tmp;
                    }
                }
            }
            #endregion

            #region Leaf context (only for overriden values) implementation
            public class Leaf : Context
            {
                public readonly Context parent;

                private Leaf(Context parent)
                {
                    System.Diagnostics.Debug.Assert(parent != null);
                    this.parent = parent;
                }

                [DebuggerHidden]
                public override double Get(Prm what, Context leaf, bool canThrow = true)
                {
                    int i = (int)what;
                    var v = values[i];
                    if (IsKnown(v))
                        return v;
                    if (parent == null)
                        throw new KeyNotFoundException($"{nameof(PVT)}.{nameof(Context)}.{nameof(Leaf)}: value of '{nameof(Prm)}.{what}' is not defined");
                    return parent.Get(what, this, canThrow);
                }

                [DebuggerHidden]
                public override double Get(Arg what, bool canThrow = true) => parent.Get(what, canThrow);

                public override Root root => parent.root;

                public static Leaf NewWith(Context parent, Prm what, double value)
                {
                    if (what == Prm.None)
                        throw new ArgumentException($"'{nameof(Prm)}.{nameof(Prm.None)}' can't be associated with value or function");
                    var ctx = new Leaf(parent);
                    ctx.values[(int)what] = AsKnown(value);
                    return ctx;
                }

#if DEBUG
                protected override void FillDbgDict(Dictionary<string, string> dbgDict)
                {
                    for (int i = 0; i < (int)Prm.MaxValue; i++)
                    {
                        var v = values[i];
                        if (!IsKnown(v))
                            continue;
                        var key = ((Prm)i).ToString();
                        if (!dbgDict.TryGetValue(key, out var s))
                            dbgDict[key] = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    parent.FillDbgDict(dbgDict);
                }
#endif

                public class Builder
                {
                    Leaf ctx;
                    public Builder(Context parent) { ctx = new Leaf(parent); }
                    public Builder With(Prm prm, double value)
                    {
                        ctx.values[(int)prm] = AsKnown(value);
                        return this;
                    }
                    public Context Done()
                    {
                        System.Diagnostics.Debug.Assert(ctx != null);
                        var tmp = ctx; ctx = null;
                        return tmp;
                    }
                }
            }
            #endregion
        }

        #region "Factory" functions
        public static Context.Root.Builder NewCtx() => new Context.Root.Builder();
        public static Context.Leaf.Builder NewCtx(this Context parent) => new Context.Leaf.Builder(parent);
        public static Context NewWith(this Context parent, Prm what, double value) => Context.Leaf.NewWith(parent, what, value);
        #endregion

        #region Implementation is specific for little-endian archs

        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleUInt64
        {
            [FieldOffset(0)] public double d;
            [FieldOffset(0)] public ulong u64;
        }

        /// <summary>
        /// True zero (+0) is interpreted as empty/unknown value
        /// </summary>
        private static bool IsKnown(double value)
        {
            var t = new DoubleUInt64() { d = value };
            return t.u64 != 0;
        }

        /// <summary>
        /// Replace true zero (+0) with signed zero (-0) to indicate nonempty/known value
        /// </summary>
        private static double AsKnown(double value)
        {
            var t = new DoubleUInt64() { d = value };
            if (t.u64 != 0)
                return value;
            // return "signed zero" to indicate nonempty value
            t.u64 = 0x8000000000000000ul;
            return t.d;
        }
        #endregion

        /// <summary>
        /// Reference values for parameter
        /// </summary>
        public class Ref
        {
            public readonly Prm prm;
            readonly Arg arg1, arg2;
            readonly double val1, val2;

            public Ref(Prm prm, Arg val1, Arg val2) { this.prm = prm; this.arg1 = val1; this.arg2 = val2; }
            public Ref(Prm prm, Arg val1, double val2) { this.prm = prm; this.arg1 = val1; this.val2 = AsKnown(val2); }
            public Ref(Prm prm, double val1, Arg val2) { this.prm = prm; this.val1 = AsKnown(val1); this.arg2 = val2; }
            public Ref(Prm prm, double val1, double val2) { this.prm = prm; this.val1 = AsKnown(val1); this.val2 = AsKnown(val2); }

            public bool With1 => arg1 != Arg.None || IsKnown(val1);
            public bool With2 => arg2 != Arg.None || IsKnown(val2);
            public double Val1(Context ctx) => (arg1 != Arg.None) ? ctx[arg1] : val1;
            public double Val2(Context ctx) => (arg2 != Arg.None) ? ctx[arg2] : val2;
        }

        public static Ref _(this Prm prm, Arg val1, Arg val2) => new Ref(prm, val1, val2);
        public static Ref _(this Prm prm, Arg val1, double val2) => new Ref(prm, val1, val2);
        public static Ref _(this Prm prm, double val1, Arg val2) => new Ref(prm, val1, val2);
        public static Ref _(this Prm prm, double val1, double val2) => new Ref(prm, val1, val2);
    }

    public static class U
    {
        public static bool isEQ(double lhs, double rhs, double tol = 1.0e-15) => Math.Abs(lhs - rhs) < tol;
        public static bool isGE(double lhs, double rhs, double tol = 1.0e-15) => lhs > rhs || isEQ(lhs, rhs, tol);
        public static bool isLE(double lhs, double rhs, double tol = 1.0e-15) => lhs < rhs || isEQ(lhs, rhs, tol);
        public static bool isZero(double v) => isEQ(v, 0.0);
        public static double Min(double a, double b) => a < b ? a : b;
        public static double Max(double a, double b) => (a > b) ? a : b;
        static readonly double InvLn10 = 1 / Math.Log(10.0);
        public static double log10(double x) => Math.Log(x) * InvLn10;
        public static double Pow(double x, double y)
        {
            if (x < 0)
                throw new ArgumentException($"Negative PowX: {x}^{y}");
            return Math.Pow(x, y);
        }
        public static double Kelv2Fahr(double T) => 1.8 * T - 460.0;
        public static double Atm2MPa(this double Atm) => Atm * 0.101325;
        public static double MPa2Atm(this double MPa) => MPa * (1 / 0.101325);
        public static double Cel2Kel(this double C) => C + 273.15;
        public static double Kev2Cel(this double K) => K - 273.15;
    }
}
