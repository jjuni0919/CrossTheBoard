using System;
using System.Reflection;
using CrossTheBoard;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Batch validation entry point: -executeMethod GameplayRegressionChecks.Run.
/// Exercises the scene's real components in memory without entering Play Mode,
/// saving the scene, or running persistent-save lifecycle methods.
/// </summary>
public static class GameplayRegressionChecks
{
    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var gameplay = UnityEngine.Object.FindFirstObjectByType<GameplayController>();
            var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
            var camera = UnityEngine.Object.FindFirstObjectByType<GameplayCamera>();
            var state = UnityEngine.Object.FindFirstObjectByType<GameStateManager>();
            Require(player != null && gameplay != null && map != null && camera != null && state != null,
                "GameplayScene must contain all gameplay components.");
            // Use deterministic empty structures, and suppress achievement/save effects.
            Field(map, "_structures").As<Tilemap>().ClearAllTiles();
            ConfigureMap(map, Array.Empty<HazardRowDefinition>());
            SetField(gameplay, "_moveAchievementIds", Array.Empty<string>());
            typeof(GameStateManager).GetProperty(nameof(GameStateManager.Instance))!.SetValue(null, state);

            int events = 0;
            int lastScore = -1;
            gameplay.ScoreChanged += score => { events++; lastScore = score; };
            Invoke(gameplay, "Start");
            Invoke(camera, "Awake");
            Invoke(camera, "LateUpdate");
            Vector3 startCamera = camera.transform.position;
            Require(player.Position == Vector2Int.zero && player.FurthestRow == 0, "Start at (0, 0).");
            Require(gameplay.Score == 0 && events == 1 && lastScore == 0, "Publish initial zero score.");

            Require(player.TryMove(Vector2Int.down), "One backward step from the starting frontier is allowed.");
            Require(player.Position == new Vector2Int(0, -1), "Backward position is (0, -1).");
            Require(!player.TryMove(Vector2Int.down), "A second backward step is blocked.");
            Require(player.TryMove(Vector2Int.right), "Sideways movement behind the frontier is allowed.");
            Require(!player.TryMove(Vector2Int.down), "Moving sideways does not reset the backward allowance.");
            Invoke(camera, "LateUpdate");
            Require(camera.transform.position == startCamera, "Backward/sideways movement must not move the camera.");
            Require(player.TryMove(Vector2Int.up), "Return to the old frontier.");
            Invoke(camera, "LateUpdate");
            Require(camera.transform.position == startCamera && gameplay.Score == 0 && events == 1,
                "Returning does not move the camera or award points.");

            Require(player.TryMove(Vector2Int.up), "Advance into a new row.");
            Invoke(camera, "LateUpdate");
            Vector3 advancedCamera = camera.transform.position;
            Require(player.FurthestRow == 1 && gameplay.ForwardScore == 100 && gameplay.Score == 100,
                "A new row awards exactly 100 points.");
            Require(Mathf.Approximately(advancedCamera.y - startCamera.y, 1f), "Camera advances exactly one row.");
            var ground = Field(map, "_ground").As<Tilemap>();
            Require(ground.cellBounds.size.x == MapManager.Width && ground.cellBounds.size.y == MapManager.VisibleRows,
                "Loaded map is nine columns wide and eleven rows high.");
            BoundsInt advancedBounds = ground.cellBounds;

            for (int i = 0; i < 3; i++)
            {
                Require(player.TryMove(Vector2Int.down), "One step behind the new frontier is allowed.");
                Require(!player.TryMove(Vector2Int.down), "Two steps behind the frontier are blocked.");
                Invoke(camera, "LateUpdate");
                Require(camera.transform.position == advancedCamera && ground.cellBounds == advancedBounds,
                    "Camera and map stay fixed while backtracking.");
                Require(player.TryMove(Vector2Int.up), "Return from backtracking.");
                Invoke(camera, "LateUpdate");
                Require(camera.transform.position == advancedCamera && gameplay.Score == 100 && events == 2,
                    "Repeated backtracking cannot farm score or shift the camera.");
            }

