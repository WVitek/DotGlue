﻿using System;
using System.Collections.Generic;
using System.IO;

namespace PipeNetCalc
{
    [Serializable]
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

        public double GetAngleDeg(Node[] nodes)
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

    [Serializable]
    public struct Node
    {
        public NodeKind kind;
        public string Node_ID;
        public float Altitude;

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

    public static class PipeGraph
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

        public static IEnumerable<int[]> Subnets(this Edge[] edges, Node[] nodes, params int[] fromEdges)
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

        /// <summary>
        /// Export to TGF (Trivial Graph Format)
        /// </summary>
        public static void ExportToTGF(TextWriter wr, Edge[] edges, Node[] nodes, int[] subnetEdges,
            Func<int, string> getNodeName,
            Func<int, string> getNodeExtra = null,
            Func<int, string> getEdgeExtra = null
        )
        {
            var subnetNodes = new HashSet<int>();

            foreach (var iEdge in subnetEdges)
            {
                subnetNodes.Add(edges[iEdge].iNodeA);
                subnetNodes.Add(edges[iEdge].iNodeB);
            }

            foreach (var iNode in subnetNodes)
            {
                var descr = getNodeName == null ? null : Transliterate(getNodeName(iNode)?.Trim());
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

        static string transl(char c)
        {
            switch (c)
            {
                case 'а': return "a";
                case 'б': return "b";
                case 'в': return "v";
                case 'г': return "g";
                case 'д': return "d";
                case 'е': return "e";
                case 'ё': return "yo";
                case 'ж': return "zh";
                case 'з': return "z";
                case 'и': return "i";
                case 'й': return "j";
                case 'к': return "k";
                case 'л': return "l";
                case 'м': return "m";
                case 'н': return "n";
                case 'о': return "o";
                case 'п': return "p";
                case 'р': return "r";
                case 'с': return "s";
                case 'т': return "t";
                case 'у': return "u";
                case 'ф': return "f";
                case 'х': return "x";
                case 'ц': return "cz";
                case 'ч': return "ch";
                case 'ш': return "sh";
                case 'щ': return "shh";
                case 'ъ': return "``";
                case 'ы': return "y`";
                case 'ь': return "`";
                case 'э': return "e`";
                case 'ю': return "yu";
                case 'я': return "ya";
                default: return null;
            }
        }

        /// <summary>
        /// Transliterate string as in https://transliteration-online.ru/
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string Transliterate(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length * 3 / 2);
            foreach (char c in s)
            {
                char lc = char.ToLowerInvariant(c);
                var ts = transl(lc);
                if (ts == null)
                { sb.Append(c); continue; }
                sb.Append(ts);
                if (lc != c)
                    sb[sb.Length - ts.Length] = char.ToUpperInvariant(sb[sb.Length - ts.Length]);
            }
            return sb.ToString();
        }

    }
    /// <summary>
    /// To serialize assembly classes with FileCache (https://github.com/acarteas/FileCache)
    /// </summary>
    public sealed class ObjectBinder : System.Runtime.Serialization.SerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            Type typeToDeserialize = null;
            //var currentAssembly = System.Reflection.Assembly.GetExecutingAssembly().FullName;

            // In this case we are always using the current assembly
            //assemblyName = currentAssembly;

            // Get the type using the typeName and assemblyName
            typeToDeserialize = Type.GetType(string.Format("{0}, {1}", typeName, assemblyName));

            return typeToDeserialize;
        }
    }


}
