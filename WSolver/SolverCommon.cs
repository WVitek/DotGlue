using System;
using System.Collections.Generic;
using System.Linq;
using W.Common;

namespace W.Expressions.Solver
{
    public class FuncInfo
    {
        public readonly FuncDef fd;
        public readonly string name;
        public readonly string[] inputs;
        public readonly string[] outputs;
        public readonly string[] inOuts;
        public readonly string[] pureIns;
        public readonly string[] pureOuts;
        readonly int nCachingInps;

        FuncInfo(FuncDef fd, SolverAliases aliasOf, string cachingDomainParam = null)
        {
            name = fd.name;
            this.fd = fd;
            if (cachingDomainParam != null && fd.cachingExpiration != TimeSpan.Zero)
            {
                nCachingInps = 1;
                inputs = new string[fd.argsInfo.Length + nCachingInps];
                int k = 0;
                inputs[k++] = cachingDomainParam;
                foreach (var arg in fd.argsInfo)
                    inputs[k++] = arg.ToString();
            }
            else
            {
                inputs = new string[fd.argsInfo.Length];
                for (int i = 0; i < inputs.Length; i++)
                {
                    var s = fd.argsInfo[i].ToString();
                    inputs[i] = aliasOf.GetRealName(s);
                }
            }
            int no = fd.resultsInfo.Length;
            outputs = new string[no];
            var pureOuts = new List<string>(no);
            var inOuts = new List<string>(no);
            for (int i = 0; i < outputs.Length; i++)
            {
                var s = fd.resultsInfo[i].ToString();
                var real = aliasOf.GetRealName(s);
                if (real != s && !inputs.Contains(real))
                    if (fd.kind != FuncKind.Macro)
                        throw new SolverException(string.Format("Name conflict between alias '{0}' and output of function '{1}'", s, name));
                outputs[i] = real;
            }
            foreach (var s in outputs)
            {
                if (Array.IndexOf<string>(inputs, s) < 0)
                    pureOuts.Add(s);
                else
                {
                    System.Diagnostics.Debug.Assert(ValueInfo.IsID(s), $"Only _ID parameters can be 'in/out' // {name}:{s}");
                    inOuts.Add(s);
                }
            }
            var pureIns = new List<string>(no);
            for (int i = 0; i < inputs.Length; i++)
            {
                var s = inputs[i];
                if (Array.IndexOf<string>(outputs, s) < 0)
                    pureIns.Add(s);
            }

            this.pureIns = pureIns.ToArray();
            this.pureOuts = pureOuts.ToArray();
            this.inOuts = inOuts.ToArray();
        }

        public static FuncInfo Create(FuncDef fd, SolverAliases aliasOf)
        { return CreateWithCachingInfo(fd, aliasOf, null); }

        public static FuncInfo CreateWithCachingInfo(FuncDef fd, SolverAliases aliasOf, string cachingDomainParam)
        {
            if (string.IsNullOrEmpty(fd.name) || fd.argsInfo == null || fd.resultsInfo == null || fd.resultsInfo.Length == 0)
                return null;
            else return new FuncInfo(fd, aliasOf, cachingDomainParam);
        }

        public static FuncInfo WithoutCachingInfo(FuncInfo info, SolverAliases aliasOf)
        {
            if (info.IsSingleInputFunc)
                return info;
            if (info.fd == null)
                return new FuncInfo(info.name, info.inputs, info.outputs, aliasOf);
            else return Create(info.fd, aliasOf);
        }

        static readonly string[] None = new string[0];

        static readonly FuncDef funcDefInputFunc = new FuncDef((Macro)FuncDefs_Solver.SolverInputFunc, null, 1, 1, null, null);

        /// <summary>
        /// Input value wrapping function
        /// </summary>
        public FuncInfo(string[] inputs, string[] outputs)
        {
            this.name = funcDefInputFunc.name;
            this.fd = funcDefInputFunc;
            this.inputs = inputs;
            this.outputs = outputs;
            this.inOuts = None;
            this.pureIns = None;
            this.pureOuts = outputs;
        }

