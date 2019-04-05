using System;
using System.Collections;
using W.Expressions;

namespace W.Files
{
    public static class FuncDefs_Xlsx
    {
        [Arity(1, 1)]
        public static object COLUMN(CallExpr ce, Generator.Ctx ctx)
        {
            var cellName = OPs.TryAsName(ce.args[0], ctx);
            if (cellName == null)
                throw new Generator.Exception($"COLUMN(cellName): can't get cell name from expession // {ce.args[0]}");
            int i = 0;
            if (CellRange.ParseCellName(cellName, ref i, cellName.Length, out int row, out int col))
                return col;
            throw new Generator.Exception($"COLUMN(cellName): can't parse given cell name // {cellName}");
        }

        [Arity(2, 3)]
        public static object ADDRESS(IList args)
        {
            int row = Convert.ToInt32(args[0]);
            int col = Convert.ToInt32(args[1]);
            int kind = (args.Count > 2) ? Convert.ToInt32(args[2]) : 4;
            switch (kind)
            {
                //case 1: row = -row; col = -col; break;
                //case 2: row = -row; break;
                //case 3: col = -col; break;
                case 4: break;
                default:
                    throw new ArgumentException($"ADDRESS({col},{row},kind={kind}): only kind=4 is supported");
            }
            if (row == 0)
                return CellRange.ColumnIndexToName(col);
            return CellRange.RowAndColToCellName(row, col);
        }
    }
}
