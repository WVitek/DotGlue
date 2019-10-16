//#define OCP_LOG

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
//using Oracle.ManagedDataAccess.Client;
using System.Data;
using W.Common;
using System.Data.Common;

namespace W.Expressions.Sql
{
    /// <summary>
    /// Oracle connections async pool
    /// </summary>
    public class DbConnPool : IDbConn
    {
        class OneConn
        {
            public DbConnection conn;
            public DbTransaction trans;
            DateTime expiractionTime;

            public DbTransaction GetTransaction(TimeSpan autoCommitPeriod)
            {
                if (trans != null && expiractionTime < DateTime.UtcNow)
                {
                    trans.Commit();
                    trans.Dispose();
                    trans = null;
                }
                if (trans == null)
                {
                    expiractionTime = (autoCommitPeriod == TimeSpan.MaxValue) ? DateTime.MaxValue : DateTime.UtcNow + autoCommitPeriod;
                    trans = conn.BeginTransaction();
                }
                return trans;
            }

            public bool Commit()
            {
                if (trans != null)
                { trans.Commit(); trans = null; return true; }
                else return false;
            }
        }

        class GrabbedConn : IDbConn, IDisposable
        {
            IAsyncLock gate = W.Common.Utils.NewAsyncLock();
            DbConnPool conn;
            int i;
            int disposed;

            internal GrabbedConn(DbConnPool conn, int i)
            {
                this.conn = conn;
                this.i = i;
            }

            public IDbmsSpecific dbms { get { return conn.dbms; } }

            public async Task<object> ExecCmd(SqlCommandData data, CancellationToken ct)
            {
                await gate.WaitAsync(ct);
                try
                {
                    if (disposed != 0)
                        throw new ObjectDisposedException(this.GetType().Name);
#if TRACE
                    return await conn.ExecCmdImplLogged(conn.Pool(i), data, ct);
#else
					return await conn.ExecCmdImpl(conn.Pool(i), data, ct);
#endif
                }
                finally { gate.Release(); }
            }

            public async Task<object> Commit(CancellationToken ct)
            {
                await gate.WaitAsync(ct);
                try
                {
                    if (disposed != 0)
                        throw new ObjectDisposedException(this.GetType().Name);
                    return conn.Pool(i).Commit();
                }
                finally { gate.Release(); }
            }

            public Task<IDbConn> GrabConn(CancellationToken ct)
            {
                throw new NotSupportedException("GrabbedConn.GrabConn()");
                //return Task.FromResult<IOraConn>(this);
            }

            public void Dispose()
            {
                if (W.Common.Utils.Once(ref disposed))
                {
                    W.Common.Utils.InterlockedReleaseIndex(ref conn.poolUsageBits, i);
                    conn.gate.Release();
                    i = -1;
                    conn = null;
                }
            }
        }

        IAsyncSemaphore gate;
        int poolUsageBits = 0;
        OneConn[] _pool;

        string connString;
        TimeSpan autoCommitPeriod;
        string[] initCmds;

        IDbmsSpecific dbms;

        IDbmsSpecific IDbConn.dbms { get { return dbms; } }

        public DbConnPool(IDbmsSpecific dbms, int nConnectionsInPool, string connString, TimeSpan autoCommitPeriod, params string[] initCmds)
        {
            System.Diagnostics.Trace.Assert(nConnectionsInPool <= 32);
            gate = W.Common.Utils.NewAsyncSemaphore(nConnectionsInPool);
            this.dbms = dbms;
            _pool = new OneConn[nConnectionsInPool];
            this.autoCommitPeriod = autoCommitPeriod;
            this.connString = connString;
            this.initCmds = initCmds;
            timer = new Timer(Maintenance, null, 0, 10000);
            iPrevNotNullConn = nConnectionsInPool;
        }

        System.Threading.Timer timer;
        int iPrevNotNullConn;
        int IsMaintenanceRunning;

