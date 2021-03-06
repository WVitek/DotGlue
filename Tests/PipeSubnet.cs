﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pipe.Exercises
{
    public struct Edge
    {
        public int iNodeA, iNodeB, color;
        public float D, L;

        public override string ToString() => $"{color}:{iNodeA}-{iNodeB}";
        public bool IsIdentical(ref Edge e) => iNodeA == e.iNodeA && iNodeB == e.iNodeB && D == e.D && L == e.L;

        public (int iNextNode, int direction) Next(int iNode)
        {
            if (iNode == iNodeA)
                return (iNodeB, +1);  // forward direction
            if (iNode == iNodeB)
                return (iNodeA, -1); // backward direction
            throw new ArgumentException($"{nameof(Edge)}.{nameof(Next)}", nameof(iNode)); // wrong iNode specified
        }

        public double GetAngleDeg<TID>(Node<TID>[] nodes) where TID : struct
        {
            var ZA = nodes[iNodeA].Altitude;
            var ZB = nodes[iNodeB].Altitude;
            if (double.IsNaN(ZA) || double.IsNaN(ZB))
                return 0d;
            const double Rad2Deg = 180 / Math.PI;
            return Math.Atan2(ZB - ZA, L) * Rad2Deg;
        }
    }

    public enum NodeKind
    {
        Unknown = 0,
        /// <summary>
        /// Куст
        /// </summary>
        Cluster = 1,
        /// <summary>
        /// Скважина
        /// </summary>
        Well = 2,
        /// <summary>
        /// Точка
        /// </summary>
        Point = 3,
        /// <summary>
        /// ГЗУ (групповая замерная установка)
        /// </summary>
        Meter = 5,
        /// <summary>
        /// БГ (блог гребёнок)
        /// </summary>
        InjFork = 6,
    }

    public struct Node<TID> where TID : struct
    {
        public NodeKind kind;
        public TID Node_ID;
        public double Altitude;

        /// <summary>
        /// Гидравлически "прозрачный" узел?
        /// </summary>
        /// <returns></returns>
        public bool IsTransparent()
        {
            switch (kind)
            {
                case NodeKind.Cluster:
                //case NodeKind.Well:
                case NodeKind.Point:
                case NodeKind.Meter:
                case NodeKind.InjFork:
                    return true;
                default:
                    return false;
            };
        }

        /// <summary>
        /// Узел куста/АГЗУ ?
        /// </summary>
        /// <returns></returns>
        public bool IsMeterOrClust() => kind == NodeKind.Meter || kind == NodeKind.Cluster;
    }

    public static class PipeSubnet
    {
        static int IndexOfFalse(this bool[] flags)
        {
            for (int i = 0; i < flags.Length; i++)
                if (!flags[i])
                    return i;
            return -1;
        }


        static void AddNodeEdge(ref List<int> edges, int iEdge)
        {
            if (edges == null)
                edges = new List<int>();
            edges.Add(iEdge);
        }

        public static IEnumerable<int[]> EnumSubnets<TID>(this Edge[] edges, Node<TID>[] nodes, params int[] fromEdges) where TID : struct
        {
            var usedEdge = new bool[edges.Length];
            var nodeEdges = new List<int>[nodes.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                var e = edges[i];
                if (e.iNodeA >= 0 && e.iNodeB >= 0)
                {
                    AddNodeEdge(ref nodeEdges[e.iNodeA], i);
                    AddNodeEdge(ref nodeEdges[e.iNodeB], i);
                }
                else usedEdge[i] = true;
            }
            int iFrom = fromEdges.Length - 1;
            var edgesQueue = new Queue<int>();
            var nextNodes = new HashSet<int>();
            var outEdges = new List<int>();
            while (true)
            {
                int firstEdge = (fromEdges.Length == 0) ? IndexOfFalse(usedEdge) : (iFrom < 0) ? -1 : fromEdges[iFrom--];
                if (firstEdge < 0)
                    yield break;

                // первое ("затравочное") ребро для поиска гидравлически связанной подсети
                edgesQueue.Enqueue(firstEdge); usedEdge[firstEdge] = true;

                // цвет "подсети" (все рёбра должны быть только этого "цвета")
                int subnetColor = edges[firstEdge].color;

                while (edgesQueue.Count > 0)
                {
                    // формируем множество гидравлически "прозрачных" вершин, инцидентных ранее найденным рёбрам
                    foreach (var i in edgesQueue)
                    {
                        outEdges.Add(i);
                        int iA = edges[i].iNodeA;
                        if (nodes[iA].IsTransparent())
                            nextNodes.Add(iA);
                        int iB = edges[i].iNodeB;
                        if (nodes[iB].IsTransparent())
                            nextNodes.Add(iB);
                    }
                    edgesQueue.Clear();

                    // добавляем ранее не пройденные смежных ребёр нужного "цвета"
                    foreach (var iNode in nextNodes)
                    {
                        foreach (int i in nodeEdges[iNode])
                        {
                            if (usedEdge[i] || edges[i].color != subnetColor)
                                continue;
                            if (!nextNodes.Contains(edges[i].iNodeA) && !nextNodes.Contains(edges[i].iNodeB))
                                continue;
                            edgesQueue.Enqueue(i);
                            usedEdge[i] = true;
                        }
                    }
                    nextNodes.Clear();
                }

                yield return outEdges.ToArray();

                outEdges.Clear();
            }
        }
    }
}
