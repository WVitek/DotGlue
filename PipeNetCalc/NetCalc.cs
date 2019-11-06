using System;
using System.Collections.Generic;
using System.Linq;
using W.Oilca;

namespace PipeNetCalc
{
    public static class NetCalc
    {
        public static System.Diagnostics.TraceSource Logger = W.Common.Trace.GetLogger("NetCalc");

        public class FluidInfo
        {
            public float Oil_VolumeFactor;
            public float Bubblpnt_Pressure__Atm; // bubble_point_pressure
            public float Oil_GasFactor;
            public float Oil_Density;
            public float Water_Density;
            public float Gas_Density;
            public float Reservoir_Pressure__Atm; // todo: Init_shut_pressure
            public float Temperature__C;
            public float Water_Viscosity;
            public float Oil_Viscosity;

            public bool IsEmpty => Water_Density == 0;

            public PVT.Context.Root GetPvtContext()
            {
                var refP = PVT.Prm.P._(PVT.Arg.P_SC, PVT.Arg.P_RES);
                var refT = PVT.Prm.T._(PVT.Arg.T_SC, PVT.Arg.T_RES);

                var root = PVT.NewCtx()
                    .With(PVT.Arg.GAMMA_O, Oil_Density)
                    .With(PVT.Arg.Rsb, Oil_GasFactor)
                    .With(PVT.Arg.GAMMA_G, Gas_Density)
                    .With(PVT.Arg.GAMMA_G_SEP, 1)
                    .With(PVT.Arg.GAMMA_W, Water_Density)
                    .With(PVT.Arg.S, PVT.WaterSalinity_From_Density(Water_Density))
                    .With(PVT.Arg.P_SC, U.Atm2MPa(1))
                    .With(PVT.Arg.T_SC, 273 + 20)
                    .With(PVT.Arg.P_RES, U.Atm2MPa(Reservoir_Pressure__Atm))
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
                    .WithRescale(PVT.Prm.Bo._(1, Oil_VolumeFactor), PVT.Bo_DEFAULT, refP, refT)
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
                    .With(PVT.Prm.Mu_w, PVT.Mu_w_MCCAIN_1990) // todo: need .Rescale with Water_Viscosity
                    .With(PVT.Prm.Mu_g, PVT.Mu_g_LGE_MCCAIN_1991)
                    .With(PVT.Prm.Tpc, PVT.Tpc_SUTTON_2005)
                    .With(PVT.Prm.Ppc, PVT.Ppc_SUTTON_2005)
                    .Done();
                return root;
            }
        }

        public class WellInfo : FluidInfo
        {
            public string Well_ID;
            public string Layer;
            public float Line_Pressure__Atm;
            public float Liq_VolRate;
            public float Liq_Watercut;

            public static readonly WellInfo Unknown = new WellInfo() { Line_Pressure__Atm = float.NaN, Liq_Watercut = 1 };
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
            public float nodeP;
            public NodeInfo(float P) { nodeP = P; }
            public void Update(float P)
            {
                if (nodeP > P || float.IsNaN(nodeP))
                    nodeP = P;
            }
            public override string ToString() => FormattableString.Invariant($"P={nodeP:0.###}");
        }

        /// <summary>
        /// Реализация гидравлического расчёта подсети
        /// </summary>
        /// <typeparam name="TID"></typeparam>
        class Impl
        {
            public readonly Edge[] edges;
            public readonly Node[] nodes;
            public readonly int[] subnet;
            public PressureDrop.StepHandler stepHandler;

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

            public Impl(Edge[] edges, Node[] nodes, int[] subnet)
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

            NodeInfo UpdateNodeInfo(int iNode, float P_atm)
            {
                if (!resNodeInfo.TryGetValue(iNode, out var I))
                {
                    I = new NodeInfo(P: P_atm);
                    resNodeInfo.Add(iNode, I);
                }
                else I.Update(P: P_atm);
                return I;
            }

