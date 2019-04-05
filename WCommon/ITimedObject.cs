using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace W.Common
{
	public interface ITimedObject : IConvertible
	{
		DateTime Time { get; }
		DateTime EndTime { get; }
		object Object { get; }
		bool IsEmpty { get; }
        int CompareTimed(object b);
	}

	public interface ITimedDouble : ITimedObject
	{
		double Value { get; }
	}

	public interface IIndexedDict : IDictionary<string, object>
	{
		IDictionary<string, int> Key2Ndx { get; }
		object[] ValuesList { get; }
        DateTime Time { get; }
        DateTime EndTime { get; }
    }
}
