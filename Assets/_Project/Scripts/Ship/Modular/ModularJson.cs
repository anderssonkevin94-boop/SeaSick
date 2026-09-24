namespace SeaSick.Ship.Modular
{
    /// The single serialization seam of the modular-ship core. In Unity this
    /// is UnityEngine.JsonUtility; the headless self-test runner
    /// (tools/modular-selftest.sh) compiles a JsonUtility-compatible shim of
    /// the same name in its place, so the SAME code does a real JSON round
    /// trip outside the editor.
    public static class ModularJson
    {
        public static string To(object o, bool pretty = false) => UnityEngine.JsonUtility.ToJson(o, pretty);
        public static T From<T>(string json) => UnityEngine.JsonUtility.FromJson<T>(json);
    }
}
