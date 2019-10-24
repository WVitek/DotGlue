﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using W.Oilca;
using System.IO;

namespace Pipe.Exercises
{
    public static class PipeNetCalc
    {
        public static System.Diagnostics.TraceSource Logger = W.Common.Trace.GetLogger("PipeNetCalc");

        public class FluidInfo
        {
            public double Liq_Viscosity;
            public double Oil_Comprssblty;
            public double Bubblpnt_Pressure__Atm; // bubble_point_pressure
            public double Oil_GasFactor;
            public double Oil_Density;
            public double Water_Density;
            public double LayerShut_Pressure__Atm; // todo: Init_shut_pressure
            public double Temperature__C;
            public double Water_Viscosity;
            public double Oil_Viscosity;

            public PVT.Context.Root GetPvtContext()
            {
                var refP = PVT.Prm.P._(PVT.Arg.P_SC, PVT.Arg.P_RES);
                var refT = PVT.Prm.T._(PVT.Arg.T_SC, PVT.Arg.T_RES);

                var root = PVT.NewCtx() // todo: need to use all fluid parameters for calc
                    .With(PVT.Arg.GAMMA_O, Oil_Density)
                    .With(PVT.Arg.Rsb, Oil_GasFactor)
                    .With(PVT.Arg.GAMMA_G, 0.8)
                    .With(PVT.Arg.GAMMA_G_SEP, 1)
                    .With(PVT.Arg.GAMMA_W, Water_Density)
                    .With(PVT.Arg.S, PVT.WaterSalinity_From_Density(Water_Density))
                    .With(PVT.Arg.P_SC, U.Atm2MPa(1))
                    .With(PVT.Arg.T_SC, 273 + 20)
                    .With(PVT.Arg.P_RES, U.Atm2MPa(LayerShut_Pressure__Atm))
                    .With(PVT.Arg.T_RES, U.Cel2Kel(Temperature__C))
                    .With(PVT.Arg.P_SEP, PVT.Arg.P_SC)
                    .With(PVT.Arg.T_SEP, PVT.Arg.T_SC)
                    .With(PVT.Arg.GAMMA_G_CORR, PVT.Gamma_g_corr_Calc)

                    .With(PVT.Prm.Bob, PVT.Bob_STANDING_1947)
                    //.With(PVT.Prm.Pb, PVT.Pb_STANDING_1947)
                    .WithRescale(PVT.Prm.Pb._(PVT.Arg.None, U.Atm2MPa(Bubblpnt_Pressure__Atm)), PVT.Pb_STANDING_1947, refP, refT)
                    .With(PVT.Prm.Rs, PVT.Rs_VELARDE_1996)
                    //.WithRescale(PVT.Prm.Rs._(0d, PVT.Arg.Rsb), PVT.Rs_VELARDE_1996, refP, refT)
                    .With(PVT.Prm.Bg, PVT.Bg_MAT_BALANS)
                    .With(PVT.Prm.Z, PVT.Z_BBS_1974)
                    //.With(PVT.Prm.Bo, PVT.Bo_DEFAULT)
                    .WithRescale(PVT.Prm.Bo._(1, Oil_Comprssblty), PVT.Bo_DEFAULT, refP, refT)
                    .With(PVT.Prm.Co, PVT.co_VASQUEZ_BEGGS_1980)
                    .With(PVT.Prm.Bw, PVT.Bw_MCCAIN_1990)
                    .With(PVT.Prm.Rho_o, PVT.Rho_o_MAT_BALANS)
                    .With(PVT.Prm.Rho_w, PVT.Rho_w_MCCAIN_1990)
                    .With(PVT.Prm.Rho_g, PVT.Rho_g_DEFAULT)
                    //
                    .With(PVT.Prm.Sigma_og, PVT.Sigma_og_BAKER_SWERDLOFF_1956)
                    .With(PVT.Prm.Sigma_wg, PVT.Sigma_wg_RAMEY_1973)
                    //.With(PVT.Prm.Mu_o, PVT.Mu_o_VASQUEZ_BEGGS_1980)
                    .WithRescale(PVT.Prm.Mu_o._(PVT.Arg.None, Oil_Viscosity), PVT.Mu_o_VASQUEZ_BEGGS_1980, refP, refT)
                    .With(PVT.Prm.Mu_os, PVT.Mu_os_BEGGS_ROBINSON_1975)
                    .With(PVT.Prm.Mu_od, PVT.Mu_od_BEAL_1946)
                    .With(PVT.Prm.Mu_w, PVT.Mu_w_MCCAIN_1990)
                    .With(PVT.Prm.Mu_g, PVT.Mu_g_LGE_MCCAIN_1991)
                    .With(PVT.Prm.Tpc, PVT.Tpc_SUTTON_2005)
                    .With(PVT.Prm.Ppc, PVT.Ppc_SUTTON_2005)
                    .Done();
                return root;
            }
        }

