using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using W.Oilca;

namespace Pipe.Exercises
{
    public static class PipesCalcPPM
    {
        /// <summary>
        /// Результаты расчёта в условиях точки Measure трубопровода
        /// </summary>
        public struct HydrPointInfo
        {
            public float Measure;         // удалённость точки по оси трубопровода от его начала, м
            public float Pressure;        // Давление, атм
            public float Temperature;     // Температура, °C
            public float OilVolumeRate;   // Объёмный дебит нефти, м³/сут
            public float WaterVolumeRate; // Объёмный дебит воды, м³/сут
            public float GasVolumeRate;   // Объёмный дебит газа, м³/сут
            public float OilDensity;      // Плотность нефти, т/м³
            public float OilViscosity;    // Вязкость нефти, сПз
            public float WaterDensity;    // Плотность воды, т/м³
            public float WaterViscosity;  // Вязкость воды, сПз
            public float GasDensity;      // Плотность газа, кг/м³
            public float GasViscosity;    // Вязкость газа, сПз

            public static void Fill(ref HydrPointInfo p, PVT.Context ctx, Gradient.DataInfo gd, int direction)
            {
                p.Pressure = (float)ctx[PVT.Prm.P].MPa2Atm();
                p.Temperature = (float)ctx[PVT.Prm.T].Cel2Kel();
                p.OilVolumeRate = (float)gd.Q_oil_rate;
                p.WaterVolumeRate = (float)gd.Q_water_rate;
                p.GasVolumeRate = (float)gd.Q_gas_rate;
                p.OilDensity = (float)ctx[PVT.Prm.Rho_o];
                p.OilViscosity = (float)ctx[PVT.Prm.Mu_o];
                p.WaterDensity = (float)ctx[PVT.Prm.Rho_w];
                p.WaterViscosity = (float)ctx[PVT.Prm.Mu_w];
                p.GasDensity = (float)ctx[PVT.Prm.Rho_g] * 1000;
                p.GasViscosity = (float)ctx[PVT.Prm.Mu_g];
            }
        }

        public class HydrCalcDataRec<TID> where TID : struct
        {
            public TID Pipe_ID;        // ID участка трубопровода
            public DateTime Calc_Time; // Дата и время расчёта
            public TID Calc_ID;        // FK ID расчёта (таблицы пока нет, нужна?)
            public int Subnet_Number;  // условный номер расчётной подсети
            public TID CalcStatus_RD;  // Статус: успешно или причина неуспеха
            // Результаты расчёта в условиях точки From.Measure
            public HydrPointInfo From;
            // Результаты расчёта в условиях точки To.Measure
            public HydrPointInfo To;
            // PVT-свойства, использованные при расчёте
            public float Reservoir_Pressure;    // Пластовое давление (начальное, пл.у.), атм
            public float Reservoir_Temperature; // Пластовая температура (пл.у.), °C
            public float Oil_VolumeFactor;      // Объёмный фактор (коэффициент) нефти (пл.у.), м³/ м³
            public float Bubblepoint_Pressure;  // Давление насыщения нефти  (пл.у.), атм
            public float Reservoir_SGOR;        // Газосодержание нефти (пл.у.),  м³/ м³
            public float Reservoir_GOR;         // Газовый фактор нефти (пл.у.),  м³/ м³
            public float Oil_Density;           // Плотность нефти (с.у.), т/м³
            public float Water_Density;         // Плотность воды (с.у.),  т/м³
            public float Gas_Density;           // Плотность газа (с.у.),  кг/м³
            public float Oil_Viscosity;         // Вязкость нефти (пл.у.), сПз
            public float Water_Viscosity;       // Вязкость воды (пл.у.), сПз
        }

        public static void CalcSubnets<TID>(
            Edge[] edges, Node<TID>[] nodes, List<int[]> subnets,
            Dictionary<int, PipeNetCalc.WellInfo<TID>> nodeWell,
            Func<int, StreamWriter> GetTgfStream = null,
            Func<int, string> GetNodeName = null
        )
            where TID : struct
        {
#if DEBUG
            var parOpts = new ParallelOptions() { MaxDegreeOfParallelism = 1 };
#else
            var parOpts = new ParallelOptions() { MaxDegreeOfParallelism = 5 };
#endif

            var edgesRecs = new ConcurrentDictionary<int, HydrCalcDataRec<TID>>();
            PressureDrop.StepHandler stepHandler = (pos, gd, ctx, cookie) =>
            {
                int direction = Math.Sign(cookie);
                int iEdge = cookie * direction - 1;
                var e = edges[iEdge];
            };

            Parallel.ForEach(Enumerable.Range(0, subnets.Count), parOpts, iSubnet =>
            {
                int[] subnetEdges = subnets[iSubnet];
                if (subnetEdges.Length > 1)
                {
                    var (edgeI, nodeI) = PipeNetCalc.Calc(edges, nodes, subnetEdges, nodeWell, stepHandler);
                    if (edgeI.Count > 0 || nodeI.Count > 0)
                    {
                        if (GetTgfStream != null)
                            using (var tw = GetTgfStream(iSubnet))
                                PipeNetCalc.ExportTGF<TID>(tw, edges, nodes, subnetEdges,
                                    iNode => GetNodeName?.Invoke(iNode),
                                    iNode => nodeI.TryGetValue(iNode, out var I) ? FormattableString.Invariant($" P={I.nodeP:0.###}") : null,
                                    iEdge => edgeI.TryGetValue(iEdge, out var I) ? FormattableString.Invariant($" Q={I.edgeQ:0.#}") : null
                                );
                    }
                }
            });
        }

    }
}