        async void Maintenance(object state)
        {
            if (disposed != 0) return;
            if (Interlocked.CompareExchange(ref IsMaintenanceRunning, 1, 0) != 0)
                // maintenance is already running, do nothing
                return;
            // wait for at least one connection is not in use
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                if (disposed != 0) return;
                int i = 0;
                // search for unused connection in all slots
                while (i < _pool.Length)
                {
                    if (!W.Common.Utils.InterlockedTryCaptureIndex(ref poolUsageBits, i))
                        // last connection is probably used, do nothing
                        return;
                    if (_pool[i] != null)
                        // last unused connection is found
                        break;
                    // no connection found in i-th pool slot, try next slot
                    W.Common.Utils.InterlockedReleaseIndex(ref poolUsageBits, i);
                    i++;
                }
                int iPrev = iPrevNotNullConn;
                iPrevNotNullConn = i;
                if (i == _pool.Length)
                    // no connections found, do nothing
                    return;
                try
                {
                    if (i < iPrev)
                        // new connections created in pool from last call
                        return;
                    var c = _pool[i];
                    _pool[i] = null;
                    c.conn.Close();
                    c.conn.Dispose();
                    Diagnostics.Logger.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 1, "OraConnPool: _pool[{0}] is successfully disposed", i);
                }
                finally { W.Common.Utils.InterlockedReleaseIndex(ref poolUsageBits, i); }
            }
            finally
            {
                gate.Release();
                IsMaintenanceRunning = 0;
            }
        }

        OneConn Pool(int i)
        {
            var c = _pool[i];
            if (c == null)
            {
                c = new OneConn() { conn = dbms.GetConnection(connString) };
                try { c.conn.Open(); }
                catch (Exception ex) { throw ex; }
                if (initCmds.Length > 0)
                {
                    using (var cmd = c.conn.CreateCommand())
                    {
                        foreach (var initCmd in initCmds)
                        {
                            cmd.CommandText = initCmd;
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                _pool[i] = c;
            }
            return c;
        }

        IIndexedDict[] ReadGroupedRows(DbDataReader rdr)
        {
            int n = rdr.FieldCount;
            var key2ndx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                var key = rdr.GetName(i);
                key2ndx.Add(key, i);
            }
            var lst = new OPs.ListOf<IIndexedDict>();
            object currKey = null;
            var currValues = new List<object>[n - 1];
            for (int i = 1; i < n; i++)
                currValues[i - 1] = new List<object>();
            var values = new object[n];
            while (rdr.Read())
            {
                rdr.GetValues(values);
                bool keyChanged = currKey == null || W.Common.Cmp.CmpKeys(currKey, values[0]) != 0;
                if (keyChanged)
                {
                    if (currKey != null)
                    {   // store akkumulated values lists
                        var vals = new object[n];
                        vals[0] = currKey;
                        for (int i = 1; i < n; i++)
                        { vals[i] = currValues[i - 1].ToArray(); currValues[i - 1].Clear(); }
                        lst.Add(ValuesDictionary.New(vals, key2ndx));
                    }
                    currKey = values[0];
                }
                for (int i = 1; i < n; i++)
                    currValues[i - 1].Add(values[i]); // akkumulate values in lists
            }
            if (currKey != null)
            {   // store akkumulated values lists
                var vals = new object[n];
                vals[0] = currKey;
                for (int i = 1; i < n; i++)
                { vals[i] = currValues[i - 1].ToArray(); currValues[i - 1].Clear(); }
                lst.Add(ValuesDictionary.New(vals, key2ndx));
            }
            return lst.ToArray();
        }

        async Task<object> ExecCmdImplLogged(OneConn oc, SqlCommandData data, CancellationToken ct)
        {
            var sbSql = new System.Text.StringBuilder(data.SqlText);
            sbSql.Replace('\r', ' ').Replace('\n', ' ');
            int L;
            do
            {
                L = sbSql.Length;
                sbSql.Replace("  ", " ");
            } while (L != sbSql.Length);

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            try
            {
                var res = await ExecCmdImpl(oc, data, ct);
                var lst = res as IList;
                sbSql.Insert(0, string.Format("{0}\t{1}\t", sw.ElapsedMilliseconds, (lst == null) ? -1 : lst.Count));
                Diagnostics.Logger.TraceEvent(System.Diagnostics.TraceEventType.Information, 1, sbSql.ToString());
                //System.Diagnostics.Trace.WriteLine(sbSql.ToString());
                return res;
            }
            catch (DbException ex)
            {
                sbSql.Insert(0, string.Format("{0}\tERR\t", ex.Message));
                Diagnostics.Logger.TraceEvent(System.Diagnostics.TraceEventType.Error, 1, sbSql.ToString());
                throw;
            }
        }

        async Task<object> ExecCmdImpl(OneConn oc, SqlCommandData data, CancellationToken ct)
        {
            int sum = 0;
            foreach (var dbCmd in dbms.GetSpecificCommands(oc.conn, data))
                using (dbCmd)
                    try
                    {
                        dbCmd.Transaction = oc.GetTransaction(autoCommitPeriod);
                        try
                        {
                            dbCmd.CommandText = data.SqlText;
                            if (data.Kind == CommandKind.NonQuery)
                            {   // For MS SQL can be multiple INSERT commands due rows count limitation in one INSERT
                                sum += dbCmd.ExecuteNonQuery();
                                continue;
                            }
                        }
                        catch (Exception ex) { throw ex; }

                        if (data.Kind == CommandKind.GetSchema)
                            using (var rdr = dbCmd.ExecuteReader(System.Data.CommandBehavior.SchemaOnly))
                                return rdr.GetSchemaTable();
                        using (var rdr = dbCmd.ExecuteReader())// (await SafeGetRdr(oraCmd, ct))
                        {
                            if (data.ConvertMultiResultsToLists)
                                return ReadGroupedRows(rdr);
                            return ReadRows(rdr);
                        }
                    }
                    finally { oc.Commit(); }

            return sum; // return from NonQuery
            //throw new InvalidOperationException("You re not supposed to be here)");
        }

        static object Minimize(object val)
        {
            if (val is decimal == false)
                return val;
            var dec = (decimal)val;
            if (long.MinValue <= dec && dec <= long.MaxValue)
                if (Math.Truncate(dec) == dec)
                    return (long)dec;
                else
                    return (double)dec;
            return val;
        }

        private static object ReadRows(DbDataReader rdr)
        {
            int nFields = rdr.FieldCount;
            var key2ndx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var lstUsedFields = new List<int>(nFields);
            int iStartTimeField = -1;
            int iEndTimeField = -1;
            for (int i = 0; i < nFields; i++)
            {
                var key = rdr.GetName(i);
                if (string.Compare(key, nameof(START_TIME), StringComparison.InvariantCultureIgnoreCase) == 0)
                { iStartTimeField = i; continue; }
                else if (iStartTimeField >= 0 && (
                    string.Compare(key, "end_time__dt", StringComparison.InvariantCultureIgnoreCase) == 0
                    || string.Compare(key, nameof(END_TIME), StringComparison.InvariantCultureIgnoreCase) == 0
                ))
                { iEndTimeField = i; continue; }
                key2ndx.Add(key, lstUsedFields.Count);
                lstUsedFields.Add(i);
            }

            var usedFields = lstUsedFields.ToArray();
            int nUsedFields = usedFields.Length;

            var lst = new OPs.ListOf<IIndexedDict>();
            if (iEndTimeField >= 0)
            {
                var values = new object[nFields];
                while (rdr.Read())
                {
                    rdr.GetValues(values);
                    var endTime = values[iEndTimeField];
                    var vals = new object[nUsedFields];
                    var dtBeg = (DateTime)values[iStartTimeField];
                    var dtEnd = (endTime == DBNull.Value) ? DateTime.MaxValue : (DateTime)endTime;
                    if (dtBeg == dtEnd)
                        dtBeg = dtEnd;
                    //vals[0] = TimedObject.Timed(dtBeg, dtEnd, values[0]);
                    for (int i = 0; i < nUsedFields; i++)
                        vals[i] = TimedObject.Timed(dtBeg, dtEnd, values[usedFields[i]]);
                    lst.Add(new ValuesDictionary(vals, key2ndx, dtBeg, dtEnd));
                }
            }
            else if (iStartTimeField >= 0)
            {
                var values = new object[nFields];
                object[] prevs = null;
                var prevTime = DateTime.MinValue;
                while (rdr.Read())
                {
                    rdr.GetValues(values);
                    var time = (DateTime)values[iStartTimeField];
                    if (prevs != null)
                    {
                        var dtBeg = prevTime;
                        var dtEnd = (Cmp.CmpKeys(prevs[0], values[0]) == 0) ? time : DateTime.MaxValue;
                        if (dtBeg == dtEnd)
                            continue;
                        for (int i = 0; i < nUsedFields; i++)
                            prevs[i] = TimedObject.Timed(dtBeg, dtEnd, prevs[i]);
                        lst.Add(new ValuesDictionary(prevs, key2ndx, dtBeg, dtEnd));
                    }
                    prevTime = time;
                    var vals = new object[nUsedFields];
                    for (int i = 0; i < nUsedFields; i++)
                        vals[i] = values[usedFields[i]];
                    prevs = vals;
                }
                if (prevs != null)
                {
                    for (int i = 0; i < nUsedFields; i++)
                        prevs[i] = TimedObject.Timed(prevTime, DateTime.MaxValue, prevs[i]);
                    lst.Add(new ValuesDictionary(prevs, key2ndx, prevTime, DateTime.MaxValue));
                }
            }
            else
            {
                System.Diagnostics.Trace.Assert(nFields == nUsedFields);
                while (rdr.Read())
                {
                    var values = new object[nFields];
                    rdr.GetValues(values);
                    for (int i = values.Length - 1; i >= 0; i--)
                        values[i] = Minimize(values[i]);
                    lst.Add(ValuesDictionary.New(values, key2ndx));
                }
            }
#if OCP_LOG
					using (var writer = System.IO.File.AppendText("OraConnPool.log"))
					{
						writer.WriteLine(string.Join("\t", key2ndx.Keys));
						foreach (var vals in lst)
							writer.WriteLine(string.Join("\t", vals.ValuesList));
						writer.Flush();
					}
#endif
            return lst.ToArray();
        }

        private async Task<DbDataReader> SafeGetRdr(DbCommand oraCmd, CancellationToken ct)
        {
            int nTries = 2;
            while (--nTries > 0)
                try { return oraCmd.ExecuteReader(); }
                catch (DbException ex)
                {
                    // todo: Oracle specific error handling
                    if (ex.ErrorCode == 02020 && oraCmd.Transaction != null)
                    //if (ex.Number == 02020 && oraCmd.Transaction != null)
                    {
                        await Task.FromResult(string.Empty);
                        oraCmd.Transaction.Commit();
                        continue;
                    }
                    throw;
                }
            return oraCmd.ExecuteReader();
        }

        public static int UnusedConnections = int.MaxValue;

        public async Task<object> ExecCmd(SqlCommandData data, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                int i = W.Common.Utils.InterlockedCaptureIndex(ref poolUsageBits, _pool.Length - 1);
                if (i < UnusedConnections)
                    UnusedConnections = i;
                try
#if TRACE
                { return await ExecCmdImplLogged(Pool(i), data, ct); }
#else
				{ return await ExecCmdImpl(Pool(i), data, ct); }
#endif
                finally
                { W.Common.Utils.InterlockedReleaseIndex(ref poolUsageBits, i); }
            }
            finally { gate.Release(); }
        }

        public async Task<IDbConn> GrabConn(CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            int i = W.Common.Utils.InterlockedCaptureIndex(ref poolUsageBits, _pool.Length - 1);
            if (i < UnusedConnections)
                UnusedConnections = i;
            return new GrabbedConn(this, i);
        }

        IAsyncLock commitGate = W.Common.Utils.NewAsyncLock();

        public async Task<object> Commit(CancellationToken ct)
        {
            await commitGate.WaitAsync(ct);
            try
            {
                foreach (var c in _pool)
                    await gate.WaitAsync(ct);
                try
                {
                    autoCommitPeriod = TimeSpan.MaxValue;
                    bool any = false;
                    foreach (var c in _pool)
                        if (c != null)
                        {
                            var tmp = c.Commit();
                            any = any || tmp;
                        }
                    return any;
                }
                finally { gate.Release(_pool.Length); }
            }
            finally { commitGate.Release(); }
        }

        int disposed;

        public void Dispose()
        {
            if (W.Common.Utils.Once(ref disposed))
            {
                // todo: dispose OraConnPool
                timer.Dispose();
                Diagnostics.Logger.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 2, "OraConnPool:Dispose() called");
                for (int i = 0; i < _pool.Length; i++)
                {
                    OneConn c = _pool[i];
                    _pool[i] = null;
                    if (c == null) continue;
                    try { c.conn.Dispose(); }
                    catch { }
                }
            }
        }
    }

}