        public class WellInfo<TID> : FluidInfo
        {
            public TID Well_ID;
            public string Layer;
            public double Line_Pressure__Atm;
            public double Liq_VolRate;
            public double Liq_Watercut;

            public static readonly WellInfo<TID> Unknown = new WellInfo<TID>() { Line_Pressure__Atm = double.NaN };
        }

        static void AddNodeEdge(Edge[] edges, Dictionary<int, List<int>> nodeEdges, int iNode, int iEdge)
        {
            if (!nodeEdges.TryGetValue(iNode, out var lst))
            {
                lst = new List<int>();
                nodeEdges[iNode] = lst;
                lst.Add(iEdge);
                return;
            }
            if (lst.Any(i => edges[i].IsIdentical(ref edges[iEdge])))
                return; // eliminate duplicated pipes
            lst.Add(iEdge);
        }

        public class EdgeInfo
        {
            /// <summary>
            /// Liquid rate through edge
            /// </summary>
            public readonly double edgeQ;
            public readonly double watercut;
            /// <summary>
            /// Liquid fluid parameters
            /// </summary>
            public FluidInfo fluid;
            public EdgeInfo(double Q, double WCT, FluidInfo f) { edgeQ = Q; watercut = WCT; fluid = f; }
            public override string ToString() => FormattableString.Invariant($"Q={edgeQ:0.#}, WCT={watercut * 100:00.#}%");
        }

        public class NodeInfo
        {
            /// <summary>
            /// Pressure at node
            /// </summary>
            public double nodeP;
            public NodeInfo(double P) { nodeP = P; }
            public void Update(double P)
            {
                if (nodeP > P || double.IsNaN(nodeP))
                    nodeP = P;
            }
            public override string ToString() => FormattableString.Invariant($"P={nodeP:0.###}");
        }

        /// <summary>
        /// Реализация гидравлического расчёта подсети
        /// </summary>
        /// <typeparam name="TID"></typeparam>
        class Impl<TID> where TID : struct
        {
            public readonly Edge[] edges;
            public readonly Node<TID>[] nodes;
            public readonly int[] subnet;

            /// <summary>
            /// Key: индекс вершины/узла, Value: список инцидентных вершине рёбер/трубопроводов
            /// </summary>
            Dictionary<int, List<int>> nodeEdges = new Dictionary<int, List<int>>();

            /// <summary>
            /// Данные по рёбрам
            /// </summary>
            readonly Dictionary<int, EdgeInfo> resEdgeInfo = new Dictionary<int, EdgeInfo>();

            /// <summary>
            /// Данные по узлам
            /// </summary>
            readonly Dictionary<int, NodeInfo> resNodeInfo = new Dictionary<int, NodeInfo>();

            void AddNodeEdge(int iNode, int iEdge)
            {
                if (!nodeEdges.TryGetValue(iNode, out var lst))
                {
                    lst = new List<int>();
                    nodeEdges[iNode] = lst;
                    lst.Add(iEdge);
                    return;
                }
                if (lst.Any(i => edges[i].IsIdentical(ref edges[iEdge])))
                    return; // eliminate duplicated pipes
                lst.Add(iEdge);
            }

            public Impl(Edge[] edges, Node<TID>[] nodes, int[] subnet)
            {
                this.edges = edges;
                this.nodes = nodes;
                this.subnet = subnet;

                // для каждого узла формируем список инцидентных рёбер/трубопроводов
                foreach (var i in subnet)
                {
                    AddNodeEdge(edges[i].iNodeA, i);
                    AddNodeEdge(edges[i].iNodeB, i);
                }
            }

