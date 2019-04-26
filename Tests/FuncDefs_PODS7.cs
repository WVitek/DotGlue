using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using W.Common;
using W.Expressions;
using W.Expressions.Sql;

namespace Pipe.Exercises
{
    [DefineQuantities(
        //"name", "name", "string",
        //"shortname", "shortname", "string",
        "dict", "dict", "object"
    )]

    class CodeLookupEntry { public string code, description, code_supedcedes; }

    public static class FuncDefs_PODS7
    {

        /// <summary>
        /// Load content of _CL data table into dictionary
        /// </summary>
        /// <param name="args">0: DB connection; 1: *_CL table name</param>
        /// <returns></returns>
        [Arity(2, 2)]
        static async Task<object> LoadCodeLookupDictRows(AsyncExprCtx ae, IList args)
        {
            var conn = (IDbConn)await OPs.ConstValueOf(ae, args[0]);
            var table = Convert.ToString(await OPs.ConstValueOf(ae, args[1])).Replace(' ', '_').Replace(',', '_');
            var query = $"SELECT code, description, code_supercedes FROM {table}";
            var cmd = new SqlCommandData() { Kind = CommandKind.Query, SqlText = query, ConvertMultiResultsToLists = false };
            var rows = (IIndexedDict[])await conn.ExecCmd(cmd, ae.Cancellation);
            var dict = new Dictionary<string, CodeLookupEntry>(rows.Length);
            foreach (var r in rows)
            {
                var entry = new CodeLookupEntry()
                {
                    code = r.ValuesList[0].ToString(),
                    description = Convert.ToString(r.ValuesList[1]),
                    code_supedcedes = Convert.ToString(r.ValuesList[2]),
                };
                dict.Add(entry.code, entry);
            }
            // incapsulate into tuple to mask IList interface
            return Tuple.Create(dict);
        }

        /// <summary>
        /// Create functions to support code lookups dictionaries
        /// </summary>
        [Arity(3, 3)]
        public static object CodeLookupHelperFuncs(CallExpr ce, Generator.Ctx ctx)
        {
            var dbConn = ce.args[0];
            var dbConnName = OPs.TryAsName(dbConn, ctx);
            if (dbConnName == null)
                throw new Generator.Exception($"Can't get DB connection name: {dbConn}");

            var pairs = ctx.GetConstant(ce.args[1]) as IList;
            if (pairs == null)
                throw new Generator.Exception($"Can't interpet as array of constants: {ce.args[1]}");
            if (pairs.Count % 2 != 0)
                throw new Generator.Exception($"Items count must be even (cause it must be array of pairs): {ce.args[1]}");

            //var dbTable = ce.args[1];
            //var dbTableName = OPs.TryAsString(dbTable, ctx);
            //var substance = Convert.ToString(ctx.GetConstant(ce.args[2]));
            var location = Convert.ToString(ctx.GetConstant(ce.args[2]));

            var defs = new List<FuncDef>(pairs.Count / 2);

            for (int i = 0; i < pairs.Count; i += 2)
            {
                var dbTableName = pairs[i].ToString();
                var substance = pairs[i + 1].ToString();

                // Dictionary loader func
                defs.Add(FuncDefs_Core.macroFuncImpl(ctx,
                    // name
                    new ConstExpr($"{dbConnName}:{dbTableName}_LoadDict"),
                    // inputs
                    new ArrayExpr(),
                    // outputs
                    new ArrayExpr(new ConstExpr($"{substance}_DICT_{location}")),
                    // body
                    new CallExpr(LoadCodeLookupDictRows, dbConn, new ConstExpr(dbTableName)),
                    //
                    null, false
                    ));
            }



            return defs;
        }
    }
}
