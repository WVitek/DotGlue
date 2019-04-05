using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using W.Common;

namespace W.Expressions
{
	public static class FuncDefs_DataTransform
	{
		static class SynchroItemsEnumHelper<T> where T : class
		{
			public struct Items
			{
				public TimeRange timeRng;
				public IEnumerable<T> items;
			}

			struct Item
			{
				public TimeRange timeRng;
				public T item;

				public Items ToItems(DateTime dtMin, DateTime dtMax)
				{
					return new Items()
					{
						timeRng = TimeRange.Intersection(timeRng.time, timeRng.endTime, dtMin, dtMax),
						items = new T[1] { item }
					};
				}
			}

			class Lst : List<Item> { }

			struct EnumInfo
			{
				public TimeRange tr;
				public int lastNdx;
			}

			static IEnumerable<EnumInfo> RecursiveHelper(Lst lst, int i, DateTime dtMin, DateTime dtMax)
			{
				if (i == lst.Count)
					yield break;
				var tr = lst[i].timeRng;
				if (dtMax < tr.time)
					yield break;
				if (i + 1 == lst.Count)
				{
					if (tr.time < dtMax && dtMin < tr.endTime)
						yield return new EnumInfo() { tr = TimeRange.Intersection(tr.time, tr.endTime, dtMin, dtMax), lastNdx = i };
				}
				else {
					if (dtMin < tr.time)
						yield return new EnumInfo() { tr = new TimeRange(dtMin, tr.time), lastNdx = i };
					foreach (var t in RecursiveHelper(lst, i + 1, tr.time, dtMax))
						yield return t;
					if (tr.endTime < dtMax)
					{
						foreach (var t in RecursiveHelper(lst, i + 1, tr.endTime, dtMax))
							yield return t;
						dtMin = tr.endTime;
					}
				}
			}

			static IEnumerable<Items> EnumerateLst(Lst lst, DateTime dtMin, DateTime dtMax)
			{
				foreach (var info in RecursiveHelper(lst, 0, dtMin, dtMax))
					yield return new Items()
					{
						timeRng = info.tr,
						items = lst.Take(info.lastNdx + 1).Where(p => p.timeRng.time < dtMax && dtMin < p.timeRng.endTime).Select(p => p.item)
					};
			}

			public static IEnumerable<Items> Do(
				IEnumerable<T> items, Func<T, TimeRange> getTimeRange,
				DateTime dtMin, DateTime dtMax)
			{
				var lst = new Lst();
				var lstMaxEndTime = dtMin;
				foreach (var item in items)
				{
					var tr = getTimeRange(item);
					if (tr.endTime == DateTime.MinValue)
						continue;

					if (lstMaxEndTime <= tr.time && lst.Count > 0)
					{
						if (lst.Count == 1)
							yield return lst[0].ToItems(dtMin, tr.time);
						else
							foreach (var tuple in EnumerateLst(lst, dtMin, tr.time))
								yield return tuple;
						lst.Clear();
						dtMin = tr.time;
					}
					if (lstMaxEndTime < tr.endTime)
						lstMaxEndTime = tr.endTime;
					lst.Add(new Item() { timeRng = tr, item = item });
				}
				if (lst.Count > 0)
					if (lst.Count == 1)
						yield return lst[0].ToItems(dtMin, dtMax);
					else
						foreach (var tuple in EnumerateLst(lst, dtMin, dtMax))
							yield return tuple;
			}
		}

		static readonly KeysComparer keyComparer = new KeysComparer(0);

		public delegate object[] SolverAggFunc(object key, TimeRange timeRange, IEnumerable<IIndexedDict> values);

		public static IEnumerable<object[]> SolverAggImpl(IEnumerable<IIndexedDict> data, SolverAggFunc aggFunc)
		{
			var res = data
				.GroupBy(d => d.ValuesList, keyComparer)
				.SelectMany(grp =>
				{
					var key = grp.Key[0];
					var timeRange = key as ITimedObject ?? TimedObject.FullRangeI;
					var items = SynchroItemsEnumHelper<IIndexedDict>.Do(
						grp
						, d => new TimeRange(d.ValuesList[1])
						, timeRange.Time
						, timeRange.EndTime
						);
					return items.Select(p => aggFunc(key, p.timeRng, p.items)).Where(r => r != null);
				});
			return res;
		}

