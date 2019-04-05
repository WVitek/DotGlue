using System;
using System.Collections.Generic;

namespace W.Common
{
	public static class TopoSort
	{
		class Helper<T>
		{
			public Func<T, IEnumerable<T>> getDependencies;
			public List<T> sorted;
			public HashSet<T> visited;

			public void VisitDependency(T item)
			{
				if (visited.Contains(item))
					return;

				visited.Add(item);

				foreach (var d in getDependencies(item))
					VisitDependency(d);

				sorted.Add(item);
			}
		}

		public static List<T> DoSort<T>(IEnumerable<T> items, Func<T, IEnumerable<T>> getDependencies)
		{
			var h = new Helper<T>() { sorted = new List<T>(), visited = new HashSet<T>(), getDependencies = getDependencies };

			foreach (var item in items)
				h.VisitDependency(item);

			return h.sorted;
		}
	}
}
