using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Caching;

namespace W.Expressions
{
    using System.Diagnostics;
    using System.IO;
    using W.Common;

    public static class FuncDefs_Core
    {
        //public const int AsyncWaitTimeoutMs = 5 * 60 * 1000;

        [Arity(3, 3)]
        public static object IF(CallExpr ce, Generator.Ctx ctx)
        {
            int n = ce.args.Count;
            System.Diagnostics.Trace.Assert(n == 3);

            object cond = Generator.Generate(ce.args[0], ctx);
            ValueKind condKind = OPs.KindOf(cond);
            if (condKind == ValueKind.Const && !(cond is IList))
                return OPs.xl2bool(cond) ? Generator.Generate(ce.args[1], ctx) : (n < 3) ? false : Generator.Generate(ce.args[2], ctx);

            object branchThen = Generator.Generate(ce.args[1], ctx);
            object branchElse = (n < 3) ? false : Generator.Generate(ce.args[2], ctx);
            ValueKind branchKind = (ValueKind)Math.Max((int)OPs.KindOf(branchThen), (int)OPs.KindOf(branchElse));
            switch ((ValueKind)Math.Max((int)condKind, (int)branchKind))
            {
                case ValueKind.Const:
                    return IF_Sync(cond, branchThen, branchElse);
                case ValueKind.Sync:
                    return (LazySync)delegate () { return IF_Sync(cond, branchThen, branchElse); };
                case ValueKind.Async:
                    return (LazyAsync)delegate (AsyncExprCtx ae)
                    { return IF_Async(ae, cond, branchThen, branchElse); };
            }
            throw new InvalidOperationException("IF: Unexpected kind of data");
        }

        static object IF_Scalar(object cond, object branchThen, object branchElse)
        { return OPs.xl2bool(cond) ? branchThen : branchElse; }

        static object IF_Sync(object lazyCond, object lazyBranchThen, object lazyBranchElse)
        {
            object cond = OPs.ConstValueOf(lazyCond);
            IList lstCond = cond as IList;
            if (lstCond == null)
                return IF_Scalar(cond, lazyBranchThen, lazyBranchElse);
            IList lstThen = null, lstElse = null;
            int n = lstCond.Count;
            OPs.ListOfVals res = new OPs.ListOfVals(n);
            for (int j = 0; j < n; j++)
                try
                {
                    if (OPs.xl2bool(lstCond[j]))
                    {
                        if (lstThen == null)
                            lstThen = (IList)OPs.VectorValueOf(lazyBranchThen);
                        res.Add(lstThen[j]);
                    }
                    else
                    {
                        if (lstElse == null)
                            lstElse = (IList)OPs.VectorValueOf(lazyBranchElse);
                        res.Add(lstElse[j]);
                    }
                }
                catch (Exception ex) { res.Add(ex.Wrap()); }
            return res;
        }