            Require(player.TryMove(Vector2Int.up), "Advance to a second new row.");
            Require(gameplay.Score == 200 && gameplay.ForwardScore == 200, "Second new row awards another 100 points.");
            Require(player.TryMove(Vector2Int.right) && player.TryMove(Vector2Int.right), "Reach the right edge.");
            Require(player.TryMove(Vector2Int.right), "Reach the fourth column to the right.");
            Require(!player.TryMove(Vector2Int.right) && player.Position.x == 4, "Nine-column right boundary is enforced.");
            Require(gameplay.Score == 200, "Lateral movement never awards distance points.");

            gameplay.AddGoldScore(25);
            gameplay.AddItemScore(50);
            Require(gameplay.Score == 275 && gameplay.ForwardScore == 200 && gameplay.GoldScore == 25 && gameplay.ItemScore == 50,
                "Distance, gold, and item score totals remain separate and add correctly.");
            int collectionEvents = events;
            gameplay.AddGoldScore(0);
            Require(events == collectionEvents && lastScore == 275, "Zero-point bonuses do not publish redundant updates.");
            bool rejectedNegative = false;
            try { gameplay.AddItemScore(-1); }
            catch (ArgumentOutOfRangeException) { rejectedNegative = true; }
            Require(rejectedNegative && gameplay.Score == 275, "Negative bonus values are rejected without changing score.");

            state.SetState(GameState.Paused);
            Require(!player.TryMove(Vector2Int.up), "Paused movement is blocked.");
            gameplay.AddGoldScore(100);
            Require(gameplay.Score == 275, "Paused collection does not award score.");
            state.SetState(GameState.Playing);
            state.SetState(GameState.GameOver);
            gameplay.AddItemScore(100);
            Require(!player.TryMove(Vector2Int.up) && gameplay.Score == 275, "Completed runs cannot gain movement or collection score.");

            CheckCameraViewport(camera);
            CheckMapInitialization();
            CheckMapContent();
            Debug.Log("[GameplayRegressionChecks] PASS: movement/camera/score regression, editable viewport, obstacle routes, hazard rows, stepping stones and path-based obstacle movement.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckCameraViewport(GameplayCamera camera)
    {
        Canvas.ForceUpdateCanvases();
        var component = camera.GetComponent<Camera>();
        var texture = new RenderTexture(1080, 1920, 24);
        component.targetTexture = texture;
        Invoke(camera, "LateUpdate");
        Require(Mathf.Approximately(component.orthographicSize, 5.5f) && Mathf.Approximately(component.rect.y, 0.3125f) &&
            Mathf.Approximately(component.rect.width, 1f) && Mathf.Approximately(component.aspect, 9f / 11f),
            "Portrait view shows nine columns and eleven rows above the empty bottom area.");
        var bottom = Field(camera, "_bottomArea").As<RectTransform>();
        bottom.anchorMax = new Vector2(1, 0.5f);
        Invoke(camera, "LateUpdate");
        Require(component.rect.y >= 0.5f && Mathf.Approximately(component.aspect, 9f / 11f), "Editing bottom anchors reserves space without stretching cells.");
        texture.width = 1920;
        texture.height = 1080;
        Invoke(camera, "LateUpdate");
        Require(component.rect.y >= 0.5f && component.rect.width < 1f && Mathf.Approximately(component.aspect, 9f / 11f),
            "Landscape view preserves the requested grid aspect.");
        component.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(texture);
    }

    private static MapManager StartMap(HazardRowDefinition[] hazardRows, float coinChance = 0f, float obstacleChance = 0f, int seed = 1)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        Field(map, "_structures").As<Tilemap>().ClearAllTiles();
        ConfigureMap(map, hazardRows, seed, obstacleChance);
        SetField(map, "_coinChance", coinChance);
        var gameplay = UnityEngine.Object.FindFirstObjectByType<GameplayController>();
        SetField(gameplay, "_moveAchievementIds", Array.Empty<string>());
        typeof(GameStateManager).GetProperty(nameof(GameStateManager.Instance))!.SetValue(null, UnityEngine.Object.FindFirstObjectByType<GameStateManager>());
        Invoke(gameplay, "Start");
        return map;
    }

