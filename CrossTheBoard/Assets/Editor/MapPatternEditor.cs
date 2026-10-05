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

    public static MapPattern CreatePattern(string name, int length, string themeId)
    {
        if (length < MapPattern.MinimumLength || length > MapPattern.MaximumLength || string.IsNullOrWhiteSpace(themeId))
            throw new ArgumentException($"A pattern requires a theme and a length of {MapPattern.MinimumLength}–{MapPattern.MaximumLength} rows.");
        var root = new GameObject(name, typeof(Grid), typeof(MapPattern));
        var pattern = root.GetComponent<MapPattern>();
        var ground = new GameObject("Ground", typeof(Tilemap), typeof(TilemapRenderer));
        ground.transform.SetParent(root.transform, false);
        var structures = new GameObject("Structures", typeof(Tilemap), typeof(TilemapRenderer));
        structures.transform.SetParent(root.transform, false);
        structures.GetComponent<TilemapRenderer>().sortingOrder = 1;
        var data = new SerializedObject(pattern);
        data.FindProperty("_length").intValue = length;
        data.FindProperty("_themeId").stringValue = themeId;
        data.FindProperty("_ground").objectReferenceValue = ground.GetComponent<Tilemap>();
        data.FindProperty("_structures").objectReferenceValue = structures.GetComponent<Tilemap>();
        data.ApplyModifiedPropertiesWithoutUndo();
        ResizeGround(pattern);
        return pattern;
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
