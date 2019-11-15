using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using W.Oilca;

namespace PipeNetCalc
{
    public static class CalcRec
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
                arr.Put(ref i, Pressure);
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
            _Started,
            /// <summary>
            /// Просчитана начальная точка
            /// </summary>
            _Half,
            /// <summary>
            /// Просчитана конечная точка
            /// </summary>
            _Full,
            MaxValue,
        }

        static void Put(this object[] vals, ref int i, float v)
        {
            if (float.IsNaN(v))
                vals[i++] = null;
            else
                vals[i++] = v;
        }

        public class HydrCalcDataRec
        {
            public int Subnet_Number;  // условный номер расчётной подсети
            public CalcStatus CalcStatus;  // Статус: успешно или причина неуспеха

            public float OilVolumeRate_sc;
            public float WaterVolumeRate_sc;
            public float GasVolumeRate_sc;

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
                    case CalcStatus.Failed:
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
                    arr.Put(ref i, OilVolumeRate_sc);
                    arr.Put(ref i, WaterVolumeRate_sc);
                    arr.Put(ref i, GasVolumeRate_sc);

                    bool All = CalcStatus == CalcStatus.Success;
                    From.GetValues(arr, ref i, All);
                    To.GetValues(arr, ref i, All);
                    if (All && fluid != null)
                    {
                        arr.Put(ref i, fluid.Reservoir_Pressure__Atm); // Пластовое давление (начальное, пл.у.), атм
                        arr.Put(ref i, fluid.Temperature__C);          // Пластовая температура (пл.у.), °C
                        arr.Put(ref i, fluid.Oil_VolumeFactor);        // Объёмный фактор (коэффициент) нефти (пл.у.), м³/ м³
                        arr.Put(ref i, fluid.Bubblpnt_Pressure__Atm);  // Давление насыщения нефти  (пл.у.), атм
                        arr.Put(ref i, fluid.Oil_GasFactor);           // Газосодержание нефти (пл.у.),  м³/ м³
                        arr.Put(ref i, float.NaN);                     // Газовый фактор нефти (пл.у.),  м³/ м³
                        arr.Put(ref i, fluid.Oil_Density);             // Плотность нефти (с.у.), т/м³
                        arr.Put(ref i, fluid.Water_Density);           // Плотность воды (с.у.),  т/м³
                        arr.Put(ref i, fluid.Gas_Density);             // Плотность газа (с.у.),  кг/м³
                        arr.Put(ref i, fluid.Oil_Viscosity);           // Вязкость нефти (пл.у.), сПз
                        arr.Put(ref i, fluid.Water_Viscosity);         // Вязкость воды (пл.у.), сПз
                        arr.Put(ref i, fluid.Particles);               // Взвешенных частиц (мехпримеси), мг/л ~ 1ppm
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
                if (pos == 0 || pos == 1d)
                {
                    var (iEdge, reversedEdge, reversedCalc) = NetCalc.DecodeCalcCookie(cookie);
                    int edgeDirection = reversedEdge ? -1 : +1;
                    var r = edgesRecs[iEdge];

                    int flowDirection = (reversedEdge ^ reversedCalc) ? -1 : +1;

                    if (edgeDirection > 0 ^ pos == 1d)
                    {
                        r.From.Fill(ctx, gd, flowDirection);
                        r.CalcStatus++;
                    }
                    else
                    {
                        r.To.Fill(ctx, gd, flowDirection);
                        r.CalcStatus++;
                    }
                }
                else if (pos == -1)
                {   // called from pressure drop calculation function before calculation
                    var (iEdge, reversedEdge, reversedCalc) = NetCalc.DecodeCalcCookie(cookie);
                    //int edgeDirection = reversedEdge ? -1 : +1;
                    int flowDirection = (reversedEdge ^ reversedCalc) ? -1 : +1;
                    var r = edgesRecs[iEdge];

                    if (gd != null)
                    {
                        r.CalcStatus = CalcStatus._Started;
                        r.OilVolumeRate_sc = (float)gd.Q_oil_rate * flowDirection;
                        r.WaterVolumeRate_sc = (float)gd.Q_water_rate * flowDirection;
                        r.GasVolumeRate_sc = (float)gd.Q_gas_rate * flowDirection;
                    }
                    else
                    {
                        r.CalcStatus = CalcStatus.Failed;
                        r.OilVolumeRate_sc = r.WaterVolumeRate_sc = r.GasVolumeRate_sc = float.NaN;
                    }
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
                            PipeGraph.ExportToTGF(tw, edges, nodes, subnetEdges,
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