            public (Dictionary<int, EdgeInfo> edgeI, Dictionary<int, NodeInfo> nodeI)
                Calc(IReadOnlyDictionary<int, WellInfo<TID>> nodeWells)
            {
                var nextNodes = FromWells(nodeWells);
                FromNextNodes(nextNodes);
                return (resEdgeInfo, resNodeInfo);
            }

            /// <summary>
            /// Проход по узлам скважин с целью просчёта до куста/АГЗУ
            /// </summary>
            /// <param name="nodeWells"></param>
            /// <returns>Индексы узлов кустов/АГЗУ для которых нашлось давление</returns>
            IEnumerable<int> FromWells(IReadOnlyDictionary<int, WellInfo<TID>> nodeWells)
            {
                // множество узлов кустов/АГЗУ для которых нашлось давление
                var setOfNextNodes = new HashSet<int>();
                var edgeStack = new Stack<int>();

                // обход всех узлов-скважин
                foreach (var pair in nodeEdges)
                {
                    int iCurNode = pair.Key;

                    if (nodes[iCurNode].kind != NodeKind.Well)
                        continue;

                    if (!nodeWells.TryGetValue(pair.Key, out WellInfo<TID> wi))
                    {   // нет информации по скважине
                        var cn = nodes[iCurNode];
                        if (cn.kind == NodeKind.Well)
                            Logger.TraceInformation($"No fluid information for well node\t{nameof(cn.Node_ID)}={cn.Node_ID}");
                        wi = WellInfo<TID>.Unknown;
                    }

                    #region Проходим рёбра от узла-скважины до узла АГЗУ/куста, собирая их в edgeStack
                    bool found = false;
                    do
                    {
                        List<int> adjEdges = nodeEdges[iCurNode];
                        if (adjEdges.Count != 1)
                        {
                            var cn = nodes[iCurNode];
                            Logger.TraceInformation($"No single way from well to '{nameof(cn.IsMeterOrClust)}' node\t{nameof(cn.Node_ID)}={cn.Node_ID}\t{nameof(wi.Well_ID)}={wi.Well_ID}");
                            break;
                        }
                        int iEdge = adjEdges[0];
                        if (edgeStack.Count > 16)// || edgeStack.Contains(iEdge))
                        {   // либо нефизично "далеко" до куста/АГЗУ, либо цикл
                            var cn = nodes[iCurNode];
                            Logger.TraceInformation($"Valid way from well not found, stopped @node\t{nameof(cn.Node_ID)}={cn.Node_ID}\t{nameof(wi.Well_ID)}={wi.Well_ID}");
                            break;
                        }
                        edgeStack.Push(iEdge);
                        iCurNode = edges[iEdge].Next(iCurNode).iNextNode;
                        // Нашли узел куста/АГЗУ ?
                        found = nodes[iCurNode].IsMeterOrClust();
                    } while (!found);
                    #endregion

                    try
                    {
                        if (!found)
                            continue;

                        var Pline = wi.Line_Pressure__Atm;
                        // устанавливаем значение Pline в узел куста/АГЗУ
                        {
                            if (!resNodeInfo.TryGetValue(iCurNode, out var I))
                                resNodeInfo.Add(iCurNode, new NodeInfo(P: Pline));
                            else if (!U.isEQ(I.nodeP, Pline))
                            {
                                I.Update(P: Pline);
                                Pline = I.nodeP;
                                var cn = nodes[iCurNode];
                                Logger.TraceInformation($"Line pressure mismatch detected\t{nameof(cn.Node_ID)}={cn.Node_ID}\t{nameof(wi.Well_ID)}={wi.Well_ID}\t{I.nodeP}<>{Pline}");
                            }
                            setOfNextNodes.Add(iCurNode);
                        }

                        #region Обратный проход по узлам от АГЗУ/куста до скважины с расчётом по рёбрам-трубопроводам
                        // Подготовка PVT-контекста
                        var root = wi.GetPvtContext();
                        var ctx = root.NewCtx()
                            .With(PVT.Prm.P, U.Atm2MPa(Pline))
                            .Done();
                        var gd = new Gradient.DataInfo();
                        List<PressureDrop.StepInfo> steps = null;
                        var Qliq = wi.Liq_VolRate;
                        var WCT = wi.Liq_Watercut;
                        var GOR = root[PVT.Arg.Rsb]; // todo: what with GOR ?

                        // Расчёт
                        for (var iEdge = edgeStack.Pop(); ; iEdge = edgeStack.Pop())
                        {
                            var e = edges[iEdge];
                            var (iNextNode, direction) = e.Next(iCurNode);

                            resEdgeInfo.Add(iEdge, new EdgeInfo(Q: Qliq, WCT: wi.Liq_Watercut, f: wi));

                            double Pnext;
                            try
                            {
                                if (double.IsNaN(Pline) || U.isZero(Qliq))
                                    Pnext = Pline;
                                else
                                {
                                    var angleDeg = e.GetAngleDeg(nodes) * direction;
                                    Pnext = PressureDrop.dropLiq(ctx, gd,
                                        D_mm: e.D, L0_m: 0, L1_m: e.L,
                                        Roughness: 0.0,
                                        flowDir: (PressureDrop.FlowDirection)direction,
                                        P0_MPa: ctx[PVT.Prm.P], Qliq, WCT, GOR,
                                        dL_m: 20, dP_MPa: 1e-4, maxP_MPa: 60, stepsInfo: steps,
                                        getTempK: (Qo, Qw, L) => 273 + 20,
                                        getAngle: _ => angleDeg,
                                        gradCalc: Gradient.BegsBrill.Calc,
                                        WithFriction: false
                                    );
                                }
                                if (resNodeInfo.TryGetValue(iNextNode, out var I))
                                    I.Update(P: U.MPa2Atm(Pnext));
                                else
                                    resNodeInfo.Add(iNextNode, new NodeInfo(P: U.MPa2Atm(Pnext)));
                            }
                            catch (Exception ex)
                            {
                                var cn = nodes[iCurNode];
                                var nn = nodes[iNextNode];
                                Logger.TraceInformation($"Error calc for edge A->B from well\tNodeA={cn.Node_ID}\tNodeB={nn.Node_ID}\t{nameof(wi.Well_ID)}={wi.Well_ID}\tP={ctx[PVT.Prm.P]}\tQ={Qliq}\tEx={ex.Message}");
                                if (!resNodeInfo.TryGetValue(iNextNode, out var I))
                                    resNodeInfo.Add(iNextNode, new NodeInfo(P: double.NaN));
                                break;
                            }

                            iCurNode = iNextNode;

                            if (edgeStack.Count == 0)
                                break;

                            ctx = root.NewCtx()
                                .With(PVT.Prm.P, Pnext)
                                .Done();

                        }
                        #endregion
                    }
                    finally { edgeStack.Clear(); }
                }

                return setOfNextNodes;
            }

