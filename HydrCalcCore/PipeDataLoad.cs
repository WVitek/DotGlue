using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using PipeNetCalc;

namespace PPM.HydrCalcPipe
{
    [Serializable]
    public class HydrCalcData
    {
        // main data
        public Edge[] edges;
        public Node[] nodes;
        public Dictionary<int, NetCalc.WellInfo> nodeWell;
        // diagnostic and I/O data
        public string[] colorName;
        public string[] nodeName;
        public ulong[] edgeID;
    }

    public static class PipeDataLoad
    {
        static int GetColor(Dictionary<string, int> dict, string key)
        {
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


        public static HydrCalcData PrepareInputData(DbConnection dbConnOisPipe, Dictionary<ulong, NetCalc.WellInfo> dictWellOp)
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
                int color, //string PuFluid_ClCD,
                float Pu_Length,
                float Pu_InnerDiam
            )>();

            string[] colorName = null;

            #region Получение исходных данных по вершинам и ребрам графа трубопроводов
            if (dbConnOisPipe != null)
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

                    var colorDict = new Dictionary<string, int>();

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
                                    color: GetColor(colorDict, rdr.GetStr(4)), //PuFluid_ClCD: rdr.GetStr(4),
                                    Pu_Length: rdr.GetFlt(5, float.Epsilon),
                                    Pu_InnerDiam: rdr.GetFlt(6)
                                ));
                            }
                    }
                    colorName = colorDict.OrderBy(p => p.Value).Select(p => p.Key).ToArray();
                }
            #endregion

            Edge[] edges = null;
            Node[] nodes = null;
            #region Подготовка входных параметров для разбиения на подсети
            {
                var nodesDict = Enumerable.Range(0, nodesLst.Count).ToDictionary(
                        i => nodesLst[i].PipeNode_ID,
                        i => (Ndx: i,
                            TypeID: nodesLst[i].NodeType_ID,
                            Altitude: 0f
                        )
                    );
                edges = edgesLst.Select(
                    r => new Edge()
                    {
                        iNodeA = nodesDict.TryGetValue(r.PuBegNode_ID, out var eBeg) ? eBeg.Ndx : -1,
                        iNodeB = nodesDict.TryGetValue(r.PuEndNode_ID, out var eEnd) ? eEnd.Ndx : -1,
                        color = r.color, //GetColor(colorDict, r.PuFluid_ClCD),
                        D = r.Pu_InnerDiam,
                        L = r.Pu_Length,
                    })
                    .ToArray();
                nodes = nodesDict
                    .Select(p => new Node() { kind = (NodeKind)p.Value.TypeID, Node_ID = p.Key.ToString(), Altitude = p.Value.Altitude })
                    .ToArray();
            }
            #endregion

            var nodeWell = new Dictionary<int, NetCalc.WellInfo>();
            #region Подготовка данных по скважинам
            //var usedWells = new HashSet<ulong>();
            if (dictWellOp != null)
            {
                for (int iNode = 0; iNode < nodesLst.Count; iNode++)
                {
                    var wellID = nodesLst[iNode].NodeObj_ID;
                    if (wellID != default && dictWellOp.TryGetValue(wellID, out var wi))
                    {
                        //usedWells.Add(wellID);
                        nodeWell.Add(iNode, wi);
                    }
                }
            }
            //var unusedWells = dictWellOp.Where(p => !usedWells.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            #endregion

            var edgeID = edgesLst.Select(r => r.Pu_ID).ToArray();
            var nodeName = nodesLst.Select(r => r.Node_Name).ToArray();

            return new HydrCalcData() { edges = edges, nodes = nodes, colorName = colorName, nodeName = nodeName, edgeID = edgeID, nodeWell = nodeWell };
        }

        public static Dictionary<ulong, NetCalc.WellInfo> LoadWellsData(DbConnection dbConnWellOP, NetCalc.WellKind whatToLoad)
        {
            var dictWellOp = new Dictionary<ulong, NetCalc.WellInfo>();

            if ((whatToLoad & NetCalc.WellKind.Oil) != 0)
                using (new StopwatchMs("Load oil wells data from WELL_OP"))
                {
                    using (var cmd = dbConnWellOP.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT
    well_id  Well_ID_OP,
    ROUND(inline_pressure,6) Line_Pressure__Atm,
    ROUND(pek,6) Particles
FROM well_op_oil
WHERE calc_date BETWEEN to_date('20190101', 'yyyymmdd') AND to_date('20190131', 'yyyymmdd') 
";
                        using (var rdr = cmd.ExecuteReader())
                            while (rdr.Read())
                            {
                                var wellID = rdr.GetUInt64(0);
                                dictWellOp.Add(wellID, new NetCalc.WellInfo()
                                {
                                    Well_ID = wellID.ToString(),
                                    kind = NetCalc.WellKind.Oil,
                                    Line_Pressure__Atm = rdr.GetFlt(1),
                                    Particles = rdr.GetFlt(2),
                                });
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
                                    wi = new NetCalc.WellInfo() { Well_ID = wellID.ToString(), kind = NetCalc.WellKind.Oil };
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

            if ((whatToLoad & NetCalc.WellKind.Water) != 0)
                using (new StopwatchMs("Load water wells data from WELL_OP"))
                {
                    using (var cmd = dbConnWellOP.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT 
    well_id, 
    ROUND(liq_rate,6), 
    ROUND(inline_pressure,6),
    ROUND(water_density,6)
FROM wellop.well_op_water
WHERE (layer_id = 'PL0000' or layer_count = 1) 
    AND liq_rate is not null AND inline_pressure is not null
    AND calc_date = to_date('20190101','yyyymmdd')
";
                        using (var rdr = cmd.ExecuteReader())
                            while (rdr.Read())
                            {
                                var wellID = rdr.GetUInt64(0);
                                dictWellOp.Add(wellID, new NetCalc.WellInfo()
                                {
                                    Well_ID = wellID.ToString(),
                                    kind = NetCalc.WellKind.Water,
                                    Liq_VolRate = rdr.GetFlt(1),
                                    Line_Pressure__Atm = rdr.GetFlt(2),
                                    Water_Density = rdr.GetFlt(3),
                                    Water_Viscosity = float.NaN,
                                    Gas_Density = 1,
                                    Bubblpnt_Pressure__Atm = 1,
                                    Liq_Watercut = 1,
                                    Oil_Density = 1,
                                    Oil_GasFactor = 1,
                                    Oil_Viscosity = 1,
                                    Oil_VolumeFactor = 1,
                                    Reservoir_Pressure__Atm = 1,
                                    Temperature__C = 20,
                                });
                            }
                    }
                }

            if ((whatToLoad & NetCalc.WellKind.Inj) != 0)
                using (new StopwatchMs("Load inj wells data from WELL_OP"))
                {
                    using (var cmd = dbConnWellOP.CreateCommand())
                    {
                        cmd.CommandText = @"
                    SELECT
                        well_id  Well_ID_OP, 
                        ROUND(intake,6),
                        ROUND(wellhead_pressure,6),
                        ROUND(water_density,6)
                    FROM v_well_op_inj 
                    WHERE (layer_id = 'PL0000' or layer_count = 1)
                        AND intake is not null AND wellhead_pressure is not null
                        AND cur_month = to_date('20190101','yyyymmdd')
                    ";
                        using (var rdr = cmd.ExecuteReader())
                            while (rdr.Read())
                            {
                                var wellID = rdr.GetUInt64(0);
                                dictWellOp.Add(wellID, new NetCalc.WellInfo()
                                {
                                    Well_ID = wellID.ToString(),
                                    kind = NetCalc.WellKind.Inj,
                                    Liq_VolRate = rdr.GetFlt(1),
                                    Line_Pressure__Atm = rdr.GetFlt(2),
                                    Water_Density = rdr.GetFlt(3),
                                });
                            }
                    }
                }

            return dictWellOp;
        }
    }
}
