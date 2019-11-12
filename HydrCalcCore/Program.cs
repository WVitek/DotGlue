using PipeNetCalc;
using System;
using System.Collections.Generic;
using System.IO;
using Oracle.ManagedDataAccess.Client;
using System.Runtime.Caching;

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
                        Console.Write($" {iSubnet}");
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

        static Dictionary<ulong, NetCalc.WellInfo> LoadWellsData()
        {
            //Dictionary<ulong, NetCalc.WellInfo> dictWellOp = new Dictionary<ulong, NetCalc.WellInfo>();
            using (new StopwatchMs("Load wells data from Well_OP"))
            using (var dbConnWellOP = GetOraConn(
                "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=pb.ssrv.tk)(PORT=1522))(CONNECT_DATA=(SERVICE_NAME=oralin2)))",
                "WELLOP", "WELLOP"))
            {
                using (new StopwatchMs("Conn opening"))
                    dbConnWellOP.Open();
                return PipeDataLoad.LoadWellsData(dbConnWellOP);
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
            //OracleConfiguration.TraceFileLocation = @"C:\temp\traces";
            //OracleConfiguration.TraceLevel = 6;

            var CalcBeg_Time = DateTime.UtcNow;

            CalcRec.HydrCalcDataRec[] edgeRec;

            try
            {
                var cache = new FileCache(nameof(FileCache), new PipeNetCalc.ObjectBinder())
                {
                    DefaultPolicy = new CacheItemPolicy() { AbsoluteExpiration = ObjectCache.InfiniteAbsoluteExpiration },
                    PayloadReadMode = FileCache.PayloadMode.Serializable,
                    PayloadWriteMode = FileCache.PayloadMode.Serializable,
                };

                var data = (HydrCalcData)cache.MyGet(nameof(HydrCalcData));

                if (data == null)
                {
                    var nodeWell = LoadWellsData();
                    data = LoadHydrCalcData(nodeWell);
                    cache[nameof(HydrCalcData)] = data;
                    //cache.Flush();
                }

                var subnets = GetSubnets(data.edges, data.nodes);

                //edgeRec = HydrCalc(edges, nodes, subnets, nodeWell, nodeName, "TGF");
                edgeRec = HydrCalc(data.edges, data.nodes, subnets, data.nodeWell, data.nodeName, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                edgeRec = null;
            }

            //const string connStr = @"Data Source = alferovav; Initial Catalog = PPM.Ugansk.Test; Trusted_Connection=True";
            //const string csPPM = @"Data Source = alferovav; Initial Catalog = PPM.Ugansk.Test; User ID = ppm; Password = 123; Pooling = False";
            // обходим баг? загрузки "Microsoft.Data.SqlClient.resources"
            //using (var c = new Microsoft.Data.SqlClient.SqlConnection(csPPM)) { c.Open(); c.Close(); }
            //Guid Calc_ID = SaveToDB.CreateCalculationRec(csPPM, CalcBeg_Time, edgeRec);
            //if (edgeRec != null)
            //    SaveToDB.SaveResults(csPPM, edgeRec, edgeOisPipeID, CalcBeg_Time, Calc_ID);
        }
    }

    class StopwatchMs : IDisposable
    {
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        string msg;
        public StopwatchMs(string msg) { this.msg = msg; Console.WriteLine($"{msg}: started"); sw.Start(); }
        public void Dispose() { sw.Stop(); Console.WriteLine($"{msg}: done in {sw.ElapsedMilliseconds}ms"); }
    }

}