    public static void ConfigureMap(MapManager map, HazardRowDefinition[] hazards, int seed = 1, float obstacleChance = 0f)
    {
        var pattern = MapPatternEditor.CreatePattern("Regression pattern", MapPattern.MaximumLength, "regression");
        pattern.transform.SetParent(map.transform, false);
        pattern.gameObject.SetActive(false);
        var localRows = new HazardRowDefinition[hazards.Length];
        for (int i = 0; i < hazards.Length; i++)
            localRows[i] = new HazardRowDefinition { row = hazards[i].row - 1, steppingStoneColumns = hazards[i].steppingStoneColumns };
        SetField(pattern, "_lavaRows", localRows);
        if (obstacleChance > 0f)
        {
            var random = new System.Random(seed);
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
            for (int row = 1; row < pattern.Length - 1; row++)
            {
                bool nearLava = Array.Exists(localRows, lava => Mathf.Abs(lava.row - row) <= 1);
                if (!nearLava && (row == 1 || random.NextDouble() < obstacleChance))
                    pattern.Structures.SetTile(new Vector3Int(row % 2 == 0 ? 2 : -2, row, 0), tile);
            }
        }
        SetField(map, "_patterns", new[] { pattern });
        SetField(map, "_initialThemeId", "regression");
        SetField(map, "_coinSeed", seed);
        SetField(map, "_rewardChance", 0f);
    }

