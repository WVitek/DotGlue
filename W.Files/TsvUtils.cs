using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace W.Files
{
    public static class TsvUtils
    {
        static readonly char[] substCharsSrc;
        static readonly char[] substCharsDst;

        static TsvUtils()
        {
            substCharsSrc = new char[] { '\n', '\r', '\t', '\\' };
            substCharsDst = new char[] { 'n', 'r', 't', '\\' };
            Array.Sort<char, char>(substCharsSrc, substCharsDst);
        }

        public static string Encode(string s)
        {
            int i = 0, L = s.Length;
            StringBuilder sb = null;
            while (i < L)
            {
                int j = s.IndexOfAny(substCharsSrc, i, L - i);
                if (j < 0)
                    break;
                if (sb == null) sb = new StringBuilder(L + 1);
                sb.Append(s.Substring(i, j - i));
                sb.Append('\\');
                int k = Array.BinarySearch<char>(substCharsSrc, s[j]);
                sb.Append(substCharsDst[k]);
                i = j + 1;
            }
            if (sb == null)
                return s;
            else return sb.ToString();
        }

        public static string Decode(string s)
        {
            StringBuilder sb = null;
            int i = 0, L = s.Length;
            while (i < L)
            {
                int j = s.IndexOf('\\', i, L - i);
                if (j < 0)
                    break;
                char c = (j + 1 == L) ? '\0' : s[j + 1];
                int k = Array.IndexOf<char>(substCharsDst, c);
                if (k < 0)
                    i = j + 1;
                else
                {
                    if (sb == null) sb = new StringBuilder(L - 1);
                    sb.Append(s.Substring(i, j - i));
                    sb.Append(substCharsSrc[k]);
                    i = j + 2;
                }
            }
            if (sb == null)
                return s;
            else return sb.ToString();
        }
    }
}
