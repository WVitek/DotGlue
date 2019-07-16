using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Text;
using System.IO;
using System.Collections;

namespace W.Expressions
{
    /// <summary>
    /// Expression node types
    /// Numerical value/16 = priority level (level 0 - maximal priority)
    /// </summary>
    public enum ExprType
    {
        Constant = 0x001, Call = 0x002, NewArrayInit = 0x003, Reference = 0x004, Index = 0x005, IndexNullCond = 0x006,
        Fluent2 = 0x051, Fluent = 0x052, FluentNullCond = 0x053,
        Power = 0x101,
        Multiply = 0x201, Divide = 0x202, Negate = 0x203,
        Add = 0x301, Subtract = 0x302,
        Concat = 0x401, In = 0x402, OraConcat = 0x403,
        Equal = 0x501, NotEqual = 0x502, LessThanOrEqual = 0x503, GreaterThanOrEqual = 0x504, LessThan = 0x505, GreaterThan = 0x506,
        /*
		Alias = 0x602, Sequence = 0x603
		/*/
        //LogicalNot = 0x601,
        LogicalAnd = 0x701,
        LogicalOr = 0x801, Alias = 0x802, Sequence = 0x803
        //*/
    }

    public abstract class Expr
    {
        public const int textLineLimit = 130;
        public const string nestingStr = "    ";
        public const int nestingStrLen = 4;

        public readonly ExprType nodeType;
        public Expr(ExprType nodeType) { this.nodeType = nodeType; }
        public virtual bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            sb.Append('|');
            var s = nodeType.ToString();
            sb.Append(s);
            sb.Append('|');
            column += 2 + s.Length;
            return false;
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            int column = 0;
            buildString(sb, 0, ref column);
            return sb.ToString();
        }
        public static int PriorityLevel(ExprType type) { return (int)type >> 4; }
        public bool IsNull
        {
            get
            {
                var c = this as ConstExpr;
                return c != null && c.value == null;
            }
        }

        public virtual Expr Copy() { return (Expr)MemberwiseClone(); }

        public abstract Expr Visit(Func<Expr, Expr> visitor);

        public static Func<Expr, Expr> RecursiveModifier(Func<Expr, Expr> modifier)
        {
            Func<Expr, Expr> v = null;
            v = e =>
            {
                var f = modifier(e);
                if (f != e)
                    return f;
                else return e.Visit(v);
            };
            return v;
        }

        public IEnumerable<T> Traverse<T>(Func<Expr, T> visitor) { return Traverse<T>(this, visitor); }

        public static IEnumerable<T> Traverse<T>(Expr expr, Func<Expr, T> visitor)
        {
            {
                var ue = expr as UnaryExpr;
                if (ue != null)
                {
                    foreach (var r in Traverse(ue.operand, visitor)) yield return r;
                    goto done;
                }
            }
            {
                var be = expr as BinaryExpr;
                if (be != null)
                {
                    foreach (var r in Traverse(be.left, visitor)) yield return r;
                    foreach (var r in Traverse(be.right, visitor)) yield return r;
                    goto done;
                }
            }
            {
                var me = expr as MultiExpr;
                if (me != null)
                {
                    foreach (var e in me.args) foreach (var r in Traverse(e, visitor)) yield return r;
                    goto done;
                }
            }
            {
                var ie = expr as IndexExpr;
                if (ie != null)
                {
                    foreach (var r in Traverse(ie.value, visitor)) yield return r;
                    foreach (var r in Traverse(ie.index, visitor)) yield return r;
                    goto done;
                }
            }
            done:
            yield return visitor(expr);
        }

        public IEnumerable<string> EnumerateReferences()
        {
            foreach (var s in Traverse(this, e => (e.nodeType == ExprType.Reference) ? ((ReferenceExpr)e).name : null))
                if (s != null)
                    yield return s;
        }

