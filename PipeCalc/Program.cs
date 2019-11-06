using PipeNetCalc;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using W.Common;
using W.Expressions;

namespace PPM.HydrCalcPipe
{
    static class Program
    {
        static int GetColor(Dictionary<string, int> dict, object prop)
        {
            var key = Convert.ToString(prop);
            if (dict.TryGetValue(key, out int c))
                return c;
            int i = dict.Count + 1;
            dict.Add(key, i);
            return i;
        }

        static float GetFlt(this DbDataReader rdr, int iColumn, float defVal = float.NaN)
            => rdr.IsDBNull(iColumn) ? defVal : rdr.GetFloat(iColumn);

        static string GetStr(this DbDataReader rdr, int iColumn, string defVal = default)
            => rdr.IsDBNull(iColumn) ? defVal : rdr.GetString(iColumn);

        static ulong GetUInt64(this DbDataReader rdr, int iColumn, ulong defVal = default)
            => rdr.IsDBNull(iColumn) ? defVal : (ulong)rdr.GetInt64(iColumn);

        static
            (Edge[] edges, Node[] nodes, string[] colorName, string[] nodeName, ulong[] edgeID,
            Dictionary<int, NetCalc.WellInfo> nodeWell)

            PrepareInputData2(DbConnection dbConnOisPipe, DbConnection dbConnWellOP)
        {
            var nodesLst = new List<(
                ulong PipeNode_ID,
                int NodeType_ID,
                ulong NodeObj_ID,
                string Node_Name
            )>();

            var edgesLst = new List<(
                ulong Pu_ID,
                ulong PuBegNode_ID,
                ulong PuEndNode_ID,
                string PuFluid_ClCD,
                float Pu_Length,
                float Pu_InnerDiam
            )>();

            #region Получение исходных данных по вершинам и ребрам графа трубопроводов
            using (new StopwatchMs("Load data from Pipe (ORA)"))
            {
                using (var cmd = dbConnOisPipe.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT 
    ""ID узла"",
    ""ID тип узла"",
    ""Код объекта"",
    ""Название"" 
FROM pipe_node";
                    using (var rdr = cmd.ExecuteReader())
                        while (rdr.Read())
                            nodesLst.Add((
                                PipeNode_ID: rdr.GetUInt64(0),
                                NodeType_ID: rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1),
                                NodeObj_ID: rdr.GetUInt64(2),
                                Node_Name: rdr.GetStr(3)
                            ));
                }

                using (var cmd = dbConnOisPipe.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT
	pu.""ID участка"" AS Ut_ID,
    pu.""ID простого участка"" AS Pu_ID,
    pu.""Узел начала участка""  AS PuBegNode_ID,
    pu.""Узел конца участка""  AS PuEndNode_ID,
    ut.""Рабочая среда""  AS PuFluid_ClCD,
    pu.""L""  AS Pu_Length,
    ut.D - ut.S  AS Pu_InnerDiam,
    ut.S AS Pu_Thickness,
    ut.D AS Pu_OuterDiam
FROM pipe_prostoy_uchastok pu
    JOIN pipe_uchastok_truboprovod ut ON pu.""ID участка"" = ut.""ID участка""
WHERE 1 = 1
    AND pu.""Состояние"" = 'HH0004'
    AND ut.""Состояние"" = 'HH0004'
    AND pu.""ID простого участка"" NOT IN (SELECT ""ID простого участка"" FROM pipe_armatura WHERE ""Состояние задвижки"" = 'HX0002')
";
                    using (var rdr = cmd.ExecuteReader())
                        while (rdr.Read())
                        {
                            edgesLst.Add((
                                Pu_ID: rdr.GetUInt64(1),
                                PuBegNode_ID: rdr.GetUInt64(2),
                                PuEndNode_ID: rdr.GetUInt64(3),
                                PuFluid_ClCD: rdr.GetStr(4),
                                Pu_Length: rdr.GetFlt(5, float.Epsilon),
                                Pu_InnerDiam: rdr.GetFlt(6)
                            ));
                        }
                }
            }
            #endregion

            Edge[] edges;
            Node[] nodes;
            string[] colorName;
            #region Подготовка входных параметров для разбиения на подсети
            {
                var nodesDict = Enumerable.Range(0, nodesLst.Count).ToDictionary(
                        i => nodesLst[i].PipeNode_ID,
                        i => (Ndx: i,
                            TypeID: nodesLst[i].NodeType_ID,
                            Altitude: 0f
                        )
                    );
                var colorDict = new Dictionary<string, int>();
                edges = edgesLst.Select(
                    r => new Edge()
                    {
                        iNodeA = nodesDict.TryGetValue(r.PuBegNode_ID, out var eBeg) ? eBeg.Ndx : -1,
                        iNodeB = nodesDict.TryGetValue(r.PuEndNode_ID, out var eEnd) ? eEnd.Ndx : -1,
                        color = GetColor(colorDict, r.PuFluid_ClCD),
                        D = r.Pu_InnerDiam,
                        L = r.Pu_Length,
                    })
                    .ToArray();
                nodes = nodesDict
                    .Select(p => new Node() { kind = (NodeKind)p.Value.TypeID, Node_ID = p.Key.ToString(), Altitude = p.Value.Altitude })
                    .ToArray();
                colorName = colorDict.OrderBy(p => p.Value).Select(p => p.Key).ToArray();
            }
            #endregion

