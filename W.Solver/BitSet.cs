using System;
using System.Collections.Generic;

namespace W.Expressions
{
	public class BitSet : IEqualityComparer<BitSet>
	{
		public static readonly BitSet Empty = new BitSet();

		public ulong[] data { get; private set; } // storage for bits
		public int MaxIndex { get; private set; }

		private BitSet() { data = new ulong[0]; MaxIndex = 0; }
		private BitSet(ulong[] data, int MaxIndex) { this.data = data; this.MaxIndex = MaxIndex; }

		public BitSet(BitSet bits)
		{
			var a = bits.data;
			int n = a.Length;
			var b = new ulong[n];
			for (int i = n - 1; i >= 0; i--)
				b[i] = a[i];
			data = b;
			MaxIndex = bits.MaxIndex;
		}

		public BitSet(BitSet baseBits, int[] bitsIndexesToSet, int minIndexToAllocate = 0)
		{
			int maxNdx = Math.Max(minIndexToAllocate, baseBits.MaxIndex);
			foreach (int ndx in bitsIndexesToSet)
				if (ndx > maxNdx)
					maxNdx = ndx;
			var a = baseBits.data;
			int maxN = Math.Max((maxNdx >> 6) + 1, a.Length);
			var b = new ulong[maxN];
			for (int i = a.Length - 1; i >= 0; i--)
				b[i] = a[i];
			foreach (int ndx in bitsIndexesToSet)
			{
				int i = ndx >> 6;
				b[i] |= 1ul << (ndx & 0x3F);
			}
			data = b;
			MaxIndex = maxNdx;
		}

		public BitSet(BitSet baseBits, int[] bitsIndexesToSet, int[] bitsIndexesToClear)
		{
			int maxNdx = baseBits.MaxIndex;
			foreach (int ndx in bitsIndexesToClear) if (ndx > maxNdx) maxNdx = ndx;
			foreach (int ndx in bitsIndexesToSet) if (ndx > maxNdx) maxNdx = ndx;
			var a = baseBits.data;
			int maxN = Math.Max((maxNdx >> 6) + 1, a.Length);
			var b = new ulong[maxN];
			for (int i = a.Length - 1; i >= 0; i--)
				b[i] = a[i];
			foreach (int ndx in bitsIndexesToSet)
			{
				int i = ndx >> 6;
				b[i] |= 1ul << (ndx & 0x3F);
			}
			foreach (int ndx in bitsIndexesToClear)
			{
				int i = ndx >> 6;
				b[i] &= ~(1ul << (ndx & 0x3F));
			}
			data = b;
			MaxIndex = maxNdx;
		}

		public void And(BitSet bits)
		{
			var a = bits.data;
			int n = Math.Min(data.Length, a.Length);
			for (int i = n - 1; i >= 0; i--)
				data[i] &= a[i];
			for (int i = data.Length - 1; i >= n; i--)
				data[i] = 0;
			if (MaxIndex < bits.MaxIndex)
				MaxIndex = bits.MaxIndex;
		}
		public void AndNot(BitSet bits)
		{
			var a = bits.data;
			int n = Math.Min(data.Length, a.Length);
			for (int i = n - 1; i >= 0; i--)
				data[i] &= ~a[i];
			//for (int i = a.Length - 1; i >= n; i--)
			//	data[i] = 0;
			if (MaxIndex < bits.MaxIndex)
				MaxIndex = bits.MaxIndex;
		}

		public BitSet Or(BitSet bits)
		{
			var a = data;
			var b = bits.data;
			int n = b.Length;
			BitSet result;
			if (a.Length < n)
			{
				Array.Resize(ref a, n);
				result = new BitSet(a, Math.Max(bits.MaxIndex, MaxIndex));
			}
			else
			{
				n = a.Length;
				result = this;
			}
			for (int i = n - 1; i >= 0; i--)
				a[i] |= b[i];
			return result;
		}

		public bool ContainsAllBits(params int[] bitsIndexes)
		{
			int n = data.Length;
			foreach (int ndx in bitsIndexes)
			{
				int i = ndx >> 6;
				if (i >= n)
					return false;
				if ((data[i] & (1ul << (ndx & 0x3F))) == 0)
					return false;
			}
			return true;
		}

		public bool AllBitsClear()
		{
			foreach (var d in data)
				if (d != 0ul)
					return false;
			return true;
		}

		public bool AllBitsSet()
		{
			int maxNdx = MaxIndex;
			int i = maxNdx >> 6;
			for (int j = 0; j < i; j++)
				if (data[i] != ulong.MaxValue)
					return false;
			var nBits = maxNdx & 0x3F;
			if (nBits > 0)
			{
				var mask = ulong.MaxValue >> (64 - nBits);
				if ((data[i] & mask) != mask)
					return false;
			}
			return true;
		}

		public bool AnyBitIsClear(params int[] zeroBitsIndexes)
		{
			int n = data.Length;
			foreach (int ndx in zeroBitsIndexes)
			{
				int i = ndx >> 6;
				if (ndx > MaxIndex)
					continue;
				if (i > n)
					return true;
				if ((data[i] & (1ul << (ndx & 0x3F))) == 0)
					return true;
			}
			return false;
		}

		public bool ContainsAny(params int[] bitsIndexes)
		{
			int n = data.Length;
			foreach (int ndx in bitsIndexes)
			{
				int i = ndx >> 6;
				if (i >= n)
					continue;
				if ((data[i] & (1ul << (ndx & 0x3F))) != 0)
					return true;
			}
			return false;
		}

		public int CountOnes(params int[] bitsIndexes)
		{
			int res = 0;
			int n = data.Length;
			foreach (int ndx in bitsIndexes)
			{
				int i = ndx >> 6;
				if (i >= n)
					continue;
				if ((data[i] & (1ul << (ndx & 0x3F))) != 0)
					res++;
			}
			return res;
		}

		public IEnumerable<int> EnumOnesIndexes()
		{
			int n = data.Length;
			for (int ndx = 0; ndx <= MaxIndex; ndx++)
			{
				int i = ndx >> 6;
				if (i >= n)
					yield break;
				if ((data[i] & (1ul << (ndx & 0x3F))) != 0)
					yield return ndx;
			}
		}

		bool Equal(BitSet bs)
		{
			if (bs == null || data.Length != bs.data.Length || MaxIndex != bs.MaxIndex)
				return false;
			for (int i = data.Length - 1; i >= 0; i--)
				if (data[i] != bs.data[i])
					return false;
			return true;
		}

		public override int GetHashCode()
		{
			uint res = 0;
			foreach (var d in data)
				res ^= ((uint)d & 0xFFFFFFFFu) ^ (uint)(d >> 32);
			return (int)res;
		}

		public override bool Equals(object obj) { return Equal(obj as BitSet); }

		// IEqualityComparer
		public bool Equals(BitSet x, BitSet y)
		{ return x.Equal(y); }

		public int GetHashCode(BitSet obj)
		{ return obj.GetHashCode(); }

		public override string ToString()
		{
			var sb = new System.Text.StringBuilder("{");
			foreach (int ndx in EnumOnesIndexes())
			{
				if (sb.Length > 1)
					sb.Append(',');
				sb.Append(ndx);
			}
			sb.Append('}');
			return sb.ToString();
		}
	}
}