        static async Task<object> IF_Async(AsyncExprCtx ctx,
            object lazyCond, object lazyBranchThen, object lazyBranchElse)
        {
            object cond = await OPs.ConstValueOf(ctx, lazyCond);
            IList lstCond = cond as IList;
            if (lstCond == null)
                return IF_Scalar(cond, lazyBranchThen, lazyBranchElse);
            bool calcThen = true, calcElse = true;
            IList lstThen = null, lstElse = null;
            object valThen = null, valElse = null;
            int n = lstCond.Count;
            OPs.ListOfVals res = new OPs.ListOfVals(n);
            for (int j = 0; j < n; j++)
                try
                {
                    bool flag = Convert.ToBoolean(lstCond[j]);
                    if (flag)
                    {
                        if (calcThen)
                        {
                            calcThen = false;
                            var r = await OPs.VectorValueOf(ctx, lazyBranchThen);
                            lstThen = r as IList;
                            if (lstThen == null) valThen = r;
                        }
                        if (lstThen != null)
                            res.Add(lstThen[j]);
                        else //if (valThen != null)
                            res.Add(valThen);
                    }
                    else
                    {
                        if (calcElse)
                        {
                            calcElse = false;
                            var r = await OPs.VectorValueOf(ctx, lazyBranchElse);
                            lstElse = r as IList;
                            if (lstElse == null) valElse = r;
                        }
                        if (lstElse != null)
                            res.Add(lstElse[j]);
                        else //if (valElse != null)
                            res.Add(valElse);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { res.Add(ex.Wrap()); }
            return res;
        }

        [Arity(1, 2)]
        public static object let(CallExpr ce, Generator.Ctx ctx)
        {
            var a0 = ce.args[0];
            if (a0.nodeType == ExprType.Reference)
            {
                var args = new Expr[ce.args.Count];
                ce.args.CopyTo(args, 0);
                args[0] = new ConstExpr((a0 as ReferenceExpr).name);
                return SetValueImpl(new CallExpr(ce.funcName, args), ctx, true);
            }
            return SetValueImpl(ce, ctx, true);
        }

        public const string optionCacheSubdomain = "#optionCacheSubdomain";
        public const string optionCacheExpirationMinutes = "#optionCacheExpirationMinutes";
        public const string optionAllowRedefinitions = "#optionAllowRedefinitions";

        static object SetValueImpl(CallExpr ce, Generator.Ctx ctx, bool valueAsResult = true)
        {
            Expr arg0 = ce.args[0];
            object varName;
            varName = Generator.Generate(arg0, ctx);
            if (OPs.KindOf(varName) == ValueKind.Const)
            {
                object value = (ce.args.Count > 1) ? Generator.Generate(ce.args[1], ctx) : null;
                string name = Convert.ToString(varName);
                int ndx = ctx.IndexOf(name);
                if (ndx < 0 || Generator.Ctx.ctxDepthStep <= ndx)
                    ndx = ctx.CreateValue(name);
                else if (ctx[ndx] != Generator.Undefined)
                {
                    if (ctx.IndexOf(optionAllowRedefinitions) < 0)
                        ctx.Error("Duplicated definition of \"" + name + "\" in " + ce.ToString());
                    ndx = ctx.CreateValue(name); // redefinition
                }
                ctx[ndx] = value;
                return valueAsResult ? GV1(ndx, ctx, name) : name;
            }
            return ctx.Error("Constant value expected: SV(" + arg0 + ", ...)");
        }

        [Arity(1, 2)]
        public static object SV(CallExpr ce, Generator.Ctx ctx)
        { return SetValueImpl(ce, ctx); }

        [Arity(1, 2)]
        public static object SSV(CallExpr ce, Generator.Ctx ctx)
        {
            Expr arg0 = ce.args[0];
            var vn = arg0 as ReferenceExpr;
            object varName;
            //if (vn != null)
            //    varName = vn.name;
            //else 
            varName = Generator.Generate(arg0, ctx);
            if (OPs.KindOf(varName) == ValueKind.Const)
            {
                string name = Convert.ToString(varName);
                int ndx = ctx.RootIndexOf(name);
                if (ndx < 0)
                    ndx = ctx.RootCreateValue(name);
                else if (ctx[ndx] != Generator.Undefined)
                {
                    if (ctx.IndexOf(optionAllowRedefinitions) < 0)
                        ctx.Error("Duplicated definition of \"" + name + "\" in " + ce.ToString());
                    else ndx = ctx.RootCreateValue(name); // redefinition
                }
                if (ce.args.Count > 1)
                    ctx[ndx] = Generator.Generate(ce.args[1], ctx);
                else ctx[ndx] = null;
                return GV1(ndx, ctx, name);
            }
            return ctx.Error("Constant value expected: SSV(" + arg0 + ", ...)");
        }

        public const string optionCachingDomainName = "#optionCachingDomainName";
        public const string optionCachingInfoParam = "#optionCachingInfoParam";
        public const string optionAllowUndefinedRootValues = "#optionAllowUndefinedRootValues";

        public const string optionMinPartSize = "#optionMinPartSize";
        public const string optionMaxPartSize = "#optionMaxPartSize";
        public const string optionPreferredMinPartsCount = "#optionPreferredMinPartsCount";

        [Arity(1, 2)]
        public static object DEFINED(CallExpr ce, Generator.Ctx ctx)
        {
            var arg = ce.args[0];
            string name = OPs.TryAsName(arg, ctx);
            if (name == null)
                ctx.Error("DEFINED: Constant value expected ('" + Convert.ToString(arg) + "')");
            int level = 0;
            if (ce.args.Count > 1)
                level = Convert.ToInt32(Generator.Generate(ce.args[1], ctx));
            int i = ctx.IndexOf(name);
            return (i / Generator.Ctx.ctxDepthStep >= level) ? 1 : 0;
        }

        [Arity(2, 2)]
        public static object EQUALEXPR(CallExpr ce, Generator.Ctx ctx)
        {
            var arg0 = ce.args[0].ToString();
            var arg1 = ce.args[1].ToString();
            return (arg0 == arg1) ? 1 : 0;
        }

        public static Expr GetCachingDomainExpr(object objIDs, DateTime timeA, DateTime timeB = default(DateTime))
        {
            var ise = objIDs as System.Collections.IStructuralEquatable;
            var wellsHash = (ise != null) ? ise.GetHashCode(System.Collections.StructuralComparisons.StructuralEqualityComparer) : objIDs.GetHashCode();
            var s = (timeB != default(DateTime))
                ? string.Format("{0}@{1}_{2}", wellsHash, timeA, timeB)
                : string.Format("{0}@{1}_", wellsHash, timeA);
            return new ConstExpr(s);
        }

        public static object GetSingleDate([CanBeVector]object arg)
        {
            var lst = arg as IList;
            double dtRes;
            if (lst != null)
            {
                dtRes = 0;
                foreach (var item in lst)
                {
                    var dt = Convert.ToDouble(item);
                    if (dtRes == dt)
                        continue;
                    if (dtRes == 0)
                        dtRes = dt;
                    else throw new Generator.Exception("GetSingleDate: Multiple dates is not supported");
                }
            }
            else dtRes = Convert.ToDouble(arg);
            return dtRes;
        }

        [Arity(1, 3)]
        public static object GetCachingDomainExpr(IList args)
        {
            var objIDs = Utils.Cast<object>(args[0]);
            var timeA = (args.Count > 1) ? DateTime.FromOADate(Convert.ToDouble(args[1])) : DateTime.MinValue;
            var timeB = (args.Count > 2) ? DateTime.FromOADate(Convert.ToDouble(args[2])) : DateTime.MaxValue;
            return GetCachingDomainExpr(objIDs, timeA, timeB);
        }

        [Arity(1, 2)]
        public static object GSV(CallExpr ce, Generator.Ctx ctx)
        {
            string name = null;
            var arg = ce.args[0];
            var vn = arg as ReferenceExpr;
            if (vn != null)
                name = vn.name;
            else
            {
                var tmp = Generator.Generate(arg, ctx);
                if (OPs.KindOf(tmp) != ValueKind.Const)
                    ctx.Error("GSV: Constant value expected ('" + Convert.ToString(arg) + "')");
                name = Convert.ToString(tmp);
            }
            int i = ctx.RootIndexOf(name);
            if (i < 0)
            {
                if (ce.args.Count == 2)
                    return Generator.Generate(new CallExpr(nameof(FuncDefs_Core.SSV), new ConstExpr(name), ce.args[1]), ctx);
                else if (ctx.IndexOf(optionAllowUndefinedRootValues) >= 0)
                    i = ctx.RootCreateValue(name);
                else ctx.Error("GSV: Value named '" + name + "' is not defined at root level");
            }
            //if (i < Generator.Ctx.ctxDepthStep && ctx.parent != null)
            //    ctx.Error("GSV: Value named '" + name + "' must be defined at higher level");
            return FuncDefs_Core.GV1(i, ctx, name);
        }


        [Arity(1, 2)]
        public static object GV(CallExpr ce, Generator.Ctx ctx)
        {
            if (ce.args.Count == 1)
                return GV1(ce, ctx);
            else return GV2(ce, ctx);
        }

        #region GV[1]

        [Arity(1, 1)]
        static object GV1(CallExpr ce, Generator.Ctx ctx)
        {
            object varName = Generator.Generate(ce.args[0], ctx);
            ValueKind varNameKind = OPs.KindOf(varName);
            switch (varNameKind)
            {
                case ValueKind.Const:
                    var name = Convert.ToString(varName);
                    var i = ctx.IndexOf(name);
                    if (i < 0)
                        if (ctx.externalValueLookup != null)
                        {
                            var v = ctx.externalValueLookup(ctx, name);
                            if (v == Generator.Ctx.LookupNewValueAdded)
                                i = ctx.IndexOf(name);
                            else if (v == Generator.Ctx.LookupValueNotFound)
                                i = ctx.CreateValue(name);
                            else return v;
                        }
                        else i = ctx.CreateValue(name);
                    return GV1(i, ctx, name);
                    //default:
                    //	return (LazyAsync)delegate(AsyncExprCtx ae) { return GV1_Async(ae, varName); };
            }
            return ctx.Error("GV[1]: Unexpected kind of data");
        }

        static object GV1(int ndx, Generator.Ctx ctx, string name)
        {
            object value = ctx[ndx];
            if (value != null && OPs.KindOf(value) == ValueKind.Const)
                return value;
            else
#if DEBUG
                return (LazyAsync)new GV1(ndx, name).Async;
#else
                return (LazyAsync)new GV1(ndx).Async;
#endif
        }

        #endregion

        /// <summary>
        /// values[key], where values=args[0], key=args[1]
        /// </summary>
        /// <param name="args">0: a)list | b)dicts | c)dict;  1: a)index | b,c)key</param>
        /// <returns>a) list[index]; b) list of values with key from dicts; c) dict[key]</returns>
        [Arity(2, 2)]
        public static object AtKey(IList args)
        {
            var dict = args[0];
            var key = args[1];
            string[] keys;
            var lstKeys = key as IList;
            if (lstKeys != null)
            {
                keys = new string[lstKeys.Count];
                for (int i = 0; i < keys.Length; i++) keys[i] = Convert.ToString(lstKeys[i]);
            }
            else keys = null;

            var lst = dict as IList;
            if (lst != null)
            {
                var res = new object[lst.Count];
                if (keys != null)
                {
                    for (int i = 0; i < lst.Count; i++)
                    {
                        var di = (IDictionary<string, object>)lst[i];
                        var arr = new object[keys.Length];
                        for (int j = 0; j < keys.Length; j++)
                            arr[j] = di[keys[j]];
                        res[i] = arr;
                    }
                }
                else
                {
                    var k = Convert.ToString(key);
                    for (int i = 0; i < lst.Count; i++)
                    {
                        var di = (IDictionary<string, object>)lst[i];
                        res[i] = di[k];
                    }
                }
                return res;
            }

            var d = (IDictionary<string, object>)dict;
            if (keys != null)
            {
                var arr = new object[keys.Length];
                for (int j = 0; j < keys.Length; j++)
                    arr[j] = d[keys[j]];
                return arr;
            }
            d.TryGetValue(Convert.ToString(key), out var val);
            return val;
        }

        /// <summary>
        /// list[index]
        /// </summary>
        /// <param name="args">0: IList; 1: int</param>
        /// <returns>value at index from list</returns>
        [Arity(2, 2)]
        public static object AtNdx(IList args)
        {
            var list = args[0];
            var index = args[1];
            return ((IList)list)[Convert.ToInt32(index)];
        }

        #region GV[2]

        [Arity(2, 2)]
        static object GV2(CallExpr ce, Generator.Ctx ctx)
        {
            var obj = Generator.Generate(ce.args[0], ctx);
            var ndx = Generator.Generate(ce.args[1], ctx);
            switch (OPs.MaxKindOf(obj, ndx))
            {
                case ValueKind.Const:
                    return GV2(obj, ndx);
                //case ValueKind.Sync:
                //	return GV2(OPs.VectorValueOf(obj), OPs.ConstValueOf(ndx));
                //case ValueKind.Async:
                default:
                    return (LazyAsync)(aec => GV2(aec, obj, ndx));
            };
        }

        static async Task<object> GV2(AsyncExprCtx ae, object argLst, object argNdx)
        {

            object obj = await OPs.VectorValueOf(ae, argLst);
            object ndx = await OPs.ConstValueOf(ae, argNdx);
            return GV2(obj, ndx);
        }

        static object GV2(object obj, object ndx)
        {
            {   // IList
                IList lst = obj as IList;
                if (lst != null)
                {
                    var lstOfNdxs = ndx as IList;
                    if (lstOfNdxs != null)
                    {
                        var res = new ArrayList(lstOfNdxs.Count);
                        foreach (object i in lstOfNdxs)
                            try { res.Add(lst[Convert.ToInt32(i)]); }
                            catch (Exception ex) { res.Add(ex.Wrap()); }
                        return res;
                    }
                    else
                    {
                        int i;
                        if (int.TryParse(ndx.ToString(), out i))
                            return lst[i];
                        else return AtKey(new object[] { obj, ndx });
                    }
                }
            }

            {   // IDictionary<string, object>
                var gdct = obj as IDictionary<string, object>;
                if (gdct != null)
                {
                    var lstOfNdxs = ndx as IList;
                    if (lstOfNdxs != null)
                    {
                        var res = new ArrayList(lstOfNdxs.Count);
                        foreach (object i in lstOfNdxs)
                            try { res.Add(gdct[Convert.ToString(i)]); }
                            catch (Exception ex) { res.Add(ex.Wrap()); }
                        return res;
                    }
                    else return gdct[Convert.ToString(ndx)];
                }
            }

            {
                var lstOfNdxs = ndx as IList;
                if (lstOfNdxs != null && lstOfNdxs.Count == 0)
                    return new object[0];
            }
            var err = new ArgumentException(string.Format("GV[2]: 1st arg must implement IList or IDictionary<string, object> interface ({0})", obj));
            err.Data.Add("1st_arg", obj);
            err.Data.Add("2nd_arg", ndx);
            throw err;
        }
        #endregion GV[2]

        /// <summary>
        /// Блок кода, нужен для ограничения области видимости объявленных внутри значений
        /// </summary>
        /// <returns></returns>
        [Arity(1, int.MaxValue)]
        public static object _block(CallExpr ce, Generator.Ctx ctxParent)
        {
            var ctx = new Generator.Ctx(ctxParent);
            var res = _Eval(ce, ctx);
            ctx.CheckUndefinedValues();
            if (OPs.KindOf(res) != ValueKind.Async)
                return res;
            var block = new Block(ctx, res);
#if DEBUG
            block.ce = ce;
#endif
            return (LazyAsync)block.Async;
        }

        [Arity(2, int.MaxValue)]
        public static object _blockInRoot(CallExpr ce, Generator.Ctx ctxParent)
        {
            var allowedValuesNames = ce.args[0] as ArrayExpr;
            if (allowedValuesNames == null)
                throw new ArgumentException("_blockInRoot: first argument must be constant array of values names to transfer from parent context", ce.args[0].ToString());
            var ctxRoot = ctxParent;
            while (ctxRoot.parent != null)
                ctxRoot = ctxRoot.parent;
            // create fake context to mask all parent contexts
            var ctx = new Generator.Ctx(ctxRoot);
            foreach (var expr in allowedValuesNames.args)
            {
                var name = OPs.TryAsName(expr, ctxParent);
                if (name == null)
                    throw new ArgumentException("_blockInRoot: value name expected instead of " + expr.ToString());
                int ndx = ctxParent.IndexOf(name);
                if (ndx < 0)
                    continue;
                ctx.CreateValue(name, null); // only definition, real values is not accessible here
            }
            var ceArgs = new Expr[ce.args.Count - 1];
            for (int i = 0; i < ceArgs.Length; i++)
                ceArgs[i] = ce.args[i + 1];
            var res = _Eval(CallExpr.Eval(ceArgs), ctx);
            if (OPs.KindOf(res) != ValueKind.Async)
                return res;
            throw new NotImplementedException("_blockInRoot: all expressions inside block must have constant (compile-time) result");
        }

        // _For(init,cond,action)
        [Arity(3, 3)]
        public static object _For(CallExpr ce, Generator.Ctx ctxParent)
        { return _ForMacro(ce, ctxParent, false); }

#if SAFE_PARALLELIZATION
        public const string stateParallelizationAlreadyInvolved = "#parallelizationInvolved";
#endif
        public const string stateTryGetConst = "#tryGetConst";

        // _ParFor(init,cond,action)
        [Arity(3, 3)]
        public static object _ParFor(CallExpr ce, Generator.Ctx ctxParent)
        {
            return _ForMacro(ce, ctxParent, true);
        }

        /// <summary>
        /// _For(init, cond, action)
        /// </summary>
        static object _ForMacro(CallExpr ce, Generator.Ctx ctxParent, bool Parallel)
        {
            bool isTryGetConst = ctxParent.IndexOf(stateTryGetConst) >= 0;
            if (isTryGetConst)
                return OPs.EmptyLazyAsync;

            Generator.Ctx ctx = new Generator.Ctx(ctxParent);
#if SAFE_PARALLELIZATION
            if (Parallel)
            {
                if (ctxParent.IndexOf(stateParallelizationAlreadyInvolved) >= 0)
                    ctx.Error("_ParFor: parallelization is already involved");
                ctx.CreateValue(stateParallelizationAlreadyInvolved, string.Empty);
            }
#endif
            object init = Generator.Generate(ce.args[0], ctx);

            if (isTryGetConst)
            {
                foreach (object val in ctx.values)
                    if (OPs.KindOf(val) != ValueKind.Const)
                        return OPs.EmptyLazyAsync;
            }
            else
                // All init values is defined?
                foreach (string s in ctx.NamesOfUndefinedValues())
                    ctx.Error("_For(init,_,_): value named \"" + s + "\" is not defined");

            IDictionary<string, int> defsInInit = new Dictionary<string, int>(ctx.name2ndx);

            // remember init values
            ArrayList values0 = new ArrayList(ctx.values);
            // clear before collect cond and action values
            for (int i = 0; i < ctx.values.Count; i++) ctx.values[i] = Generator.Undefined;

            // fill cond & action values
            object cond = Generator.Generate(ce.args[1], ctx);
            object action = Generator.Generate(ce.args[2], ctx);
            IList valuesI = ctx.values;

            if (isTryGetConst)
            {
                foreach (var p in ctx.name2ndx)
                    if (!defsInInit.ContainsKey(p.Key) && OPs.KindOf(ctx.values[p.Value]) != ValueKind.Const)
                        return OPs.EmptyLazyAsync;
            }
            else
                // All values is defined?
                foreach (string s in ctx.NamesOfUndefinedValues())
                    if (!defsInInit.ContainsKey(s))
                        ctx.Error("_For(_,cond,action): value named \"" + s + "\" is not defined");

            // determine redefined in loop values (loop variables)
            List<int> ndxsLoopValues = new List<int>();
            for (int i = 0; i < valuesI.Count; i++)
                if (valuesI[i] != Generator.Undefined && (i >= values0.Count || valuesI[i] != values0[i]))
                    ndxsLoopValues.Add((i < defsInInit.Count) ? ~i : i);

            if (ndxsLoopValues.Count == 0)
                ctx.Error("_For(_,cond,action): at least one loop variable must be changed in COND or ACTION");

            for (int i = 0; i < values0.Count; i++)
                if (values0[i] == null)
                    values0[i] = valuesI[i];
                else if (valuesI[i] == null)
                    valuesI[i] = values0[i];
            for (int i = values0.Count; i < valuesI.Count; i++)
                values0.Add(valuesI[i]);

            // todo: precalculate loop for constants
            if (Parallel)
                return (LazyAsync)delegate (AsyncExprCtx ae) { return _ParFor(ae, cond, action, ctx.name2ndx, values0, valuesI, ndxsLoopValues); };
            else
                return (LazyAsync)delegate (AsyncExprCtx ae) { return _For(ae, cond, action, ctx.name2ndx, values0, valuesI, ndxsLoopValues); };
        }

        static async Task<object> _For(AsyncExprCtx ae, object cond, object action,
            IDictionary<string, int> name2ndx, IList values0, IList valuesI, IList<int> ndxLoopValues)
        {
            var results = new OPs.ListOfConst(128);
            AsyncExprCtx ctx = new AsyncExprCtx(name2ndx, values0, ae);
            while (true)
            {
                var flag = await OPs.ConstValueOf(ctx, cond);
                if (!OPs.xl2bool(flag))
                    break;
                results.Add(null);
                //***** run action
                int ndx = results.Count - 1;
                results[ndx] = await OPs.ConstValueOf(ctx, action);
                // next context
                ctx = new AsyncExprCtx(ctx, valuesI, ndxLoopValues);
            }
            if (results.Count == 0)
                return new object[0];
            return results;
        }

        static async Task<object> _ParFor(AsyncExprCtx ae, object cond, object action,
            IDictionary<string, int> name2ndx, IList values0, IList valuesI, IList<int> ndxLoopValues)
        {
            var results = new OPs.ListOfConst(128);
            AsyncExprCtx prevCtx = null;
            bool first = true;
            int nActions = 1;
            var semaNewCtx = ae.NewContextSemaphore;
            var allComplete = Utils.NewAsyncLock(true);
            try
            {

                Action<object, object> onActionParCalcComplete = (res, i) =>
                {
                    try
                    {
                        semaNewCtx.Release();
                        lock (results)
                            results[(int)i] = res;
                        if (Interlocked.Decrement(ref nActions) == 0)
                            allComplete.Release();
                    }
                    catch (Exception ex) { throw ex; }
                };
                int nMaxActions = 0;
                while (true)
                {
                    AsyncExprCtx ctx;
                    bool inParallel;
                    if (first)
                    {
                        ctx = new AsyncExprCtx(name2ndx, values0, ae);
                        first = false;
                        inParallel = true; // was false
                    }
                    else
                    {
                        await semaNewCtx.WaitAsync(ae.Cancellation);
                        ctx = new AsyncExprCtx(prevCtx, valuesI, ndxLoopValues);
                        inParallel = true;
                    }
                    var flag = await OPs.ConstValueOf(ctx, cond);
                    if (!OPs.xl2bool(flag))
                        break;
                    lock (results)
                        results.Add(null);
                    prevCtx = ctx;
                    int tmp = Interlocked.Increment(ref nActions);
                    if (nMaxActions < tmp)
                        lock (ae) nMaxActions = tmp;
                    //***** run action
                    //Task<object> t;
                    int ndx = results.Count - 1;
                    if (inParallel)
                    {
                        var tt = Task.Factory.StartNew(
                            () => OPs.ConstValueOf(ctx, action, onActionParCalcComplete, ndx),
                            ae.Cancellation,
                            TaskCreationOptions.AttachedToParent | TaskCreationOptions.PreferFairness,
                            TaskScheduler.Default
                        );
                    }
                    else await OPs.ConstValueOf(ctx, action, onActionParCalcComplete, ndx);
                }
                if (results.Count == 0)
                    return new object[0];
                if (Interlocked.Decrement(ref nActions) > 0) // 
                    await allComplete.WaitAsync(ae.Cancellation);
                return results;
            }
            finally
            {
                while (Interlocked.Decrement(ref nActions) > 0)
                    semaNewCtx.Release();
            }
        }


        [Arity(3, int.MaxValue)]
        public static object _ForEach(CallExpr ce, Generator.Ctx ctx)
        { return _ForEachMacro(ce, ctx, false); }

        [Arity(3, int.MaxValue)]
        public static object _ParForEach(CallExpr ce, Generator.Ctx ctx)
        { return _ForEachMacro(ce, ctx, true); }

        /// <summary>
        /// "for each" cycle emulator:
        /// _ForEach(string varName, IList list, action) // action can use GV(varName)
        /// _ForEach(string varName, item0, ..., itemN, action)
        /// return object[] of action results
        /// </summary>
        static object _ForEachMacro(CallExpr ce, Generator.Ctx ctx, bool Parallel)
        {
            int n = ce.args.Count;
            Expr srcLstExpr;
            if (n == 3)
                srcLstExpr = ce.args[1];
            else
            {
                Expr[] lst = new Expr[n - 2];
                for (int i = n - 3; i >= 0; i--)
                    lst[i] = ce.args[i + 1];
                srcLstExpr = new ArrayExpr(lst);
            }
            Expr varName = ce.args[0];
            var lstInits = new List<Expr>();
            if (varName.nodeType == ExprType.NewArrayInit)
            {
                var init = ((ArrayExpr)varName).args;
                int m = init.Count;
                for (int i = 0; i < m - 1; i++)
                    lstInits.Add(init[i]);
                varName = init[m - 1];
            }
            Expr action = ce.args[n - 1];

            Expr lstExpr;
            Expr cntExpr = new ReferenceExpr("#cnt");
            if (srcLstExpr is ArrayExpr)
            {
                lstExpr = srcLstExpr;
                cntExpr = new ConstExpr(((ArrayExpr)srcLstExpr).args.Count);
            }
            else
            {
                lstExpr = new ReferenceExpr("#lst");
                lstInits.Add(CallExpr.let(lstExpr, srcLstExpr));
                cntExpr = new ReferenceExpr("#cnt");
                lstInits.Add(CallExpr.let(cntExpr, new CallExpr(nameof(FuncDefs_Core.COLUMNS), lstExpr)));
            }
            Expr varNdx = new ReferenceExpr("#ndx");
            lstInits.Add(CallExpr.let(varNdx, ConstExpr.Zero));

            Expr[] args_For = new Expr[] {
				// init
				CallExpr.Eval(lstInits.ToArray()),
				// condition
				new BinaryExpr(ExprType.LessThan,varNdx,cntExpr),
				// action
				CallExpr.Eval(
                    CallExpr.let(varNdx,new BinaryExpr(ExprType.Add,varNdx,ConstExpr.One)),
                    CallExpr.let(varName,new CallExpr(GV,lstExpr,varNdx)),
                    action)
            };
            return _ForMacro(new CallExpr(_For, args_For), ctx, Parallel);
        }

        static object COLUMNS(object v)
        {
            IList list = Utils.TryAsIList(v);
            if (list != null)
                return list.Count;
            else return null;
        }

        [Arity(1, 1)]
        public static object COLUMNS(CallExpr ce, Generator.Ctx ctx)
        {
            var arg = Generator.Generate(ce.args[0], ctx);
            switch (OPs.KindOf(arg))
            {
                case ValueKind.Const:
                    return COLUMNS(arg);
                case ValueKind.Sync:
                    return (LazySync)(() => COLUMNS(OPs.VectorValueOf(arg)));
                default: //case ValueKind.Async:
                    return (LazyAsync)(async ae => COLUMNS(await OPs.VectorValueOf(ae, arg)));
            }
            //throw new InvalidProgramException();
        }

        static object _Count(object v)
        {
            IList list = Utils.TryAsIList(v);
            if (list != null)
                return list.Count;
            IDictionary<string, object> dict;
            if (Utils.TryCastClass(v, out dict))
                return dict.Count;
            return null;
        }

        [Arity(1, 1)]
        public static object _Count(CallExpr ce, Generator.Ctx ctx)
        {
            var arg = Generator.Generate(ce.args[0], ctx);
            switch (OPs.KindOf(arg))
            {
                case ValueKind.Const:
                    return _Count(arg);
                case ValueKind.Sync:
                    return (LazySync)(() => _Count(OPs.ConstValueOf(arg)));
                default: //case ValueKind.Async:
                    return (LazyAsync)(async ae => _Count(await OPs.ConstValueOf(ae, arg)));
            }
            //throw new InvalidProgramException();
        }

        [Arity(1, 1)]
        public static object LoadAssembly(CallExpr ce, Generator.Ctx ctx)
        {
            var assemblyString = Generator.Generate(ce.args[0], ctx);
            var assembly = System.Reflection.Assembly.Load(Convert.ToString(assemblyString));
            return assembly;
        }

        [Arity(3, 3)]
        public static object DefineQuantity(IList args) => Quantities.DefineQuantity(args[0], args[1], args[2]);

        public const string sUsingLibraryPath = "FuncDefs_Core:UsingLibraryPath";

        [Arity(1, 3)]
        //[ArgumentInfo("TYPE_NAME")]
        //[ArgumentInfo("ASSEMBLY_NAMEorOBJ")] optional
        //[ArgumentInfo("FuncName_PREFIX")] optional
        //[return: ResultInfo("TYPE_OBJ")]
        public static object Using(CallExpr ce, Generator.Ctx ctx)
        {
            var typeName = Generator.Generate(ce.args[0], ctx);
            var cacheKey = "FuncDefs:" + typeName;
            var fd = (FuncDefs)System.Web.HttpRuntime.Cache.Get(cacheKey);
            if (fd == null)
            {
                System.Type typeObj;
                if (ce.args.Count > 1)
                {
                    var assemblyInfo = Generator.Generate(ce.args[1], ctx);
                    var assembly = assemblyInfo as System.Reflection.Assembly;
                    if (assembly == null)
                    {
                        int i = ctx.IndexOf(sUsingLibraryPath);
                        var filePath = (i < 0)
                            ? Convert.ToString(assemblyInfo)
                            : System.IO.Path.Combine(Convert.ToString(ctx.values[i]), Convert.ToString(assemblyInfo));
                        assembly = System.Reflection.Assembly.LoadFrom(filePath);
                    }
                    typeObj = assembly.GetType(Convert.ToString(typeName), true);
                }
                else typeObj = System.Type.GetType(Convert.ToString(typeName), true);

                var nsPrefix = (ce.args.Count < 3) ? null : OPs.TryAsString(ce.args[2], ctx);
                fd = new FuncDefs().AddFrom(typeObj, nsPrefix);

                var obj = System.Web.HttpRuntime.Cache.Add(cacheKey, fd, null, System.Web.Caching.Cache.NoAbsoluteExpiration, TimeSpan.FromMinutes(5), CacheItemPriority.Normal, null);
                if (obj != null)
                    fd = (FuncDefs)obj;
            }
            ctx.UseFuncs(fd.GetFuncs);
            return typeName;
        }

        static ValueInfo[] GetValueInfos(Expr list, Generator.Ctx ctx, out string[] names)
        {
            var items = OPs.ItemsOfArray(list);
            if (items != null)
            {
                names = new string[items.Count];
                for (int i = 0; i < names.Length; i++)
                {
                    var name = OPs.TryAsName(items[i], ctx);
                    if (name == null)
                        ctx.Error(string.Format("GetValueInfos: can't interpret value as name: {0}", items[i]));
                    names[i] = name;
                }
            }
            else
            {
                var args = (IList)Generator.Generate(list, ctx);
                if (OPs.KindOf(args) != ValueKind.Const)
                    ctx.Error("GetValueInfos: list must be array of references or constant strings");
                names = new string[args.Count];
                for (int i = 0; i < names.Length; i++)
                    names[i] = Convert.ToString(args[i]);
            }
            if (names.Length == 0)
                return null;
            return GetValueInfos(names);
        }

        static ValueInfo[] GetValueInfos(IEnumerable descriptors)
        {
            if (descriptors == null)
                return null;
            List<ValueInfo> res = null;
            foreach (var di in descriptors)
            {
                string desc;
                var re = di as ReferenceExpr;
                if (re != null)
                    desc = re.name;
                else
                {
                    var n = di as ConstExpr;
                    if (n != null)
                        desc = n.value as string;
                    else desc = Convert.ToString(di);
                }
                var info = ValueInfo.Create(desc, true);
                if (info == null)
                    return null;
                if (res == null)
                    res = new List<ValueInfo>();
                res.Add(info);
            }
            if (res == null)
                return null;
            return res.ToArray();
        }

        [Arity(4, 5)]
        public static object letmacro(CallExpr ce, Generator.Ctx context)
        { return Generator.Generate(CallExpr.let(ce.args[0], new CallExpr(macrofunc, ce.args)), context); }

        [Arity(4, 5)]
        public static object letmacrov(CallExpr ce, Generator.Ctx context)
        { return Generator.Generate(CallExpr.let(ce.args[0], new CallExpr(macrofuncv, ce.args)), context); }

        [Arity(2, int.MaxValue)]
        public static object letfunc(CallExpr ce, Generator.Ctx context)
        { return Generator.Generate(CallExpr.let(ce.args[0], new CallExpr(func, ce.args)), context); }

        [Arity(1, int.MaxValue)]
        public static object _call(CallExpr ce, Generator.Ctx context)
        {
            var fd = (FuncDef)Generator.Generate(ce.args[0], context);
            int n = ce.args.Count - 1;
            var args = new Expr[n];
            for (int i = 0; i < n; i++) args[i] = ce.args[i + 1];
            return Generator.GenerateCall(fd, new CallExpr(fd.name, args), context);
        }

        static FuncDef macroFuncImplMacro(CallExpr ce, Generator.Ctx context, bool acceptVector)
        {
            return macroFuncImpl(context, 
                ce.args[0], ce.args[1], ce.args[2], ce.args[ce.args.Count - 1], (ce.args.Count == 5) ? ce.args[3] : null, 
                acceptVector);
        }

        [Arity(4, 5)]
        public static object macrofunc(CallExpr ce, Generator.Ctx context)
        { return macroFuncImplMacro(ce, context, false); }

        [Arity(4, 5)]
        public static object macrofuncv(CallExpr ce, Generator.Ctx context)
        { return macroFuncImplMacro(ce, context, true); }

        public static FuncDef macroFuncImpl(Generator.Ctx context, 
            Expr nameForNewFunc, Expr inpsDescriptors, Expr outsDescriptors, Expr funcBody, Expr inputParameterToSubstitute = null, 
            bool funcAcceptVector = false)
        {
            var funcName = OPs.TryAsName(nameForNewFunc, context);

            string[] argsNames, outsNames;

            // process results list
            var infoArgs = GetValueInfos(inpsDescriptors, context, out argsNames) ?? ValueInfo.Empties;
            // body
            var infoResults = GetValueInfos(outsDescriptors, context, out outsNames);
            if (inputParameterToSubstitute != null)
                argsNames = new string[] { OPs.TryAsName(inputParameterToSubstitute, context) };

            int nArgs = argsNames.Length;

            Macro fm = (callExpr, ctx) =>
            {
                var dict = new Dictionary<string, Expr>(argsNames.Length);
                for (int i = 0; i < argsNames.Length; i++)
                    dict[argsNames[i]] = callExpr.args[i];
                var modifiedBody = funcBody.Visit(Expr.RecursiveModifier(e =>
                {
                    var re = e as ReferenceExpr;
                    if (re == null)
                        return e;
                    Expr repl;
                    if (dict.TryGetValue(re.name, out repl))
                        return repl;
                    else return e;
                }));
                return Generator.Generate(modifiedBody, ctx);
            };

            var cb = funcBody as CallExpr;
            var cachingExpiration = default(TimeSpan);
            string cacheSubdomain = null;

            {
                var v = context.GetConstant(optionCacheSubdomain);
                if (v != null)
                    cacheSubdomain = Convert.ToString(v);
                v = context.GetConstant(optionCacheExpirationMinutes);
                if (v != null)
                    cachingExpiration = TimeSpan.FromMinutes(OPs.xl2dbl(v));
            }

            // special form of macrofunc compatible with Solver (if infoArgs and infoResults specified)
            return new FuncDef(fm, funcName, nArgs, nArgs, infoArgs, infoResults, FuncFlags.Defaults, 0
                , funcAcceptVector ? (uint)((1 << nArgs) - 1) : 0u
                , cachingExpiration, cacheSubdomain);
        }

        [Arity(2, int.MaxValue)]
        public static object func(CallExpr ce, Generator.Ctx ctx)
        {
            var externalValuesMap = new Dictionary<int, int>();
            int nExternalRefs = 0;
            // initialize context with external values lookup function
            var funcCtx = new Generator.Ctx(null, ctx.GetFunc,
                (fCtx, name) =>
                {
                    var i = ctx.IndexOf(name);
                    if (i < 0)
                        return Generator.Ctx.LookupValueNotFound;
                    var v = ctx[i];
                    if (OPs.KindOf(v) == ValueKind.Const)
                        return v;
                    // we have external reference
                    int k = fCtx.CreateValue(name);
                    fCtx.values[k] = v;
                    externalValuesMap.Add(k, i + 1);
                    nExternalRefs++;
                    return Generator.Ctx.LookupNewValueAdded;
                });
            IList<Expr> funcArgs = null, funcResults = null;
            string funcName = null;
            if (ce.args.Count == 4)
            {
                funcArgs = OPs.ItemsOfArray(ce.args[1]);
                if (funcArgs != null)
                {
                    funcResults = OPs.ItemsOfArray(ce.args[2]);
                    if (funcResults != null)
                        funcName = ce.args[0].ToString();
                }
            }

            if (funcArgs == null)
            {
                funcArgs = new Expr[ce.args.Count - 1];
                for (int i = 0; i < funcArgs.Count; i++)
                    funcArgs[i] = ce.args[i];
            }

            int nArgs = funcArgs.Count;
            for (int i = 0; i < nArgs; i++)
            {
                string name = null;
                {
                    var re = funcArgs[i] as ReferenceExpr;
                    if (re != null)
                        name = re.name;
                    else
                    {
                        var n = funcArgs[i] as ConstExpr;
                        name = n.value as string;
                    }
                }
                if (name == null)
                    ctx.Error("Parameter name expected instead of '" + Convert.ToString(ce.args[i]) + '\'');
                funcCtx.CreateValue(name);
                externalValuesMap.Add(i, ~i);
            }
            var bodyExpr = ce.args[ce.args.Count - 1];
            var body = Generator.Generate(bodyExpr, funcCtx);
            for (int i = 0; i < nArgs; i++)
                funcCtx[i] = string.Empty;

            funcCtx.CheckUndefinedValues();

            if (nExternalRefs == 0)
            {
                AsyncFn f = (callerCtx, args) =>
                    {
                        var fctx = new AsyncExprCtx(funcCtx, callerCtx, args, nArgs);
                        return OPs.ConstValueOf(fctx, body);
                    };
                var infoArgs = GetValueInfos(funcArgs);
                if (infoArgs != null)
                {
                    var infoResults = GetValueInfos(funcResults);
                    if (infoResults != null)
                    {   // special form of func compatible with Solver
                        int nOuts = infoResults.Length;
                        Fx fx = x => (LazyAsync)(ae =>
                            OPs.CalcAsync(ae, x, nArgs, nOuts, f));
                        //*** function for Solver use one argument (all input data included into it)
                        return new FuncDef(fx, funcName, 1, 1, infoArgs, infoResults, FuncFlags.Defaults, 0, 0);
                    }
                }
                return new FuncDef(f, nArgs, ce.ToString());
            }
            else
            {
                var valuesNdx = new int[funcCtx.values.Count];
                foreach (var p in externalValuesMap)
                    valuesNdx[p.Key] = p.Value;
                LazyAsync v = declarationCtx =>
                {
                    var declCtx = declarationCtx;
                    AsyncFn f = (callerCtx, args) =>
                    {
                        int n = args.Count;
                        if (n != nArgs)
                            throw new ArgumentException(string.Format("func: invalid number of args[{0}] (expected {1})", n, nArgs));
                        var fctx = new AsyncExprCtx(funcCtx.name2ndx, funcCtx.values, args, callerCtx, declCtx, valuesNdx);
                        return OPs.ConstValueOf(fctx, body);
                        //return OPs._call_func(fctx, callerCtx, body);
                    };
                    return Utils.TaskFromResult(f);
                };
                return v;
                //ctx.Error("func: closures is not supported yet (" + string.Join(", ", closuresNames.ToArray()) + ")");
            }
        }

        [IsNotPure]
        public static object Semaphore(object count)
        {
            return W.Common.Utils.NewAsyncSemaphore(Convert.ToInt32(count));
        }

        [Arity(2, int.MaxValue)]
        public static async Task<object> _EvalWithSemaphore(AsyncExprCtx ae, IList args)
        {
            var a0 = await OPs.ConstValueOf(ae, args[0]);
            var sema = a0 as W.Common.IAsyncSemaphore;
            if (sema == null)
                throw new ArgumentException("_EvalWithSemaphore: args[0] is not IAsyncSemaphore (is '" +
                    (a0 == null ? "null" : a0.GetType().ToString()) + "')");
            try
            {
                await sema.WaitAsync(ae.Cancellation);
                object obj = null;
                for (int i = 1; i < args.Count; i++)
                    obj = await OPs.ConstValueOf(ae, args[i]);
                return obj;
            }
            finally { sema.Release(); }
        }

        static object _Eval_Sync(IList args)
        {
            int n = args.Count - 1;
            for (int i = 0; i < n; n++)
                OPs.ConstValueOf(args[i]);
            return OPs.ConstValueOf(args[n]);
        }

        static async Task<object> _Eval_Async(AsyncExprCtx ae, IList args)
        {
            object obj = null;
            for (int i = 0; i < args.Count; i++)
                obj = await OPs.ConstValueOf(ae, args[i]);
            return obj;
        }

        [Arity(1, 1)]
        public static object _include(CallExpr ce, Generator.Ctx ctx)
        {
            var fileName = Generator.Generate(ce.args[0], ctx);
            if (OPs.KindOf(fileName) != ValueKind.Const)
                ctx.Error("_include(fileName): fileName must be a constant string");
            var fn = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Convert.ToString(fileName));
            var cacheKey = "_include(" + fn + ")";
            var exprs = (Expr)_Cached(cacheKey, () =>
            {
                var fileInfo = new System.IO.FileInfo(fn);
                if (!fileInfo.Exists)
                    ctx.Error(string.Format("_include: file \"{0}\" not found", fileInfo.FullName));
                var text = System.IO.File.ReadAllText(fileInfo.FullName);
                var res = Parser.ParseToExpr(text);
                return res;
            }, System.Web.Caching.Cache.NoAbsoluteExpiration, TimeSpan.FromSeconds(30));
            return Generator.Generate(exprs, ctx);
        }

