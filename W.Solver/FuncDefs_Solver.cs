using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using W.Common;
using W.Expressions;

namespace W.Expressions
{
    using Solver;

    public static partial class FuncDefs_Solver
    {
        /// <summary>
        /// Declare lookup function for data table (list of arrays), key columns must be after value columns
        /// </summary>
        [Arity(4, 4)]
        public static object letLookupFunc(CallExpr ce, Generator.Ctx ctx)
        {
            var arg0 = ce.args[0];
            object name;
            var ref0 = arg0 as ReferenceExpr;
            if (ref0 != null)
                name = ref0.name;
            else
            {
                name = Generator.Generate(arg0, ctx);
                if (OPs.KindOf(name) != ValueKind.Const)
                    throw new SolverException("letLookupFunc(name, ...) : name must be constant");
            }
            var keysDescriptorObjs = Generator.Generate(ce.args[1], ctx);
            var keysDescriptors = keysDescriptorObjs as IList;
            if (OPs.KindOf(keysDescriptorObjs) != ValueKind.Const || keysDescriptors == null)
                throw new SolverException("letLookupFunc(...,keysDescrs ...) : keysDescrs must be list of constants");
            var valsDescriptorObjs = Generator.Generate(ce.args[2], ctx);
            var valsDescriptors = valsDescriptorObjs as IList;
            if (OPs.KindOf(valsDescriptorObjs) != ValueKind.Const || valsDescriptors == null)
                throw new SolverException("letLookupFunc(...,...,valuesDescrs ...) : valuesDescrs must be list of constants");
            var body = ce.args[3];
            var func = (Macro)((expr, gctx) =>
                Generator.Generate(body, gctx)
                );
            var keys = keysDescriptors.Cast<object>().Select(Convert.ToString);
            var vals = valsDescriptors.Cast<object>().Select(Convert.ToString);
            var fd = GetLookupFuncDef(Convert.ToString(name), keys, vals, func);
            return Generator.Generate(CallExpr.let(ce.args[0], new ConstExpr(fd)), ctx);
        }

        public static object IDictsToObjsLst([CanBeVector]object dicts)
        {
            var src = (IList)dicts;
            var dst = new object[src.Count][];
            for (int i = 0; i < dst.Length; i++)
            {
                var lst = ((IIndexedDict)src[i]).ValuesList;
                dst[i] = lst;
            }
            return dst;
        }

        [Arity(2, 4)]
        public static object IDictsToLookupTable(IList args)
        {
            var src = (IList)args[0];
            var keys = (IList)args[1];
            var untime = (args.Count > 2) ? Convert.ToBoolean(args[2]) : false;
            //var resFields = (args.Count > 3) ? (IList)args[3] : null;
            var dst = new object[src.Count][];
            if (dst.Length > 0)
            {
                int[] keysNdxs = new int[keys.Count];
                var k2n = ((IIndexedDict)src[0]).Key2Ndx;
                for (int i = keys.Count - 1; i >= 0; i--)
                    keysNdxs[i] = k2n[Convert.ToString(keys[i])];
                if (untime)
                {
                    for (int i = 0; i < dst.Length; i++)
                    {
                        var lst = ((IIndexedDict)src[i]).ValuesList;
                        var res = new object[lst.Length];
                        for (int j = res.Length - 1; j >= 0; j--)
                        {
                            var to = lst[j] as ITimedObject;
                            res[j] = (to != null) ? to.Object : lst[j];
                        }
                        dst[i] = res;
                    }
                }
                else
                    for (int i = 0; i < dst.Length; i++)
                        dst[i] = ((IIndexedDict)src[i]).ValuesList;
                Array.Sort(dst, (a, b) => a.CompareSameTimedKeys(b, keysNdxs));
            }
            return dst;
        }

