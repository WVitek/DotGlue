using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pipe.Excercises
{
    class Program
    {
        public class UndefinedValuesException : System.Exception { public UndefinedValuesException(string msg) : base(msg) { } }

        static void Main(string[] args)
        {
            W.Expressions.OPs.GlobalMaxParallelismSemaphore = W.Common.Utils.NewAsyncSemaphore((Environment.ProcessorCount * 3 + 1) / 2);

            var funcDefs = new W.Expressions.FuncDefs()
                .AddFrom(typeof(W.Expressions.FuncDefs_Core))
                .AddFrom(typeof(W.Expressions.FuncDefs_Excel))
                .AddFrom(typeof(W.Expressions.FuncDefs_Report))
                ;
            var valsDefs = new Dictionary<string, object>();

            var codeText = @"_include('Init.glue.h')";

            //***** формируем по тексту синтаксическое дерево 
            var e = W.Expressions.Parser.ParseToExpr(codeText);
            //***** создаём контекст кодогенератора, содержащий предопределённые значения и перечень функций
            var ctx = new W.Expressions.Generator.Ctx(valsDefs, funcDefs.GetFuncs);
            //***** формируем код
            var g = W.Expressions.Generator.Generate(e, ctx);
            //***** проверяем, заданы ли выражения для всех именованных значений
            try { ctx.CheckUndefinedValues(); }
            catch ( W.Expressions.Generator.Exception ex) { throw new UndefinedValuesException(ex.Message); }
            //***** вычисление
            var calcRes = W.Expressions.OPs.GlobalValueOfAny(g, ctx, System.Threading.CancellationToken.None).Result;
            //
            { }// Console.ReadLine();
        }
    }
}
