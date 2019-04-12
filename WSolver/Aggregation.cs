using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace W.Expressions
{
    using W.Common;

    public static class WeightedSums
    {
        internal struct Sum
        {
            public double SumValues;
            public double SumWeights;
        }

        public class Sums
        {
            public readonly Factory fact;
            readonly Sum[] items;
            internal Sums(Factory fact, Sum[] items) { this.fact = fact; this.items = items; }

            /// <summary>
            /// Добавление к суммам значений values, имеющих ключи keys
            /// </summary>
            /// <param name="values">Массив числовых значений ITimedObject, отсортированных по keys и ITimedObject.Time</param>
            /// <param name="keys">Массив соответствующих values ключевых ID (значения для контроля добавляемых значений с одинаковыми ID - отсутствия их пересечений по времени)</param>
            public void Add(IList values, IList keys)
            {
                int n = values.Count;
                if (n == 0)
                    return;
                System.Diagnostics.Trace.Assert(n == keys.Count);
                IConvertible currKey = 0;
                var prevEndTime = DateTime.MinValue;

                for (int j = 0; j < n; j++)
                {
                    var val = values[j] as W.Common.ITimedObject;
                    if (val == null || val.IsEmpty)
                        continue;
                    var key = (IConvertible)keys[j];
                    var dtBeg = val.Time;
                    if (W.Common.Cmp.UnsafeCmpKeys(currKey, key) != 0)
                        currKey = key;
                    else if (dtBeg < prevEndTime)
                        continue;
                    var dtEnd = val.EndTime;
                    prevEndTime = dtEnd;
                    var xtBeg = dtBeg.ToOADate();
                    if (xtBeg < fact.begTime)
                        xtBeg = fact.begTime;
                    var ndxBeg = (xtBeg - fact.begTime) * fact.invStep;
                    var xtEnd = dtEnd.ToOADate();
                    if (fact.endTime < xtEnd)
                        xtEnd = fact.endTime;
                    var ndxEnd = (xtEnd - fact.begTime) * fact.invStep;
                    if (ndxEnd >= items.Length)
                        ndxEnd = items.Length - 1;
                    var ndxBegCeil = Math.Ceiling(ndxBeg);
                    var ndxEndCeil = Math.Ceiling(ndxEnd);
                    int iBeg = (int)ndxBegCeil - 1;
                    int iEnd = (int)ndxEndCeil - 1;
                    var value = Convert.ToDouble(val);
                    if (iBeg >= 0)
                    {
                        var weightBeg = ndxBegCeil - ndxBeg;
                        items[iBeg].SumValues += value * weightBeg;
                        items[iBeg].SumWeights += weightBeg;
                    }
                    if (iEnd >= 0)
                    {
                        var weightEnd = ndxEndCeil - ndxEnd;
                        items[iEnd].SumValues -= value * weightEnd;
                        items[iEnd].SumWeights -= weightEnd;
                    }
                    for (int i = iBeg + 1; i <= iEnd; i++)
                    {
                        items[i].SumValues += value;
                        items[i].SumWeights += 1d;
                    }
                }
            }

            /// <summary>
            /// Добавление к суммам значений values (все значения относятся к одной сущности)
            /// </summary>
            /// <param name="values">Массив числовых значений ITimedObject</param>
            public void Add(IList values) { Add(values, new RepeatingValueList(0m, values.Count)); }

            public void Add(Sums what)
            {
                if (!fact.EqualTo(what.fact))
                    System.Diagnostics.Debug.Assert(false, "PartialSums.Sums.Add: instances of PartialSums.Factory must be equal");
                var a = this.items;
                var b = what.items;
                for (int i = fact.itemsCount - 1; i >= 0; i--)
                {
                    a[i].SumValues += b[i].SumValues;
                    a[i].SumWeights += b[i].SumWeights;
                }
            }

            public double[] GetAverages()
            {
                int n = items.Length;
                var r = new double[n];
                for (int i = n - 1; i >= 0; i--)
                {
                    var w = items[i].SumWeights;
                    if (w <= double.Epsilon)
                        r[i] = double.NaN;
                    else
                        r[i] = items[i].SumValues / w;
                }
                return r;
            }

            public double[] GetValues()
            {
                int n = items.Length;
                var r = new double[n];
                for (int i = n - 1; i >= 0; i--)
                    r[i] = items[i].SumValues;
                return r;
            }

            public double[] GetWeights()
            {
                int n = items.Length;
                var r = new double[n];
                for (int i = n - 1; i >= 0; i--)
                    r[i] = items[i].SumWeights;
                return r;
            }
        }

        public class Factory
        {
            public readonly DateTime START_TIME;
            public readonly DateTime END_TIME;
            public readonly double begTime;
            public readonly double endTime;
            public readonly double timeStep;
            public readonly double invStep;
            public readonly int itemsCount;

            /// <summary>
            /// Фабрика сумм, с привязкой начальной и конечной даты к целому числу шагов
            /// </summary>
            /// <param name="START_TIME">Опорная начальная дата</param>
            /// <param name="END_TIME">Опорная конечная дата</param>
            /// <param name="integralStep">Шаг, к которому должна быть привязана сетка суммирования</param>
            public Factory(DateTime START_TIME, DateTime END_TIME, TimeSpan integralStep)
            {
                this.START_TIME = START_TIME;
                this.END_TIME = END_TIME;
                timeStep = integralStep.TotalDays;
                invStep = 1d / timeStep;
                begTime = Math.Floor(START_TIME.ToOADate() * invStep) * timeStep;
                endTime = Math.Ceiling(END_TIME.ToOADate() * invStep) * timeStep;
                var cnt = Math.Round((endTime - begTime) * invStep);
                itemsCount = (int)cnt;
            }

            /// <summary>
            /// Фабрика сумм с фиксированными начальной и конечной датой и размерностью массива сумм
            /// </summary>
            /// <param name="START_TIME">Начальная дата</param>
            /// <param name="END_TIME">Конечная дата</param>
            /// <param name="nSteps">Количество шагов = размерность массива сумм</param>
            public Factory(DateTime START_TIME, DateTime END_TIME, int nSteps)
            {
                this.START_TIME = START_TIME;
                this.END_TIME = END_TIME;
                begTime = START_TIME.ToOADate();
                endTime = END_TIME.ToOADate();
                timeStep = (endTime - begTime) / nSteps;
                invStep = 1d / timeStep;
                itemsCount = nSteps;
            }

            public bool EqualTo(Factory f) { return this == f || START_TIME == f.START_TIME && END_TIME == f.END_TIME && timeStep == f.timeStep; }
            public Sums NewSums() { return new Sums(this, new Sum[itemsCount]); }
        }

        class RepeatingValueList : IList
        {
            object v;
            int n;
            public RepeatingValueList(object value, int count) { v = value; n = count; }
            public int Add(object value) { throw new NotSupportedException(); }
            public void Clear() { throw new NotSupportedException(); }
            public bool Contains(object value) { return value == v; }
            public int IndexOf(object value) { return (value == v) ? 0 : -1; }
            public void Insert(int index, object value) { throw new NotSupportedException(); }
            public bool IsFixedSize { get { return true; } }
            public bool IsReadOnly { get { return true; } }
            public void Remove(object value) { throw new NotSupportedException(); }
            public void RemoveAt(int index) { throw new NotSupportedException(); }
            public object this[int index] { get { return v; } set { throw new NotSupportedException(); } }
            public void CopyTo(Array array, int index) { throw new NotImplementedException(); }
            public int Count { get { return n; } }
            public bool IsSynchronized { get { return false; } }
            public object SyncRoot { get { throw new NotImplementedException(); } }
            public IEnumerator GetEnumerator() { for (int i = n - 1; i >= 0; i++) yield return v; }
        }
    }

    public static class DataLoader
    {
        const string sUsedParamsDict = "_UsedParams";
        const string sLoadingInfo = "_LoadingInfo";
        const string sData = "@r";
        const string sTimeAt = "AT_TIME__XT";
        const string sTimeA = "A_TIME__XT";
        const string sTimeB = "B_TIME__XT";

        const string sFactory = "@Factory";
        const string sWithKey = "@WithKey";
        const string sAkk = "@Akk";
        const string sVal = "@Val";

        static class FuncDefs_NoAggr
        {
            /// <summary>
            /// Псевдоагрегирование, для одного объекта
            /// </summary>
            /// <param name="ce">0: период агрегации (не используется); 1:ключевой показатель; ...; n:возвращаемое значение</param>
            /// <param name="ctx"></param>
            /// <returns></returns>
            [Arity(3, int.MaxValue)]
            public static object AGGR(CallExpr call, Generator.Ctx ctx)
            {
                var dictUsedValues = new Dictionary<string, object>();
                //var timeSpanExpr = ce.args[0];

                var ce = Filter.ReplaceValueInfoRefsWithData(call, DataLoader.sData, dictUsedValues);
                ctx.CreateValue(DataLoader.sUsedParamsDict, dictUsedValues);
                var expr = new CallExpr(FuncDefs_Solver.SolverTimedObjects,
                    new CallExpr((Fx)FuncDefs_Solver.SolverTimesOfIDicts, new ReferenceExpr(DataLoader.sData)),
                    ce.args[ce.args.Count - 1]
                );
                return Generator.Generate(expr, ctx);
            }

            static object AggImplSimple(CallExpr ce, Generator.Ctx ctx)
            {
                var arg = ce.args[0];
                return Generator.Generate(arg, ctx);
            }

            [Arity(1, 2)]
            public static object SUMS(CallExpr ce, Generator.Ctx ctx) { return AggImplSimple(ce, ctx); }

            [Arity(1, 2)]
            public static object WEIGHTS(CallExpr ce, Generator.Ctx ctx) { return AggImplSimple(ce, ctx); }

            [Arity(1, 2)]
            public static object AVGS(CallExpr ce, Generator.Ctx ctx) { return AggImplSimple(ce, ctx); }
        }

        static class FuncDefs_Aggr
        {
            /// <summary>
            /// Агрегирование по нескольким объектам
            /// </summary>
            /// <param name="ce">0: период агрегации; 1:ключевой показатель; ...; n:возвращаемое значение</param>
            /// <param name="ctx"></param>
            /// <returns></returns>
            [Arity(3, int.MaxValue)]
            public static object AGGR(CallExpr call, Generator.Ctx ctx)
            {
                CallExpr ce;
                {
                    var dict = new Dictionary<string, object>();
                    ce = Filter.ReplaceValueInfoRefsWithData(call, DataLoader.sData, dict);
                    ctx.CreateValue(DataLoader.sUsedParamsDict, dict);
                }

                var args = new List<Expr>();
                args.Add(CallExpr.let(new ReferenceExpr(sFactory), 
                    new CallExpr(FuncDefs_Solver.SolverWeightedSumsFactory, ce.args[0], new ReferenceExpr(sTimeA), new ReferenceExpr(sTimeB))));
                args.Add(CallExpr.let(new ReferenceExpr(sWithKey), ce.args[1]));

                for (int i = 2; i < ce.args.Count; i++)
                    args.Add(ce.args[i]);
                var exprAgg = CallExpr.Eval(args.ToArray());

                var expr = new CallExpr(FuncDefs_Solver.SolverTimedObjects, 
                    new CallExpr((Fx)FuncDefs_Solver.SolverWeightedSumsFactoryTimes, new ReferenceExpr(sFactory)), exprAgg);
                return Generator.Generate(expr, ctx);
            }

            static Expr AggImpl(CallExpr ce, Generator.Ctx ctx)
            {
                var valsExpr = ce.args[0];

                Expr keysExpr;
                if (ce.args.Count > 1)
                    keysExpr = ce.args[1];
                else
                    keysExpr = new ReferenceExpr(sWithKey);

                var refAkk = new ReferenceExpr(string.Format("{0}:{1}:{2}", sAkk, valsExpr, keysExpr));
                int iAkk = ctx.IndexOf(refAkk.name);

                Expr resultExpr;
                if (iAkk >= 0)
                    resultExpr = refAkk;
                else
                    resultExpr = new CallExpr(FuncDefs_Solver.SolverWeightedSumsAdd, refAkk, 
                        new CallExpr(FuncDefs_Solver.SolverWeightedSumsNew, new ReferenceExpr(sFactory), valsExpr, keysExpr));

                return resultExpr;
            }

            static object AggImplSimple(CallExpr ce, Generator.Ctx ctx)
            {
                var what = ce.funcName;
                var expr = new BinaryExpr(ExprType.Fluent, AggImpl(ce, ctx), new CallExpr(FuncDefs_Solver.SolverWeightedSumsGet, new ConstExpr(what)));
                return Generator.Generate(expr, ctx);
            }

            [Arity(1, 2)]
            public static object SUMS(CallExpr ce, Generator.Ctx ctx) { return AggImplSimple(ce, ctx); }

            [Arity(1, 2)]
            public static object WEIGHTS(CallExpr ce, Generator.Ctx ctx) { return AggImplSimple(ce, ctx); }

            [Arity(1, 2)]
            public static object AVGS(CallExpr ce, Generator.Ctx ctx) { return AggImplSimple(ce, ctx); }
        }

        static FuncDefs funcDefs = null;
        public static FuncDefs CoreFuncDefs()
        {
            lock (typeof(DataLoader))
                if (funcDefs == null)
                    funcDefs = new FuncDefs().AddFrom(typeof(FuncDefs_Core));
            return funcDefs;
        }

        public delegate void OnDataLoading(string dataExpr, object cookie, IList data);

        class LoadingInfo
        {
            public string dataExpr;
            public object cookie;
            public Generator.Ctx aggCtx;
            public object aggCode;

            public override string ToString() { return dataExpr; }

            public LoadingInfo(string dataExpr, object cookie, Generator.Ctx rootCtx)
            {
                this.dataExpr = dataExpr;
                this.cookie = cookie;
                aggCtx = new Generator.Ctx(rootCtx);
                var expr = Parser.ParseToExpr(dataExpr);
                aggCode = Generator.Generate(expr, aggCtx);
            }

            public Task LoadData(AsyncExprCtx rootAec, Expr loadingExpr, CancellationToken ct)
            {
                int i = aggCtx.IndexOf(sFactory);
                if (i >= 0)
                {
                    var f = (WeightedSums.Factory)aggCtx[i];
                    foreach (var n2i in aggCtx.name2ndx)
                        if (n2i.Key.StartsWith(sAkk))
                            aggCtx[n2i.Value] = f.NewSums();
                }
                var loadingCtx = new Generator.Ctx(aggCtx.parent);
                loadingCtx.CreateValue(sUsedParamsDict, aggCtx[aggCtx.IndexOf(sUsedParamsDict)]);
                loadingCtx.CreateValue(sLoadingInfo, this);
                var loadingCode = Generator.Generate(loadingExpr, loadingCtx);
                var ae = new AsyncExprCtx(loadingCtx, loadingCtx.values, rootAec);
                return OPs.ConstValueOf(ae, loadingCode);
            }

            public Task<object> Aggregate(AsyncExprCtx parent, IList data)
            {
                int i = aggCtx.name2ndx[sData];
                var vals = new object[aggCtx.values.Count];
                aggCtx.values.CopyTo(vals, 0);
                vals[i] = data;
                var aec = new AsyncExprCtx(aggCtx, vals, parent);
                return OPs.ConstValueOf(aec, aggCode);
            }
        }

        public static async Task Load(IDictionary<string, object> dataExprsAndCookies, DateTime StartDate, DateTime EndDate,
            long[] ids, string idParamName, string contextInitializationScript,
            OnDataLoading onDataLoading,
            Action<float> onProgress,
            int maxPartSize,
            System.Threading.CancellationToken ct)
        {
            bool timeRange = StartDate < EndDate;
            bool withAggr = ids.Length > 1;
            int nTotalPartsToLoad = 0;
            int nPartsLoaded = 0;

            AsyncFn fProgress = async (aec, args) =>
            {
                if (!ct.IsCancellationRequested)
                {
                    var info = (LoadingInfo)await OPs.ConstValueOf(aec, args[0]);
                    var value = await OPs.ConstValueOf(aec, args[1]);
                    var data = value as IList;
                    if (data != null)
                    {
                        var aggData = await info.Aggregate(aec, data);
                        onDataLoading(info.dataExpr, info.cookie, (IList)aggData);
                        Interlocked.Increment(ref nPartsLoaded);
                    }
                    else // calc n
                    {
                        int n = Convert.ToInt32(value);
                        Interlocked.Add(ref nTotalPartsToLoad, n);
                    }
                    if (onProgress != null)
                        onProgress(100f * nPartsLoaded / nTotalPartsToLoad);
                }
                return string.Empty;
            };

            // initialize code generator context
            var defs = new Dictionary<string, object>();
            defs.Add(idParamName, ids);
            defs.Add("PROGRESS", new FuncDef(fProgress, 2, "PROGRESS"));
            if (timeRange)
            {
                defs.Add("A_TIME__XT", StartDate.ToOADate());
                defs.Add("B_TIME__XT", EndDate.ToOADate());
            }
            else defs.Add("AT_TIME__XT", StartDate.ToOADate());
            var rootCtx = new Generator.Ctx(defs, CoreFuncDefs().GetFuncs);
            Generator.Generate(Parser.ParseToExpr(contextInitializationScript), rootCtx);
            rootCtx.UseFuncs(new FuncDefs().AddFrom(withAggr ? typeof(FuncDefs_Aggr) : typeof(FuncDefs_NoAggr)).GetFuncs);

            var infos = new List<LoadingInfo>(dataExprsAndCookies.Count);
            var tasks = new List<Task>();

            foreach (var pair in dataExprsAndCookies)
                infos.Add(new LoadingInfo(pair.Key, pair.Value, rootCtx));

            var loadingExpr = Parser.ParseToExpr(@"(
	PartsOfLimitedSize(" + idParamName + " ," + maxPartSize.ToString() + @")
	..let(parts)
	,let(n,COLUMNS(parts)) .. PROGRESS(" + sLoadingInfo + @"),
	,_ParFor(
		let(i,0),
		i<n,
		(
			let(" + idParamName + @", parts[i])
			,FindSolutionExpr({}, " + sUsedParamsDict + @".Keys()) . ExprToExecutable() . AtNdx(0) .. PROGRESS(" + sLoadingInfo + @")
			,let(i, i+1)
		)
	)
)");
            var rootAec = new AsyncExprRootCtx(rootCtx.name2ndx, rootCtx.values, OPs.GlobalMaxParallelismSemaphore);
            foreach (var info in infos)
                tasks.Add(Task.Factory.StartNew(() => info.LoadData(rootAec, loadingExpr, ct)).Unwrap());
            await TaskEx.WhenAll(tasks);
        }
    }

}