		//[ArgumentInfo(0, "OBJ_ID_SOME")]
		//[ArgumentInfo(1, "PART_VALUE_SOME")]
		//[return: ResultInfo(0, "AGGREGATED_VALUE_SOME")]
		//[return: ResultInfo(1, "OBJ_ID_SOME")]
		public static object SolverAggFirst2([CanBeVector]object arg)
		{
			var res = SolverAggImpl(((IList)arg).Cast<IIndexedDict>(),
				(key, timeRange, values) =>
				{
					var val = values.FirstOrDefault(d => !Utils.IsEmpty(d.ValuesList[1]));
					if (val == null)
						return null;
					return new object[2] { TimedObject.Timed(timeRange.time, timeRange.endTime, val.ValuesList[1]), key };
				}).ToArray();
			return res;
		}

		//todo: correct work for time range
		//[ArgumentInfo(0, "OBJ_ID_SOME")]
		//[ArgumentInfo(1, "MSG_STR_SOME")]
		//[return: ResultInfo(0, "AggregatedMsg_STR_SOME")]
		//[return: ResultInfo(1, "ODJ_ID_SOME")]
		public static object SolverAggStr([CanBeVector]object arg)
		{
			var lst = (IList)arg;
			var res = lst.Cast<IIndexedDict>().GroupBy(
				d => d.ValuesList[0],
				(key, dicts) =>
				{
					var agg = string.Join("; ", dicts.Select(d => Convert.ToString(d.ValuesList[1])).Distinct().OrderBy(s => s));
					if (string.IsNullOrEmpty(agg))
						return null;
					var rs = new object[2];
					rs[0] = agg;
					rs[1] = key;
					return rs;
				}
				).ToArray();
			return res;
		}

		//todo: correct work for time range
		//[ArgumentInfo(0, "OBJ_ID_SOME")]
		//[ArgumentInfo(1, "PART_VALUE_SOME")]
		//[return: ResultInfo(0, "AGGREGATED_VALUE_SOME")]
		//[return: ResultInfo(1, "OBJ_ID_SOME")]
		public static object SolverAggFirst([CanBeVector]object arg)
		{
			var lst = (IList)arg;
			var res = lst.Cast<IIndexedDict>().GroupBy(
				d => d.ValuesList[0],
				(key, dicts) =>
				{
					var agg = dicts.Select(d => d.ValuesList[1]).FirstOrDefault(o => !Utils.IsEmpty(o));
					if (agg == null)
						return null;
					var rs = new object[2];
					rs[0] = agg;
					rs[1] = key;
					return rs;
				}
				).ToArray();
			return res;
		}

		//todo: correct work for time range
		//[ArgumentInfo(0, "OBJ_ID_SOME")]
		//[ArgumentInfo(1, "PART_VALUE_SOME")]
		//[ArgumentInfo(2, "PART_FACTOR_SOME")]
		//[return: ResultInfo(0, "AGGREGATED_VALUE_SOME")]
		//[return: ResultInfo(1, "OBJ_ID_SOME")]
		public static object SolverAggWeightedAvg([CanBeVector]object arg)
		{
			var lst = (IList)arg;
			var res = lst.Cast<IIndexedDict>().GroupBy(
				d => d.ValuesList[0],
				(key, dicts) =>
				{
					double sumValue = 0;
					double sumFactor = 0;
					IIndexedDict fd = null;
					foreach (var d in dicts)
					{
						var vs = d.ValuesList;
						if (Utils.IsEmpty(vs[1]) || Utils.IsEmpty(vs[2]))
							continue;
						var factor = Convert.ToDouble(vs[2]);
						sumValue += Convert.ToDouble(vs[1]) * factor;
						sumFactor += factor;
						if (fd == null) fd = d;
					}
					if (fd == null)
						return null;
					object val;
					if (Math.Abs(sumFactor - 100d) > 1e-4)
						val = null;
					else
						val = sumValue / sumFactor;
					{
						var vs = fd.ValuesList;
						var rs = new object[2];
						rs[0] = val;
						rs[1] = key;
						return rs;
					}
				}
				).ToArray();
			return res;
		}

