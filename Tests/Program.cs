using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pipe.Excercises
{
    using W.Expressions;
    using W.Expressions.Sql;

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

        static void Main(string[] args)
        {
            OPs.GlobalMaxParallelismSemaphore = W.Common.Utils.NewAsyncSemaphore((Environment.ProcessorCount * 3 + 1) / 2);

            //PPM_SQL().Wait();
            //RunDepsGraphFor(GetCtx_PPM("{ 'TimeSlice' }"), "Pipe_ID_PPM");
            //RunDepsGraphFor(GetCtx_Pipe(),"Pt_ID_Pipe");
            Pipe_SQL().Wait();
            //var res = Calc_Try().Result;


            { }// Console.ReadLine();
        }
    }



}
