using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using W.Common;
using W.Expressions;

namespace Pipe.Exercises
{
    [DefineQuantities(
        "ClassCD", "classcd", "string",
        "Name", "name", "string",
        "Shortname", "shortname", "string",
        "Dict", "dict", "object"
    )]
    public static class FuncDefs_Pipe
    {
        class ClassItem
        {
            public string Code, Name, ShortName;
            public override string ToString() => $"[{Code}]\t{Name}";
        }

        [ArgumentInfo(0, "ClassItem_CLASSCD_PIPE")]
        [ArgumentInfo(1, "ClassItem_NAME_PIPE")]
        [ArgumentInfo(2, "ClassItem_SHORTNAME_PIPE")]
        [return: ResultInfo(0, "CLASS_DICT_PIPE")]
        public static object GetClassDictObj(object arg)
        {
            var dict = (IIndexedDict)arg;
            var codes = Utils.Cast<IList>(dict.ValuesList[0]);
            var names = Utils.Cast<IList>(dict.ValuesList[1]);
            var shnms = Utils.Cast<IList>(dict.ValuesList[2]);
            Dictionary<string, ClassItem> res = Enumerable.Range(0, codes.Count).ToDictionary(i => Convert.ToString(codes[i]), i => new ClassItem()
            {
                Code = Convert.ToString(codes[i]),
                Name = Convert.ToString(names[i]),
                ShortName = Convert.ToString(shnms[i])
            });
            return Tuple.Create(res);
        }

        /// <summary>
        /// Decode value by using PIPE.CLASS table
        /// </summary>
        /// <param name="arg">0: CLASSCODE_PIPE; 1: CLASS_DICT_PIPE</param>
        /// <returns>0: entity_name; 1: entity_shortname</returns>
        //[ArgumentInfo(0, "CLASSCODE_PIPE")]
        //[ArgumentInfo(1, "CLASS_DICT_PIPE")]
        //[return: ResultInfo(0, "entity_NAME")]
        //[return: ResultInfo(1, "entity_SHORTNAME")]
        [Arity(2, 2)]
        public static object GetClassInfo(object arg)
        {
            return Utils.CalcNotNulls(arg, 2, 2,
                args =>
                {
                    var classDict = Utils.Cast<Tuple<Dictionary<string, ClassItem>>>(args[1]).Item1;
                    var classCode = Convert.ToString(args[0]);
                    if (classDict.TryGetValue(classCode, out var item))
                        return new object[2] { item.Name, item.ShortName };
                    return null;
                });
        }
    }
}
