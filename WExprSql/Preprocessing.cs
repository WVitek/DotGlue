using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using W.Common;

namespace W.Expressions.Sql
{
    public static partial class Preprocessing
    {
        /// <summary>
        /// SQL file processing context
        /// </summary>
        internal class LoadingSqlFuncsContext
        {
            public string sqlFileName;
            public string dbConnValueName;
            public QueryKind forKinds;
            public TimeSpan cachingExpiration;
            public string cacheSubdomain;
            public string defaultLocationForValueInfo;
            public Generator.Ctx ctx;

            struct FieldsInfo
            {
                public IList<Expr> fields;
                public IDictionary<string, object> attrs;
            }

            Dictionary<string, FieldsInfo> abstracts = new Dictionary<string, FieldsInfo>();
            Dictionary<string, FieldsInfo> CL_templs = new Dictionary<string, FieldsInfo>();
            public Dictionary<string, FuncDef> extraFuncs = new Dictionary<string, FuncDef>();

            Func<Expr, Expr> ModifyFieldExpr(string substance)
            {
                Func<string, string> subst = s =>
                {
                    switch (s)
                    {
                        case nameof(START_TIME):
                        case nameof(END_TIME):
                        case nameof(END_TIME__DT):
                        case nameof(INS_OUTS_SEPARATOR):
                            return null;
                    }
                    int i = s.IndexOf('_');

                    string desc, quan;
                    if (i < 0)
                    {
                        desc = substance + '_' + s;
                        quan = s;
                    }
                    else
                    {
                        desc = substance + s;
                        int j = s.IndexOf('_', i + 1);
                        quan = (j < 0) ? s.Substring(i + 1) : s.Substring(i + 1, j - i - 1);
                    }
                    return desc;
                };

                return arg =>
                {
                    string p = null;
                    switch (arg.nodeType)
                    {
                        case ExprType.Alias:
                            var ae = (AliasExpr)arg;
                            if ((p = subst(ae.alias)) == null)
                                return arg;
                            return new AliasExpr(ae.expr, new ReferenceExpr(p));
                        case ExprType.Sequence:
                            var args = ((SequenceExpr)arg).args;
                            int n = args.Count;
                            if ((p = subst(args[n - 1].ToString())) == null)
                                return arg;
                            return new SequenceExpr(args.Take(n - 1).Concat(new[] { new ReferenceExpr(p) }).ToList());
                        case ExprType.Reference:
                            var re = (ReferenceExpr)arg;
                            if ((p = subst(re.name)) == null)
                                return arg;
                            return new AliasExpr(re, new ReferenceExpr(p));
                        default:
                            return arg;
                    }
                };
            }

            IEnumerable<FuncDef> SqlFuncDefAction(string funcNamePrefix, int actualityInDays, string queryText, bool arrayResults, IDictionary<string, object> xtraAttrs)
            {
                var c = new SqlFuncDefinitionContext()
                {
                    ldr = this,
                    funcNamesPrefix = funcNamePrefix,
                    actualityInDays = actualityInDays,
                    queryText = queryText,
                    arrayResults = arrayResults,
                    xtraAttrs = xtraAttrs,
                };
                return Impl.DefineLoaderFuncs(c);
            }

            public IEnumerable<FuncDef> LoadingFuncs()
            {
                foreach (var fdEnum in Impl.ParseSqlFuncs(sqlFileName, SqlFuncDefAction, ctx))
                    foreach (var fd in fdEnum)
                        yield return fd;
            }

            static class AbstractTable { }
            static class Substance { }
            static class Inherits { }
            static class LookupTableTemplate { }

