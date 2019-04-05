using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace W.Expressions
{
    //public abstract class TimedObject : ITimedObject, IConvertible
    //{
    //    public abstract DateTime Time { get; }
    //    public abstract object Object { get; }
    //    public virtual bool IsEmpty { get { return false; } }
    //    // static
    //    public static TimedObject Empty = new OneTimedObject(DateTime.MinValue, null);
    //    // IConvertible
    //    public TypeCode GetTypeCode()
    //    { return TypeCode.Object; }
    //    public bool ToBoolean(IFormatProvider provider)
    //    { return Convert.ToBoolean(Object); }
    //    public byte ToByte(IFormatProvider provider)
    //    { return Convert.ToByte(Object); }
    //    public char ToChar(IFormatProvider provider)
    //    { return Convert.ToChar(Object); }
    //    public DateTime ToDateTime(IFormatProvider provider)
    //    { return Time; }
    //    public decimal ToDecimal(IFormatProvider provider)
    //    { return Convert.ToDecimal(Object); }
    //    public double ToDouble(IFormatProvider provider)
    //    { return IsEmpty ? double.NaN : Convert.ToDouble(Object); }
    //    public short ToInt16(IFormatProvider provider)
    //    { return Convert.ToInt16(Object); }
    //    public int ToInt32(IFormatProvider provider)
    //    { return Convert.ToInt32(Object); }
    //    public long ToInt64(IFormatProvider provider)
    //    { return Convert.ToInt64(Object); }
    //    public sbyte ToSByte(IFormatProvider provider)
    //    { return Convert.ToSByte(Object); }
    //    public float ToSingle(IFormatProvider provider)
    //    { return IsEmpty ? float.NaN : Convert.ToSingle(Object); }
    //    public string ToString(IFormatProvider provider)
    //    { return Convert.ToString(Object); }
    //    public object ToType(Type conversionType, IFormatProvider provider)
    //    { return Convert.ChangeType(Object, conversionType, provider); }
    //    public ushort ToUInt16(IFormatProvider provider)
    //    { return Convert.ToUInt16(Object); }
    //    public uint ToUInt32(IFormatProvider provider)
    //    { return Convert.ToUInt32(Object); }
    //    public ulong ToUInt64(IFormatProvider provider)
    //    { return Convert.ToUInt64(Object); }
    //    //
    //    public override string ToString()
    //    {
    //        if (IsEmpty)
    //            return string.Empty;
    //        else
    //        {
    //            string tmp;
    //            if (!NumberUtils.TryNumberToString(Object, out tmp))
    //                tmp = Convert.ToString(Object);
    //            return OPs.ToStr(Time) + "\t" + tmp;
    //        }
    //    }
    //    //public int CompareTo(ITimedObject other) { return DateTime.Compare(Time, other.Time); }

    //    public int CompareTo(object obj)
    //    {
    //        var co = obj as IComparable;
    //        if (co != null)
    //            return -co.CompareTo(Object);
    //    }
    //}

    //public class OneTimedObject : TimedObject
    //{
    //    DateTime time;
    //    object obj;
    //    public OneTimedObject(DateTime time, object obj)
    //    { this.time = time; this.obj = obj; }
    //    public override DateTime Time { get { return time; } }
    //    public override object Object { get { return obj; } }
    //    public override bool IsEmpty { get { return obj == null; } }
    //}

    //public class TimedObjects : TimedObject, IList
    //{
    //    class Null : TimedObjects
    //    {
    //        public Null() : base(new DateTime[0], new object[0]) { }
    //        public override DateTime Time { get { return DateTime.MinValue; } }
    //        public override object Object { get { return null; } }
    //        public override bool IsEmpty { get { return true; } }
    //        public override string ToString()
    //        { return string.Empty; }
    //    }
    //    public static readonly TimedObjects Empties = new Null();
    //    public static TimedObjects FromTimesAndObjs(IList<DateTime> times, IList objs)
    //    {
    //        if (times == null || objs == null || times.Count == 0 || objs.Count == 0)
    //            return Empties;
    //        if (times.Count != objs.Count)
    //            throw new ArgumentException("TimedObjects: times.Count!=objs.Count");
    //        return new TimedObjects(times, objs);
    //    }
    //    public readonly IList<DateTime> times;
    //    public readonly IList objs;
    //    protected TimedObjects(IList<DateTime> times, IList objs)
    //    {
    //        this.times = times; this.objs = objs;
    //    }
    //    public override DateTime Time { get { return times[times.Count - 1]; } }
    //    public override object Object { get { return objs[objs.Count - 1]; } }
    //    public override bool IsEmpty { get { return times.Count == 0; } }

    //    #region IList
    //    int ICollection.Count { get { return times.Count; } }
    //    bool IList.IsReadOnly { get { return true; } }
    //    int IList.Add(object item) { throw new NotSupportedException(); }
    //    void IList.Clear() { throw new NotSupportedException(); }
    //    bool IList.Contains(object item) { throw new NotImplementedException(); }
    //    void ICollection.CopyTo(Array array, int index)
    //    { throw new NotImplementedException(); }
    //    void IList.Remove(object item) { throw new NotSupportedException(); }
    //    bool IList.IsFixedSize { get { return true; } }
    //    bool ICollection.IsSynchronized { get { return false; } }
    //    object ICollection.SyncRoot { get { return this; } }

    //    object IList.this[int index]
    //    {
    //        get { return new OneTimedObject(times[index], objs[index]); }
    //        set { throw new NotSupportedException(); }
    //    }

    //    int IList.IndexOf(object item) { throw new NotImplementedException(); }
    //    void IList.Insert(int index, object item) { throw new NotSupportedException(); }
    //    void IList.RemoveAt(int index) { throw new NotSupportedException(); }

    //    IEnumerator IEnumerable.GetEnumerator()
    //    {
    //        for (int i = 0; i < times.Count; i++)
    //            yield return new OneTimedObject(times[i], objs[i]);
    //        yield break;
    //    }

    //    #endregion
    //}
}
