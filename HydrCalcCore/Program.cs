using PipeNetCalc;
using System;
using System.Collections.Generic;
using System.IO;
using Oracle.ManagedDataAccess.Client;
using System.Runtime.Caching;
using System.Linq;

namespace PPM.HydrCalcPipe
{
    static class Program
    {
        /// <summary>
        /// Поиск гидравлически единых подсетей
        /// </summary>
        /// <param name="edges"></param>
        /// <param name="nodes"></param>
        /// <returns>Список подсетей (подсеть - массив индексов относящихся к ней рёбер)</returns>
        static List<int[]> GetSubnets(Edge[] edges, Node[] nodes)
        {
            var subnets = new List<int[]>();
            using (new StopwatchMs("Splitting into subnets"))
            {
                int min = int.MaxValue, max = 0, sum = 0;

                foreach (var subnetEdges in PipeGraph.Subnets(edges, nodes))
                {
                    subnets.Add(subnetEdges);
                    int n = subnetEdges.Length;
                    if (n < min) min = n;
                    if (n > max) max = n;
                    sum += n;
                }
                Console.WriteLine($"nSubnets={subnets.Count}, min={min}, avg={sum / subnets.Count:g}, max={max}");
            }
            return subnets;
        }

        static CalcRec.HydrCalcDataRec[] HydrCalc(
            Edge[] edges, Node[] nodes, List<int[]> subnets,
            Dictionary<int, NetCalc.WellInfo> nodeWell,
            string[] nodeNameForTGF,
            string dirForTGF = null)
        {
            if (dirForTGF != null)
                foreach (var f in Directory.CreateDirectory(dirForTGF).EnumerateFiles())
                    if (f.Name.EndsWith(".tgf")) f.Delete();

            CalcRec.HydrCalcDataRec[] recs;

            using (new StopwatchMs("Calc on subnets"))
            {
                recs = CalcRec.CalcSubnets(edges, nodes, subnets, nodeWell,
                    iSubnet =>
                    {
#if DEBUG
                        Console.Write($"{iSubnet + 1}.");
#endif
                        if (dirForTGF == null)
                            return null;
                        return new StreamWriter(Path.Combine(dirForTGF, $"{iSubnet + 1}.tgf"));
                    },
                    iNode => nodeNameForTGF[iNode]
                );
#if DEBUG
                Console.WriteLine();
#endif
            }
            return recs;
        }

        static OracleConnection GetOraConn(string source, string username, string password)
        {
            var ocsb = new OracleConnectionStringBuilder();
            ocsb.DataSource = source;
            ocsb.UserID = username;
            ocsb.Password = password;
            ocsb.Pooling = true;
            ocsb.LoadBalancing = false;
            ocsb.HAEvents = false;
            return new OracleConnection(ocsb.ToString());
        }

        static Dictionary<ulong, NetCalc.WellInfo> LoadWellsData(NetCalc.WellKind whatToLoad)
        {
            //Dictionary<ulong, NetCalc.WellInfo> dictWellOp = new Dictionary<ulong, NetCalc.WellInfo>();
            using (new StopwatchMs("Load wells data from Well_OP"))
            using (var dbConnWellOP = GetOraConn(
                "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=pb.ssrv.tk)(PORT=1522))(CONNECT_DATA=(SERVICE_NAME=oralin2)))",
                "WELLOP", "WELLOP"))
            {
                using (new StopwatchMs("Conn opening"))
                    dbConnWellOP.Open();
                return PipeDataLoad.LoadWellsData(dbConnWellOP, whatToLoad);
            }
        }

        static HydrCalcData LoadHydrCalcData(Dictionary<ulong, NetCalc.WellInfo> dictWellOp)
        {
            using (new StopwatchMs("Load pipe graph data from OIS Pipe"))
            using (var dbConnOisPipe = GetOraConn(
                "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=probook)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=oralin)))",
                "pipe48", "pipe48"))
            {
                using (new StopwatchMs("Conn opening"))
                    dbConnOisPipe.Open();

                var data = PipeDataLoad.PrepareInputData(dbConnOisPipe, dictWellOp);

                return data;
            }
        }

        static object MyGet(this ObjectCache cache, string key) { try { return cache.Get(key); } catch { return null; } }

