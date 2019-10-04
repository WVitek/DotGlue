using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Pipe.Exercises
{
    public static partial class PVT
    {
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

        #region Implementation is specific for little-endian archs

        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleUInt64
        {
            [FieldOffset(0)] public double d;
            [FieldOffset(0)] public ulong u64;
        }

        private static bool IsKnown(double value)
        {
            var t = new DoubleUInt64() { d = value };
            return t.u64 != 0;
        }

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

        public class Ref
        {
            readonly Prm prm;
            readonly Arg arg1, arg2;
            readonly double val1, val2;

            public Ref(Prm prm, Arg val1, Arg val2) { this.prm = prm; this.arg1 = val1; this.arg2 = val2; }
            public Ref(Prm prm, Arg val1, double val2) { this.prm = prm; this.arg1 = val1; this.val2 = AsKnown(val2); }
            public Ref(Prm prm, double val1, Arg val2) { this.prm = prm; this.val1 = AsKnown(val1); this.arg2 = val2; }
            public Ref(Prm prm, double val1, double val2) { this.prm = prm; this.val1 = AsKnown(val1); this.val2 = AsKnown(val2); }

            public bool With1 => arg1 != Arg.None || IsKnown(val1);
            public bool With2 => arg2 != Arg.None || IsKnown(val2);
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
            #endregion

            #region Implementation part
            protected readonly double[] values = new double[(int)Prm.MaxValue];
            protected void Set(Prm what, double value) { values[(int)what] = AsKnown(value); }
            #endregion

            #region Root context (constants or functions) implementation

            public delegate double Func(Context ctx);

            public class Root : Context
            {
                readonly Func[] funcs = new Func[(int)Prm.MaxValue];
                protected readonly double[] args = new double[(int)Arg.MaxValue];
                protected readonly double[] ref1 = new double[(int)Prm.MaxValue];
                protected readonly double[] ref2 = new double[(int)Prm.MaxValue];

                private Root() { }

                [DebuggerHidden]
                public override double Get(Prm what, Context leaf, bool canThrow = true)
                {
                    int i = (int)what;
                    var v = values[i];
                    if (IsKnown(v))
                        return v;
                    if (funcs[i] == null)
                        if (canThrow)
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
                            dbgDict[key] = $"{s}//{f.Method.Name}";
                        else
                            dbgDict[key] = s;
                    }
                }
#endif

                public class Builder
                {
                    Root ctx;
                    public Builder() { ctx = new Root(); }
                    public Builder With(Arg what, double value)
                    {
                        int i = (int)what;
                        System.Diagnostics.Debug.Assert(!IsKnown(ctx.args[i]));
                        ctx.args[i] = AsKnown(value);
                        return this;
                    }
                    public Builder With(Prm what, double value)
                    {
                        int i = (int)what;
                        System.Diagnostics.Debug.Assert(!IsKnown(ctx.values[i]) && ctx.funcs[i] == null);
                        ctx.Set(what, value);
                        return this;
                    }
                    public Builder With(Prm what, Func func)
                    {
                        System.Diagnostics.Debug.Assert(func != null);
                        int i = (int)what;
                        System.Diagnostics.Debug.Assert(!IsKnown(ctx.values[i]) && ctx.funcs[i] == null);
                        ctx.funcs[i] = func;
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

            #region Leaf context (only for overriden values) implementation
            public class Leaf : Context
            {
                public readonly Context parent;
                public readonly Root root;

                private Leaf(Context parent)
                {
                    System.Diagnostics.Debug.Assert(parent != null);
                    this.parent = parent;
                    if (parent is Root root)
                        this.root = root;
                    else if (parent is Leaf leaf)
                        this.root = leaf.root;
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

                public static Leaf NewWith(Context parent, Prm what, double value)
                {
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

        public static Context.Root.Builder NewCtx() => new Context.Root.Builder();

        public static Context.Leaf.Builder NewCtx(this Context parent) => new Context.Leaf.Builder(parent);

        public static Context NewWith(this Context parent, Prm what, double value) => Context.Leaf.NewWith(parent, what, value);
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
        public static double Kelv2Fahr(double T)
        {
            return 1.8 * T - 460.0;
        }
    }
}
