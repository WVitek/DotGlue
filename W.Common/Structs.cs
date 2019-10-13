using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Serialization;

namespace W.Common
{
    [Serializable]
    public struct TimedObject : ITimedObject
    {
        public DateTime time, endTime;
        public object value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpObj(this, b); }
        #endregion

        #region IConvertible Members
        public TypeCode GetTypeCode() { return Utils.GetTypeCode(value); }
        public bool ToBoolean(IFormatProvider provider) { return Convert.ToBoolean(value, provider); }
        public byte ToByte(IFormatProvider provider) { return Convert.ToByte(value, provider); }
        public char ToChar(IFormatProvider provider) { return Convert.ToChar(value, provider); }
        public DateTime ToDateTime(IFormatProvider provider) { return Convert.ToDateTime(value, provider); }
        public decimal ToDecimal(IFormatProvider provider) { return Convert.ToDecimal(value, provider); }
        public double ToDouble(IFormatProvider provider) { return Convert.ToDouble(value, provider); }
        public short ToInt16(IFormatProvider provider) { return Convert.ToInt16(value, provider); }
        public int ToInt32(IFormatProvider provider) { return Convert.ToInt32(value, provider); }
        public long ToInt64(IFormatProvider provider) { return Convert.ToInt64(value, provider); }
        public sbyte ToSByte(IFormatProvider provider) { return Convert.ToSByte(value, provider); }
        public float ToSingle(IFormatProvider provider) { return Convert.ToSingle(value, provider); }
        public string ToString(IFormatProvider provider) { return value == null ? string.Empty : value.ToString(); }
        public object ToType(Type conversionType, IFormatProvider provider) { return Convert.ChangeType(value, conversionType, provider); }
        public ushort ToUInt16(IFormatProvider provider) { return Convert.ToUInt16(value, provider); }
        public uint ToUInt32(IFormatProvider provider) { return Convert.ToUInt32(value, provider); }
        public ulong ToUInt64(IFormatProvider provider) { return Convert.ToUInt64(value, provider); }
        #endregion

        TimedObject(DateTime time, DateTime endTime, object value)
        {
            System.Diagnostics.Debug.Assert(time <= endTime);
            this.time = time; this.endTime = endTime; this.value = value;
        }
        public override string ToString() { return Convert.ToString(value); }
        public static readonly TimedObject Empty = default(TimedObject);
        public static readonly TimedObject FullRange = new TimedObject(DateTime.MinValue, DateTime.MaxValue, null);
        public static readonly ITimedObject EmptyI = Empty;
        public static readonly ITimedObject FullRangeI = FullRange;

        #region Statics
        public static readonly DateTime NonZeroTime = DateTime.MinValue + TimeSpan.FromSeconds(1);
        static readonly IFormatProvider fmtProv = System.Globalization.CultureInfo.InvariantCulture;

        public static ITimedObject Timed(DateTime time, DateTime endTime, object value)
        {
            System.Diagnostics.Debug.Assert(time <= endTime);
            if (time > endTime)
                throw new ArgumentException("TimedObject.Timed: time > endTime");
            var to = value as ITimedObject;
            if (to != null && to.Time == time && to.EndTime == endTime)
                return to;
            var ic = value as IConvertible;
            if (ic != null)
                switch (ic.GetTypeCode())
                {
                    case TypeCode.DateTime:
                        return new TimedValue<DateTime>(time, endTime, ic.ToDateTime(fmtProv));
                    case TypeCode.String:
                        return new TimedString(time, endTime, ic.ToString(fmtProv));
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.Byte:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                        return new TimedInt64(time, endTime, ic.ToInt64(fmtProv));
                    case TypeCode.Single:
                        return new TimedSingle(time, endTime, ic.ToSingle(fmtProv));
                    case TypeCode.Double:
                        return new TimedDouble(time, endTime, ic.ToDouble(fmtProv));
                    case TypeCode.UInt64:
                    case TypeCode.Decimal:
                        var dec = ic.ToDecimal(fmtProv);
                        if (long.MinValue <= dec && dec <= long.MaxValue)
                            if (Math.Truncate(dec) == dec)
                                return new TimedInt64(time, endTime, (long)dec);
                            else
                                return new TimedDouble(time, endTime, (double)dec);
                        return new TimedDecimal(time, endTime, dec);
                }
            if (value is Guid g)
                return new TimedGuid(time, endTime, g);
            if (Utils.IsEmpty(value))
                return new TimedNull(time, endTime);
            return new TimedObject(time, endTime, value);
        }

