using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Diagnostics;

using System.Threading;
using System.Threading.Tasks;

namespace W.Expressions
{
    public delegate object LazySync();
    public delegate Task<object> LazyAsync(AsyncExprCtx ae);

    internal class CtxValue
    {
        static readonly W.Common.IAsyncLock errFlag = W.Common.Utils.NewAsyncLock();
        internal volatile W.Common.IAsyncLock sema = W.Common.Utils.NewAsyncLock();
        internal volatile object value;

        public CtxValue(object value) { this.value = value; }

        public CtxValue(object valueToCalc, AsyncExprCtx context)
        {
            this.value = (LazyAsync)delegate (AsyncExprCtx ae)
            {
                if (ae != context)
                    return OPs.ConstValueOf(context, valueToCalc);
                else return OPs.ConstValueOf(ae, valueToCalc);
            };
        }

        public CtxValue(AsyncExprCtx context, int index)
        {
            this.value = (LazyAsync)((AsyncExprCtx ae) => context.GetValue(ae, index));
        }

        [DebuggerHidden]
        public async Task<object> Get(AsyncExprCtx ae)
        {
            var s = sema;
            if (s != null)
            {
                if (s == errFlag)
                    throw (Exception)value;
                await s.WaitAsync(ae.Cancellation);
                try
                {
                    if (sema != null)
                    {   // enter here only once
                        if (sema == errFlag)
                            throw (Exception)value;
                        try
                        {
                            value = await OPs.VectorValueOf(ae, value);
                            sema = null;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            value = ex;
                            var tmp = sema; sema = errFlag; //tmp.Dispose();
                            throw;
                        }
                    }
                }
                finally { s.Release(); }
            }
            return value;
        }

        public override string ToString() { return Convert.ToString(value); }
    }

    public class AsyncExprCtx
    {
        protected readonly IDictionary<string, int> name2ndx;
        internal readonly CtxValue[] ctxValues;
        protected AsyncExprCtx parent;

        static readonly CtxValue[] EmptyCtxValues = new CtxValue[0];
        static readonly IDictionary<string, int> EmptyName2Ndx = new Dictionary<string, int>(0);

        public AsyncExprCtx()
        {
            parent = this;
            ctxValues = EmptyCtxValues;
            name2ndx = EmptyName2Ndx;
        }

        /// <summary>
        /// Nested (child) context with validation
        /// </summary>
        public AsyncExprCtx(Generator.Ctx ctx, IList values, AsyncExprCtx parent)
            : this(ctx.name2ndx, values)
        {
            Trace.Assert(parent != null);
            this.parent = parent;
            Trace.Assert(parent.name2ndx.Count == 0 || (ctx.parent != null && parent.name2ndx == ctx.parent.name2ndx));
        }

        /// <summary>
        /// Nested (child) context
        /// </summary>
        /// <param name="name2ndx"></param>
        /// <param name="values"></param>
        /// <param name="parent"></param>
        public AsyncExprCtx(IDictionary<string, int> name2ndx, IList values, AsyncExprCtx parent)
            : this(name2ndx, values)
        {
            Trace.Assert(parent != null);
            this.parent = parent;
        }

        /// <summary>
        /// New independent context
        /// </summary>
        /// <param name="name2ndx">names index</param>
        /// <param name="values">values list</param>
        protected AsyncExprCtx(IDictionary<string, int> name2ndx, IList values)
        {
            this.name2ndx = name2ndx;
            int n = values.Count;
            ctxValues = new CtxValue[n];
            for (int i = 0; i < n; i++)
                ctxValues[i] = new CtxValue(values[i]);
        }

        /// <summary>
        /// Context for next loop iteration
        /// </summary>
        /// <param name="prevIterationContext"></param>
        /// <param name="nextIterationValues"></param>
        /// <param name="ndxOfValsToCalc"></param>
        public AsyncExprCtx(AsyncExprCtx prevIterationContext, IList nextIterationValues, IEnumerable<int> ndxOfValsToCalc)
        {
            int n = nextIterationValues.Count;
            name2ndx = prevIterationContext.name2ndx;
            parent = prevIterationContext.parent;
            Trace.Assert(parent != null);
            ctxValues = new CtxValue[n];
            for (int i = 0; i < n; i++)
                ctxValues[i] = prevIterationContext.ctxValues[i];
            foreach (int i in ndxOfValsToCalc)
                if (i < 0)
                    ctxValues[~i] = new CtxValue(nextIterationValues[~i], prevIterationContext);
                else ctxValues[i] = new CtxValue(nextIterationValues[i]);
        }

