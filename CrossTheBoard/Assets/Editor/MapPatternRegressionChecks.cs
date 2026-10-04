using System;
using System.Collections;
using System.IO;
using System.Reflection;
using CrossTheBoard;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class MapPatternRegressionChecks
{
    public static void CreateSamples()
    {
        try
        {
            Require(Application.companyName == "CodexValidation", "Generate samples only in the isolated validation project.");
            CreateSample("Meadow_06", 6, "meadow", new[] { new Vector2Int(-2, 2), new Vector2Int(2, 3) },
                Array.Empty<HazardRowDefinition>(), new[] { Patrol(-3, 4) },
                new[] { new RowTriggerDefinition { id = "checkpoint", row = 3 } });
            CreateSample("Meadow_12", 12, "meadow", new[]
            {
                new Vector2Int(-3, 2), new Vector2Int(-2, 2), new Vector2Int(2, 4),
                new Vector2Int(3, 4), new Vector2Int(2, 8)
            }, new[] { new HazardRowDefinition { row = 6, steppingStoneColumns = new[] { -1, 1 } } },
                new[] { Patrol(-3, 9), Chase(0, 3) },
                new[] { new RowTriggerDefinition { id = "to_volcano", row = 10, nextThemeId = "volcano" } });
            CreateSample("Volcano_18", 18, "volcano", new[]
            {
                new Vector2Int(-3, 2), new Vector2Int(2, 9), new Vector2Int(3, 9), new Vector2Int(-2, 13)
            }, new[]
            {
                new HazardRowDefinition { row = 4, steppingStoneColumns = new[] { 0 } },
                new HazardRowDefinition { row = 5, steppingStoneColumns = new[] { 0 } }
            }, new[] { Patrol(-3, 11), Chase(3, 15) }, new[] { new RowTriggerDefinition { id = "checkpoint", row = 16 } });
            CreateSample("Volcano_30", 30, "volcano", new[]
            {
                new Vector2Int(-2, 3), new Vector2Int(2, 5), new Vector2Int(-3, 12), new Vector2Int(-2, 12),
                new Vector2Int(2, 20), new Vector2Int(3, 20), new Vector2Int(-2, 24), new Vector2Int(2, 24)
            }, new[]
            {
                new HazardRowDefinition { row = 7, steppingStoneColumns = new[] { 0 } },
                new HazardRowDefinition { row = 8, steppingStoneColumns = new[] { 0 } },
                new HazardRowDefinition { row = 16, steppingStoneColumns = new[] { -2, 0, 2 } }
            }, new[] { Patrol(-3, 10), Chase(0, 22) },
                new[] { new RowTriggerDefinition { id = "to_meadow", row = 28, nextThemeId = "meadow" } });
            AssetDatabase.SaveAssets();
            Debug.Log("[MapPatternRegressionChecks] Created four sample pattern prefabs.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static MovingObstacleDefinition Patrol(int x, int row) => new()
    {
        position = new Vector2Int(x, row), movement = ObstacleMovement.Patrol, distance = 3,
        tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/RollingRockTile.asset")
    };

    private static MovingObstacleDefinition Chase(int x, int row) => new()
    {
        position = new Vector2Int(x, row), movement = ObstacleMovement.Chase, stepInterval = 1f,
        tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/MonsterTile.asset")
    };

    private static void CreateSample(string name, int length, string theme, Vector2Int[] obstacles,
        HazardRowDefinition[] lava, MovingObstacleDefinition[] moving, RowTriggerDefinition[] triggers)
    {
        string folder = "Assets/Resources/MapPatterns/" + theme;
        MapPatternEditor.EnsureFolder(folder);
        string path = folder + "/" + name + ".prefab";
        if (File.Exists(path))
        {
            AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapPattern>().Validate();
            return;
        }
        var pattern = MapPatternEditor.CreatePattern(name, length, theme);
        try
        {
            pattern.Ground.color = theme == "meadow" ? new Color(0.58f, 0.78f, 0.5f) : new Color(0.56f, 0.48f, 0.46f);
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
            foreach (var position in obstacles) pattern.Structures.SetTile((Vector3Int)position, tile);
            Set(pattern, "_lavaRows", lava);
            Set(pattern, "_movingObstacles", moving);
            Set(pattern, "_rowTriggers", triggers);
            pattern.Validate();
            Require(PrefabUtility.SaveAsPrefabAsset(pattern.gameObject, path) != null, "Pattern prefab is saved.");
        }
        finally { UnityEngine.Object.DestroyImmediate(pattern.gameObject); }
    }

    public static void Run()
    {
        try
        {
            var prefabs = Resources.LoadAll<GameObject>("MapPatterns");
            Require(prefabs.Length >= 4, "The sample resource prefabs are discovered.");
            var lengths = new System.Collections.Generic.HashSet<int>();
            foreach (var prefab in prefabs)
            {
                var pattern = prefab.GetComponent<MapPattern>();
                pattern.Validate();
                lengths.Add(pattern.Length);
            }
            Require(lengths.IsSupersetOf(new[] { 6, 12, 18, 30 }), "Sample lengths span 6–30 rows.");
            CheckRepeatedPatterns();
            CheckThemeBoundary();
            CheckRewardCollection();
            CheckMovingObstacles();
            CheckTraversal();
            Debug.Log("[MapPatternRegressionChecks] PASS: prefab discovery, 6–30 row lengths, repeat weighting, theme boundaries, random rewards, motion and safe traversal.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static MapManager StartMap(int seed, float coinChance = 0f, float rewardChance = 0f)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        Set(map, "_coinSeed", seed);
        Set(map, "_coinChance", coinChance);
        Set(map, "_rewardChance", rewardChance);
        var gameplay = UnityEngine.Object.FindFirstObjectByType<GameplayController>();
        Set(gameplay, "_moveAchievementIds", Array.Empty<string>());
        typeof(GameStateManager).GetProperty("Instance").SetValue(null, UnityEngine.Object.FindFirstObjectByType<GameStateManager>());
        Invoke(gameplay, "Start");
        return map;
    }

    private static void CheckRepeatedPatterns()
    {
        var map = StartMap(17);
        int repeats = 0;
        MapPattern previous = null;
        var placements = (IList)Field(map, "_placedPatterns");
        for (int i = 0; i < 400; i++)
        {
            int next = (int)Field(map, "_nextPatternRow");
            Invoke(map, "EnsurePatternsThrough", next);
            var selected = (MapPattern)Field(placements[placements.Count - 1], "Template");
            if (selected == previous) repeats++;
            previous = selected;
        }
        Require(repeats > 0 && repeats < 100, "Consecutive repeats are possible but much less frequent than uniform selection.");
    }

    private static void CheckThemeBoundary()
    {
        var map = StartMap(23);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var first = ((IList)Field(map, "_placedPatterns"))[0];
        int boundary = (int)first.GetType().GetProperty("LastRow").GetValue(first) + 1;
        int changes = 0;
        map.ThemeChanged += _ => changes++;
        map.RequestThemeChange("volcano");
        Invoke(map, "EnsurePatternsThrough", boundary);
        var placements = (IList)Field(map, "_placedPatterns");
        Require((int)Field(placements[1], "FirstRow") == boundary &&
            ((MapPattern)Field(placements[0], "Template")).ThemeId == "meadow" &&
            ((MapPattern)Field(placements[1], "Template")).ThemeId == "volcano" && map.CurrentThemeId == "meadow" && changes == 0,
            "Lookahead replacement preserves the current pattern and starts the new theme at its boundary.");
        while (player.FurthestRow < boundary)
        {
            var target = SafeTarget(map, player.FurthestRow + 1);
            MoveTo(map, player, target);
        }
        Require(map.CurrentThemeId == "volcano" && changes == 1, "Theme becomes active only when the player reaches the new pattern.");
        Require(player.TryMove(Vector2Int.down) && player.TryMove(Vector2Int.up) && changes == 1,
            "Backtracking over a theme boundary does not toggle the theme or replay its event.");
        var before = placements[0];
        bool rejected = false;
        try { map.RequestThemeChange("missing"); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected && ReferenceEquals(before, placements[0]), "Unknown themes are rejected before changing the map.");
    }

    private static void CheckRewardCollection()
    {
        var map = StartMap(1, 0f, 1f);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var gameplay = UnityEngine.Object.FindFirstObjectByType<GameplayController>();
        Require(map.Rewards.Count > 0 && map.Coins.Count == 0, "Rewards are randomly generated independently of coins.");
        var position = new Vector2Int(0, 1);
        int points = map.GetRewardPoints(position);
        Require(points == 100 && player.TryMove(Vector2Int.up) && map.GetRewardPoints(position) == 0 &&
            gameplay.ItemScore == points && gameplay.Score == 100 + points, "A reward adds item score and is consumed once.");
        Require(player.TryMove(Vector2Int.down) && player.TryMove(Vector2Int.up) && gameplay.ItemScore == points,
            "Revisiting a reward cannot farm points.");
        foreach (var reward in map.Rewards)
            Require(map.CanMoveTo(reward.Key) && !map.IsLethal(reward.Key) && !map.Coins.ContainsKey(reward.Key),
                "Rewards only occupy safe, unoccupied cells.");
    }

    private static void CheckTraversal()
    {
        for (int seed = 1; seed <= 4; seed++)
        {
            var map = StartMap(seed, 0.2f, 0.1f);
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            for (int row = 1; row <= 100; row++)
            {
                var target = SafeTarget(map, row);
                MoveTo(map, player, target);
                Invoke(map, "AdvanceMovingObstacles", row * 2f);
                Require((bool)Invoke(map, "HasValidRoutes") && GameStateManager.Instance.State == GameState.Playing,
                    "Patterns, theme transitions and moving obstacles preserve all collectible routes.");
                Require(((IList)Field(map, "_placedPatterns")).Count <= 4, "Old pattern runtime state is discarded.");
            }
        }
    }

    private static void CheckMovingObstacles()
    {
        var map = StartMap(23);
        Require(UnityEngine.Object.FindFirstObjectByType<PlayerController>().TryMove(Vector2Int.up), "Load the authored patrol row into the visible map.");
        var movers = (IList)Field(map, "_movingObstacles");
        int patrols = 0, chasers = 0;
        var positions = new Vector2Int[movers.Count];
        var authoredPositions = new Vector2Int[movers.Count];
        for (int i = 0; i < movers.Count; i++)
        {
            positions[i] = (Vector2Int)Field(movers[i], "Position");
            authoredPositions[i] = ((MovingObstacleDefinition)Field(movers[i], "Definition")).position;
        }
        Invoke(map, "AdvanceMovingObstacles", float.MaxValue);
        for (int i = 0; i < movers.Count; i++)
        {
            var definition = (MovingObstacleDefinition)Field(movers[i], "Definition");
            var position = (Vector2Int)Field(movers[i], "Position");
            Require(definition.position == authoredPositions[i] && position != positions[i] && map.GetContactDamage(positions[i]) == 0 &&
                map.CanMoveTo(position) && map.GetContactDamage(position) > 0,
                "Runtime movement relocates contact damage without changing prefab spawn coordinates.");
            if (definition.movement == ObstacleMovement.Patrol)
            {
                Require(position == positions[i] + definition.direction, "Patrol moves exactly one cell along its authored direction.");
                patrols++;
            }
            else
            {
                Require(position.sqrMagnitude < positions[i].sqrMagnitude, "Chaser takes a safe path step toward the player.");
                chasers++;
            }
        }
        Require(patrols > 0 && chasers > 0, "Both patrol and chase behaviours are exercised.");
        foreach (var prefab in Resources.LoadAll<GameObject>("MapPatterns")) prefab.GetComponent<MapPattern>().Validate();
    }

    private static void MoveTo(MapManager map, PlayerController player, Vector2Int target)
    {
        int moves = 0;
        while (player.Position != target && player.FurthestRow <= target.y)
            Require(++moves < MapManager.Width * MapManager.VisibleRows && map.TryGetNextStep(player.Position, target, out var next) &&
                player.TryMove(next - player.Position), "Follow a safe pattern path under the one-step backward limit.");
    }

    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    private static Vector2Int SafeTarget(MapManager map, int row)
    {
        var position = new Vector2Int(0, row);
        if (!map.IsLethal(position) && map.CanMoveTo(position)) return position;
        for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
        {
            position = new Vector2Int(x, row);
            if (!map.IsLethal(position) && map.CanMoveTo(position)) return position;
        }
        throw new InvalidOperationException($"Row {row} has no safe cell.");
    }

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