            public (IReadOnlyDictionary<int, EdgeInfo> edgeI, IReadOnlyDictionary<int, NodeInfo> nodeI)
                ImplCalc(IReadOnlyDictionary<int, WellInfo> nodeWells)
            {
                FromWells(nodeWells);
                FromDeadEnds();
                CalcNextNodes();
                return (resEdgeInfo, resNodeInfo);
            }


            /// <summary>
            /// "Отсечение" тупиковых путей путём заполнения их нулевыми дебитами и NaN-давлениями 
            /// для определённости начальных условий по ним
            /// </summary>
            void FromDeadEnds()
            {
                foreach (var pair in nodeEdges)
                {
                    int iNode = pair.Key;

                    if (nodes[iNode].IsTransparent() == false || resNodeInfo.ContainsKey(iNode))
                        continue; // пропускаем, если узел "непрозрачен" или уже имеет заданные параметры

                    if (!nodeEdges.TryGetValue(iNode, out var lstEdges) || lstEdges.Count > 1)
                        continue; // пропускаем, если более одного ребра (не тупик)

                    int iDeadEndEdge = lstEdges[0];
                    if (resEdgeInfo.TryGetValue(iDeadEndEdge, out var I))
                        continue;
                    // на тупиковую вершину назначаем неопределённое давление
                    int iDeadEndNode = iNode;
                    UpdateNodeInfo(iDeadEndNode, float.NaN);
                    // на тупиковое ребро назначаем нулевой дебит и неизвестный флюид
                    resEdgeInfo.Add(iDeadEndEdge, new EdgeInfo(0, 0, WellInfo.Unknown));
                    // следующая от тупиковой вершины будет "затравочной" для протягивания данных о тупике далее
                    int iNextNode = edges[iDeadEndEdge].Next(iDeadEndNode).iNextNode;
                    UpdateNodeInfo(iNextNode, float.NaN);
                }
            }