        [Arity(1, int.MaxValue)]
        public static object _Eval(CallExpr ce, Generator.Ctx ctx)
        {
            int n = ce.args.Count;
            object[] args = new object[n];
            int maxKind = 0;
            for (int i = 0; i < n; i++)
            {
                object tmp = Generator.Generate(ce.args[i], ctx);
                int kind = (int)OPs.KindOf(tmp);
                if (maxKind < kind) maxKind = kind;
                args[i] = tmp;
            }
#if DEBUG
            var eval = new Eval(args, ce);
#else
            var eval = new Eval(args);
#endif
            switch ((ValueKind)maxKind)
            {
                case ValueKind.Const:
                    return args[n - 1];
                case ValueKind.Sync:
                    return (LazySync)eval.Sync;
                default: //case ValueKind.Async:
                    return (LazyAsync)eval.Async;
            }
        }

        public static object Type(object o)
        {
            if (o == null)
                return "null";

            return o.GetType().FullName;
        }

        public static object Keys(object dictionary)
        {
            var dict = (IDictionary<string, object>)dictionary;
            var lst = dict.Keys.ToArray();
            return lst;
        }

        public static object Values(object dictionary)
        {
            var dict = (IDictionary<string, object>)dictionary;
            var lst = dict.Values.ToArray();
            return lst;
        }

