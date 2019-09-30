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
        "ID", "ID", "code",
        "ClCD", "clcd", "string",
        "Name", "name", "string",
        "Shortname", "shortname", "string",
        "Descr", "descr", "string",
        "Dict", "dict", "object",
        "RawGeom", "rawgeom", "bytes",
        "XCoord", "xcoord", "1",
        "YCoord", "ycoord", "1",
        "ZCoord", "zcoord", "1"
    )]
    public static class FuncDefs_Pipe
    {
        class ClassItem
        {
            public string Code, Name, ShortName;
            public override string ToString() => $"[{Code}]\t{Name}";
        }

        [ArgumentInfo(0, "ClassItem_CLCD_PIPE")]
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

        static string EscapeSpecialChars(string s)
        {
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                string r = null;
                if (s[i] < ' ')
                    r = $"'+CHAR({(int)s[i]})+'";
                else if (s[i] == '\'')
                    r = "''";
                else
                {
                    if (sb != null)
                        sb.Append(s[i]);
                    continue;
                }
                if (sb == null)
                    sb = new StringBuilder(s.Substring(0, i));
                sb.Append(r);
            }
            if (sb == null)
                return s;
            return sb.ToString();
        }

        [ArgumentInfo(0, "Pu_RawGeom_Pipe")]
        [ArgumentInfo(1, "Pu_Length_Pipe")]
        [ArgumentInfo(2, "PuBegNode_ZCoord_Pipe")]
        [ArgumentInfo(3, "PuEndNode_ZCoord_Pipe")]
        [ArgumentInfo(4, "PuBegNode_Name_Pipe")]
        [ArgumentInfo(5, "PuEndNode_Name_Pipe")]
        [ArgumentInfo(6, "UtOrg_Name_Pipe")]
        //[ArgumentInfo(7, "Pu_ID_Pipe")]
        [return: ResultInfo(0, "InsertPipe_OBJ_Test")]
        public static object PipeGeomConv(object arg)
        {
            return Utils.Calc(arg, 7, 1, args =>
            {
                var raw = Utils.Cast<byte[]>(args[0]);
                var MaxL = Convert.ToDouble(args[1]);
                var Z0 = Convert.ToDouble(args[2]);
                var Z1 = Convert.ToDouble(args[3]);
                var res = PipeGeometryUtils.FromPipeCoords(raw, MaxL, Z0, Z1);
                if ((string.IsNullOrEmpty(res.errMsg) || !res.errMsg.Contains("Lcalc >")) && res.points.Length >= 2)
                {
                    var toWGS = GeoCoordConv.GetConvByOrganization(Convert.ToString(args[6]));
                    if (toWGS.conv == null)
                        return null;
                    var sb = new StringBuilder("LINESTRING(");
                    PipeGeometryUtils.LineToWKT(res.points.Select(p => toWGS.conv(p.coords)), sb, 2);
                    sb.Append(")");
                    var PuId = Convert.ToInt64(args[7]); // undocumented feature, most significant key value is after last data value
                    var Pipe_ID = PuId * 10 + toWGS.id;
                    var Name = EscapeSpecialChars($"{args[4]}-{args[5]}");
                    var ins = $"INSERT INTO wgs_pipe(Pipe_ID, Name, Geometry) VALUES ({Pipe_ID}, '{Name}', geometry::STGeomFromText('{sb}',4326))";
                    return ins;
                }
                return null;
            }, 0, 1);
        }

        [ArgumentInfo(0, "PipeNode_Descr_Pipe")]
        [ArgumentInfo(1, "PipeNode_XCoord_Pipe")]
        [ArgumentInfo(2, "PipeNode_YCoord_Pipe")]
        [ArgumentInfo(3, "NodeType_Name_Pipe")]
        [ArgumentInfo(4, "PipeNodeOrg_Name_Pipe")]
        //[ArgumentInfo(5, "PipeNode_ID_Pipe")]
        [return: ResultInfo(0, "InsertNode_OBJ_Test")]
        public static object NodeGeomConv(object arg)
        {
            return Utils.CalcNotNulls(arg, 5, 1, args =>
            {
                var toWGS = GeoCoordConv.GetConvByOrganization(Convert.ToString(args[4]));
                if (toWGS.conv == null)
                    return null;
                //***** INSERT
                var sb = new StringBuilder("INSERT INTO MARKER(Marker_ID,NAME,TYPE_RD,Geometry) VALUES (");
                //***** Marker_ID
                var Node_ID = Convert.ToInt64(args[5]); // undocumented feature, most significant key value is after last data value
                sb.Append(Node_ID * 10 + toWGS.id); sb.Append(',');
                //***** NAME
                sb.Append('\''); sb.Append(EscapeSpecialChars(Convert.ToString(args[0]))); sb.Append("',");
                //***** TYPE_RD
                sb.Append('\''); sb.Append(EscapeSpecialChars(Convert.ToString(args[3]))); sb.Append("',");
                //***** Geometry
                var X = Convert.ToDouble(args[1]);
                var Y = Convert.ToDouble(args[2]);
                var wgs = toWGS.conv(new double[2] { X, Y });
                sb.Append("geometry::STGeomFromText('POINT("); PipeGeometryUtils.PointToWKT(wgs, sb, 2); sb.Append(")',4326)");
                //***** trailing bracket
                sb.Append(')');
                return sb.ToString();
            });
        }
    }
}