		/// <summary>
		/// Проверяет 2 условия и возвращает значение, если они истинны или не определены, иначе возвращает отстутствующее значение
		/// </summary>
		/// <param name="arg">0: значение; 1:условие1; 2:условие2</param>
		public static object DoubleConditionsValue(object arg)
		{
			return Utils.Calc(arg, 3, 1, data =>
			{
				var value = (ITimedObject)data[0];
				if (value == null || value.IsEmpty) // unknown value
					return value;

				var cond1 = (ITimedObject)data[1];
				if (cond1 != null && !cond1.IsEmpty)
				{
					var cond2 = (ITimedObject)data[2];
					if (cond2 != null && !cond2.IsEmpty)
					{
						if (Utils.CnvToDbl(cond1, 1d) == 0d && Utils.CnvToDbl(cond2, 1d) == 0d)
							return null; // both conditions are zero, result undefined
						else // is active
						{
							var res = TimedDouble.ValueInRange(value, cond1);
							res = TimedDouble.ValueInRange(res, cond2);
							return res;
						}
					}
				}
				// interpret unknown conditions as true and return value
				return value;
			});
		}

		public static object Multiply(object arg)
		{
			return Utils.Calc(arg, 2, 1, data =>
			{
				var r = OPs.xl2dbl(data[0]) * OPs.xl2dbl(data[1]);
				if (double.IsNaN(r))
					return null;
				else return OPs.WithTime(data[0], data[1], r);
			});
		}

		public static object Substract(object arg)
		{
			return Utils.Calc(arg, 2, 1, data =>
			{
				var r = OPs.xl2dbl(data[0]) - OPs.xl2dbl(data[1]);
				if (double.IsNaN(r))
					return null;
				else return OPs.WithTime(data[0], data[1], r);
			});
		}

		public static object Divide(object arg)
		{
			return Utils.Calc(arg, 2, 1, data =>
			{
				var r = OPs.xl2dbl(data[0]) / OPs.xl2dbl(data[1]);
				if (double.IsNaN(r))
					return null;
				else return OPs.WithTime(data[0], data[1], r);
			});
		}

		static object FirstGoodValue(object x, object y, Func<object, bool> IsGood)
		{
			var toX = x as ITimedObject ?? TimedObject.FullRangeI;
			var toY = y as ITimedObject ?? TimedObject.FullRangeI;
			object a, b;
			if (toX.Time >= toY.Time)
			{ a = x; b = y; }
			else
			{ a = y; b = x; }
			if (IsGood(a))
				return a;
			else if (IsGood(b))
				return b;
			return null;
		}

		public static object FirstValue(object arg)
		{ return Utils.Calc(arg, 2, 1, data => FirstGoodValue(data[0], data[1], x => !float.IsNaN(Utils.Cnv(data[0], float.NaN)))); }

		static bool IsNZ(object obj)
		{
			float v = Utils.Cnv(obj, float.NaN);
			return !float.IsNaN(v) && v != 0;
		}

		static bool IsNonNeg(object obj)
		{
			float v = Utils.Cnv(obj, float.NaN);
			return !float.IsNaN(v) && v >= 0;
		}

		public static object NzValue2(object arg)
		{ return Utils.Calc(arg, 2, 1, data => FirstGoodValue(data[0], data[1], IsNZ)); }

		public static object NzValue3(object arg)
		{ return Utils.Calc(arg, 3, 1, data => FirstGoodValue(FirstGoodValue(data[0], data[1], IsNZ), data[2], IsNZ)); }

		public static object NNegValue2(object arg)
		{ return Utils.Calc(arg, 2, 1, data => FirstGoodValue(data[0], data[1], IsNonNeg)); }
	}
}
