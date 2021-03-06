﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace W.Oilca
{
    public static partial class PVT
    {
        public class PvtInitException : Exception { public PvtInitException(string msg) : base(msg) { } }
        public class PvtCalcException : Exception { public PvtCalcException(string msg) : base(msg) { } }

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
#if DEBUG
            #region Virtual methods
            protected abstract void FillDbgDict(Dictionary<string, string> dbgDict);
            public Dictionary<string, string> _DbgDict { get { var d = new Dictionary<string, string>(); FillDbgDict(d); return d; } }
            #endregion
#endif

            #region Utilities
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            [DebuggerHidden]
            public double Get(Prm what, Context leaf, bool canThrow = true)
            {
                int i = (int)what;
                var v = values[i];
                if (IsKnown(v))
                    return v;
                if (root == this)
                    return root.CalcPrm(what, leaf, canThrow);
                else
                    return root.Get(what, leaf, canThrow);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            [DebuggerHidden]
            public double Get(Arg what, bool canThrow = true) => (root != this) ? root.GetArg(what, canThrow) : ((Root)this).GetArg(what, canThrow);

            public double this[Prm what]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [DebuggerHidden]
                get => Get(what, this, true);
            }
            public double this[Arg what]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [DebuggerHidden]
                get => Get(what, true);
            }

            void CheckWith(Prm what)
            {
                if (what == Prm.None)
                    throw new PvtInitException($"'{nameof(Prm)}.{nameof(Prm.None)}' can't be associated with value or function");
                var v = values[(int)what];
                if (IsKnown(v))
                    throw new PvtInitException(FormattableString.Invariant($"'{nameof(Prm)}.{what}' is already associated with value '{v}'"));
            }
            #endregion

            #region Common parts of implementation
            public readonly Root root;
            protected readonly double[] values = new double[(int)Prm.MaxValue];
            void Clear() { for (int i = 0; i < values.Length; i++) values[i] = UnknownValue; }
            protected Context(Root root)
            {
                this.root = root ?? (Root)this;
                Clear();
            }
            protected void Set(Prm what, double value) { values[(int)what] = value; }
            #endregion

            public delegate double Func(Context ctx);

            #region Root context (constants or functions) implementation
            public class Root : Context
            {
                readonly Func[] funcs = new Func[(int)Prm.MaxValue];
                protected readonly double[] args = new double[(int)Arg.MaxValue];
                protected Root() : base(null) { for (int i = 0; i < args.Length; i++) args[i] = UnknownValue; }
                [DebuggerHidden]
                new void CheckWith(Prm what)
                {
                    base.CheckWith(what);
                    var f = funcs[(int)what];
                    if (f != null)
                    {
                        var fn = (f.Target is Rescaler r) ? r.ToString() : f.Method.Name;
                        throw new PvtInitException(FormattableString.Invariant($"'{nameof(Prm)}.{what}' is already associated with function '{fn}'"));
                    }
                }

                [DebuggerHidden]
                public double CalcPrm(Prm what, Context ctx, bool canThrow = true)
                {
                    int i = (int)what;
                    if (funcs[i] == null)
                        if (canThrow && what != Prm.None)
                            throw new PvtInitException($"{nameof(PVT)}.{nameof(Context)}.{nameof(Root)}: value or function for '{nameof(Prm)}.{what}' is not defined");
                        else return 0d;
                    // set temporary nonvalid value to avoid recursion
                    ctx.values[i] = double.NegativeInfinity;
                    // calc
                    var v = funcs[i](ctx);
                    // set result value
                    ctx.Set(what, v);
                    return v;
                }

                [DebuggerHidden]
                public double GetArg(Arg what, bool canThrow = true)
                {
                    int i = (int)what;
                    var v = args[i];
                    if (IsKnown(v))
                        return v;
                    if (canThrow)
                        throw new PvtInitException($"{nameof(PVT)}.{nameof(Context)}.{nameof(Root)}: value for '{nameof(Arg)}.{what}' is not defined");
                    else return 0d;
                }


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
                                    throw new PvtInitException($"Rescaler('{target.prm}'): reuse of the '{nameof(Prm)}.{r.prm}' is not allowed");
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

                    double RecursionError(Context _) => throw new PvtInitException($"Recursive dependency detected while rescaling '{target.prm}'");

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
                        var val1 = UnknownValue;
                        if (target.With1)
                        {   // calculate first reference value of target parameter
                            var bldr = root.NewCtx();
                            foreach (var r in refs)
                                if (r.With1) bldr.With(r.prm, r.Val1(root));
                            val1 = calc(bldr.Done());
                        }
                        var val2 = UnknownValue;
                        if (target.With2)
                        {   // calculate second reference value of target parameter
                            var bldr = root.NewCtx();
                            foreach (var r in refs)
                                if (r.With2) bldr.With(r.prm, r.Val2(root));
                            val2 = calc(bldr.Done());
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

                public struct Builder
                {
                    Root root;
                    public Builder(int _) { root = new Root(); }

                    [DebuggerHidden]
                    public Builder With(Arg what, double value)
                    {
                        if (what == Arg.None)
                            throw new PvtInitException($"'{nameof(Arg)}.{nameof(Arg.None)}' can't be associated with value");
                        int i = (int)what;
                        var v = root.args[i];
                        if (IsKnown(v))
                            throw new PvtInitException(FormattableString.Invariant($"'{nameof(Arg)}.{what}' is already associated with value '{v}'"));
                        root.args[i] = value;
                        return this;
                    }

                    [DebuggerHidden]
                    public Builder With(Arg what, Arg from) => With(what, root[from]);

                    [DebuggerHidden]
                    public Builder With(Arg what, Func<Root, double> calcArg) => With(what, calcArg(root));

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
                    public Root Done()
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
                private Leaf(Root root) : base(root)
                {
                    System.Diagnostics.Debug.Assert(root != null);
                }

                public static Leaf NewWith(Context from, Prm what, double value)
                {
                    if (what == Prm.None)
                        throw new PvtInitException($"'{nameof(Prm)}.{nameof(Prm.None)}' can't be associated with value or function");
                    var ctx = new Leaf(from.root);
                    ctx.values[(int)what] = value;
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
                    root.FillDbgDict(dbgDict);
                }
#endif

                public struct Builder
                {
                    Leaf ctx;
                    public Builder(Root root) { ctx = new Leaf(root); }
                    private Builder(Leaf ctx) { this.ctx = ctx; }
                    public Builder With(Prm prm, double value)
                    {
                        ctx.values[(int)prm] = value;
                        return this;
                    }
                    public Leaf Done()
                    {
                        System.Diagnostics.Debug.Assert(ctx != null);
                        var tmp = ctx; ctx = null;
                        return tmp;
                    }

                    public static Builder Reuse(Leaf ctx)
                    {
                        ctx.Clear();
                        return new Builder(ctx);
                    }
                }
            }
            #endregion
        }

        #region "Factory" functions
        public static Context.Root.Builder NewCtx() => new Context.Root.Builder(0);
        public static Context.Leaf.Builder NewCtx(this Context some) => new Context.Leaf.Builder(some.root);
        public static Context.Leaf.Builder Reuse(this Context.Leaf ctx) => Context.Leaf.Builder.Reuse(ctx);
        public static Context.Leaf NewWith(this Context from, Prm what, double value) => Context.Leaf.NewWith(from, what, value);
        #endregion

        public const double UnknownValue = -4.94065645841246E-324;

        /// <summary>
        /// True zero (+0) is interpreted as empty/unknown value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsKnown(double value) => value != UnknownValue;

        /// <summary>
        /// Reference values for parameter
        /// </summary>
        public class Ref
        {
            public readonly Prm prm;
            readonly Arg arg1, arg2;
            readonly double val1 = UnknownValue, val2 = UnknownValue;

            public Ref(Prm prm, Arg val1, Arg val2) { this.prm = prm; this.arg1 = val1; this.arg2 = val2; }
            public Ref(Prm prm, Arg val1, double val2) { this.prm = prm; this.arg1 = val1; this.val2 = val2; }
            public Ref(Prm prm, double val1, Arg val2) { this.prm = prm; this.val1 = val1; this.arg2 = val2; }
            public Ref(Prm prm, double val1, double val2) { this.prm = prm; this.val1 = val1; this.val2 = val2; }

            [DebuggerHidden]
            public bool With1 => arg1 != Arg.None || IsKnown(val1);
            [DebuggerHidden]
            public bool With2 => arg2 != Arg.None || IsKnown(val2);
            [DebuggerHidden]
            public double Val1(Context ctx) => (arg1 != Arg.None) ? ctx[arg1] : val1;
            [DebuggerHidden]
            public double Val2(Context ctx) => (arg2 != Arg.None) ? ctx[arg2] : val2;
        }

        public static Ref _(this Prm prm, Arg val1, Arg val2) => new Ref(prm, val1, val2);
        public static Ref _(this Prm prm, Arg val1, double val2) => new Ref(prm, val1, val2);
        public static Ref _(this Prm prm, double val1, Arg val2) => new Ref(prm, val1, val2);
        public static Ref _(this Prm prm, double val1, double val2) => new Ref(prm, val1, val2);
    }

    public static class U
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool isEQ(double lhs, double rhs, double tol = 1.0e-15) => Math.Abs(lhs - rhs) < tol;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool isGE(double lhs, double rhs, double tol = 1.0e-15) => lhs > rhs || isEQ(lhs, rhs, tol);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool isLE(double lhs, double rhs, double tol = 1.0e-15) => lhs < rhs || isEQ(lhs, rhs, tol);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool isZero(double v) => isEQ(v, 0.0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Min(double a, double b) => a < b ? a : b;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Max(double a, double b) => (a > b) ? a : b;
        static readonly double InvLn10 = 1 / Math.Log(10.0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double log10(double x) => Math.Log(x) * InvLn10;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Pow(double x, double y)
        {
            if (x < 0)
                throw new ArithmeticException($"Negative PowX: {x}^{y}");
            return Math.Pow(x, y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Kelv2Fahr(double T) => 1.8 * T - 460.0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Atm2MPa(this double Atm) => Atm * 0.101325;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double MPa2Atm(this double MPa) => MPa * (1 / 0.101325);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Cel2Kel(this double C) => C + 273.15;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Kel2Cel(this double K) => K - 273.15;
    }
}
