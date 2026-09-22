using UnityEditor;
using UnityEditor.Compilation;

/// Auto Refresh may be off in Preferences, in which case an edited script sits
/// on disk and the running DLL stays stale — a probe run then measures the OLD
/// code while every timestamp check says the file changed. Forces the import.
public static class ForceRefresh
{
    public static void Execute()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        CompilationPipeline.RequestScriptCompilation();
    }
}
