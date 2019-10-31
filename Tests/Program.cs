using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pipe.Exercises
{
    using PipeNetCalc;
    using System.Collections;
    using System.IO;
    using System.Threading;
    using W.Common;
    using W.Expressions;
    using W.Expressions.Sql;
    using W.Oilca;

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
            }), DateTime.UtcNow.AddMinutes(5), System.Web.Caching.Cache.NoSlidingExpiration);

            return (Contexts)obj;
        }

        static Contexts GetRootCtx() => GetCtxForCode(default, "_include('Init.glue.h')");

        static Contexts GetCtx_Pipe() => GetCtxForCode(GetRootCtx(), @"
        (
            db::UseSqlAsFuncsFrom('Pipe.meta.sql', { 'TimeSlice' }, oraPipeConn, 'Pipe'),
            solver::DefineProjectionFuncs({ '_CLCD_PIPE','CLASS_DICT_PIPE'}, { '_NAME_PIPE','_SHORTNAME_PIPE' }, data, pipe::GetClassInfo(data) )
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

        static Task<object> Calc_Try()
        {
            return Calc(GetCtx_Pipe(), null, @"
            (
                let(At_TIME__XT, DATEVALUE('2019-04-17')),
                let(Pt_ID_Pipe, Pipe_Truboprovod_Slice( solver::TextToExpr('""Месторождение""='&""'MS0060'""), At_TIME__XT )['Pt_ID_Pipe']),

                //solver::FindSolutionExpr({'Pt_ID_Pipe','AT_TIME__XT'}, {'PU_RAWGEOM_PIPE'})
                //solver::FindSolutionExpr({ }, { 'CLASS_DICT_PIPE' })
                solver::FindAllSolutions({ }, { 'PtBegNode_DESCR_Pipe','PtEndNode_DESCR_Pipe','UtInnerCoatType_ClCD_PIPE', 'UtInnerCoatType_Name_PIPE', 'UtOuterCoatType_NAME_PIPE' }) // , , 'PipeNode_DESCR_PIPE' })
        	    .solver::ExprToExecutable()
            )").task;
        }

        static Task<object> Calc_Try(params string[] prmz)
        {
            return Calc(GetCtx_Pipe(), null, @"
            (
                let(At_TIME__XT, DATEVALUE('2019-04-17')),
                let(Pu_ID_Pipe, 9001682 ),
                //let(Pipe_ID_Pipe, AsPPM_Pipe_Slice( solver::TextToExpr('""Месторождение""='&""'MS0060'""), At_TIME__XT )['Pipe_ID_Pipe']),

                solver::FindSolutionExpr({ }, { '" + string.Join("','", prmz) + @"' })
        	    .solver::ExprToExecutable()
            )").task;
        }

        static Task<object> Calc_Try2()
        {
            return Calc(GetCtx_Pipe(), null, @"
            (
                let(At_TIME__XT, DATEVALUE('2019-04-17')),
                AsPPM_Pipe_Slice( 
                    solver::TextToExprWithSeq('""Месторождение""=' & ""'MS0060'"" & ' AND ""Координаты"" IS NOT NULL')
                    , At_TIME__XT
                )
            )").task;
        }

        static Task PPM_SQL()
        {
            return Calc(GetCtx_PPM("{ 'Raw', 'TimeSlice' }"), default, @"(
db::SqlFuncsToText('PPM').._WriteAllText('PPM.unfolded.sql'),
let( sqls, db::SqlFuncsToDDL('PPM') ), 
sqls[0].._WriteAllText('PPM.genDDL.sql'), 
sqls[1].._WriteAllText('PPM.drops.sql'),
)
"
            ).task;
        }

        static Task Pipe_SQL()
        {
            return Calc(GetCtx_Pipe(), null, @"db::SqlFuncsToText({'Pipe','OP'}).._WriteAllText('Pipe.unfolded.sql')").task;
        }

        static void RunDepsGraphFor(Contexts ctxs, string param)
        {
            var defs = new Dictionary<string, object>();

            var t = Calc(ctxs, defs, "solver::FindDependencies({'" + param + "'}, , {'A_TIME__XT', 'B_TIME__XT', 'MIN_TIME__XT', 'MAX_TIME__XT' } )");
            var outParams = ((IDictionary<string, object>)t.task.Result).Keys.ToArray();
            Console.WriteLine(outParams);

            defs.Add(FuncDefs_Solver.optionSolverDependencies, null);

            var (task, gCtx) = Calc(ctxs, defs,
@"(
	solver::FindSolutionExpr({'" + param + "','AT_TIME__XT'}, {" + string.Join(", ", outParams.Select(s => '\'' + s + '\'')) + @"})
//	.solver::ExprToExecutable().AtNdx(0)
)"
    );
            var deps = (W.Common.IIndexedDict)gCtx[gCtx.IndexOf(FuncDefs_Solver.optionSolverDependencies)];

            //var caps = (IIndexedDict[])await ProgramInterface.GetParamsCaptions(deps.Keys.ToArray(), ct);
            //var prmzCaps = caps.ToDictionary(r => Convert.ToString(r["PARAM_ID_DESCR"]), r => Convert.ToString(r["PARAM_NAME_DESCR"]));

            var sGV = W.Expressions.Solver.Exporters.SolverDependencies2GraphVis(
                deps
                , paramzCaptions: null
                , multiEdge: true
                , withTables: true
                , showPrmNames: true
                , outputParams: outParams
            );
            //System.IO.File.WriteAllBytes("DepsGraph.gv", Encoding.UTF8.GetBytes(sGV).Skip(2).ToArray()); // write without BOM
            System.IO.File.WriteAllText($"DepsGraph for {param}.gv", sGV, Encoding.UTF8); // write with BOM
            //var txt = Convert.ToString(W.Expressions.FuncDefs_Report._IDictsToStr(caps));
            //System.IO.File.WriteAllText("DepsGraphParamsNames.log", txt);
        }

        static void PipesFromPipe()
        {
            var res = Calc_Try2().Result;
            var s = ValuesDictionary.IDictsToStr((IList)res);
        }

        static void Pipe_Geometry()
        {
            var res = Calc(GetCtx_Pipe(), null, @"
            (
                let(At_TIME__XT, DATEVALUE('2019-09-27')),
                //let(Pu_ID_Pipe, 90016820 ),
                //let(where, solver::TextToExprWithSeq('""Месторождение""='&""'MS0060'""&' AND Pu_ID is not null') ),
                //let(where, solver::TextToExprWithSeq('""ID простого участка""=8025665') ),
                let(where, ''),
                let( partMaxSize, 1000 ),
                let( parts, PartsOfLimitedSize( PuPipesList( where )['Pu_ID_Pipe'], partMaxSize ) ),
             
                let(fnPfx, '_PipeGeom.sql'),
                _DeleteFile(0&fnPfx), _AppendText(0&fnPfx, 'use [PODS6]'&CHAR(13)&CHAR(10) ),
                _DeleteFile(1&fnPfx), _AppendText(1&fnPfx, 'use [PODS6]'&CHAR(13)&CHAR(10) ),

                _ParFor( (let(n,COLUMNS(parts)), let(i,0), let(nRes,0) ),
                    i<n,
                    (
                        let( Pu_ID_Pipe, parts[i] ),
                        let(res, solver::FindSolutionExpr({ }, { 'InsertPipe_OBJ_Test' }).solver::ExprToExecutable().AtNdx(0) ),
                        _AppendText(MOD(i,2) & fnPfx, _StrJoin(CHAR(13)&CHAR(10), res['InsertPipe_OBJ_Test'] )),
                        PrintLn( _StrFmt( '{0}: nSrcPipes={1}, nGeom={2}', i, i*partMaxSize+parts[i].COLUMNS(), nRes+res.COLUMNS() )),
                        let(nRes, nRes+res.COLUMNS()),
                        let(i,i+1)
                    )
                ),

                _AppendText(0&fnPfx, CHAR(13)&CHAR(10)&'GO'),
                _AppendText(1&fnPfx, CHAR(13)&CHAR(10)&'GO'),
                1
            )").task.Result;
            //var s = string.Join("\r\n", res.Select(r => r.ValuesList[0]));
        }

        static void Node_Geometry()
        {
            var res = Calc(GetCtx_Pipe(), null, @"
            (
                let(At_TIME__XT, DATEVALUE('2019-09-27')),
                let(where, ''),
                let( partMaxSize, 1000 ),
                let( parts, PartsOfLimitedSize( PipeNodesList( where )['PipeNode_ID_Pipe'], partMaxSize ) ),
             
                let(fnPfx, '_NodeGeom.sql'),
                _DeleteFile(0&fnPfx), _AppendText(0&fnPfx, 'use [PODS6]'&CHAR(13)&CHAR(10) ),
                _DeleteFile(1&fnPfx), _AppendText(1&fnPfx, 'use [PODS6]'&CHAR(13)&CHAR(10) ),

                _ParFor( (let(n,COLUMNS(parts)), let(i,0), let(nRes,0) ),
                    i<n,
                    (
                        let( PipeNode_ID_Pipe, parts[i] ),
                        let(res, solver::FindSolutionExpr({ }, { 'InsertNode_OBJ_Test' }).solver::ExprToExecutable().AtNdx(0) ),
                        _AppendText(MOD(i,2) & fnPfx, _StrJoin(CHAR(13)&CHAR(10), res['InsertNode_OBJ_Test'] )),
                        PrintLn( _StrFmt( '{0}: nSrcNodes={1}, nGeom={2}', i, i*partMaxSize+parts[i].COLUMNS(), nRes+res.COLUMNS() )),
                        let(nRes, nRes+res.COLUMNS()),
                        let(i,i+1)
                    )
                ),

                _AppendText(0&fnPfx, CHAR(13)&CHAR(10)&'GO'),
                _AppendText(1&fnPfx, CHAR(13)&CHAR(10)&'GO'),
                1
            )").task.Result;
            //var s = string.Join("\r\n", res.Select(r => r.ValuesList[0]));
        }

        static readonly int MaxParallelism = 4;// (Environment.ProcessorCount * 3 + 1) / 2;

        static void PipeGradient()
        {
            var refP = PVT.Prm.P._(PVT.Arg.P_SC, PVT.Arg.P_RES);
            var refT = PVT.Prm.T._(PVT.Arg.T_SC, PVT.Arg.T_RES);
            var GOR = 129.9;

            var root = PVT.NewCtx()
                .With(PVT.Arg.GAMMA_O, 0.824)
                //.With(PVT.Arg.Rsb, 50)
                .With(PVT.Arg.Rsb, 50)
                .With(PVT.Arg.GAMMA_G, 0.8)
                .With(PVT.Arg.GAMMA_W, 1.0)
                .With(PVT.Arg.S, 50000)
                .With(PVT.Arg.P_SC, U.Atm2MPa(1))
                .With(PVT.Arg.T_SC, 273 + 20)
                .With(PVT.Arg.P_RES, U.Atm2MPa(50))
                .With(PVT.Arg.T_RES, 273 + 90)
                .WithRescale(PVT.Prm.Bob._(1, 1.5), PVT.Bob_STANDING_1947, refP, refT)
                .With(PVT.Prm.Pb, PVT.Pb_STANDING_1947)
                .With(PVT.Prm.Rs, PVT.Rs_VELARDE_1996)
                //.WithRescale(PVT.Prm.Rs._(0d, PVT.Arg.Rsb), PVT.Rs_VELARDE_1996, refP, refT)
                .With(PVT.Prm.Bg, PVT.Bg_MAT_BALANS)
                .With(PVT.Prm.Z, PVT.Z_BBS_1974)
                .With(PVT.Prm.Bo, PVT.Bo_DEFAULT)
                .With(PVT.Prm.Bw, PVT.Bw_MCCAIN_1990)
                .With(PVT.Prm.Rho_o, PVT.Rho_o_MAT_BALANS)
                .With(PVT.Prm.Rho_w, PVT.Rho_w_MCCAIN_1990)
                .With(PVT.Prm.Rho_g, PVT.Rho_g_DEFAULT)
                //
                .With(PVT.Prm.Sigma_og, 0.00841) //PVT.Sigma_og_BAKER_SWERDLOFF_1956)
                .With(PVT.Prm.Sigma_wg, PVT.Sigma_wg_RAMEY_1973)
                .With(PVT.Prm.Mu_o, PVT.Mu_o_VASQUEZ_BEGGS_1980)
                .With(PVT.Prm.Mu_os, PVT.Mu_os_BEGGS_ROBINSON_1975)
                .With(PVT.Prm.Mu_od, PVT.Mu_od_BEAL_1946)
                .With(PVT.Prm.Mu_w, PVT.Mu_w_MCCAIN_1990)
                .With(PVT.Prm.Mu_g, PVT.Mu_g_LGE_MCCAIN_1991)
                .With(PVT.Prm.Tpc, PVT.Tpc_SUTTON_2005)
                .With(PVT.Prm.Ppc, PVT.Ppc_SUTTON_2005)
                .Done();
            var ctx = root.NewCtx()
                .With(PVT.Prm.T, 273 + 78.6391323860655)
                .With(PVT.Prm.P, U.Atm2MPa(20))
                .Done();

            var Qliq = 150d;
            var WCT = 1E-5;
            var gd = new Gradient.DataInfo();
            {
                var q_osc = Qliq * (1 - WCT);
                var q_wsc = Qliq * WCT;
                var q_gsc = U.Max(GOR, ctx[PVT.Arg.Rsb]) * q_osc;

                var bbg = Gradient.BegsBrill.Calc(ctx,
                    D_mm: 62,
                    theta: 0,
                    eps: 1.65E-05,
                    q_osc: q_osc, q_wsc: q_wsc, q_gsc: q_gsc,
                    gd: gd);
            }

            var P1 = PressureDrop.dropLiq(ctx, gd,
                D_mm: 62, L0_m: 0, L1_m: 1000,
                Roughness: 0.0,
                flowDir: PressureDrop.FlowDirection.Forward,
                P0_MPa: U.Atm2MPa(20), Qliq, WCT, GOR, dL_m: 20, dP_MPa: 1e-4, maxP_MPa: 60, stepHandler: null, 0,
                getTempK: (Qo, Qw, L) => 273 + 20,
                getAngle: _ => 0,
                gradCalc: Gradient.BegsBrill.Calc, WithFriction: false);
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

        static void Subnets()
        {
            var Calc_Time = DateTime.UtcNow;

            const string sDirTGF = "TGF";

            StringBuilder sbLog = null; // new StringBuilder();
            StringBuilder sbSql = null; // new StringBuilder();
            StringBuilder sbTgf = null; // new StringBuilder();
            bool BulkSave = true;

            IIndexedDict[] nodesArr, edgesArr;
            #region Получение исходных данных по вершинам и ребрам графа трубопроводов

            // Запуск загрузки справочника трансляции кодов OIS Pipe->PPM, если нужно
            Task<object> tO2P = (sbSql == null && !BulkSave) ? null
                : Calc(GetCtx_Pipe(), null, "OIS_2_PPM_PU( '' )").ParTask();

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
            Node[] nodes;
            #region Подготовка входных параметров для разбиения на подсети
            string[] colors;
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
                colors = colorDict.OrderBy(p => p.Value).Select(p => p.Key).ToArray();
            }
            #endregion

            var nodeWell = new Dictionary<int, NetCalc.WellInfo>();
            #region Подготовка данных по скважинам
            using (new Stopwatch("Load wells data"))
            {
                var dictWellOp = new Dictionary<string, (IIndexedDict press, IIndexedDict fluid)>();
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
                for (int iNode = 0; iNode < nodesArr.Length; iNode++)
                {
                    var wellID = nodesArr[iNode].GetStr("NodeObj_ID_Pipe");
                    if (wellID == null)
                        continue;
                    if (!dictWellOp.TryGetValue(wellID, out var t))
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

            var subnets = new List<int[]>();

            #region Поиск гидравлически единых подсетей

            var dictO2P = tO2P == null ? null : ((IIndexedDict[])tO2P.Result).ToDictionary(
                r => Convert.ToUInt64(r["Pipe_ID_Pipe"]),
                r => Get<Guid>(r, "Pipe_ID_PPM")
            );

            using (var log = new StreamWriter("SubNets.log", false, Encoding.ASCII))
            using (var sql = new StreamWriter("SubNets.sql", false, Encoding.ASCII))
            using (new Stopwatch("EnumSubnets"))
            {
                int min = int.MaxValue, max = 0, sum = 0;

                if (sbTgf != null)
                    foreach (var f in Directory.CreateDirectory(sDirTGF).EnumerateFiles())
                        if (f.Name.EndsWith(".tgf")) f.Delete();

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
                        if (sbSql != null)
                        {
                            if (dictO2P.TryGetValue(pu_id, out var pipe_id))
                                sbSql.AppendLine($"UPDATE PIPE SET ROUTE_ID='00000000-0000-0000-{nSubnets:X04}-000000000000' WHERE ENTRY_ID='{pipe_id}' AND TO_DATE IS NULL");
                            else { }
                        }
                    }

                    #region Export to TGF (Trivial Graph Format)
                    if (sbTgf != null)
                        using (var tw = new StreamWriter(Path.Combine(sDirTGF, $"{nSubnets}.tgf")))
                            NetCalc.ExportTGF(tw, edges, nodes, subnetEdges,
                                iNode => nodesArr[iNode].GetStr("Node_Name_Pipe"), null, null);
                    #endregion

                    if (sbLog != null)
                    {
                        sbLog.Append($"{n}\t{setOfPuIDs.Count}\t{colors[edges[subnetEdges[0]].color - 1]}\t(");
                        int k = 0;
                        foreach (var id in setOfPuIDs)
                        {
                            if (k > 0) sbLog.Append(',');
                            sbLog.Append(id);
                            if (++k == 1000)
                            { sbLog.Append(")\t("); k = 0; }
                        }
                        sbLog.AppendLine(")");
                    }

                    if ((nSubnets & 0xF) == 0)
                    {
                        if (sbLog != null) { log.Write(sbLog); sbLog.Clear(); }
                        if (sbSql != null) { sql.Write(sbSql); sbSql.Clear(); }
                    }

                    if (sbLog != null && sbLog.Length > 0) { log.Write(sbLog); sbLog.Clear(); }
                    if (sbSql != null && sbSql.Length > 0) { sql.Write(sbSql); sbSql.Clear(); }
                }
                Console.WriteLine($"nSubnets={subnets.Count}, min={min}, avg={sum / subnets.Count:g}, max={max}");
            }
            #endregion

            foreach (var f in Directory.CreateDirectory(sDirTGF).EnumerateFiles())
                if (f.Name.EndsWith(".tgf")) f.Delete();

            PipesCalc.HydrCalcDataRec[] recs;

            using (new Stopwatch("Calc on subnets"))
            {
                recs = PipesCalc.CalcSubnets(edges, nodes, subnets, nodeWell,
                    iSubnet =>
                    {
                        Console.Write($" {iSubnet}");
                        return new StreamWriter(Path.Combine(sDirTGF, $"{iSubnet + 1}.tgf"));
                    },
                    iNode => nodesArr[iNode].GetStr("Node_Name_Pipe")
                );
                Console.WriteLine();
            }

            if (BulkSave)
                using (new Stopwatch("Bulk save"))
                {
                    var connStr = @"Data Source = alferovav; Initial Catalog = PPM.Ugansk.Test; User ID = geoserver; Password = geo1412; Pooling = False";

                    using (var loader = new System.Data.SqlClient.SqlBulkCopy(connStr))
                    {
                        loader.DestinationTableName = "HYDR_CALC_DATA";
                        //loader.BatchSize = 1;
                        var reader = new BulkDataReader<PipesCalc.HydrCalcDataRec>(recs, (iEdge, r, vals) =>
                        {
                            int i = 0;
                            var pu_id = Convert.ToUInt64(edgesArr[iEdge]["Pu_ID_Pipe"]);
                            vals[i++] = dictO2P.TryGetValue(pu_id, out var g) ? g : Guid.Empty;
                            vals[i++] = Calc_Time;
                            r.GetValues(vals, ref i);
                        }, 39);

                        loader.WriteToServer(reader);
                    }
                }

            Console.WriteLine();
        }

        static void GradientPerf()
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            const int nIter = 100000;
            Parallel.For(0, nIter, i => PipeGradient());
            //for (int i = 0; i < nIter; i++) PipeGradient();
            sw.Stop();
            Console.WriteLine($"{sw.ElapsedMilliseconds}ms, PipeGradient/s = {1000 * nIter / sw.ElapsedMilliseconds}");
        }

        static void Main(string[] args)
        {
            OPs.GlobalMaxParallelismSemaphore = W.Common.Utils.NewAsyncSemaphore(MaxParallelism);
            //PPM_SQL().Wait();
            //RunDepsGraphFor(GetCtx_PPM("{ 'TimeSlice' }"), "Pipe_ID_PPM");
            //RunDepsGraphFor(GetCtx_Pipe(),"Pt_ID_Pipe");
            //Pipe_SQL().Wait();

            //var res = Calc_Try("PipeRow_ID_Pipe", "Pipe_ID_Pipe", "PipeLevel_RD_Pipe", "PipeParent_ID_Pipe", "");
            //var res = Calc_Try("Pu_RawGeom_Pipe", "Pu_Length_Pipe", "PuBegNode_ZCoord_Pipe", "PuEndNode_ZCoord_Pipe", "PuBegNode_Name_Pipe", "PuEndNode_Name_Pipe", "UtOrg_Name_Pipe").Result;

            //Pipe_Geometry();
            //Node_Geometry();
            //GradientPerf();

            Subnets();
#if !DEBUG
            Console.Write("Press Enter to exit...");
            Console.ReadLine();
#endif
            { }
        }
    }



}
