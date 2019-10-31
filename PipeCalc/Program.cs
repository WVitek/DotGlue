using PipeNetCalc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using W.Common;
using W.Expressions;

namespace PPM
{
    static class Program
    {
        public class UndefinedValuesException : System.Exception { public UndefinedValuesException(string msg) : base(msg) { } }

        public class Contexts
        {
            public Generator.Ctx gCtx;
            public AsyncExprCtx eCtx;
        }

        static Contexts GetCtxForCode(Contexts parent, string codeText)
        {
            var obj = FuncDefs_Core._Cached($"$Contexts_{codeText.GetHashCode()}", (Func<object>)(() =>
            {
                var res = new Contexts();

                //***** формируем по тексту синтаксическое дерево 
                var e = Parser.ParseToExpr(codeText);

                if (parent == null)
                {
                    var funcDefs = new W.Expressions.FuncDefs()
                        .AddFrom(typeof(W.Expressions.FuncDefs_Core))
                        .AddFrom(typeof(W.Expressions.FuncDefs_Excel))
                        .AddFrom(typeof(W.Expressions.FuncDefs_Report))
                        ;
                    var valsDefs = new Dictionary<string, object>();
                    //***** создаём контекст кодогенератора, содержащий предопределённые значения и перечень функций
                    res.gCtx = new Generator.Ctx(valsDefs, funcDefs.GetFuncs);
                    //***** формируем код
                    var g = Generator.Generate(e, res.gCtx);
                    //***** проверяем, заданы ли выражения для всех именованных значений
                    try { res.gCtx.CheckUndefinedValues(); }
                    catch (W.Expressions.Generator.Exception ex) { throw new UndefinedValuesException(ex.Message); }
                    res.eCtx = new AsyncExprRootCtx(res.gCtx.name2ndx, res.gCtx.values, OPs.GlobalMaxParallelismSemaphore);
                }
                else
                {
                    //***** создаём контекст кодогенератора, содержащий предопределённые значения и перечень функций
                    res.gCtx = new Generator.Ctx(parent.gCtx);
                    //***** формируем код
                    var g = Generator.Generate(e, res.gCtx);
                    //***** проверяем, заданы ли выражения для всех именованных значений
                    try { res.gCtx.CheckUndefinedValues(); }
                    catch (W.Expressions.Generator.Exception ex) { throw new UndefinedValuesException(ex.Message); }
                    res.eCtx = new AsyncExprCtx(res.gCtx, res.gCtx.values, parent.eCtx);
                }
                return res;
            }), DateTime.UtcNow.AddMinutes(5), ObjectCache.NoSlidingExpiration);

            return (Contexts)obj;
        }

        static Contexts GetRootCtx() => GetCtxForCode(default, "_include('PipeCalc.Init.h')");

