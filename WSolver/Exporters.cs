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
		static readonly string s_ID_ = charCase("_ID_");
		static readonly string s_ID = charCase("_ID_");
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
			if (paramzCaptions != null && showPrmNames)
				sb.AppendLine("node [fontname=\"arial narrow\"];");
			if (!withTables)
				sb.AppendLine("node [shape=record]");
			{
				var nodes = new Dictionary<string, NodeInfo>();
				nodes.Add(sFuncSrcData, new NodeInfo(null));
				var dictPort2Node = new Dictionary<string, string>();
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
						nodeInfo = new NodeInfo(Enumerable.Range(1, lst.Count - 1).Select(i => lst.GetPort(i)));
						nodes.Add(fn, nodeInfo);
					}
					var key = charCase(pair.Key);
					nodeInfo.outs.Add(key);
					dictPort2Node.Add(key, fn);
				}
				#endregion
				sb.AppendLine();
				#region Declare nodes
				var dictOutPrms = outputParams.ToDictionary(s => charCase(s), s => true);
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
							sb.AppendFormat("\t<TR><TD BGCOLOR=\"aliceblue\"><b>{0}</b></TD></TR>", nodeName);
							//sb.AppendFormat("\t<TR><TD BGCOLOR=\"dimgray\"><FONT COLOR=\"white\"><b>{0}</b></FONT></TD></TR>", nodeName);
							bool first = true;
							foreach (var port in nodeInfo.inps)
							{
								if (first)
									first = false;
								else if (port.Contains(s_ID_) || port.EndsWith(s_ID))
									continue;
								string prm = showPrmNames ? "<b>" + port + "</b>" : null;
								string fmt = (dictPort2Node[port] == sFuncSrcData) ? " BGCOLOR=\"gold\"" : null;
								string cap;
								if (paramzCaptions != null && paramzCaptions.TryGetValue(port.ToUpperInvariant(), out cap))
									cap = " : " + cap;
								else cap = null;
								sb.AppendFormat("\t<TR><TD PORT=\"i{0}\" ALIGN=\"LEFT\"{1}>{2}{3}</TD></TR>\r\n", port, fmt, prm, cap);
							}
							foreach (var port in nodeInfo.outs)
							{
								string prm = showPrmNames ? "<b>" + port + "</b>" : null;
								string fmt = dictOutPrms.ContainsKey(port) ? " BGCOLOR=\"greenyellow\"" : null;
								string cap;
								if (paramzCaptions != null && paramzCaptions.TryGetValue(port.ToUpperInvariant(), out cap))
									cap = cap + " : ";
								else cap = null;
								sb.AppendFormat("\t<TR><TD PORT=\"o{0}\" ALIGN=\"RIGHT\"{1}>{2}{3}</TD></TR>\r\n", port, fmt, cap, prm);
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
					var fromNodes = new Dictionary<string, bool>();
					for (int i = 1; i < lst.Count; i++)
					{
						var port = lst.GetPort(i);
						if (i > 1)
							if (port.Contains(s_ID_) || port.EndsWith(s_ID))
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