            /// <summary>
            /// Расчёт далее от узлов с, предположительно, известным давлением и входными дебитами
            /// </summary>
            /// <param name="nextNodes">узлы для дальнейшего расчёта</param>
            void FromNextNodes(IEnumerable<int> nextNodes)
            {
                var nextNodesQueue = new Queue<int>(nextNodes);
                while (nextNodesQueue.Count > 0)
                {
                    int iNode = nextNodesQueue.Dequeue();
                    var lstEdges = nodeEdges[iNode];

                    // умеем считать только один неизвестный/исходящий из узла поток
                    if (lstEdges.Count - 1 != lstEdges.Count(i => resEdgeInfo.TryGetValue(i, out var I)))
                        continue; // если не один, пропускаем

                    FluidInfo fluid = null;
                    int iEdgeOut = -1;
                    var Qliq = 0d;
                    var WCT = 0d;

                    #region Определяем для узла дебит и характеристики флюида
                    {
                        double sumW = 0, sumO = 0, maxO = 0;
                        foreach (var iEdge in lstEdges)
                        {
                            if (!resEdgeInfo.TryGetValue(iEdge, out var I))
                            { iEdgeOut = iEdge; continue; }

                            if (double.IsNaN(I.edgeQ))
                                continue; // ошибочно посчитанные пропускаем

                            var O = I.edgeQ * (1 - I.watercut);
                            var W = I.edgeQ * I.watercut;
                            if (maxO < O)
                            {   // характеристики флюида пока берём от входящего потока с наибольшим дебитом нефти
                                fluid = I.fluid;
                                maxO = O;
                            }
                            sumO += O;
                            sumW += W;
                        }
                        Qliq = sumO + sumW;

                        if (U.isZero(Qliq))
                            continue; // нет данных по итоговому дебиту через узел, пропускаем

                        WCT = sumW / Qliq; // пересчитываем обводнённость суммарного потока
                    }
                    #endregion

                    var Pin = resNodeInfo[iNode].nodeP;
                    #region И опять расчёт)
                    var root = fluid.GetPvtContext();
                    var ctx = root.NewCtx()
                        .With(PVT.Prm.P, U.Atm2MPa(Pin))
                        .Done();
                    var gd = new Gradient.DataInfo();
                    List<PressureDrop.StepInfo> steps = null;
                    var GOR = root[PVT.Arg.Rsb]; // todo: what with GOR ?

                    resEdgeInfo.Add(iEdgeOut, new EdgeInfo(Q: Qliq, WCT: WCT, f: fluid));

                    var e = edges[iEdgeOut];
                    var (iNextNode, direction) = e.Next(iNode);

                    double Pnext;
                    try
                    {
                        var angleDeg = e.GetAngleDeg(nodes) * direction;
                        Pnext = PressureDrop.dropLiq(ctx, gd,
                            D_mm: e.D, L0_m: 0, L1_m: e.L,
                            Roughness: 0.0,
                            flowDir: (PressureDrop.FlowDirection)direction,
                            P0_MPa: ctx[PVT.Prm.P], Qliq, WCT, GOR,
                            dL_m: 20, dP_MPa: 1e-4, maxP_MPa: 60, stepsInfo: steps,
                            getTempK: (Qo, Qw, L) => 273 + 20,
                            getAngle: _ => angleDeg,
                            gradCalc: Gradient.BegsBrill.Calc,
                            WithFriction: false
                        );
                        var P_atm = U.MPa2Atm(Pnext);
                        if (resNodeInfo.TryGetValue(iNextNode, out var I))
                            I.Update(P: P_atm);
                        else resNodeInfo.Add(iNextNode, new NodeInfo(P: P_atm));
                        nextNodesQueue.Enqueue(iNextNode);
                    }
                    catch (Exception ex)
                    {
                        var cn = nodes[iNode];
                        var nn = nodes[iNextNode];
                        Logger.TraceInformation($"Error calc for edge A->B\tNodeA={cn.Node_ID}\tNodeB={nn.Node_ID}\tP={ctx[PVT.Prm.P]}\tQ={Qliq}\tEx={ex.Message}");
                        if (!resNodeInfo.TryGetValue(iNextNode, out var I))
                            resNodeInfo.Add(iNextNode, new NodeInfo(P: double.NaN));
                        break;
                    }
                    #endregion
                }
            }

        }

