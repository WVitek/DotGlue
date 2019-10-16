using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using W.Common;
using W.Expressions;

namespace W.Rpt
{
	public static class ExprsTopoSort
	{
		class ItemInfo
		{
			public readonly Expr expr;
			public readonly Dictionary<string, bool> lets = new Dictionary<string, bool>();
			public readonly List<Expr> lets2 = new List<Expr>();
			public readonly Dictionary<string, bool> refs = new Dictionary<string, bool>();
			public readonly List<Expr> refs2 = new List<Expr>();

			public ItemInfo(Expr expr)
			{
				this.expr = expr;

				// lets
				// refs
				foreach (var _ in expr.Traverse<Expr>(e =>
				{
					// refs
					if (e.nodeType == ExprType.Reference)
					{
						refs[(e as ReferenceExpr).name] = true;
						return e;
					}
					if (e.nodeType != ExprType.Call)
						return null;
					var ce = (CallExpr)e;

					// lets || lets2
					if (ce.funcName != "GV" && ce.funcName != "GSV" || ce.args.Count < 1)
						return null;
					var a0 = ce.args[0];
					if (a0.nodeType == ExprType.Reference)
					{
						var name = (a0 as ReferenceExpr).name;
						refs[name] = true;
					}
					else
						refs2.Add(a0);
					return null;
				})) ;

				foreach (var _ in expr.Traverse<Expr>(e =>
				{
					if (e.nodeType != ExprType.Call)
						return null;
					var ce = (CallExpr)e;

					// lets || lets2
					if (ce.funcName != "let" || ce.args.Count != 2)
						return null;
					{
						var a0 = ce.args[0];
						if (a0.nodeType == ExprType.Reference)
						{
							var name = (a0 as ReferenceExpr).name;
							lets[name] = true;
							refs.Remove(name);
						}
						else
							lets2.Add(a0);
					}
					return null;
				})) ;
			}

#if DEBUG
			public override string ToString() { return expr.ToString(); }
#endif
		}

		static readonly object Fail = new object();

		static object TryGen(Generator.Ctx ctx, Expr expr)
		{
			try { return Generator.Generate(expr, ctx); }
			catch { return Fail; }
		}

		/// <summary>
		/// Iterative topological sorting of expressions (ability of calculated values names is taked into account)
		/// </summary>
		/// <param name="srcItems"></param>
		/// <param name="parentCtx"></param>
		/// <returns></returns>
		public static IEnumerable<Expr> DoSort(IEnumerable<Expr> srcItems, Generator.Ctx parentCtx)
		{
			List<ItemInfo> infos = srcItems.Select(item => new ItemInfo(item)).ToList();

			while (true)
			{
				//****** sort items with defined names
				var allLets = new Dictionary<string, ItemInfo>();

				foreach (var nfo in infos)
					foreach (var name in nfo.lets.Keys)
						allLets[name] = nfo;

				infos = TopoSort.DoSort(infos, ii => ii.refs.Keys.Where(s => allLets.ContainsKey(s)).Select(s => allLets[s]));

				var thereIsNameResolve = false;
				var allNamesRosolved = true;
				var ctx = new Generator.Ctx(parentCtx);

				//****** calculate unresolved names
				for (int i = 0; i < infos.Count; i++)
				{
					var nfo = infos[i];
                    // side effect?: new values can be declared in context
					var value = TryGen(ctx, nfo.expr);
					var lets2 = nfo.lets2;
					for (int j = 0; j < lets2.Count;)
					{
						var let2 = lets2[j];
						var letName = TryGen(ctx, let2);
						if (letName != Fail && OPs.KindOf(letName) == ValueKind.Const)
						{
							thereIsNameResolve = true;
							lets2.RemoveAt(j);
							nfo.lets[Convert.ToString(letName)] = true;
						}
						else { j++; allNamesRosolved = false; }
					}
					var refs2 = nfo.refs2;
					for (int j = 0; j < refs2.Count;)
					{
						var ref2 = refs2[j];
						var refName = TryGen(ctx, ref2);
						if (refName != Fail && OPs.KindOf(refName) == ValueKind.Const)
						{
							thereIsNameResolve = true;
							refs2.RemoveAt(j);
							nfo.refs[Convert.ToString(refName)] = true;
						}
						else { j++; allNamesRosolved = false; }
					}
					if (nfo.refs.Keys.Any(s => ctx.IndexOf(s) < 0))
						allNamesRosolved = false;
				}

				if (allNamesRosolved || !thereIsNameResolve)
					break;
			}
			return infos.Select(ii => ii.expr);
		}

