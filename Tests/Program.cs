using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pipe.Excercises
{
    using W.Expressions;

    class Program
    {
        public class UndefinedValuesException : System.Exception { public UndefinedValuesException(string msg) : base(msg) { } }

        static (Generator.Ctx gCtx, AsyncExprCtx eCtx) GetRootCtx()
        {
            var obj = FuncDefs_Core._Cached("$GlobalContexts", () =>
            {
                var funcDefs = new W.Expressions.FuncDefs()
                    .AddFrom(typeof(W.Expressions.FuncDefs_Core))
                    .AddFrom(typeof(W.Expressions.FuncDefs_Excel))
                    .AddFrom(typeof(W.Expressions.FuncDefs_Report))
                    ;
                var valsDefs = new Dictionary<string, object>();

                var codeText = @"_include('Init.glue.h')";

                //***** формируем по тексту синтаксическое дерево 
                var e = Parser.ParseToExpr(codeText);
                //***** создаём контекст кодогенератора, содержащий предопределённые значения и перечень функций
                var ctx = new Generator.Ctx(valsDefs, funcDefs.GetFuncs);
                //***** формируем код
                var g = Generator.Generate(e, ctx);
                //***** проверяем, заданы ли выражения для всех именованных значений
                try { ctx.CheckUndefinedValues(); }
                catch (W.Expressions.Generator.Exception ex) { throw new UndefinedValuesException(ex.Message); }
                AsyncExprCtx ae = new AsyncExprRootCtx(ctx.name2ndx, ctx.values, OPs.GlobalMaxParallelismSemaphore);
                return (ctx, ae);
            }, DateTime.UtcNow.AddMinutes(5), System.Web.Caching.Cache.NoSlidingExpiration);

            return ((Generator.Ctx, AsyncExprCtx))obj;
        }

        static (Task<object> task, Generator.Ctx gCtx) Calc(IDictionary<string, object> defs, string codeText)
        {
            var e = Parser.ParseToExpr(codeText);

            var root = GetRootCtx();
            var ctx = new Generator.Ctx(root.gCtx);

            if (defs != null)
                foreach (var def in defs)
                    ctx.CreateValue(def.Key, def.Value);

            var g = Generator.Generate(e, ctx);
            try { ctx.CheckUndefinedValues(); }
            catch (Generator.Exception ex) { throw new UndefinedValuesException(ex.Message); }

            var ae = new AsyncExprCtx(ctx, ctx.values, root.eCtx);

            return (OPs.ConstValueOf(ae, g), ctx);
        }


        static Task PPM_SQL()
        {
            return Calc(null, @"(
db::SqlFuncsToText('PPM').._WriteAllText('PPM.unfolded.sql'),
let( sqls, db::SqlFuncsToDDL('PPM')), 
sqls[0].._WriteAllText('PPM.genDDL.sql'), 
sqls[1].._WriteAllText('PPM.drops.sql'),
)
"
            ).task;
        }

        static void RunDepsGraph()
        {
            var defs = new Dictionary<string, object>();

            var t = Calc(defs, "solver::FindDependencies({'PIPE_ID_PPM'}, , {'A_TIME__XT', 'B_TIME__XT', 'MIN_TIME__XT', 'MAX_TIME__XT' } )");
            var outParams = ((IDictionary<string, object>)t.task.Result).Keys.ToArray();
            Console.WriteLine(outParams);

            defs.Add(FuncDefs_Solver.optionSolverDependencies, null);

            //string[] outParams =
            //{
            //    "ElbowSpec_DESCR_PPM"
            //};

            var (task, gCtx) = Calc(defs,
@"(
	solver::FindSolutionExpr({'Pipe_ID_PPM','AT_TIME__XT'}, {" + string.Join(", ", outParams.Select(s => '\'' + s + '\'')) + @"})
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
            System.IO.File.WriteAllText("DepsGraph.gv", sGV, Encoding.UTF8); // write with BOM
            //var txt = Convert.ToString(W.Expressions.FuncDefs_Report._IDictsToStr(caps));
            //System.IO.File.WriteAllText("DepsGraphParamsNames.log", txt);
        }

        static void Main(string[] args)
        {
            OPs.GlobalMaxParallelismSemaphore = W.Common.Utils.NewAsyncSemaphore((Environment.ProcessorCount * 3 + 1) / 2);

            //PPM_SQL().Wait();
            RunDepsGraph();


            { }// Console.ReadLine();
        }
    }



}