        public static object NotNull([CanBeVector]object obj)
        { return obj != null; }

        public static object IsNull([CanBeVector]object obj)
        { return obj == null; }

        public static object SkipWhile([CanBeVector]object obj)
        {
            var lst = (IList)obj;
            var res = new OPs.ListOfVals(lst.Count);
            bool flag = false;
            foreach (var tmp in lst)
            {
                var it = (IList)tmp;
                if (flag || !Convert.ToBoolean(it[0]))
                {
                    flag = true;
                    res.Add(it[1]);
                }
            }
            return res.ToArray();
        }

        public static object TakeWhile([CanBeVector]object obj)
        {
            var lst = (IList)obj;
            var res = new OPs.ListOfVals(lst.Count);
            foreach (var tmp in lst)
            {
                var it = (IList)tmp;
                if (Convert.ToBoolean(it[0]))
                    res.Add(it[1]);
                else break;
            }
            return res.ToArray();
        }

        #region Caching

        static readonly object[] cacheLocks;

        static FuncDefs_Core()
        {
            cacheLocks = new object[4];
            for (int i = 0; i < 4; i++) cacheLocks[i] = new object();
        }

        public static object _Cached(string cacheKey, Func<object> func, DateTime absoluteExpiration, TimeSpan slidingExpiration)
        {
            int iLock = cacheKey.GetHashCode() & 0x3;
            Lazy<object> cv;
            var obj = System.Web.HttpRuntime.Cache.Get(cacheKey);
            if (obj == null)
                lock (cacheLocks[iLock])
                {
                    obj = System.Web.HttpRuntime.Cache.Get(cacheKey);
                    if (obj == null)
                    {
                        cv = new Lazy<object>(func, true);
                        obj = System.Web.HttpRuntime.Cache.Add(cacheKey, cv, null, absoluteExpiration, slidingExpiration, CacheItemPriority.Normal, null);
                        if (obj != null)
                            throw new Generator.Exception("Caching debug exception: " + cacheKey);
                    }
                    else cv = (Lazy<object>)obj;
                }
            else cv = (Lazy<object>)obj;
            return cv.Value;
        }