		class ModifyHelper
		{
			public Generator.Ctx ctx;
			public bool thereIsNameResolve;
			public bool thereIsNonresolved;
			public List<string> lets = new List<string>();
			public readonly Func<Expr, Expr> modify;
			public ModifyHelper()
			{
				modify = Expr.RecursiveModifier(e =>
				{
					if (e.nodeType == ExprType.Reference || e.nodeType == ExprType.Constant)
						return e;
					if (e.nodeType != ExprType.Call)
						goto tryConst;
					var ce = (CallExpr)e;
					bool isLet = ce.funcName == "let";
					bool isDef = isLet || ce.funcName == "SV" || ce.funcName == "SSV";
					if (!isDef && ce.funcName != "GV" && ce.funcName != "GSV")
						goto tryConst;
					var arg0 = ce.args[0];
					string name;
					if (arg0.nodeType == ExprType.Constant)
					{
						name = Convert.ToString((arg0 as ConstExpr).value);
						if (!isLet && ce.args.Count == 1)
							e = new ReferenceExpr(name);
						goto constResolved;
					}
					if (arg0.nodeType == ExprType.Reference)
					{
						name = (arg0 as ReferenceExpr).name;
						goto nameResolved;
					}
					// try to resolve name
					var nameObj = TryGen(ctx, arg0);
					if (nameObj == Fail || OPs.KindOf(nameObj) != ValueKind.Const)
					{
						thereIsNonresolved = true;
						return e;
					}
					name = Convert.ToString(nameObj);
					thereIsNameResolve = true;
				constResolved:
					var args = new Expr[ce.args.Count];
					if (isLet)
						args[0] = new ReferenceExpr(name);
					else
					{
						if (ce.args.Count == 1 && ce.funcName != "GSV")
							return new ReferenceExpr(name);
						args[0] = new ConstExpr(name);
					}
					for (int i = 1; i < args.Length; i++)
						args[i] = modify(ce.args[i]);
					e = new CallExpr(ce.funcName, args);
				nameResolved:
					if (isDef && ce.args.Count == 2)
					{
						lets.Add(name);
						//var value = modify(ce.args[1]);
						//if (value.nodeType == ExprType.Constant)
						//{ }
					}
					return e;
				tryConst:
					var val = TryGen(ctx, e);
					if (val == Fail || OPs.KindOf(val) != ValueKind.Const)
						return e;
					return new ConstExpr(val);
				});
			}
		}

		public static IEnumerable<string> EnumRefs(Expr expr)
		{
			return Expr.Traverse(expr, e =>
			{
				if (e.nodeType == ExprType.Reference)
					return (e as ReferenceExpr).name;
				if (e.nodeType != ExprType.Call)
					return null;
				var ce = (CallExpr)e;
				if (ce.funcName != "GV" && ce.funcName != "GSV")
					return null;
				if (ce.args.Count < 1)
					return null;
				var a0 = ce.args[0];
				if (a0.nodeType != ExprType.Constant)
					return null;
				return Convert.ToString((a0 as ConstExpr).value);
			}
			);
		}

		/// <summary>
		/// Iterative topological sorting of expressions (ability of calculated values names is taked into account)
		/// </summary>
		/// <param name="srcItems"></param>
		/// <param name="parentCtx"></param>
		/// <returns></returns>
		public static IEnumerable<Expr> DoSort2(IEnumerable<Expr> srcItems, Generator.Ctx parentCtx)
		{
			var items = new List<Expr>(srcItems);

			var mh = new ModifyHelper();
			var allLets = new Dictionary<string, Expr>();

			while (true)
			{
				mh.ctx = new Generator.Ctx(parentCtx);
				mh.ctx.CreateValue(FuncDefs_Core.stateTryGetConst, string.Empty);

				for (int i = 0; i < items.Count; i++)
				{
					var item = items[i];
					item = mh.modify(item);
					items[i] = item;
					foreach (var letName in mh.lets)
					{
						allLets[letName] = item;
						//mh.ctx.CreateValue(letName);
					}
					mh.lets.Clear();
				}

				{
					var values = mh.ctx.values;
					var lets = new List<Expr>();
					foreach (var p in mh.ctx.name2ndx)
					{
						if (p.Value == 0)
							continue;
						var v = values[p.Value];
						if (v == null || allLets.ContainsKey(p.Key) || OPs.KindOf(v) != ValueKind.Const)
							continue;
						var le = CallExpr.let(new ReferenceExpr(p.Key), new ConstExpr(v));
						allLets.Add(p.Key, le);
						lets.Add(le);
					}
					if (lets.Count > 0)
						items.InsertRange(0, lets);
				}

				if (!mh.thereIsNameResolve && !mh.thereIsNonresolved)
					break;

				mh.thereIsNonresolved = mh.thereIsNameResolve = false;

				items = TopoSort.DoSort(items,
					e => EnumRefs(e)
					.Where(s => s != null && allLets.ContainsKey(s))
					.Select(s => allLets[s])
				);

				allLets.Clear();
			}

			return items;
		}
	}
}
