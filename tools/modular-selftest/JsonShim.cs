// HEADLESS ONLY. Never compiled into the Unity project (it lives under tools/).
//
// UnityEngine.JsonUtility lives in UnityEngine.JSONSerializeModule, a native
// module that cannot run outside the player. This is a small managed stand-in
// with the same name and the same rules the modular-ship data relies on:
//   - public instance fields and [SerializeField] fields; [NonSerialized],
//     static, const and readonly fields are skipped;
//   - int/long/float/double/bool/string, enums as integers, arrays, List<T>,
//     nested [Serializable] classes and Unity structs (Vector3 -> {x,y,z});
//   - null strings are written as "", null lists/arrays as [], null nested
//     objects as a default instance;
//   - FromJson constructs the object (field initialisers run), fills only
//     the fields present, ignores unknown fields;
//   - numbers use the invariant culture, floats round-trip ("R").
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace UnityEngine
{
    public static class JsonUtility
    {
        public static string ToJson(object obj) => ToJson(obj, false);

        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null) return "";
            var sb = new StringBuilder();
            WriteObject(sb, obj, obj.GetType());
            return sb.ToString(); // prettyPrint ignored: layout is not part of the contract
        }

        public static T FromJson<T>(string json) => (T)FromJson(json, typeof(T));

        public static object FromJson(string json, Type type)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            var tree = Parse(json, ref i);
            SkipWs(json, ref i);
            if (i != json.Length) throw new ArgumentException("JSON parse error: trailing characters at " + i);
            if (!(tree is Dictionary<string, object> d)) throw new ArgumentException("JSON must represent an object type.");
            return Bind(d, type, Activator.CreateInstance(type));
        }

        /// Fills only the fields present in `json` on an existing object
        /// (game save code uses it; tools/selftest-outside-editor shares this shim).
        public static void FromJsonOverwrite(string json, object target)
        {
            if (string.IsNullOrEmpty(json) || target == null) return;
            int i = 0;
            var tree = Parse(json, ref i);
            if (!(tree is Dictionary<string, object> d)) throw new ArgumentException("JSON must represent an object type.");
            Bind(d, target.GetType(), target);
        }

        // ---- reflection ---------------------------------------------------

        static IEnumerable<FieldInfo> Fields(Type t)
        {
            var chain = new List<Type>();
            for (var x = t; x != null && x != typeof(object) && x != typeof(ValueType); x = x.BaseType) chain.Insert(0, x);
            foreach (var x in chain)
                foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsNotSerialized || f.IsInitOnly || f.IsLiteral) continue;
                    if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                    yield return f;
                }
        }

        static bool IsList(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
        static Type ElementOf(Type t) => t.IsArray ? t.GetElementType() : t.GetGenericArguments()[0];

        // ---- writing ------------------------------------------------------

        static void WriteObject(StringBuilder sb, object o, Type t)
        {
            if (o == null) o = Activator.CreateInstance(t);
            sb.Append('{');
            bool first = true;
            foreach (var f in Fields(t))
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, f.Name);
                sb.Append(':');
                WriteValue(sb, f.GetValue(o), f.FieldType);
            }
            sb.Append('}');
        }

        static void WriteValue(StringBuilder sb, object v, Type t)
        {
            if (t == typeof(string)) { WriteString(sb, (string)v ?? ""); return; }
            if (t == typeof(bool)) { sb.Append((bool)v ? "true" : "false"); return; }
            if (t == typeof(float)) { sb.Append(((float)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (t == typeof(double)) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (t.IsEnum) { sb.Append(System.Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
            if (t.IsPrimitive) { sb.Append(System.Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (t.IsArray || IsList(t))
            {
                var et = ElementOf(t);
                sb.Append('[');
                if (v != null)
                {
                    bool first = true;
                    foreach (var x in (IEnumerable)v)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteValue(sb, x, et);
                    }
                }
                sb.Append(']');
                return;
            }
            WriteObject(sb, v, t);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- binding ------------------------------------------------------

        static object Bind(Dictionary<string, object> d, Type t, object target)
        {
            foreach (var f in Fields(t))
            {
                if (!d.TryGetValue(f.Name, out var raw)) continue; // missing: keep the default
                f.SetValue(target, Convert(raw, f.FieldType, f.GetValue(target)));
            }
            return target;
        }

        static object Convert(object raw, Type t, object current)
        {
            if (t == typeof(string)) return raw as string ?? (raw == null ? "" : raw.ToString());
            if (t == typeof(bool)) return raw is bool b ? b : false;
            if (t.IsEnum) return Enum.ToObject(t, raw is NumberText ne ? long.Parse(ne.text, CultureInfo.InvariantCulture) : 0L);
            if (t.IsPrimitive)
            {
                if (!(raw is NumberText n)) return Activator.CreateInstance(t);
                if (t == typeof(float)) return float.Parse(n.text, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (t == typeof(double)) return double.Parse(n.text, NumberStyles.Float, CultureInfo.InvariantCulture);
                double dv = double.Parse(n.text, NumberStyles.Float, CultureInfo.InvariantCulture);
                return System.Convert.ChangeType(Math.Truncate(dv), t, CultureInfo.InvariantCulture);
            }
            if (t.IsArray || IsList(t))
            {
                var et = ElementOf(t);
                var items = raw as List<object> ?? new List<object>();
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(et));
                foreach (var x in items) list.Add(Convert(x, et, null));
                if (IsList(t)) return list;
                var arr = Array.CreateInstance(et, list.Count);
                list.CopyTo(arr, 0);
                return arr;
            }
            var obj = current ?? Activator.CreateInstance(t);
            if (raw is Dictionary<string, object> d) obj = Bind(d, t, obj);
            return obj;
        }

        // ---- parsing ------------------------------------------------------

        sealed class NumberText { public string text; }

        static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Parse(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new ArgumentException("JSON parse error: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs(s, ref i);
                    string key = ParseString(s, ref i);
                    SkipWs(s, ref i);
                    Expect(s, ref i, ':');
                    d[key] = Parse(s, ref i);
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, '}');
                    return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Parse(s, ref i));
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, ']');
                    return l;
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new ArgumentException($"JSON parse error: unexpected '{c}' at {i}");
            return new NumberText { text = s.Substring(start, i - start) };
        }

        static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new ArgumentException($"JSON parse error: expected '{c}' at {i}");
            i++;
        }

        static string ParseString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            Expect(s, ref i, '"');
            return sb.ToString();
        }
    }
}
