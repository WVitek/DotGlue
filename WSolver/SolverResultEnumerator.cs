using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace W.Expressions.Solver
{
    using W.Common;
    using W.Expressions;

    class ResultEnumerator
    {
        public readonly Dictionary<string, int> outFieldsNdx;

        public ResultEnumerator(ResultInfo[] results, string[][] outputFields, string[] outFieldsOrder, int[] keyFields)
        {
            int n = results.Length;
            System.Diagnostics.Trace.Assert(n == outputFields.Length);
            var allFields = new Dictionary<string, int>();
            this.outFieldsNdx = new Dictionary<string, int>();
            var valuesDependenciesDict = new Dictionary<int, List<int>>();
            for (int i = 0; i < outFieldsOrder.Length; i++)
                outFieldsNdx[outFieldsOrder[i]] = -1;
            srcInfos = new SrcInfo[n];
            for (int i = 0; i < n; i++)
            {
                var src = results[i];
                var key2loc = src.key2ndx;
                // loc2glb
                var loc2glb = new int[key2loc.Count];
                foreach (var pair in key2loc)
                {
                    var key = pair.Key;
                    var locNdx = pair.Value;
                    int glbNdx;
                    if (!allFields.TryGetValue(key, out glbNdx))
                    {
                        glbNdx = allFields.Count;
                        allFields.Add(key, glbNdx);
                    }
                    loc2glb[locNdx] = glbNdx;
                }
                var fi = src.funcInfo;
                var si = new SrcInfo() { loc2glb = loc2glb, data = src.data };
                // pureIns
                {
                    var pi = fi.pureIns;
                    var pig = new List<int>(pi.Length);
                    for (int j = 0; j < pi.Length; j++)
                    {
                        int k;
                        if (allFields.TryGetValue(pi[j], out k))
                            pig.Add(k);
                    }
                    si.pureInsGlb = pig.ToArray();
                }
                // inOuts
                {
                    var io = fi.inOuts;
                    si.inOuts = new int[io.Length];
                    for (int j = io.Length - 1; j >= 0; j--)
                        si.inOuts[j] = key2loc[io[j]];
                    Array.Sort<int>(si.inOuts);
                }
                // pureOuts
                {
                    var po = fi.pureOuts;
                    si.pureOuts = new int[po.Length];
                    var extKeysGlb = new Dictionary<int, bool>();
                    for (int j = po.Length - 1; j >= 0; j--)
                    {
                        int loc = key2loc[po[j]];
                        si.pureOuts[j] = loc;
                        int glb = loc2glb[loc];
                        // update dependencies info
                        List<int> lst;
                        if (!valuesDependenciesDict.TryGetValue(glb, out lst))
                        {
                            lst = new List<int>();
                            valuesDependenciesDict.Add(glb, lst);
                        }
                        foreach (var s in fi.inputs)
                        {
                            int k;
                            if (!allFields.TryGetValue(s, out k))
                                continue;
                            if (!lst.Contains(k))
                                lst.Add(k);
                        }
                    }
                    Array.Sort<int>(si.pureOuts);
                    si.extKeysGlb = fi.inputs.SelectMany(
                        s =>
                        {
                            int k;
                            List<int> lst;
                            if (allFields.TryGetValue(s, out k) && valuesDependenciesDict.TryGetValue(k, out lst))
                                return lst;
                            else return Enumerable.Empty<int>();
                        }).Distinct().OrderBy(k => k).ToArray();
                }
                // outputs
                {
                    si.outputs = new int[fi.outputs.Length];
                    for (int j = 0; j < si.outputs.Length; j++) si.outputs[j] = j;
                }
                // results
                {
                    var of = outputFields[i];
                    si.results = new int[of.Length];
                    for (int j = 0; j < of.Length; j++)
                    {
                        var key = of[j];
                        int loc = key2loc[key];
                        si.results[j] = loc;
                        int glb;
                        if (outFieldsNdx.TryGetValue(key, out glb))
                        {
                            if (glb < 0)
                            {
                                glb = loc2glb[loc];
                                outFieldsNdx[key] = glb;
                            }
                            else throw new ArgumentException("Duplicated result field specified: " + key);
                        }
                        else System.Diagnostics.Trace.Assert(false, "Key or alias not found in outFieldsNdx: " + key);
                    }
                }
                // save
                srcInfos[i] = si;
            }
            // 
            if (outFieldsNdx.Count != outFieldsOrder.Length)
                System.Diagnostics.Trace.Assert(false);
            this.resColumns = new int[outFieldsNdx.Count];
            for (int i = resColumns.Length - 1; i >= 0; i--)
            {
                int k;
                var s = outFieldsOrder[i];
                if (!outFieldsNdx.TryGetValue(s, out k))
                    k = -1;
                if (k < 0)
                    throw new KeyNotFoundException();
                resColumns[i] = k;
            }
            //
            this.allFields = allFields.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();
            // propagate results activity
            var activeFields = new bool[allFields.Count];
            for (int i = srcInfos.Length - 1; i >= 0; i--)
            {
                var si = srcInfos[i];
                if (si.loc2glb == null)
                    continue;
                bool active = (si.results.Length > 0) && (si.data.Count > 0);
                if (!active)
                    // check out fields usage
                    foreach (int j in si.outputs)
                        if (activeFields[si.loc2glb[j]])
                        {   // j-th field value is marked as used in joining
                            active = true;
                            break;
                        }
                if (active != si.active)
                {   // mark key fields as active (must be used in joining)
                    foreach (int loc in si.inOuts)
                        activeFields[si.loc2glb[loc]] = true;
                    foreach (int glb in si.pureInsGlb)
                        activeFields[glb] = true;
                    srcInfos[i].active = true;
                }
            }
            if (keyFields != null)
                foreach (var i in keyFields)
                    resColumns[i] = -Math.Abs(resColumns[i] + 1);
        }

        class SrcInfo
        {
            /// <summary>
            /// to convert field index from local to global
            /// </summary>
            public int[] loc2glb;
            public int[] outputs;
            public int[] pureInsGlb;
            public int[] extKeysGlb;
            public int[] inOuts;
            public int[] pureOuts;
            public int[] results;
            public IList<IIndexedDict> data;
            public bool active;

            public override string ToString()
            {
                if (data == null || data.Count == 0)
                    return string.Empty;
                return string.Format("[{0}]:{1}", data.Count, string.Join(",", data[0].Key2Ndx.Keys));
            }
        }

        struct ColData
        {
            public int iRow;
            public object Value;
        }

        class ColumnStack
        {
            public string name;
            public Stack<ColData> values;
            public override string ToString()
            {
                if (values.Count == 0)
                    return string.Format("{0}=", name);
                var p = values.Peek();
                var v = p.Value as ITimedObject;
                if (v != null)
                    return string.Format("{0}={1}:{2} [{3}, {4}]", name, p.iRow, v.Object, v.Time.ToString("yyyy-MM-dd HH:mm:ss"), v.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
                else
                    return string.Format("{0}={1}:{2}", name, p.iRow, p.Value);
            }
            public ColData Peek() { return (values.Count == 0) ? new ColData() { iRow = -1 } : values.Peek(); }
            ColData first;
            public void Push(int iRow, object value)
            {
                var cd = new ColData() { iRow = iRow, Value = value };
                if (values.Count == 0)
                    first = cd;
                values.Push(cd);
            }
            public ColData First()
            {
                if (values.Count == 0)
                    return default(ColData);
                else return first;
            }
            public bool Empty { get { return values.Count == 0; } }
        }

        readonly SrcInfo[] srcInfos;
        readonly string[] allFields;
        readonly int[] resColumns;

        IEnumerable<object[]> EnumerateTimed(ColumnStack[] colInfos, int iSrc, ITimedObject prevRange)
        {
            if (iSrc == srcInfos.Length)
            #region Return result of enumeration
            {   // only return one result
                if (prevRange.EndTime < prevRange.Time)
                    yield break;

                var res = new object[resColumns.Length];
                bool allValNulls = true;
                bool firstKey = true;

                for (int i = 0; i < res.Length; i++)
                {
                    int j = resColumns[i];
                    int ndx = (j < 0) ? -j - 1 : j;
                    var cd = colInfos[ndx].First().Value;
                    object ri;
                    if (Utils.IsEmpty(cd))
                        ri = cd;
                    else if (j >= 0) // nonnegative j for data field (output value)
                    {
                        allValNulls = false;
                        ri = TimedObject.TryAsTimed(cd, prevRange.Time, prevRange.EndTime);
                    }
                    else // negative j for key field (not output value)
                    {
                        var to = cd as ITimedObject;
                        if (to != null)
                        {
                            ri = TimedObject.ValueInRange(to, prevRange);
                            if (firstKey && ri == null)
                                yield break;
                        }
                        else
                            ri = TimedObject.Timed(prevRange.Time, prevRange.EndTime, cd); // keys
                        firstKey = false;
                    }
                    res[i] = ri;
                }
                if (!allValNulls)
                {
                    if (res[0] == null)
                        yield return res;
                    else
                        yield return res;
                    yield break;
                }
                else yield break;
            }
            #endregion
            var si = srcInfos[iSrc];
            #region Skip inactive source
            if (!si.active)
            {
                // values of iSrc-th source don't affects results
                // just process remaining sources
                foreach (var res in EnumerateTimed(colInfos, iSrc + 1, prevRange))
                    yield return res;
                yield break;
            }
            #endregion
            // values of iSrc-th source affects results
            IEnumerable<int> rows;
            int[] outNdxs = null;
            int nRowsFound = 0;
            var keysTimeRange = new TimedInt64(DateTime.MinValue, DateTime.MaxValue, 0);
            if (iSrc == 0)
            #region Rows of first source (not filtered)
            {
                rows = Enumerable.Range(0, si.data.Count);
                outNdxs = si.outputs;
            }
            #endregion
            else if (si.inOuts.Length > 0)
            #region like SelectMany
            {   // rows filtered by key field ( 'SelectMany'-like function)
                outNdxs = si.pureOuts;
                var inOuts = si.inOuts;
                var keyVals = new object[inOuts.Length];
                bool withUnknown = false, withKnown = false;
                for (int i = 0; i < inOuts.Length; i++)
                {
                    int iGlb = si.loc2glb[inOuts[i]];
                    var ci = colInfos[iGlb];
                    if (ci.Empty)
                    {
                        withUnknown = true;
                        continue;
                    }
                    else
                    {
                        withKnown = true;
                        var val = ci.Peek().Value;
                        keyVals[i] = val;
                        var to = val as ITimedObject;
                        if (to != null)
                        {
                            if (to.Time > keysTimeRange.time)
                                keysTimeRange.time = to.Time;
                            if (to.EndTime < keysTimeRange.endTime)
                                keysTimeRange.endTime = to.EndTime;
                        }
                    }
                }
                if (withKnown && withUnknown)
                //if (withUnknown)
                {
                    //yield break;
                    throw new Exception("Solver.ResultEnumerator: part of key values is undefined ("
                        + string.Join(",", inOuts.Select(i => colInfos[si.loc2glb[i]]).Where(c => c.Empty).Select(c => c.name))
                        + ")");
                }
                else if (withKnown)
                    rows = si.data.BinarySearchAllWithTimeRange(inOuts, keyVals, prevRange);
                else rows = Enumerable.Range(0, si.data.Count);
            }
            #endregion
            else if (si.pureInsGlb.Length > 0)
            #region like Select
            {   // row is result of 'Select'-like function ( one result for one input)
                var iMax = -1;
                foreach (int glb in si.pureInsGlb)
                {
                    var ci = colInfos[glb];
                    if (ci.Empty)
                        continue;
                    var cd = ci.Peek();
                    if (cd.iRow > iMax)
                        iMax = cd.iRow;
                    else if (cd.iRow < 0)
                    { iMax = -1; break; }
                }
                if (iMax >= 0)
                    rows = Enumerable.Range(iMax, 1);
                else rows = null;
                outNdxs = si.pureOuts;
            }
            #endregion
            else
            {   // "Input" function = independent value(s)
                rows = Enumerable.Range(0, si.data.Count);
                outNdxs = si.outputs;
            }
            var prevEndTime = prevRange.Time;
            if (rows != null)
            {
                //var dataRange = new TimedObject() { time = prevRange.Time, endTime = prevRange.EndTime };
                foreach (var iRow in rows)
                {
                    nRowsFound++;
                    var rowVals = si.data[iRow].ValuesList;
                    var dataTimeRange = new TimedInt64(DateTime.MinValue, DateTime.MaxValue, 0);
                    // determine time range
                    foreach (int i in si.inOuts)
                    {
                        var to = rowVals[i] as ITimedObject;
                        if (to == null)
                            continue;
                        if (to.Time > dataTimeRange.time)
                            dataTimeRange.time = to.Time;
                        if (to.EndTime < dataTimeRange.endTime)
                            dataTimeRange.endTime = to.EndTime;
                    }
                    ITimedObject nextRange;
                    if (dataTimeRange.time > DateTime.MinValue)
                        nextRange = TimedObject.ValueInRange(dataTimeRange, prevRange);
                    else if (keysTimeRange.time > DateTime.MinValue)
                    {
                        dataTimeRange = keysTimeRange;
                        nextRange = TimedObject.ValueInRange(dataTimeRange, prevRange);
                    }
                    else nextRange = prevRange;
                    if (nextRange == null)
                        yield break;
                    if (DateTime.MinValue < prevEndTime && prevEndTime < nextRange.Time) // todo: nextRange == null
                    {   // emulate empty values of current source
                        foreach (int i in outNdxs)
                            colInfos[si.loc2glb[i]].Push(-1, null);
                        foreach (var res in EnumerateTimed(colInfos, iSrc + 1, new TimeRange(prevEndTime, nextRange.Time)))
                            yield return res;
                        foreach (int i in outNdxs)
                            colInfos[si.loc2glb[i]].values.Pop();
                    }
                    // push found values
                    foreach (int i in outNdxs)
                    {
                        var rv = rowVals[i];
                        var col = colInfos[si.loc2glb[i]];
                        //col.Push(iRow, rv);
                        var to = rv as ITimedObject;
                        if (to != null)
                            col.Push(iRow, to);
                        else if (Utils.IsEmpty(rv))
                            //col.Push(iRow, TimedObject.EmptyI);
                            col.Push(iRow, null);
                        else if (dataTimeRange.time > DateTime.MinValue)
                            col.Push(iRow, TimedObject.Timed(dataTimeRange.time, dataTimeRange.endTime, rv));
                        else col.Push(iRow, rv);
                    }
                    // process remained sources
                    foreach (var res in EnumerateTimed(colInfos, iSrc + 1, nextRange))
                        yield return res;
                    // restore values
                    foreach (int i in outNdxs)
                        colInfos[si.loc2glb[i]].values.Pop();
                    prevEndTime = nextRange.EndTime;
                }
            }
            if (prevEndTime < prevRange.EndTime)//.AddMilliseconds(-1))
            {   // emulate ending empty value of current source, if needed
                var lastRange = new TimeRange(prevEndTime, prevRange.EndTime); // TimedObject.Timed(prevEndTime, prevRange.EndTime, prevRange);
                                                                               // push null values
                foreach (int i in outNdxs)
                    colInfos[si.loc2glb[i]].Push(-1, null);
                // process remained sources
                foreach (var res in EnumerateTimed(colInfos, iSrc + 1, lastRange))
                    yield return res;
                // restore values
                foreach (int i in outNdxs)
                    colInfos[si.loc2glb[i]].values.Pop();
            }
        }

        public IEnumerable<object[]> EnumTimed(ITimedObject range = null)
        {
            var ci = new ColumnStack[allFields.Length];
            int n = srcInfos.Length;
            for (int i = 0; i < ci.Length; i++)
                ci[i] = new ColumnStack() { name = allFields[i], values = new Stack<ColData>(n) };
            if (range == null)
                range = new TimedNull(TimedObject.NonZeroTime, DateTime.MaxValue);
            return EnumerateTimed(ci, 0, range);
        }

        public IEnumerable<object[]> EnumNonTimed()
        {
            var ci = new ColumnStack[allFields.Length];
            int n = srcInfos.Length;
            for (int i = 0; i < ci.Length; i++)
                ci[i] = new ColumnStack() { name = allFields[i], values = new Stack<ColData>(n) };
            return EnumerateNonTimed(ci, 0);
        }

        IEnumerable<object[]> EnumerateNonTimed(ColumnStack[] colInfos, int iSrc)
        {
            if (iSrc == srcInfos.Length)
            #region Return result of enumeration
            {   // only return one result
                var res = new object[resColumns.Length];
                bool allNulls = true;
                for (int i = 0; i < res.Length; i++)
                {
                    int j = resColumns[i];
                    int ndx = (j < 0) ? -j - 1 : j;
                    var cd = colInfos[ndx].First().Value;
                    if (Utils.IsEmpty(cd))
                        res[i] = cd;
                    else if (j >= 0) // nonnegative j for data field (output value)
                    {
                        allNulls = false;
                        res[i] = cd;
                    }
                    else // negative j for key field (not output value)
                        res[i] = cd; // keys
                }
                if (!allNulls)
                {
                    yield return res;
                    yield break;
                }
                else yield break;
            }
            #endregion
            var si = srcInfos[iSrc];
            #region Skip inactive source
            if (!si.active)
            {
                // values of iSrc-th source don't affects results
                // just process remaining sources
                foreach (var res in EnumerateNonTimed(colInfos, iSrc + 1))
                    yield return res;
                yield break;
            }
            #endregion
            // values of iSrc-th source affects results
            IEnumerable<int> rows;
            int[] outNdxs = null;
            int nRowsFound = 0;
            if (iSrc == 0)
            #region Rows of first source (not filtered)
            {
                rows = Enumerable.Range(0, si.data.Count);
                outNdxs = si.outputs;
            }
            #endregion
            else if (si.inOuts.Length > 0)
            #region like SelectMany
            {   // rows filtered by key field ( 'SelectMany'-like function)
                outNdxs = si.pureOuts;
                var inOuts = si.inOuts;
                var keyVals = new object[inOuts.Length];
                for (int i = 0; i < keyVals.Length; i++)
                {
                    int iGlb = si.loc2glb[inOuts[i]];
                    var we = colInfos[iGlb].Peek().Value;
                    keyVals[i] = we;
                }
                rows = si.data.BinarySearchAll(inOuts, keyVals);
            }
            #endregion
            else if (si.pureInsGlb.Length > 0)
            #region like Select
            {   // row is result of 'Select'-like function ( one result for one input)
                var iMax = -1;
                foreach (int glb in si.pureInsGlb)
                {
                    var cd = colInfos[glb].First();
                    if (cd.iRow > iMax)
                        iMax = cd.iRow;
                    else if (cd.iRow < 0)
                    { iMax = -1; break; }
                }
                if (iMax >= 0)
                    rows = Enumerable.Range(iMax, 1);
                else rows = null;
                outNdxs = si.pureOuts;
            }
            #endregion
            else
            {   // "Input" function = independent value(s)
                rows = Enumerable.Range(0, si.data.Count);
                outNdxs = si.outputs;
            }
            if (rows != null)
            {
                foreach (var iRow in rows)
                {
                    nRowsFound++;
                    var rowVals = si.data[iRow].ValuesList;
                    // push found values
                    foreach (int i in outNdxs)
                    {
                        var rv = rowVals[i];
                        var col = colInfos[si.loc2glb[i]];
                        col.Push(iRow, rv);
                    }
                    // process remained sources
                    foreach (var res in EnumerateNonTimed(colInfos, iSrc + 1))
                        yield return res;
                    // restore values
                    foreach (int i in outNdxs)
                        colInfos[si.loc2glb[i]].values.Pop();
                }
            }
            //SkipRows:
            if (nRowsFound == 0)
            {
                // push null values
                foreach (int i in outNdxs)
                    //colInfos[si.loc2glb[i]].Push(-1, TimedObject.EmptyI);
                    colInfos[si.loc2glb[i]].Push(-1, null);
                // process remained sources
                foreach (var res in EnumerateNonTimed(colInfos, iSrc + 1))
                    yield return res;
                // restore values
                foreach (int i in outNdxs)
                    colInfos[si.loc2glb[i]].values.Pop();
            }
        }
    }

}
