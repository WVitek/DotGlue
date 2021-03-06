﻿using System;
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

        internal static SqlFuncPreprocessingCtx NewCodeLookupDict(SqlFuncPreprocessingCtx src, SqlInfo tmpl,
            string descriptor, Dictionary<Attr.Col, object> descrFieldAttrs)
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
                actualityInDays = Attr.defaultActualityDays * 10,
                arrayResults = true,
                funcNamesPrefix = descriptor + "_DictData",
                tblAttrs = tblAttrs,
                queryText = sb.ToString(),
            };

            tblAttrs.Remove(Attr.Tbl.LookupTableTemplate);

            string tmplDescr;

            {
                string srcTableComment = Attr.OneLineText(src.tblAttrs.Get(Attr.Tbl.Description));
                string tmplDescrComment = Attr.OneLineText(descrFieldAttrs.Get(Attr.Col.Description));

                if (tblAttrs.TryGetValue(Attr.Tbl.TemplateDescription, out var objTmplDescr))
                    tmplDescr = string.Format(Convert.ToString(objTmplDescr), descriptor, tmplDescrComment, srcTableComment);
                else
                    tmplDescr = $"Instantiated lookup table for\t{descriptor}\t{tmplDescrComment}\t{srcTableComment}";
            }

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
            public DbFuncType forKinds;
            public TimeSpan cachingExpiration;
            public string cacheSubdomain;
            public string defaultLocationForValueInfo;
            public Generator.Ctx ctx;

            readonly Dictionary<string, SqlInfo> abstracts = new Dictionary<string, SqlInfo>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, SqlInfo> templates = new Dictionary<string, SqlInfo>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Autogenerated "SQL functions" dictionary: lookup tables, for example
            /// </summary>
            readonly Dictionary<string, SqlFuncPreprocessingCtx> extraFuncs = new Dictionary<string, SqlFuncPreprocessingCtx>();
            /// <summary>
            /// Clearable list of autogenerated "SQL functions" for output purposes
            /// </summary>
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

            public static (string descr, string lookup) Combine(string name, string[] subsDescr, bool isPK)
            {
                if (name.IndexOf('_') < 0)
                    name = '_' + name;

                var tmplParts = ValueInfo.FourParts(name);
                var parts = (subsDescr == null)
                    ? tmplParts
                    : Enumerable.Range(0, 4).Select(
                    j =>
                    {
                        var a = tmplParts[j];
                        var b = subsDescr[j];
                        switch ((ValueInfo.Part)j)
                        {
                            case ValueInfo.Part.Substance:
                                return (a == null || b == null) ? a ?? b : a.StartsWith(b) ? a : b.EndsWith(a) ? b : b + a;
                            case ValueInfo.Part.Quantity:
                                return (a == null || b == null) ? b ?? a : isPK ? b : a;
                            default:
                                return a ?? b;
                        }

                    }).ToArray();

                var lookup = (parts.Length > 3) ? parts[3] ?? parts[1] : parts[1];

                return (ValueInfo.FromParts(parts), lookup);
            }

            /// <summary>
            /// Create field processing func
            /// </summary>
            internal Func<Expr, Dictionary<Attr.Col, object>, Expr> ModifyFieldExpr(SqlFuncPreprocessingCtx src, string targetDescrMask)
            {
                var maskPartsOnlyLocation = new string[4] { null, null, src.DefaultLocationForValueInfo, null };
                string[] maskParts;
                if (targetDescrMask == null)
                    maskParts = maskPartsOnlyLocation;
                else
                {
                    maskParts = ValueInfo.FourParts(targetDescrMask);
                    if (maskParts[2] == null)
                        maskParts[2] = src.DefaultLocationForValueInfo;
                }

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

                    bool isFixedAlias = attrs.GetBool(Attr.Col.FixedAlias);
                    bool isPK = attrs.GetBool(Attr.Col.PK);

                    var nameDescr = Combine(name, isFixedAlias ? maskPartsOnlyLocation : maskParts, isPK).descr;

                    if (nameDescr.Length > 30)
                        throw new Generator.Exception($"Alias is too long: {nameDescr}, {nameDescr.Length}>30");

                    string lkupDescr;
                    if (attrs != null && attrs.TryGetValue(Attr.Col.Lookup, out var lookup))
                        lkupDescr = ValueInfo.OverrideByMask(name, lookup.ToString());
                    else
                    {
                        lkupDescr = nameDescr;
                        lookup = null;
                    }

                    var vi = ValueInfo.Create(lkupDescr, true);//, src.ldr.defaultLocationForValueInfo);

                    if (vi == null)
                        return nameDescr;

                    //nameDescr = vi.ToString();

                    string lkupTable = null;

                    if (templates.TryGetValue(vi.unit.Name, out var fields))
                    {   // when templated by 'units', remove 'units' part
                        var parts = ValueInfo.FourParts(lkupDescr);
                        parts[2] = parts[3] = null; // remove location and units from lookup table name
                        lkupTable = ValueInfo.FromParts(parts);
                        parts = ValueInfo.FourParts(nameDescr);
                        parts[3] = null; // remove units, needed only for templating purposes
                        nameDescr = ValueInfo.FromParts(parts);
                    }
                    else if (templates.TryGetValue(vi.quantity.Name, out fields))
                    {
                        lkupTable = lkupDescr;
                        var parts = ValueInfo.FourParts(lkupDescr);
                        parts[2] = null; // remove LOCATION part from lookup table name
                        lkupTable = ValueInfo.FromParts(parts);
                    }
                    else if (lookup != null)
                        throw new KeyNotFoundException($"Can't found template table named '{vi.unit}' or {vi.quantity.Name} for lookup attribute '{lkupDescr}' of '{name}'");


                    if (!isPK && fields.sql != null)
                    {
                        if (!extraFuncs.ContainsKey(lkupTable))
                            AddExtraFunc(lkupTable, () => NewCodeLookupDict(src, fields, lkupTable, attrs));
                    }

                    return nameDescr;
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
                c.tblAttrs.TryGetValue(Attr.Tbl.AbstractTable, out var objAbstractTable);
                c.tblAttrs.TryGetValue(Attr.Tbl.LookupTableTemplate, out var objLookupTableTemplate);

                var modFunc = c.tblAttrs.TryGetValue(Attr.Tbl.Substance, out var objSubstance)
                    ? ModifyFieldExpr(c, objSubstance.ToString())
                    : (objAbstractTable == null && objLookupTableTemplate == null)
                        ? ModifyFieldExpr(c, null)
                        : (expr, attrs) => expr;

                SqlSectionExpr select = sql[SqlSectionExpr.Kind.Select];

                #region Postprocess SELECT expression: insert inherited fields if needed
                if (c.tblAttrs.TryGetValue(Attr.Tbl._columns_attrs, out var objInnerAttrs))
                {
                    var innerAttrs = (IList<Dictionary<Attr.Col, object>>)objInnerAttrs;
                    var args = select.args;

                    //bool changed = false;
                    int n = innerAttrs.Count;
                    var newInner = new List<Dictionary<Attr.Col, object>>(n);
                    var fields = new List<Expr>(n);

                    for (int i = 0; i < n; i++)
                    {
                        var attrs = innerAttrs[i];
                        #region 'Inherits' attribute processing
                        if (attrs.TryGet(Attr.Col.Inherits, out var objInherits))
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
                                //changed = true;
                                if (abstr.attrs.TryGetValue(Attr.Tbl._columns_attrs, out var objInners))
                                {
                                    var inners = (Dictionary<Attr.Col, object>[])objInners;
                                    int k = inheritedFields.Count;
                                    for (int j = 0; j < k; j++)
                                        fields.Add(modFunc(inheritedFields[j], inners[j]));
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
                        #endregion
                        if (i < args.Count)
                            // add field
                            fields.Add(modFunc(args[i], attrs));
                        newInner.Add(attrs);
                    }
                    //if (changed)
                    {
                        // inherited fields added, create updated SELECT expression
                        select = new SqlSectionExpr(SqlSectionExpr.Kind.Select, fields);
                        c.tblAttrs[Attr.Tbl._columns_attrs] = newInner.ToArray();
                    }
                }
                else if (objSubstance != null && objAbstractTable == null && objLookupTableTemplate == null)
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

            public string DefaultLocationForValueInfo => tblAttrs.GetString(Attr.Tbl.DefaultLocation) ?? ldr.defaultLocationForValueInfo;

        }
    }

}