        public static object TryAsTimed(object value, DateTime time, DateTime endTime)
        {
            var to = value as ITimedObject;
            if (to != null)
                return to;
            var ic = value as IConvertible;
            if (ic != null)
                switch (ic.GetTypeCode())
                {
                    case TypeCode.DateTime:
                        return new TimedValue<DateTime>(time, endTime, ic.ToDateTime(fmtProv));
                    case TypeCode.String:
                        return new TimedString(time, endTime, ic.ToString(fmtProv));
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.Byte:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                        return new TimedInt64(time, endTime, ic.ToInt64(fmtProv));
                    case TypeCode.Single:
                        return new TimedSingle(time, endTime, ic.ToSingle(fmtProv));
                    case TypeCode.Double:
                        return new TimedDouble(time, endTime, ic.ToDouble(fmtProv));
                    case TypeCode.UInt64:
                    case TypeCode.Decimal:
                        var dec = ic.ToDecimal(fmtProv);
                        if (long.MinValue <= dec && dec <= long.MaxValue)
                            if (Math.Truncate(dec) == dec)
                                return new TimedInt64(time, endTime, (long)dec);
                            else
                                return new TimedDouble(time, endTime, (double)dec);
                        return new TimedDecimal(time, endTime, dec);
                }
            if (value is Guid tg)
                return new TimedGuid(time, endTime, tg);
            if (time == NonZeroTime)
                return value;
            if (Utils.IsEmpty(value))
                return new TimedNull(time, endTime);
            return new TimedObject(time, endTime, value);
        }
        #endregion

        public static ITimedObject ValueInRange(ITimedObject value, ITimedObject range)
        {
            if (value == null)
                return null;
            if (range.EndTime <= value.Time || value.EndTime <= range.Time)
                if (range.EndTime != value.EndTime || range.Time != value.Time)
                    return null;
                else // equal
                    return value;
            bool below = value.Time < range.Time;
            bool above = range.EndTime < value.EndTime;
            if (below || above)
            {
                var time = below ? range.Time : value.Time;
                var endTime = above ? range.EndTime : value.EndTime;
                if (time == endTime)
                    time = endTime;
                return Timed(time, endTime, value.Object); ;
            }
            else return value;
        }

        public static TimedObject ValueInRange(TimedObject value, ITimedObject range)
        {
            if (range.EndTime < value.Time || value.EndTime <= range.Time)
                return TimedObject.Empty;
            bool below = value.Time < range.Time;
            bool above = range.EndTime < value.EndTime;
            if (below || above)
                return new TimedObject(below ? range.Time : value.Time, above ? range.EndTime : value.EndTime, value.Object);
            else return value;
        }