            var nodeWell = new Dictionary<int, NetCalc.WellInfo>();
            #region Подготовка данных по скважинам
            {
                var dictWellOp = new Dictionary<ulong, NetCalc.WellInfo>();

                using (new StopwatchMs("Load wells data from WELL_OP"))
                {
                    using (var cmd = dbConnWellOP.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT
    well_id  Well_ID_OP,
    inline_pressure Line_Pressure__Atm
FROM well_op_oil
WHERE calc_date BETWEEN to_date('20190101', 'yyyymmdd') AND to_date('20190131', 'yyyymmdd') 
";
                        using (var rdr = cmd.ExecuteReader())
                            while (rdr.Read())
                            {
                                var wellID = rdr.GetUInt64(0);
                                dictWellOp[wellID] = new NetCalc.WellInfo()
                                {
                                    Well_ID = wellID.ToString(),
                                    Line_Pressure__Atm = rdr.GetFlt(1),
                                };
                            }
                    }

                    using (var cmd = dbConnWellOP.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT
	well_id  AS Well_ID_OP,
	layer_id  AS Layer_ClCD,
	ROUND(liq_rate,6)  AS Liq_VolRate, 
	ROUND(water_cut,6)   AS Liq_Watercut,
	ROUND(oil_compressibility,6)  AS Oil_Comprssblty,
	ROUND(bubble_point_pressure,6)  AS Bubblpnt_Pressure__Atm,
	ROUND(gas_factor,6)  AS Oil_GasFactor,
	ROUND(oil_density,6)  AS Oil_Density,
	ROUND(water_density,6)  AS Water_Density,
	ROUND(NVL(init_shut_pressure, layer_shut_pressure),6)  AS LayerShut_Pressure__Atm,
	ROUND(temperature,6)  AS Layer_Temperature__C,
	ROUND(water_viscosity,6)  AS Water_Viscosity,
	ROUND(oil_viscosity,6)  AS Oil_Viscosity
FROM well_layer_op
WHERE calc_date BETWEEN to_date('20190101', 'yyyymmdd') AND to_date('20190131', 'yyyymmdd') 
";
                        using (var rdr = cmd.ExecuteReader())
                            while (rdr.Read())
                            {
                                int i = 0;
                                var wellID = rdr.GetUInt64(0);
                                if (!dictWellOp.TryGetValue(wellID, out var wi))
                                {
                                    wi = new NetCalc.WellInfo() { Well_ID = wellID.ToString() };
                                    dictWellOp[wellID] = wi;
                                }
                                else if (wi.Layer == "PL0000")
                                    // если прочитан пласт с агрегированной информацией (псевдо-пласт "PL0000")
                                    // остальные строки по скважине пропускаем
                                    continue;
                                wi.Layer = rdr.GetStr(++i);
                                wi.Liq_VolRate = rdr.GetFlt(++i);
                                wi.Liq_Watercut = rdr.GetFlt(++i) * 0.01f;
                                wi.Oil_VolumeFactor = rdr.GetFlt(++i);
                                wi.Bubblpnt_Pressure__Atm = rdr.GetFlt(++i);
                                wi.Oil_GasFactor = rdr.GetFlt(++i);
                                wi.Oil_Density = rdr.GetFlt(++i);
                                wi.Water_Density = rdr.GetFlt(++i);
                                wi.Gas_Density = 0.8f;
                                wi.Reservoir_Pressure__Atm = rdr.GetFlt(++i);
                                wi.Temperature__C = rdr.GetFlt(++i);
                                wi.Water_Viscosity = rdr.GetFlt(++i);
                                wi.Oil_Viscosity = rdr.GetFlt(++i);
                            }
                    }
                }

                for (int iNode = 0; iNode < nodesLst.Count; iNode++)
                {
                    var wellID = nodesLst[iNode].NodeObj_ID;
                    if (wellID != default && dictWellOp.TryGetValue(wellID, out var wi))
                        nodeWell.Add(iNode, wi);
                }
            }

            #endregion

            var edgeID = edgesLst.Select(r => r.Pu_ID).ToArray();
            var nodeName = nodesLst.Select(r => r.Node_Name).ToArray();

            return (edges, nodes, colorName, nodeName, edgeID, nodeWell);
        }

