using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using W.Common;

namespace Pipe.Exercises
{
    [DefineQuantities(
        "classcode", "classcode", "string",
        "name", "name", "string",
        "shortname", "shortname", "string",
        "dict", "dict", "object"
    )]
    public static class FuncDefs_Pipe
    {
        [ArgumentInfo(0, "ClassItem_CLASSCODE_PIPE")]
        [ArgumentInfo(1, "ClassItem_NAME_PIPE")]
        [ArgumentInfo(2, "ClassItem_SHORTNAME_PIPE")]
        [return: ResultInfo(0, "CLASS_DICT_PIPE")]
        public static object ErpNameToEspIdDict(object arg)
        {
            var dict = (IIndexedDict)arg;
            return null;
            //return Utils.Calc(arg, 2, 1, args =>
            //{
            //    var lst0 = Utils.ToIList(args[0]);
            //    var lst1 = Utils.ToIList(args[1]);
            //    int n = lst0.Count;
            //    var lstDict = new List<KeyValuePair<string, int>>();
            //    for (int i = n - 1; i >= 0; i--)
            //    {
            //        var pfx = Convert.ToString(lst0[i]);
            //        var id = Convert.ToInt32(lst1[i]);
            //        lstDict.Add(new KeyValuePair<string, int>(pfx, id));
            //    }
            //    //lstDict.Sort(PrefixComparer.Instance);
            //    // Оборачиваем в Tuple для скрытия интерфейса IList
            //    return new Tuple<List<KeyValuePair<string, int>>>(lstDict);
            //});
        }
    }
}
