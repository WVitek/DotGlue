using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using PipeNetCalc;

namespace PPM.HydrCalcPipe
{
    public class SaveToDB
    {
        static void WriteTaskInfo() { }

        public static void SaveResults(string connStr, PipesCalc.HydrCalcDataRec[] recs, ulong[] edgesIDs, DateTime Calc_Time, Guid Calculation_ID)
        {
            var dictO2P = new Dictionary<ulong, Guid>();

            using (new StopwatchMs("Загрузка справочника преобразования ID OIS в ID PPM"))
            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT OIS_ID, PPM_ID FROM OIS_2_PPM WHERE OIS_TABLE_NAME = 'PipeProstoyUchastok'";
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                            dictO2P[Convert.ToUInt64(rdr[0])] = rdr.GetGuid(1);
                    }
                }
            }

            using (new StopwatchMs("Bulk save into HYDRAULIC_CALCULATION_RESULT"))
            using (var loader = new Microsoft.Data.SqlClient.SqlBulkCopy(connStr))
            {
                loader.DestinationTableName = "HYDRAULIC_CALCULATION_RESULT";
                //loader.BatchSize = 1;
                var reader = new BulkDataReader<PipesCalc.HydrCalcDataRec>(recs, (iEdge, r, vals) =>
                {
                    int i = 0;
                    var pu_id = edgesIDs[iEdge];
                    vals[i++] = dictO2P.TryGetValue(pu_id, out var g) ? g : Guid.Empty;
                    vals[i++] = Calc_Time;
                    vals[i++] = Calculation_ID;
                    r.GetValues(vals, ref i);
                }, 40);

                loader.WriteToServer(reader);
            }
        }

        internal static Guid CreateCalculationRec(string connStr, DateTime CalcBeg_Time, PipesCalc.HydrCalcDataRec[] recs)
        {
            var hc = new PPM.Pipeline.Fact.ApiModels.Service.HydraulicCalculation()
            {
                Id = Guid.NewGuid(),
                StartCalculationTime = CalcBeg_Time,
                StopCalculationTime = DateTime.UtcNow,
                SegmentsCount = (recs == null) ? -1 : recs.Length,
            };

            if (recs != null)
                foreach (var r in recs)
                    switch (r.CalcStatus)
                    {
                        case PipesCalc.CalcStatus.Success: hc.CalculatedCount++; break;
                        case PipesCalc.CalcStatus.Failed: hc.ErrorsCount++; break;
                    }

            using (new StopwatchMs("Save into HYDRAULIC_CALCULATION"))
            using (var loader = new Microsoft.Data.SqlClient.SqlBulkCopy(connStr))
            {
                loader.DestinationTableName = "HYDRAULIC_CALCULATION";

                var reader = new BulkDataReader<int>(new[] { 0 }, (_, j, vals) =>
                {
                    int i = 0;
                    vals[i++] = hc.Id;
                    vals[i++] = hc.StartCalculationTime;
                    vals[i++] = hc.StopCalculationTime;
                    vals[i++] = hc.CalculationStatusRd;
                    vals[i++] = hc.PipesCount;
                    vals[i++] = hc.SegmentsCount;
                    vals[i++] = hc.CalculatedCount;
                    vals[i++] = hc.ErrorsCount;
                    vals[i++] = hc.WithSheduler;
                    vals[i++] = hc.Initiator;
                    vals[i++] = Guid.Empty; // FILE_ID
                }, 11);

                loader.WriteToServer(reader);
            }

            return hc.Id;
        }

        //internal static void UpdateCalculation(HydralogyCalculationResult result, DateTime utcNow, string v)
        //{
        //    throw new NotImplementedException();
        //}
    }

    public class BulkDataReader<T> : System.Data.IDataReader
    {
        IEnumerator<T> recs;
        readonly Action<uint, T, object[]> getValues;
        uint iRec;
        object[] vals;

        void FillVals()
        {
            for (int i = 0; i < vals.Length; i++) vals[i] = null;
            getValues(iRec, recs.Current, vals);
        }

        public BulkDataReader(IEnumerable<T> recs, Action<uint, T, object[]> getValues, int nFields)
        {
            this.recs = recs.GetEnumerator();
            this.getValues = getValues;
            vals = new object[nFields];
        }

        public object GetValue(int i) => vals[i];
        public bool Read() { if (recs.MoveNext()) { FillVals(); iRec++; return true; } else return false; }
        public int FieldCount => vals.Length;
        public void Dispose() { recs.Dispose(); }

        public object this[int i] => throw new NotImplementedException();
        public object this[string name] => throw new NotImplementedException();
        public int Depth => throw new NotImplementedException();
        public bool IsClosed => throw new NotImplementedException();
        public int RecordsAffected => throw new NotImplementedException();
        public void Close() { }
        public bool GetBoolean(int i) { throw new NotImplementedException(); }
        public byte GetByte(int i) { throw new NotImplementedException(); }
        public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length) { throw new NotImplementedException(); }
        public char GetChar(int i) { throw new NotImplementedException(); }
        public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length) { throw new NotImplementedException(); }
        public IDataReader GetData(int i) { throw new NotImplementedException(); }
        public string GetDataTypeName(int i) { throw new NotImplementedException(); }
        public DateTime GetDateTime(int i) { throw new NotImplementedException(); }
        public decimal GetDecimal(int i) { throw new NotImplementedException(); }
        public double GetDouble(int i) { throw new NotImplementedException(); }
        public Type GetFieldType(int i) { throw new NotImplementedException(); }
        public float GetFloat(int i) { throw new NotImplementedException(); }
        public Guid GetGuid(int i) { throw new NotImplementedException(); }
        public short GetInt16(int i) { throw new NotImplementedException(); }
        public int GetInt32(int i) { throw new NotImplementedException(); }
        public long GetInt64(int i) { throw new NotImplementedException(); }
        public string GetName(int i) { throw new NotImplementedException(); }
        public int GetOrdinal(string name) { throw new NotImplementedException(); }
        public DataTable GetSchemaTable() { throw new NotImplementedException(); }
        public string GetString(int i) { throw new NotImplementedException(); }
        public int GetValues(object[] values) { throw new NotImplementedException(); }
        public bool IsDBNull(int i) { throw new NotImplementedException(); }
        public bool NextResult() { throw new NotImplementedException(); }
    }

}