        /// <summary>
        /// Поиск гидравлически единых подсетей
        /// </summary>
        /// <param name="edges"></param>
        /// <param name="nodes"></param>
        /// <returns>Список подсетей (подсеть - массив индексов относящихся к ней рёбер)</returns>
        static List<int[]> GetSubnets(Edge[] edges, Node[] nodes)
        {
            var subnets = new List<int[]>();
            using (new StopwatchMs("Splitting into subnets"))
            {
                int min = int.MaxValue, max = 0, sum = 0;

                foreach (var subnetEdges in Graph.Subnets(edges, nodes))
                {
                    subnets.Add(subnetEdges);
                    int n = subnetEdges.Length;
                    if (n < min) min = n;
                    if (n > max) max = n;
                    sum += n;
                }
                Console.WriteLine($"nSubnets={subnets.Count}, min={min}, avg={sum / subnets.Count:g}, max={max}");
            }
            return subnets;
        }

        static PipesCalc.HydrCalcDataRec[] HydrCalc(
            Edge[] edges, Node[] nodes, List<int[]> subnets,
            Dictionary<int, NetCalc.WellInfo> nodeWell,
            string[] nodeNameForTGF,
            string dirForTGF = null)
        {
            if (dirForTGF != null)
                foreach (var f in Directory.CreateDirectory(dirForTGF).EnumerateFiles())
                    if (f.Name.EndsWith(".tgf")) f.Delete();

            PipesCalc.HydrCalcDataRec[] recs;

            using (new StopwatchMs("Calc on subnets"))
            {
                recs = PipesCalc.CalcSubnets(edges, nodes, subnets, nodeWell,
                    iSubnet =>
                    {
#if DEBUG
                        Console.Write($" {iSubnet}");
#endif
                        if (dirForTGF == null)
                            return null;
                        return new StreamWriter(Path.Combine(dirForTGF, $"{iSubnet + 1}.tgf"));
                    },
                    iNode => nodeNameForTGF[iNode]
                );
#if DEBUG
                Console.WriteLine();
#endif
            }
            return recs;
        }

        static void Main(string[] args)
        {
            var CalcBeg_Time = DateTime.UtcNow;

            //const string connStr = @"Data Source = alferovav; Initial Catalog = PPM.Ugansk.Test; Trusted_Connection=True";
            const string csPPM = @"Data Source = alferovav; Initial Catalog = PPM.Ugansk.Test; User ID = ppm; Password = 123; Pooling = False";
            const string csPipe = "DATA SOURCE =\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=pb.ssrv.tk)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=oralin)))\";PASSWORD=pipe48;USER ID=pipe48;HA EVENTS=False;POOLING=False;LOAD BALANCING=False;";
            const string csWellOP = "DATA SOURCE =\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=pb.ssrv.tk)(PORT=1522))(CONNECT_DATA=(SERVICE_NAME=oralin2)))\";PASSWORD=WELLOP;USER ID=WELLOP;HA EVENTS=False;POOLING=False;LOAD BALANCING=False;";

            // обходим баг? загрузки "Microsoft.Data.SqlClient.resources"
            using (var c = new Microsoft.Data.SqlClient.SqlConnection(csPPM)) { c.Open(); c.Close(); }

            PipesCalc.HydrCalcDataRec[] edgeRec;
            ulong[] edgeOisPipeID = null;

            using (var dbConnOisPipe = new Oracle.ManagedDataAccess.Client.OracleConnection(csPipe))
            using (var dbConnWellOP = new Oracle.ManagedDataAccess.Client.OracleConnection(csWellOP))
            {
                dbConnOisPipe.Open();
                dbConnWellOP.Open();

                try
                {
                    var (edges, nodes, colorName, nodeName, edgeID, nodeWell) = PrepareInputData2(dbConnOisPipe, dbConnWellOP);
                    //var (edges, nodes, colorName, nodeName, edgeID, nodeWell) = PrepareInputData();

                    var subnets = GetSubnets(edges, nodes);

                    edgeRec = HydrCalc(edges, nodes, subnets, nodeWell, nodeName, "TGF");
                    //edgeRec = HydrCalc(edges, nodes, subnets, nodeWell, nodeName, null);
                    edgeOisPipeID = edgeID;
                }
                catch { edgeRec = null; }
            }

            //Guid Calc_ID = SaveToDB.CreateCalculationRec(csPPM, CalcBeg_Time, edgeRec);
            //if (edgeRec != null)
            //    SaveToDB.SaveResults(csPPM, edgeRec, edgeOisPipeID, CalcBeg_Time, Calc_ID);
        }
    }

    class StopwatchMs : IDisposable
    {
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        string msg;
        public StopwatchMs(string msg) { this.msg = msg; Console.WriteLine($"{msg}: started"); sw.Start(); }
        public void Dispose() { sw.Stop(); Console.WriteLine($"{msg}: done in {sw.ElapsedMilliseconds}ms"); }
    }

}
