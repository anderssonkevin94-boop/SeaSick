using UnityEditor;

public static class RefreshOnly
{
    public static void Execute()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
    }
}