        public static bool CheckLineLimit(StringBuilder sb, int nestingLevel, ref int column)
        {
            if (column >= textLineLimit)
            {
                NewLine(sb, nestingLevel, ref column);
                return true;
            }
            else return false;
        }
        public static void NewLine(StringBuilder sb, int nestingLevel)
        {
            sb.AppendLine();
            while (--nestingLevel >= 0)
                sb.Append(nestingStr);
        }
        public static void NewLine(StringBuilder sb, int nestingLevel, ref int column)
        {
            NewLine(sb, nestingLevel);
            column = nestingStrLen * nestingLevel;
        }

        public static bool bldStr(StringBuilder sb, IList<Expr> args, string delimiter, int firstPos, int nestingLevel, ref int column)
        {
            bool first = true;
            bool multiline = false;
            int n = args.Count;
            int pos = nestingStrLen * nestingLevel;

            for (int i = 0; i < n; i++)
            {
                var tsb = new StringBuilder();
                int col = 0;
                var ml = args[i].buildString(tsb, nestingLevel + 1, ref col);
                if (first)
                    first = false;
                else
                {
                    sb.Append(delimiter);
                    pos += delimiter.Length;
                }
                if (ml || (pos + tsb.Length >= textLineLimit))
                {
                    NewLine(sb, nestingLevel + 1, ref pos);
                    multiline = true;
                    if (ml)
                        pos = col;
                    else pos += tsb.Length;
                    sb.Append(tsb);
                }
                else
                {
                    pos += tsb.Length;
                    sb.Append(tsb);
                }
            }
            column = pos;
            return multiline;
            //{
            //    if (first) first = false;
            //    else { sb.Append(delimiter); column += delimiter.Length; }
            //    if (column >= textLineLimit || ml)
            //    {
            //        if (!multiline)
            //        {
            //            var tmp = new StringBuilder();
            //            NewLine(tmp, nestingLevel);
            //            column = nestCharWidth * nestingLevel + (sb.Length - firstPos);
            //            sb.Insert(firstPos, tmp.ToString());
            //            multiline = true;
            //        }
            //        CheckLineLimit(sb, nestingLevel + 1, ref column);
            //    }
            //    ml = expr.buildString(sb, nestingLevel + 1, ref column);
            //    var prevLen = sb.Length;
            //}
            //return multiline;
        }
    }

    public class ConstExpr : Expr
    {
        public static readonly ConstExpr Null = new ConstExpr(null);
        public static readonly ConstExpr Zero = new ConstExpr(0);
        public static readonly ConstExpr One = new ConstExpr(1);
        public static readonly ConstExpr True = new ConstExpr(true);
        public static readonly ConstExpr False = new ConstExpr(false);
        public static readonly ConstExpr StringEmpty = new ConstExpr(string.Empty);
        public static readonly Expr NaN = new BinaryExpr(ExprType.Divide, ConstExpr.Zero, ConstExpr.Zero);

        public readonly object value;
        public ConstExpr(object value) : base(ExprType.Constant) { this.value = value; }
        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            var prevLen = sb.Length;
            ToText(new StringWriter(sb), value);
            column += sb.Length - prevLen;
            return false;
        }

        public static void ToText(TextWriter wr, object val)
        {
            if (val is IList lst)
            {
                wr.Write('{');
                for (int i = 0; i < lst.Count; i++)
                {
                    if (i > 0)
                        wr.Write(',');
                    ToText(wr, lst[i]);
                }
                wr.Write('}');
            }
            else if (val is string s)
            {
                wr.Write("'");
                wr.Write(s.Replace("'", "''"));
                wr.Write("'");
            }
            //else if (W.Common.NumberUtils.TryNumberToString(val, out s))
            //    wr.Write(s);
            else wr.Write(val);
        }

        public override Expr Visit(Func<Expr, Expr> visitor)
        { return this; }
    }

    public class StringExpr : ConstExpr
    {
        public readonly char quote;
        public StringExpr(string value, char quote) : base(value) { this.quote = quote; }

        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            var prevLen = sb.Length;
            sb.Append(quote);
            sb.Append(value);
            sb.Append(quote);
            column += sb.Length - prevLen;
            return false;
        }
    }

    public sealed class EmptyExpr : ConstExpr
    {
        public static readonly EmptyExpr Value = new EmptyExpr();
        private EmptyExpr() : base(null) { }
        public override string ToString() { return string.Empty; }
        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column) { return false; }
    }

    public class UnaryExpr : Expr
    {
        public readonly Expr operand;
        public UnaryExpr(ExprType nodeType, Expr operand)
            : base(nodeType)
        { this.operand = operand; }
        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            switch (nodeType)
            {
                case ExprType.Negate:
                    sb.Append('-');
                    column++;
                    break;
                default:
                    base.buildString(sb, nestingLevel, ref column);
                    break;
            }
            bool multiline;
            if (PriorityLevel(nodeType) < PriorityLevel(operand.nodeType))
            {
                sb.Append('('); column++;
                multiline = operand.buildString(sb, nestingLevel, ref column);
                sb.Append(')'); column++;
            }
            else multiline = operand.buildString(sb, nestingLevel, ref column);
            return multiline;
        }

        public override Expr Visit(Func<Expr, Expr> visitor)
        {
            var r = visitor(operand);
            if (r != operand)
                return new UnaryExpr(nodeType, r);
            else return this;
        }
    }

    public class BinaryExpr : Expr
    {
        public readonly Expr left;
        public readonly Expr right;
        public BinaryExpr(ExprType nodeType, Expr left, Expr right)
            : base(nodeType)
        {
            System.Diagnostics.Debug.Assert(left != null && right != null);
            this.left = left; this.right = right;
        }
        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            bool ml, mr;
            var pr = PriorityLevel(nodeType);
            if (pr < PriorityLevel(left.nodeType))
            {
                sb.Append('('); column++;
                ml = left.buildString(sb, nestingLevel, ref column);
                sb.Append(')'); column++;
            }
            else ml = left.buildString(sb, nestingLevel, ref column);
            var sOP = binaryOpString(nodeType);
            if (char.IsLetter(sOP, 0))
            {
                sb.Append(' '); sb.Append(sOP); sb.Append(' ');
                column += sOP.Length + 2;
            }
            else
            {
                sb.Append(sOP);
                column += sOP.Length;
            }
            if (pr <= PriorityLevel(right.nodeType))
            {
                sb.Append('('); column++;
                mr = right.buildString(sb, nestingLevel, ref column);
                sb.Append(')'); column++;
            }
            else mr = right.buildString(sb, nestingLevel, ref column);
            return ml || mr;
        }
        public static string binaryOpString(ExprType type)
        {
            switch (type)
            {
                case ExprType.Fluent:
                    return ".";
                case ExprType.Fluent2:
                    return "..";
                case ExprType.FluentNullCond:
                    return "?.";
                case ExprType.Power:
                    return "^";
                case ExprType.Multiply:
                    return "*";
                case ExprType.Divide:
                    return "/";
                case ExprType.Add:
                    return "+";
                case ExprType.Subtract:
                    return "-";
                case ExprType.Concat:
                    return "&";
                case ExprType.Equal:
                    return "=";
                case ExprType.NotEqual:
                    return "<>";
                case ExprType.LessThan:
                    return "<";
                case ExprType.GreaterThan:
                    return ">";
                case ExprType.LessThanOrEqual:
                    return "<=";
                case ExprType.GreaterThanOrEqual:
                    return ">=";
                case ExprType.LogicalAnd:
                    return "AND";
                case ExprType.LogicalOr:
                    return "OR";
                //case ExprType.LogicalNot:
                //	return "NOT";
                case ExprType.In:
                    return "IN";
                case ExprType.Alias:
                    return " ";
                case ExprType.OraConcat:
                    return "||";
                default: return null;
            }
        }

        public override Expr Visit(Func<Expr, Expr> visitor)
        {
            var l = visitor(left);
            var r = visitor(right);
            if (l == left && r == right)
                return this;
            else
                return new BinaryExpr(nodeType, l, r);
        }

        public static Expr Create(ExprType nodeType, params Expr[] args)
        {
            System.Diagnostics.Trace.Assert(args.Length > 1);
            var expr = args[0];
            for (int i = 1; i < args.Length; i++)
                expr = new BinaryExpr(nodeType, expr, args[i]);
            return expr;
        }
    }

    public class ReferenceExpr : Expr
    {
        public readonly string name;
        public ReferenceExpr(string name) : base(ExprType.Reference) { this.name = name; }
        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        { sb.Append(name); column += name.Length; return false; }

        public override Expr Visit(Func<Expr, Expr> visitor)
        { return this; }

        public static ReferenceExpr Create(string name) => new ReferenceExpr(name);
    }

    public class IndexExpr : Expr
    {
        public readonly Expr value;
        public readonly Expr index;

        public IndexExpr(Expr value, Expr index, bool IsNullCond = false)
            : base(IsNullCond ? ExprType.IndexNullCond : ExprType.Index)
        { this.value = value; this.index = index; }

        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            bool mv, mi;
            var pr = PriorityLevel(nodeType);
            if (pr < PriorityLevel(value.nodeType))
            {
                sb.Append('('); column++;
                mv = value.buildString(sb, nestingLevel, ref column);
                sb.Append(')'); column++;
            }
            else mv = value.buildString(sb, nestingLevel, ref column);
            sb.Append('['); column++;
            if (nodeType == ExprType.IndexNullCond)
            { sb.Append('?'); column++; }
            mi = index.buildString(sb, nestingLevel, ref column);
            sb.Append(']'); column++;
            return mv || mi;
        }

        public override Expr Visit(Func<Expr, Expr> visitor)
        {
            var v = visitor(value);
            var i = visitor(index);
            if (v == value && i == index)
                return this;
            else return new IndexExpr(v, i);
        }
    }

    public abstract class MultiExpr : Expr
    {
        public readonly ReadOnlyCollection<Expr> args;
        public MultiExpr(ExprType nodeType, IList<Expr> args)
            : base(nodeType)
        {
            this.args = args as ReadOnlyCollection<Expr>;
            if (this.args == null)
                this.args = new ReadOnlyCollection<Expr>(args);
        }

        public static ReadOnlyCollection<Expr> Visit(ReadOnlyCollection<Expr> args, Func<Expr, Expr> visitor)
        {
            Expr[] lst = null;
            for (int i = 0; i < args.Count; i++)
            {
                var ai = args[i];
                var e = visitor(ai);
                if (e != ai)
                {
                    if (lst == null)
                    {
                        lst = new Expr[args.Count];
                        args.CopyTo(lst, 0);
                    }
                    lst[i] = e;
                }
            }
            if (lst == null)
                return args;
            else return new ReadOnlyCollection<Expr>(lst);
        }
    }

    public class CallExpr : MultiExpr
    {
        public readonly string funcName;
        public readonly FuncDef funcDef;

        public CallExpr(string funcName, IList<Expr> args) : base(ExprType.Call, args) { this.funcName = funcName; }

        public CallExpr(CallExpr ce, IList<Expr> args) : base(ExprType.Call, args)
        {
            this.funcName = ce.funcName;
            this.funcDef = ce.funcDef;
        }

        public CallExpr(FuncDef fd, IList<Expr> args) : base(ExprType.Call, args)
        {
            this.funcName = fd.name;
            this.funcDef = fd;
        }

        public CallExpr(string funcName, params Expr[] args) : base(ExprType.Call, args) { this.funcName = funcName; }

        static readonly ConcurrentDictionary<Delegate, FuncDef> defs = new ConcurrentDictionary<Delegate, FuncDef>();

        public CallExpr(Delegate func, IList<Expr> args) : base(ExprType.Call, args)
        {
            funcName = func.Method.Name;
            funcDef = defs.GetOrAdd(func, f => new FuncDef(f, f.Method.Name));
        }

        public CallExpr(Macro func, IList<Expr> args) : this((Delegate)func, args) { }
        public CallExpr(Macro func, params Expr[] args) : this((Delegate)func, args) { }
        public CallExpr(Fn func, IList<Expr> args) : this((Delegate)func, args) { }
        public CallExpr(Fn func, params Expr[] args) : this((Delegate)func, args) { }
        public CallExpr(Fx func, IList<Expr> args) : this((Delegate)func, args) { }
        public CallExpr(Fx func, params Expr[] args) : this((Delegate)func, args) { }
        public CallExpr(Fxy func, IList<Expr> args) : this((Delegate)func, args) { }
        public CallExpr(Fxy func, params Expr[] args) : this((Delegate)func, args) { }
        public CallExpr(AsyncFn func, IList<Expr> args) : this((Delegate)func, args) { }
        public CallExpr(AsyncFn func, params Expr[] args) : this((Delegate)func, args) { }

        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            var firstPos = sb.Length;
            sb.Append(funcName); column += funcName.Length;
            sb.Append('('); column++;
            var multiline = bldStr(sb, args, ", ", firstPos, nestingLevel, ref column);
            if (multiline)
                NewLine(sb, nestingLevel, ref column);
            sb.Append(')'); column++;
            return multiline;
        }

        public override Expr Visit(Func<Expr, Expr> visitor)
        {
            var lst = Visit(args, visitor);
            if (lst == args)
                return this;
            else return new CallExpr(this, lst);
        }

        public static CallExpr Eval(params Expr[] args)
        {
            System.Diagnostics.Trace.Assert(args.Length > 0, "Eval([1+])");
            return new CallExpr(string.Empty, args);
        }
        public static CallExpr let(params Expr[] args)
        {
            System.Diagnostics.Trace.Assert(args.Length == 2, "let([2])");
            return new CallExpr(FuncDefs_Core.let, args);
        }
        public static CallExpr IF(params Expr[] args)
        {
            System.Diagnostics.Trace.Assert(args.Length == 3, "IF([2])");
            return new CallExpr(FuncDefs_Core.IF, args);
        }
    }

    public class ArrayExpr : MultiExpr
    {
        public ArrayExpr(IList<Expr> args)
            : base(ExprType.NewArrayInit, args)
        { }

        public ArrayExpr(params Expr[] args)
            : base(ExprType.NewArrayInit, args)
        { }

        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            sb.Append('{'); column++;
            var multiline = bldStr(sb, args, ", ", sb.Length - 1, nestingLevel, ref column);
            if (multiline)
                NewLine(sb, nestingLevel, ref column);
            sb.Append('}'); column++;
            return multiline;
        }

        public override Expr Visit(Func<Expr, Expr> visitor)
        {
            var lst = Visit(args, visitor);
            if (lst == args)
                return this;
            else return new ArrayExpr(lst);
        }
    }

    public class SequenceExpr : MultiExpr
    {
        public SequenceExpr(IList<Expr> args)
            : base(ExprType.Sequence, args)
        { }
        public SequenceExpr(params Expr[] args)
            : base(ExprType.Sequence, args)
        { }
        public override bool buildString(StringBuilder sb, int nestingLevel, ref int column)
        {
            var multiline = bldStr(sb, args, " ", sb.Length - 1, nestingLevel, ref column);
            if (multiline)
                NewLine(sb, nestingLevel, ref column);
            return multiline;
        }

        public override Expr Visit(Func<Expr, Expr> visitor)
        {
            var lst = Visit(args, visitor);
            if (lst == args)
                return this;
            else return new SequenceExpr(lst);
        }

        public static Expr New(IList<Expr> args)
        { return (args.Count > 1) ? new SequenceExpr(args) : args[0]; }
    }
}
