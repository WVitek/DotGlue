using PipeNetCalc;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Oracle.ManagedDataAccess.Client;

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

        static void InitOraConn(this OracleConnection conn)
        {
            var initCmds = new string[]
            {
                //"ALTER SESSION SET NLS_TERRITORY = cis"
                //, "ALTER SESSION SET CURSOR_SHARING = SIMILAR"
                //, "ALTER SESSION SET NLS_NUMERIC_CHARACTERS ='. '"
                //, "ALTER SESSION SET NLS_COMP = ANSI"
                //, "ALTER SESSION SET NLS_SORT = BINARY"
            };
            conn.Open();
            using (var cmd = conn.CreateCommand())
                foreach (var initCmd in initCmds)
                {
                    cmd.CommandText = initCmd;
                    cmd.ExecuteNonQuery();
                }
        }

        static void Main(string[] args)
        {
            OracleConfiguration.TraceFileLocation = @"C:\temp\traces";
            OracleConfiguration.TraceLevel = 7;

            var CalcBeg_Time = DateTime.UtcNow;

            const string csPipe = "POOLING=False;USER ID=pipe48;DATA SOURCE=\"(DESCRIPTION = (ADDRESS = (PROTOCOL = TCP)(HOST = 10.79.50.70)(PORT = 1521))(CONNECT_DATA = (SERVICE_NAME = oralin)))\";LOAD BALANCING=False;PASSWORD=pipe48;HA EVENTS=False;Connection Timeout=600";
            const string csWellOP = "POOLING=False;USER ID=WELLOP;DATA SOURCE=\"(DESCRIPTION = (ADDRESS = (PROTOCOL = TCP)(HOST = 10.79.50.70)(PORT = 1522))(CONNECT_DATA = (SERVICE_NAME = oralin2)))\";LOAD BALANCING=False;PASSWORD=WELLOP;HA EVENTS=False;Connection Timeout=600";

            CalcRec.HydrCalcDataRec[] edgeRec;
            ulong[] edgeOisPipeID = null;

            using (var dbConnOisPipe = new OracleConnection(csPipe))
            using (var dbConnWellOP = new OracleConnection(csWellOP))
            {
                using (new StopwatchMs("Opening Oracle connections"))
                {
                    dbConnOisPipe.InitOraConn();
                    dbConnWellOP.InitOraConn();
                }

                try
                {
                    var (edges, nodes, colorName, nodeName, edgeID, nodeWell) = PipeDataLoad.PrepareInputData(dbConnOisPipe, dbConnWellOP);
                    //var (edges, nodes, colorName, nodeName, edgeID, nodeWell) = PrepareInputData();

                    var subnets = GetSubnets(edges, nodes);

                    edgeRec = HydrCalc(edges, nodes, subnets, nodeWell, nodeName, "TGF");
                    //edgeRec = HydrCalc(edges, nodes, subnets, nodeWell, nodeName, null);
                    edgeOisPipeID = edgeID;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                    edgeRec = null;
                }
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