            internal Expr PostProcSelect(SqlFuncDefinitionContext c, SqlSectionExpr sqlSection)
            {
                var modFunc = c.xtraAttrs.TryGetValue(nameof(Substance), out var objSubstance)
                    ? ModifyFieldExpr(objSubstance.ToString())
                    : x => x;

                #region Postprocess SELECT expression: insert inherited fields if needed
                if (c.xtraAttrs.TryGetValue(nameof(Attr.innerAttrs), out var objInnerAttrs))
                {
                    var innerAttrs = (Dictionary<string, object>[])objInnerAttrs;
                    var args = sqlSection.args;

                    bool changed = false;
                    int n = innerAttrs.Length;
                    var newInner = new List<Dictionary<string, object>>(n);
                    var fields = new List<Expr>(n);

                    for (int i = 0; i < n; i++)
                    {
                        var attrs = innerAttrs[i];
                        if (attrs != null && attrs.TryGetValue(nameof(Inherits), out var objInherits))
                        {   // inherit lot of fields from abstract tables
                            var lst = objInherits as IList;
                            if (lst == null)
                                lst = new object[] { objInherits };
                            foreach (var aT in lst)
                            {
                                if (!abstracts.TryGetValue(aT.ToString(), out var abstr))
                                    throw new Generator.Exception($"No one AbstractTable='{aT}' found");

                                var inheritedFields = abstr.fields;
                                // inherit fields
                                changed = true;
                                fields.AddRange(inheritedFields.Select(modFunc));
                                if (abstr.attrs.TryGetValue(nameof(Attr.innerAttrs), out var inners))
                                    // inherit fields attributes
                                    newInner.AddRange((Dictionary<string, object>[])inners);
                                else
                                    // no attributes to inherit
                                    for (int j = inheritedFields.Count - 1; j >= 0; j--)
                                        newInner.Add(null);
                            }

                        }
                        if (i < args.Count)
                            fields.Add(modFunc(args[i]));
                        newInner.Add(attrs);
                    }
                    if (changed)
                    {
                        // inherited fields added, create updated SELECT expression
                        sqlSection = new SqlSectionExpr(SqlSectionExpr.Kind.Select, fields);
                        c.xtraAttrs[nameof(Attr.innerAttrs)] = newInner.ToArray();
                    }
                }
                else if (objSubstance != null)
                    sqlSection = new SqlSectionExpr(SqlSectionExpr.Kind.Select, sqlSection.args.Select(modFunc).ToList());
                #endregion


                if (c.xtraAttrs.TryGetValue(nameof(AbstractTable), out var objAbstractTable))
                {   // It is "abstract table", add to abstracts dictionary
                    var abstractTable = objAbstractTable.ToString();
                    abstracts.Add(abstractTable, new FieldsInfo() { fields = sqlSection.args, attrs = c.xtraAttrs });
                    return null;
                }

                if (c.xtraAttrs.TryGetValue(nameof(LookupTableTemplate), out var objLookupTableTemplate))
                {
                    var ltt = objLookupTableTemplate.ToString();
                    CL_templs.Add(ltt, new FieldsInfo() { fields = sqlSection.args, attrs = c.xtraAttrs });
                    return null;
                }

                return sqlSection;
            }
        }

        /// <summary>
        /// SQL query to functions converter context 
        /// </summary>
        internal class SqlFuncDefinitionContext
        {
            public LoadingSqlFuncsContext ldr;

            public string funcNamesPrefix;
            public double actualityInDays;
            public string queryText;
            public bool arrayResults;
            public IDictionary<string, object> xtraAttrs;

            public Expr PostProc(Expr e)
            {
                var sqlSection = (e.nodeType == ExprType.Call) ? e as SqlSectionExpr : null;
                if (sqlSection != null && sqlSection.kind == SqlSectionExpr.Kind.Select)
                {
                    e = ldr.PostProcSelect(this, sqlSection);
                    if (e == null)
                        return null;
                }

                if (e.nodeType != ExprType.Alias)
                    return e;
                var r = ((AliasExpr)e).right as ReferenceExpr;
                if (r == null)
                    return e;
                var d = r.name.ToUpperInvariant();
                switch (d)
                {
                    case nameof(START_TIME):
                    case nameof(END_TIME):
                    case nameof(END_TIME__DT):
                    case nameof(INS_OUTS_SEPARATOR):
                        // skip special fields
                        return e;
                }
                var vi = ValueInfo.Create(d, true, ldr.defaultLocationForValueInfo);
                if (vi == null) return e;
                var v = vi.ToString();
                if (v == d)
                    return e;
                return new ReferenceExpr(v);
            }
        }


    }

}