        [Arity(2, 2)]
        public static object IDictsToLookup2(IList args)
        {
            var dicts = (IList)args[0];
            var fields = (IList)args[1];
            var res = new object[dicts.Count][];
            if (res.Length == 0)
                return res;
            int n = fields.Count;
            int[] fieldsNdxs = new int[n];
            var lstKeys = new List<int>();
            var k2n = ((IIndexedDict)dicts[0]).Key2Ndx;
            for (int i = n - 1; i >= 0; i--)
            {
                var s = Convert.ToString(fields[i]);
                fieldsNdxs[i] = k2n[s];
                if (ValueInfo.IsID(s))
                    lstKeys.Add(i);
            }
            for (int i = 0; i < res.Length; i++)
            {
                var src = ((IIndexedDict)dicts[i]).ValuesList;
                var dst = new object[n];
                for (int j = 0; j < n; j++)
                    dst[j] = src[fieldsNdxs[j]];
                res[i] = dst;
            }
            var keysNdxs = lstKeys.ToArray();
            Array.Sort(res, (a, b) => a.CompareSameTimedKeys(b, keysNdxs));
            return res;
        }


        [Arity(2, 2)]
        public static object SolverIDictsGroupBy(IList args)
        {
            var rows = (IList<IIndexedDict>)args[0];
            var grpBy = (IList)args[1];
            if (rows.Count == 0)
                return rows; // return empty list
            var row0 = (IIndexedDict)rows[0];
            var k2n = new Dictionary<string, int>(row0.Key2Ndx, StringComparer.OrdinalIgnoreCase);
            var keyNdxs = grpBy.Cast<object>().Select(o => k2n[Convert.ToString(o)]).ToArray();
            {
                var lst = rows.ToArray();
                IIndexedDictExtensions.SortByKeyTimed(lst, keyNdxs);
                rows = lst;
            }
            int n = row0.ValuesList.Length;
            var valNdxs = Enumerable.Range(0, n).Except(keyNdxs).ToArray();
            var results = new OPs.ListOf<IIndexedDict>();
            foreach (IIndexedDict row in rows)
            {
                var vals = row.ValuesList;
                int i = IIndexedDictExtensions.BinarySearch(results, keyNdxs, vals);
                if (i < 0)
                {
                    var grp = new object[n];
                    foreach (int j in keyNdxs)
                    {
                        var to = vals[j] as ITimedObject;
                        grp[j] = (to == null) ? vals[j] : to.Object;
                    }
                    foreach (int j in valNdxs)
                    {
                        var lst = new List<object>();
                        lst.Add(vals[j]);
                        grp[j] = lst;
                    }
                    results.Insert(~i, ValuesDictionary.New(grp, k2n));
                }
                else
                {
                    var grp = results[i].ValuesList;
                    foreach (int j in valNdxs)
                    {
                        var lst = (IList)grp[j];
                        lst.Add(vals[j]);
                    }
                }
            }
            return results;
        }

