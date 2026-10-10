using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a playable grid aligned with the blue hexagons in ConceptArena.png.
/// The artwork remains the visible floor; transparent HexCell objects provide
/// coordinates, colliders, occupancy and movement highlights at runtime.
/// </summary>
public static class BuildConceptMap
{
    private const string ImagePath = "Assets/Game/Art/Maps/ConceptArena.png";
    private const string CellPath = "Assets/Game/Prefabs/Board/ConceptMapCell.prefab";
    private const string MapPath = "Assets/Game/Prefabs/Board/ConceptMap.prefab";
    private const string ScenePath = "Assets/Game/Scenes/ConceptMap.unity";

    [MenuItem("Game/Build Concept Map")]
    public static void Build()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(ImagePath);
        if (importer == null)
            throw new System.InvalidOperationException("Map image is missing: " + ImagePath);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 180;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        var mapSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ImagePath);
        var placeholder = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/Placeholders/HexCell.png");
        if (mapSprite == null || placeholder == null)
            throw new System.InvalidOperationException("A map or hex cell sprite could not be loaded.");

        HexCell mapCell = CreateMapCell(placeholder);
        GameObject mapPrefab = CreateMapPrefab(mapSprite, mapCell);
        CreateScene(mapPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Concept map built: 63 walkable hex cells, " + ScenePath);
    }

    private static HexCell CreateMapCell(Sprite sprite)
    {
        var go = new GameObject("ConceptMapCell");
        go.transform.localScale = new Vector3(0.59f, 0.54f, 1f);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;
        renderer.color = new Color(1f, 1f, 1f, 0f);

        var collider = go.AddComponent<PolygonCollider2D>();
        collider.pathCount = 1;
        collider.SetPath(0, new[] {
            new Vector2(0.93f, 0), new Vector2(0.465f, 0.805f),
            new Vector2(-0.465f, 0.805f), new Vector2(-0.93f, 0),
            new Vector2(-0.465f, -0.805f), new Vector2(0.465f, -0.805f)
        });

        var cell = go.AddComponent<HexCell>();
        cell.spriteRenderer = renderer;
        cell.defaultColor = new Color(1f, 1f, 1f, 0f);
        cell.highlightColor = new Color(0.35f, 0.93f, 1f, 0.45f);

        PrefabUtility.SaveAsPrefabAsset(go, CellPath);
        Object.DestroyImmediate(go);
        return AssetDatabase.LoadAssetAtPath<GameObject>(CellPath).GetComponent<HexCell>();
    }

    private static GameObject CreateMapPrefab(Sprite sprite, HexCell cell)
    {
        var go = new GameObject("ConceptMap");
        var background = go.AddComponent<SpriteRenderer>();
        background.sprite = sprite;
        background.sortingOrder = -20;

        var grid = go.AddComponent<HexGrid>();
        grid.cellPrefab = cell;
        grid.generateOnStart = true;
        grid.useCustomShape = true;
        grid.customCells = GetArtworkCells();
        grid.orientation = HexOrientation.FlatTop;
        grid.gridRadius = 5;

        // Derived from the artwork: grid center approximately (1434,1600) px,
        // horizontal column spacing 182 px, vertical row spacing 156 px.
        grid.hexSize = 0.674f;
        grid.originOffset = new Vector2(-0.567f, -2.389f);
        grid.verticalScale = 0.744f;

        PrefabUtility.SaveAsPrefabAsset(go, MapPath);
        Object.DestroyImmediate(go);
        return AssetDatabase.LoadAssetAtPath<GameObject>(MapPath);
    }

    private static List<HexCoord> GetArtworkCells()
    {
        // q column -> inclusive r range, traced from the painted outlines.
        int[,] rows = {
            { -5, 1, 4 }, { -4, 0, 4 }, { -3, -1, 4 },
            { -2, -2, 4 }, { -1, -2, 3 }, { 0, -3, 3 },
            { 1, -3, 2 }, { 2, -4, 2 }, { 3, -4, 1 },
            { 4, -4, 0 }, { 5, -4, -1 }
        };
        var result = new List<HexCoord>(63);
        for (int i = 0; i < rows.GetLength(0); i++)
            for (int r = rows[i, 1]; r <= rows[i, 2]; r++)
                result.Add(new HexCoord(rows[i, 0], r));
        return result;
    }

    private static void CreateScene(GameObject mapPrefab)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 6.7f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.white;
        cameraObject.AddComponent<AudioListener>();

        PrefabUtility.InstantiatePrefab(mapPrefab, scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
