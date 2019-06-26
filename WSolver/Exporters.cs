using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace W.Expressions.Solver
{
    public static class Exporters
    {
        struct NodeInfo
        {
            public List<string> inps;
            public List<string> outs;
            public NodeInfo(IEnumerable<string> inps)
            {
                if (inps != null)
                    this.inps = new List<string>(inps);
                else
                    this.inps = new List<string>();
                outs = new List<string>();
            }
        }

        const string sFuncSrcData = "\"Source Data\"";
        static string charCase(string s) { return s.ToUpperInvariant(); }
        static string GetPort(this IList lst, int i) { return charCase(Convert.ToString(lst[i])); }

        public static string SolverDependencies2GraphVis(IDictionary<string, object> solverDeps
            , IDictionary<string, string> paramzCaptions
            , bool multiEdge
            , bool withTables
            , bool showPrmNames
            , params string[] outputParams
        )
        {
            if (paramzCaptions == null)
                showPrmNames = true;
            var sb = new StringBuilder();
            sb.AppendLine("digraph G {");
            sb.AppendLine("rankdir = \"LR\";");
            //if (paramzCaptions != null && showPrmNames)
            sb.AppendLine("node [fontname=\"arial narrow\"];");
            if (!withTables)
                sb.AppendLine("node [shape=record]");
            {
                var nodes = new Dictionary<string, NodeInfo>();
                nodes.Add(sFuncSrcData, new NodeInfo(null));
                var dictPort2Node = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                #region Collect nodes info
                foreach (var pair in solverDeps)
                {
                    var lst = (IList)pair.Value;
                    string fn;
                    if (lst == null)
                        fn = sFuncSrcData;
                    else
                        fn = Convert.ToString(lst[0]).Substring(FuncDefs_Solver.sDepsFuncNamePrefix.Length);
                    NodeInfo nodeInfo;
                    if (!nodes.TryGetValue(fn, out nodeInfo))
                    {
                        nodeInfo = new NodeInfo(Enumerable.Range(1, lst.Count - 1).Select(i => Convert.ToString(lst[i])));
                        nodes.Add(fn, nodeInfo);
                    }
                    nodeInfo.outs.Add(pair.Key);
                    dictPort2Node.Add(pair.Key, fn);
                }
                #endregion
                sb.AppendLine();
                #region Declare nodes
                var dictOutPrms = outputParams.ToDictionary(s => s, s => true, StringComparer.OrdinalIgnoreCase);
                foreach (var node in nodes)
                {
                    var nodeName = node.Key;
                    if (nodeName == sFuncSrcData)
                        continue;
                    var nodeInfo = node.Value;
                    if (withTables)
                    {
                        sb.Append(nodeName);
                        sb.AppendLine(" [shape=none, label=<<TABLE BORDER=\"0\" CELLBORDER=\"1\" CELLSPACING=\"0\" CELLPADDING=\"4\">");
                        {
                            sb.AppendFormat("\t<TR><TD BGCOLOR=\"lightblue\"><b>{0}</b></TD></TR>", nodeName);
                            //sb.AppendFormat("\t<TR><TD BGCOLOR=\"dimgray\"><FONT COLOR=\"white\"><b>{0}</b></FONT></TD></TR>", nodeName);
                            bool first = true;
                            foreach (var param in nodeInfo.inps)
                            {
                                if (first)
                                    first = false;
                                else if (Common.ValueInfo.IsID(param))
                                    continue;
                                string prm = showPrmNames ? "<b>" + param + "</b>" : null;
                                string fmt = (dictPort2Node[param] == sFuncSrcData) ? " BGCOLOR=\"gold\"" : null;
                                string cap;
                                if (paramzCaptions != null && paramzCaptions.TryGetValue(param, out cap))
                                    cap = " : " + cap;
                                else cap = null;
                                sb.AppendFormat("\t<TR><TD PORT=\"i{0}\" ALIGN=\"LEFT\"{1}>{2}{3}</TD></TR>\r\n", charCase(param), fmt, prm, cap);
                            }
                            foreach (var param in nodeInfo.outs)
                            {
                                string prm = showPrmNames ? "<b>" + param + "</b>" : null;
                                string fmt = dictOutPrms.ContainsKey(param) ? " BGCOLOR=\"greenyellow\"" : null;
                                string cap;
                                if (paramzCaptions != null && paramzCaptions.TryGetValue(param, out cap))
                                    cap = cap + " : ";
                                else cap = null;
                                sb.AppendFormat("\t<TR><TD PORT=\"o{0}\" ALIGN=\"RIGHT\"{1}>{2}{3}</TD></TR>\r\n", charCase(param), fmt, cap, prm);
                            }
                        }
                        sb.AppendLine("\t</TABLE>>]");
                    }
                    else
                    {
                        sb.Append(nodeName);
                        sb.AppendFormat(" [label=\"{0}|{1}\"]\r\n", nodeName.Replace("\"", ""), string.Join("|", nodeInfo.outs));
                    }
                }
                #endregion
                sb.AppendLine();
                #region Declare edges
                foreach (var pair in solverDeps)
                {
                    var lst = (IList)pair.Value;
                    if (lst == null) continue;
                    var fn = Convert.ToString(lst[0]).Substring(FuncDefs_Solver.sDepsFuncNamePrefix.Length);
                    if (!nodes.Remove(fn))
                        continue; // edges to this func already added
                    var fromNodes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lst.Count; i++)
                    {
                        var port = charCase(Convert.ToString(lst[i]));
                        if (i > 1)
                            if (Common.ValueInfo.IsID(port))
                                continue;
                        var fromNode = dictPort2Node[port];
                        if (fromNode == sFuncSrcData)
                            continue;
                        if (multiEdge)
                            sb.AppendFormat("\t{0}:o{1} -> {2}:i{3}\r\n", fromNode, port, fn, port);
                        else
                        {
                            if (fromNodes.ContainsKey(fromNode))
                                continue;
                            fromNodes.Add(fromNode, true);
                            sb.AppendFormat("\t{0} -> {1}\r\n", fromNode, fn);
                        }
                    }
                }
                #endregion
            }
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