            /// <summary>
            /// Распределение данных от скважин по элементам подсети
            /// (Pлин в замерные узлы, дебиты и флюиды в соответствующие рёбра замерных узлов)
            /// </summary>
            /// <param name="nodeWells">Данные по скважиным</param>
            void FromWells(IReadOnlyDictionary<int, WellInfo> nodeWells)
            {
                var edgesToCalc = new List<(int iEdge, int iFromNode)>();
                // обход всех узлов-скважин
                foreach (var pair in nodeEdges)
                {
                    int iWellNode = pair.Key;

                    if (nodes[iWellNode].kind != NodeKind.Well)
                        continue;

                    if (!nodeWells.TryGetValue(pair.Key, out WellInfo wi))
                    {   // нет информации по скважине
                        var cn = nodes[iWellNode];
                        if (cn.kind == NodeKind.Well)
                            Logger.TraceInformation($"No fluid information for well node\t{nameof(cn.Node_ID)}={cn.Node_ID}");
                        wi = WellInfo.Unknown;
                    }

                    int iMeterNode = iWellNode, iPrevNode = -1, iMeterEdge = -1;
                    #region Пытаемся от скважины пройти по рёбрам до замерного узла (АГЗУ/куста)
                    for (int iNode = iWellNode, nDist = 0; ;)
                    {
                        int n = nodeEdges.TryGetValue(iNode, out var lstEdges) ? lstEdges.Count : 0;
                        int nValid = (iNode == iWellNode) ? 1 : 2;

                        if (n != nValid)
                        {   // От скважины пришли в тупик или незамерную развилку. Странно)
                            var cn = nodes[iNode];
                            Logger.TraceInformation($"No single way found from well to '{nameof(cn.IsMeterOrClust)}' node\t{nameof(cn.Node_ID)}={cn.Node_ID}\t{nameof(wi.Well_ID)}={wi.Well_ID}");
                            break;
                        }

                        int iEdge = -1;
                        foreach (int i in lstEdges)
                        {
                            int iNextNode = edges[i].Next(iNode).iNextNode;
                            if (iNextNode == iPrevNode)
                                continue;
                            iEdge = i; iNode = iNextNode;
                        }

                        nDist++;

                        // Даже если не найдём узел куста/АГЗУ, таковым будем считать последний в подходящей цепочке,
                        // последнее ребро будем считать ребром, с которым ассоциирован замер дебита скважины
                        iMeterNode = iNode;
                        iMeterEdge = iEdge;

                        // Нашли узел куста/АГЗУ ?
                        if (nodes[iNode].IsMeterOrClust())
                            break;

                        if (nDist > 16)
                        {   // либо нефизично "далеко" до куста/АГЗУ, либо цикл
                            var cn = nodes[iNode];
                            Logger.TraceInformation($"Valid way from well not found, stopped @node\t{nameof(cn.Node_ID)}={cn.Node_ID}\t{nameof(wi.Well_ID)}={wi.Well_ID}");
                            break;
                        }
                    }
                    #endregion

                    // заносим значение Pline в замерной узел
                    var Pline = wi.Line_Pressure__Atm;
                    var I = UpdateNodeInfo(iMeterNode, Pline);
                    if (!U.isEQ(I.nodeP, Pline))
                    {   // при несоответствии давлений Pлин в данных по разным скважинам, стараемся использовать минимальное
                        Pline = I.nodeP;
                        var cn = nodes[iMeterNode];
                        Logger.TraceInformation($"Line pressure mismatch detected\t{nameof(cn.Node_ID)}={cn.Node_ID}\t{nameof(wi.Well_ID)}={wi.Well_ID}\t{I.nodeP}<>{Pline}");
                    }

                    // Обратный просчёт одного ребра от узла замера дебита в сторону скважины
                    // todo: для гипотетической "висящей" скважины нет исходящего ребра, записать дебит некуда
                    if (iMeterEdge >= 0)
                    {
                        resEdgeInfo.Add(iMeterEdge, new EdgeInfo(wi.Liq_VolRate, wi.Liq_Watercut, wi));
                        // посчитаем попозже, когда окончательно определится значение Pline для замерного узла
                        edgesToCalc.Add((iMeterEdge, iMeterNode));
                    }
                }

                foreach (var (iEdge, iFromNode) in edgesToCalc)
                {
                    var ni = resNodeInfo[iFromNode];
                    var ei = resEdgeInfo[iEdge];
                    var (iNextNode, Pout) = CalcEdge(iEdge, iFromNode,
                        Pin: ni.nodeP, Qliq: ei.edgeQ, WCT: ei.watercut, fluid: ei.fluid,
                        addEdgeInfo: false);
                    //UpdateNodeInfo(iNextNode, (float)Pout); //already in CalcEdge
                }
            }

            /// <summary>
            /// Расчёт по ребру iEdge, выходящему из узла iNode с указанными параметрами потока
            /// </summary>
            (int iNextNode, double Pout)
                CalcEdge(int iEdge, int iNode, double Pin, double Qliq, double WCT, FluidInfo fluid, bool addEdgeInfo = true)
            {
                if (addEdgeInfo)
                    resEdgeInfo.Add(iEdge, new EdgeInfo(Q: Qliq, WCT: WCT, f: fluid));

                var e = edges[iEdge];
                var (iNextNode, direction) = e.Next(iNode);

                var Pout = double.NaN;

                if (!double.IsNaN(Pin) && !fluid.IsEmpty)
                {

                    var root = fluid.GetPvtContext();
                    var ctx = root.NewCtx()
                        .With(PVT.Prm.P, U.Atm2MPa(Pin))
                        .Done();

                    var gd = new Gradient.DataInfo();
                    var GOR = root[PVT.Arg.Rsb]; // todo: what with GOR ?

                    try
                    {
                        var angleDeg = e.GetAngleDeg(nodes) * direction;
                        var P_MPa = PressureDrop.dropLiq(ctx, gd,
                            D_mm: e.D, L0_m: 0, L1_m: e.L,
                            Roughness: 0.0,
                            flowDir: (PressureDrop.FlowDirection)direction,
                            P0_MPa: ctx[PVT.Prm.P], Qliq, WCT, GOR,
                            dL_m: 20, dP_MPa: 1e-4, maxP_MPa: 60, stepHandler: stepHandler, (iEdge + 1) * direction,
                            getTempK: (Qo, Qw, L) => 273 + 20,
                            getAngle: _ => angleDeg,
                            gradCalc: Gradient.BegsBrill.Calc,
                            WithFriction: false
                        );
                        Pout = U.MPa2Atm(P_MPa);
                    }
                    catch (Exception ex)
                    {
                        var cn = nodes[iNode];
                        var nn = nodes[iNextNode];
                        Logger.TraceInformation($"Error calc for edge A->B\tNodeA={cn.Node_ID}\tNodeB={nn.Node_ID}\tP={ctx[PVT.Prm.P]}\tQ={Qliq}\tEx={ex.Message}");
                    }
                }
                UpdateNodeInfo(iNextNode, (float)Pout);
                return (iNextNode, Pout);
            }

