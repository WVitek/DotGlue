using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using W.Common;

namespace W.Expressions.Sql
{
    internal static partial class Preprocessing
    {
        internal struct SqlInfo
        {
            public SqlExpr sql;
            public IDictionary<Attr.Tbl, object> attrs;
        }

        internal static SqlFuncPreprocessingCtx NewCodeLookupDict(SqlFuncPreprocessingCtx src, SqlInfo tmpl, string codeFieldName)
        {
            var select = tmpl.sql[SqlSectionExpr.Kind.Select];

            var innerAttrs = new List<Dictionary<Attr.Col, object>>(select.args.Count + 1);

            var sb = new StringBuilder();

            foreach (var kind in SqlSectionExpr.EnumKinds())
            {
                var section = tmpl.sql[kind];

                switch (kind)
                {
                    case SqlSectionExpr.Kind.Select:
                        {
                            sb.AppendLine("SELECT");

                            {   // first field
                                var firstFieldAttrs = new Dictionary<Attr.Col, object>();
                                firstFieldAttrs.Add(Attr.Col.Description, "Dummy key field used for grouping");
                                innerAttrs.Add(firstFieldAttrs);
                                sb.Append($"\t0  x{codeFieldName.GetHashCode():X}_ID_TMP");
                            }

                            var substance = codeFieldName.Substring(0, codeFieldName.IndexOf('_'));
                            var modFunc = src.ldr.ModifyFieldExpr(src, substance);

                            var tmplInnerAttrs = (Dictionary<Attr.Col, object>[])tmpl.attrs[Attr.Tbl._columns_attrs];

                            for (int i = 0; i < select.args.Count; i++)
                            {
                                sb.AppendLine(",");
                                object v;
                                if (Attr.GetBool(tmplInnerAttrs[i], Attr.Col.FixedAlias))
                                    v = select.args[i];
                                else
                                    v = modFunc(select.args[i]);
                                sb.Append($"\t{v}");
                                innerAttrs.Add(tmplInnerAttrs[i]);
                            }
                            sb.AppendLine();
                        }
                        break;
                    case SqlSectionExpr.Kind.From:
                        if (section != null)
                            sb.AppendLine(section.ToString());
                        else
                            sb.AppendLine($"FROM {codeFieldName}");
                        break;
                    default:
                        if (section != null)
                            sb.AppendLine(section.ToString());
                        break;
                }
            }


            var tblAttrs = new Dictionary<Attr.Tbl, object>(tmpl.attrs);

            var c = new SqlFuncPreprocessingCtx()
            {
                ldr = src.ldr,
                actualityInDays = 36525 * 10,
                arrayResults = true,
                funcNamesPrefix = codeFieldName + "_DictData",
                tblAttrs = tblAttrs,
                queryText = sb.ToString(),
            };

            tblAttrs.Remove(Attr.Tbl.LookupTableTemplate);

            string tmplDescr;
            if (tblAttrs.TryGetValue(Attr.Tbl.TemplateDescription, out var objTmplDescr))
                tmplDescr = string.Format(Convert.ToString(objTmplDescr), codeFieldName);
            else
                tmplDescr = $"Instantiated lookup table for '{codeFieldName}' field";

            tblAttrs.Remove(Attr.Tbl.TemplateDescription);
            tblAttrs[Attr.Tbl.FuncPrefix] = c.funcNamesPrefix;
            Attr.Add(tblAttrs, Attr.Tbl.Description, tmplDescr, true);
            tblAttrs[Attr.Tbl.ActualityDays] = c.actualityInDays;
            tblAttrs[Attr.Tbl.ArrayResults] = true;
            tblAttrs[Attr.Tbl._columns_attrs] = innerAttrs;

            return c;
        }

        /// <summary>
        /// SQL file processing context
        /// </summary>
        internal class PreprocessingContext
        {
            public string sqlFileName;
            public string dbConnValueName;
            public QueryKind forKinds;
            public TimeSpan cachingExpiration;
            public string cacheSubdomain;
            public string defaultLocationForValueInfo;
            public Generator.Ctx ctx;

            readonly Dictionary<string, SqlInfo> abstracts = new Dictionary<string, SqlInfo>();
            readonly Dictionary<string, SqlInfo> CL_templs = new Dictionary<string, SqlInfo>();

            readonly Dictionary<string, SqlFuncPreprocessingCtx> extraFuncs = new Dictionary<string, SqlFuncPreprocessingCtx>();
            readonly List<SqlFuncPreprocessingCtx> lstAddedExtraFuncs = new List<SqlFuncPreprocessingCtx>();

