using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace W.Files
{
    public class CellRange : IEqualityComparer<CellRange>
    {
        public readonly int row, col, row2, col2;

        string _name;
        public string name
        {
            get
            {
                if (_name == null)
                    _name = (row2 == 0)
                        ? RowAndColToCellName(row, col)
                        : RowAndColToCellName(row, col) + ':' + RowAndColToCellName(row2, col2);
                return _name;
            }
        }

        protected CellRange() { }
        protected CellRange(int r, int c, int r2, int c2, string name)
        {
            row = r; col = c; row2 = r2; col2 = c2;
            this._name = name;
        }

        public CellRange(int r, int c)
        { row = r; col = c; }

        public CellRange(int r, int c, int r2, int c2)
            : this(r, c, r2, c2, null)
        { }

        public bool IsOneCell { get { return row2 == 0 && col2 == 0; } }
        public override string ToString() { return name; }

        public CellRange AsRelative()
        {
            if (row >= 0 && col >= 0 && row2 >= 0 && col2 >= 0)
                return this;
            if (row2 != 0 || col2 != 0)
                return new CellRange(Math.Abs(row), Math.Abs(col), Math.Abs(row2), Math.Abs(col2));
            else return new CellRange(Math.Abs(row), Math.Abs(col));
        }

        public CellRange ShiftedCell(int deltaRow, int deltaCol)
        {
            return new CellRange(row + deltaRow, col + deltaCol);
        }

        public bool Inside(CellRange b)
        {
            if (IsOneCell || b.IsOneCell)
                throw new Exception("'Inside' function applicable only for ranges, not single cells");
            return b.row <= row && row <= b.row2 && b.col <= col && col <= b.col2;
            //return (row < b.row2 || b.row < row2 || b.col < col2 || b.col2 > col);
        }

        public static CellRange TryFromName(string name)
        {
            int row, col;
            int i = 0, L = name.Length;
            if (!ParseCellName(name, ref i, L, out row, out col))
                return null;
            if (i == L)
                return new CellRange(row, col, 0, 0, name);
            if (name[i++] != ':')
                return null;
            int row2, col2;
            if (!ParseCellName(name, ref i, L, out row2, out col2))
                return null;
            return new CellRange(row, col, row2, col2, name);
        }

        public static CellRange FromName(string name)
        {
            var r = TryFromName(name);
            if (r == null)
                throw new Exception("Can't parse range name: " + name);
            return r;
        }

        public static IEnumerable<CellRange> EnumBetween(CellRange a, CellRange b)
        {
            int r0 = Math.Max(1, a.row);
            int c0 = Math.Max(1, a.col);
            int r1 = Math.Max(1, b.row);
            int c1 = Math.Max(1, b.col);
            int dr = (r0 < r1) ? +1 : -1; r1 += dr;
            int dc = (c0 < c1) ? +1 : -1; c1 += dc;

            for (int i = r0; i != r1; i += dr)
                for (int j = c0; j != c1; j += dc)
                    yield return new CellRange(i, j);
        }

        public static int Compare(CellRange a, CellRange b)
        {
            int dr = a.row - b.row;
            if (dr != 0)
                return dr;
            int dc = a.col - b.col;
            return dc;
        }

        public bool Equals(CellRange x, CellRange y) { return string.Equals(x.name, y.name); }
        public int GetHashCode(CellRange obj) { return obj.name.GetHashCode(); }
        public static readonly CellRange Comparer = new CellRange();

        public static string RowAndColToCellName(int row, int col)
        {
            var sCol = (col <= 0)
                ? ((col == 0) ? string.Empty : "$" + ColumnIndexToName(-col))
                : ColumnIndexToName(col);
            var sRow = (row <= 0)
                ? ((row == 0) ? string.Empty : "$" + RowIndexToName(-row))
                : RowIndexToName(row);
            return sCol + sRow;
        }

        public static string ColumnIndexToName(int columnIndex)
        {
            var sb = new StringBuilder(3);
            do
            {
                sb.Insert(0, (char)('A' + (char)((--columnIndex) % 26)));
                columnIndex /= 26;
            } while (columnIndex != 0);
            return sb.ToString();
        }

        public static string RowIndexToName(int rowIndex)
        {
            return rowIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool ParseCellName(string s, ref int i, int L, out int row, out int col)
        {
            row = 0;
            col = 0;
            // first optional dollar sign
            bool absCol = s[i] == '$';
            if (absCol) i++;
            if (i >= L)
                return false;
            // column name
            var c = s[i];
            if ('A' <= c && c <= 'Z')
            {
                while (true)
                {
                    col = col * 26 + (int)(c - 'A') + 1;
                    if (++i >= L)
                        return false;
                    c = s[i];
                    if (c < 'A' || 'Z' < c)
                        break;
                }
            }
            else if (absCol) i--;
            // second optional dollar sign
            bool absRow = s[i] == '$';
            if (absRow) i++;
            if (i >= L)
                return false;
            // row number
            c = s[i];
            if (c < '0' || '9' < c)
                return false;
            row = 0;
            while (true)
            {
                row = row * 10 + (int)(c - '0');
                if (++i >= L)
                    break;
                c = s[i];
                if (c < '0' || '9' < c)
                    break;
            }
            if (absCol)
                col = -col;
            if (absRow)
                row = -row;
            return true;
        }
    }

}