        public static (Dictionary<int, EdgeInfo> edgeI, Dictionary<int, NodeInfo> nodeI)
            Calc<TID>(Edge[] edges, Node<TID>[] nodes, int[] subnet, IReadOnlyDictionary<int, WellInfo<TID>> nodeWells)
            where TID : struct
        {
            var impl = new Impl<TID>(edges, nodes, subnet);
            return impl.Calc(nodeWells);
        }


        /// <summary>
        /// Export to TGF (Trivial Graph Format)
        /// </summary>
        public static void ExportTGF<TID>(TextWriter wr, Edge[] edges, Node<TID>[] nodes, int[] subnetEdges,
            Func<int, string> getNodeName,
            Func<int, string> getNodeExtra = null,
            Func<int, string> getEdgeExtra = null
        )
            where TID : struct
        {
            var subnetNodes = new HashSet<int>();

            foreach (var iEdge in subnetEdges)
            {
                subnetNodes.Add(edges[iEdge].iNodeA);
                subnetNodes.Add(edges[iEdge].iNodeB);
            }

            foreach (var iNode in subnetNodes)
            {
                var descr = getNodeName == null ? null : W.Common.Utils.Transliterate(getNodeName(iNode).Trim());
                var extra = getNodeExtra == null ? null : getNodeExtra(iNode);
                wr.WriteLine($"{iNode} {(int)nodes[iNode].kind}:{descr}{extra}");
            }
            wr.WriteLine("#");
            foreach (var iEdge in subnetEdges)
            {
                var e = edges[iEdge];
                var extra = getEdgeExtra == null ? null : getEdgeExtra(iEdge);
                wr.WriteLine(FormattableString.Invariant($"{e.iNodeA} {e.iNodeB} d{e.D}/L{e.L}{extra}"));
            }
        }
    }
}
