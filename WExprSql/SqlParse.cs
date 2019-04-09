using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace W.Expressions
{
	using W.Expressions;

	public static class SqlParse
	{
		public static readonly string[] reservedWords = new string[] { }; //"AND", "OR", "NOT", "IN" };

		public static SqlExpr Do(string txt)
		{
			int maxNdx = txt.Length;
			var lst = new List<Expr>();
			var p = new Parser(reservedWords, StringComparison.InvariantCultureIgnoreCase) { AllowSequences = true, txt = txt, DoubleQuotedAsIdentifer = true };
			while (p.ndx < maxNdx)
			{
				var pndx = p.ndx;
				//var expr = Parser.ParseToExpr(txt, ref ndx, reservedWords, StringComparison.InvariantCultureIgnoreCase);
				var expr = p.ParseToExpr();
				if (p.stack.Count > 0)
					throw new Generator.Exception("SqlParse.Do error: p.stack is not empty");
				if (p.ndx == pndx)
					p.ndx++;
				else lst.Add(expr);
			}
			return new SqlExpr(RestructAsSqlSelect(lst));
		}

		static void moveArgsToSection(string sectionName, List<Expr> sectionItems, List<Expr> argItems)
		{
			if (argItems.Count == 1)
				sectionItems.Add(argItems[0]);
			else if (argItems.Count > 0)
			{
				if (sectionName == "SELECT")
				{
					var item0 = argItems[0] as SequenceExpr;
					if (item0 != null)
					{
						var first = item0.args[0] as ReferenceExpr;
						if (first != null && first.name == "SELECT")
						{   // add brackets to subqueries: "(SELECT ...)"
							var newArg0 = Expr.RecursiveModifier(e => (e == item0) ? CallExpr.Eval(item0) : e)
								(argItems[0]);
							argItems[0] = newArg0;
						}
					}
				}
				sectionItems.Add(new SequenceExpr(argItems.ToArray()));
			}
			argItems.Clear();
		}

		static IList<Expr> RestructAsSqlSelect(IList<Expr> lst)
		{
			var seq = lst[0] as SequenceExpr;
			if (seq == null)
				return lst;
			var ref0 = (seq.args.Count > 0) ? seq.args[0] as ReferenceExpr : null;
			if (ref0 == null || string.Compare(ref0.name, "SELECT", StringComparison.InvariantCultureIgnoreCase) != 0)
				return lst;
			string sectionName = null;
			string sectionPartialName = string.Empty;
			var res = new List<Expr>(lst.Count);
			var sectionItems = new List<Expr>(lst.Count);
			foreach (var item in lst)
			{
				var se = item as SequenceExpr;
				if (se == null)
					se = new SequenceExpr(item);
				var argItems = new List<Expr>();
				foreach (var exprArg in se.args)
				{
					var expr = exprArg;
					var r = expr as ReferenceExpr;
					CallExpr ce = null;
					if (r == null)
					{
						ce = expr as CallExpr;
						if (ce != null && ce.funcName.Length > 0)
							r = new ReferenceExpr(ce.funcName);
					}
					if (r != null)
					{
						var s = r.name.ToUpperInvariant();
						switch (s)
						{
							case "SELECT":
							case "FROM":
							case "WHERE":
							case "BY":
							case "USING":
								if (argItems.Count > 0)
									moveArgsToSection(sectionName, sectionItems, argItems);
								if (sectionName != null)
									res.Add(new SqlSectionExpr(sectionName, sectionItems.ToArray()).Postprocess());
								else if (sectionItems.Count > 0)
									res.AddRange(sectionItems);
								sectionItems.Clear();
								if (s == "BY")
								{
									sectionName = sectionPartialName + ' ' + s;
									sectionPartialName = string.Empty;
								}
								else
								{
									System.Diagnostics.Trace.Assert(sectionPartialName.Length == 0, "Unknown SQL clause");
									sectionName = s;
								}
								break;
							case "JOIN":
								{
									System.Diagnostics.Trace.Assert(argItems.Count > 0, "No expressions before JOIN");
									var ae = AliasExpr.AsAlias(argItems);
									if (ae != null)
									{
										argItems.Clear();
										argItems.Add(ae);
									}
									if (sectionPartialName.Length > 0)
										argItems.Add(new ReferenceExpr(sectionPartialName));
									argItems.Add(new ReferenceExpr(s));
									if (ae == null)
									{
										var tmp = new SequenceExpr(argItems.ToArray());
										argItems.Clear();
										argItems.Add(tmp);
									}
									sectionPartialName = string.Empty;
								}
								break;
							case "ON":
								{
									int i = argItems.Count - 1;
									var subseq = new List<Expr>();
									while (i >= 0)
									{
										var exp = argItems[i];
										var re = exp as ReferenceExpr;
										if (re != null && re.name == "JOIN")
											break;
										argItems.RemoveAt(i);
										i--;
										subseq.Insert(0, exp);
									}
									if (subseq.Count > 0)
										argItems.Add((Expr)AliasExpr.AsAlias(subseq) ?? new SequenceExpr(subseq));
									argItems.Add(new ReferenceExpr(s));
								}
								break;
							case "ORDER":
							case "GROUP":
							case "INNER":
							case "OUTER":
							case "LEFT":
							case "RIGHT":
							case "CROSS":
							case "FULL":
								if (sectionPartialName.Length == 0)
									sectionPartialName = s;
								else sectionPartialName += ' ' + s;
								break;
							default:
								if (ce == null)
									argItems.Add(r);
								r = null;
								break;
						}
						if (ce == null)
							continue;
						if (r != null)
						{
							expr = new CallExpr(string.Empty, ce.args);
							ce = null;
						}
					}
					if (ce != null && ce.funcName == string.Empty)
					{
						var items = RestructAsSqlSelect(ce.args);
						if (items != ce.args)
							argItems.Add(CallExpr.Eval(new SequenceExpr(items)));
					}
					else argItems.Add(expr);
				}
				moveArgsToSection(sectionName, sectionItems, argItems);
			}
			if (sectionName != null)
				res.Add(new SqlSectionExpr(sectionName, sectionItems.ToArray()).Postprocess());
			else if (sectionItems.Count > 0)
				res.AddRange(sectionItems);
			return res.ToArray();
		}
	}

	public class SqlSectionExpr : MultiExpr
	{
		public enum Kind { Select = 0, From = 1, Where = 2, OrderBy = 3, GroupBy = 4, _count = 5 }
		public static readonly string[] Names = new string[(int)Kind._count] { "SELECT", "FROM", "WHERE", "ORDER BY", "GROUP BY" };

		public readonly Kind kind;
		public string sectionName { get { return Names[(int)kind]; } }

		public SqlSectionExpr(string sectionName, IList<Expr> args)
			: base(ExprType.Call, args)
		{
			int i = Array.IndexOf<string>(Names, sectionName);
			System.Diagnostics.Trace.Assert(i >= 0, "Unknown SQL clause named '" + sectionName + '\'');
			kind = (Kind)i;
		}

		public SqlSectionExpr(Kind kind, IList<Expr> args)
			: base(ExprType.Call, args)
		{ this.kind = kind; }

		public SqlSectionExpr(Kind kind, params Expr[] args)
			: base(ExprType.Call, args)
		{ this.kind = kind; }

		public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
		{
			var firstPos = sb.Length;
#if CHECK_DATA_DUPS
			var prefix = (kind == Kind.Select) ? sectionName + " DISTINCT" : sectionName;
#else
			var prefix = sectionName;
#endif
			sb.Append(prefix); column += prefix.Length;
			sb.Append(' '); column++;
			var multiline = bldStr(sb, args, ", ", firstPos, nestingLevel, ref column);
			if (multiline)
				NewLine(sb, nestingLevel, ref column);
			//sb.Append(')'); column++;
			return multiline;
		}

		public SqlSectionExpr Postprocess()
		{
			switch (sectionName)
			{
				case "SELECT":
				case "FROM":
					var lst = new Expr[args.Count];
					for (int i = 0; i < args.Count; i++)
					{
						var expr = args[i];
						var seq = expr as SequenceExpr;
						if (seq != null && seq.args.Count > 1)
							lst[i] = AliasExpr.AsAlias(seq.args) ?? expr;
						else lst[i] = expr;
					}
					return new SqlSectionExpr(sectionName, lst);
				default:
					return this;
			}
		}
		public override Expr Visit(Func<Expr, Expr> visitor)
		{
			var lst = Visit(args, visitor);
			if (lst == args)
				return this;
			else return new SqlSectionExpr(sectionName, args);
		}
	}

	public class AliasExpr : BinaryExpr
	{
		public Expr expr { get { return left; } }
		public string alias { get { return right.ToString(); } }
		public AliasExpr(Expr expr, Expr alias) : base(ExprType.Alias, expr, alias) { }
		public override Expr Visit(Func<Expr, Expr> visitor)
		{
			var l = visitor(left);
			var r = visitor(right);
			if (l == left && r == right)
				return this;
			else return new AliasExpr(left, right);
		}

		public static AliasExpr AsAlias(IList<Expr> seq)
		{
			int n = seq.Count;
			var last = seq[n - 1];
			if (last is ReferenceExpr || last is ConstExpr)
			{
				int q;
				if (n > 2)
				{
					var r = seq[n - 2] as ReferenceExpr;
					q = (r != null && r.name.ToUpperInvariant() == "AS") ? n - 2 : n - 1;
				}
				else q = n - 1;
				if (q > 1)
				{
					var tmp = new Expr[q];
					for (int j = 0; j < q; j++) tmp[j] = seq[j];
					return new AliasExpr(new SequenceExpr(tmp), seq[n - 1]);
				}
				else  // q==1
					return new AliasExpr(seq[0], seq[n - 1]);
			}
			else return null;
		}
	}

	public class SqlExpr : SequenceExpr
	{
		//public enum ResultKind { begTime, endTime, valueTime };

		SqlSectionExpr[] sql = new SqlSectionExpr[(int)SqlSectionExpr.Kind._count];
		public SqlSectionExpr this[SqlSectionExpr.Kind kind] { get { return sql[(int)kind]; } private set { sql[(int)kind] = value; } }
		/// <summary>
		/// Dictionary for "result column name" to "result index" conversion
		/// </summary>
		public readonly IDictionary<string, int> resNdx;
		/// <summary>
		/// Result expressions in order of appearance in SELECT clause
		/// </summary>
		public readonly AliasExpr[] results;
		/// <summary>
		/// fields used in result expressions // only one (last) "table.field" for each result
		/// </summary>
		public readonly Expr[] resFields;
		/// <summary>
		/// tables enumerated in FROM section
		/// </summary>
		public readonly AliasExpr[] sources;
		//public readonly uint[] resultUsesSrcs;
		public readonly Expr condition;

		public SqlExpr(IList<Expr> sections)
			: base(sections)
		{
			foreach (SqlSectionExpr s in sections)
				sql[(int)s.kind] = s;
			SqlSectionExpr section;
			// FROM: scan sources
			section = this[SqlSectionExpr.Kind.From];
			IDictionary<string, int> srcAlias2Ndx;
			{
				var items = section.args;
				var srcs = new List<AliasExpr>(items.Count);
				srcAlias2Ndx = new Dictionary<string, int>(items.Count);
				int i = 0;
				foreach (var item in items)
				{
					var ae = item as AliasExpr;
					if (ae != null)
					{
						srcAlias2Ndx[ae.alias] = i++;
						srcs.Add(ae);
						continue;
					}
					var sq = item as SequenceExpr;
					if (sq != null)
						foreach (var it in sq.args)
						{
							var tmp = it as AliasExpr;
							if (tmp != null)
							{
								srcAlias2Ndx[tmp.alias] = i++;
								srcs.Add(tmp);
							}
						}
					else
					{
						var tmp = new AliasExpr(item, item);
						srcAlias2Ndx[tmp.alias] = i++;
						srcs.Add(tmp);
					}
				}
				System.Diagnostics.Trace.Assert(i <= 32, "No more than 32 sources in FROM supported");
				sources = srcs.ToArray();
			}
			// SELECT: scan results
			section = this[SqlSectionExpr.Kind.Select];
			{
				var items = section.args;
				int n = items.Count;
				System.Diagnostics.Trace.Assert(n <= 64, "No more than 64 result columns in SELECT supported");
				results = new AliasExpr[n];
				resFields = new Expr[n];
				resNdx = new Dictionary<string, int>(n);
				//resultUsesSrcs = new uint[n];
				for (int i = 0; i < n; i++)
				{
					var aliasExpr = items[i] as AliasExpr;
					if (aliasExpr != null)
					{   // item in form "expression alias"
						Expr resField = null;
						foreach (var srcAlias in aliasExpr.expr.Traverse<BinaryExpr>(e =>
							{
								var be = e as BinaryExpr;
								if (be != null && be.nodeType == ExprType.Fluent)
									return be;
								else return null;
							}))
						{
							if (srcAlias == null)
								continue;
							int j;
							if (srcAlias2Ndx.TryGetValue(srcAlias.left.ToString(), out j)) // source alias may be to the left of point
							{
								//resultUsesSrcs[i] |= (uint)(1 << j);
								resField = srcAlias;
							}
						}
						//System.Diagnostics.Trace.Assert(resField != null, "No field reference found");
						resFields[i] = resField ?? aliasExpr.expr;
						results[i] = aliasExpr;
						resNdx.Add(aliasExpr.alias.ToUpperInvariant(), i);
						continue;
					}
					var binExpr = items[i] as BinaryExpr;
					if (binExpr != null)
					{
						if (binExpr.nodeType == ExprType.Fluent)
						{   // item in form "tablename.fieldname"
							resFields[i] = binExpr;
							results[i] = new AliasExpr(binExpr, binExpr.right);
							resNdx.Add(binExpr.right.ToString().ToUpperInvariant(), i);
							continue;
						}
					}
					else
					{
						var tmp = items[i] as ReferenceExpr;
						if (tmp != null)
						{   // item in form "fieldname"
							resFields[i] = tmp;
							results[i] = new AliasExpr(tmp, tmp);
							resNdx.Add(tmp.ToString().ToUpperInvariant(), i);
							continue;
						}
					}
					throw new ArgumentException("SELECTed expression must be a simple field reference or have alias", items[i].ToString());
				}
			}
			// WHERE: scan conditions
			section = this[SqlSectionExpr.Kind.Where];
			if (section != null)
			{
				var items = section.args;
				System.Diagnostics.Trace.Assert(items.Count == 1, "Only one condition expression in WHERE supported");
				condition = items[0];
			}
		}

		static AliasExpr AsIs(AliasExpr ae) { return ae; }

		public Dictionary<string, bool> GetPresenseDict(IEnumerable<Expr> exprs)
		{
			var dict = new Dictionary<string, bool>();
			foreach (var e in exprs)
			{
				var s = e.ToString().ToUpperInvariant();
				dict[s] = true;
				int i;
				if (resNdx.TryGetValue(s, out i))
					dict[results[i].expr.ToString().ToUpperInvariant()] = true;
			}
			return dict;
		}

		public bool IsGroupedBy(Dictionary<string, bool> dictGroupBy, AliasExpr r)
		{
			if (dictGroupBy == null)
				return false;
			else return dictGroupBy.ContainsKey(r.alias.ToUpperInvariant()) || dictGroupBy.ContainsKey(r.expr.ToString().ToUpperInvariant());
		}

		public Expr CreateQueryExpr(Expr andCondition = null, AliasExpr groupBy = null, Expr[] orderBy = null,
			Func<AliasExpr, AliasExpr> resultModifier = null, params string[] resultsNames)
		{
			var sections = new List<Expr>(args.Count);
			// select
			Expr[] toSelect;
			var gba = (groupBy == null) ? null : groupBy.alias;
			// "group by" preprocessing
			Expr newGroupBySection;
			Dictionary<string, bool> presentInGroupBy = null;
			{
				var currGroupBy = this[SqlSectionExpr.Kind.GroupBy];
				if (currGroupBy != null)
				{
					newGroupBySection = currGroupBy;
					presentInGroupBy = GetPresenseDict(currGroupBy.args);
				}
				else if (groupBy != null)
				{
					newGroupBySection = new SqlSectionExpr(SqlSectionExpr.Kind.GroupBy, groupBy.expr);
					presentInGroupBy = GetPresenseDict(new Expr[] { groupBy });
				}
				else newGroupBySection = null;
			}
			// "select"
			bool resultsModified = false;
			if (resultsNames.Length == 0)
			{
				if (resultModifier != null)
				{
					toSelect = new Expr[results.Length];
					for (int i = toSelect.Length - 1; i >= 0; i--)
					{
						var r = results[i];
						var s = IsGroupedBy(presentInGroupBy, r) ? r : resultModifier(r);
						if (r != s)
							resultsModified = true;
						toSelect[i] = s;
					}
				}
				else toSelect = results;
			}
			else
			{
				resultModifier = resultModifier ?? AsIs;
				toSelect = new Expr[resultsNames.Length];
				resultsModified = true;
				for (int i = toSelect.Length - 1; i >= 0; i--)
				{
					var r = results[resNdx[resultsNames[i]]];
					toSelect[i] = IsGroupedBy(presentInGroupBy, r) ? r : resultModifier(r);
				}
			}
			if (resultsModified)
				sections.Add(new SqlSectionExpr(SqlSectionExpr.Kind.Select, toSelect));
			else sections.Add(this[SqlSectionExpr.Kind.Select]);
			// from
			sections.Add(this[SqlSectionExpr.Kind.From]);
			// where
			if (andCondition != null)
				sections.Add(new SqlSectionExpr(SqlSectionExpr.Kind.Where, (condition == null) ? andCondition : new BinaryExpr(ExprType.LogicalAnd, condition, andCondition)));
			else if (condition != null)
				sections.Add(this[SqlSectionExpr.Kind.Where]);
			// group by
			if (newGroupBySection != null)
				sections.Add(newGroupBySection);
			// order by
			var thisOrderBy = this[SqlSectionExpr.Kind.OrderBy];
			if (orderBy != null && orderBy.Length > 0)
			{
				if (thisOrderBy != null)
				{
					var fromQuery = thisOrderBy.args.Where(e =>
					{
						var s = e.ToString().ToUpperInvariant();
						return presentInGroupBy == null || presentInGroupBy.ContainsKey(s);
					}).ToArray();
					var presentInOrderBy = GetPresenseDict(fromQuery);

					orderBy = fromQuery.Concat(orderBy.Where(e =>
					{
						var s = e.ToString().ToUpperInvariant();
						return !presentInOrderBy.ContainsKey(s);
					})).ToArray();
				}
				sections.Add(new SqlSectionExpr(SqlSectionExpr.Kind.OrderBy, orderBy));
			}
			else if (thisOrderBy != null)
				sections.Add(this[SqlSectionExpr.Kind.OrderBy]);
			//for (int i = 3; i < sql.Length; i++)
			//	if (sql[i] != null)
			//		sections.Add(sql[i]);
			return new SequenceExpr(sections); // todo
		}
	}
}