        /// <summary>
        /// Function call context
        /// </summary>
        public AsyncExprCtx(Generator.Ctx funcCtx, AsyncExprCtx callerCtx, IList args, int nArgs)
        {
            if (funcCtx.parent != null)
                Trace.Assert(funcCtx.parent.name2ndx == callerCtx.name2ndx);
            else
                Trace.Assert(funcCtx.values.Count == nArgs);
            this.name2ndx = funcCtx.name2ndx;
            var values = funcCtx.values;
            int n = values.Count;
            ctxValues = new CtxValue[n];
            //int nArgs = args.Count;
            for (int i = 0; i < nArgs; i++)
                ctxValues[i] = new CtxValue(args[i], callerCtx);
            for (int i = nArgs; i < n; i++)
                ctxValues[i] = new CtxValue(values[i]);
            parent = callerCtx;
        }

        /// <summary>
        /// Function call context (with closures)
        /// </summary>
        /// <param name="name2ndx">Value name -> index</param>
        /// <param name="values">Values array</param>
        /// <param name="callerCtx">Calling context</param>
        /// <param name="args">Arguments array</param>
        /// <param name="declCtx">Context in which function is declared (for closures)</param>
        /// <param name="valuesNdx">Descriptor of function context values: 
        ///     -(k+1) = value from caller context;
        ///          0 = local value (from current context);
        ///     +(k+1) = value from declaration context (closures emulation).
        /// </param>
        public AsyncExprCtx(IDictionary<string, int> name2ndx, IList values, IList args,
            AsyncExprCtx callerCtx, AsyncExprCtx declCtx,
            IList<int> valuesNdx)
        {
            this.name2ndx = name2ndx;
            int n = values.Count;
            ctxValues = new CtxValue[n];
            for (int i = 0; i < valuesNdx.Count; i++)
            {
                int j = valuesNdx[i];
                if (j < 0)
                    ctxValues[i] = new CtxValue(args[~j], callerCtx);
                else if (j > 0)
                    ctxValues[i] = new CtxValue(declCtx, j - 1);
                else ctxValues[i] = new CtxValue(values[i]);
            }
            parent = callerCtx;
        }

        public Task<object> GetValue(string valueName)
        {
            int i;
            if (name2ndx.TryGetValue(valueName, out i))
                return GetValue(i);
            else if (parent != this)
                return parent.GetValue(valueName);
            else throw new KeyNotFoundException("Value named '" + valueName + "' is not found");
        }

        [DebuggerHidden]
        public Task<object> GetValue(AsyncExprCtx ae, int ndx)
        {
            if (ndx >= Generator.Ctx.ctxDepthStep)
                return parent.GetValue(ae, ndx - Generator.Ctx.ctxDepthStep);
            else return ctxValues[ndx].Get(this); //aec
        }

        [DebuggerHidden]
        public Task<object> GetValue(int ndx)
        {
            if (ndx >= Generator.Ctx.ctxDepthStep)
                return parent.GetValue(ndx - Generator.Ctx.ctxDepthStep);
            else return ctxValues[ndx].Get(this);
        }

        public virtual W.Common.IAsyncSemaphore NewContextSemaphore { get { return parent.NewContextSemaphore; } }
        public virtual CancellationToken Cancellation { get { return parent.Cancellation; } }

#if DEBUG
        public Dictionary<string, object> DbgDict
        {
            get
            {
                var dict = new Dictionary<string, object>(name2ndx.Count);
                foreach (var p in name2ndx)
                    dict.Add(p.Key, (p.Value < ctxValues.Length) ? ctxValues[p.Value] : null);
                return dict;
            }
        }
#endif
    }

    public class AsyncExprRootCtx : AsyncExprCtx
    {
        readonly W.Common.IAsyncSemaphore semaNewContext;
        public CancellationToken cancellationToken = CancellationToken.None;

        public AsyncExprRootCtx(IDictionary<string, int> name2ndx, IList values, W.Common.IAsyncSemaphore semaNewContext)
            : base(name2ndx, values)
        {
            parent = this;
            this.semaNewContext = semaNewContext;
        }

        public override W.Common.IAsyncSemaphore NewContextSemaphore { get { return semaNewContext; } }
        public override CancellationToken Cancellation { get { return cancellationToken; } }
    }

    [Flags]
    public enum ValueKind { Const = 0, Sync = 1, Async = 2, Undef = 3, MaxValue = 3 };

    public static class Generator
    {
        public class Exception : System.Exception { public Exception(string msg) : base(msg) { } }

        public class CantGetConstException : Exception { public CantGetConstException(string msg) : base(msg) { } }

        public class FunctionNotFoundException : Exception { public FunctionNotFoundException(string msg) : base(msg) { } }

