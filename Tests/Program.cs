using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pipe.Exercises
{
    using System.Collections;
    using W.Common;
    using W.Expressions;
    using W.Expressions.Sql;
    using W.Oilca;

    class Program
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
            db::UseSqlAsFuncsFrom('Pipe.meta.sql', { 'TimeSlice' }, oraConn, 'Pipe'),
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
            return Calc(GetCtx_Pipe(), null, @"(
db::SqlFuncsToText('Pipe').._WriteAllText('Pipe.unfolded.sql')
)").task;
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

        static void ConvGeometry()
        {
            /*
            // 02
            var cnvToWGS = GeoCoordConv.GetConvToWGS84(GeoCoordConv.PROJCS_MSK02_1);
            var srcPoint = new double[] { 1298411.51, 540416.08, 0 };
            // 86
            /*/
            var cnvToWGS = GeoCoordConv.GetConvToWGS84(GeoCoordConv.PROJCS_MSK86_3);
            var (dX, dY) = (3157817.641, -5810365.348);
            //var srcPoint = new double[] { 372338.09 + dX, 6731357.6 + dY, 0 }; //
            //var srcPoint = new double[] { 375054.48 + dX, 6738395 + dY, 0 }; // вр. кон.дюк.р.Пыть-Ях (1)
            var srcPoint = new double[] { 375088.86 + dX, 6738338.91 + dY, 0 }; // вр. кон.дюк.р.Пыть-Ях (2)
            //*/
            var dstPoint = cnvToWGS(srcPoint);
        }

        static readonly int MaxParallelism = 2;// (Environment.ProcessorCount * 3 + 1) / 2;

        static void PipeGradient()
        {
            var refP = PVT.Prm.P._(PVT.Arg.P_SC, PVT.Arg.P_RES);
            var refT = PVT.Prm.T._(PVT.Arg.T_SC, PVT.Arg.T_RES);
            var GOR = 129.9;

            var root = PVT.NewCtx()
                .With(PVT.Arg.GAMMA_O, 0.824)
                .With(PVT.Arg.Rsb, 50)
                .With(PVT.Arg.GAMMA_G, 0.8)
                .With(PVT.Arg.GAMMA_W, 1.0)
                .With(PVT.Arg.S, 50000)
                .With(PVT.Arg.P_SC, 0.1)
                .With(PVT.Arg.T_SC, 273 + 20)
                .With(PVT.Arg.P_RES, 50 * 0.101325)
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

            var steps = new List<PressureDrop.StepInfo>();
            var P1 = PressureDrop.dropLiq(ctx, gd,
                D_mm: 62, L0_m: 0, L1_m: 1000,
                Roughness: 0.0,
                flowDir: PressureDrop.FlowDirection.Forward,
                P0_MPa: U.Atm2MPa(20), Qliq, WCT, GOR, dL_m: 20, dP_MPa: 1e-4, maxP_MPa: 60, stepsInfo: steps,
                getTempK: (Qo, Qw, L) => 273 + 20,
                getAngle: _ => 0,
                gradCalc: Gradient.BegsBrill.Calc, WithFriction: false);
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
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            const int nIter = 100000;
            Parallel.For(0, nIter, i => PipeGradient());
            //for (int i = 0; i < nIter; i++) PipeGradient();
            sw.Stop();
            Console.WriteLine($"{sw.ElapsedMilliseconds}ms, PipeGradient/s = {1000 * nIter / sw.ElapsedMilliseconds}");

            { }
            Console.ReadLine();
        }
    }



}