        [Arity(1, 1)]
        public static object IDictsGroupByFirstOrderedField([CanBeVector]object arg)
        {
            var rows = (IList)arg;
            if (rows.Count == 0)
                return rows; // return empty list
            var results = new OPs.ListOf<IIndexedDict>();
            var keyNdxs = new int[] { 0 };
            foreach (IIndexedDict row in rows)
            {
                var vals = row.ValuesList;
                int n = vals.Length;
                int i = IIndexedDictExtensions.BinarySearch(results, keyNdxs, vals);
                if (i < 0)
                {
                    var grp = new object[n];
                    {
                        var to = vals[0] as ITimedObject;
                        grp[0] = (to == null) ? vals[0] : to.Object;
                    }
                    for (int j = 1; j < n; j++)
                    {
                        var lst = new List<object>();
                        lst.Add(vals[j]);
                        grp[j] = lst;
                    }
                    results.Insert(~i, ValuesDictionary.New(grp, row.Key2Ndx));
                }
                else
                {
                    var grp = results[i].ValuesList;
                    for (int j = 1; j < n; j++)
                    {
                        var lst = (IList)grp[j];
                        lst.Add(vals[j]);
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ce">0: source param name; 1: source data list; 2: filtering expression (AND only supported); 3: output parameters list</param>
        /// <param name="ctx"></param>
        /// <returns></returns>
        [Arity(4, 4)]
        public static object Filtering(CallExpr ce, Generator.Ctx ctx)
        {
            // имя входного параметра
            var inputParam = (string)Generator.Generate(ce.args[0], ctx);
            var sourceData = ce.args[1].ToString();

            // условия фильтрации, связанные по AND
            IList<Expr> filterConditions;
            {
                var cond = ce.args[2] as CallExpr;
                if (cond == null || cond.funcName != "AND")
                {
                    cond = (CallExpr)Parser.ParseToExpr(Convert.ToString(Generator.Generate(ce.args[2], ctx)));
                    System.Diagnostics.Trace.Assert(cond.funcName == "AND", "Filtering: AND");
                }
                filterConditions = cond.args;
            }
            var outputParams = ((IList)Generator.Generate(ce.args[3], ctx)).Cast<string>().ToArray();

            return Filter.Filtering(ctx, inputParam, sourceData, filterConditions, outputParams);
        }

        static object solverAliasImpl(CallExpr ce, Generator.Ctx ctx)
        {
            var alias = OPs.TryAsName(ce.args[0], ctx);
            if (alias == null)
                throw new SolverException(string.Format(ce.funcName + "(alias ...): alias must be constant or name instead of [{0}]", ce.args[0]));
            var target = (ce.args.Count > 1) ? OPs.TryAsName(ce.args[1], ctx) : string.Empty;
            if (target == null)
                throw new SolverException(string.Format(ce.funcName + "(..., target ...): target must be constant or name instead of [{0}]", ce.args[0]));
            int priority = (ce.args.Count > 2) ? Convert.ToInt32(ctx.GetConstant(ce.args[2])) : 255;
            int i = ctx.GetOrCreateIndexOf(optionSolverAliases);
            var aliases = ctx[i] as SolverAliases;
            if (aliases == null)
            {
                aliases = new SolverAliases();
                ctx[i] = aliases;
            }
            switch (ce.funcName)
            {
                case nameof(solverAlias):
                    return aliases.Set(alias, target, priority);
                case nameof(solverAliasPush):
                    return aliases.Push(alias, target);
                case nameof(solverAliasPop):
                    return aliases.Pop(alias);
                default:
                    throw new NotImplementedException(ce.funcName);
            }
        }

        [Arity(2, 3)]
        public static object solverAlias(CallExpr ce, Generator.Ctx ctx) { return solverAliasImpl(ce, ctx); }

        [Arity(2, 2)]
        public static object solverAliasPush(CallExpr ce, Generator.Ctx ctx) { return solverAliasImpl(ce, ctx); }

        [Arity(1, 1)]
        public static object solverAliasPop(CallExpr ce, Generator.Ctx ctx) { return solverAliasImpl(ce, ctx); }

        [Arity(3, 3)]
        public static object SolverWeightedSumsFactory(IList args)
        {
            var periodInDays = Convert.ToDouble(args[0]);
            var startTime = OPs.FromExcelDate(Convert.ToDouble(args[1]));
            var endTime = OPs.FromExcelDate(Convert.ToDouble(args[2]));
            return new WeightedSums.Factory(startTime, endTime, TimeSpan.FromDays(periodInDays));
        }

        [Arity(1, 3)]
        public static object SolverWeightedSumsNew(IList args)
        {
            var f = (WeightedSums.Factory)args[0];
            var sums = f.NewSums();
            if (args.Count == 3)
            {
                var values = (IList)args[1];
                var keys = (IList)args[2];
                sums.Add(values, keys);
            }
            return sums;
        }

        [Arity(2, 2)]
        public static object SolverWeightedSumsAdd(IList args)
        {
            var akk = (WeightedSums.Sums)args[0];
            var add = (WeightedSums.Sums)args[1];
            lock (akk)
                akk.Add(add);
            return akk;
        }

        static double[] Get(this WeightedSums.Sums sums, string what)
        {
            switch (what)
            {
                case "SUMS": return sums.GetValues();
                case "WEIGHTS": return sums.GetWeights();
                case "AVGS": return sums.GetAverages();
            }
            throw new ArgumentException(string.Format("WeightedSums.Sums.Get(what): 'what' must be one of {'SUMS','WEIGHTS','AVGS'} instead of  '{0}'", what), "what");
        }

        [Arity(2, 2)]
        public static object SolverWeightedSumsGet(IList args)
        {
            var sums = (WeightedSums.Sums)args[0];
            var what = Convert.ToString(args[1]);
            return sums.Get(what);
        }

        public static object SolverTimesOfIDicts([CanBeVector]object arg0)
        {
            var idicts = Utils.AsIList(arg0);
            int n = idicts.Count;
            var res = new ITimedObject[n];
            for (int i = 0; i < n; i++)
            {
                var r = idicts[i] as IIndexedDict;
                if (r != null)
                {
                    var maxBegTime = DateTime.MinValue;
                    var minEndTime = DateTime.MaxValue;
                    foreach (ITimedObject to in r.ValuesList)
                    {
                        if (to == null)
                            continue;
                        if (maxBegTime < to.Time)
                            maxBegTime = to.Time;
                        if (to.EndTime < minEndTime)
                            minEndTime = to.EndTime;
                    }
                    res[i] = new TimedString(maxBegTime, minEndTime, string.Empty);
                }
                else res[i] = TimedObject.FullRangeI;
            }
            return res;
        }

        public static object SolverWeightedSumsFactoryTimes(object arg0)
        {
            var f = (WeightedSums.Factory)arg0;
            int n = f.itemsCount;
            var res = new ITimedObject[n];
            var ts = f.timeStep;
            var beg = f.begTime;
            for (int i = 0; i < n; i++)
                res[i] = new TimedString(DateTime.FromOADate(beg + ts * i), DateTime.FromOADate(beg + ts * (i + 1)), string.Empty);
            return res;
        }

        [Arity(4, 5)]
        public static object SolverTimedTabulation(IList args)
        {
            var items = Utils.ToIList(args[0]) ?? new object[0];
            var timeA = Convert.ToDouble(args[1]);
            var timeB = Convert.ToDouble(args[2]);
            var nParts = Convert.ToInt32(args[3]);
            var what = (args.Count > 4) ? Convert.ToString(args[4]) : null;
            var f = new WeightedSums.Factory(OPs.FromExcelDate(timeA), OPs.FromExcelDate(timeB), nParts);
            var sums = f.NewSums();
            sums.Add(items);
            var data = sums.Get(what ?? "AVGS");
            // prepare result array
            var res = new object[data.Length];
            for (int i = 0; i < data.Length; i++)
                res[i] = double.IsNaN(data[i]) ? null : (object)data[i];
            return res;
        }

        [Arity(2, 2)]
        public static object SolverTimedObjects(IList args)
        {
            var times = (IList)args[0];
            var values = (IList)args[1];
            var res = new ITimedObject[times.Count];
            for (int i = 0; i < res.Length; i++)
            {
                var vi = values[i] as ITimedObject;
                if (vi != null)
                    res[i] = vi;
                else
                {
                    var to = times[i] as ITimedObject;
                    res[i] = TimedObject.Timed(to.Time, to.EndTime, values[i]);
                }
            }
            return res;
        }

        static class Dependecies
        {
            public static IEnumerable<FuncInfo> GetFuncInfos(IEnumerable<FuncDef> funcDefs, SolverAliases aliasOf, Func<FuncInfo, bool> funcFilter = null)
            {
                if (funcFilter == null)
                    funcFilter = x => true;
                return funcDefs.Select(fd => FuncInfo.Create(fd, aliasOf)).Where(fi => fi != null && funcFilter(fi));
            }

            public static Dictionary<string, Dictionary<string, int>> Find(IEnumerable<FuncInfo> availableFunctions,
                SolverAliases aliases,
                int maxDistance = int.MaxValue,
                string[] sourceValues = null,
                string[] dependentValues = null)
            {
                var dependence = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

                // dependencies with "distance" = 1
                foreach (var fi in availableFunctions)
                    foreach (var value in fi.pureOuts)
                    {
                        var valueDeps = aliases.AtKey(dependence, value, null);
                        if (valueDeps == null)
                        {
                            valueDeps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            dependence[value] = valueDeps;
                        }
                        foreach (var src in fi.inputs)
                            valueDeps[src] = 1;
                    }

                if (maxDistance > 1)
                {
                    // dependencies with "distance" > 1
                    int prevDistance = 1;

                    if (sourceValues != null)
                    {
                        var emptyDeps = new Dictionary<string, int>(0);
                        foreach (var value in sourceValues)
                        {
                            var valueDeps = aliases.AtKey(dependence, value, null);
                            if (valueDeps == null)
                                dependence[value] = emptyDeps;
                            else valueDeps.Clear();
                        }
                    }


                    var nextq = new Dictionary<string, bool>(dependence.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var value in (dependentValues != null) ? dependentValues.Distinct() : dependence.Keys)
                        nextq[value] = true;

                    var queue = new Dictionary<string, bool>(dependence.Count, StringComparer.OrdinalIgnoreCase);

                    while (prevDistance < maxDistance)
                    {
                        {
                            var tmp = nextq;
                            nextq = queue;
                            queue = tmp;
                        }
                        foreach (var valueName in queue.Keys)
                        {
                            if (valueName.Length == 0)
                                break;
                            var valueDeps = aliases.AtKey(dependence, valueName, null);
                            if (valueDeps == null)
                                continue;
                            var prevLvlDeps = valueDeps.Where(dep => dep.Value == prevDistance).ToArray();
                            bool newDepAdded = false;
                            foreach (var dep in prevLvlDeps)
                            {
                                var srcDeps = aliases.AtKey(dependence, dep.Key, null);
                                if (srcDeps != null)
                                    foreach (var srcDep in srcDeps)
                                    {
                                        int dist = aliases.AtKey(valueDeps, srcDep.Key, 0);
                                        if (dist == 0)
                                        {
                                            valueDeps[srcDep.Key] = prevDistance + 1;
                                            nextq[srcDep.Key] = true;
                                            newDepAdded = true;
                                        }
                                    }
                            }
                            if (newDepAdded)
                                nextq[valueName] = true;
                        }
                        queue.Clear();
                        if (nextq.Count == 0)
                            break;
                        prevDistance++;
                    }
                }
                return dependence;
            }
        }

        /// <summary>
        /// Find dependencies of specified parameters
        /// </summary>
        /// <param name="ce">0:source params names; [1:possible dependent params; [2:what parameters must be not used]]</param>
        /// <param name="ctx"></param>
        /// <returns>Returns dictionary[param_name, influences], where influences is a parameter list on which param_name depends</returns>
        [Arity(1, 3)]
        public static object FindDependencies(CallExpr ce, Generator.Ctx ctx)
        {
            var arg0 = Generator.Generate(ce.args[0], ctx);
            if (OPs.KindOf(arg0) != ValueKind.Const || arg0 == null)
                throw new SolverException("FindDependencies: list of source params must be constant and not null");
            var arg1 = (ce.args.Count > 1) ? Generator.Generate(ce.args[1], ctx) : null;
            if (OPs.KindOf(arg1) != ValueKind.Const)
                throw new SolverException("FindDependencies: optional list of dependent params must be constant");
            var arg2 = (ce.args.Count > 2) ? Generator.Generate(ce.args[2], ctx) : null;
            if (OPs.KindOf(arg2) != ValueKind.Const)
                throw new SolverException("FindDependencies: optional list of unusable params must be constant");
            var srcPrms = ((IList)arg0).Cast<object>().Select(o => o.ToString()).ToArray();
            var depPrms = (arg1 == null) ? null : ((IList)arg1).Cast<object>().Select(o => o.ToString()).ToArray();
            var unusables = (arg2 == null)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : ((IList)arg2).Cast<object>().Select(o => o.ToString()).ToDictionary(s => s, StringComparer.OrdinalIgnoreCase);
            var aliasOf = GetAliases(ctx);
            var funcs = Dependecies.GetFuncInfos(ctx.GetFunc(null, 0), aliasOf, fi => !fi.inputs.Any(inp => unusables.ContainsKey(inp)));
            var dependencies = Dependecies.Find(funcs, aliasOf, int.MaxValue, srcPrms, depPrms);
            var resDict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var resParams = (depPrms == null)
                ? dependencies.Keys.Except(srcPrms, StringComparer.OrdinalIgnoreCase)
                : depPrms.Where(s => dependencies.ContainsKey(s));
            var res = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var prmName in resParams)
            {
                var deps = dependencies[prmName];
                var srcs = srcPrms.Where(s => deps.ContainsKey(s)).ToArray();
                if (srcs.Length > 0)
                    res.Add(prmName, srcs);
            }
            return res;
        }

        static string GetDetailsIfMatched(string[] paramParts, string[] maskParts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < maskParts.Length; i++)
            {
                if (!string.IsNullOrEmpty(maskParts[i]) &&
                    string.Compare(maskParts[i], paramParts[i], StringComparison.OrdinalIgnoreCase) != 0
                )
                    return null;
                if (sb.Length > 0)
                    sb.Append('_');
                sb.Append(paramParts[i]);
            }
            return sb.ToString();
        }

        static string ParamFromMaskAndDetails(string details, string[] maskParts)
        {
            var detailParts = details.Split('_');
            var sb = new StringBuilder(details.Length);
            for (int i = 0; i < maskParts.Length; i++)
            {
                if (sb.Length > 0) sb.Append('_');
                sb.Append(string.IsNullOrEmpty(maskParts[i]) ? detailParts[i] : maskParts[i]);
            }
            return sb.ToString();
        }

        static IEnumerable<FuncDef> DefineProjectionFuncsImpl(CallExpr ce, Generator.Ctx ctx, bool acceptVector)
        {
            var inpParams = ((IList)OPs.GetConstant(ctx, ce.args[0])).Cast<object>().Select(Convert.ToString).ToArray();
            var inpParamsParts = inpParams.Select(s => s.Split('_')).ToArray();
            var inpParamsMasks = inpParamsParts.Where(parts => parts.Any(string.IsNullOrEmpty)).ToArray();
            var outParams = ((IList)OPs.GetConstant(ctx, ce.args[1])).Cast<object>().Select(Convert.ToString).ToArray();
            var outParamsMasks = outParams.Select(s => Convert.ToString(s).Split('_')).ToArray();

            var newFuncName = "@projfunc:" + ce.args[ce.args.Count - 1].ToString().Replace('(', '_').Replace(')', '_').Replace('"', '_').Replace('\'', '_');

            #region Collect suitable parameters from all functions outputs
            var suitableParams = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in ctx.GetFunc(null, 0))
            {
                foreach (var prm in f.resultsInfo)
                {
                    var prmParts = prm.Parts();
                    for (int i = 0; i < inpParamsMasks.Length; i++)
                    {
                        var maskParts = inpParamsMasks[i];
                        var details = GetDetailsIfMatched(prmParts, maskParts);
                        if (details == null) continue;
                        if (!suitableParams.TryGetValue(details, out var suitables))
                        {
                            suitables = new string[inpParamsMasks.Length];
                            suitableParams.Add(details, suitables);
                        }
                        suitables[i] = details;
                    }
                }
            }
            #endregion

            #region Return FuncDef for each suitable params set
            foreach (var suitable in suitableParams.Where(prms => prms.Value.All(s => s != null)))
            {
                var details = suitable.Key;
                var inps = new string[inpParamsParts.Length];
                for (int i = 0; i < inpParamsParts.Length; i++)
                {
                    var ip = inpParamsParts[i];
                    if (ip.Any(string.IsNullOrEmpty))
                    {   // one or more parts masked
                        inps[i] = ParamFromMaskAndDetails(details, ip);
                    }
                    else inps[i] = inpParams[i];
                }

                var outs = new string[outParams.Length];
                for (int i = 0; i < outParamsMasks.Length; i++)
                    outs[i] = ParamFromMaskAndDetails(details, outParamsMasks[i]);

                yield return FuncDefs_Core.macroFuncImpl(
                    context: ctx,
                    nameForNewFunc: new ConstExpr(newFuncName + details),
                    inpsDescriptors: new ArrayExpr(inps.Select(ReferenceExpr.Create).Cast<Expr>().ToList()),
                    outsDescriptors: new ArrayExpr(outs.Select(ReferenceExpr.Create).Cast<Expr>().ToList()),
                    funcBody: ce.args[ce.args.Count - 1],
                    inputParameterToSubstitute: (ce.args.Count > 3) ? ce.args[2] : null,
                    funcAcceptVector: acceptVector
                    );
            }
            #endregion
        }

        /// <summary>
        /// Define functions to convert from one parameters set to another.
        /// For example, code lookup functions, e.g.
        /// "solver:DefineProjectionFuncs({'_CLCD_PIPE','CLASS_DICT_PIPE'}, { '_NAME_PIPE','_SHORTNAME_PIPE' }, data, pipe:GetClassInfo(data) )"
        /// </summary>
        [Arity(3, 4)]
        public static object DefineProjectionFuncs(CallExpr ce, Generator.Ctx ctx)
        {
            return DefineProjectionFuncsImpl(ce, ctx, false);
        }

    }
}