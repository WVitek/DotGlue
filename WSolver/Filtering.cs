using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace W.Expressions
{
    using W.Common;

    internal class Filter
    {
        struct StageInfo
        {
            /// <summary>
            /// Параметры, используемые на этой стадии (в условиях и/или выходные)
            /// </summary>
            public string[] ownParams;
            /// <summary>
            /// Условие фильтрации на стадии
            /// </summary>
            public Expr Cond;
        }

        static Dictionary<string, bool> AllSrcFuncs(IEnumerable<string> dstFuncs, Dictionary<string, Tuple<string[], List<string>>> funcInpsOuts, Dictionary<string, bool> readyFuncs = null)
        {
            if (readyFuncs == null)
                readyFuncs = new Dictionary<string, bool>();
            var funcsQueue = new Queue<string>();
            foreach (var inp in dstFuncs)
            {
                funcsQueue.Enqueue(inp);
                readyFuncs.Add(inp, true);
            }
            while (funcsQueue.Count > 0)
            {
                var func = funcsQueue.Dequeue();
                foreach (var inp in funcInpsOuts[func].Item1.Where(f => !readyFuncs.ContainsKey(f)))
                {
                    funcsQueue.Enqueue(inp);
                    readyFuncs.Add(inp, true);
                }
            }
            return readyFuncs;
        }

        const int defaultMinPartSize = 200;
        const int defaultMaxPartSize = 2000;

        public static object Filtering(Generator.Ctx ctx, string inputParam, string sourceData, IList<Expr> filterConditions, string[] outputParams)
        {
            var aliasOf = FuncDefs_Solver.GetAliases(ctx);

            // количество стадий = количество условий фильтрации + 1 стадия подготовки выходных данных
            int nStages = filterConditions.Count + 1;

            // для каждой стадии перечень параметров, необходимых для её обработки
            var stage = new StageInfo[nStages];

            // перечень всех параметров всех стадий
            var usedParamsDict = new Dictionary<string, bool>();

            #region Определяем используемые на стадиях обработки данных показатели (стадии фильтрации + стадия подготовки выходных данных)

            // проход по стадиям фильтрации с формированием перечней используемых параметров
            for (int i = 0; i < nStages - 1; i++)
            {
                var prmsDict = new Dictionary<string, bool>();
                foreach (var paramName in filterConditions[i].EnumerateReferences().Where(ValueInfo.IsDescriptor))
                    prmsDict[aliasOf.GetRealName(paramName)] = true;
                var ownParams = prmsDict.Keys.ToArray<string>();
                stage[i].ownParams = ownParams;
                stage[i].Cond = filterConditions[i];
                foreach (var s in ownParams)
                    usedParamsDict[aliasOf.GetRealName(s)] = true;
            }

            string[] outputParamsRealNames = outputParams.Select(s => aliasOf.GetRealName(s)).ToArray();

            // стадия выходных данных
            stage[nStages - 1].ownParams = outputParamsRealNames;
            foreach (var s in outputParamsRealNames)
                usedParamsDict[s] = true;
            #endregion

            // справочник "параметр -> оценка сложности"
            var funcComplexity = new Dictionary<string, int>();

            //// справочник "параметр -> влияющие на него параметры"
            //IDictionary<string, string[]> dependencies;

            // справочник "параметр -> имя вычисляющей его функции"
            IDictionary<string, string> param2func;

            // справочник "функция -> входы / выходы"
            var funcInpsOuts = new Dictionary<string, Tuple<string[], List<string>>>();

            #region Определяем зависимости и "сложность" параметров
            {
                var localCtx = new Generator.Ctx(ctx);
                localCtx.GetOrCreateIndexOf(FuncDefs_Solver.optionSolverDependencies);
                var res = Generator.Generate(
                    // вызываем функцию поиска решения, 
                    // которая при наличии optionSolverDependencies возвращает также справочник зависимостей
                    new CallExpr(FuncDefs_Solver.FindSolutionExpr
                        // перечень дополнительных входных параметров (которые в месте этого вызова не определены)
                        , new ArrayExpr(new ConstExpr(inputParam))
                        // перечень выходных параметров, решение для которых (последовательность вызовов известных функций) надо найти
                        , new ArrayExpr(usedParamsDict.Keys.Select(s => new ConstExpr(s)).ToArray())
                    )
                    // контекст кодогенератора для поиска решения (содержит перечень имён известных значений и описаний функций)
                    , localCtx
                );
                var deps = (IDictionary<string, object>)localCtx.values[localCtx.IndexOf(FuncDefs_Solver.optionSolverDependencies)];

                //dependencies = deps.ToDictionary(p => p.Key, p => (p.Value == null) ? new string[0] : ((IList)p.Value).Cast<string>().Where(s => !s.StartsWith("#")).ToArray());
                param2func = deps.ToDictionary(
                    p => p.Key,
                    p => (p.Value == null) ? string.Empty
                        : ((IList)p.Value).Cast<string>().Where(s => s.StartsWith(FuncDefs_Solver.sDepsFuncNamePrefix)).First()
                );

                foreach (var d in deps)
                {
                    var prm = d.Key;
                    var arguments = (IList)d.Value;
                    if (arguments == null)
                        arguments = new string[] { string.Empty };
                    Tuple<string[], List<string>> fInpsOuts;
                    var sFunc = arguments[0].ToString();
                    if (!funcInpsOuts.TryGetValue(sFunc, out fInpsOuts))
                    {
                        var inpFuncs = arguments.Cast<string>()
                            .Skip(1) // first arg is function name
                            .Select(s => param2func[s]) // get function name by her output parameter name
                            .Where(f => f != null)
                            .Distinct().ToArray();
                        fInpsOuts = new Tuple<string[], List<string>>(inpFuncs, new List<string>());
                        funcInpsOuts[sFunc] = fInpsOuts;
                    }
                    fInpsOuts.Item2.Add(prm);
                }

                var lstAllFuncs = funcInpsOuts.Keys.ToList();

                bool someToDo = true;
                while (someToDo)
                {
                    someToDo = false;
                    for (int i = lstAllFuncs.Count - 1; i >= 0; --i)
                    {
                        var func = lstAllFuncs[i];
                        int cmpl = 0;
                        foreach (var s in funcInpsOuts[func].Item1)
                        {
                            int c;
                            if (!funcComplexity.TryGetValue(s, out c))
                            { cmpl = -1; break; }
                            else cmpl += c;
                        }
                        if (cmpl >= 0)
                        {
                            funcComplexity.Add(func, cmpl + 1);
                            lstAllFuncs.RemoveAt(i);
                            someToDo = true;
                        }
                    }
                }
                System.Diagnostics.Trace.Assert(lstAllFuncs.Count == 0, "Filtering: lstAllFuncs.Count == 0");
            }
            #endregion


            {   // сортировка стадий по возрастанию "сложности" параметров
                int[] reorder = Enumerable.Range(0, nStages - 1)
                    .OrderBy(i => (stage[i].ownParams.Length == 0)
                        ? 0
                        : stage[i].ownParams.Select(s => param2func[s]).Distinct().Select(f => funcComplexity[f]).Sum())
                    .Concat(new int[] { nStages - 1 })
                    .ToArray();
                Array.Sort(reorder, stage);
            }

            #region Проход по стадиям
            {
#if SAFE_PARALLELIZATION
                bool inParallel = ctx.IndexOf(FuncDefs_Core.stateParallelizationAlreadyInvolved) < 0;
#else
				const bool inParallel = true;
#endif
                if (ctx.IndexOf(FuncDefs_Core.optionMinPartSize) < 0)
                    ctx.CreateValue(FuncDefs_Core.optionMinPartSize, defaultMinPartSize);

                if (ctx.IndexOf(FuncDefs_Core.optionMaxPartSize) < 0)
                    ctx.CreateValue(FuncDefs_Core.optionMaxPartSize, defaultMaxPartSize);

                var readyFuncs = new Dictionary<string, bool>();
                readyFuncs.Add(string.Empty, true);
                var prevDataName = sourceData;
                var interstageParams = new string[] { inputParam };
                var lstStages = new List<StageInfo>(stage);
                var readyStages = new List<StageInfo>();
                var mergedStagesCode = new List<string>();
                int iMergedStage = 0;
                while (lstStages.Count > 0 || readyStages.Count > 0)
                {
                    for (int i = lstStages.Count - 1; i >= 0; i--)
                        if (lstStages[i].ownParams.All(prm => readyFuncs.ContainsKey(param2func[prm])))
                        {
                            readyStages.Add(lstStages[i]);
                            lstStages.RemoveAt(i);
                        }
                    if (readyStages.Count > 0)
                    {
                        var conds = readyStages.Select(stg => stg.Cond)
                            .Where(cond => cond != null)
                            .OrderBy(cond => cond.Traverse(e => (e is CallExpr || e is ReferenceExpr) ? 1 : 0).Sum())
                            .ToArray();
                        bool lastStage = conds.Length == 0;
                        Dictionary<string, bool> flowOfParams = readyStages.Concat(lstStages).SelectMany(si => si.ownParams).Distinct()
                            .ToDictionary(s => s, s => true);
                        var ownParamsFuncs = readyStages
                            .SelectMany(stg => stg.ownParams.Select(prm => param2func[prm])
                            .Where(func => !readyFuncs.ContainsKey(func)))
                            .Distinct().ToArray();
                        var paramsToGetEnum =
                            // все выходные параметры готовых функций (источников данных)
                            readyFuncs.Keys.SelectMany(f => funcInpsOuts[f].Item2).Distinct()
                            // оставляем только те, что нужны на этой и последующих стадиях, а также ID
                            .Where(s => s.EndsWith("_ID") || s.Contains("_ID_") || flowOfParams.ContainsKey(s))
                            //// показатели оставляем в той форме, которая запрошена (например, в списке есть LIQUID_WATERCUT_OISP и его алиас LIQUID_WATERCUT, а запрошен именно LIQUID_WATERCUT)
                            //.Select(s => aliasOf.AsIn(flowOfParams, s))
                            ;
                        //if (lastStage)
                        paramsToGetEnum = paramsToGetEnum.Distinct();
                        //else
                        //	paramsToGetEnum = paramsToGetEnum.Distinct(aliasOf);
                        var paramsToGet = paramsToGetEnum.ToArray();
                        var condExpr = lastStage ? null : (conds.Length == 1) ? conds[0] : new CallExpr("AND", conds);
                        var code = GetConditionStageText(prevDataName,
                            lastStage ? interstageParams : interstageParams.Distinct(),
                            paramsToGet, condExpr, (iMergedStage == 0), inParallel);
                        prevDataName = "stgRows_" + iMergedStage.ToString();
                        code = string.Format("{0}\r\n..let({1}),", code, prevDataName);
                        if (lastStage)
                            code += string.Format("\r\nPROGRESS('FilteringItemsList',{0}),\r\n", prevDataName);
                        mergedStagesCode.Add(code);
                        iMergedStage++;
                        interstageParams = paramsToGet.Distinct().ToArray();
                        readyStages.Clear();
                    }
                    else
                    {
                        var stg = lstStages[0];
                        lstStages.RemoveAt(0);
                        readyStages.Add(stg);
                        AllSrcFuncs(
                            stg.ownParams.Select(prm => param2func[prm]).Where(func => !readyFuncs.ContainsKey(func)).Distinct()
                            , funcInpsOuts, readyFuncs);
                    }
                }
                var sbCode = new StringBuilder();
                sbCode.AppendLine("(");
                sbCode.AppendFormat("PROGRESS('FilteringStagesCount',{0}),", mergedStagesCode.Count);
                sbCode.AppendLine();
                foreach (var code in mergedStagesCode)
                    sbCode.AppendLine(code);
                sbCode.AppendFormat("PROGRESS('FilteringComplete',{0}.COLUMNS()),", prevDataName);
                sbCode.AppendLine();
                sbCode.AppendLine(prevDataName);
                sbCode.AppendLine(")");
                var sCode = sbCode.ToString();
                return Parser.ParseToExpr(sCode);
            }
            #endregion
        }

        public static T ReplaceValueInfoRefsWithData<T>(T expr, string dataSrcName, Dictionary<string, object> dictValues = null) where T : Expr
        {
            var dataRef = new ReferenceExpr(dataSrcName);
            T res = (T)expr.Visit(Expr.RecursiveModifier(e =>
            {
                var refExpr = e as ReferenceExpr;
                if (refExpr == null || !ValueInfo.IsDescriptor(refExpr.name))
                    return e;
                if (dictValues != null)
                    dictValues[refExpr.name] = null;
                return new IndexExpr(dataRef, new ConstExpr(refExpr.name));
            }));
            return res;
        }

        static string GetConditionStageText(string prevData, IEnumerable<string> prevDataParams, IEnumerable<string> arrayOfPrmNames, Expr condition
            , bool isFirst = false, bool inParallel = true)
        {
            var stringcond = condition == null ? null : ReplaceValueInfoRefsWithData(condition, "data").ToString();

            var prevDataFuncCode = string.Format("letLookupFunc(getPrevData, _Arr(), _Arr('{0}'), parts[i]{1})",
                string.Join("', '", prevDataParams),
                isFirst ? string.Empty : ".IDictsToObjsLst()"
                );

            string code;
            if (inParallel)
                code =
@"_block(
	" +
    "PartsOfLimitedSize(" + prevData + ")"
    //string.Format("PartsOfLimitedSize({0}, _StairSelect( COLUMNS({0})/15, {1}, {1}, COLUMNS({0})/15, {2}, {2} ))", prevData, minPartSize, maxPartSize)
    + @"..let(parts),
	let(n,COLUMNS(parts)),
	PROGRESS(""" + Convert.ToString(stringcond) + @""",n),
	_ParFor(
		let(i,0),
		i<n,
		(	" + prevDataFuncCode + @"
			,FindSolutionExpr({}, {'" + string.Join("', '", arrayOfPrmNames) + @"'}) . ExprToExecutable().AtNdx(0)..let(data)
			,let(i,i+1)
			,let(res, data" + ((stringcond == null) ? string.Empty : ".Where(" + stringcond + ")") + @")
			,PROGRESS(i,COLUMNS(res))
			,res
		)
	).MergeListsRows()
)";
            else
                code = string.Format(
@"_block(
	PROGRESS(""{0}"",n),
	(	{1}
		,FindSolutionExpr(_Arr(), _Arr('{2}')) . ExprToExecutable().AtNdx(0)..let(data)
		,data{3}
	)
)"
, Convert.ToString(stringcond)
, prevDataFuncCode
, string.Join("', '", arrayOfPrmNames)
, (stringcond == null) ? string.Empty : ".Where(" + stringcond + ")"
);

            return code;
        }

        static IDictionary<string, bool> GetInfluencingParams(IDictionary<string, string[]> dependecies, string[] paramNames)
        {
            var result = new Dictionary<string, bool>();

            var queue = new Queue<string>();

            foreach (var s in paramNames)
            {
                queue.Enqueue(s);
                result.Add(s, true);
            }

            while (queue.Count > 0)
            {
                string item = queue.Dequeue();

                foreach (var dep in dependecies[item])
                    if (!result.ContainsKey(dep))
                    {
                        queue.Enqueue(dep);
                        result.Add(dep, true);
                    }
            }

            return result;
        }
    }
}
