using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

using System.Threading;
using System.Threading.Tasks;
//using System.Linq;

using W.Common;

namespace W.Expressions
{
    public delegate object Macro(CallExpr expr, Generator.Ctx ctx);
    public delegate object Fx(object x);
    public delegate object Fxy(object x, object y);
    public delegate object Fn(IList args);
    public delegate Task<object> AsyncFn(AsyncExprCtx ae, IList args);

    public enum FuncKind { Other, Macro, Fx, Fxy, Fn, AsyncFn };

    /// <summary>
    /// Value may be of type LazySync or LazyAsync
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method)]
    public class CanBeLazyAttribute : Attribute { }

    /// <summary>
    /// Value may be vector with IList interface
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public class CanBeVectorAttribute : Attribute { }

    /// <summary>
    /// Attribute for function, that is not pure (nondeterministic or has side effects)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class IsNotPureAttribute : Attribute { }

    /// <summary>
    /// Declares arity for Fn or AsyncFn delegate
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ArityAttribute : Attribute
    {
        public readonly int minArity;
        public readonly int maxArity;
        public ArityAttribute(int minArity, int maxArity)
        {
            System.Diagnostics.Trace.Assert(0 <= minArity && minArity <= maxArity);
            this.minArity = minArity; this.maxArity = maxArity;
        }
    }

    /// <summary>
    /// Caching of function results is preferred
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CacheableAttribute : Attribute
    {
        public TimeSpan expiration;
        public string subdomain;
        public CacheableAttribute(float minutesInCache)
        { this.expiration = TimeSpan.FromMinutes(minutesInCache); }
    }

    public delegate IEnumerable<FuncDef> GetFuncs(string name, int arity);

    [Flags]
    public enum FuncFlags
    {
        None = 0,
        isNotPure = 1,
        resultCanBeLazy = 2,
        resultCanBeVector = 4,
        isLookupFunc = 8,
        Defaults = isNotPure | resultCanBeLazy | resultCanBeVector
    }

    public class FuncDef
    {
        static readonly IDictionary<string, object> emptyAttrs = new Dictionary<string, object>(0);
        public static readonly ValueInfo[] emptyInfo = new ValueInfo[0];
        public readonly Delegate func;
        public readonly string name;
        public readonly FuncKind kind;
        public readonly int minArity;
        public readonly int maxArity;
        public readonly FuncFlags flags;
        public readonly uint flagsArgCanBeLazy;
        public readonly uint flagsArgCanBeVector;
        public readonly TimeSpan cachingExpiration;
        public readonly string cacheSubdomain;
        public IDictionary<string, object> xtraAttrs = emptyAttrs;
        public readonly ValueInfo[] argsInfo = emptyInfo;
        public readonly ValueInfo[] resultsInfo = emptyInfo;

        public bool isNotPure => (flags & FuncFlags.isNotPure) != 0;
        public bool resultCanBeLazy => (flags & FuncFlags.resultCanBeLazy) != 0;
        public bool resultCanBeVector => (flags & FuncFlags.resultCanBeVector) != 0;
        public bool isLookupFunc => (flags & FuncFlags.isLookupFunc) != 0;

        public static FuncKind KindOf(object func)
        {
            if (func is Macro)
                return FuncKind.Macro;
            else if (func is Fx)
                return FuncKind.Fx;
            else if (func is Fxy)
                return FuncKind.Fxy;
            else if (func is Fn)
                return FuncKind.Fn;
            else if (func is AsyncFn)
                return FuncKind.AsyncFn;
            else return FuncKind.Other;
        }

        public FuncDef(Delegate func, string name,
            int minArity, int maxArity, ValueInfo[] argsInfo, ValueInfo[] resultsInfo,
            FuncFlags flags = FuncFlags.Defaults,
            uint flagsArgCanBeLazy = 0, uint flagsArgCanBeVector = 0,
            TimeSpan cachingExpiration = default(TimeSpan), string cacheSubdomain = null,
            IDictionary<string, object> xtraAttrs = null)
        {
            kind = KindOf(func);
            System.Diagnostics.Trace.Assert(kind != FuncKind.Other);
            this.func = func;
            this.name = name;
            this.minArity = minArity;
            this.maxArity = maxArity;
            this.argsInfo = argsInfo;
            this.resultsInfo = resultsInfo;
            this.flags = flags;
            this.flagsArgCanBeLazy = flagsArgCanBeLazy;
            this.flagsArgCanBeVector = flagsArgCanBeVector;
            this.cachingExpiration = cachingExpiration;
            this.cacheSubdomain = cacheSubdomain;
            this.xtraAttrs = xtraAttrs;
        }

        public FuncDef(AsyncFn func, int arity, string name) : this(func, arity, arity, name) { }

        public FuncDef(AsyncFn func, int minArity, int maxArity, string name)
        {
            this.func = func;
            kind = FuncKind.AsyncFn;
            this.minArity = minArity;
            this.maxArity = maxArity;
            this.flags = FuncFlags.Defaults;
            flagsArgCanBeLazy = flagsArgCanBeVector = 0xFFFFFFFF;
            this.name = name;
        }

        internal FuncDef(Delegate def, string name)
        {
            this.name = name;
            kind = KindOf(def);
            System.Diagnostics.Trace.Assert(kind != FuncKind.Other);
            func = def;
            MethodInfo method = def.Method;
            if (method.GetCustomAttributes(typeof(IsNotPureAttribute), true).Length > 0)
                flags |= FuncFlags.isNotPure;
            if (method.GetCustomAttributes(typeof(CanBeLazyAttribute), false).Length > 0)
                flags |= FuncFlags.resultCanBeLazy;
            if (method.GetCustomAttributes(typeof(CanBeVectorAttribute), false).Length > 0)
                flags |= FuncFlags.resultCanBeVector;
            {
                var attrs = method.GetCustomAttributes(typeof(CacheableAttribute), false);
                if (attrs.Length > 0)
                {
                    var ca = (CacheableAttribute)attrs[0];
                    cachingExpiration = ca.expiration;
                    cacheSubdomain = ca.subdomain;
                }
            }
            ParameterInfo[] prms = method.GetParameters();
            int n = prms.Length;
            System.Diagnostics.Trace.Assert(n < 32, "Functions with more than 32 arguments is not supported");
            flagsArgCanBeLazy = 0;
            flagsArgCanBeVector = 0;
            bool aritySpecified;

            if (kind == FuncKind.AsyncFn || kind == FuncKind.Macro || kind == FuncKind.Fn)
            {
                object[] tmp = method.GetCustomAttributes(typeof(ArityAttribute), false);
                if (tmp.Length > 0)
                {
                    ArityAttribute arity = (ArityAttribute)tmp[0];
                    minArity = arity.minArity;
                    maxArity = arity.maxArity;
                    aritySpecified = true;
                }
                else { minArity = 0; maxArity = int.MaxValue; aritySpecified = false; }
            }
            else { minArity = maxArity = n; aritySpecified = true; }

            #region Determine attributes of parameters

            var valueInfos = new Dictionary<int, ValueInfo>(n);
            int maxInfoIndex = -1;
            foreach (ArgumentInfoAttribute a in method.GetCustomAttributes(typeof(ArgumentInfoAttribute), false))
            {
                int i;
                if (a.index >= 0)
                {
                    i = a.index;
                    if (a.index > maxInfoIndex)
                        maxInfoIndex = a.index;
                }
                else
                {
                    maxInfoIndex++;
                    i = maxInfoIndex;
                }
                System.Diagnostics.Trace.Assert(i < maxArity || kind == FuncKind.Fx, "Number of ArgumentInfoAttribute is greater than number of parameters for '" + method.Name + "'");
                valueInfos.Add(i, a.info);
            }
            for (int i = 0; i < n; i++)
            {
                ParameterInfo pi = prms[i];
                if (pi.GetCustomAttributes(typeof(CanBeLazyAttribute), false).Length > 0)
                    flagsArgCanBeLazy |= (uint)(1 << i);
                if (pi.GetCustomAttributes(typeof(CanBeVectorAttribute), false).Length > 0)
                    flagsArgCanBeVector |= (uint)(1 << i);
                var infos = pi.GetCustomAttributes(typeof(ParameterInfoAttribute), false);
                if (infos.Length > 0)
                {
                    System.Diagnostics.Trace.Assert(infos.Length == 1, "More than one ParameterInfoAttribute specified for parameter '" + pi.Name + "'");
                    if (maxInfoIndex < i)
                        maxInfoIndex = i;
                    valueInfos.Add(i, ((ArgumentInfoAttribute)infos[0]).info);
                }
            }
            if (maxInfoIndex >= 0)
            {
                argsInfo = new ValueInfo[maxInfoIndex + 1];
                foreach (var vi in valueInfos) argsInfo[vi.Key] = vi.Value;
                if (!aritySpecified)
                    minArity = maxArity = maxInfoIndex + 1;
            }
            valueInfos.Clear();
            #endregion

            #region Determine attributes of result

            maxInfoIndex = -1;
            foreach (ResultInfoAttribute a in method.ReturnTypeCustomAttributes.GetCustomAttributes(typeof(ResultInfoAttribute), false))
            {
                int i = a.index;
                if (i > maxInfoIndex)
                    maxInfoIndex = i;
                valueInfos.Add(i, a.info);
            }
            if (maxInfoIndex >= 0)
            {
                resultsInfo = new ValueInfo[maxInfoIndex + 1];
                foreach (var vi in valueInfos) resultsInfo[vi.Key] = vi.Value;
            }
            valueInfos.Clear();
            #endregion
        }
        public override string ToString()
        {
            return name;
        }
    }

    public class FuncDefs : Dictionary<string, IList<FuncDef>>
    {
        Macro dummyFunc;
        FuncDef dummyDef;

        public Macro DummyFunc
        {
            get { return dummyFunc; }
            set { dummyFunc = value; if (value != null) dummyDef = new FuncDef(value, string.Empty); }
        }

        public void AddDef(string name, FuncDef fd)
        {
            IList<FuncDef> lst;
            if (!TryGetValue(name, out lst))
            { lst = new List<FuncDef>(); Add(name, lst); }
            lst.Add(fd);
        }

        public void AddDef(string name, Delegate func) { AddDef(name, new FuncDef(func, name)); }

        public void AddDef(Delegate func) { AddDef(func.Method.Name, func); }
        public void AddDef(string name, Fn func) { AddDef(name, (Delegate)func); }
        public void AddDef(string name, AsyncFn func) { AddDef(name, (Delegate)func); }

        public IEnumerable<FuncDef> GetFuncs(string name, int arity)
        {
            if (name == null)
            {
                foreach (var lst in Values)
                    foreach (var fd in lst)
                        yield return fd;
            }
            else
            {
                IList<FuncDef> lst;
                if (TryGetValue(name, out lst))
                    foreach (FuncDef fd in lst)
                        if (fd.minArity <= arity && arity <= fd.maxArity)
                            yield return fd;
                if (dummyFunc != null)
                    yield return dummyDef;
            }
        }

        public FuncDefs AddFrom(params Type[] types)
        {
            foreach (Type type in types)
            {
                // initialize type attributes:, e.g. physical quantities and units definitions
                if (type.GetCustomAttributes(typeof(W.Common.DefineUnitsAttribute), false) == null
                  & type.GetCustomAttributes(typeof(W.Common.DefineQuantitiesAttribute), false) == null
                  & type.GetCustomAttributes(false) == null
                )
                    throw new InvalidOperationException();    // never executed)
                foreach (MethodInfo mi in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    Type t = null;
                    var paramsInfo = mi.GetParameters();
                    var returnType = mi.ReturnType;
                    switch (paramsInfo.Length)
                    {
                        case 0:
                            if (returnType == typeof(IEnumerable<FuncDef>))
                                foreach (var fd in (IEnumerable<FuncDef>)mi.Invoke(null, null))
                                    AddDef(fd.name, fd);
                            break;
                        case 1:
                            if (returnType == typeof(object))
                            {
                                Type parType = paramsInfo[0].ParameterType;
                                if (parType == typeof(object))
                                    t = typeof(Fx);
                                else if (parType == typeof(IList))
                                    t = typeof(Fn);
                            }
                            break;
                        case 2:
                            {
                                var p0t = paramsInfo[0].ParameterType;
                                var p1t = paramsInfo[1].ParameterType;
                                if (returnType == typeof(Task<object>))
                                {
                                    if (p0t == typeof(AsyncExprCtx) && p1t == typeof(IList))
                                        t = typeof(AsyncFn);
                                }
                                else if (returnType == typeof(object))
                                {
                                    if (p0t == typeof(CallExpr) && p1t == typeof(Generator.Ctx))
                                        t = typeof(Macro);
                                    else if (p0t == typeof(object) && p1t == typeof(object))
                                        t = typeof(Fxy);
                                }
                            }
                            break;
                    }
                    Delegate def;
                    if (t != null)
                    {
                        def = Delegate.CreateDelegate(t, mi);
                        if (FuncDef.KindOf(def) != FuncKind.Other)
                            AddDef(def);
                    }
                }
            }
            return this;
        }
    }
}