        static Contexts GetCtx_Pipe() => GetCtxForCode(GetRootCtx(), @"
        (
            db::UseSqlAsFuncsFrom('Pipe.meta.sql', { 'TimeSlice' }, oraPipeConn, 'Pipe'),
            //solver::DefineProjectionFuncs({ '_CLCD_PIPE','CLASS_DICT_PIPE'}, { '_NAME_PIPE','_SHORTNAME_PIPE' }, data, pipe::GetClassInfo(data) )
        )");

        static Contexts GetCtx_PPM(string queryKinds) => GetCtxForCode(GetRootCtx(), $@"
        (
            db::UseSqlAsFuncsFrom('PPM.meta.sql', {queryKinds}, oraConn, 'PPM')
        )");

        static (Task<object> task, Generator.Ctx gCtx) Calc(Contexts contexts, IDictionary<string, object> defs, string codeText)
        {
            var e = Parser.ParseToExpr(codeText);

            contexts = contexts ?? GetRootCtx();
            var ctx = new Generator.Ctx(contexts.gCtx);

            if (defs != null)
                foreach (var def in defs)
                    ctx.CreateValue(def.Key, def.Value);

            var g = Generator.Generate(e, ctx);
            try { ctx.CheckUndefinedValues(); }
            catch (Generator.Exception ex) { throw new UndefinedValuesException(ex.Message); }

            var ae = new AsyncExprCtx(ctx, ctx.values, contexts.eCtx);

            return (OPs.ConstValueOf(ae, g), ctx);
        }

        static Task<object> ParTask(this (Task<object> task, Generator.Ctx gCtx) calc)
        {
            //return calc.task;
            calc.task.ConfigureAwait(false);
            return Task.Factory.StartNew(() => calc.task.Result);
        }

        class Stopwatch : IDisposable
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            string msg;
            public Stopwatch(string msg) { this.msg = msg; Console.WriteLine($"{msg}: started"); sw.Start(); }
            public void Dispose() { sw.Stop(); Console.WriteLine($"{msg}: done in {sw.ElapsedMilliseconds}ms"); }
        }

        static int GetColor(Dictionary<string, int> dict, object prop)
        {
            var key = Convert.ToString(prop);
            if (dict.TryGetValue(key, out int c))
                return c;
            int i = dict.Count + 1;
            dict.Add(key, i);
            return i;
        }

        static double GetDbl(this IIndexedDict d, string key)
        {
            if (d == null) return double.NaN;
            var v = d[key];
            if (v == DBNull.Value) return double.NaN;
            return Convert.ToDouble(v);
        }

        static float GetFlt(this IIndexedDict d, string key)
        {
            if (d == null) return float.NaN;
            var v = d[key];
            if (v == DBNull.Value) return float.NaN;
            return Convert.ToSingle(v);
        }

        static bool TryGet<T>(this IIndexedDict d, string key, out T res)
        {
            res = default(T);
            if (d == null) return false;
            var v = d[key];
            if (v == DBNull.Value) return false;
            res = Utils.Cast<T>(v);
            return true;
        }

        static T Get<T>(this IIndexedDict d, string key)
        {
            if (d == null) return default;
            var v = d[key];
            if (v == DBNull.Value) return default;
            return Utils.Cast<T>(v);
        }
        static bool GetUInt64(this IIndexedDict d, string key, out ulong res)
        {
            res = 0;
            if (d == null) return false;
            var v = d[key];
            if (v == DBNull.Value) return false;
            res = Convert.ToUInt64(v);
            return true;
        }

        static string GetStr(this IIndexedDict d, string key)
        {
            if (d == null) return null;
            var v = d[key];
            if (v == DBNull.Value) return null;
            return Convert.ToString(v);
        }

        static void Subnets<TID>() where TID : struct
        {
            const string sDirTGF = "TGF";
            const bool SaveToDB = true;

            IIndexedDict[] nodesArr, edgesArr;
            #region Получение исходных данных по вершинам и ребрам графа трубопроводов

            // Запуск загрузки справочника трансляции кодов OIS Pipe->PPM, если нужно
            Task<object> tO2P = SaveToDB ? Calc(GetCtx_Pipe(), null, "OIS_2_PPM_PU( '' )").ParTask() : null;

            using (new Stopwatch("Pipes data loading"))
            {
                var tNodes = Calc(GetCtx_Pipe(), null, "PipeNodesList( '' )").ParTask();
                var tEdges = Calc(GetCtx_Pipe(), null, "PU_List( '' )").ParTask();
                Task.WaitAll(tNodes, tEdges);
                nodesArr = (IIndexedDict[])tNodes.Result;
                edgesArr = (IIndexedDict[])tEdges.Result;
            }
            #endregion

            Edge[] edges;
            Node<TID>[] nodes;
            #region Подготовка входных параметров для разбиения на подсети
            string[] colors;
            {
                var nodesDict = Enumerable.Range(0, nodesArr.Length).ToDictionary(
                        i => Utils.Cast<TID>(nodesArr[i]["PipeNode_ID_Pipe"]),
                        i => (Ndx: i,
                            TypeID: Convert.ToInt32(nodesArr[i]["NodeType_ID_Pipe"]),
                            Altitude: GetDbl(nodesArr[i], "Node_Altitude_Pipe")
                        )
                    );
                var colorDict = new Dictionary<string, int>();
                edges = edgesArr.Select(
                    r => new Edge()
                    {
                        iNodeA = nodesDict.TryGetValue(Utils.Cast<TID>(r["PuBegNode_ID_Pipe"]), out var eBeg) ? eBeg.Ndx : -1,
                        iNodeB = nodesDict.TryGetValue(Utils.Cast<TID>(r["PuEndNode_ID_Pipe"]), out var eEnd) ? eEnd.Ndx : -1,
                        color = GetColor(colorDict, r["PuFluid_ClCD_Pipe"]),
                        D = GetFlt(r, "Pu_InnerDiam_Pipe"),
                        L = GetFlt(r, "Pu_Length_Pipe"),
                    })
                    .ToArray();
                nodes = nodesDict
                    .Select(p => new Node<TID>() { kind = (NodeKind)p.Value.TypeID, Node_ID = p.Key, Altitude = p.Value.Altitude })
                    .ToArray();
                colors = colorDict.OrderBy(p => p.Value).Select(p => p.Key).ToArray();
            }
            #endregion

            var nodeWell = new Dictionary<int, NetCalc.WellInfo<TID>>();
            #region Подготовка данных по скважинам
            using (new Stopwatch("Load wells data"))
            {
                var dictWellOp = new Dictionary<TID, (IIndexedDict press, IIndexedDict fluid)>();
                var tWellPress = Calc(GetCtx_Pipe(), null, "well_op_oil_Slice( , DATE(2019,01,01) )").ParTask();
                var tWellFluid = Calc(GetCtx_Pipe(), null, "well_layer_op_Slice( , DATE(2019,01,01)  )").ParTask();
                foreach (var r in (IIndexedDict[])tWellPress.Result)
                    dictWellOp[r.Get<TID>("Well_ID_OP")] = (press: r, fluid: null);
                foreach (var r in (IIndexedDict[])tWellFluid.Result)
                {
                    if (!r.TryGet<TID>("Well_ID_OP", out var wellID))
                        continue;
                    if (!dictWellOp.TryGetValue(wellID, out var t))
                        dictWellOp[wellID] = (null, r);
                    else if (t.fluid == null || t.fluid.GetStr("WellLayer_ClCD_OP") != "PL0000")
                        // отдаём предпочтение строке данных по пласту с агрегированной информацией (псевдо-пласт "PL0000")
                        dictWellOp[wellID] = (t.press, r);
                }
                for (int iNode = 0; iNode < nodesArr.Length; iNode++)
                {
                    if (!nodesArr[iNode].TryGet<TID>("NodeObj_ID_Pipe", out var wellID))
                        continue;
                    if (!dictWellOp.TryGetValue(wellID, out var t))
                        continue;
                    var p = t.press;
                    var f = t.fluid;
                    var wellInfo = new NetCalc.WellInfo<TID>()
                    {
                        Well_ID = wellID,
                        Layer = f.GetStr("WellLayer_ClCD_OP"),
                        Line_Pressure__Atm = p.GetDbl("WellLine_Pressure_OP_Atm"),
                        Liq_VolRate = f.GetDbl("WellLiq_VolRate_OP"),
                        Liq_Watercut = f.GetDbl("WellLiq_Watercut_OP") * 0.01,
                        Temperature__C = f.GetDbl("WellLayer_Temperature_OP_C"),
                        Bubblpnt_Pressure__Atm = f.GetDbl("WellBubblpnt_Pressure_OP_Atm"),
                        Reservoir_Pressure__Atm = f.GetDbl("WellLayerShut_Pressure_OP_Atm"),
                        //Liq_Viscosity = f.GetDbl("WellLiq_Viscosity_OP"),
                        Oil_Density = f.GetDbl("WellOil_Density_OP"),
                        Oil_VolumeFactor = f.GetDbl("WellOil_Comprssblty_OP"),
                        Oil_GasFactor = f.GetDbl("WellOil_GasFactor_OP"),
                        Water_Density = f.GetDbl("WellWater_Density_OP"),
                        Oil_Viscosity = f.GetDbl("WellOil_Viscosity_OP"),
                        Water_Viscosity = f.GetDbl("WellWater_Viscosity_OP"),
                    };
                    nodeWell.Add(iNode, wellInfo);
                }
            }
            #endregion

            var subnets = new List<int[]>();

            #region Поиск гидравлически единых подсетей
            var dictO2P = tO2P == null ? null : ((IIndexedDict[])tO2P.Result).ToDictionary(
                r => Convert.ToUInt64(r["Pipe_ID_Pipe"]),
                r => Convert.ToString(r["Pipe_ID_PPM"])
            );

            using (new Stopwatch("EnumSubnets"))
            {
                int min = int.MaxValue, max = 0, sum = 0;
                foreach (var subnetEdges in PipeSubnet.EnumSubnets(edges, nodes))
                {
                    subnets.Add(subnetEdges);
                    int n = subnetEdges.Length;
                    if (n < min) min = n;
                    if (n > max) max = n;
                    sum += n;
                    int nSubnets = subnets.Count;

                    var setOfPuIDs = new HashSet<ulong>();
                    var subnetNodes = new HashSet<int>();

                    foreach (var iEdge in subnetEdges)
                    {
                        subnetNodes.Add(edges[iEdge].iNodeA);
                        subnetNodes.Add(edges[iEdge].iNodeB);
                        var pu_id = Convert.ToUInt64(edgesArr[iEdge]["Pu_ID_Pipe"]);
                        setOfPuIDs.Add(pu_id);
                    }
                }
                Console.WriteLine($"nSubnets={subnets.Count}, min={min}, avg={sum / subnets.Count:g}, max={max}");
            }
            #endregion

            foreach (var f in Directory.CreateDirectory(sDirTGF).EnumerateFiles())
                if (f.Name.EndsWith(".tgf")) f.Delete();

            using (new Stopwatch("Calc on subnets"))
                PipesCalc.CalcSubnets<TID>(edges, nodes, subnets, nodeWell,
                    iSubnet =>
                    {
                        Console.Write($" {iSubnet}");
                        return new StreamWriter(Path.Combine(sDirTGF, $"{iSubnet + 1}.tgf"));
                    },
                    iNode => nodesArr[iNode].GetStr("Node_Name_Pipe")
                );
            Console.WriteLine();
        }

        static void Main(string[] args)
        {
            Subnets<long>();
        }
    }
}