    private static void CheckMapInitialization()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        ConfigureMap(map, Array.Empty<HazardRowDefinition>());
        SetField(map, "_coinChance", 0f);
        SetField(map, "_cellTriggers", new[]
        {
            new CellTriggerDefinition { id = "duplicate", position = new Vector2Int(0, 1) },
            new CellTriggerDefinition { id = "duplicate", position = new Vector2Int(0, 2) }
        });
        bool rejected = false;
        try { map.LoadRows(0); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && !Field(map, "_contentInitialized").As<bool>() && Field(map, "_coinLayer") == null,
            "Failed configuration validation leaves no initialized flag or generated coin layer.");
        SetField(map, "_cellTriggers", new[] { new CellTriggerDefinition { id = "valid", position = new Vector2Int(0, 1) } });
        SetField(map, "_rowTriggers", new[] { new RowTriggerDefinition { id = "configured", row = 2 } });
        Require(!map.RegisterRowTrigger(new RowTriggerDefinition { id = "configured", row = 3 }),
            "Runtime registration respects serialized row trigger IDs before loading the map.");
        map.LoadRows(0);
        Require(Field(map, "_contentInitialized").As<bool>() && !map.TryPlaceCoin(new Vector2Int(0, 1)),
            "Corrected configuration retries cleanly and reserves its serialized cell triggers.");
    }

    private static void CheckMapContent()
    {
        bool rejected = false;
        try
        {
            StartMap(new[]
            {
                new HazardRowDefinition { row = 20, steppingStoneColumns = new[] { 0, 2 } },
                new HazardRowDefinition { row = 21, steppingStoneColumns = new[] { 0, 2 } }
            });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        {
            rejected = true;
        }
        Require(rejected, "Disconnected future hazard rows are rejected before gameplay starts.");
        var map = StartMap(Array.Empty<HazardRowDefinition>());
        var tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
        var structures = Field(map, "_structures").As<Tilemap>();
        for (int x = -MapManager.HalfWidth; x < MapManager.HalfWidth; x++)
            Require(map.TrySetObstacle(new Vector2Int(x, 1), tile), "A row may be blocked while one crossing remains.");
        Require(!map.TrySetObstacle(new Vector2Int(MapManager.HalfWidth, 1), tile) &&
            !structures.HasTile(new Vector3Int(MapManager.HalfWidth, 1, 0)), "The final forward crossing cannot be blocked and rejected placement rolls back.");
        Require(!map.TrySetObstacle(Vector2Int.zero, tile), "Do not place obstacles under the player.");

        structures.ClearAllTiles();
        Require(map.TryPlaceCoin(new Vector2Int(0, 2)), "Place a reachable coin.");
        Require(map.TrySetObstacle(new Vector2Int(-1, 2), tile) && map.TrySetObstacle(new Vector2Int(1, 2), tile), "Partial enclosure keeps accessible entrances.");
        Require(!map.TrySetObstacle(new Vector2Int(0, 1), tile), "An obstacle cannot strand a coin behind a wall after advancing.");
        map.RemoveCoin(new Vector2Int(0, 2));
        Require(map.RegisterCellTrigger(new CellTriggerDefinition { id = "reachable", position = new Vector2Int(0, 2) }), "Register a reachable special cell.");
        Require(!map.TrySetObstacle(new Vector2Int(0, 1), tile), "An obstacle cannot strand a trigger.");

        map = StartMap(Array.Empty<HazardRowDefinition>());
        tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
        var source = new Vector2Int(2, 2);
        Require(map.TrySetObstacle(source, tile), "Spawn a moving obstacle using the existing placement API.");
        Require(map.TryGetNextStep(source, Vector2Int.zero, out var next) && (next - source).sqrMagnitude == 1,
            "Find a cardinal path step toward the player from an occupied obstacle cell.");
        Require(map.TryMoveObstacle(source, next) && !map.CanMoveTo(next) && map.CanMoveTo(source), "Move obstacle atomically along the path.");
        Require(!map.TryMoveObstacle(next, Vector2Int.zero), "Do not teleport an obstacle or overwrite the player.");
        Require(!map.TryGetNextStep(next, new Vector2Int(20, 2), out _), "Out-of-map path requests fail.");
        int spawns = 0;
        var row = new RowTriggerDefinition { id = "spawn", row = 1 };
        row.onReached.AddListener(() => { if (map.TrySetObstacle(new Vector2Int(-2, 3), tile)) spawns++; });
        map.RegisterRowTrigger(row);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Require(player.TryMove(Vector2Int.up) && spawns == 1, "Existing row event can spawn an obstacle.");
        Require(player.TryMove(Vector2Int.down) && player.TryMove(Vector2Int.up) && spawns == 1, "Revisiting the spawn row does not duplicate the obstacle.");

        var hazards = new[] { new HazardRowDefinition { row = 3, steppingStoneColumns = new[] { 0 } } };
        map = StartMap(hazards);
        Require(map.IsLethal(new Vector2Int(1, 3)) && !map.IsLethal(new Vector2Int(0, 3)), "Only stepping stones are safe on hazard rows.");
        Require(!map.TryPlaceCoin(new Vector2Int(1, 3)) && !map.RegisterCellTrigger(new CellTriggerDefinition { id = "water", position = new Vector2Int(1, 3) }),
            "Coins and triggers cannot be placed in lethal cells.");
        Require(map.TryPlaceCoin(new Vector2Int(0, 3)) && !map.TryMoveSteppingStone(new Vector2Int(0, 3), new Vector2Int(1, 3)),
            "A collectible on a stone prevents moving it away.");
        map.RemoveCoin(new Vector2Int(0, 3));
        Require(map.TryMoveSteppingStone(new Vector2Int(0, 3), new Vector2Int(1, 3)) && map.IsLethal(new Vector2Int(0, 3)) && !map.IsLethal(new Vector2Int(1, 3)),
            "Moving a free stone updates the safe cells.");
        var ground = Field(map, "_ground").As<Tilemap>();
        Require(ground.GetColor(new Vector3Int(0, 3, 0)) != ground.GetColor(new Vector3Int(1, 3, 0)), "Hazards and stones have different colors.");
        Require(map.RegisterCellTrigger(new CellTriggerDefinition { id = "stone", position = new Vector2Int(1, 3) }) &&
            !map.TryMoveSteppingStone(new Vector2Int(1, 3), new Vector2Int(0, 3)), "A trigger cannot be stranded by moving its stone.");

        map = StartMap(new[]
        {
            new HazardRowDefinition { row = 3, steppingStoneColumns = new[] { 0 } },
            new HazardRowDefinition { row = 4, steppingStoneColumns = new[] { 0 } }
        });
        Require(!map.TryMoveSteppingStone(new Vector2Int(0, 3), new Vector2Int(1, 3)) && !map.IsLethal(new Vector2Int(0, 3)),
            "Moving a last crossing stone is rejected and rolled back.");
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Require(player.TryMove(Vector2Int.up) && player.TryMove(Vector2Int.up) && player.TryMove(Vector2Int.up), "Cross on the safe stone.");
        Require(!map.TryMoveSteppingStone(player.Position, player.Position + Vector2Int.right), "Do not move a stone under the player.");
        int score = UnityEngine.Object.FindFirstObjectByType<GameplayController>().Score;
        Require(player.TryMove(Vector2Int.right) && GameStateManager.Instance.State == GameState.GameOver &&
            !player.TryMove(Vector2Int.up) && UnityEngine.Object.FindFirstObjectByType<GameplayController>().Score == score,
            "Entering a lethal cell ends the run without granting further points.");

        map = StartMap(new[] { new HazardRowDefinition { row = 2, steppingStoneColumns = new[] { 0 } } });
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var camera = UnityEngine.Object.FindFirstObjectByType<GameplayCamera>();
        Invoke(camera, "Awake");
        Require(player.TryMove(Vector2Int.up), "Reach the row before the lethal forward cell.");
        Invoke(camera, "LateUpdate");
        Vector3 beforeDeath = camera.transform.position;
        Require(player.TryMove(Vector2Int.right) && player.TryMove(Vector2Int.up) && GameStateManager.Instance.State == GameState.GameOver,
            "Enter a lethal cell in a new forward row.");
        Invoke(camera, "LateUpdate");
        Require(camera.transform.position == beforeDeath, "Death does not advance the camera beyond the loaded map frontier.");

        for (int seed = 1; seed <= 6; seed++)
        {
            map = StartMap(new[]
            {
                new HazardRowDefinition { row = 5, steppingStoneColumns = new[] { 0, 2 } },
                new HazardRowDefinition { row = 12, steppingStoneColumns = new[] { -2, 0 } }
            }, 0.45f, 0.35f, seed);
            player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            Require(Field(map, "_structures").As<Tilemap>().GetUsedTilesCount() > 0, "Random obstacles are actually generated.");
            for (int frontier = 1; frontier <= 40; frontier++)
            {
                Vector2Int target = new(0, frontier);
                bool found = false;
                for (int x = -MapManager.HalfWidth; x <= MapManager.HalfWidth; x++)
                {
                    var position = new Vector2Int(x, frontier);
                    if (map.CanMoveTo(position) && !map.IsLethal(position)) { target = position; found = true; break; }
                }
                Require(found, "Every generated row has a safe crossing.");
                int moves = 0;
                while (player.Position != target && player.FurthestRow <= frontier)
                {
                    Require(++moves < MapManager.Width * MapManager.VisibleRows && map.TryGetNextStep(player.Position, target, out next) &&
                        player.TryMove(next - player.Position), "Generated map can be traversed under the actual backward limit.");
                }
                foreach (var coin in map.Coins)
                    Require(map.CanMoveTo(coin.Key) && !map.IsLethal(coin.Key) &&
                        (coin.Key == player.Position || map.TryGetNextStep(player.Position, coin.Key, out _)), "Every visible coin stays on a reachable safe cell.");
                Require(GameStateManager.Instance.State == GameState.Playing, "Following a safe route never enters a lethal row cell.");
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static T As<T>(this object value) => (T)value;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
}
