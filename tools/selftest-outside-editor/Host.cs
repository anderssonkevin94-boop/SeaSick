// HEADLESS ONLY. Never compiled into the Unity project (it lives under tools/).
//
// Runs plain-C# self-tests from a freshly compiled Assembly-CSharp on Unity's
// bundled .NET, with no editor: see run.sh next to this file.
//
//   t.exe <path/to/Assembly-CSharp.dll> <Namespace.Type.Method> [...]
//
// Each method must be static and take no arguments. Pass/fail:
//   bool   -> true passes;
//   string -> passes when it starts with "PASS" or ends with "ALL PASS";
//   void   -> passes when it does not throw.
// Debug.Log* output is redirected to stdout (the native log handler is not
// available outside the player). Exit code = number of failed methods.
using System;
using System.Reflection;
using UnityEngine;

sealed class ConsoleLog : ILogHandler
{
    public void LogFormat(LogType t, UnityEngine.Object c, string f, params object[] a)
        => Console.WriteLine(a != null && a.Length > 0 ? string.Format(f, a) : f);
    public void LogException(Exception e, UnityEngine.Object c) => Console.WriteLine(e);
}

static class Host
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: t.exe <Assembly-CSharp.dll> <Namespace.Type.Method> [...]"); return 99; }
        try { Debug.unityLogger.logHandler = new ConsoleLog(); }
        catch (Exception e) { Console.WriteLine("(could not redirect Debug.Log: " + e.Message + ")"); }

        var asm = Assembly.LoadFrom(args[0]);
        int failed = 0;
        for (int k = 1; k < args.Length; k++)
        {
            string name = args[k];
            int dot = name.LastIndexOf('.');
            var type = dot > 0 ? asm.GetType(name.Substring(0, dot)) : null;
            var m = type?.GetMethod(name.Substring(dot + 1),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (m == null) { Console.WriteLine($"== {name}: NOT FOUND (static, no arguments)"); failed++; continue; }

            bool ok;
            string shown;
            try
            {
                object r = m.Invoke(null, null);
                if (r is bool b) { ok = b; shown = b.ToString(); }
                else if (r is string s)
                {
                    string t = s.Trim();
                    ok = t.StartsWith("PASS") || t.EndsWith("ALL PASS");
                    shown = t;
                }
                else { ok = true; shown = r == null ? "(void)" : r.ToString(); }
            }
            catch (TargetInvocationException e)
            {
                ok = false;
                shown = "THREW " + e.InnerException;
            }
            if (!ok) failed++;
            Console.WriteLine($"== {name}: {(ok ? "PASS" : "FAIL")} -- {shown}");
        }
        Console.WriteLine(failed == 0 ? "== all methods passed" : $"== {failed} method(s) failed");
        return failed;
    }
}