            void AddExtraFunc(string name, Func<SqlFuncPreprocessingCtx> funcGetter)
            {
                // mark name as already added to avoid possible infinite recursion
                extraFuncs.Add(name, null);
                // get value
                var func = funcGetter();
                // set value for name
                extraFuncs[name] = func;
                // 
                lstAddedExtraFuncs.Add(func);
            }

            internal Func<Expr, Expr> ModifyFieldExpr(SqlFuncPreprocessingCtx src, string substance)
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

                    if (desc.Length > 30)
                        throw new Generator.Exception($"Alias is too long: |{desc}|={desc.Length}, >30");

                    if (CL_templs.TryGetValue(quan, out var fields) && !extraFuncs.ContainsKey(desc))
                        AddExtraFunc(desc, () => NewCodeLookupDict(src, fields, desc));

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

            IEnumerable<FuncDef> SqlFuncDefAction(string funcNamePrefix, int actualityInDays, string queryText, bool arrayResults, IDictionary<Attr.Tbl, object> xtraAttrs)
            {
                var c = new SqlFuncPreprocessingCtx()
                {
                    ldr = this,
                    funcNamesPrefix = funcNamePrefix,
                    actualityInDays = actualityInDays,
                    queryText = queryText,
                    arrayResults = arrayResults,
                    tblAttrs = xtraAttrs
                };
                var fds = Impl.FuncDefsForSql(c).ToList();

                if (lstAddedExtraFuncs.Count > 0)
                {   // some extra functions/tables autogenerated
                    foreach (var fdc in lstAddedExtraFuncs)
                        foreach (var fd in Impl.FuncDefsForSql(fdc))
                            yield return fd;
                }

                foreach (var fd in fds)
                    yield return fd;
            }

            public IEnumerable<FuncDef> LoadingFuncs()
            {
                foreach (var fdEnum in Impl.ParseSqlFuncs(sqlFileName, SqlFuncDefAction, ctx))
                    foreach (var fd in fdEnum)
                        yield return fd;
            }

            static SqlExpr SqlFromSections(params SqlSectionExpr[] sections)
                => new SqlExpr(sections.Where(s => s != null).ToArray(), SqlExpr.Options.EmptyFromPossible);

            static SqlExpr SqlFromTmpl(SqlExpr tmpl, SqlSectionExpr newSelect)
            {
                if (tmpl[SqlSectionExpr.Kind.Select] == newSelect)
                    return tmpl;
                return SqlFromSections(
                    newSelect,
                    tmpl[SqlSectionExpr.Kind.From],
                    tmpl[SqlSectionExpr.Kind.Where],
                    tmpl[SqlSectionExpr.Kind.OrderBy],
                    tmpl[SqlSectionExpr.Kind.GroupBy]
                );
            }

