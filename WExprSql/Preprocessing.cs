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

        internal static SqlFuncPreprocessingCtx NewCodeLookupDict(SqlFuncPreprocessingCtx src, SqlInfo tmpl, string descriptor)
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
                                //var firstFieldAttrs = new Dictionary<Attr.Col, object>();
                                //firstFieldAttrs.Add(Attr.Col.Description, "Dummy key field used for grouping");
                                //innerAttrs.Add(firstFieldAttrs);
                                //sb.Append($"\t0  x{descriptor.GetHashCode():X}_ID_TMP");
                                ////innerAttrs.Add(null);
                                ////sb.Append("\t0\tINS_OUTS_SEPARATOR");
                            }

                            //var vi = ValueInfo.Create(descriptor)
                            //var substance = codeFieldName.Substring(0, codeFieldName.IndexOf('_'));
                            var modFunc = src.ldr.ModifyFieldExpr(src, descriptor);
                            var unmFunc = src.ldr.ModifyFieldExpr(src, null);

                            var tmplInnerAttrs = (Dictionary<Attr.Col, object>[])tmpl.attrs[Attr.Tbl._columns_attrs];

                            for (int i = 0; i < select.args.Count; i++)
                            {
                                var attrs = tmplInnerAttrs[i];
                                if (i > 0) sb.AppendLine(",");
                                object v;
                                if (Attr.GetBool(attrs, Attr.Col.FixedAlias))
                                    v = unmFunc(select.args[i], attrs);
                                else
                                    v = modFunc(select.args[i], attrs);
                                sb.Append($"\t{v}");
                                innerAttrs.Add(attrs);
                            }
                            sb.AppendLine();
                        }
                        break;
                    case SqlSectionExpr.Kind.From:
                        if (section != null)
                            sb.AppendLine(section.ToString());
                        else
                            sb.AppendLine($"FROM {descriptor}");
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
                funcNamesPrefix = descriptor + "_DictData",
                tblAttrs = tblAttrs,
                queryText = sb.ToString(),
            };

            tblAttrs.Remove(Attr.Tbl.LookupTableTemplate);

            string tmplDescr;
            if (tblAttrs.TryGetValue(Attr.Tbl.TemplateDescription, out var objTmplDescr))
                tmplDescr = string.Format(Convert.ToString(objTmplDescr), descriptor);
            else
                tmplDescr = $"Instantiated lookup table for '{descriptor}' field";

            tblAttrs.Remove(Attr.Tbl.TemplateDescription);
            tblAttrs[Attr.Tbl.FuncPrefix] = c.funcNamesPrefix;
            Attr.Add(tblAttrs, Attr.Tbl.Description, tmplDescr, true);
            if (!src.isTimed)
                tblAttrs.Remove(Attr.Tbl.ActualityDays);
            else if (!tblAttrs.ContainsKey(Attr.Tbl.ActualityDays))
                tblAttrs.Add(Attr.Tbl.ActualityDays, c.actualityInDays);
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

            readonly Dictionary<string, SqlInfo> abstracts = new Dictionary<string, SqlInfo>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, SqlInfo> templates = new Dictionary<string, SqlInfo>(StringComparer.OrdinalIgnoreCase);

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

            static (string descr, string lookup) Combine(string tmplDescr, string[] subsDescr)
            {
                int i = tmplDescr.IndexOf('_');

                if (i < 0)
                    tmplDescr = '_' + tmplDescr;

                var tmplParts = ValueInfo.FourParts(tmplDescr);
                var parts = (subsDescr == null)
                    ? tmplParts
                    : Enumerable.Range(0, 4).Select(
                    j =>
                    {
                        var a = tmplParts[j];
                        var b = subsDescr[j];
                        switch (j)
                        {
                            case 0:
                                return (a == null || b == null) ? a ?? b : a.StartsWith(b) ? a : b.EndsWith(a) ? b : b + a;
                            case 1:
                                return (a == null || b == null) ? b ?? a : (a.Length > b.Length) ? a : b;
                            default:
                                return a ?? b;
                        }

                    }).ToArray();

                var lookup = (parts.Length > 3) ? parts[3] ?? parts[1] : parts[1];

                return (ValueInfo.FromParts(parts), lookup);

                //int i = tmplDescr.IndexOf('_');
                //string desc, quan;
                //if (i < 0)
                //{   // s contains only quantity
                //    desc = (descriptor == null) ? tmplDescr : descriptor + '_' + tmplDescr;
                //    quan = tmplDescr;
                //    //suff = string.Empty;
                //}
                //else
                //{
                //    desc = (descriptor == null || tmplDescr.StartsWith(descriptor)) ? tmplDescr : descriptor + tmplDescr;
                //    int j = tmplDescr.IndexOf('_', i + 1);
                //    quan = (j < 0) ? tmplDescr.Substring(i + 1) : tmplDescr.Substring(i + 1, j - i - 1);
                //    //{
                //    //    bool upper = false;
                //    //    int k = i - 1;
                //    //    for (; k >= 0; k--)
                //    //        if (!upper)
                //    //            upper = char.IsUpper(s[k]);
                //    //        else if (!char.IsUpper(s[k]))
                //    //            break;
                //    //    if (upper)
                //    //        suff = s.Substring(k + 1, i - k - 1);
                //    //    else
                //    //        suff = s.Substring(0, i);
                //    //}
                //}
                //return (desc, quan);
            }

            internal Func<Expr, Dictionary<Attr.Col, object>, Expr> ModifyFieldExpr(SqlFuncPreprocessingCtx src, string descriptor)
            {
                var descrParts = (descriptor == null) ? null : ValueInfo.FourParts(descriptor);

                string subst(string name, Dictionary<Attr.Col, object> attrs)
                {
                    switch (name)
                    {
                        case nameof(START_TIME):
                            src.isTimed = true;
                            return null;
                        case nameof(END_TIME):
                        case nameof(END_TIME__DT):
                        case nameof(INS_OUTS_SEPARATOR):
                            return null;
                    }

                    var (nameDescr, nameLookup) = Combine(name, descrParts);

                    if (nameDescr.Length > 30)
                        throw new Generator.Exception($"Alias is too long: {nameDescr}, {nameDescr.Length}>30");

                    var (lkupDesc, lkupQuan) = (nameDescr, nameLookup);
                    if (attrs != null && attrs.TryGetValue(Attr.Col.Lookup, out var lookup))
                    {
                        var sLookup = Convert.ToString(lookup);
                        (lkupDesc, lkupQuan) = Combine(sLookup, null);
                        //if (lkupQuan != nameQuan)
                        //    throw new Generator.Exception($"Lookup: quantity codes mismatch for {lkupDesc} and {nameDesc} // {lkupQuan}!={nameQuan}");
                    }

                    var vi = ValueInfo.Create(lkupDesc);

                    var lkupTable = lkupDesc;

                    var gotcha = templates.TryGetValue(vi.unit.Name, out var fields);
                    if (gotcha)
                    {
                        var parts = ValueInfo.FourParts(lkupDesc);
                        parts[3] = null;
                        lkupTable = ValueInfo.FromParts(parts);
                    }

                    if (gotcha || templates.TryGetValue(vi.quantity.Name, out fields))
                        if (!extraFuncs.ContainsKey(lkupTable))
                            AddExtraFunc(lkupTable, () => NewCodeLookupDict(src, fields, lkupTable));

                    return lkupTable;
                }

                return (arg, attrs) =>
                {
                    string p = null;
                    switch (arg.nodeType)
                    {
                        case ExprType.Alias:
                            var ae = (AliasExpr)arg;
                            if ((p = subst(ae.alias, attrs)) == null)
                                return arg;
                            return new AliasExpr(ae.expr, new ReferenceExpr(p));
                        case ExprType.Sequence:
                            var args = ((SequenceExpr)arg).args;
                            int n = args.Count;
                            if ((p = subst(args[n - 1].ToString(), attrs)) == null)
                                return arg;
                            return new SequenceExpr(args.Take(n - 1).Concat(new[] { new ReferenceExpr(p) }).ToList());
                        case ExprType.Reference:
                            var re = (ReferenceExpr)arg;
                            if ((p = subst(re.name, attrs)) == null)
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
                    for (int i = 0; i < lstAddedExtraFuncs.Count; i++)
                        foreach (var fd in Impl.FuncDefsForSql(lstAddedExtraFuncs[i]))
                            yield return fd;
                    lstAddedExtraFuncs.Clear();
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
                    : ModifyFieldExpr(c, null);

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
                                            fields.Add(modFunc(inheritedFields[j], fieldAttrs));
                                    }
                                    // inherit fields attributes
                                    newInner.AddRange(inners);
                                }
                                else
                                {
                                    fields.AddRange(inheritedFields.Select(s => modFunc(s, null)));
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
                                fields.Add(modFunc(args[i], attrs));
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
                    select = new SqlSectionExpr(SqlSectionExpr.Kind.Select, select.args.Select(s => modFunc(s, null)).ToList());
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
                    templates.Add(ltt, new SqlInfo() { sql = newSql, attrs = c.tblAttrs });
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
            public bool isTimed;
            public IDictionary<Attr.Tbl, object> tblAttrs;

            public SqlExpr PostProc(SqlExpr sql) => ldr.PostProc(this, sql);

        }
    }

}