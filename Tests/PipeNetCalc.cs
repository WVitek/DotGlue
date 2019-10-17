﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pipe.Exercises
{
    public static class PipeNetCalc
    {
        public struct WellInfo
        {
            public ulong Well_ID;
            public double Line_Pressure__Atm;
            public double Liq_VolRate;
            public double Liq_Watercut;
            public double Liq_Viscosity;
            public double Oil_Comprssblty;
            public double Bottomhole_Pressure__Atm;
            public double Oil_GasFactor;
            public double Oil_Density;
            public double Water_Density;
            public double LayerShut_Pressure__Atm;
            public double Temperature__C;
            public double Water_Viscosity;
            public double Oil_Viscosity;
        }

        public struct PipeInfo
        {

        }

        static void AddNodeEdge(Dictionary<int,List<int>> nodeEdges, int iNode, int iEdge)
        {
            if (!nodeEdges.TryGetValue(iNode, out var lst))
            {
                lst = new List<int>();
                nodeEdges[iNode] = lst;
            }
            lst.Add(iEdge);
        }

        public static void Calc(Edge[] edges, int[] subnet, IDictionary<int, WellInfo> nodeWells, Func<int, bool> IsMeterNode)
        {
            var nodeEdges = new Dictionary<int, List<int>>();
            foreach(var i in subnet)
            {
                AddNodeEdge(nodeEdges, edges[i].iNodeA, i);
                AddNodeEdge(nodeEdges, edges[i].iNodeB, i);
            }
        }
    }
}
