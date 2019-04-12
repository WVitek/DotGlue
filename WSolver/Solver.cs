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

    public class SolverException : Generator.Exception
    {
        public SolverException(string msg) : base(msg) { }
    }

    public static class FuncDefs_Solver
    {
        class Index : Dictionary<string, int>
        {
            //public Index() { }

            public int Get(string key)
            {
                int result;
                if (!TryGetValue(key, out result))
                {
                    result = Count;
                    Add(key, result);
                }
                return result;
            }
            public int[] GetAll(string[] keys)
            {
                var res = new int[keys.Length];
                for (int i = keys.Length - 1; i >= 0; i--)
                    res[i] = Get(keys[i]);
                return res;
            }

            public int[] GetAll(string[] keys, out int maxIndex)
            {
                int max = -1;
                var res = new int[keys.Length];
                for (int i = keys.Length - 1; i >= 0; i--)
                {
                    int k = Get(keys[i]);
                    if (k > max) max = k;
                    res[i] = k;
                }
                maxIndex = max;
                return res;
            }

            public int GetMaxIndexOfAll(string[] keys)
            {
                int max = -1;
                for (int i = keys.Length - 1; i >= 0; i--)
                {
                    int k = Get(keys[i]);
                    if (k > max) max = k;
                }
                return max;
            }

            public int[] TryGetAll(string[] keys)
            {
                var r = new int[keys.Length];
                for (int i = keys.Length - 1; i >= 0; i--)
                    if (!TryGetValue(keys[i], out r[i]))
                        return null;
                return r;
            }
            public int[] GetSome(string[] keys)
            {
                var r = new int[keys.Length];
                for (int i = keys.Length - 1; i >= 0; i--)
                    if (!TryGetValue(keys[i], out r[i]))
                        r[i] = int.MaxValue;
                return r;
            }
        }

        class StateInfo : IEqualityComparer<StateInfo>
        {
            public static readonly StateInfo Empty = new StateInfo() { knowns = BitSet.Empty, unknowns = BitSet.Empty };
            public StateInfo prevState;
            public FuncInfo fi;
            public int iLastFunc;
            public BitSet knowns;
            public BitSet unknowns;

            public bool Equals(StateInfo x, StateInfo y)
            {
                var eq = BitSet.Empty.Equals(x.knowns, y.knowns) && BitSet.Empty.Equals(x.unknowns, y.unknowns);
                if (eq)
                {
                    var sx = x.Enumerate().Select(fi => fi.name).OrderBy(s => s);
                    var sy = y.Enumerate().Select(fi => fi.name).OrderBy(s => s);
                    eq = sx.SequenceEqual(sy);
                }
                return eq;
            }

            public int GetHashCode(StateInfo obj)
            {
                var a = (knowns == null) ? 0 : knowns.GetHashCode();
                var b = (unknowns == null) ? 0 : unknowns.GetHashCode();
                return a ^ b;
            }

            public IEnumerable<FuncInfo> Enumerate()
            {
                var s = this;
                while (s != null && s.fi != null)
                {
                    yield return s.fi;
                    s = s.prevState;
                }
            }

            public bool ContainsCall(FuncInfo fi)
            {
                for (var s = this; s != null && s.fi != null; s = s.prevState)
                    if (s.fi == fi)
                        return true;
                return false;
            }

            public override string ToString()
            {
                return string.Join(", ", Enumerate().Select(fi => fi.name));
            }
#if DEBUG
            public Index index;
            string DbgBitSet(BitSet bs)
            {
                if (bs.MaxIndex == 0)
                    return string.Empty;
                if (index == null)
                    return "!index is not set";
                int n = index.Values.Max() + 1;
                var dict = Enumerable.Range(0, n).Select(i => new List<string>()).ToArray();
                foreach (var p in index) dict[p.Value].Add(p.Key);
                return string.Join(",", bs.EnumOnesIndexes().SelectMany(i => dict[i]));
            }
            public string DbgKnowns { get { return DbgBitSet(knowns); } }
            public string DbgUnknowns { get { return DbgBitSet(unknowns); } }
#endif
        }

        class States
        {
            public StateInfo Enqueue(StateInfo si)
            {
                queue.Enqueue(si);
                dict.Add(si, si);
                return si;
            }

            public void EnqueueEmpty() { queue.Enqueue(StateInfo.Empty); }

            public StateInfo Dequeue()
            {
                if (queue.Count == 0)
                    return StateInfo.Empty;
                var si = queue.Dequeue();
                dict.Remove(si);
                return si;
            }

            public void Clear() { queue.Clear(); }

            public int Count { get { return queue.Count; } }
            public bool Contains(StateInfo state) { return dict.ContainsKey(state); }

            Queue<StateInfo> queue = new Queue<StateInfo>();
            Dictionary<StateInfo, StateInfo> dict = new Dictionary<StateInfo, StateInfo>(StateInfo.Empty);
        }

        public static SolverAliases GetAliases(Generator.Ctx ctx)
        {
            int i = ctx.IndexOf(optionSolverAliases);
            if (i >= 0)
                return (SolverAliases)ctx[i];
            else return SolverAliases.Empty;
        }

        static IEnumerable<Expr> Solution(IEnumerable<FuncDef> availableFunctions, string[] inputValues, string[] resultValues, IList<string[]> outputSets, Generator.Ctx ctx)
        {
            var aliasOf = GetAliases(ctx);
            var info = new StringBuilder();
            var accessibleFuncs = new List<FuncInfo>();

            var dictInputValues = new Dictionary<string, string>(inputValues.Length);
            {
                var inputValuesRealNames = aliasOf.ToRealNames(inputValues);
                for (int i = 0; i < inputValues.Length; i++)
                    dictInputValues[inputValuesRealNames[i]] = inputValues[i];
            }

            var dictResultValues = new Dictionary<string, string>(resultValues.Length);
            {
                var resultValuesRealNames = aliasOf.ToRealNames(resultValues);
                for (int i = 0; i < resultValues.Length; i++)
                    dictResultValues[resultValuesRealNames[i]] = resultValues[i];
            }

            var cachingInfoFuncs = new List<FuncInfo>();

            string[] arrAccessibleVals;

            {   // filter functions list \ determine accessible functions
                string sCachingInfoParam = null;
                {
                    int iCaching = ctx.IndexOf(W.Expressions.FuncDefs_Core.optionCachingInfoParam);
                    var ce = (iCaching < 0) ? null : ctx[iCaching];
                    if (ce != null && OPs.KindOf(ce) == ValueKind.Const)
                        sCachingInfoParam = ValueInfo.Create(ce.ToString()).ToString();
                }

                var availableFuncs = new List<FuncInfo>(availableFunctions.Select(fd => FuncInfo.Create(fd, aliasOf)).Where(fi =>
                {
                    if (fi == null)
                        return false;
                    if (fi.outputs.Length == 1 && fi.outputs[0] == sCachingInfoParam)
                        cachingInfoFuncs.Add(fi);
                    return true;
                }));

                var accessibleValues = new Dictionary<string, bool>();
                foreach (var s in dictInputValues.Keys)
                    accessibleValues[s] = true;
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int i = availableFuncs.Count - 1; i >= 0; i--)
                    {
                        var fi = availableFuncs[i];
                        if (fi.inputs.All(s => accessibleValues.ContainsKey(s)))
                            if (fi.pureOuts.Any(s => !accessibleValues.ContainsKey(s)))
                            {
                                availableFuncs.RemoveAt(i);
                                accessibleFuncs.Add(fi);
                                foreach (var s in fi.pureOuts)
                                    aliasOf.SetAt(accessibleValues, s, true);
                                changed = true;
                            }
                            else { availableFuncs.RemoveAt(i); }
                    }
                }

                var unaccessibleValues = dictInputValues.Keys
                    .Where(s => !accessibleValues.ContainsKey(s))
                    .ToDictionary(s => s, s => true);

                if (unaccessibleValues.Count > 0)
                {
                    var unaccVals = new Dictionary<string, bool>(unaccessibleValues);
                    bool dodo = true;
                    while (dodo)
                    {
                        dodo = false;
                        for (int i = availableFuncs.Count - 1; i >= 0; i--)
                        {
                            var fi = availableFuncs[i];
                            if (fi.outputs.Any(s => unaccVals.ContainsKey(s)))
                            {
                                availableFuncs.RemoveAt(i);
                                foreach (var arg in fi.inputs.Where(s => !accessibleValues.ContainsKey(s)))
                                    unaccVals[arg] = true;
                                dodo = true;
                            }
                        }
                    }
                    throw new Generator.UnknownValuesException("Can't found value(s) named: " + string.Join(", ", unaccVals.Keys), unaccVals.Keys);
                }

                arrAccessibleVals = accessibleValues.Keys.ToArray<string>();
            }
            // all funcs is topologically sorted (independent is first, most dependent is last)
            accessibleFuncs = FuncInfo.TopoSort(accessibleFuncs);

            var valuesIndex = new Index();
            //valuesIndex.GetAll(arrAccessibleVals);

            var solutions = new List<StateInfo>();
            var usedLookupFuncs = new List<FuncInfo>();
            StateInfo lastState;
            {
                StateInfo initState;
                //*** init states queue
                {
                    Dictionary<string, bool> knownsDict = dictInputValues.Keys.ToDictionary(s => s, s => true);
                    Dictionary<string, bool> unknownsDict = dictResultValues.Keys.ToDictionary(s => s, s => true);
                    foreach (var fi in accessibleFuncs.Where(f => f.IsLookup))
                    {
                        if (!fi.inputs.All(knownsDict.ContainsKey))
                            continue;
                        bool used = false;
                        foreach (var knownPrm in fi.pureOuts.Where(unknownsDict.ContainsKey))
                        {
                            used = true;
                            unknownsDict.Remove(knownPrm);
                            knownsDict[knownPrm] = true;
                        }
                        if (used)
                            usedLookupFuncs.Add(fi);
                    }

                    var inputs = valuesIndex.GetAll(knownsDict.Keys.ToArray<string>());
                    var outputs = valuesIndex.GetAll(unknownsDict.Keys.ToArray<string>());

                    initState = new StateInfo()
                    {
                        prevState = null,
                        fi = null,
                        knowns = new BitSet(BitSet.Empty, inputs),
                        unknowns = new BitSet(BitSet.Empty, outputs.Where(ndx => Array.IndexOf(inputs, ndx) < 0).ToArray()),
                        iLastFunc = accessibleFuncs.Count
                    };
#if DEBUG
                    initState.index = valuesIndex;
#endif
                }

                if (initState.unknowns.MaxIndex > 0)
                    lastState = SolutionReversedSearch(accessibleFuncs, valuesIndex, solutions, initState);
                else
                {
                    lastState = initState;
                    solutions.Add(initState);
                }
            }
            if (solutions.Count == 0)
            {
                // no solutions found
                //var info = new StringBuilder();
                var ndxToName = new Dictionary<int, string>();
                foreach (var pair in valuesIndex) ndxToName.Add(pair.Value, pair.Key);
                info.Append("Unknown values: {");
                info.Append(string.Join(", ", lastState.unknowns.EnumOnesIndexes().Select(ndx => ndxToName[ndx])));
                info.AppendLine("}");
                info.Append("Known values: {");
                info.Append(string.Join(", ", lastState.knowns.EnumOnesIndexes().Select(ndx => ndxToName[ndx])));
                info.AppendLine("}");
                var ex = new Generator.Exception(info.ToString());
                //ex.Data.Add("lastState", lastState);
                throw ex;
            }

            foreach (var solution in solutions)
            {
                var callsList = solution.Enumerate().ToList();
                var callsInOuts = callsList.SelectMany(fi => fi.inOuts).Distinct().ToDictionary(s => s, s => true);
                var callsPureIns = callsList.SelectMany(fi => fi.pureIns).Concat(dictResultValues.Keys).Distinct().ToDictionary(s => s, s => true);
                var inpVals = new List<string>();
                var inpCalls = new List<FuncInfo>();
                // Single input functions calls

                foreach(var p in dictInputValues)
                {
                    var inpV = p.Value;
                    var inpRN = p.Key;
                    bool isKey = false;
                    if (!callsInOuts.ContainsKey(inpRN))
                    {
                        if (callsPureIns.ContainsKey(inpRN))
                            inpVals.Add(inpV);
                        else
                            continue;
                    }
                    else isKey = true;
                    var arrInps = new string[] { inpV };
                    var arrOuts = new string[] { inpRN };
                    // input function
                    var fi = new FuncInfo(arrInps, arrOuts);
                    if (isKey)
                        inpCalls.Insert(0, fi);
                    else inpCalls.Add(fi);
                }
                // Multi input and lookup functions calls
                inpCalls.AddRange(usedLookupFuncs);
                callsList.InsertRange(0, inpCalls);
                var lstCalls = callsList.Select(nfo => FuncInfo.WithoutCachingInfo(nfo, aliasOf)).ToList();
                yield return DecodeSolution(
                    lstCalls,
                    inpVals.ToArray(),
                    outputSets,
                    ctx, aliasOf, cachingInfoFuncs.ToArray()
                );
            }
        }

        struct PrmInfo
        {
            public string prmName;
            public List<int> ndxResFuncs;
        }

        private static StateInfo SolutionReversedSearch2(List<FuncInfo> accessibleFuncs, Index prmIndex, List<StateInfo> solutions, StateInfo initialState)
        {
            var states = new States();
            states.Enqueue(initialState);
            states.EnqueueEmpty();

            #region Initialize parameters info 
            var prmInfoByNdx = new PrmInfo[prmIndex.Count];
            {
                foreach (var p in prmIndex)
                    prmInfoByNdx[p.Value] = new PrmInfo() { prmName = p.Key, ndxResFuncs = new List<int>() };

                for (int iFunc = accessibleFuncs.Count - 1; iFunc >= 0; iFunc--)
                    foreach (int ndx in prmIndex.GetSome(accessibleFuncs[iFunc].pureOuts))
                        prmInfoByNdx[ndx].ndxResFuncs.Add(iFunc);
            }
            #endregion

            var lastState = initialState;

            while (true)
            {
                var state = states.Dequeue();
                if (state == StateInfo.Empty)
                {
                    // level increase mark found
                    if (states.Count == 0 || solutions.Count > 0)
                    { state = null; break; }
                    states.EnqueueEmpty();
                    continue;
                }

                int iLastFunc = state.iLastFunc;
                int[] orderedUnknownsIndexes = state.unknowns.EnumOnesIndexes()
                    .OrderBy(i => prmInfoByNdx[i].ndxResFuncs.Where(j => j < iLastFunc).Max())
                    .ToArray();

                #region Search new state loop
                for (int i = state.iLastFunc - 1; i >= 0; i--)
                {
                    var funcInfo = accessibleFuncs[i];
                    var outs = prmIndex.GetSome(funcInfo.pureOuts);
                    if (!state.unknowns.ContainsAny(outs))
                        // no any required value at output of func
                        continue;

                    int maxOutIndex, maxInpIndex;
                    outs = prmIndex.GetAll(funcInfo.pureOuts, out maxOutIndex);
                    var inps = prmIndex.GetAll(funcInfo.inputs, out maxInpIndex);
                    var newKnowns = new BitSet(state.knowns, outs, Math.Max(maxInpIndex, maxOutIndex));
                    var newUnknowns = new BitSet(state.unknowns, inps, outs);
                    newUnknowns.AndNot(newKnowns);
                    var newState = new StateInfo() { prevState = state, fi = funcInfo, knowns = newKnowns, unknowns = newUnknowns, iLastFunc = i };
                    if (states.Contains(newState))
                        // equal state already reached
                        continue;
                    // new state reached
                    if (newUnknowns.AllBitsClear())
                        // new solution found
                        solutions.Add(newState);
                    lastState = newState;
                    states.Enqueue(newState);
                    //break;
                }
                #endregion
            }
            return lastState;
        }

        private static StateInfo SolutionReversedSearch(List<FuncInfo> accessibleFuncs, Index valuesIndex, List<StateInfo> solutions, StateInfo firstState)
        {
            var states = new States();
            states.Enqueue(firstState);
            states.EnqueueEmpty();

            var lastState = firstState;

            while (true)
            {
                var state = states.Dequeue();
                if (state == StateInfo.Empty)
                {   // level increase mark found
                    if (states.Count == 0 || solutions.Count > 0)
                    { state = null; break; }
                    states.EnqueueEmpty();
                    continue;
                }

                #region Search new state loop
                for (int i = state.iLastFunc - 1; i >= 0; i--)
                {
                    var funcInfo = accessibleFuncs[i];
                    //if (state.ContainsCall(funcInfo))
                    //	continue;
                    var outs = valuesIndex.GetSome(funcInfo.pureOuts);
                    if (!state.unknowns.ContainsAny(outs))
                        // no any required value at output of func
                        continue;

                    int maxOutIndex, maxInpIndex;
                    outs = valuesIndex.GetAll(funcInfo.pureOuts, out maxOutIndex);
                    var inps = valuesIndex.GetAll(funcInfo.inputs, out maxInpIndex);
                    var newKnowns = new BitSet(state.knowns, outs, Math.Max(maxInpIndex, maxOutIndex));
                    var newUnknowns = new BitSet(state.unknowns, inps, outs);
                    newUnknowns.AndNot(newKnowns);
                    var newState = new StateInfo() { prevState = state, fi = funcInfo, knowns = newKnowns, unknowns = newUnknowns, iLastFunc = i };
#if DEBUG
                    newState.index = valuesIndex;
#endif
                    if (states.Contains(newState))
                        // equal state already reached
                        continue;
                    // new state reached
                    if (newUnknowns.AllBitsClear())
                        // new solution found
                        solutions.Add(newState);
                    lastState = newState;
                    states.Enqueue(newState);
                    break;
                }
                #endregion
            }
            return lastState;
        }

        public const string optionSolverAliases = "#SolverAliases";

        struct SrcNdx
        {
            public int iFunc;
            public int iOutput;
            //public int iCache;
            public override string ToString() { return string.Format("{0}, {1}", iFunc, iOutput); }
        }

        static Expr _ResJoinExpr(Dictionary<int, bool> usedResultsDict, List<CallInfo> callInfos, int n, string[] outs)
        {
            bool mayNeedInnerKeys;// = true;// n < funcInfos.Count;
            {
                if (n < callInfos.Count)
                {
                    var fin = callInfos[n].funcInfo;
                    var fd = fin.fd;
                    var IsVectorFuncSignature = fd != null && fin.fd.maxArity == 1 && fin.fd.flagsArgCanBeVector == 1u;
                    mayNeedInnerKeys = !IsVectorFuncSignature;
                }
                else mayNeedInnerKeys = true;
            }
            string[] orderedInnerKeys = null;
            IEnumerable<int> usedResults;
            {
                var innerKeys = new Dictionary<string, bool>();
                var neededForInputs = new Dictionary<string, bool>();
                var neededForResult = outs.ToDictionary(s => s, s => true);
                var usedOuts = new Dictionary<string, bool>(neededForResult);
                usedResults = Enumerable.Range(0, n).Reverse().Where(ndx =>
                {
                    var fi = callInfos[ndx].funcInfo;
                    bool used;
                    if (fi.IsSingleInputFunc || fi.inputs.Length == 0)
                        used = fi.pureOuts.Any(s => neededForResult.ContainsKey(s) || innerKeys.ContainsKey(s));
                    else if (fi.pureOuts.Any(s => neededForInputs.ContainsKey(s) || neededForResult.ContainsKey(s)))
                    {
                        used = true;
                        foreach (var s in fi.inputs)
                            neededForInputs[s] = true;
                        if (mayNeedInnerKeys)
                        {
                            foreach (var s in fi.pureOuts)
                                if (usedOuts.ContainsKey(s))
                                    usedOuts.Remove(s);
                            foreach (var s in fi.inOuts)
                                innerKeys[s] = true;
                        }
                    }
                    else used = false;
                    if (used)
                    {
                        usedResultsDict[ndx] = true;
                        foreach (var s in fi.pureOuts)
                        {
                            neededForInputs.Remove(s);
                            neededForResult.Remove(s);
                        }
                    }
                    return used;
                }).Reverse().ToArray();

                if (mayNeedInnerKeys)
                    orderedInnerKeys = usedResults.SelectMany(
                        ndx => callInfos[ndx].funcInfo.inOuts
                            .Where(s => innerKeys.ContainsKey(s))
                            .Select(s => { innerKeys.Remove(s); return s; })
                        ).ToArray();
            }

            //
            var resInfos = new ArrayExpr(usedResults.Select(ndx => new ReferenceExpr("RI" + ndx.ToString())).ToArray());
            var outFieldsDict = outs.ToDictionary(s => s, s => true);
            if (mayNeedInnerKeys)
            {
                mayNeedInnerKeys = orderedInnerKeys.Any(s => !outFieldsDict.ContainsKey(s));
                foreach (var s in orderedInnerKeys.Reverse())
                    outFieldsDict[s] = true;
            }
            var outFields = new ArrayExpr(usedResults.Select(
                    ndx => new ArrayExpr(
                        callInfos[ndx].funcInfo.outputs.Where(key =>
                        {
                            if (outFieldsDict.ContainsKey(key))
                                return true;
                            else return false;
                        }).Select(key =>
                        {
                            outFieldsDict.Remove(key);
                            return new ConstExpr(key);
                        }).ToArray())
                ).ToArray());
            IEnumerable<string> lstOuts;
            if (mayNeedInnerKeys)
            {
                lstOuts = outs.Union(orderedInnerKeys);
                if (n < callInfos.Count)
                    callInfos[n].funcInfo = callInfos[n].funcInfo.WithExtraKeys(orderedInnerKeys);
                //else lstOuts = outs;
            }
            else lstOuts = outs;
            //var dictOuts = lstOuts.ToDictionary(s => s, s => true);
            var outExpr = new CallExpr(SolverResJoin, resInfos, outFields,
                new ArrayExpr(lstOuts.Select(s => new ConstExpr(s)).ToArray()),
                new ArrayExpr((orderedInnerKeys == null) ? new ConstExpr[0] : orderedInnerKeys.Select(s => new ConstExpr(s)).ToArray())
            );
            return outExpr;
        }

        public const string optionSolverDependencies = "#SolverDependencies";
        public const string optionSolverFuncInfos = "#SolverFuncInfos";

        class CallInfo
        {
            public FuncInfo funcInfo;
            public long inputsMask;
            public int cachingNdx;
            public override string ToString() { return string.Format("C{0}: {1}", cachingNdx, funcInfo); }
        }

        static int uniqCounter = 0;

        public const string sDepsFuncNamePrefix = "#FuncName=";

        static Expr DecodeSolution(IList<FuncInfo> callsList, string[] externalValues, IList<string[]> outputSets, Generator.Ctx ctx,
            SolverAliases aliases, FuncInfo[] cachingInfoFuncs)
        {
            var valueIndex = new Index();
            var valueSources = new List<List<SrcNdx>>();
            var callInfos = new List<CallInfo>();
            // trace calls
            for (int k = 0; k < callsList.Count; k++)
            {
                var fi = callsList[k];
                long inpsBits = 0;
                foreach (var arg in fi.inputs)
                {
                    int i = valueIndex.Get(arg);
                    if (valueSources.Count == i)
                        valueSources.Add(new List<SrcNdx>(3));
                    inpsBits |= 1L << i;
                }
                int iFunc = callInfos.Count;
                for (int iOutput = fi.outputs.Length - 1; iOutput >= 0; iOutput--)
                {
                    var s = fi.outputs[iOutput];
                    int j = valueIndex.Get(s);
                    var srcNdx = new SrcNdx() { iFunc = iFunc, iOutput = iOutput };
                    if (valueSources.Count == j)
                    {
                        var lst = new List<SrcNdx>(3); lst.Add(srcNdx);
                        valueSources.Add(lst);
                    }
                    else valueSources[j].Add(srcNdx);
                }
                callInfos.Add(new CallInfo() { funcInfo = fi, inputsMask = inpsBits });
            }
            // mark input values as not having a source
            foreach (var inp in externalValues)
            {
                int i = valueIndex.Get(inp);
                if (i < valueSources.Count)
                    valueSources[i].Insert(0, new SrcNdx() { iFunc = -1 });
            }
            // determine applicable caching funcs
            CallInfo[] cachingInfos = cachingInfoFuncs.Select(fi =>
            {
                var ndxs = valueIndex.TryGetAll(fi.inputs);
                long bits;
                if (ndxs == null)
                    bits = -1L;
                else
                {
                    if (ndxs.Any(j => j >= 63))
                        throw new NotImplementedException("Solver.DecodeSolution.caching: index of value is too big for current implementation");
                    bits = ndxs.Aggregate(0L, (akk, j) => akk |= 1L << j);
                }
                return new CallInfo() { funcInfo = fi, cachingNdx = -1, inputsMask = bits };
            }).Where(ci => ci.inputsMask >= 0).OrderBy(ci => ci.inputsMask).ToArray();

            #region Determine cachingNdx for each function call
            {
                int maxNdx = -1;
                for (int i = 0; i < callInfos.Count; i++)
                {
                    var ci = callInfos[i];
                    if (maxNdx < cachingInfos.Length - 1)
                    {
                        var fi = ci.funcInfo;
                        int jMax = -1;
                        if (!fi.IsSingleInputFunc && fi.fd != null && fi.fd.cachingExpiration != TimeSpan.Zero)
                        {
                            long max = -1;
                            long bits = ci.inputsMask;
                            for (int j = 0; j < cachingInfos.Length; j++)
                            {
                                long cfiBits = cachingInfos[j].inputsMask;
                                var mask = bits & cfiBits;
                                if (mask == 0 && cfiBits != 0)
                                    continue;
                                if (cfiBits > max)
                                {
                                    max = cfiBits;
                                    jMax = j;
                                }
                            }
                        }
                        if (maxNdx < jMax)
                            maxNdx = jMax;
                    }
                    ci.cachingNdx = maxNdx;
                }
            }
            #endregion
            #region generate result expression
            int n = callInfos.Count;
            callInfos.AddRange(cachingInfos);
            var letsR = new Expr[n];
            var lets = new List<Expr>();
            var cacheLets = new List<Expr>();
            lets.Add(ConstExpr.Null);
            var cacheDomsDict = new Dictionary<int, Expr>();
            var usedResultsDict = new Dictionary<int, bool>();
            var domainsDict = new Dictionary<string, Expr>();
            //Expr exprCacheDomain = null;
            #region Declare functions results values, 'R#'
            for (int i = 0; i < n; i++)
            {
                Expr[] args = GetCallArgs(cachingInfos.Length - 1, valueIndex, valueSources, callInfos, usedResultsDict, i);

                var callInfo = callInfos[i];
                var funcInfo = callInfo.funcInfo;

                var call = new CallExpr(funcInfo.name, args);
                var fd = funcInfo.fd;
                if (fd != null && fd.cachingExpiration != TimeSpan.Zero)
                {
                    Expr exprCacheSubdomain = null;
                    var domain = fd.cacheSubdomain ?? string.Empty;
                    if (!domainsDict.TryGetValue(domain, out exprCacheSubdomain))
                    {
                        int ndx = ctx.IndexOf(W.Expressions.FuncDefs_Core.optionCachingDomainName + domain);
                        if (ndx > 0)
                            exprCacheSubdomain = ctx[ndx] as Expr;
                        if (exprCacheSubdomain == null)
                        { }
                        else if (!exprCacheSubdomain.IsNull)
                        {
                            var cacheDomainPrefixRef = new ReferenceExpr("#cacheDomainPrefix:" + domain);
                            lets.Add(CallExpr.let(cacheDomainPrefixRef, exprCacheSubdomain));
                            exprCacheSubdomain = cacheDomainPrefixRef;
                        }
                        domainsDict.Add(domain, exprCacheSubdomain);
                    }
                    if (exprCacheSubdomain != ConstExpr.Null)
                    {   // if caching is not disabled
                        Expr exprCacheDom = null;
                        int cNdx = callInfo.cachingNdx;
                        if (cNdx >= 0 && !cacheDomsDict.TryGetValue(cNdx, out exprCacheDom))
                        {
                            var re = new ReferenceExpr("#CacheDom" + System.Threading.Interlocked.Increment(ref uniqCounter).ToString());//cNdx.ToString());
                            var cacheInfoFuncCall = new CallExpr(cachingInfos[cNdx].funcInfo.name,
                                GetCallArgs(cachingInfos.Length - 1, valueIndex, valueSources, callInfos, usedResultsDict, n + cNdx));
                            cacheLets.Add(CallExpr.let(re, cacheInfoFuncCall));
                            cacheDomsDict.Add(cNdx, re);
                            exprCacheDom = re;
                        }
                        exprCacheSubdomain = exprCacheDom ?? exprCacheSubdomain;
                    }
                    else exprCacheSubdomain = null;
                    if (exprCacheSubdomain != null)
                    {
                        var subKey = (fd.cacheSubdomain == null) ? funcInfo.name : fd.cacheSubdomain + '.' + funcInfo.name;
                        call = new CallExpr(FuncDefs_Core.Cached,
                            new BinaryExpr(ExprType.Concat, exprCacheSubdomain, new ConstExpr(subKey)),  // cache key
                            call, // data to cache
                            ConstExpr.Null, // sliding expiration
                            new BinaryExpr(ExprType.Add, new CallExpr(FuncDefs_Excel.NOW), new ConstExpr(fd.cachingExpiration.TotalDays)) // absolute expiration = NOW + expiration_days
                        );
                    }
                }
                // let Ri
                {
                    var refRi = new ReferenceExpr('R' + i.ToString());
                    var let = CallExpr.let(refRi, call);
                    letsR[i] = let;
                }
            }
            callInfos.RemoveRange(n, cachingInfos.Length);
            #endregion
            var outputExprs = new List<Expr>();
            var dependenciesDict = (ctx.IndexOf(optionSolverDependencies) >= 0) ? new Dictionary<string, OPs.ListOfConst>() : null;
            foreach (var rawOuts in outputSets)
            {
                var outs = aliases.ToRealNames(rawOuts).Distinct().ToArray();
                string[] outVals;
                if (dependenciesDict != null)
                {
                    #region Fill in dependencies dictionary
                    // update global dependencies dictionary
                    var queue = new Queue<string>(outs.Where(prm => !dependenciesDict.ContainsKey(prm)));
                    while (queue.Count > 0)
                    {
                        var s = queue.Dequeue();
                        var src = valueSources[valueIndex.Get(s)][0];
                        if (src.iFunc <= 0)
                            continue;
                        var fi = callInfos[src.iFunc].funcInfo;
                        var lst = new OPs.ListOfConst(fi.inputs.Length + 1);
                        lst.Add(sDepsFuncNamePrefix + fi.name);
                        lst.AddRange(fi.inputs);
                        dependenciesDict[s] = lst;
                        foreach (var inp in fi.inputs.Where(prm => !dependenciesDict.ContainsKey(prm)))
                        {
                            dependenciesDict.Add(inp, null);
                            queue.Enqueue(inp);
                        }
                    }
                    // get list of all dependencies for outs
                    var allSrcsDict = outs.ToDictionary(s => s, s => true);
                    queue = new Queue<string>(outs);
                    while (queue.Count > 0)
                    {
                        var s = queue.Dequeue();
                        var src = valueSources[valueIndex.Get(s)][0];
                        if (src.iFunc <= 0) continue;
                        var fi = callInfos[src.iFunc].funcInfo;
                        foreach (var inp in fi.inputs.Where(prm => !allSrcsDict.ContainsKey(prm)))
                        {
                            allSrcsDict.Add(inp, true);
                            queue.Enqueue(inp);
                        }
                    }
                    #endregion
                    outVals = allSrcsDict.Keys.ToArray<string>();
                }
                else outVals = outs.Distinct().ToArray();
                outputExprs.Add(_ResJoinExpr(usedResultsDict, callInfos, n, outVals));
            }
            lets.AddRange(Enumerable.Range(0, n).SelectMany(j =>
            {
                if (usedResultsDict.ContainsKey(j))
                    return new Expr[]
                    {
                        letsR[j],
                        CallExpr.let(new ReferenceExpr("RI" + j.ToString()),new CallExpr(SolverResInfo, new ReferenceExpr('R' + j.ToString()), new ConstExpr(callInfos[j].funcInfo)))
                    };
                else return new Expr[] { letsR[j] };
            }));
            lets.AddRange(cacheLets);
            // dependencies
            if (dependenciesDict != null)
            {
                ctx[ctx.IndexOf(optionSolverDependencies)] = ValuesDictionary.New(
                    dependenciesDict.Keys.ToArray<string>(),            // string[] keys
                    dependenciesDict.Values.ToArray<OPs.ListOfConst>()  // object[] values
                );
            }
            {
                int i = ctx.IndexOf(optionSolverFuncInfos);
                if (i >= 0)
                    ctx[i] = callsList.ToDictionary(fi => fi.name, fi => fi);
            }
            var resultExpr = new CallExpr(FuncDefs_Core._block, new CallExpr(FuncDefs_Core.Bypass, lets.ToArray()), new ArrayExpr(outputExprs));
            #endregion
            return resultExpr;
        }

        private static Expr[] GetCallArgs(int maxCachingNdx, Index valueIndex,
            List<List<SrcNdx>> valueSources, List<CallInfo> callInfos, Dictionary<int, bool> usedResultsDict, int iCall)
        {
            Expr[] args;
            var ci = callInfos[iCall];
            var funcInfo = ci.funcInfo;
            var inps = funcInfo.inputs;
            int cachingFuncNdx = ci.cachingNdx;
            if (funcInfo.IsSingleInputFunc)
            {
                cachingFuncNdx = -1;
                args = new Expr[] { new ReferenceExpr(inps[0]) };
            }
            else if (funcInfo.NeedLinearizedArgs)
            {
                if (cachingFuncNdx < maxCachingNdx)
                {
                    var inpMax = inps.Max(s =>
                    {
                        int vndx = valueIndex.Get(s);
                        var srcs = valueSources[vndx];
                        SrcNdx srcNdx = (srcs.Count == 0) ? new SrcNdx() : srcs[0];
                        return (srcNdx.iFunc < 0) ? -1 : callInfos[srcNdx.iFunc].cachingNdx;
                    });
                    if (inpMax > cachingFuncNdx)
                        cachingFuncNdx = inpMax;
                }
                // func needs "linearized" args
                args = new Expr[] { _ResJoinExpr(usedResultsDict, callInfos, iCall, inps) };
            }
            else
            {   // func needs "as is" args
                args = new Expr[inps.Length];
                for (int j = 0; j < inps.Length; j++)
                {
                    var s = inps[j];
                    int vndx = valueIndex.Get(s);
                    var srcs = valueSources[vndx];
                    SrcNdx srcNdx;
                    if (srcs.Count == 0)
                        srcNdx = new SrcNdx() { iFunc = -1 };
                    else srcNdx = srcs[0];
                    if (srcNdx.iFunc == -1)
                        args[j] = new ReferenceExpr(s);
                    else
                    {
                        int iSrc = srcNdx.iFunc;
                        if (srcNdx.iFunc == iCall)
                            throw new SolverException("Can't find source for parameter: " + funcInfo.outputs[srcNdx.iOutput]);
                        int tmp = callInfos[srcNdx.iFunc].cachingNdx;
                        if (tmp > cachingFuncNdx)
                            cachingFuncNdx = tmp;
                        args[j] = new CallExpr(SolverAtCol, new ReferenceExpr('R' + iSrc.ToString()), new ConstExpr(srcNdx.iOutput));
                    }
                }
            }
            callInfos[iCall].cachingNdx = cachingFuncNdx;
            return args;
        }

        [Arity(2, 2)]
        public static object SolverAtCol(IList args)
        {
            var resObj = args[0];
            var index = args[1];
            string sndx;
            int i;
            if (index is int)
            {
                i = (int)index;
                sndx = null;
            }
            else
            {
                sndx = Convert.ToString(index);
                if (!int.TryParse(sndx, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out i))
                    i = -1;
            }
            var rows = resObj as IList<IIndexedDict>;
            if (rows != null)
            {
                if (rows.Count == 0)
                    return OPs.ListOfConst.Empty;
                var row0 = rows[0];
                if (i < 0) i = row0.Key2Ndx[sndx];
                var lst = new OPs.ListOfConst(rows.Count);
                foreach (var row in rows)
                    lst.Add(row.ValuesList[i]);
                return lst;
            }
            else
            {
                var row = resObj as IIndexedDict;
                if (row != null)
                {
                    if (i < 0) i = row.Key2Ndx[sndx];
                    return row.ValuesList[i];
                }
                //else if (i == 0)
                //	return resObj;
                var lst = resObj as IList;
                if (lst != null)
                {
                    if (lst.Count == 0)
                        return OPs.ListOfConst.Empty;
                    if (i == 0 && lst[0] != null && lst[0] as IList == null)
                        return resObj;
                    var res = new object[lst.Count];
                    for (int j = lst.Count - 1; j >= 0; j--)
                    {
                        var items = lst[j] as IList;
                        res[j] = (items == null || items.Count <= i) ? null : items[i];
                    }
                    return res;
                }
                else if (i == 0)
                    return resObj;
                throw new ArgumentException("SolverAtCol(res,ndx) can't determine kind of res: " + Convert.ToString(resObj));
            }
        }

        /// <summary>
        /// Special identity function to reinterpret input value as output of function
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        [Arity(1, 1)]
        public static object SolverInputFunc(CallExpr ce, Generator.Ctx ctx)
        {
            var call = new CallExpr(ce.funcName, ce.args[0], GetTimeRangeExpr(ctx));
            return Generator.Generate(call, ctx);
        }

        static Expr GetTimeRangeExpr(Generator.Ctx ctx)
        {
            if (ctx.IndexOf("A_TIME__XT") >= 0 && ctx.IndexOf("B_TIME__XT") >= 0)
                return new CallExpr(CreateTimedObject, new ReferenceExpr("A_TIME__XT"), new ReferenceExpr("B_TIME__XT"), ConstExpr.Zero);
            else if (ctx.IndexOf("AT_TIME__XT") >= 0)
            {
                var refDt = new ReferenceExpr("AT_TIME__XT");
                return new CallExpr(CreateTimedObject, refDt
                    , new BinaryExpr(ExprType.Add, refDt, new ConstExpr(TimeSpan.FromMilliseconds(1).TotalDays))
                    , ConstExpr.Zero
                );
            }
            else return ConstExpr.Null;
        }

        /// <summary>
        /// Special identity function to reinterpret input value as output of function
        /// </summary>
        public static object SolverInputFunc([CanBeVector]object data, object timeRange)
        {
            var to = timeRange as ITimedObject;
            var lst = data as IList;
            if (lst != null)
            {
                var r = new object[lst.Count];
                lst.CopyTo(r, 0);
                Array.Sort<object>(r, W.Common.Cmp.cmp);
                if (to != null)
                    for (int i = 0; i < r.Length; i++)
                        r[i] = TimedObject.TryAsTimed(r[i], to.Time, to.EndTime);
                return r;
            }
            else return (to == null) ? data : TimedObject.TryAsTimed(data, to.Time, to.EndTime);
        }

        [Arity(2, int.MaxValue)]
        public static object FindSolutionExpr(CallExpr ce, Generator.Ctx ctx)
        {
            var argObjs = ce.args.Select(e => Generator.Generate(e, ctx)).ToArray();
            if (OPs.MaxKindOf(argObjs) == ValueKind.Const)
                return FindSolutionExprImpl(argObjs, ctx);
            return (LazyAsync)(async aec =>
            {
                int n = argObjs.Length;
                var args = new object[n];
                for (int i = 0; i < n; i++)
                    args[i] = await OPs.ConstValueOf(aec, argObjs[i]);
                var lctx = new Generator.Ctx(ctx);
                var res = FindSolutionExprImpl(args, lctx);
                lctx.CheckUndefinedValues();
                return res;
            });
            //throw new NotImplementedException("FindSolutionExpr with nonconst args is not implemented");
        }

        static string GetValueName(string name)
        { return ValueInfo.Create(name).ToString(); }

        static string TryAsValueName(string name)
        {
            if (ValueInfo.IsDescriptor(name))
                return ValueInfo.Create(name).ToString();
            else return name.ToUpperInvariant();
        }

        static object FindSolutionExprImpl(object[] args, Generator.Ctx ctx)
        {
            object ins = args[0];
            System.Diagnostics.Trace.Assert(OPs.KindOf(ins) == ValueKind.Const, "FindSolutionExpr: args[0] must be constant array of strings or null");
            int n = args.Length - 1;
            var outputSets = new OPs.ListOfConst(n);
            for (int i = 1; i <= n; i++)
                outputSets.Add(args[i]);
            System.Diagnostics.Trace.Assert(OPs.MaxKindOf(outputSets) == ValueKind.Const, "FindSolutionExpr: args[1..n] must be constant arrays of strings or nulls");
            // collect names of inputs (all defined values)
            string[] inputs;
            {
                var defsDict = new Dictionary<string, bool>();
                var gc = ctx;
                while (gc != null)
                {
                    foreach (var pair in gc.name2ndx)
                        defsDict[TryAsValueName(pair.Key)] = true;
                    gc = gc.parent;
                }
                var lst = ins as IList;
                foreach (var name in lst)
                {
                    var s = Convert.ToString(name);
                    bool remove;
                    if (s.StartsWith("-"))
                    { remove = true; s = s.Substring(1); }
                    else remove = false;
                    s = TryAsValueName(s);
                    if (remove)
                        defsDict.Remove(s);
                    else defsDict[s] = true;
                }
                inputs = defsDict.Keys.ToArray<string>();
            }
            // get names of requested outputs
            string[] results;
            var outputs = new List<string[]>(outputSets.Count);
            {
                var defsDict = new Dictionary<string, bool>();
                foreach (IList lst in outputSets)
                {
                    var outps = new string[lst.Count];
                    for (int i = 0; i < outps.Length; i++)
                    {
                        var name = GetValueName(Convert.ToString(lst[i]));
                        outps[i] = name;
                        defsDict[name] = true;
                    }
                    outputs.Add(outps);
                }
                results = defsDict.Keys.ToArray<string>();
            }
            foreach (var solutionExpr in Solution(ctx.GetFunc(null, 0), inputs, results, outputs, ctx))
                return solutionExpr;
            var ex = new Generator.Exception(string.Format("FindSolutionExpr: solution not found\r\ninputs={0}\n\routputs={1}", string.Join(",", inputs), string.Join(",", results)));
            throw ex;
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
            var unusables = (arg2 == null) ? new Dictionary<string, string>() : ((IList)arg2).Cast<object>().Select(o => o.ToString()).ToDictionary(s => s);
            var aliasOf = GetAliases(ctx);
            var funcs = Dependecies.GetFuncInfos(ctx.GetFunc(null, 0), aliasOf, fi => !fi.inputs.Any(inp => unusables.ContainsKey(inp)));
            var dependencies = Dependecies.Find(funcs, aliasOf, int.MaxValue, srcPrms, depPrms);
            var resDict = new Dictionary<string, bool>();
            var resParams = (depPrms == null) ? dependencies.Keys.Except(srcPrms) : depPrms.Where(s => dependencies.ContainsKey(s));
            var res = new Dictionary<string, object>();
            foreach (var prmName in resParams)
            {
                var deps = dependencies[prmName];
                var srcs = srcPrms.Where(s => deps.ContainsKey(s)).ToArray();
                if (srcs.Length > 0)
                    res.Add(prmName, srcs);
            }
            return res;
        }

        [Arity(1, 1)]
        public static object ExprToExecutable(CallExpr ce, Generator.Ctx ctx)
        {
            var exprObj = Generator.Generate(ce.args[0], ctx);
            var kind = OPs.KindOf(exprObj);
            switch (kind)
            {
                case ValueKind.Const:
                    {
                        var expr = exprObj as Expr;
                        if (expr == null)
                            expr = Parser.ParseToExpr(Convert.ToString(expr));
                        return Generator.Generate(expr, ctx);
                    }
                default:
                    throw new SolverException("ExprToExecutable: expression must be constant");
                    //return (LazyAsync)(async aec =>
                    //{
                    //	var tmp = await OPs.ConstValueOf(aec, exprObj);
                    //	var lctx = new Generator.Ctx(ctx);
                    //	var expr = tmp as Expr;
                    //	if (expr == null)
                    //		expr = Parser.ParseToExpr(Convert.ToString(tmp));
                    //	var obj = Generator.Generate(expr, lctx);
                    //	lctx.CheckUndefinedValues();
                    //	return obj;
                    //});
            }
        }

        public static object TextToExpr(object text)
        {
            int i = 0;
            return Parser.ParseToExpr(Convert.ToString(text), ref i, new string[0], StringComparison.InvariantCultureIgnoreCase);
        }

        static IList<IIndexedDict> ToIndexedDicts(object arg0, FuncInfo funcInfo)
        {
            var dicts = arg0 as IList<IIndexedDict>;
            if (dicts != null)
                return dicts;
            int nFuncOuts = funcInfo.outputs.Length;
            var key2ndx = new Dictionary<string, int>(nFuncOuts);
            for (int i = 0; i < nFuncOuts; i++)
                key2ndx.Add(funcInfo.outputs[i], i);
            var rows = arg0 as IList;
            if (rows == null)
                // can't convert to IIndexedDict[n], return IIndexedDict[1]
                return new IIndexedDict[1] { ValuesDictionary.New(new object[1] { arg0 }, key2ndx) };
            //if (key2ndx.Count == 1)
            //    return arg0;
            var resLst = new OPs.ListOf<IIndexedDict>(rows.Count);
            if (rows.Count == 0)
                return resLst;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null)
                    continue;
                var dct = row as IIndexedDict;
                if (dct != null)
                {
                    resLst.Add(dct);
                    break;
                }
                var arr = row as object[];
                if (arr == null)
                {
                    var lst = row as IList<object>;
                    if (lst != null)
                    {
                        arr = new object[lst.Count];
                        for (int j = arr.Length - 1; j >= 0; j--) arr[j] = lst[j];
                    }
                    else
                    {
                        var ils = row as IList;
                        if (ils != null)
                        {
                            arr = new object[ils.Count];
                            for (int j = arr.Length - 1; j >= 0; j--) arr[j] = ils[j];
                        }
                        else
                        {
                            var en = row as IEnumerable;
                            if (en == null || row is string)
                            {
                                if (key2ndx.Count == 1)
                                    arr = new object[1] { row };
                                else throw new ArgumentException("ToIndexedDict: can't convert specified object");
                            }
                            else arr = en.Cast<object>().ToArray();

                        }
                    }
                }
                if (arr.Length < nFuncOuts)
                    System.Diagnostics.Trace.Assert(false);
                resLst.Add(new ValuesDictionary(arr, key2ndx, false));
            }
            var res = resLst.ToArray();
            if (funcInfo.inOuts.Length > 0)
            {
                var keysNdxs = funcInfo.inOuts.Select(s => key2ndx[s]).ToArray();
                Array.Sort<IIndexedDict>(res, (a, b) => a.ValuesList.CompareSameTimedKeys(b.ValuesList, keysNdxs));
            }
            return res;
        }

        /// <summary>
        /// (Internal) prepare ResultInfo
        /// </summary>
        /// <param name="args">0: vector of IIndexedDict or compatible structure :); 1: FuncInfo</param>
        /// <returns>ResultInfo</returns>
        [Arity(2, 2)]
        public static object SolverResInfo(IList args)
        {
            var funcInfo = (FuncInfo)args[1];
            var data = ToIndexedDicts(args[0], funcInfo);
            var res = new ResultInfo() { data = data, funcInfo = funcInfo };
            if (data.Count > 0)
                res.key2ndx = data[0].Key2Ndx;
            else
            {
                var funcOuts = funcInfo.outputs;
                var k2n = new Dictionary<string, int>(funcOuts.Length);
                for (int j = 0; j < funcOuts.Length; j++)
                    k2n[funcOuts[j]] = j;
                res.key2ndx = k2n;
            }

            return res;
        }

        /// <summary>
        /// Construct output table from partial results
        /// </summary>
        /// <param name="args">
        /// 0: list of partial results (ResultInfo[]); 1: fields names to output (string[][]); 2: string[] output fields in order of appearance
        /// 3: names of key fields to ensure uniqueness
        /// </param>
        /// <returns></returns>
        [Arity(3, 4)]
        public static object SolverResJoin(CallExpr ce, Generator.Ctx ctx)
        {
            Expr rangeArg = GetTimeRangeExpr(ctx);
            var args = ce.args;
            return Generator.Generate(new CallExpr(SolverResJoinImpl, args[0], args[1], args[2],
                (args.Count > 3) ? args[3] : ConstExpr.Null,
                rangeArg, new ConstExpr(GetAliases(ctx))
            ), ctx);
        }

        /// <summary>
        /// Construct output table from partial results
        /// </summary>
        /// <param name="args">
        /// 0: list of partial results (ResultInfo[]); 1: fields names to output (string[][]); 2: string[] output fields in order of appearance
        /// 3: names of key fields (non informative)
        /// 4: TimedObject range; 5: SolverAliases aliasOf
        /// </param>
        /// <returns></returns>
        [Arity(6, 6)]
        public static object SolverResJoinImpl(IList args)
        {
            ResultInfo[] results = (Utils.ToIList(args[0])).Cast<ResultInfo>().ToArray();
            string[][] resultFields = (Utils.ToIList(args[1])).Cast<IList>().Select(lst => lst.Cast<object>().Select(x => Convert.ToString(x)).ToArray()).ToArray();
            string[] outFieldsOrder = (Utils.ToIList(args[2])).Cast<object>().Select(x => Convert.ToString(x)).ToArray();
            Dictionary<string, int> key2ndx;// = Enumerable.Range(0, outFieldsOrder.Length).ToDictionary(i => outFieldsOrder[i], i => i);
            var arg3 = args[3];
            var arg4 = args[4];
            var aliasOf = (SolverAliases)args[5];
            string[] outFieldsOrderUniq;
            {
                int n = outFieldsOrder.Length;
                var lst = new List<string>(n);
                var k2n = new Dictionary<string, int>(n);
                foreach (var s in outFieldsOrder)
                {
                    int i;
                    if (k2n.TryGetValue(s, out i))
                        k2n.Add(s, i);
                    else
                    {
                        i = lst.Count;
                        string realName = null;
                        foreach (var name in aliasOf.RealNameAndAliasesOf(s))
                        {
                            if (realName == null)
                                realName = name;
                            k2n.Add(name, i);
                        }
                        lst.Add(realName);
                    }
                }
                outFieldsOrderUniq = lst.ToArray();
                key2ndx = k2n;
            }
            var range = (arg4 == null) ? TimedObject.FullRangeI : (ITimedObject)arg4;
            if (arg3 != null)
            {
                var keyFields = (Utils.ToIList(arg3)).Cast<object>().Select(key => Convert.ToString(key)).ToArray();
                var keyNdxs = keyFields.Select(key =>
                {
                    int ndx;
                    return key2ndx.TryGetValue(key, out ndx) ? ndx : -1;
                })
                .Where(i => i >= 0)
                .ToArray();
                /*/
                var re = new ResEnum(results, resultFields, outFieldsOrderUniq, keyNdxs);
                List<Timed<object[]>> lst;
                {
                    var set = new List<Timed<object[]>>();
                    var prev = Timed<object[]>.Empty;
                    foreach (var curr in re.EnumTimed(range))
                    {
                        //if (prev.value != null && keyNdxs.Length == 0 && prev.value.CompareTimed(curr.value) == 0)
                        //    continue;
                        if (prev.value != null
                            && (keyNdxs.Length == 0 || prev.value.CompareSameTimedKeys(curr.value, keyNdxs) == 0)
                            && prev.value.CompareTimed(curr.value) == 0)
                            continue;
                        set.Add(curr);
                        prev = curr;
                    }
                    lst = set.ToList();
                }
                if (keyNdxs.Length > 0 && lst.Count > 1)
                {   // sort and remove duplicates
                    lst.Sort((a, b) => a.value.CompareSameTimedKeys(b.value, keyNdxs));
                    int n = lst.Count;
                    int n1 = n - 1;
                    int nn = 1;
                    for (int i = 0; i < n1; i++)
                    {
                        var curr = lst[i];
                        var next = lst[i + 1];
                        if (next.value.CompareSameTimedKeys(curr.value, keyNdxs) == 0 && next.CompareTimed(curr) == 0)
                            lst[i] = Timed<object[]>.Empty;
                        else nn++;
                    }
                    var lstNN = new List<Timed<object[]>>(nn);
                    foreach (var item in lst)
                        if (!item.IsEmpty)
                            lstNN.Add(item);
                    lst = lstNN;
                }
                var res = lst.Select(r => new ValuesDictionary(r.value, key2ndx, r.time, r.endTime) as IIndexedDict).ToArray();
                return res;
                /*/
                var re = new ResultEnumerator(results, resultFields, outFieldsOrderUniq, keyNdxs);
                List<object[]> lst;
                {
                    ICollection<object[]> set;
                    set = new List<object[]>();
                    object[] prev = null;
                    foreach (var curr in re.EnumTimed(range))
                    {
                        //if (prev != null && keyNdxs.Length == 0 && prev.CompareTimed(curr) == 0)
                        //    continue;
                        if (prev != null
                            && (keyNdxs.Length == 0 || prev.CompareSameTimedKeys(curr, keyNdxs) == 0)
                            && prev.CompareTimed(curr) == 0)
                            continue;
                        set.Add(curr);
                        prev = curr;
                    }
                    lst = set.ToList();
                }
                if (keyNdxs.Length > 0 && lst.Count > 1)
                {   // sort and remove duplicates
                    lst.Sort((a, b) => a.CompareSameTimedKeys(b, keyNdxs));
                    int n = lst.Count;
                    int n1 = n - 1;
                    int nn = 1;
                    for (int i = 0; i < n1; i++)
                    {
                        var curr = lst[i];
                        var next = lst[i + 1];
                        if (next.CompareSameTimedKeys(curr, keyNdxs) == 0 && next.CompareTimed(curr) == 0)
                            lst[i] = null;
                        else nn++;
                    }
                    var lstNN = new List<object[]>(nn);
                    foreach (var item in lst)
                        if (item != null)
                            lstNN.Add(item);
                    lst = lstNN;
                }
#if DEBUG
                var maxRows = (results.Length == 0) ? 0 : results.Max(r => r.data.Count) * 2;
                if (lst.Count > maxRows && maxRows > 50)
                {
                    var L0 = lst.First();
                    int nCols = L0.Length;
                    var counts = new int[nCols];
                    object[] prev = null;
                    foreach (var curr in lst)
                    {
                        if (prev != null)
                            for (int i = 0; i < nCols; i++)
                                if (prev[i] != null && prev[i] == curr[i])
                                    counts[i]++;
                        prev = curr;
                    }
                    var msg = string.Join("\r\n", key2ndx.Select(p => p.Key + '\t' + counts[p.Value]));
                    //var msg2 = ValuesDictionary.IDictsToTimedStr(dups);
                    W.Common.Diagnostics.Logger.TraceInformation("SolverResJoinImpl: Data multiplication effect detected // {0}>{1}", lst.Count, maxRows);
                    W.Common.Diagnostics.Logger.TraceInformation("SolverResJoinImpl: " + msg);
                    //System.Diagnostics.Debug.WriteLine(W.Expressions.FuncDefs_Report._IDictsToStr(lst));
                    //lst.RemoveRange(maxRows, lst.Count - maxRows);
                }
#endif
                var res = lst.Select(items => ValuesDictionary.New(items, key2ndx) as IIndexedDict).ToArray();
                return res;
                //*/
            }
            else
            {
                var re = new ResultEnumerator(results, resultFields, outFieldsOrderUniq, null);
                var table = re.EnumTimed(range).Select(values => ValuesDictionary.New(values, key2ndx)).ToArray();
                return table;
            }
        }

        public static object WithoutTime(object obj)
        {
            var to = obj as ITimedObject;
            if (to != null)
                return to.IsEmpty ? null : to.Object;
            return obj;
        }

        [Arity(3, 3)]
        public static object CreateTimedObject(IList args)
        {
            var value = args[2];
            var to = value as ITimedObject;
            if (to != null)
                value = to.Object;
            var dtBeg = (args[0] == null) ? DateTime.MinValue : DateTime.FromOADate(Convert.ToDouble(args[0]));
            var dtEnd = (args[1] == null) ? DateTime.MaxValue : DateTime.FromOADate(Convert.ToDouble(args[1]));
            return TimedObject.Timed(dtBeg, dtEnd, value);
        }

        static FuncDef GetLookupFuncDef(string funcName, IEnumerable<string> keyFields, IEnumerable<string> valFields, Delegate func)
        {
            var intersection = keyFields.Intersect(valFields);
            if (intersection.Any())
                throw new SolverException(string.Format(
                    "letLookupFunc({0},...): detected intersection between keys and values [{1}]",
                    string.Join(",", intersection)
                ));
            var keys = keyFields.ToArray();
            var fd = new FuncDef(
                func,
                funcName,
                keys.Length, keys.Length,
                ValueInfo.CreateMany(keys),
                ValueInfo.CreateMany(valFields.Concat(keyFields).ToArray()), FuncFlags.Defaults | FuncFlags.isLookupFunc, 0, 0);
            return fd;
        }

        public static FuncDef GetSrcDataFuncDef(string funcName, IEnumerable<string> fields, object[][] data)
        {
            var func = (Macro)((expr, gctx) => data);
            return GetLookupFuncDef(funcName, new string[0], fields, func);
        }

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
            var k2n = new Dictionary<string, int>(row0.Key2Ndx);
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
        /// Объединяет несколько наборов данных по значениям ключей (по аналогии с FULL OUTER JOIN)
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        [Arity(3, int.MaxValue)]
        public static object Solver_FullJoinIDicts(IList args)
        {
            int n = (args.Count - 2) / 2;
            var joinKeys = ((IList)args[0]).Cast<object>().Select(k => k.ToString()).ToArray();
            var aliasOf = (SolverAliases)args[1];
            var outputFields = new string[n + 1][];
            outputFields[0] = joinKeys;
            Dictionary<string, int> Key2Ndx = Enumerable.Range(0, joinKeys.Length).ToDictionary(i => joinKeys[i], i => i);
            var resultKey2Ndx = aliasOf.GetKey2Ndx_WithAllNames(Key2Ndx);
            var keysCmp = new W.Common.KeysComparer(Enumerable.Range(0, joinKeys.Length).ToArray());
            var allKeys = new HashSet<object[]>(keysCmp);
            var infos = new ResultInfo[n + 1];
            for (int i = 0; i < n; i++)
            {
                var dataLst = (IList<IIndexedDict>)args[2 + i * 2];
                var data = dataLst as IIndexedDict[] ?? dataLst.ToArray();
                string[] fields;
                if (data.Length > 0)
                    fields = aliasOf.GetKey2Ndx_OnlyRealNames(data[0].Key2Ndx).Keys.ToArray<string>();
                else
                    fields = ((IList)args[2 + i * 2 + 1]).Cast<object>().Select(o => Convert.ToString(o)).ToArray();
                var prefix = i.ToString() + '_';
                var pureOuts = fields.Except(joinKeys).ToArray();
                var fi = new FuncInfo(prefix + "@Solver_ResFunc", joinKeys, fields.Select(s => joinKeys.Contains(s) ? s : prefix + s).ToArray());
                var ri = new ResultInfo()
                {
                    data = data,
                    funcInfo = fi,
                    key2ndx = Enumerable.Range(0, fi.outputs.Length).ToDictionary(j => fi.outputs[j], j => j)
                };
                infos[i + 1] = ri;
                var prefOuts = pureOuts.Select(s => prefix + s).ToArray();
                outputFields[i + 1] = prefOuts;
                for (int j = 0; j < pureOuts.Length; j++)
                {
                    int k = Key2Ndx.Count;
                    Key2Ndx.Add(prefOuts[j], k);
                    foreach (var s in aliasOf.RealNameAndAliasesOf(pureOuts[j]))
                        resultKey2Ndx.Add(prefix + s, k);
                }
                var keysNdxs = joinKeys.Select(s => ri.key2ndx[s]).ToArray();
                var dataKeys = data.Select(row =>
                {
                    var res = new object[keysNdxs.Length];
                    for (int j = res.Length - 1; j >= 0; j--)
                        res[j] = row.ValuesList[keysNdxs[j]];
                    return res;
                }).ToArray();
                Array.Sort(dataKeys, data, keysCmp);
                allKeys.UnionWith(dataKeys);
            }
            {   // create full list of possible keys (full join)
                var fi = new FuncInfo("@Solver_Keys", joinKeys, joinKeys);
                var keys = allKeys.ToArray();
                Array.Sort(keys, keysCmp);
                var k2n = Enumerable.Range(0, joinKeys.Length).ToDictionary(i => joinKeys[i], i => i);
                infos[0] = new ResultInfo()
                {
                    data = keys.Select(lst => ValuesDictionary.New(lst, k2n)).ToArray(),
                    funcInfo = fi,
                    key2ndx = k2n
                };
            }
            var outFieldsOrder = Key2Ndx.Keys.ToArray<string>();
            var re = new ResultEnumerator(infos, outputFields, outFieldsOrder, null);
            var table = re.EnumNonTimed().Select(values => ValuesDictionary.New(values, resultKey2Ndx)).ToArray();
            return table;
        }

        static List<TR> FullOuterJoin<TA, TB, TK, TR>(
                this IEnumerable<TA> a,
                IEnumerable<TB> b,
                Func<TA, TK> selectKeyA,
                Func<TB, TK> selectKeyB,
                Func<TA, TB, TK, TR> projection,
                TA defaultA = default(TA),
                TB defaultB = default(TB),
                IEqualityComparer<TK> cmp = null)
        {
            cmp = cmp ?? EqualityComparer<TK>.Default;
            var alookup = a.ToLookup(selectKeyA, cmp);
            var blookup = b.ToLookup(selectKeyB, cmp);

            var keys = new HashSet<TK>(alookup.Select(p => p.Key), cmp);
            keys.UnionWith(blookup.Select(p => p.Key));

            var join =
                from key in keys
                from xa in alookup[key].DefaultIfEmpty(defaultA)
                from xb in blookup[key].DefaultIfEmpty(defaultB)
                select projection(xa, xb, key);

            return join.ToList();
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
                var dependence = new Dictionary<string, Dictionary<string, int>>();

                // dependencies with "distance" = 1
                foreach (var fi in availableFunctions)
                    foreach (var value in fi.pureOuts)
                    {
                        var valueDeps = aliases.AtKey(dependence, value, null);
                        if (valueDeps == null)
                        {
                            valueDeps = new Dictionary<string, int>();
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


                    var nextq = new Dictionary<string, bool>(dependence.Count);
                    foreach (var value in (dependentValues != null) ? dependentValues.Distinct() : dependence.Keys)
                        nextq[value] = true;

                    var queue = new Dictionary<string, bool>(dependence.Count);

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
                                        }
                                    }
                            }
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
    }
}