        public class UnknownValuesException : Exception
        {
            public IEnumerable<String> Names { get { return _names; } }

            public UnknownValuesException(string message, IEnumerable<String> names) : base(message)
            { _names = names.ToList(); }

            public UnknownValuesException(string message, params string[] names) : base(message)
            { _names = names; }

            private readonly IEnumerable<String> _names;
        }

        public static readonly object Undefined = new Exception(nameof(Undefined));
        public static readonly object LazyDummy = (LazyAsync)((AsyncExprCtx ae) => throw new NotSupportedException(nameof(LazyDummy)));

        public class Ctx
        {
            public static readonly object LookupNewValueAdded = new object();
            public static readonly object LookupValueNotFound = new object();

            public readonly IDictionary<string, int> name2ndx = new Dictionary<string, int>();
            public readonly IList values = new ArrayList();
            public readonly Func<Ctx, string, object> externalValueLookup;
            public readonly Ctx parent;

            public Ctx(Ctx parent)
            {
                this.parent = parent;
                this.externalFuncs = parent.externalFuncs;
                this.externalValueLookup = parent.externalValueLookup;
            }

            public Ctx(IDictionary<string, object> predefinedValues, GetFuncs getFunc, Func<Ctx, string, object> externalValueLookup = null)
            {
                if (predefinedValues != null)
                    foreach (var pair in predefinedValues)
                        values[GetOrCreateIndexOf(pair.Key)] = pair.Value;
                externalFuncs = new List<GetFuncs>();
                externalFuncs.Add(getFunc);
                this.externalValueLookup = externalValueLookup;
            }

            public IEnumerable<FuncDef> GetFunc(string name, int arity)
            {
                if (name == null)
                {
                    var ctx = this;
                    while (ctx != null)
                    {
                        foreach (var v in ctx.values)
                        {
                            var fd = v as FuncDef;
                            if (fd != null)
                                yield return fd;
                        }
                        ctx = ctx.parent;
                    }
                }
                else
                {
                    int i = IndexOf(name);
                    if (i >= 0)
                    {
                        var v = this[i];
                        if (v is FuncDef)
                        {
                            var def = (FuncDef)v;
                            if (def.minArity <= arity && arity <= def.maxArity)
                                yield return def;
                        }
                        else if (v is LazyAsync)
                        {
                            AsyncFn f =
                                (ae, args) =>
                                {
                                    return OPs._call_func(ae, i, args);
                                };
                            yield return new FuncDef(f, 0, int.MaxValue, name);
                        }
                    }
                }
                for (int i = externalFuncs.Count - 1; i >= 0; i--)
                    foreach (var fd in externalFuncs[i](name, arity))
                        yield return fd;
            }

            public const int ctxDepthStep = 0x01000000;

            public int IndexOf(string name, int nDepth = 16)
            {
                if (nDepth < 0)
                    Error("Max contexts recursion depth exceeded!");
                int ndx;
                if (name2ndx.TryGetValue(name, out ndx))
                    return ndx;
                else if (parent != null)
                    return parent.IndexOf(name, nDepth - 1) + ctxDepthStep;
                else return int.MinValue;
            }

            public int CreateValue(string name, object value)
            {
                int ndx = values.Count;
                name2ndx.Add(name, ndx);
                values.Add(value);
                return ndx;
            }

            public int CreateValue(string name)
            { return CreateValue(name, Undefined); }

            public int RootIndexOf(string name)
            {
                if (parent == null)
                    return IndexOf(name);
                else return parent.RootIndexOf(name) + ctxDepthStep;
            }

            public int RootCreateValue(string name)
            {
                if (parent == null)
                    return CreateValue(name);
                else return parent.RootCreateValue(name) + ctxDepthStep;
            }

            public int GetOrCreateIndexOf(string name)
            {
                int ndx = IndexOf(name);
                if (ndx >= 0)
                    return ndx;
                ndx = values.Count;
                values.Add(Undefined);
                name2ndx[name] = ndx;
                return ndx;
            }

            public object this[int ndx]
            {
                get
                {
                    if (ndx >= ctxDepthStep)
                        return parent[ndx - ctxDepthStep];
                    else return values[ndx];
                }
                set
                {
                    if (ndx >= ctxDepthStep)
                        parent[ndx - ctxDepthStep] = value;
                    else values[ndx] = value;
                }
            }

            public object this[string name]
            {
                get
                {
                    int i = IndexOf(name);
                    if (i < 0 || i >= ctxDepthStep)
                        return Undefined;
                    return this[i];
                }
                set
                {
                    int i = IndexOf(name);
                    if (i < 0 || i >= ctxDepthStep)
                        throw new Exception($"No value named '{name}' found in context");
                    values[i] = value;
                }
            }