        private static void cacheItemRemoveCallback(string key, object value, CacheItemRemovedReason reason)
        {
            var d = value as IDisposable;
            if (d != null)
                d.Dispose();
        }

        public static Task<object> _Cached(AsyncExprCtx ae, string cacheKey, object lazyValue, DateTime absoluteExpiration, TimeSpan slidingExpiration)
        {
            int iLock = cacheKey.GetHashCode() & 0x3;
            var obj = System.Web.HttpRuntime.Cache.Get(cacheKey);
            CtxValue cv;
            if (obj == null)
                lock (cacheLocks[iLock])
                {
                    obj = System.Web.HttpRuntime.Cache.Get(cacheKey);
                    if (obj == null)
                    {
                        cv = new CtxValue(lazyValue, ae);
                        obj = System.Web.HttpRuntime.Cache.Add(cacheKey, cv, null, absoluteExpiration, slidingExpiration, CacheItemPriority.Normal, cacheItemRemoveCallback);
                        if (obj != null)
                            throw new Generator.Exception("Caching debug exception: " + cacheKey);  //prev: cv = (CtxValue)obj;
                    }
                    else cv = (CtxValue)obj;
                }
            else cv = (CtxValue)obj;
            return cv.Get(ae);
        }

        /// <summary>
        /// Get value from cache or from lazyValue
        /// </summary>
        /// <param name="ae"></param>
        /// <param name="args">0: string cacheKey; 1: object lazyValue; [2: double slidingExpiration, s [; double absoluteExpiration, DateTime]]</param>
        /// <returns></returns>
        static async Task<object> _Cached(AsyncExprCtx ae, IList args)
        {
            var cacheKey = Convert.ToString(await OPs.ConstValueOf(ae, args[0]));
            TimeSpan slidingExp;
            if (args.Count > 2 && args[2] != null)
                slidingExp = TimeSpan.FromSeconds(Convert.ToDouble(await OPs.ConstValueOf(ae, args[2])));
            else slidingExp = Cache.NoSlidingExpiration;

            DateTime absExp;
            if (args.Count > 3 && args[3] != null)
            {
                absExp = OPs.FromExcelDate(Convert.ToDouble(await OPs.ConstValueOf(ae, args[3])));
            }
            else absExp = Cache.NoAbsoluteExpiration;

            return await _Cached(ae, cacheKey, args[1], absExp, slidingExp);
        }