        public FuncInfo(string name, string[] inputs, string[] outputs, SolverAliases aliasOf = null)
        {
            this.name = name;
            if (inputs == outputs) // fix for _Input function
            {
                this.inputs = inputs;
                this.outputs = outputs;
                this.inOuts = None;
                this.pureIns = None;
                this.pureOuts = outputs;
                return;
            }
            if (aliasOf != null)
            {
                this.inputs = aliasOf.ToRealNames(inputs);
                var outs = aliasOf.ToRealNames(outputs);
                for (int i = 0; i < outputs.Length; i++)
                    if (outs[i] != outputs[i] && !inputs.Contains(outs[i]))
                        throw new SolverException(string.Format("Alias name can't be used as function output ({0}: {1}=>{2})", outs[i], name, outputs[i]));
                this.outputs = outs;
            }
            else
            {
                this.inputs = inputs;
                this.outputs = outputs;
            }
            int no = outputs.Length;
            var pureOuts = new List<string>(no);
            var inOuts = new List<string>(no);
            for (int i = 0; i < no; i++)
            {
                var s = outputs[i];
                if (Array.IndexOf<string>(inputs, s) < 0)
                    pureOuts.Add(s);
                else
                    inOuts.Add(s);
            }
            this.pureIns = inputs.Where(s => Array.IndexOf<string>(outputs, s) < 0).ToArray();
            this.pureOuts = pureOuts.ToArray();
            this.inOuts = inOuts.ToArray();
        }

        public FuncInfo WithExtraKeys(IEnumerable<string> extraKeys)
        {
            return new FuncInfo(name, inputs.Union(extraKeys).ToArray(), outputs.Union(extraKeys).ToArray());
        }

        public bool IsSingleInputFunc { get { return fd == funcDefInputFunc && inputs.Length == 1 && outputs.Length == 1; } }
        public bool IsMultiInputFunc { get { return fd != null && fd.kind == FuncKind.Macro && inputs.Length == 0 && outputs.Length > 1; } }
        public bool IsLookup { get { return fd != null && fd.isLookupFunc; } }

        public bool NeedLinearizedArgs
        {
            get
            {
                if (fd == null) return false;
                switch (fd.kind)
                {
                    case FuncKind.Fx:
                        return inputs.Length - nCachingInps > 0;
                    case FuncKind.Macro:
                        return fd.maxArity == 1 && inputs.Length - nCachingInps > fd.maxArity;
                }
                return false;
            }
        }

        public override string ToString()
        {
            return string.Format("'{0}:[{1}]->[{2}]'", name, string.Join(",", inputs), string.Join(",", outputs));
        }

        class Usage
        {
            public int n;
            public override string ToString() { return n.ToString(); }
        }

        public static List<FuncInfo> TopoSort(IList<FuncInfo> funcs)
        {
            var dict = new Dictionary<string, Usage>(StringComparer.OrdinalIgnoreCase);
            var src = new List<FuncInfo>(funcs);
            var dst = new List<FuncInfo>(src.Count);
            foreach (var fi in src)
                foreach (var valueName in fi.pureOuts)
                {
                    Usage usage;
                    if (dict.TryGetValue(valueName, out usage))
                        usage.n++;
                    else
                        dict[valueName] = new Usage() { n = 1 };
                }
            while (src.Count > 0)
            {
                int iMin = -1;
                int min = int.MaxValue;
                for (int i = src.Count - 1; i >= 0; i--)
                {
                    var fi = src[i];
                    var inps = fi.inputs;
                    if (inps.Any(valueName =>
                    {
                        Usage usage;
                        return dict.TryGetValue(valueName, out usage) && usage.n > 0;
                    })) continue;
                    int cur = (inps.Length << 16) + fi.outputs.Length;
                    if (cur <= min)
                    {
                        iMin = i;
                        min = cur;
                    }
                }
                FuncInfo nextFunc;
                if (iMin >= 0)
                {
                    nextFunc = src[iMin];
                    if (nextFunc == null)
                    { }
                    foreach (var valueName in nextFunc.pureOuts)
                    {
                        Usage usage;
                        if (dict.TryGetValue(valueName, out usage))
                            usage.n--;
                    }
                }
                else
                {
                    nextFunc = src[0];
                    iMin = 0;
                }
                dst.Add(nextFunc);
                src.RemoveAt(iMin);
            }
            return dst; // todo
        }
    }

    class ResultInfo
    {
        public IList<IIndexedDict> data;
        public IDictionary<string, int> key2ndx;
        public FuncInfo funcInfo;
        public override string ToString()
        { return string.Format("[{0}]{1})", data.Count, funcInfo); }
    }


}