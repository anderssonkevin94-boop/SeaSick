// HEADLESS ONLY: entry point for tools/modular-selftest.sh.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class ModularSelfTestMain
{
    public static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : ".";
        string res = Path.Combine(root, "Assets/_Project/Resources");
        string dir = Path.Combine(res, "ShipModules");
        var files = Directory.GetFiles(Path.Combine(dir, "Modules"), "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var texts = files.Select(File.ReadAllText).ToList();
        var names = files.Select(Path.GetFileNameWithoutExtension).ToList();
        // A visual part "resolves" headlessly when its FBX exists on disk
        // (in Unity the same gate asks Resources.Load after import).
        Func<string, bool> exists = p => File.Exists(Path.Combine(res, p + ".fbx"));
        string hullForm = Path.Combine(res, "Steamer/hullform.json");
        bool ok = SeaSick.Ship.Modular.ModularShipSelfTest.RunWith(File.ReadAllText(Path.Combine(dir, "standards.json")), texts, names, exists,
            File.Exists(hullForm) ? File.ReadAllText(hullForm) : null,
            path => { var f = Path.Combine(res, path + ".json"); return File.Exists(f) ? File.ReadAllText(f) : null; });
        Console.Write(SeaSick.Ship.Modular.ModularShipSelfTest.Report);
        return ok ? 0 : 1;
    }
}