        [Arity(2, 4)]
        [CanBeLazy]
        public static object Cached(CallExpr ce, Generator.Ctx ctx)
        {
            object cacheKey = Generator.Generate(ce.args[0], ctx);
            object lazyValue = Generator.Generate(ce.args[1], ctx);
            object slidingExpiration = (ce.args.Count > 2) ? Generator.Generate(ce.args[2], ctx) : null;
            object absoluteExpiration = (ce.args.Count > 3) ? Generator.Generate(ce.args[3], ctx) : null;
            if (OPs.MaxKindOf(cacheKey, slidingExpiration, absoluteExpiration) == ValueKind.Const)
            {
                string sKey = Convert.ToString(cacheKey);
                DateTime absExp = (absoluteExpiration != null)
                    ? OPs.FromExcelDate(Convert.ToDouble(absoluteExpiration))
                    : Cache.NoAbsoluteExpiration;
                TimeSpan sldExp = (slidingExpiration != null)
                    ? TimeSpan.FromSeconds(Convert.ToDouble(slidingExpiration))
                    : Cache.NoSlidingExpiration;
                return (LazyAsync)(aec => _Cached(aec, sKey, lazyValue, absExp, sldExp));
            }
            else
            {
                var args = new object[] { cacheKey, lazyValue, slidingExpiration, absoluteExpiration };
                return (LazyAsync)(aec => _Cached(aec, args));
            }
        }
        #endregion

