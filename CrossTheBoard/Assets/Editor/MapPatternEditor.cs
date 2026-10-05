using System;
using System.IO;
using CrossTheBoard;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

[CustomEditor(typeof(MapPattern))]
public sealed class MapPatternEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var pattern = (MapPattern)target;
        if (GUILayout.Button("Resize / Fill Ground"))
        {
            Undo.RecordObject(pattern.Ground, "Resize pattern ground");
            ResizeGround(pattern);
            EditorUtility.SetDirty(pattern.Ground);
        }
        if (GUILayout.Button("Validate Pattern"))
        {
            pattern.Validate();
            Debug.Log($"Pattern '{pattern.name}' is valid.", pattern);
        }
        if (pattern.Structures != null && GUILayout.Button("Convert Obstacles to Sprite Objects"))
        {
            ConvertObstacles(pattern);
            EditorUtility.SetDirty(pattern);
        }
    }

    [MenuItem("Assets/Create/CrossTheBoard/Map Pattern")]
    private static void CreatePatternAsset()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (!AssetDatabase.IsValidFolder(path) || path != "Assets/Resources/MapPatterns" &&
            !path.StartsWith("Assets/Resources/MapPatterns/", StringComparison.Ordinal))
            path = "Assets/Resources/MapPatterns";
        EnsureFolder(path);
        path = AssetDatabase.GenerateUniqueAssetPath(path + "/MapPattern.prefab");
        var pattern = CreatePattern("MapPattern", MapPattern.MinimumLength, "meadow");
        try
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(pattern.gameObject, path);
            if (prefab == null) throw new InvalidOperationException($"Could not create pattern at '{path}'.");
            Selection.activeObject = prefab;
        }
        finally { DestroyImmediate(pattern.gameObject); }
    }

    public static MapPattern CreatePattern(string name, int length, string themeId, bool legacyStructures = false)
    {
        if (length < MapPattern.MinimumLength || length > MapPattern.MaximumLength || string.IsNullOrWhiteSpace(themeId))
            throw new ArgumentException($"A pattern requires a theme and a length of {MapPattern.MinimumLength}–{MapPattern.MaximumLength} rows.");
        var root = new GameObject(name, typeof(Grid), typeof(MapPattern));
        var pattern = root.GetComponent<MapPattern>();
        var ground = new GameObject("Ground", typeof(Tilemap), typeof(TilemapRenderer));
        ground.transform.SetParent(root.transform, false);
        var data = new SerializedObject(pattern);
        data.FindProperty("_length").intValue = length;
        data.FindProperty("_themeId").stringValue = themeId;
        data.FindProperty("_ground").objectReferenceValue = ground.GetComponent<Tilemap>();
        if (legacyStructures)
        {
            var structures = new GameObject("Structures", typeof(Tilemap), typeof(TilemapRenderer));
            structures.transform.SetParent(root.transform, false);
            structures.GetComponent<TilemapRenderer>().sortingOrder = 1;
            data.FindProperty("_structures").objectReferenceValue = structures.GetComponent<Tilemap>();
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        ResizeGround(pattern);
        return pattern;
    }

    public static void ConvertObstacles(MapPattern pattern)
    {
        if (pattern.Structures != null)
        {
            foreach (var cell in pattern.Structures.cellBounds.allPositionsWithin)
            {
                var tile = pattern.Structures.GetTile(cell);
                if (tile == null) continue;
                var obstacle = CreateObstacle(pattern, new Vector2Int(cell.x, cell.y), tile);
                var visual = obstacle.GetComponentInChildren<SpriteRenderer>();
                visual.color *= pattern.Structures.GetColor(cell) * pattern.Structures.color;
                visual.transform.localRotation = pattern.Structures.GetTransformMatrix(cell).rotation;
                visual.transform.localScale = pattern.Structures.GetTransformMatrix(cell).lossyScale;
            }
            DestroyImmediate(pattern.Structures.gameObject);
        }
        foreach (var definition in pattern.MovingObstacles)
            CreateObstacle(pattern, definition.position, definition.tile, definition);
        var data = new SerializedObject(pattern);
        data.FindProperty("_structures").objectReferenceValue = null;
        data.FindProperty("_movingObstacles").arraySize = 0;
        data.ApplyModifiedPropertiesWithoutUndo();
        pattern.Validate();
    }

    public static MapObstacle CreateObstacle(MapPattern pattern, Vector2Int cell, TileBase tile, MovingObstacleDefinition movement = null)
    {
        var instance = new GameObject(tile.name, typeof(MapObstacle));
        instance.transform.SetParent(pattern.transform, false);
        instance.transform.localPosition = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0);
        var obstacle = instance.GetComponent<MapObstacle>();
        typeof(MapObstacle).GetMethod("InitializeTile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(obstacle, new object[] { tile, movement });
        foreach (var renderer in instance.GetComponentsInChildren<SpriteRenderer>())
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Gameplay/SpriteMaterial.mat");
        return obstacle;
    }

    public static void MigrateSamples()
    {
        try
        {
            if (Application.companyName != "CodexValidation")
                throw new InvalidOperationException("Batch migration requires the isolated validation project.");
            EnsureFolder("Assets/Resources/MapObjects");
            string stonePath = "Assets/Resources/MapObjects/SteppingStone.prefab";
            if (AssetDatabase.LoadAssetAtPath<SteppingStone>(stonePath) == null)
            {
                var stone = new GameObject("SteppingStone", typeof(SpriteRenderer), typeof(SteppingStone));
                var renderer = stone.GetComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Tile>("Assets/Gameplay/GroundTile.asset").sprite;
                renderer.sortingOrder = 1;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Gameplay/SpriteMaterial.mat");
                PrefabUtility.SaveAsPrefabAsset(stone, stonePath);
                DestroyImmediate(stone);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/MapPatterns" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var pattern = root.GetComponent<MapPattern>();
                    ConvertObstacles(pattern);
                    var data = new SerializedObject(pattern);
                    var rows = data.FindProperty("_lavaRows");
                    for (int i = 0; i < rows.arraySize; i++)
                    {
                        var row = rows.GetArrayElementAtIndex(i);
                        var columns = row.FindPropertyRelative("steppingStoneColumns");
                        var stones = row.FindPropertyRelative("steppingStones");
                        if (stones.arraySize > 0) continue;
                        stones.arraySize = columns.arraySize;
                        for (int j = 0; j < columns.arraySize; j++)
                        {
                            var stone = stones.GetArrayElementAtIndex(j);
                            stone.FindPropertyRelative("column").intValue = columns.GetArrayElementAtIndex(j).intValue;
                            stone.FindPropertyRelative("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SteppingStone>(stonePath);
                            stone.FindPropertyRelative("speed").floatValue = 1f;
                            stone.FindPropertyRelative("direction").intValue = 1;
                            stone.FindPropertyRelative("distance").intValue = 2;
                        }
                    }
                    data.ApplyModifiedPropertiesWithoutUndo();
                    pattern.Validate();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[MapPatternEditor] Migrated pattern obstacles without changing layout or GUIDs.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void ResizeGround(MapPattern pattern)
    {
        if (pattern.Length < MapPattern.MinimumLength || pattern.Length > MapPattern.MaximumLength || pattern.Ground == null)
            throw new InvalidOperationException($"Pattern ground and a length of {MapPattern.MinimumLength}–{MapPattern.MaximumLength} rows are required.");
        var tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/GroundTile.asset");
        if (tile == null) throw new InvalidOperationException("The default ground tile is missing.");
        foreach (var position in pattern.Ground.cellBounds.allPositionsWithin)
            if (position.z != 0 || position.x < -MapManager.HalfWidth || position.x > MapManager.HalfWidth || position.y < 0 || position.y >= pattern.Length)
                pattern.Ground.SetTile(position, null);
        for (int row = 0; row < pattern.Length; row++)
            for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
            {
                var position = new Vector3Int(x, row, 0);
                if (!pattern.Ground.HasTile(position)) pattern.Ground.SetTile(position, tile);
            }
        pattern.Ground.CompressBounds();
    }

    public static void EnsureFolder(string path)
    {
        if (path == "Assets") return;
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains(".."))
            throw new ArgumentException("Pattern assets must be inside Assets.", nameof(path));
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        if (parent != "Assets") EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