        [STAThread]
        static void Main(string[] args)
        {
            {
                var lstnr = new TextLogTraceListener("Trace.log");
                NetCalc.Logger.Listeners.Add(lstnr);
                System.Diagnostics.Trace.Listeners.Add(lstnr);
            }

            //OracleConfiguration.TraceFileLocation = @"C:\temp\traces";
            //OracleConfiguration.TraceLevel = 6;

            var CalcBeg_Time = DateTime.UtcNow;

            CalcRec.HydrCalcDataRec[] edgeRec;
            ulong[] edgeOisPipeID = null;

            try
            {
                var cache = new FileCache(nameof(FileCache), new PipeNetCalc.ObjectBinder())
                {
                    DefaultPolicy = new CacheItemPolicy() { AbsoluteExpiration = ObjectCache.InfiniteAbsoluteExpiration },
                    PayloadReadMode = FileCache.PayloadMode.Serializable,
                    PayloadWriteMode = FileCache.PayloadMode.Serializable,
                };

                var wellKinds = NetCalc.WellKind.Oil | NetCalc.WellKind.Water;

                var cacheKey = $"{nameof(HydrCalcData)}_{wellKinds}";
                var data = (HydrCalcData)cache.MyGet(cacheKey);

                if (data == null)
                {
                    var nodeWell = LoadWellsData(wellKinds);
                    data = LoadHydrCalcData(nodeWell);

                    cache[cacheKey] = data;
                    //cache.Flush();
                }

                edgeOisPipeID = data.edgeID;

                var subnets = GetSubnets(data.edges, data.nodes);

                edgeRec = HydrCalc(data.edges, data.nodes, subnets, data.nodeWell, data.nodeName, $"TGF {wellKinds}");

#if DEBUG
                { // todo: remove debug code
                    var usedWells = data.nodeWell.Where(p => p.Value.nUsed > 0).ToDictionary(p => p.Key, p => p.Value);
                    var unusedWells = data.nodeWell.Where(p => p.Value.nUsed == 0).ToDictionary(p => p.Key, p => p.Value);
                    //var iNode = Enumerable.Range(0, data.nodes.Length).Where(i => data.nodes[i].Node_ID == "5093939").First();
                    //var myEdges = data.edges.Where(e => unusedWells.ContainsKey(e.iNodeA) || unusedWells.ContainsKey(e.iNodeB)).ToArray();
                    var unusedWellNodeIDs = string.Join(",", unusedWells.Select(p => data.nodes[p.Key].Node_ID));
                    var usedWellNodeIDs = string.Join(",", usedWells.Select(p => data.nodes[p.Key].Node_ID));
                }
#endif
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                edgeRec = null;
            }

            return;

            const string csPPM = @"Data Source = alferovav; Initial Catalog = PPM.Ugansk.Test; User ID = ppm; Password = 123; Pooling = False";
            //обходим баг? загрузки "Microsoft.Data.SqlClient.resources"
            using (var c = new Microsoft.Data.SqlClient.SqlConnection(csPPM)) { c.Open(); c.Close(); }
            Guid Calc_ID = SaveToDB.CreateCalculationRec(csPPM, CalcBeg_Time, edgeRec);
            if (edgeRec != null)
                SaveToDB.SaveResults(csPPM, edgeRec, edgeOisPipeID, CalcBeg_Time, Calc_ID);
        }
    }

    class StopwatchMs : IDisposable
    {
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        string msg;
        public StopwatchMs(string msg) { this.msg = msg; Console.WriteLine($"{msg}: started"); sw.Start(); }
        public void Dispose() { sw.Stop(); Console.WriteLine($"{msg}: done in {sw.ElapsedMilliseconds}ms"); }
    }

    class TextLogTraceListener : System.Diagnostics.TraceListener
    {
        string fileName;
        System.Text.StringBuilder buf = new System.Text.StringBuilder();

        public TextLogTraceListener(string fileName) { this.fileName = fileName; }

        public override void Write(string message)
        {
            if (buf.Length > 0) buf.AppendFormat("\t{0}", message);
            else buf.Append(message);
        }

        public override void WriteLine(string message)
        {
            try
            {
                Write(message);
                string txt = string.Format("{0}\t{1}\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff"), buf);
                if (!string.IsNullOrEmpty(fileName))
                    System.IO.File.AppendAllText(fileName, txt);
                else Console.Write(txt);
                buf.Remove(0, buf.Length); // clear buf
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
        }
    }
}
