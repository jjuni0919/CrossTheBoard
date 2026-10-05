using System;
using System.Collections;
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
            CreateSample("Meadow_15", 15, "meadow", new[]
            {
                new Vector2Int(-2, 3), new Vector2Int(2, 5), new Vector2Int(-3, 9), new Vector2Int(3, 11)
            }, Array.Empty<HazardRowDefinition>(), new[] { Patrol(-2, 7), Chase(2, 11) },
                new[] { new RowTriggerDefinition { id = "checkpoint", row = 12 } },
                new[] { new Vector2Int(0, 10) });
            CreateSample("Meadow_25", 25, "meadow", new[]
            {
                new Vector2Int(-3, 3), new Vector2Int(-2, 3), new Vector2Int(2, 6), new Vector2Int(3, 6),
                new Vector2Int(2, 12), new Vector2Int(-2, 18), new Vector2Int(3, 20)
            }, new[] { new HazardRowDefinition { row = 8, steppingStoneColumns = new[] { -1, 1 } } },
                new[] { Patrol(-3, 15), Chase(0, 4) },
                new[] { new RowTriggerDefinition { id = "to_volcano", row = 23, nextThemeId = "volcano" } },
                new[] { new Vector2Int(2, 10) });
            CreateSample("Meadow_40", 40, "meadow", new[]
            {
                new Vector2Int(-2, 3), new Vector2Int(2, 3), new Vector2Int(2, 14), new Vector2Int(-3, 18),
                new Vector2Int(-2, 18), new Vector2Int(-2, 28), new Vector2Int(2, 28), new Vector2Int(3, 34)
            }, new[]
            {
                new HazardRowDefinition { row = 10, steppingStoneColumns = new[] { 0, 2 } },
                new HazardRowDefinition { row = 25, steppingStoneColumns = new[] { -1, 1 } }
            }, new[] { Patrol(-3, 32), Chase(2, 7) }, new[]
            {
                new RowTriggerDefinition { id = "checkpoint", row = 20 },
                new RowTriggerDefinition { id = "to_volcano", row = 38, nextThemeId = "volcano" }
            }, new[] { new Vector2Int(0, 6), new Vector2Int(-3, 16), new Vector2Int(2, 31) });
            CreateSample("Volcano_20", 20, "volcano", new[]
            {
                new Vector2Int(-2, 3), new Vector2Int(2, 12), new Vector2Int(3, 12), new Vector2Int(2, 16)
            }, new[] { new HazardRowDefinition { row = 6, steppingStoneColumns = new[] { -1, 1 } } },
                new[] { Patrol(-3, 15), Chase(0, 10) },
                new[] { new RowTriggerDefinition { id = "to_meadow", row = 18, nextThemeId = "meadow" } },
                new[] { new Vector2Int(3, 9), new Vector2Int(-3, 9), new Vector2Int(-2, 13) });
            CreateSample("Volcano_35", 35, "volcano", new[]
            {
                new Vector2Int(-3, 3), new Vector2Int(2, 13), new Vector2Int(3, 13),
                new Vector2Int(-2, 22), new Vector2Int(2, 28)
            }, new[]
            {
                new HazardRowDefinition { row = 6, steppingStoneColumns = new[] { 0 } },
                new HazardRowDefinition { row = 7, steppingStoneColumns = new[] { 0 } },
                new HazardRowDefinition { row = 25, steppingStoneColumns = new[] { -1, 1 } }
            }, new[] { Patrol(-3, 19), Chase(3, 30) },
                new[] { new RowTriggerDefinition { id = "checkpoint", row = 33 } },
                new[] { new Vector2Int(2, 10), new Vector2Int(-3, 17) });
            CreateSample("Volcano_50", 50, "volcano", new[]
            {
                new Vector2Int(-2, 3), new Vector2Int(2, 6), new Vector2Int(-3, 19), new Vector2Int(-2, 19),
                new Vector2Int(2, 30), new Vector2Int(3, 30), new Vector2Int(-2, 38), new Vector2Int(2, 38), new Vector2Int(3, 45)
            }, new[]
            {
                new HazardRowDefinition { row = 10, steppingStoneColumns = new[] { 0 } },
                new HazardRowDefinition { row = 11, steppingStoneColumns = new[] { 0 } },
                new HazardRowDefinition { row = 25, steppingStoneColumns = new[] { -2, 0, 2 } },
                new HazardRowDefinition { row = 42, steppingStoneColumns = new[] { -1, 1 } }
            }, new[] { Patrol(-3, 16), Chase(0, 34) },
                new[] { new RowTriggerDefinition { id = "to_meadow", row = 48, nextThemeId = "meadow" } },
                new[] { new Vector2Int(3, 8), new Vector2Int(-3, 28), new Vector2Int(-2, 46) });
            AssetDatabase.SaveAssets();
            Debug.Log("[MapPatternRegressionChecks] Generated six 15–50-row sample pattern prefabs.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static MovingObstacleDefinition Patrol(int x, int row) => new()
    {
        position = new Vector2Int(x, row), movement = ObstacleMovement.Patrol, distance = 3,
        tile = AssetDatabase.LoadAssetAtPath<DamageTile>("Assets/Gameplay/RollingRockTile.asset")
    };

    private static MovingObstacleDefinition Chase(int x, int row) => new()
    {
        position = new Vector2Int(x, row), movement = ObstacleMovement.Chase, stepInterval = 1f,
        tile = AssetDatabase.LoadAssetAtPath<DamageTile>("Assets/Gameplay/MonsterTile.asset")
    };

    private static void CreateSample(string name, int length, string theme, Vector2Int[] obstacles,
        HazardRowDefinition[] lava, MovingObstacleDefinition[] moving, RowTriggerDefinition[] triggers, Vector2Int[] spikes = null)
    {
        string folder = "Assets/Resources/MapPatterns/" + theme;
        MapPatternEditor.EnsureFolder(folder);
        string path = folder + "/" + name + ".prefab";
        var pattern = MapPatternEditor.CreatePattern(name, length, theme);
        try
        {
            pattern.Ground.color = theme == "meadow" ? new Color(0.58f, 0.78f, 0.5f) : new Color(0.56f, 0.48f, 0.46f);
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
            foreach (var position in obstacles) pattern.Structures.SetTile((Vector3Int)position, tile);
            if (spikes != null)
            {
                var spike = AssetDatabase.LoadAssetAtPath<DamageTile>("Assets/Gameplay/SpikeTile.asset");
                Require(spike != null && spike.Damage > 0, "The spike damage tile is assigned before saving patterns.");
                foreach (var position in spikes) pattern.Structures.SetTile((Vector3Int)position, spike);
            }
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
            Require(prefabs.Length == 6, "The six sample resource prefabs are discovered.");
            var lengths = new System.Collections.Generic.HashSet<int>();
            foreach (var prefab in prefabs)
            {
                var pattern = prefab.GetComponent<MapPattern>();
                pattern.Validate();
                foreach (var mover in pattern.MovingObstacles)
                {
                    string tilePath = mover.movement == ObstacleMovement.Patrol
                        ? "Assets/Gameplay/RollingRockTile.asset" : "Assets/Gameplay/MonsterTile.asset";
                    Require(mover.tile == AssetDatabase.LoadAssetAtPath<DamageTile>(tilePath),
                        "Every authored mover retains its saved damage tile reference.");
                }
                lengths.Add(pattern.Length);
            }
            Require(lengths.SetEquals(new[] { 15, 20, 25, 35, 40, 50 }), "Sample lengths span 15–50 rows.");
            CheckInvalidObstacles();
            CheckScenePattern();
            CheckMapLifecycle();
            CheckRepeatedPatterns();
            CheckThemeBoundary();
            CheckRewardCollection();
            CheckMovingObstacles();
            CheckTraversal();
            CheckInfiniteStreaming();
            Debug.Log("[MapPatternRegressionChecks] PASS: invalid tiles/overlaps/blocked rows rejected, row-one start, six resources, three live prefabs, whole-pattern random coins/rewards, boundary disposal, repeat weighting, theme boundaries, motion and 1,000-row streaming.");
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

    private static void CheckInvalidObstacles()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/MapPatterns/meadow/Meadow_15.prefab");
        var pattern = UnityEngine.Object.Instantiate(source).GetComponent<MapPattern>();
        try
        {
            var mover = pattern.MovingObstacles[0];
            var tile = mover.tile;
            var cell = (Vector3Int)mover.position;
            pattern.Structures.SetTile(cell, tile);
            bool overlapRejected = false;
            try { pattern.Validate(); }
            catch (InvalidOperationException exception) { overlapRejected = exception.Message.Contains("overlaps another obstacle at " + mover.position); }
            Require(overlapRejected, "Overlapping mover spawns report the exact cell.");
            pattern.Structures.SetTile(cell, null);
            mover.tile = null;
            bool missingTileRejected = false;
            try { pattern.Validate(); }
            catch (InvalidOperationException exception) { missingTileRejected = exception.Message.Contains("Invalid moving obstacle") && exception.Message.Contains("missing"); }
            Require(missingTileRejected, "A missing mover tile is rejected before streaming begins.");
            mover.tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
            bool tileRejected = false;
            try { pattern.Validate(); }
            catch (InvalidOperationException exception) { tileRejected = exception.Message.Contains("Invalid moving obstacle") && !exception.Message.Contains("overlaps"); }
            Require(tileRejected, "A non-damage mover tile is not mislabeled as an overlap.");
            mover.tile = tile;
            int length = pattern.Length;
            foreach (int invalidLength in new[] { MapPattern.MinimumLength - 1, MapPattern.MaximumLength + 1 })
            {
                Set(pattern, "_length", invalidLength);
                bool lengthRejected = false;
                try { pattern.Validate(); }
                catch (InvalidOperationException exception) { lengthRejected = exception.Message.Contains("requires a theme"); }
                Require(lengthRejected, "Lengths outside 15–50 rows are rejected.");
            }
            Set(pattern, "_length", length);
            pattern.Validate();
        }
        finally { UnityEngine.Object.DestroyImmediate(pattern.gameObject); }
        pattern = MapPatternEditor.CreatePattern("Blocked row regression", MapPattern.MinimumLength, "regression");
        try
        {
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
            for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
                pattern.Structures.SetTile(new Vector3Int(x, 2, 0), tile);
            bool blockedRowRejected = false;
            try { pattern.Validate(); }
            catch (InvalidOperationException exception) { blockedRowRejected = exception.Message.Contains("no safe forward route"); }
            Require(blockedRowRejected, "An entirely blocked row is rejected even when both neighbouring rows are fully open.");
            pattern.Structures.SetTile(new Vector3Int(0, 2, 0), null);
            pattern.Validate();
            pattern.Structures.ClearAllTiles();
            Set(pattern, "_lavaRows", new[] { new HazardRowDefinition { row = 2, steppingStoneColumns = new[] { 0 } } });
            Set(pattern, "_movingObstacles", new[]
            {
                new MovingObstacleDefinition { position = new Vector2Int(0, 2), movement = ObstacleMovement.Chase,
                    tile = AssetDatabase.LoadAssetAtPath<DamageTile>("Assets/Gameplay/MonsterTile.asset") }
            });
            bool bridgeSpawnRejected = false;
            try { pattern.Validate(); }
            catch (InvalidOperationException exception) { bridgeSpawnRejected = exception.Message.Contains("cannot start on a stepping-stone row"); }
            Require(bridgeSpawnRejected, "Monsters cannot be authored on stepping-stone rows.");
        }
        finally { UnityEngine.Object.DestroyImmediate(pattern.gameObject); }
    }

    private static void CheckScenePattern()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        var ground = (Tilemap)Field(map, "_ground");
        var structures = (Tilemap)Field(map, "_structures");
        Require(map.GetComponentsInChildren<MapPattern>(true).Length == 0,
            "Gameplay contains no fixed pattern instances before the Resources stream starts.");
        Require(ground.transform.parent == map.transform && structures.transform.parent == map.transform &&
            ((Tilemap)Field(map, "_coinLayer")).transform.parent == map.transform,
            "Gameplay data and collectible layers belong to Map, not to a disposable pattern.");
        Require(map.GetWorldPosition(Vector2Int.zero) == Vector3.zero, "The map preserves gameplay grid coordinates.");
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

    private static void CheckMapLifecycle()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var state = UnityEngine.Object.FindFirstObjectByType<GameStateManager>();
        typeof(GameStateManager).GetProperty("Instance").SetValue(null, state);
        Set(map, "_coinSeed", 13);
        player.Initialize(map);
        state.SetState(GameState.Playing);
        Invoke(map, "Start");
        Require(player.Position == new Vector2Int(0, 1) && (int)Field(((IList)Field(map, "_placedPatterns"))[0], "FirstRow") == 0,
            "The first prefab starts at row zero and the player starts in the centre of row one.");
        CheckPatternRendering(map, player);
        for (int row = 2; row <= 50; row++)
        {
            MoveTo(map, player, SafeTarget(map, row));
            Invoke(map, "Update");
            CheckPatternRendering(map, player);
        }
        Require((int)Field(map, "_nextPatternRow") > 50,
            "Map startup and forward streaming do not depend on a gameplay score-event subscription.");
    }

    private static void CheckThemeBoundary()
    {
        var map = StartMap(23);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var first = ((IList)Field(map, "_placedPatterns"))[0];
        int boundary = (int)first.GetType().GetProperty("LastRow").GetValue(first) + 1;
        int changes = 0;
        map.ThemeChanged += _ => changes++;
        var oldView = (MapPattern)Field(first, "View");
        var nextTemplate = Field(((IList)Field(map, "_placedPatterns"))[1], "Template");
        map.RequestThemeChange("volcano");
        var placements = (IList)Field(map, "_placedPatterns");
        Require((int)Field(placements[1], "FirstRow") == boundary &&
            ((MapPattern)Field(placements[0], "Template")).ThemeId == "meadow" &&
            ReferenceEquals(Field(placements[1], "Template"), nextTemplate) && map.CurrentThemeId == "meadow" && changes == 0,
            "A pending theme leaves all three prefabs unchanged until the boundary.");
        while (player.FurthestRow < boundary)
        {
            var target = SafeTarget(map, player.FurthestRow + 1);
            MoveTo(map, player, target);
        }
        Require(map.CurrentThemeId == "volcano" && changes == 1, "Theme becomes active only when the player reaches the new pattern.");
        Require(oldView != null && (int)Field(placements[1], "FirstRow") == boundary && placements.Count == 3,
            "One previous prefab is retained with the new current and next patterns at a theme boundary.");
        CheckPatternRendering(map, player);
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
        var position = new Vector2Int(0, 2);
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
            CheckPatternRendering(map, player);
            for (int row = 2; row <= 100; row++)
            {
                var target = SafeTarget(map, row);
                MoveTo(map, player, target);
                Invoke(map, "AdvanceMovingObstacles", row * 2f);
                Require((bool)Invoke(map, "HasValidRoutes") && GameStateManager.Instance.State == GameState.Playing,
                    "Patterns, theme transitions and moving obstacles preserve all collectible routes.");
                CheckPatternRendering(map, player);
            }
        }
    }

    private static void CheckInfiniteStreaming()
    {
        var map = StartMap(19, 0.14f, 0.03f);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var templates = new System.Collections.Generic.HashSet<MapPattern>();
        CheckPatternRendering(map, player);
        bool sawCoins = map.Coins.Count > 0;
        bool sawRewards = map.Rewards.Count > 0;
        int lastBoundary = 0;
        for (int row = 2; row <= 1000; row++)
        {
            MoveTo(map, player, SafeTarget(map, row));
            CheckPatternRendering(map, player);
            sawCoins |= map.Coins.Count > 0;
            sawRewards |= map.Rewards.Count > 0;
            var placements = (IList)Field(map, "_placedPatterns");
            for (int i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                templates.Add((MapPattern)Field(placement, "Template"));
                if (i > 0)
                    Require((int)Field(placement, "FirstRow") == (int)placements[i - 1].GetType().GetProperty("LastRow").GetValue(placements[i - 1]) + 1,
                        "Random patterns join without gaps or overlap.");
            }
            lastBoundary = (int)Field(map, "_nextPatternRow");
        }
        Require(templates.Count >= 2 && lastBoundary > 1000, "Multiple random templates continue beyond a thousand rows.");
        Require(sawCoins && sawRewards, "Random coins and special reward cells continue throughout streaming.");
    }

    private static void CheckPatternRendering(MapManager map, PlayerController player)
    {
        var placements = (IList)Field(map, "_placedPatterns");
        int rendered = 0;
        foreach (var placement in placements)
        {
            var view = (MapPattern)Field(placement, "View");
            if (view != null && view.gameObject.activeInHierarchy)
            {
                Require(view.transform.parent == map.transform && view.Ground.GetComponent<TilemapRenderer>().enabled &&
                    view.Structures.GetComponent<TilemapRenderer>().enabled, "Live patterns are visible children of Map.");
                rendered++;
            }
        }
        Require(placements.Count == 3 && rendered == 3, "Three current/upcoming patterns are retained and rendered, including at startup.");
        int current = 0;
        while ((int)placements[current].GetType().GetProperty("LastRow").GetValue(placements[current]) < player.FurthestRow) current++;
        Require(current <= 1 && (int)Field(placements[current], "FirstRow") <= player.FurthestRow,
            "At most one full previous pattern remains behind the current pattern.");
        var ground = (Tilemap)Field(map, "_ground");
        var structures = (Tilemap)Field(map, "_structures");
        var collectibles = (Tilemap)Field(map, "_coinLayer");
        var renderer = collectibles.GetComponent<TilemapRenderer>();
        Require(ground.cellBounds.size.y <= MapPattern.MaximumLength * 3 + MapManager.RowsBehind &&
            ((IDictionary)Field(map, "_registeredRows")).Count <= 6 &&
            ((IList)Field(map, "_movingObstacles")).Count <= MapManager.Width * (MapManager.MonsterDespawnDistance * 2 - 1) + 6,
            "Streaming bounds floor data, actors and generated trigger registrations.");
        Require(collectibles.transform.parent == map.transform && renderer.enabled &&
            renderer.sortingOrder > structures.GetComponent<TilemapRenderer>().sortingOrder &&
            renderer.sortingOrder < player.GetComponentInChildren<SpriteRenderer>().sortingOrder,
            "The coin/special-cell layer is visible above terrain/obstacles without covering the player.");
        foreach (var coin in map.Coins)
            Require(collectibles.GetSprite((Vector3Int)coin.Key) != null && map.GetCoinAmount(coin.Key) > 0,
                "Every generated coin has a rendered sprite and collectible value.");
        foreach (var reward in map.Rewards)
            Require(collectibles.GetSprite((Vector3Int)reward.Key) != null && map.GetRewardPoints(reward.Key) > 0 &&
                !map.Coins.ContainsKey(reward.Key), "Every special reward cell has a sprite and does not overlap a coin.");
        Require(!ground.GetComponent<TilemapRenderer>().enabled && !structures.GetComponent<TilemapRenderer>().enabled,
            "The shared gameplay tilemaps do not render a duplicate pattern.");
        for (int row = player.FurthestRow - MapManager.RowsBehind; row <= player.FurthestRow + MapManager.RowsAhead; row++)
            for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
            {
                var placement = Invoke(map, "GetPattern", row);
                Require(placement != null, "Every visible row belongs to a rendered pattern.");
                var view = (MapPattern)Field(placement, "View");
                var cell = new Vector3Int(x, row, 0);
                var local = new Vector3Int(x, row - (int)Field(placement, "FirstRow"), 0);
                Require(view != null && view.Ground.GetTile(local) == ground.GetTile(cell) &&
                    view.Structures.GetTile(local) == structures.GetTile(cell) && view.Ground.GetColor(local) == ground.GetColor(cell) &&
                    view.Ground.GetCellCenterWorld(local) == map.GetWorldPosition(new Vector2Int(x, row)),
                    "Rendered terrain, hazards and actors match gameplay cells.");
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
        var expectedSteps = new Vector2Int[movers.Count];
        var shouldMove = new bool[movers.Count];
        var target = UnityEngine.Object.FindFirstObjectByType<PlayerController>().Position;
        for (int i = 0; i < movers.Count; i++)
        {
            positions[i] = (Vector2Int)Field(movers[i], "Position");
            var definition = (MovingObstacleDefinition)Field(movers[i], "Definition");
            authoredPositions[i] = definition.position;
            if (definition.movement == ObstacleMovement.Chase)
                shouldMove[i] = (bool)Field(movers[i], "HasStartedChasing") &&
                    map.TryGetNextStep(positions[i], target, out expectedSteps[i]);
            else shouldMove[i] = true;
        }
        Invoke(map, "AdvanceMovingObstacles", float.MaxValue);
        for (int i = 0; i < movers.Count; i++)
        {
            var definition = (MovingObstacleDefinition)Field(movers[i], "Definition");
            var position = (Vector2Int)Field(movers[i], "Position");
            if (!shouldMove[i])
            {
                Require(position == positions[i], "Distant or river-separated monsters remain at their current cell.");
                continue;
            }
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
                Require(position == expectedSteps[i], "Chaser follows the safe path toward the player, including obstacle detours.");
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
                player.TryMove(next - player.Position), $"Follow a safe pattern path: {player.Position} -> {target}, frontier {player.FurthestRow}, health {player.Health}, state {GameStateManager.Instance.State}.");
    }

    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    private static Vector2Int SafeTarget(MapManager map, int row)
    {
        var position = new Vector2Int(0, row);
        if (!map.IsLethal(position) && map.CanMoveTo(position) && map.GetContactDamage(position) == 0) return position;
        for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
        {
            position = new Vector2Int(x, row);
            if (!map.IsLethal(position) && map.CanMoveTo(position) && map.GetContactDamage(position) == 0) return position;
        }
        throw new InvalidOperationException($"Row {row} has no safe cell.");
    }

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