            public IEnumerable<string> NamesOfUndefinedValues()
            {
                foreach (KeyValuePair<string, int> pair in name2ndx)
                    if (object.ReferenceEquals(values[pair.Value], Undefined))
                        yield return pair.Key;
            }

            [DebuggerHidden]
            public void CheckUndefinedValues()
            {
                string[] lst = new List<string>(NamesOfUndefinedValues()).ToArray();
                if (lst.Length > 0)
                    Error("Some values not defined: " + string.Join(", ", lst));
            }

            public void UseFuncs(GetFuncs funcs)
            {
                externalFuncs.Add(funcs);
            }

            List<GetFuncs> externalFuncs;

#if DEBUG
            public Dictionary<string, object> DbgDict
            { get { return new Dictionary<string, object>(W.Common.ValuesDictionary.New(values.ToArray(), name2ndx)); } }
#endif
            [DebuggerHidden]
            public object Error(string msg)
            {
                if (IndexOf(FuncDefs_Core.stateTryGetConst) < 0)
                    throw new Exception(msg);
                else
                    throw new CantGetConstException(msg);
            }

            static readonly Type[] arrStringTypes = new Type[1] { typeof(string) };

            [DebuggerHidden]
            public object Error(string msg, Type exceptionType)
            {
                if (IndexOf(FuncDefs_Core.stateTryGetConst) < 0)
                {
                    ConstructorInfo ctor = exceptionType.GetConstructor(arrStringTypes);
                    var ex = (System.Exception)ctor.Invoke(new object[] { msg });
                    throw ex;
                }
                else throw new CantGetConstException(msg);
            }

            public override string ToString() { return string.Join(",", name2ndx.Keys); }
        }

