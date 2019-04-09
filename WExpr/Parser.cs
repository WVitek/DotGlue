using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace W.Expressions
{
    //public delegate TResult Func<T, TResult>(T arg);
    //public delegate TResult Func<T1, T2, TResult>(T1 arg1, T2 arg2);
    //public delegate TResult Func<T1, T2, T3, TResult>(T1 arg1, T2 arg2, T3 arg3);
    //public delegate TResult Func<T1, T2, T3, T4, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);

    public class ParserException : System.Exception
    {
        public readonly int atNdx;
        public ParserException(int atNdx, string msg) : base(msg) { this.atNdx = atNdx; }
        public override string ToString() { return string.Format("@{0}: {1}", atNdx, base.ToString()); }
    }

    public class Parser
    {
        /// <summary>
        /// Reverse Polish Notation
        /// </summary>
        public Stack<Expr> stack = new Stack<Expr>();
        /// <summary>
        /// Source text of expression
        /// </summary>
        public string txt;
        /// <summary>
        /// Current position of parsing
        /// </summary>
        public int ndx;
        /// <summary>
        /// Allow SequenceExpr
        /// </summary>
        public bool AllowSequences;
        /// <summary>
        /// Interpret double-quoted string as identifier (SQL-style)
        /// </summary>
        public bool DoubleQuotedAsIdentifer;
        /// <summary>
        /// Treat unknown/unexpected chars as references
        /// </summary>
        public bool UnexpectedCharsAsReferences;

        struct op
        {
            public readonly string code;
            public readonly Action<Stack<Expr>> toExpr;

            public op(string code, Action<Stack<Expr>> toExpr)
            { this.code = code; this.toExpr = toExpr; }

            public static op unary(string code, Func<Expr, Expr> toExpr)
            { return new op(code, s => s.Push(toExpr(s.Pop()))); }

            public static op binary(string code, Func<Expr, Expr, Expr> toExpr)
            { return new op(code, s => { var y = s.Pop(); var x = s.Pop(); s.Push(toExpr(x, y)); }); }

            public static op unary(string code, ExprType exprType)
            { return op.unary(code, x => new UnaryExpr(exprType, x)); }

            public static op binary(string code, ExprType exprType)
            { return op.binary(code, (x, y) => new BinaryExpr(exprType, x, y)); }

            public override string ToString() { return code; }
        }

        static readonly op[][] ops;

        static Parser()
        {
            // build 2D array[priority][op] of binary expression operands
            var priops = new List<op[]>();
            var line = new List<op>();
            int prevPriority = 0;

            foreach (ExprType t in Enum.GetValues(typeof(ExprType)))
            {
                var s = BinaryExpr.binaryOpString(t);
                if (s == null) continue;
                var p = Expr.PriorityLevel(t);
                if (p != prevPriority)
                {
                    priops.Add(line.ToArray());
                    line = new List<op>();
                    prevPriority = p;
                }
                line.Add(op.binary(s, t));
            }
            if (line.Count > 0) priops.Add(line.ToArray());
            ops = priops.ToArray();
        }

        public Parser() { }

        class Comparer : IComparer<string>
        {
            public StringComparison comparison;
            public int Compare(string x, string y) { return string.Compare(x, y, comparison); }
        }

        public Parser(string[] reservedWords, StringComparison comparison)
        {
            this.reservedWords = reservedWords;
            this.comparer = new Comparer() { comparison = comparison };
            this.comparison = comparison;
            if (reservedWords != null)
                Array.Sort(reservedWords, comparer);
        }

        public static Expr ParseToExpr(string txt)
        {
            int ndx = 0;
            return ParseToExpr(txt, ref ndx);
        }

        public static Expr ParseToExpr(string txt, ref int ndx)
        {
            var p = new Parser();
            p.txt = txt;
            p.ndx = ndx;
            p.E();
            ndx = p.ndx;
            return p.stack.Pop();
        }

        public static Expr ParseToExpr(string txt, ref int ndx, string[] reservedWords, StringComparison comparison)
        {
            var p = new Parser(reservedWords, comparison);
            p.txt = txt;
            p.ndx = ndx;
            p.E();
            ndx = p.ndx;
            return p.stack.Pop();
        }

        public static IEnumerable<Expr> ParseSequence(string txt, string[] reservedWords = null, StringComparison comparison = StringComparison.InvariantCultureIgnoreCase, bool checkCompleteness = true)
        {
            int maxNdx = txt.Length;
            var lst = new List<Expr>();
            var p = new Parser(reservedWords, comparison) { AllowSequences = true, txt = txt };
            while (p.ndx < maxNdx)
            {
                var pndx = p.ndx;
                var expr = p.ParseToExpr();
                if (checkCompleteness && p.stack.Count > 0)
                    throw new Generator.Exception("Parser.ParseSequence error: p.stack is not empty (maybe expression is not completed)");
                if (p.ndx == pndx)
                    p.ndx++;
                else
                {
                    var se = expr as SequenceExpr;
                    if (se != null)
                        foreach (var e in se.args) yield return e;
                    else
                        yield return expr;
                }
            }
        }

        public Expr ParseToExpr()
        {
            E();
            return stack.Pop();
        }

        void error(string msg)
        { throw new ParserException(ndx, msg + "// ... " + ((ndx >= 16) ? txt.Substring(ndx - 16, 16) : txt.Substring(0, ndx))); }

        static readonly char[] lineEndingChars = new char[] { '\r', '\n' };

        void skipSpaces()
        {
            while (ndx < txt.Length)
            {
                if (char.IsWhiteSpace(txt, ndx))
                    ndx++;
                else if (txt[ndx] == '/' && ndx < txt.Length - 1)
                    // skip C-style comments
                    switch (txt[ndx + 1])
                    {
                        case '/': // comment to end of line "//"
                            ndx = txt.IndexOfAny(lineEndingChars, ndx);
                            if (ndx < 0) ndx = txt.Length;
                            //else ndx += 1;
                            break;
                        case '*': // multiline comment "/* ... */"
                            ndx = txt.IndexOf("*/", ndx + 2);
                            if (ndx < 0) ndx = txt.Length;
                            else ndx += 2;
                            break;
                        default: return;
                    }
                else if (txt[ndx] == '-' && ndx < txt.Length - 1 && txt[ndx + 1] == '-')
                {
                    // skip '--' comment
                    ndx = txt.IndexOfAny(lineEndingChars, ndx);
                    if (ndx < 0) ndx = txt.Length;
                }
                else return;
            }
        }

        int IndexOfAny(op[] ops)
        {
            char c = peek();
            if (char.IsLetter(c))
            {
                for (int i = 0; i < ops.Length; i++)
                    if (string.Compare(txt, ndx, ops[i].code, 0, ops[i].code.Length, comparison) == 0)
                    {
                        int j = ndx + ops[i].code.Length;
                        if (j >= txt.Length || !char.IsLetter(txt, j))
                            return i;
                    }
            }
            else
                for (int i = 0; i < ops.Length; i++)
                    if (string.Compare(txt, ndx, ops[i].code, 0, ops[i].code.Length, StringComparison.Ordinal) == 0)
                        return i;
            return -1;
        }

        char peekAny() { if (ndx < txt.Length) return txt[ndx]; else return '\0'; }
        void acpt() { if (ndx < txt.Length) ndx++; }
        char readAny() { if (ndx < txt.Length) return txt[ndx++]; else return '\0'; }

        char peek() { skipSpaces(); return peekAny(); }
        char read() { skipSpaces(); return readAny(); }

        // E = E(iMax) *
        void E()
        {
            int i = 0;
            while (ndx < txt.Length)
            {
                int pndx = ndx;
                E(ops.Length - 1);
                i++;
                char c = peekAny();
                if (c == ',' || c == ')' || c == ']' || c == '}' || c == '\0')
                    break;
                if (ndx == pndx)
                    if (UnexpectedCharsAsReferences)
                    {
                        ndx++;
                        stack.Pop();
                        stack.Push(new ReferenceExpr(c.ToString()));
                        //i--;
                    }
                    else error("Unexpected chars (you can try UnexpectedCharsAsReferences at your own risk)");
            }
            if (i > 1)
                if (AllowSequences)
                {
                    var lst = new Expr[i];
                    while (i > 0)
                        lst[--i] = stack.Pop();
                    stack.Push(new SequenceExpr(lst));
                }
                else error("Sequences is not allowed (you can try AllowSequences at your own risk)");
        }

        // E(0) = F [ '[' E ']' ]*
        // E(i) = E(i-1) [ op[i] E(i-1) ]*
        void E(int opPriorityLevel)
        {
            if (opPriorityLevel < 0)
            {
                F();
                while (peek() == '[')
                {
                    acpt();
                    char c = peek();
                    if (c == '?')
                        acpt();
                    E();
                    if (read() != ']')
                        error("Expected ']'");
                    Expr index = stack.Pop();
                    Expr value = stack.Pop();
                    stack.Push(new IndexExpr(value, index, c == '?'));
                }
                return;
            }

            E(opPriorityLevel - 1);
            op[] ops = Parser.ops[opPriorityLevel];
            while (true)
            {
                int i = IndexOfAny(ops);
                if (i < 0) break;
                ndx += ops[i].code.Length;
                E(opPriorityLevel - 1);
                ops[i].toExpr(stack);
            }
        }

        static readonly ConstExpr nullExpr = new ConstExpr(null);

        bool CanBeFirstCharOfIdentifier(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '$' || c == ':' || c == '#' || (c == '"' && DoubleQuotedAsIdentifer);
        }

        IComparer<string> comparer;
        StringComparison comparison;
        string[] reservedWords;

        // F = ( E )
        // F = <variable>
        // F = <function> ( P )
        // F = "string"
        // F = <number>
        // F = - F
        void F()
        {
            char c = peek();
            if (c == '(')
            {
                acpt();
                Expr[] lst = ParseExprList(')');
                c = read();
                if (c != ')')
                    error("Right bracket ')' expected");
                if (lst.Length == 0)
                    error("Expression expected");
                else if (lst.Length == 1)
                    stack.Push(lst[0]);
                else stack.Push(CallExpr.Eval(lst));
            }
            else if (c == '{')
            {   // 'set' = { item1, ..., itemN }
                acpt();
                Expr[] items = ParseExprList('}');
                c = read();
                if (c != '}')
                    error("Right brace '}' expected");
                stack.Push(new ArrayExpr(items));
            }
            else if (CanBeFirstCharOfIdentifier(c))
            {
                string id = IdentifierToken(c);
                if (string.Compare(id, "TRUE", comparison) == 0 || string.Compare(id, "FALSE", comparison) == 0)
                {
                    bool value = bool.Parse(id);
                    stack.Push(new ConstExpr(value));
                }
                else
                {
                    if (reservedWords != null)
                    {
                        int i = Array.BinarySearch(reservedWords, id, comparer);
                        if (i > 0)
                        {
                            stack.Push(nullExpr);
                            return;
                        }
                    }
                    // function call
                    c = peek();
                    if (c == '(')
                    {
                        Expr[] prms = null;
                        acpt();
                        prms = ParseExprList(')');
                        c = read();
                        if (c != ')')
                            error("Right bracket ')' expected");
                        stack.Push(new CallExpr(id, prms));
                    }
                    else stack.Push(new ReferenceExpr(id));
                }
            }
            else if ((c == '"' && !DoubleQuotedAsIdentifer) || c == '\'')
                StringToken(c);
            else
            {
                string numStr = W.Common.NumberUtils.GetFloatStr(txt, ndx);
                if (numStr != null)
                {
                    object v;
                    if (numStr.Contains(".") || numStr.Contains("E") || numStr.Contains("e"))
                        v = double.Parse(numStr, System.Globalization.CultureInfo.InvariantCulture);
                    else
                    {
                        int i;
                        if (int.TryParse(numStr, out i))
                            v = i;
                        else v = long.Parse(numStr);
                    }
                    ndx += numStr.Length;
                    stack.Push(new ConstExpr(v));
                }
                else if (c == '-')
                {
                    acpt();
                    F();
                    stack.Push(new UnaryExpr(ExprType.Negate, stack.Pop()));
                }
                else stack.Push(nullExpr);// error("Expression expected");
            }
        }

        Expr[] ParseExprList(char endChar)
        {
            char c = peek();
            if (c == endChar)
                return new Expr[0];
            var prms = new List<Expr>();
            while (true)
            {
                Expr expr;
                //expr = ParseToExpr(txt, ref ndx);
                expr = ParseToExpr();
                //try { expr = ParseToExpr(txt, ref ndx); }
                //catch (ParserException e)
                //{
                //    if (e.Message.StartsWith("Expression expected"))
                //        expr = new ConstExpr(null);
                //    else throw;
                //}
                prms.Add(expr);
                c = peek();
                if (c != ',')
                    break;
                acpt();
            }
            return prms.ToArray();
        }

        string IdentifierToken(char firstChar)
        {
            if (firstChar == '"')
                return '"' + QuotedString(firstChar) + '"';

            char c = read();
            var sb = new StringBuilder();
            while (c != '\0')
            {
                sb.Append(c);
                c = peekAny();
                if (char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == ':' || c == '#' || c == '!' || c == '@')
                    acpt();
                else
                    break;
            }
            return sb.ToString();// txt.Substring(i0, ndx - i0);
        }

        enum StringTokenParseState { before, chars, quote, end };

        string QuotedString(char delimiter)
        {
            var st = new StringBuilder(); // string token
            var state = StringTokenParseState.before;
            while (state != StringTokenParseState.end)
            {
                char c = peekAny();
                switch (state)
                {
                    case StringTokenParseState.before:
                        if (c != delimiter)
                            error("Quotation mark expected (" + delimiter + ')');
                        ndx++;
                        state = StringTokenParseState.chars;
                        break;
                    case StringTokenParseState.chars:
                        if (c == delimiter)
                            state = StringTokenParseState.quote;
                        else if (c == '\0')
                            error("Unexpected end of text");
                        else st.Append(c);
                        acpt();
                        break;
                    case StringTokenParseState.quote:
                        if (c == delimiter)
                        {
                            st.Append(delimiter);
                            acpt();
                            state = StringTokenParseState.chars;
                        }
                        else state = StringTokenParseState.end;
                        break;
                }
            }
            return st.Length == 0 ? string.Empty : st.ToString();
        }

        void StringToken(char delimiter)
        {
            stack.Push(new ConstExpr(QuotedString(delimiter)));
        }
    }

}