        public override int GetHashCode() { return (value == null) ? int.MinValue : value.GetHashCode(); }
    }

    [Serializable]
    public struct TimedNull : ITimedObject
    {
        public readonly DateTime time, endTime;

        public TimedNull(DateTime time, DateTime endTime) { this.time = time; this.endTime = endTime; }

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return null; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpTimed(this, b); }
        #endregion

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Empty; }
        public bool ToBoolean(IFormatProvider provider) { throw new NotSupportedException(); }
        public byte ToByte(IFormatProvider provider) { throw new NotSupportedException(); }
        public char ToChar(IFormatProvider provider) { throw new NotSupportedException(); }
        public DateTime ToDateTime(IFormatProvider provider) { throw new NotSupportedException(); }
        public decimal ToDecimal(IFormatProvider provider) { return 0; }
        public double ToDouble(IFormatProvider provider) { return double.NaN; }
        public short ToInt16(IFormatProvider provider) { return 0; }
        public int ToInt32(IFormatProvider provider) { return 0; }
        public long ToInt64(IFormatProvider provider) { return 0; }
        public sbyte ToSByte(IFormatProvider provider) { return 0; }
        public float ToSingle(IFormatProvider provider) { return float.NaN; }
        public string ToString(IFormatProvider provider) { return null; }
        public object ToType(Type conversionType, IFormatProvider provider) { throw new NotSupportedException(); }
        public ushort ToUInt16(IFormatProvider provider) { return 0; }
        public uint ToUInt32(IFormatProvider provider) { return 0; }
        public ulong ToUInt64(IFormatProvider provider) { return 0; }
        #endregion

        public override string ToString() { return string.Empty; }
    }

    public struct TimeRange : ITimedObject
    {
        public DateTime time, endTime;
        public TimeRange(DateTime time, DateTime endTime) { this.time = time; this.endTime = endTime; }

        public TimeRange(object o)
        {
            var to = o as ITimedObject ?? TimedObject.FullRangeI;
            time = to.Time;
            endTime = to.EndTime;
        }

        public TimeRange(object a, object b)
        {
            var ta = a as ITimedObject ?? TimedObject.FullRangeI;
            var tb = b as ITimedObject ?? TimedObject.FullRangeI;
            var t0 = (ta.Time < tb.Time) ? tb.Time : ta.Time;
            var t1 = (ta.EndTime < tb.EndTime) ? ta.EndTime : tb.EndTime;
            if (t0 < t1)
            {
                time = t0;
                endTime = t1;
            }
            else time = endTime = DateTime.MinValue;
        }

        public TimeRange(params object[] items)
        {
            var t0 = DateTime.MinValue;
            var t1 = DateTime.MaxValue;
            foreach (var item in items)
            {
                var to = item as ITimedObject ?? TimedObject.FullRangeI;
                if (t0 < to.Time)
                    t0 = to.Time;
                if (to.EndTime < t1)
                    t1 = to.EndTime;
            }
            if (t0 < t1)
            {
                time = t0;
                endTime = t1;
            }
            else time = endTime = DateTime.MinValue;
        }

        public TimeRange IntersectWith(DateTime dtMin, DateTime dtMax)
        { return Intersection(time, endTime, dtMin, dtMax); }

        public static TimeRange Intersection(DateTime tA0, DateTime tA1, DateTime tB0, DateTime tB1)
        {
            var t0 = (tA0 < tB0) ? tB0 : tA0;
            var t1 = (tA1 < tB1) ? tA1 : tB1;
            if (t0 < t1)
                return new TimeRange(t0, t1);
            else
                return new TimeRange();
        }

        #region ITimedObject interface
        DateTime ITimedObject.EndTime { get { return endTime; } }
        bool ITimedObject.IsEmpty { get { return endTime == DateTime.MinValue; ; } }
        object ITimedObject.Object { get { throw new NotSupportedException(); } }
        DateTime ITimedObject.Time { get { return time; } }
        public int CompareTimed(object b) { throw new NotSupportedException(); }
        #endregion

        #region IConvertible interface
        TypeCode IConvertible.GetTypeCode() { throw new NotSupportedException(); }
        bool IConvertible.ToBoolean(IFormatProvider provider) { throw new NotSupportedException(); }
        byte IConvertible.ToByte(IFormatProvider provider) { throw new NotSupportedException(); }
        char IConvertible.ToChar(IFormatProvider provider) { throw new NotSupportedException(); }
        DateTime IConvertible.ToDateTime(IFormatProvider provider) { throw new NotSupportedException(); }
        decimal IConvertible.ToDecimal(IFormatProvider provider) { throw new NotSupportedException(); }
        double IConvertible.ToDouble(IFormatProvider provider) { throw new NotSupportedException(); }
        short IConvertible.ToInt16(IFormatProvider provider) { throw new NotSupportedException(); }
        int IConvertible.ToInt32(IFormatProvider provider) { throw new NotSupportedException(); }
        long IConvertible.ToInt64(IFormatProvider provider) { throw new NotSupportedException(); }
        sbyte IConvertible.ToSByte(IFormatProvider provider) { throw new NotSupportedException(); }
        float IConvertible.ToSingle(IFormatProvider provider) { throw new NotSupportedException(); }
        string IConvertible.ToString(IFormatProvider provider) { throw new NotSupportedException(); }
        object IConvertible.ToType(Type conversionType, IFormatProvider provider) { throw new NotSupportedException(); }
        ushort IConvertible.ToUInt16(IFormatProvider provider) { throw new NotSupportedException(); }
        uint IConvertible.ToUInt32(IFormatProvider provider) { throw new NotSupportedException(); }
        ulong IConvertible.ToUInt64(IFormatProvider provider) { throw new NotSupportedException(); }
        #endregion
    }

    [Serializable]
    public struct TimedInt64 : ITimedObject, ITimedDouble
    {
        public DateTime time, endTime;
        public long value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b)
        {
            if (b is TimedInt64 tb)
            {
                //var tb = (TimedInt64)b;
                var r = Math.Sign(value - tb.value);
                if (r != 0)
                    return r;
                if (endTime < tb.time)
                    return -1;
                if (tb.endTime < time)
                    return +1;
                if (time == tb.time && endTime == tb.endTime)
                    return 0;
                if (time < tb.time)
                    return -1;
                if (time > tb.time)
                    return +1;
                return 0;
            }
            else return Cmp.CmpTimed(this, b);
        }

        #endregion

        double ITimedDouble.Value { get { return value; } }

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Int64; }
        public bool ToBoolean(IFormatProvider provider) { return value != 0; }
        public byte ToByte(IFormatProvider provider) { return (byte)value; }
        public char ToChar(IFormatProvider provider) { return (char)value; }
        public DateTime ToDateTime(IFormatProvider provider) { return Time; }
        public decimal ToDecimal(IFormatProvider provider) { return (decimal)value; }
        public double ToDouble(IFormatProvider provider) { return (double)value; }
        public short ToInt16(IFormatProvider provider) { return (Int16)value; }
        public int ToInt32(IFormatProvider provider) { return (Int32)value; }
        public long ToInt64(IFormatProvider provider) { return (Int64)value; }
        public sbyte ToSByte(IFormatProvider provider) { return (SByte)value; }
        public float ToSingle(IFormatProvider provider) { return (float)value; }
        public string ToString(IFormatProvider provider) { return value.ToString(provider); }
        public object ToType(Type conversionType, IFormatProvider provider) { return Convert.ChangeType(value, conversionType, provider); }
        public ushort ToUInt16(IFormatProvider provider) { return (UInt16)value; }
        public uint ToUInt32(IFormatProvider provider) { return (UInt32)value; }
        public ulong ToUInt64(IFormatProvider provider) { return (UInt64)value; }
        #endregion

        public TimedInt64(DateTime time, DateTime endTime, long value) { this.time = time; this.endTime = endTime; this.value = value; }
        public override string ToString()
        { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value); }
        public static readonly TimedInt64 Empty = default(TimedInt64);
        public override int GetHashCode() { return value.GetHashCode(); }
    }

    [Serializable]
    public struct TimedSingle : ITimedObject, ITimedDouble
    {
        public DateTime time, endTime;
        public float value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpObj(this, b); }
        #endregion

        double ITimedDouble.Value { get { return value; } }

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Single; }
        public bool ToBoolean(IFormatProvider provider) { return value != 0; }
        public byte ToByte(IFormatProvider provider) { return (byte)value; }
        public char ToChar(IFormatProvider provider) { return (char)value; }
        public DateTime ToDateTime(IFormatProvider provider) { return Time; }
        public decimal ToDecimal(IFormatProvider provider) { return (decimal)value; }
        public double ToDouble(IFormatProvider provider) { return (double)value; }
        public short ToInt16(IFormatProvider provider) { return (Int16)value; }
        public int ToInt32(IFormatProvider provider) { return (Int32)value; }
        public long ToInt64(IFormatProvider provider) { return (Int64)value; }
        public sbyte ToSByte(IFormatProvider provider) { return (SByte)value; }
        public float ToSingle(IFormatProvider provider) { return (float)value; }
        public string ToString(IFormatProvider provider) { return value.ToString(provider); }
        public object ToType(Type conversionType, IFormatProvider provider) { return Convert.ChangeType(value, conversionType, provider); }
        public ushort ToUInt16(IFormatProvider provider) { return (UInt16)value; }
        public uint ToUInt32(IFormatProvider provider) { return (UInt32)value; }
        public ulong ToUInt64(IFormatProvider provider) { return (UInt64)value; }
        #endregion

        public TimedSingle(DateTime time, DateTime endTime, float value) { this.time = time; this.endTime = endTime; this.value = value; }
        public override string ToString()
        { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value); }
        public static readonly TimedSingle Empty = default(TimedSingle);
        public override int GetHashCode() { return value.GetHashCode(); }
    }


    [Serializable]
    public struct TimedDouble : ITimedObject, ITimedDouble
    {
        public DateTime time, endTime;
        public double value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpObj(this, b); }
        #endregion

        double ITimedDouble.Value { get { return value; } }

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Double; }
        public bool ToBoolean(IFormatProvider provider) { return value != 0; }
        public byte ToByte(IFormatProvider provider) { return (byte)value; }
        public char ToChar(IFormatProvider provider) { return (char)value; }
        public DateTime ToDateTime(IFormatProvider provider) { return Time; }
        public decimal ToDecimal(IFormatProvider provider) { return (decimal)value; }
        public double ToDouble(IFormatProvider provider) { return (double)value; }
        public short ToInt16(IFormatProvider provider) { return (Int16)value; }
        public int ToInt32(IFormatProvider provider) { return (Int32)value; }
        public long ToInt64(IFormatProvider provider) { return (Int64)value; }
        public sbyte ToSByte(IFormatProvider provider) { return (SByte)value; }
        public float ToSingle(IFormatProvider provider) { return (float)value; }
        public string ToString(IFormatProvider provider) { return value.ToString(provider); }
        public object ToType(Type conversionType, IFormatProvider provider) { return Convert.ChangeType(value, conversionType, provider); }
        public ushort ToUInt16(IFormatProvider provider) { return (UInt16)value; }
        public uint ToUInt32(IFormatProvider provider) { return (UInt32)value; }
        public ulong ToUInt64(IFormatProvider provider) { return (UInt64)value; }
        #endregion

        public TimedDouble(DateTime time, DateTime endTime, double value) { this.time = time; this.endTime = endTime; this.value = value; }
        public override string ToString()
        { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value); }
        public static readonly TimedDouble Empty = default(TimedDouble);

        public static ITimedObject ValueInRange(ITimedObject value, ITimedObject range)
        {
            if (range.EndTime < value.Time || value.EndTime <= range.Time)
                return TimedDouble.Empty;
            bool below = value.Time < range.Time;
            bool above = range.EndTime < value.EndTime;
            if (below || above)
                return new TimedDouble(below ? range.Time : value.Time, above ? range.EndTime : value.EndTime, Convert.ToDouble(value));
            else return value;
        }
        public override int GetHashCode() { return value.GetHashCode(); }
    }

    [Serializable]
    public struct TimedDecimal : ITimedObject, ITimedDouble
    {
        public DateTime time, endTime;
        public decimal value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpTimed(this, b); }
        #endregion

        double ITimedDouble.Value { get { return (double)value; } }

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Double; }
        public bool ToBoolean(IFormatProvider provider) { return value != 0; }
        public byte ToByte(IFormatProvider provider) { return (byte)value; }
        public char ToChar(IFormatProvider provider) { return (char)value; }
        public DateTime ToDateTime(IFormatProvider provider) { return Time; }
        public decimal ToDecimal(IFormatProvider provider) { return (decimal)value; }
        public double ToDouble(IFormatProvider provider) { return (double)value; }
        public short ToInt16(IFormatProvider provider) { return (Int16)value; }
        public int ToInt32(IFormatProvider provider) { return (Int32)value; }
        public long ToInt64(IFormatProvider provider) { return (Int64)value; }
        public sbyte ToSByte(IFormatProvider provider) { return (SByte)value; }
        public float ToSingle(IFormatProvider provider) { return (float)value; }
        public string ToString(IFormatProvider provider) { return value.ToString(provider); }
        public object ToType(Type conversionType, IFormatProvider provider) { return Convert.ChangeType(value, conversionType, provider); }
        public ushort ToUInt16(IFormatProvider provider) { return (UInt16)value; }
        public uint ToUInt32(IFormatProvider provider) { return (UInt32)value; }
        public ulong ToUInt64(IFormatProvider provider) { return (UInt64)value; }
        #endregion

        public TimedDecimal(DateTime time, DateTime endTime, decimal value) { this.time = time; this.endTime = endTime; this.value = value; }
        public override string ToString()
        { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value); }
        public static readonly TimedDecimal Empty = default(TimedDecimal);
        public override int GetHashCode() { return value.GetHashCode(); }
    }

    [Serializable]
    public struct TimedString : ITimedObject
    {
        public DateTime time, endTime;
        public string value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return value == null || endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpTimed(this, b); }
        #endregion

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.String; }
        public bool ToBoolean(IFormatProvider provider) { return Convert.ToBoolean(value); }
        public byte ToByte(IFormatProvider provider) { return Convert.ToByte(value); }
        public char ToChar(IFormatProvider provider) { return Convert.ToChar(value); }
        public DateTime ToDateTime(IFormatProvider provider) { return Time; }
        public decimal ToDecimal(IFormatProvider provider) { return Convert.ToDecimal(value); }
        public double ToDouble(IFormatProvider provider) { return Convert.ToDouble(value); }
        public short ToInt16(IFormatProvider provider) { return Convert.ToInt16(value); }
        public int ToInt32(IFormatProvider provider) { return Convert.ToInt32(value); }
        public long ToInt64(IFormatProvider provider) { return Convert.ToInt64(value); }
        public sbyte ToSByte(IFormatProvider provider) { return Convert.ToSByte(value); }
        public float ToSingle(IFormatProvider provider) { return Convert.ToSingle(value); }
        public string ToString(IFormatProvider provider) { return value; }
        public object ToType(Type conversionType, IFormatProvider provider) { return Convert.ChangeType(value, conversionType, provider); }
        public ushort ToUInt16(IFormatProvider provider) { return Convert.ToUInt16(value); }
        public uint ToUInt32(IFormatProvider provider) { return Convert.ToUInt32(value); }
        public ulong ToUInt64(IFormatProvider provider) { return Convert.ToUInt64(value); }
        #endregion

        public TimedString(DateTime time, DateTime endTime, string value) { this.time = time; this.endTime = endTime; this.value = value; }
        public override string ToString() { return value; }
        public static readonly TimedString Empty = default(TimedString);
        public override int GetHashCode() { return value.GetHashCode(); }
    }

    [Serializable]
    public struct TimedBinary : ITimedObject
    {
        public static readonly byte[] EmptyValue = new byte[0];

        public DateTime time, endTime;
        public byte[] value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return time; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpTimed(this, b); }
        #endregion

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Object; }
        public bool ToBoolean(IFormatProvider provider) { return Convert.ToBoolean(value); }
        public byte ToByte(IFormatProvider provider) { return Convert.ToByte(value); }
        public char ToChar(IFormatProvider provider) { return Convert.ToChar(value); }
        public DateTime ToDateTime(IFormatProvider provider) { return Time; }
        public decimal ToDecimal(IFormatProvider provider) { return Convert.ToDecimal(value); }
        public double ToDouble(IFormatProvider provider) { return Convert.ToDouble(value); }
        public short ToInt16(IFormatProvider provider) { return Convert.ToInt16(value); }
        public int ToInt32(IFormatProvider provider) { return Convert.ToInt32(value); }
        public long ToInt64(IFormatProvider provider) { return Convert.ToInt64(value); }
        public sbyte ToSByte(IFormatProvider provider) { return Convert.ToSByte(value); }
        public float ToSingle(IFormatProvider provider) { return Convert.ToSingle(value); }
        public string ToString(IFormatProvider provider) { return (value == null) ? "[NULL]" : "byte[" + value.Length.ToString() + "]"; }
        public object ToType(Type conversionType, IFormatProvider provider) { return Convert.ChangeType(value, conversionType, provider); }
        public ushort ToUInt16(IFormatProvider provider) { return Convert.ToUInt16(value); }
        public uint ToUInt32(IFormatProvider provider) { return Convert.ToUInt32(value); }
        public ulong ToUInt64(IFormatProvider provider) { return Convert.ToUInt64(value); }
        #endregion

        public TimedBinary(DateTime time, DateTime endTime, byte[] value) { this.time = time; this.endTime = endTime; this.value = value; }

        public override string ToString()
        {
            if (value == null)
                return "[NULL]";
            return $"byte[{value.Length}]";
        }

        public static readonly TimedBinary Empty = default(TimedBinary);
        public override int GetHashCode() { return value.GetHashCode(); }
    }

    [Serializable]
    public struct TimedGuid : ITimedObject
    {
        public static readonly Guid EmptyValue = Guid.Empty;

        public DateTime time, endTime;
        public Guid value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return time; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b)
        {
            if (b is TimedGuid tg)
            {
                var r = value.CompareTo(tg.value);
                if (r != 0)
                    return r;
                if (endTime < tg.time)
                    return -1;
                if (tg.endTime < time)
                    return +1;
                if (time == tg.time && endTime == tg.endTime)
                    return 0;
                if (time < tg.time)
                    return -1;
                if (time > tg.time)
                    return +1;
                return 0;
            }
            else return Cmp.CmpTimed(this, b);
        }
        #endregion

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Object; }
        public bool ToBoolean(IFormatProvider provider) { throw new NotSupportedException(); }
        public byte ToByte(IFormatProvider provider) { throw new NotSupportedException(); }
        public char ToChar(IFormatProvider provider) { throw new NotSupportedException(); }
        public DateTime ToDateTime(IFormatProvider provider) { return Time; }
        public decimal ToDecimal(IFormatProvider provider) { throw new NotSupportedException(); }
        public double ToDouble(IFormatProvider provider) { throw new NotSupportedException(); }
        public short ToInt16(IFormatProvider provider) { throw new NotSupportedException(); }
        public int ToInt32(IFormatProvider provider) { throw new NotSupportedException(); }
        public long ToInt64(IFormatProvider provider) { throw new NotSupportedException(); }
        public sbyte ToSByte(IFormatProvider provider) { throw new NotSupportedException(); }
        public float ToSingle(IFormatProvider provider) { throw new NotSupportedException(); }
        public string ToString(IFormatProvider provider) { return ToString(); }
        public object ToType(Type conversionType, IFormatProvider provider) { throw new NotSupportedException(); }
        public ushort ToUInt16(IFormatProvider provider) { throw new NotSupportedException(); }
        public uint ToUInt32(IFormatProvider provider) { throw new NotSupportedException(); }
        public ulong ToUInt64(IFormatProvider provider) { throw new NotSupportedException(); }
        #endregion

        public TimedGuid(DateTime time, DateTime endTime, Guid value) { this.time = time; this.endTime = endTime; this.value = value; }

        public override string ToString()
        {
            if (value == null)
                return "[NULL]";
            return value.ToString();
        }

        public override int GetHashCode() { return value.GetHashCode(); }
    }

    [Serializable]
    public struct TimedValue<T> : ITimedObject where T : IConvertible
    {
        public DateTime time, endTime;
        public T value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpObj(this, b); }
        #endregion

        #region IConvertible Members
        public TypeCode GetTypeCode() { return value.GetTypeCode(); }
        public bool ToBoolean(IFormatProvider provider) { return value.ToBoolean(provider); }
        public byte ToByte(IFormatProvider provider) { return value.ToByte(provider); }
        public char ToChar(IFormatProvider provider) { return value.ToChar(provider); }
        public DateTime ToDateTime(IFormatProvider provider) { return value.ToDateTime(provider); }
        public decimal ToDecimal(IFormatProvider provider) { return value.ToDecimal(provider); }
        public double ToDouble(IFormatProvider provider) { return value.ToDouble(provider); }
        public short ToInt16(IFormatProvider provider) { return value.ToInt16(provider); }
        public int ToInt32(IFormatProvider provider) { return value.ToInt32(provider); }
        public long ToInt64(IFormatProvider provider) { return value.ToInt64(provider); }
        public sbyte ToSByte(IFormatProvider provider) { return value.ToSByte(provider); }
        public float ToSingle(IFormatProvider provider) { return value.ToSingle(provider); }
        public string ToString(IFormatProvider provider) { return value.ToString(provider); }
        public object ToType(Type conversionType, IFormatProvider provider) { return value.ToType(conversionType, provider); }
        public ushort ToUInt16(IFormatProvider provider) { return value.ToUInt16(provider); }
        public uint ToUInt32(IFormatProvider provider) { return value.ToUInt32(provider); }
        public ulong ToUInt64(IFormatProvider provider) { return value.ToUInt64(provider); }
        #endregion

        public TimedValue(DateTime time, DateTime endTime, T value) { this.time = time; this.endTime = endTime; this.value = value; }

        public override string ToString()
        { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value); }

        public static readonly TimedValue<T> Empty = default(TimedValue<T>);
        public override int GetHashCode() { return value.GetHashCode(); }
    }

    public struct Timed<T> : ITimedObject
    {
        public readonly DateTime time, endTime;
        public readonly T value;

        #region ITimedObject Members
        public DateTime Time { get { return time; } }
        public DateTime EndTime { get { return endTime; } }
        public object Object { get { return value; } }
        public bool IsEmpty { get { return endTime == DateTime.MinValue; } }
        public int CompareTimed(object b) { return Cmp.CmpObj(this, b); }
        #endregion

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Object; }
        public bool ToBoolean(IFormatProvider provider) { throw new NotSupportedException(); }
        public byte ToByte(IFormatProvider provider) { throw new NotSupportedException(); }
        public char ToChar(IFormatProvider provider) { throw new NotSupportedException(); }
        public DateTime ToDateTime(IFormatProvider provider) { throw new NotSupportedException(); }
        public decimal ToDecimal(IFormatProvider provider) { throw new NotSupportedException(); }
        public double ToDouble(IFormatProvider provider) { throw new NotSupportedException(); }
        public short ToInt16(IFormatProvider provider) { throw new NotSupportedException(); }
        public int ToInt32(IFormatProvider provider) { throw new NotSupportedException(); }
        public long ToInt64(IFormatProvider provider) { throw new NotSupportedException(); }
        public sbyte ToSByte(IFormatProvider provider) { throw new NotSupportedException(); }
        public float ToSingle(IFormatProvider provider) { throw new NotSupportedException(); }
        public string ToString(IFormatProvider provider) { throw new NotSupportedException(); }
        public object ToType(Type conversionType, IFormatProvider provider) { throw new NotSupportedException(); }
        public ushort ToUInt16(IFormatProvider provider) { throw new NotSupportedException(); }
        public uint ToUInt32(IFormatProvider provider) { throw new NotSupportedException(); }
        public ulong ToUInt64(IFormatProvider provider) { throw new NotSupportedException(); }
        #endregion

        public Timed(DateTime time, DateTime endTime, T value) { this.time = time; this.endTime = endTime; this.value = value; }

        public override string ToString()
        {
            var sBeg = (time > DateTime.MinValue) ? Utils.ToStr(time) : null;
            var sEnd = (time < DateTime.MaxValue) ? Utils.ToStr(endTime) : null;
            var sVal = Utils.ToString(value, true);
            if (sEnd != null)
                return "[" + sBeg + ".." + sEnd + "] " + sVal;
            if (sBeg == null)
                return sVal;
            return "[" + sBeg + "] " + sVal;
        }

        public static readonly Timed<T> Empty = default(Timed<T>);
        public override int GetHashCode() { return value.GetHashCode(); }
    }

    public class ErrorWrapper : IConvertible
    {
        public readonly Exception Ex;

        public ErrorWrapper(Exception ex) { Ex = ex; }

        public override string ToString() { return Ex.ToString(); }

        #region IConvertible Members
        public TypeCode GetTypeCode() { return TypeCode.Empty; }
        public bool ToBoolean(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public byte ToByte(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public char ToChar(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public DateTime ToDateTime(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public decimal ToDecimal(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public double ToDouble(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public short ToInt16(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public int ToInt32(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public long ToInt64(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public sbyte ToSByte(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public float ToSingle(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public string ToString(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public object ToType(Type conversionType, IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public ushort ToUInt16(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public uint ToUInt32(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        public ulong ToUInt64(IFormatProvider provider) { throw Ex.PrepareForRethrow(); }
        #endregion
    }
}