            internal SqlExpr PostProc(SqlFuncPreprocessingCtx c, SqlExpr sql)
            {
                bool aliasesFixedByDefault = false;

                if (c.tblAttrs.TryGetValue(Attr.Tbl.AbstractTable, out var objAbstractTable))
                    aliasesFixedByDefault = true;

                if (c.tblAttrs.TryGetValue(Attr.Tbl.LookupTableTemplate, out var objLookupTableTemplate))
                    aliasesFixedByDefault = true;

                var modFunc = c.tblAttrs.TryGetValue(Attr.Tbl.Substance, out var objSubstance)
                    ? ModifyFieldExpr(c, objSubstance.ToString())
                    : x => x;

                SqlSectionExpr select = sql[SqlSectionExpr.Kind.Select];

                #region Postprocess SELECT expression: insert inherited fields if needed
                if (c.tblAttrs.TryGetValue(Attr.Tbl._columns_attrs, out var objInnerAttrs))
                {
                    var innerAttrs = (IList<Dictionary<Attr.Col, object>>)objInnerAttrs;
                    var args = select.args;

                    bool changed = false;
                    int n = innerAttrs.Count;
                    var newInner = new List<Dictionary<Attr.Col, object>>(n);
                    var fields = new List<Expr>(n);

                    for (int i = 0; i < n; i++)
                    {
                        var attrs = innerAttrs[i];
                        if (attrs != null && attrs.TryGetValue(Attr.Col.Inherits, out var objInherits))
                        {   // inherit lot of fields from abstract tables
                            var lst = objInherits as IList;
                            if (lst == null)
                                lst = new object[] { objInherits };
                            foreach (var aT in lst)
                            {
                                if (!abstracts.TryGetValue(aT.ToString(), out var abstr))
                                    throw new Generator.Exception($"No one AbstractTable='{aT}' found");

                                var inheritedFields = abstr.sql[SqlSectionExpr.Kind.Select].args;
                                // inherit fields
                                changed = true;
                                if (abstr.attrs.TryGetValue(Attr.Tbl._columns_attrs, out var objInners))
                                {
                                    var inners = (Dictionary<Attr.Col, object>[])objInners;
                                    int k = inheritedFields.Count;
                                    for (int j = 0; j < k; j++)
                                    {   // check every inherited field modifiability
                                        var fieldAttrs = inners[j];
                                        if (Attr.GetBool(fieldAttrs, Attr.Col.FixedAlias, aliasesFixedByDefault))
                                            fields.Add(inheritedFields[j]);
                                        else
                                            fields.Add(modFunc(inheritedFields[j]));
                                    }
                                    // inherit fields attributes
                                    newInner.AddRange(inners);
                                }
                                else
                                {
                                    fields.AddRange(inheritedFields.Select(modFunc));
                                    // no attributes to inherit
                                    for (int j = inheritedFields.Count - 1; j >= 0; j--)
                                        newInner.Add(null);
                                }
                            }

                        }
                        if (i < args.Count)
                        {   // add field
                            if (Attr.GetBool(attrs, Attr.Col.FixedAlias, aliasesFixedByDefault))
                                fields.Add(args[i]);
                            else
                                fields.Add(modFunc(args[i]));
                        }
                        newInner.Add(attrs);
                    }
                    if (changed)
                    {
                        // inherited fields added, create updated SELECT expression
                        select = new SqlSectionExpr(SqlSectionExpr.Kind.Select, fields);
                        c.tblAttrs[Attr.Tbl._columns_attrs] = newInner.ToArray();
                    }
                }
                else if (objSubstance != null && !aliasesFixedByDefault)
                    select = new SqlSectionExpr(SqlSectionExpr.Kind.Select, select.args.Select(modFunc).ToList());
                #endregion

                var newSql = SqlFromTmpl(sql, select);

                if (objAbstractTable != null)
                {   // It is "abstract table", add to abstracts dictionary
                    var abstractTable = objAbstractTable.ToString();
                    abstracts.Add(abstractTable, new SqlInfo() { sql = newSql, attrs = c.tblAttrs });
                    return null;
                }

                if (objLookupTableTemplate != null)
                {
                    var ltt = objLookupTableTemplate.ToString();
                    CL_templs.Add(ltt, new SqlInfo() { sql = newSql, attrs = c.tblAttrs });
                    return null;
                }

                return newSql;
            }
        }

        /// <summary>
        /// SQL query to functions converter context 
        /// </summary>
        internal class SqlFuncPreprocessingCtx
        {
            public PreprocessingContext ldr;

            public string funcNamesPrefix;
            public double actualityInDays;
            public string queryText;
            public bool arrayResults;
            public IDictionary<Attr.Tbl, object> tblAttrs;

            public SqlExpr PostProc(SqlExpr sql)
            {
                return ldr.PostProc(this, sql);
                //var sqlSection = (e.nodeType == ExprType.Call) ? e as SqlSectionExpr : null;
                //if (sqlSection != null && sqlSection.kind == SqlSectionExpr.Kind.Select)
                //{
                //    e = ldr.PostProcSelect(this, sqlSection);
                //    if (e == null)
                //        return null;
                //}

                //if (e.nodeType != ExprType.Alias)
                //    return e;
                //var r = ((AliasExpr)e).right as ReferenceExpr;
                //if (r == null)
                //    return e;
                //var d = r.name.ToUpperInvariant();
                //switch (d)
                //{
                //    case nameof(START_TIME):
                //    case nameof(END_TIME):
                //    case nameof(END_TIME__DT):
                //    case nameof(INS_OUTS_SEPARATOR):
                //        // skip special fields
                //        return e;
                //}
                //var vi = ValueInfo.Create(d, true);
                //if (vi == null) return e;
                //var v = vi.ToString();
                //if (v == d)
                //    return e;
                //return new ReferenceExpr(v);
            }

            //public SqlExpr PostProc(SqlExpr sql)
            //{
            //    if (e == null)
            //        return null;

            //    if (e.nodeType != ExprType.Alias)
            //        return e;
            //    var r = ((AliasExpr)e).right as ReferenceExpr;
            //    if (r == null)
            //        return e;
            //    var d = r.name.ToUpperInvariant();
            //    switch (d)
            //    {
            //        case nameof(START_TIME):
            //        case nameof(END_TIME):
            //        case nameof(END_TIME__DT):
            //        case nameof(INS_OUTS_SEPARATOR):
            //            // skip special fields
            //            return e;
            //    }
            //    var vi = ValueInfo.Create(d, true);
            //    if (vi == null) return e;
            //    var v = vi.ToString();
            //    if (v == d)
            //        return e;
            //    return new ReferenceExpr(v);
            //}

        }
    }

}