        public static object Generate(Expr src, Ctx ctx)
        {
            BinaryExpr be = src as BinaryExpr;
            if (be != null)
            {
                Fxy op;
                switch (be.nodeType)
                {
                    case ExprType.Power: op = OPs.t_POW; break;
                    case ExprType.Multiply: op = OPs.t_MUL; break;
                    case ExprType.Divide: op = OPs.t_DIV; break;
                    case ExprType.Add: op = OPs.t_PLUS; break;
                    case ExprType.Subtract: op = OPs.t_MINUS; break;
                    case ExprType.Concat: op = OPs.t_CONCAT; break;
                    case ExprType.Equal: op = OPs.t_EQ; break;
                    case ExprType.NotEqual: op = OPs.t_NE; break;
                    case ExprType.LessThan: op = OPs.t_LT; break;
                    case ExprType.GreaterThan: op = OPs.t_GT; break;
                    case ExprType.LessThanOrEqual: op = OPs.t_LE; break;
                    case ExprType.GreaterThanOrEqual: op = OPs.t_GE; break;
                    case ExprType.Fluent:
                    case ExprType.Fluent2:
                    case ExprType.FluentNullCond:
                        {
                            if (be.right is CallExpr)
                            {
                                CallExpr ce = (CallExpr)be.right;
                                List<Expr> args = new List<Expr>(ce.args.Count + 1);
                                if (be.nodeType == ExprType.Fluent2)
                                {
                                    if (ce.args.Count == 0)
                                        ctx.Error("Expected function call with at least 1 argument");
                                    args.Add(ce.args[0]);
                                    args.Add(be.left);
                                    for (int i = 1; i < ce.args.Count; i++) args.Add(ce.args[i]);
                                }
                                else //if (be.nodeType == ExprType.Fluent || be.nodeType == ExprType.FluentNullCond)
                                {
                                    args.Add(be.left);
                                    args.AddRange(ce.args);
                                }
                                Expr valueExpr = new CallExpr(ce, args);

                                if (be.nodeType == ExprType.FluentNullCond)
                                    valueExpr = new CallExpr(FuncDefs_Core.IF,
                                        new BinaryExpr(ExprType.Equal, be.left, ConstExpr.Null),
                                        ConstExpr.Null,
                                        valueExpr
                                    );
                                return Generate(valueExpr, ctx);
                            }
                            else return ctx.Error("At right side of fluent operator (. or ..) must be a function call");
                        }
                    default: throw new InvalidOperationException();
                }
                object a = Generate(be.left, ctx);
                object b = Generate(be.right, ctx);
                return OPs.GenFxyCall(op, a, b, OPs.MaxKindOf(a, b), false);
            }
            switch (src.nodeType)
            {
                case ExprType.Constant:
                    return ((ConstExpr)src).value;
                case ExprType.Negate:
                    return Generate(new BinaryExpr(ExprType.Subtract, new ConstExpr(0), ((UnaryExpr)src).operand), ctx);
                case ExprType.Call:
                    {
                        CallExpr ce = (CallExpr)src;
                        if (ce.funcName == string.Empty) // syntactic sugar "(X0,...,Xn) = _Eval(X0,...,Xn)
                            return Generate(new CallExpr(FuncDefs_Core._Eval, ce.args), ctx);
                        int nArgs = ce.args.Count;
                        if (ce.funcDef != null)
                            return GenerateCall(ce.funcDef, ce, ctx);
                        else
                            foreach (FuncDef fd in ctx.GetFunc(ce.funcName, nArgs))
                                return GenerateCall(fd, ce, ctx);
                        return ctx.Error(string.Format("{0}[{1}]", ce.funcName, nArgs), typeof(FunctionNotFoundException));
                    }
                case ExprType.NewArrayInit:
                    {
                        ArrayExpr ar = (ArrayExpr)src;
                        var items = new OPs.ListOfVals(ar.args.Count);
                        int maxKind = 0;
                        foreach (Expr expr in ar.args)
                        {
                            object item = Generate(expr, ctx);
                            items.Add(item);
                            int kind = (int)OPs.KindOf(item);
                            if (kind > maxKind) maxKind = kind;
                        }
                        switch ((ValueKind)maxKind)
                        {
                            case ValueKind.Const:
                                return items.ToArray();
                            case ValueKind.Sync:
                                return new OPs.ListOfSync(items);
                            case ValueKind.Async:
                                return new OPs.ListOfVals(items);
                        }
                    }
                    break;
                case ExprType.Reference:
                    {
                        ReferenceExpr re = (ReferenceExpr)src;
                        return FuncDefs_Core.GV(new CallExpr(FuncDefs_Core.GV, new ConstExpr(re.name)), ctx);
                    }
                case ExprType.Index:
                case ExprType.IndexNullCond:
                    {
                        IndexExpr ie = (IndexExpr)src;
                        var ndx = ie.index as ConstExpr;
                        //var arr = Generate(ie.value, ctx);
                        CallExpr valueExpr;
                        if ((ndx != null && ndx.value is string))
                            valueExpr = new CallExpr(FuncDefs_Core.AtKey, ie.value, ie.index);
                        else
                            valueExpr = new CallExpr(FuncDefs_Core.GV, ie.value, ie.index);
                        if (src.nodeType == ExprType.IndexNullCond)
                            valueExpr = new CallExpr(FuncDefs_Core.IF,
                                new BinaryExpr(ExprType.Equal, ie.value, ConstExpr.Null),
                                ConstExpr.Null,
                                valueExpr
                            );
                        return Generate(valueExpr, ctx);
                    }
            }
            throw new InvalidOperationException();
        }

        internal static object GenerateCall(FuncDef fd, CallExpr ce, Ctx ctx)
        {
            int maxArgKind = 0;
            IList args = null;
            if (fd.kind != FuncKind.Macro && args == null)
            {
                args = new OPs.ListOfVals(ce.args.Count);
                maxArgKind = 0;
                foreach (Expr expr in ce.args)
                {
                    object item = Generate(expr, ctx);
                    args.Add(item);
                    int kind = (int)OPs.KindOf(item);
                    if (maxArgKind < kind) maxArgKind = kind;
                }
            }
            switch (fd.kind)
            {
                case FuncKind.Fx:
                    return OPs.FxCall.Gen(fd, args[0], ce);
                case FuncKind.Fxy:
                    return OPs.GenFxyCall(fd, args[0], args[1]);
                case FuncKind.Macro:
                    {
                        var res = ((Macro)fd.func)(ce, ctx);
                        var funcDefs = res as IEnumerable<FuncDef>;
                        if (funcDefs != null)
                        {
                            var fds = new FuncDefs();
                            int n = 0;
                            foreach (FuncDef item in funcDefs)
                            {
                                fds.AddDef(item.name, item);
                                n++;
                            }
                            ctx.UseFuncs(fds.GetFuncs);
                            return string.Format("Added {0} func(s) by {1}", n, ce);
                        }
                        return res;
                    }
                case FuncKind.AsyncFn:
                    AsyncFn fn = (AsyncFn)fd.func;
                    return (LazyAsync)delegate (AsyncExprCtx ae) { return fn(ae, args); };
                case FuncKind.Fn:
                    return OPs.GenFnCall(fd, args, (ValueKind)maxArgKind, ce);
            }
            return ctx.Error("GenerateCall: unsupported FuncKind value");
        }
    }
}
