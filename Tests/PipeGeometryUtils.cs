using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pipe.Exercises
{
    public struct GeoPoint
    {
        public double[] coords;
        public GeoPoint(params double[] coords) { this.coords = coords; }
        public GeoPoint(params decimal?[] dbCoords)
        { coords = dbCoords.Select(c => c.HasValue ? Convert.ToDouble(c.Value) : double.NaN).ToArray(); }
        public bool WithNaN => coords.Any(double.IsNaN);
        static double Sqr(double x) => x * x;
        public static double SqrDistXY(GeoPoint a, GeoPoint b) => Sqr(a.coords[0] - b.coords[0]) + Sqr(a.coords[1] - b.coords[1]);
        public static double SqrDist(GeoPoint a, GeoPoint b) => a.coords.Zip(b.coords, (ca, cb) => Sqr(ca - cb)).Sum();
        public static readonly GeoPoint NaNs3 = new GeoPoint(double.NaN, double.NaN, double.NaN);
        public override string ToString() => string.Join(" ", coords);
    }

    public static class PipeGeometryUtils
    {
        static readonly System.Globalization.CultureInfo fmt = System.Globalization.CultureInfo.InvariantCulture;

        public static void PointToWKT(double[] p, StringBuilder sb, int maxDims = 3)
        {
            bool fc = true;
            int d = 0;
            foreach (var c in p)
            {
                if (fc)
                    fc = false;
                else sb.Append(' ');
                sb.Append(c.ToString(fmt));
                if (++d >= maxDims)
                    break;
            }
        }

        public static void LineToWKT(this IEnumerable<double[]> points, StringBuilder sb, int maxDims = 3)
        {
            bool first = true;
            foreach (var p in points)
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                bool fc = true;
                int d = 0;
                foreach (var c in p)
                {
                    if (fc)
                        fc = false;
                    else sb.Append(' ');
                    sb.Append(c.ToString(fmt));
                    if (++d >= maxDims)
                        break;
                }
            }
        }

        public static string AppendLog(this string str, string toAdd, string delimiter = "; ")
        {
            if (str != null && str.Length > 0)
                str += delimiter;
            return str + toAdd;
        }

        static double Pow2(double x) => x * x;
        static bool EqualCoords(double Am, double Bm) => Math.Abs(Am - Bm) < 0.001;

        public static (GeoPoint[] points, string errMsg) FromPipeCoords(this byte[] raw, double Length, double Z0, double Z1)
        {
            if (raw == null)
                return (points: Array.Empty<GeoPoint>(), errMsg: "coords NULL");

            if (raw.Length < 4)
                return (points: Array.Empty<GeoPoint>(), errMsg: "coords blobSize < 4");

            var maxL = Length * 3;
            var minL = Length * 0.8;

            using (var ms = new MemoryStream(raw))
            {
                var br = new BinaryReader(ms);
                int n = br.ReadInt32();

                string errMsg = double.IsNaN((double)Length) ? "MaxLength is NaN" : null;

                int calcSize = 4 + n * 16;
                if (calcSize < raw.Length)
                {
                    n = (raw.Length - 4) / 16;
                    errMsg = errMsg.AppendLog("coords calcSize < blobSize");
                }
                else if (calcSize > raw.Length)
                {
                    n = (raw.Length - 4) / 16;
                    errMsg = errMsg.AppendLog("coords calcSize > blobSize");
                }
                if (n < 2)
                    errMsg = errMsg.AppendLog("coords points count < 2");

                var res = new GeoPoint[n];

                var prevX = double.NaN;
                var prevY = double.NaN;
                int j = 0;

                bool withDups = false;
                bool withZero = false;
                bool withBurst = false;
                var SumL = 0d;

                for (int i = 0; i < n; i++)
                {
                    var x = br.ReadDouble();
                    var y = br.ReadDouble();

                    //if (prevX == x && prevY == y)
                    //{ withDups = true; continue; }

                    if (x == 0 && y == 0)
                    { withZero = true; continue; }

                    var p = new double[] { x, y, 0 };

                    if (j > 0)
                    {
                        if (Enumerable.Range(0, j).Any(k => EqualCoords(res[k].coords[0], x) && EqualCoords(res[k].coords[1], y)))
                        { withDups = true; continue; }

                        var L = Math.Sqrt(Pow2(x - prevX) + Pow2(y - prevY));
                        if (SumL + L > maxL)
                        {
                            withBurst = true;
                            continue;
                        }
                        SumL += L;
                    }

                    prevX = x; prevY = y;
                    res[j].coords = p;
                    j++;
                }
                if (withBurst)
                {
                    if (j < 2)
                        return (points: Array.Empty<GeoPoint>(), errMsg.AppendLog("!no points without bursts"));
                    errMsg = errMsg.AppendLog("possible burst(s) found");
                }

                if (SumL < minL)
                    errMsg = errMsg.AppendLog("Lcalc < Ldecl");
                else if (SumL > maxL)
                    errMsg = errMsg.AppendLog("Lcalc > Ldecl");

                if (j < n)
                    res = res.Take(j).ToArray();
                if (withDups)
                    errMsg = errMsg.AppendLog("duplicate point(s) found");
                if (withZero)
                    errMsg = errMsg.AppendLog("null point(s) found");

                if (res.Length > 0)
                {
                    var dL = 1 / SumL;
                    var L = 0d;

                    bool wrongZ = false;
                    if (Z0 < -12000 || Z0 > 12000)
                    {
                        wrongZ = true;
                        errMsg = errMsg.AppendLog("wrong Z of start node");
                    }
                    else res[0].coords[2] = Z0;

                    if (Z1 < -12000 || Z1 > 12000)
                    {
                        wrongZ = true;
                        errMsg = errMsg.AppendLog("wrong Z of end node");
                    }
                    else res[res.Length - 1].coords[2] = Z1;

                    if (!wrongZ && Z0 != 0 && Z1 != 0)
                        // interpolate Z between begin and end
                        for (int i = 1; i < res.Length - 1; i++)
                        {
                            L += Math.Sqrt(GeoPoint.SqrDistXY(res[i], res[i - 1]));
                            var w = L * dL;
                            var Z = (1 - w) * Z0 + w * Z1;
                            if (Z == 0)
                                Z = 1e-6;
                            res[i].coords[2] = Z;
                        }
                }

                return (points: res, errMsg);
            }
        }
    }
}
