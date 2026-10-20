using System;
using UnityEditor;
using UnityEngine;

public static class RunAssetChecks
{
    [MenuItem("Tools/Game/Check Run Assets")]
    public static void Run()
    {
        string folder = "Assets/Game/ScriptableObjects/关卡";
        string[] levels = AssetDatabase.FindAssets("t:LevelData", new[] { folder });
        Require(levels.Length == 18, "A full run needs 18 battle levels");

        for (int chapter = 1; chapter <= 2; chapter++)
        {
            for (int stage = 1; stage <= 6; stage++)
                Require(AssetDatabase.LoadAssetAtPath<LevelData>(
                    folder + "/Level_" + chapter + "_" + stage + ".asset") != null,
                    "Missing normal level " + chapter + "-" + stage);
        }

        StartingDeckChecks.Run();
        Debug.Log("Run asset checks passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
