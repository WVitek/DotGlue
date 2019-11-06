using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using W.Oilca;

namespace PipeNetCalc
{
    public static class PipesCalc
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

            public void Fill(PVT.Context ctx, Gradient.DataInfo gd, int direction)
            {
                Pressure = (float)ctx[PVT.Prm.P].MPa2Atm();
                Temperature = (float)ctx[PVT.Prm.T].Kel2Cel();
                OilVolumeRate = (float)gd.Q_oil_rate * direction;
                WaterVolumeRate = (float)gd.Q_water_rate * direction;
                GasVolumeRate = (float)gd.Q_gas_rate * direction;
                OilDensity = (float)ctx[PVT.Prm.Rho_o] * 0.001f;
                OilViscosity = (float)ctx[PVT.Prm.Mu_o];
                WaterDensity = (float)ctx[PVT.Prm.Rho_w] * 0.001f;
                WaterViscosity = (float)ctx[PVT.Prm.Mu_w];
                GasDensity = (float)ctx[PVT.Prm.Rho_g];
                GasViscosity = (float)ctx[PVT.Prm.Mu_g];
            }

            public void GetValues(object[] arr, ref int i, bool All)
            {
                if (!float.IsNaN(Pressure))
                    arr[i] = Pressure;
                i++;
                if (All)
                {
                    arr[i++] = Temperature;
                    arr[i++] = OilVolumeRate;
                    arr[i++] = WaterVolumeRate;
                    arr[i++] = GasVolumeRate;
                    arr[i++] = OilDensity;
                    arr[i++] = OilViscosity;
                    arr[i++] = WaterDensity;
                    arr[i++] = WaterViscosity;
                    arr[i++] = GasDensity;
                    arr[i++] = GasViscosity;
                }
                else i += 10;
            }
        }

        public enum CalcStatus
        {
            /// <summary>
            /// Расчёта тут не было 
            /// (возможно, текущий вариант расчётного алгоритма не полностью поддерживает такой вид подсети)
            /// </summary>
            Virgin = 0,
            /// <summary>
            /// Недостаточно данных для расчёта
            /// </summary>
            NoData,
            /// <summary>
            /// Расчёт произведён успешно
            /// </summary>
            Success,
            /// <summary>
            /// "Протянутые" значения давления от соседних узлов
            /// </summary>
            ExtraP,
            /// <summary>
            /// Расчёт привёл к ошибке
            /// </summary>
            Failed,
            /// <summary>
            /// Расчёт начат
            /// </summary>
            _Started = 1000,
            /// <summary>
            /// Просчитана начальная точка
            /// </summary>
            _Half = 1001,
            /// <summary>
            /// Просчитана конечная точка
            /// </summary>
            _Full = 1002,
        }

        public class HydrCalcDataRec
        {
            public int Subnet_Number;  // условный номер расчётной подсети
            public CalcStatus CalcStatus;  // Статус: успешно или причина неуспеха

            // Результаты расчёта в условиях точки From.Measure
            public HydrPointInfo From;

            // Результаты расчёта в условиях точки To.Measure
            public HydrPointInfo To;

            // PVT-свойства, использованные при расчёте
            public NetCalc.FluidInfo fluid;

            public void Fill(NetCalc.FluidInfo fi, double P0, double P1)
            {
                if (!fi.IsEmpty)
                    fluid = fi;
                bool noP0 = double.IsNaN(P0);
                bool noP1 = double.IsNaN(P1);

                switch (CalcStatus)
                {
                    case CalcStatus._Started:
                    case CalcStatus._Half:
                        CalcStatus = CalcStatus.Failed;
                        break;
                    case CalcStatus._Full:
                        CalcStatus = (noP0 || noP1) ? CalcStatus.Failed : CalcStatus.Success;
                        break;
                    default:
                        if (noP0 && noP1)
                            CalcStatus = (fluid == null) ? CalcStatus.Virgin : CalcStatus.NoData;
                        else if (!noP0 && !noP1)
                            CalcStatus = CalcStatus.ExtraP;
                        else
                            CalcStatus = CalcStatus.NoData;
                        break;
                }
            }

            public override string ToString() => $"{Subnet_Number}: {CalcStatus}";

            public void GetValues(object[] arr, ref int i)
            {
                arr[i++] = From.Measure;
                arr[i++] = To.Measure;
                arr[i++] = Subnet_Number;
                arr[i++] = CalcStatus.ToString();
                if (CalcStatus == CalcStatus.Success || CalcStatus == CalcStatus.ExtraP)
                {
                    bool All = CalcStatus == CalcStatus.Success;
                    From.GetValues(arr, ref i, All);
                    To.GetValues(arr, ref i, All);
                    if (All && fluid != null)
                    {
                        arr[i++] = fluid.Reservoir_Pressure__Atm;//public float Reservoir_Pressure;    // Пластовое давление (начальное, пл.у.), атм
                        arr[i++] = fluid.Temperature__C;//public float Reservoir_Temperature; // Пластовая температура (пл.у.), °C
                        arr[i++] = fluid.Oil_VolumeFactor;//public float Oil_VolumeFactor;      // Объёмный фактор (коэффициент) нефти (пл.у.), м³/ м³
                        arr[i++] = fluid.Bubblpnt_Pressure__Atm;//public float Bubblepoint_Pressure;  // Давление насыщения нефти  (пл.у.), атм
                        arr[i++] = fluid.Oil_GasFactor;//public float Reservoir_SGOR;        // Газосодержание нефти (пл.у.),  м³/ м³
                        arr[i++] = null;//public float Reservoir_GOR;         // Газовый фактор нефти (пл.у.),  м³/ м³
                        arr[i++] = fluid.Oil_Density;//public float Oil_Density;           // Плотность нефти (с.у.), т/м³
                        arr[i++] = fluid.Water_Density;//public float Water_Density;         // Плотность воды (с.у.),  т/м³
                        arr[i++] = fluid.Gas_Density;//public float Gas_Density;           // Плотность газа (с.у.),  кг/м³
                        arr[i++] = fluid.Oil_Viscosity;//public float Oil_Viscosity;         // Вязкость нефти (пл.у.), сПз
                        arr[i++] = fluid.Water_Viscosity;//public float Water_Viscosity;       // Вязкость воды (пл.у.), сПз
                    }
                }
            }

        }

        public static HydrCalcDataRec[]

            CalcSubnets(

            Edge[] edges, Node[] nodes, List<int[]> subnets,
            Dictionary<int, NetCalc.WellInfo> nodeWell,
            Func<int, StreamWriter> GetTgfStream = null,
            Func<int, string> GetTgfNodeName = null
        )
        {
#if DEBUG
            var parOpts = new ParallelOptions() { MaxDegreeOfParallelism = 1 };
#else
            var parOpts = new ParallelOptions() { MaxDegreeOfParallelism = 5 };
#endif

            var edgesRecs = new HydrCalcDataRec[edges.Length];

            PressureDrop.StepHandler stepHandler = (pos, gd, ctx, cookie) =>
            {
                if (pos != 0 && pos != 1d)
                {
                    if (pos == -1)
                    {
                        int i = cookie * Math.Sign(cookie) - 1;
                        edgesRecs[i].CalcStatus = CalcStatus._Started;
                    }
                    return;
                }

                int direction = Math.Sign(cookie);
                int iEdge = cookie * direction - 1;

                var r = edgesRecs[iEdge];

                if (direction > 0 ^ pos == 1d)
                {
                    r.From.Fill(ctx, gd, direction);
                    r.CalcStatus++;
                }
                else
                {
                    r.To.Fill(ctx, gd, direction);
                    r.CalcStatus++;
                }
            };

            Parallel.ForEach(Enumerable.Range(0, subnets.Count), parOpts, iSubnet =>
            {
                int[] subnetEdges = subnets[iSubnet];
                if (subnetEdges.Length == 0)
                    return;

                foreach (var iEdge in subnetEdges)
                {
                    var r = new HydrCalcDataRec() { Subnet_Number = iSubnet, };
                    r.From.Measure = 0;
                    r.To.Measure = edges[iEdge].L;
                    edgesRecs[iEdge] = r;
                }

                var (edgeI, nodeI) = NetCalc.Calc(edges, nodes, subnetEdges, nodeWell, stepHandler);

                if (edgeI.Count == 0 && nodeI.Count == 0)
                    return;

                foreach (var p in edgeI)
                {
                    int iEdge = p.Key;
                    var r = edgesRecs[iEdge];
                    var e = edges[iEdge];
                    var P0 = nodeI.TryGetValue(e.iNodeA, out var N0) ? N0.nodeP : double.NaN;
                    var P1 = nodeI.TryGetValue(e.iNodeB, out var N1) ? N1.nodeP : double.NaN;

                    r.Fill(p.Value.fluid, P0, P1);
                }

                if (GetTgfStream != null)
                {
                    var tw = GetTgfStream(iSubnet);
                    if (tw != null)
                        using (tw)
                            Graph.ExportToTGF(tw, edges, nodes, subnetEdges,
                                iNode => GetTgfNodeName?.Invoke(iNode),
                                iNode => nodeI.TryGetValue(iNode, out var I) ? I.StrTGF() : null,
                                iEdge => edgeI.TryGetValue(iEdge, out var I) ? I.StrTGF() : null
                            );
                }
            });

            return edgesRecs;
        }

    }
}