            /// <summary>
            /// Расчёт далее от узлов с, предположительно, известным давлением и входными дебитами
            /// </summary>
            /// <param name="nextNodes">узлы для дальнейшего расчёта</param>
            void CalcNextNodes()
            {
                // Начинаем с узлов с заданными параметрами
                var nextNodesQueue = new Queue<int>(resNodeInfo.Keys);

                while (nextNodesQueue.Count > 0)
                {
                    int iNode = nextNodesQueue.Dequeue();

                    if (!nodeEdges.TryGetValue(iNode, out var lstEdges))
                        continue; // одиночные узлы без связей пропускаем

                    // умеем считать только один неизвестный/исходящий из узла поток
                    if (lstEdges.Count - 1 != lstEdges.Count(i => resEdgeInfo.TryGetValue(i, out var I)))
                        continue; // если не один, нижерасположенным расчётом посчитать не получится


                    var Pin = resNodeInfo[iNode].nodeP;

                    FluidInfo fluid = null;
                    int iEdgeOut = -1;
                    double Qliq = 0, Qwat = 0;

                    #region Определяем для узла дебит и характеристики флюида
                    {
                        double Qoil = 0, maxO = 0;
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
                            Qoil += O;
                            Qwat += W;
                        }
                        Qliq = Qoil + Qwat;

                    }
                    #endregion

                    int iNextNode;

                    if (double.IsNaN(Pin))
                    {   // протягиваем NaN-давление из тупика
                        iNextNode = edges[iEdgeOut].Next(iNode).iNextNode;
                        // из узла с NaN-давлением выходной дебит принимаем нулевым
                        resEdgeInfo.Add(iEdgeOut, new EdgeInfo(0, 0, WellInfo.Unknown));
                        // в следующем узле, возможно, тоже будет NaN
                        UpdateNodeInfo(iNextNode, float.NaN);
                    }
                    else if (fluid != null && !U.isZero(Qliq))
                    {
                        var WCT = Qwat / Qliq; // пересчитываем обводнённость суммарного потока
                        iNextNode = CalcEdge(iEdgeOut, iNode, Pin, Qliq, WCT, fluid).iNextNode;
                    }
                    else continue;
                    // от узла с посчитанными или протянутыми данными потом будем пытаться считать дальше
                    nextNodesQueue.Enqueue(iNextNode);
                }
            }
        }

        public static (IReadOnlyDictionary<int, EdgeInfo> edgeI, IReadOnlyDictionary<int, NodeInfo> nodeI)
            Calc(
                Edge[] edges, Node[] nodes, int[] subnet,
                IReadOnlyDictionary<int, WellInfo> nodeWells,
                PressureDrop.StepHandler stepHandler
            )
        {
            var impl = new Impl(edges, nodes, subnet);
            impl.stepHandler = stepHandler;
            return impl.ImplCalc(nodeWells);
        }
    }
}
