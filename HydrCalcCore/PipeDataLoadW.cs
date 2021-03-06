﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using W.Common;
using W.Expressions;
using PipeNetCalc;

namespace PPM.HydrCalcPipe
{
    public static class PipeDataLoadW
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

        static int GetColor(Dictionary<string, int> dict, object prop)
        {
            var key = Convert.ToString(prop);
            if (dict.TryGetValue(key, out int c))
                return c;
            int i = dict.Count + 1;
            dict.Add(key, i);
            return i;
        }

        static float GetFlt(this IIndexedDict d, string key, float defVal = float.NaN)
        {
            if (d == null) return defVal;
            var v = d[key];
            if (v == DBNull.Value) return defVal;
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

        static ulong GetUInt64(this IIndexedDict d, string key, ulong defVal = default(ulong))
        {
            if (d == null) return defVal;
            var v = d[key];
            if (v == DBNull.Value) return defVal;
            return Convert.ToUInt64(v);
        }

        static string GetStr(this IIndexedDict d, string key)
        {
            if (d == null) return null;
            var v = d[key];
            if (v == DBNull.Value) return null;
            return Convert.ToString(v);
        }

        static
            (Edge[] edges, Node[] nodes, string[] colorName, string[] nodeName, ulong[] edgeID,
            Dictionary<int, NetCalc.WellInfo> nodeWell)

            PrepareInputData()
        {
            IIndexedDict[] nodesArr, edgesArr;
            #region Получение исходных данных по вершинам и ребрам графа трубопроводов
            using (new StopwatchMs("Load data from Pipe (ORA)"))
            {
                var tNodes = Calc(GetCtx_Pipe(), null, "PipeNodesList( '' )").ParTask();
                var tEdges = Calc(GetCtx_Pipe(), null, "PU_List( '' )").ParTask();
                Task.WaitAll(tNodes, tEdges);
                nodesArr = (IIndexedDict[])tNodes.Result;
                edgesArr = (IIndexedDict[])tEdges.Result;
            }
            #endregion

            Edge[] edges;
            Node[] nodes;
            string[] colorName;
            #region Подготовка входных параметров для разбиения на подсети
            {
                var nodesDict = Enumerable.Range(0, nodesArr.Length).ToDictionary(
                        i => nodesArr[i].GetStr("PipeNode_ID_Pipe"),
                        i => (Ndx: i,
                            TypeID: Convert.ToInt32(nodesArr[i]["NodeType_ID_Pipe"]),
                            Altitude: GetFlt(nodesArr[i], "Node_Altitude_Pipe")
                        )
                    );
                var colorDict = new Dictionary<string, int>();
                edges = edgesArr.Select(
                    r => new Edge()
                    {
                        iNodeA = nodesDict.TryGetValue(r.GetStr("PuBegNode_ID_Pipe"), out var eBeg) ? eBeg.Ndx : -1,
                        iNodeB = nodesDict.TryGetValue(r.GetStr("PuEndNode_ID_Pipe"), out var eEnd) ? eEnd.Ndx : -1,
                        color = GetColor(colorDict, r["PuFluid_ClCD_Pipe"]),
                        D = GetFlt(r, "Pu_InnerDiam_Pipe"),
                        L = GetFlt(r, "Pu_Length_Pipe", float.Epsilon),
                    })
                    .ToArray();
                nodes = nodesDict
                    .Select(p => new Node() { kind = (NodeKind)p.Value.TypeID, Node_ID = p.Key, Altitude = p.Value.Altitude })
                    .ToArray();
                colorName = colorDict.OrderBy(p => p.Value).Select(p => p.Key).ToArray();
            }
            #endregion

            var nodeWell = new Dictionary<int, NetCalc.WellInfo>();
            #region Подготовка данных по скважинам
            {
                var dictWellOp = new Dictionary<string, (IIndexedDict press, IIndexedDict fluid)>();

                using (new StopwatchMs("Load wells data from WELL_OP"))
                {
                    var tWellPress = Calc(GetCtx_Pipe(), null, "well_op_oil_Slice( , DATE(2019,01,01) )").ParTask();
                    var tWellFluid = Calc(GetCtx_Pipe(), null, "well_layer_op_Slice( , DATE(2019,01,01)  )").ParTask();
                    foreach (var r in (IIndexedDict[])tWellPress.Result)
                        dictWellOp[r.GetStr("Well_ID_OP")] = (press: r, fluid: null);
                    foreach (var r in (IIndexedDict[])tWellFluid.Result)
                    {
                        var wellID = r.GetStr("Well_ID_OP");
                        if (wellID == null)
                            continue;
                        if (!dictWellOp.TryGetValue(wellID, out var t))
                            dictWellOp[wellID] = (null, r);
                        else if (t.fluid == null || t.fluid.GetStr("WellLayer_ClCD_OP") != "PL0000")
                            // отдаём предпочтение строке данных по пласту с агрегированной информацией (псевдо-пласт "PL0000")
                            dictWellOp[wellID] = (t.press, r);
                    }
                }

                for (int iNode = 0; iNode < nodesArr.Length; iNode++)
                {
                    var wellID = nodesArr[iNode].GetStr("NodeObj_ID_Pipe");
                    if (wellID == null || !dictWellOp.TryGetValue(wellID, out var t))
                        continue;
                    var p = t.press;
                    var f = t.fluid;
                    var wellInfo = new NetCalc.WellInfo()
                    {
                        Well_ID = wellID,
                        Layer = f.GetStr("WellLayer_ClCD_OP"),
                        Line_Pressure__Atm = p.GetFlt("WellLine_Pressure_OP_Atm"),
                        Liq_VolRate = f.GetFlt("WellLiq_VolRate_OP"),
                        Liq_Watercut = f.GetFlt("WellLiq_Watercut_OP") * 0.01f,
                        Temperature__C = f.GetFlt("WellLayer_Temperature_OP_C"),
                        Bubblpnt_Pressure__Atm = f.GetFlt("WellBubblpnt_Pressure_OP_Atm"),
                        Reservoir_Pressure__Atm = f.GetFlt("WellLayerShut_Pressure_OP_Atm"),
                        //Liq_Viscosity = f.GetDbl("WellLiq_Viscosity_OP"),
                        Oil_Density = f.GetFlt("WellOil_Density_OP"),
                        Oil_VolumeFactor = f.GetFlt("WellOil_Comprssblty_OP"),
                        Oil_GasFactor = f.GetFlt("WellOil_GasFactor_OP"),
                        Water_Density = f.GetFlt("WellWater_Density_OP"),
                        Gas_Density = f.GetFlt("WellGas_Density_OP"),
                        Oil_Viscosity = f.GetFlt("WellOil_Viscosity_OP"),
                        Water_Viscosity = f.GetFlt("WellWater_Viscosity_OP"),
                    };
                    nodeWell.Add(iNode, wellInfo);
                }
            }
            #endregion

            var edgeID = edgesArr.Select(r => GetUInt64(r, "PU_ID_Pipe")).ToArray();
            var nodeName = nodesArr.Select(r => GetStr(r, "Node_Name_Pipe")).ToArray();

            return (edges, nodes, colorName, nodeName, edgeID, nodeWell);
        }
    }
}