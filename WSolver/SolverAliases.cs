using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace W.Expressions
{
    public class SolverAliases : IEqualityComparer<string>, IDictionary<string, object>
    {
        internal static readonly SolverAliases Empty = new SolverAliases();

        Dictionary<string, List<KeyValuePair<int, string>>> alias2realname = new Dictionary<string, List<KeyValuePair<int, string>>>();

        Dictionary<string, List<string>> realname2aliases = new Dictionary<string, List<string>>();

        string Add(string alias, string targetName, bool push, int priority)
        {
            var va = W.Common.ValueInfo.Create(alias).ToString();
            var vn = W.Common.ValueInfo.Create(targetName).ToString();
            var realName = GetRealName(vn);
            if (va == vn || va == realName)
                throw new ArgumentException("SolverAliases.Add: the alias and target name must differ //" + va);
            List<KeyValuePair<int, string>> lst;
            if (!alias2realname.TryGetValue(va, out lst))
            {
                lst = new List<KeyValuePair<int, string>>(1);
                alias2realname[va] = lst;
            }
            string prevRealName = null;
            string currRealName = realName;
            if (push)
            {   // push new realname
                if (lst.Count > 0)
                {
                    var p = lst[0];
                    if (p.Key < 0)
                        prevRealName = p.Value;
                }
                lst.Insert(0, new KeyValuePair<int, string>(-1, realName));
            }
            else
            {   // update realname according priority
                if (priority < 0)
                    throw new ArgumentException("SolverAliases.Add: priority value must be zero or positive // instead of " + priority.ToString());
                if (lst.Count > 0)
                {
                    var p = lst[lst.Count - 1];
                    if (p.Key > priority && p.Value != realName)
                    {
                        prevRealName = p.Value;
                        var pn = new KeyValuePair<int, string>(priority, realName);
                        lst[lst.Count - 1] = pn;
                    }
                    else currRealName = null;
                }
                else lst.Add(new KeyValuePair<int, string>(priority, realName));
            }
            if (prevRealName != currRealName)
            {
                if (prevRealName != null)
                    realname2aliases[prevRealName].Remove(va);
                if (currRealName != null)
                {
                    List<string> alst;
                    if (!realname2aliases.TryGetValue(currRealName, out alst))
                    {
                        alst = new List<string>(1);
                        realname2aliases[currRealName] = alst;
                    }
                    alst.Add(va);
                }
            }
            return prevRealName ?? string.Empty;
        }

        public string GetRealName(string name)
        {
            List<KeyValuePair<int, string>> lst;
            if (!alias2realname.TryGetValue(name, out lst) || lst.Count == 0)
                return name;
            return lst[0].Value;
        }

        public string Set(string alias, string targetName, int priority = int.MaxValue)
        { return Add(alias, targetName, false, priority); }

        public string Push(string alias, string targetName)
        { return Add(alias, targetName, true, 0); }

        public string Pop(string name)
        {
            List<KeyValuePair<int, string>> lst;
            if (alias2realname.TryGetValue(name, out lst) && lst.Count > 0 && lst[0].Key < 0)
            {
                var prev = lst[0].Value;
                lst.RemoveAt(0);
                realname2aliases[prev].Remove(name);
                return prev;
            }
            else
                throw new InvalidOperationException("SolverAliases.Pop('" + name + "'): Can't pop because no pushed realname found");

        }

        public T AtKey<T>(IDictionary<string, T> dict, string param, T defaultValue)
        {
            T value;
            if (dict.TryGetValue(param, out value))
                return value;
            var realName = GetRealName(param);
            if (dict.TryGetValue(realName, out value))
                return value;
            return defaultValue;
        }

        public void SetAt<T>(IDictionary<string, T> dict, string key, T value)
        {
            dict[key] = value;
            string alias = GetRealName(key);
            dict[alias] = value;
        }

        public string this[string name] { get { return GetRealName(name); } }

        public string[] ToRealNames(string[] paramz)
        {
            if (alias2realname.Count == 0)
                return paramz;
            string[] realNames = null;
            int n = paramz.Length;
            for (int i = n - 1; i >= 0; i--)
            {
                string prm = paramz[i];
                string name = GetRealName(prm);
                if (prm == name)
                    continue;
                if (realNames == null)
                {
                    realNames = new string[n];
                    Array.Copy(paramz, realNames, n);
                }
                realNames[i] = name;
            }
            return realNames ?? paramz;
        }

        public IEnumerable<string> RealNameAndAliasesOf(string nameOrAlias)
        {
            string realName = GetRealName(nameOrAlias);
            yield return realName;
            List<string> aliases;
            if (realname2aliases.TryGetValue(realName, out aliases))
                foreach (var s in aliases)
                    yield return s;
        }

        public Dictionary<string, int> GetKey2Ndx_WithAllNames(IDictionary<string, int> Key2Ndx)
        {
            var res = new Dictionary<string, int>(Key2Ndx.Count * 2);
            foreach (var pair in Key2Ndx)
            {
                var realName = GetRealName(pair.Key);
                int i;
                if (res.TryGetValue(realName, out i))
                {
                    if (i != pair.Value)
                        throw new SolverException(string.Format(
                            "GetKey2Ndx_WithAllNames: key value mismatch // {0}={1}, but {2}={3}"
                            , realName, i, pair.Key, pair.Value
                            ));
                    continue;
                }
                foreach (var s in RealNameAndAliasesOf(pair.Key))
                    res[s] = pair.Value;
            }
            return res;
        }

        public Dictionary<string, int> GetKey2Ndx_OnlyRealNames(IDictionary<string, int> Key2Ndx)
        {
            var res = new Dictionary<string, int>(Key2Ndx.Count);
            foreach (var pair in Key2Ndx)
            {
                var realName = GetRealName(pair.Key);
                int i;
                if (res.TryGetValue(realName, out i))
                {
                    if (i != pair.Value)
                        throw new SolverException(string.Format(
                            "GetKey2Ndx_OnlyRealNames: key value mismatch // {0}={1}, but {2}={3}"
                            , realName, i, pair.Key, pair.Value
                            ));
                }
                else res[realName] = pair.Value;
            }
            return res;
        }

        #region IDictionary<string, object> implementation
        void IDictionary<string, object>.Add(string key, object value) { throw new NotSupportedException(); }
        bool IDictionary<string, object>.ContainsKey(string key) { return alias2realname.ContainsKey(key); }
        ICollection<string> IDictionary<string, object>.Keys { get { return alias2realname.Keys; } }
        bool IDictionary<string, object>.Remove(string key) { throw new NotSupportedException(); }
        bool IDictionary<string, object>.TryGetValue(string key, out object value)
        {
            List<KeyValuePair<int, string>> lst;
            if (!alias2realname.TryGetValue(key, out lst) || lst.Count == 0)
            {
                value = null;
                return false;
            }
            value = lst[0].Value;
            return true;
        }
        ICollection<object> IDictionary<string, object>.Values
        {
            get
            {
                var res = new List<object>(alias2realname.Count);
                foreach (var v in alias2realname.Values)
                    res.Add(v);
                return res.AsReadOnly();
            }
        }
        object IDictionary<string, object>.this[string key] { get { return alias2realname[key]; } set { throw new NotSupportedException(); } }
        void ICollection<KeyValuePair<string, object>>.Add(KeyValuePair<string, object> item) { throw new NotSupportedException(); }
        void ICollection<KeyValuePair<string, object>>.Clear() { throw new NotSupportedException(); }
        bool ICollection<KeyValuePair<string, object>>.Contains(KeyValuePair<string, object> item) { throw new NotImplementedException(); }
        void ICollection<KeyValuePair<string, object>>.CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) { throw new NotImplementedException(); }
        int ICollection<KeyValuePair<string, object>>.Count { get { return alias2realname.Count; } }
        bool ICollection<KeyValuePair<string, object>>.IsReadOnly { get { return true; } }
        bool ICollection<KeyValuePair<string, object>>.Remove(KeyValuePair<string, object> item) { throw new NotSupportedException(); }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            foreach (var pair in alias2realname)
                if (pair.Value.Count > 0)
                    yield return new KeyValuePair<string, object>(pair.Key, pair.Value[0]);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        { return ((IEnumerable<KeyValuePair<string, object>>)this).GetEnumerator(); }
        #endregion

        #region IEqualityComparer<string> implementation
        public bool Equals(string x, string y)
        {
            if (x == y || GetRealName(x) == y)
                return true;
            else
                return false;
        }

        public int GetHashCode(string obj)
        {
            var s = GetRealName(obj);
            if (s == obj)
                return s.GetHashCode();
            else
                return Math.Min(s.GetHashCode(), obj.GetHashCode());
        }
        #endregion
    }
}