        public static object NVL1(object arg)
        {
            if (arg == null || arg is Exception)
                return null;
            var tv = arg as ITimedObject;
            if (tv != null)
                return tv.IsEmpty ? null : arg;
            //if (arg is double)
            //    return double.IsNaN((double)arg) ? null : arg;
            return arg;
        }

        [Arity(2, int.MaxValue)]
        public static async Task<object> COALESCE(AsyncExprCtx ae, IList args)
        {
            object[] results = null;
            int cnt = args.Count;
            for (int j = 0; j < cnt; j++)
            {
                object v = await OPs.ConstValueOf(ae, args[j]);
                try
                {
                    IList lst = v as IList;
                    if (lst == null)
                    {
                        if (NVL1(v) != null)
                        {
                            if (results == null)
                                return v; // scalar, scalar
                            else
                            {   // vector, scalar
                                for (int i = results.Length - 1; i >= 0; i--)
                                    results[i] = v;
                                return results;
                            }
                        }
                    }
                    else
                    {
                        bool allNotNulls = true;
                        if (results == null)
                        {   // scalar, vector
                            int n = lst.Count;
                            results = new object[n];
                            for (int i = n - 1; i >= 0; i--)
                            {
                                var tmp = NVL1(lst[i]);
                                allNotNulls = allNotNulls && (tmp != null);
                                results[i] = tmp;
                            }
                        }
                        else
                        {   // vector, vector
                            int n = Math.Min(results.Length, lst.Count);
                            if (n < results.Length)
                                Array.Resize<object>(ref results, n);
                            for (int i = n - 1; i >= 0; i--)
                            {
                                if (results[i] != null) continue;
                                var tmp = NVL1(lst[i]);
                                allNotNulls = allNotNulls && (tmp != null);
                                results[i] = tmp;
                            }
                        }
                        if (allNotNulls)
                            return results;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            return results ?? null;
        }

        [Arity(1, int.MaxValue)]
        public static object Bypass(CallExpr ce, Generator.Ctx ctx)
        {
            object res = null;
            foreach (var arg in ce.args)
            {
                var r = Generator.Generate(arg, ctx);
                if (res == null) res = r;
            }
            return res;
        }

        public static class DbgDiag
        {
            public static System.Diagnostics.TraceSource Logger = W.Common.Trace.GetLogger("DBG");
        }

        [Arity(1, int.MaxValue)]
        public static object DBG(IList args)
        {
            if (args.Count > 1)
            {
                if (args.Count == 2)
                    DbgDiag.Logger.TraceInformation(Convert.ToString(args[1]), args[0]);
                else
                {
                    var objs = new object[args.Count - 2];
                    for (int i = 2; i < args.Count; i++)
                        objs[i - 2] = args[i];
                    DbgDiag.Logger.TraceInformation(Convert.ToString(args[1]), objs);
                }
            }
            return args[0];
        }

    }

    /// <summary>
    /// GetValue1 helper class
    /// </summary>
    class GV1
    {
        readonly int ndx;
#if DEBUG
        readonly string name;
        public GV1(int ndx, string name) { this.ndx = ndx; this.name = name; }
        public override string ToString() { return name; }
#else
        public GV1(int ndx) { this.ndx = ndx; }
#endif
        public Task<object> Async(AsyncExprCtx ae) { return ae.GetValue(ndx); }
    }

    /// <summary>
    /// _Eval function helper class
    /// </summary>
    class Eval
    {
        readonly object[] args;
#if DEBUG
        readonly CallExpr ce;
        public Eval(object[] args, CallExpr ce) { this.args = args; this.ce = ce; }
        public override string ToString() { return ce.ToString(); }
#else
        public Eval(object[] args) { this.args = args; }
#endif
        public object Sync()
        {
            int n = args.Length - 1;
            for (int i = 0; i < n; i++)
                OPs.ConstValueOf(args[i]);
            return OPs.ConstValueOf(args[n]);
        }

        public async Task<object> Async(AsyncExprCtx ae)
        {
            object obj = null;
            for (int i = 0; i < args.Length; i++)
                obj = await OPs.ConstValueOf(ae, args[i]);
            return obj;
        }
    }

    /// <summary>
    /// _block helper class
    /// </summary>
    class Block
    {
        Generator.Ctx ctx;
        object res;

        public Block(Generator.Ctx ctx, object res) { this.ctx = ctx; this.res = res; }
        public Task<object> Async(AsyncExprCtx ae)
        {
            var aec = new AsyncExprCtx(ctx, ctx.values, ae);
            return OPs.ConstValueOf(aec, res);
        }
#if DEBUG
        public CallExpr ce;
        public override string ToString() { return ce.ToString(); }
#endif
    }

}
