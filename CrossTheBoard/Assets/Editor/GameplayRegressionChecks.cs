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
            Require(player.Position == new Vector2Int(0, 1) && player.FurthestRow == 1, "Start in the centre of row one.");
            Require(gameplay.Score == 0 && events == 1 && lastScore == 0, "Publish initial zero score.");

            Require(MovePlayer(player, Vector2Int.down), "One backward step from the starting frontier is allowed.");
            Require(player.Position == Vector2Int.zero, "One backward step reaches row zero.");
            Require(!MovePlayer(player, Vector2Int.down), "A second backward step is blocked.");
            Require(MovePlayer(player, Vector2Int.right), "Sideways movement behind the frontier is allowed.");
            Require(!MovePlayer(player, Vector2Int.down), "Moving sideways does not reset the backward allowance.");
            Invoke(camera, "LateUpdate");
            Require(camera.transform.position == startCamera, "Backward/sideways movement must not move the camera.");
            Require(MovePlayer(player, Vector2Int.up), "Return to the old frontier.");
            Invoke(camera, "LateUpdate");
            Require(camera.transform.position == startCamera && gameplay.Score == 0 && events == 1,
                "Returning does not move the camera or award points.");

            Require(MovePlayer(player, Vector2Int.up), "Advance into a new row.");
            Invoke(camera, "LateUpdate");
            Vector3 advancedCamera = camera.transform.position;
            Require(player.FurthestRow == 2 && gameplay.ForwardScore == 100 && gameplay.Score == 100,
                "A new row awards exactly 100 points.");
            Require(Mathf.Approximately(advancedCamera.y - startCamera.y, 1f), "Camera advances exactly one row.");
            var ground = GetGroundBounds(map);
            Require(ground.size.x == MapManager.Width && ground.size.y <= MapPattern.MaximumLength * 3 + MapManager.RowsBehind &&
                ground.Contains(new Vector3Int(0, player.FurthestRow + MapManager.RowsAhead, 0)),
                "Three full patterns cover the camera without unbounded floor data.");
            BoundsInt advancedBounds = ground;

            for (int i = 0; i < 3; i++)
            {
                Require(MovePlayer(player, Vector2Int.down), "One step behind the new frontier is allowed.");
                Require(!MovePlayer(player, Vector2Int.down), "Two steps behind the frontier are blocked.");
                Invoke(camera, "LateUpdate");
                Require(camera.transform.position == advancedCamera && GetGroundBounds(map) == advancedBounds,
                    "Camera and map stay fixed while backtracking.");
                Require(MovePlayer(player, Vector2Int.up), "Return from backtracking.");
                Invoke(camera, "LateUpdate");
                Require(camera.transform.position == advancedCamera && gameplay.Score == 100 && events == 2,
                    "Repeated backtracking cannot farm score or shift the camera.");
            }

            Require(MovePlayer(player, Vector2Int.up), "Advance to a second new row.");
            Require(gameplay.Score == 200 && gameplay.ForwardScore == 200, "Second new row awards another 100 points.");
            Require(MovePlayer(player, Vector2Int.right) && MovePlayer(player, Vector2Int.right), "Reach the right edge.");
            Require(MovePlayer(player, Vector2Int.right), "Reach the fourth column to the right.");
            Require(!MovePlayer(player, Vector2Int.right) && player.Position.x == 4, "Nine-column right boundary is enforced.");
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
            Require(!MovePlayer(player, Vector2Int.up), "Paused movement is blocked.");
            gameplay.AddGoldScore(100);
            Require(gameplay.Score == 275, "Paused collection does not award score.");
            state.SetState(GameState.Playing);
            state.SetState(GameState.GameOver);
            gameplay.AddItemScore(100);
            Require(!MovePlayer(player, Vector2Int.up) && gameplay.Score == 275, "Completed runs cannot gain movement or collection score.");

            CheckCameraViewport(camera);
            CheckMapInitialization();
            CheckMapContent();
            CheckPlayerMoveDelay();
            CheckPlayerInputQueue();
            CheckDamageObstacles();
            CheckChaserLifetimeAndBridges();
            CheckMovingSteppingStones();
            CheckSmoothObstacleContact();
            Debug.Log("[GameplayRegressionChecks] PASS: movement/camera/score, map routes, lava, stepping stones, terrain, speed-based contact transfer on arrival, destination reservation, dodging, contact death despite immunity, smooth obstacle visuals, chasing monsters, rolling rocks, previous-pattern retention and monster lifetime/river barriers.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static BoundsInt GetGroundBounds(MapManager map)
    {
        return new BoundsInt(-MapManager.HalfWidth, (int)Field(map, "_firstRow"), 0,
            MapManager.Width, (int)Field(map, "_lastRow") - (int)Field(map, "_firstRow") + 1, 1);
    }

    public static bool MovePlayer(PlayerController player, Vector2Int direction)
    {
        // Edit Mode has no advancing game clock; route tests model completed player steps.
        SetField(player, "_nextMoveTime", Time.time);
        return player.TryMove(direction);
    }

    private static void CheckPlayerMoveDelay()
    {
        var map = StartMap(Array.Empty<HazardRowDefinition>());
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        bool reentered = false;
        Action<Vector2Int, Vector2Int> reenter = (_, _) => reentered = player.TryMove(Vector2Int.up);
        player.Moved += reenter;
        Require(player.TryMove(Vector2Int.right) && !reentered,
            "The first move is immediate and movement events cannot bypass its cooldown.");
        player.Moved -= reenter;
        Require(Mathf.Approximately((float)Field(player, "_nextMoveTime") - Time.time, .3f) &&
            !player.TryMove(Vector2Int.up) && !player.TryMove(Vector2Int.left) && player.Position == Vector2Int.right,
            "Successful moves block all directions for 0.3 game seconds.");
        SetField(player, "_swipeStart", Vector2.zero);
        Invoke(player, "MoveFromSwipe", new Vector2(10000f, 0f));
        Require(player.Position == Vector2Int.right, "Swipes share the same player cooldown.");
        SetField(player, "_nextMoveTime", Time.time + .001f);
        Require(!player.TryMove(Vector2Int.up), "Movement is blocked until the deadline.");
        SetField(player, "_nextMoveTime", Time.time);
        var terrain = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
        float deadline = (float)Field(player, "_nextMoveTime");
        Require(map.TrySetObstacle(new Vector2Int(2, 0), terrain) && !player.TryMove(Vector2Int.right) &&
            (float)Field(player, "_nextMoveTime") == deadline && player.TryMove(Vector2Int.up),
            "Blocked moves do not consume a cooldown; movement resumes exactly at the deadline.");
        deadline = (float)Field(player, "_nextMoveTime");
        Invoke(player, "RideStone", new Vector2Int(2, 1), Vector3.left * .5f);
        Require(player.Position == new Vector2Int(2, 1) && (float)Field(player, "_nextMoveTime") == deadline &&
            !player.TryMove(Vector2Int.up), "Platform transport neither waits for nor resets the input cooldown.");
        GameStateManager.Instance.SetState(GameState.Paused);
        Require(!player.TryMove(Vector2Int.up) && (float)Field(player, "_nextMoveTime") == deadline,
            "Paused input preserves the movement deadline.");
        GameStateManager.Instance.SetState(GameState.Playing);
        player.Initialize(map);
        Invoke(player, "MoveFromSwipe", new Vector2(10000f, 0f));
        Invoke(player, "ProcessMoveQueue");
        Require(player.Position == Vector2Int.right && !player.TryMove(Vector2Int.up),
            "Initialization resets the cooldown and an accepted swipe starts it again.");
    }

    private static void CheckPlayerInputQueue()
    {
        var map = StartMap(Array.Empty<HazardRowDefinition>());
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var inputs = Field(player, "_moveInputs").As<System.Collections.Generic.Queue<Vector2Int>>();
        Invoke(player, "QueueMove", Vector2Int.right);
        Invoke(player, "ProcessMoveQueue");
        Require(player.Position == Vector2Int.right && inputs.Count == 0 &&
            Mathf.Approximately((float)Field(player, "_nextInputTime") - Time.time, .1f),
            "The first queued input is immediate and starts the 0.1-second ignore window.");
        Invoke(player, "QueueMove", Vector2Int.up);
        SetField(player, "_swipeStart", Vector2.zero);
        Invoke(player, "MoveFromSwipe", new Vector2(-10000f, 0f));
        SetField(player, "_nextInputTime", Time.time + .001f);
        Invoke(player, "QueueMove", Vector2Int.up);
        Require(inputs.Count == 0, "Key directions and swipes before 0.1 seconds are discarded.");
        SetField(player, "_nextInputTime", Time.time);
        Invoke(player, "QueueMove", Vector2Int.up);
        Invoke(player, "MoveFromSwipe", new Vector2(-10000f, 0f));
        Invoke(player, "ProcessMoveQueue");
        Require(inputs.Count == 2 && inputs.Peek() == Vector2Int.up && player.Position == Vector2Int.right,
            "Input is accepted at the ignore deadline and waits in a shared FIFO until movement is ready.");
        SetField(player, "_nextMoveTime", Time.time);
        Invoke(player, "ProcessMoveQueue");
        Invoke(player, "QueueMove", Vector2Int.right);
        Require(player.Position == new Vector2Int(1, 1) && inputs.Count == 1 && inputs.Peek() == Vector2Int.left &&
            Mathf.Approximately((float)Field(player, "_nextMoveTime") - Time.time, .3f),
            "Automatic movement consumes only the oldest input and starts both timing windows again.");
        SetField(player, "_nextMoveTime", Time.time);
        Invoke(player, "ProcessMoveQueue");
        Require(player.Position == Vector2Int.up && inputs.Count == 0, "The second direction executes without new input.");

        SetField(player, "_nextInputTime", Time.time);
        Invoke(player, "QueueMove", Vector2Int.right);
        Invoke(player, "QueueMove", Vector2Int.up);
        var terrain = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
        Require(map.TrySetObstacle(new Vector2Int(1, 1), terrain), "Block a destination after its input was stored.");
        SetField(player, "_nextMoveTime", Time.time);
        Invoke(player, "ProcessMoveQueue");
        Require(player.Position == new Vector2Int(0, 2) && inputs.Count == 0,
            "Queued moves use execution-time map data and a blocked direction does not stall later input.");
        SetField(player, "_nextInputTime", Time.time);
        Invoke(player, "QueueMove", Vector2Int.left);
        Invoke(player, "QueueMove", Vector2Int.up);
        SetField(player, "_nextMoveTime", Time.time);
        GameStateManager.Instance.SetState(GameState.Paused);
        Invoke(player, "Update");
        Invoke(player, "ProcessMoveQueue");
        Require(inputs.Count == 2 && player.Position == new Vector2Int(0, 2), "Pause preserves but does not execute the queue.");
        GameStateManager.Instance.SetState(GameState.Playing);
        Invoke(player, "ProcessMoveQueue");
        float moveDeadline = (float)Field(player, "_nextMoveTime");
        float inputDeadline = (float)Field(player, "_nextInputTime");
        Invoke(player, "RideStone", new Vector2Int(-2, 2), Vector3.left * .5f);
        Require(inputs.Count == 1 && (float)Field(player, "_nextMoveTime") == moveDeadline &&
            (float)Field(player, "_nextInputTime") == inputDeadline,
            "Platform transport leaves queued input and its timing windows unchanged.");
        SetField(player, "_nextInputTime", Time.time);
        Invoke(player, "QueueMove", Vector2Int.right);
        var spike = AssetDatabase.LoadAssetAtPath<DamageTile>("Assets/Gameplay/SpikeTile.asset");
        Require(map.TrySetObstacle(new Vector2Int(-2, 3), spike), "Make the pending direction fatal after transport.");
        SetField(player, "_nextMoveTime", Time.time);
        Invoke(player, "ProcessMoveQueue");
        Require(player.Position == new Vector2Int(-2, 3) && player.Health == 0 && inputs.Count == 0,
            "A queued move uses the transported position and death discards all remaining input.");

        player.Initialize(map);
        GameStateManager.Instance.SetState(GameState.Playing);
        Invoke(player, "QueueMove", Vector2Int.right);
        player.Initialize(map);
        Require(inputs.Count == 0 && player.TryMove(Vector2Int.left), "Initialization clears pending input and timing windows.");
        SetField(player, "_nextInputTime", Time.time);
        Invoke(player, "QueueMove", Vector2Int.up);
        player.gameObject.SetActive(false);
        Invoke(player, "OnDisable");
        Require(inputs.Count == 0, "Disabling the player discards pending input.");
        player.gameObject.SetActive(true);
        player.Initialize(map);
        Invoke(player, "QueueMove", Vector2Int.right);
        Require(player.TakeDamage(player.MaxHealth) && inputs.Count == 0, "Health-based death also clears the queue.");
    }

    private static void ClearObstacles(MapManager map)
    {
        var obstacles = Field(map, "_obstacles").As<System.Collections.Generic.Dictionary<Vector2Int, MapObstacle>>();
        foreach (var obstacle in obstacles.Values) UnityEngine.Object.DestroyImmediate(obstacle.gameObject);
        obstacles.Clear();
        Field(map, "_movingObstacles").As<System.Collections.IList>().Clear();
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

    private static MapManager StartMap(HazardRowDefinition[] hazardRows, float coinChance = 0f, float obstacleChance = 0f, int seed = 1,
        MovingObstacleDefinition[] moving = null)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        ConfigureMap(map, hazardRows, seed, obstacleChance);
        if (moving != null)
        {
            foreach (var obstacle in moving)
                obstacle.tile = AssetDatabase.LoadAssetAtPath<DamageTile>(obstacle.movement == ObstacleMovement.Patrol
                    ? "Assets/Gameplay/RollingRockTile.asset" : "Assets/Gameplay/MonsterTile.asset");
            SetField(Field(map, "_patterns").As<MapPattern[]>()[0], "_movingObstacles", moving);
        }
        SetField(map, "_coinChance", coinChance);
        var gameplay = UnityEngine.Object.FindFirstObjectByType<GameplayController>();
        SetField(UnityEngine.Object.FindFirstObjectByType<PlayerController>(), "_startPosition", Vector2Int.zero);
        SetField(gameplay, "_moveAchievementIds", Array.Empty<string>());
        typeof(GameStateManager).GetProperty(nameof(GameStateManager.Instance))!.SetValue(null, UnityEngine.Object.FindFirstObjectByType<GameStateManager>());
        Invoke(gameplay, "Start");
        return map;
    }

    public static void ConfigureMap(MapManager map, HazardRowDefinition[] hazards, int seed = 1, float obstacleChance = 0f)
    {
        var pattern = MapPatternEditor.CreatePattern("Regression pattern", MapPattern.MaximumLength, "regression", true);
        pattern.transform.SetParent(map.transform, false);
        pattern.gameObject.SetActive(false);
        var localRows = new HazardRowDefinition[hazards.Length];
        for (int i = 0; i < hazards.Length; i++)
            localRows[i] = new HazardRowDefinition { row = hazards[i].row,
                steppingStoneColumns = hazards[i].steppingStoneColumns, steppingStones = hazards[i].steppingStones };
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
        var collectibleViews = Field(map, "_collectibleViews").As<System.Collections.IDictionary>();
        SetField(map, "_cellTriggers", new[]
        {
            new CellTriggerDefinition { id = "duplicate", position = new Vector2Int(0, 1) },
            new CellTriggerDefinition { id = "duplicate", position = new Vector2Int(0, 2) }
        });
        bool rejected = false;
        try { map.LoadRows(0); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && !Field(map, "_contentInitialized").As<bool>() &&
            collectibleViews.Count == 0,
            "Failed configuration validation creates no collectible objects and does not initialize content.");
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
        var structures = Field(map, "_obstacles").As<System.Collections.Generic.Dictionary<Vector2Int, MapObstacle>>();
        for (int x = -MapManager.HalfWidth; x < MapManager.HalfWidth; x++)
            Require(map.TrySetObstacle(new Vector2Int(x, 1), tile), "A row may be blocked while one crossing remains.");
        Require(!map.TrySetObstacle(new Vector2Int(MapManager.HalfWidth, 1), tile) &&
            !structures.ContainsKey(new Vector2Int(MapManager.HalfWidth, 1)), "The final forward crossing cannot be blocked and rejected placement rolls back.");
        Require(!map.TrySetObstacle(Vector2Int.zero, tile), "Do not place obstacles under the player.");

        ClearObstacles(map);
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
        Require(MovePlayer(player, Vector2Int.up) && spawns == 1, "Existing row event can spawn an obstacle.");
        Require(MovePlayer(player, Vector2Int.down) && MovePlayer(player, Vector2Int.up) && spawns == 1, "Revisiting the spawn row does not duplicate the obstacle.");

        var hazards = new[] { new HazardRowDefinition { row = 3, steppingStoneColumns = new[] { 0 } } };
        map = StartMap(hazards);
        Require(map.IsLethal(new Vector2Int(1, 3)) && !map.IsLethal(new Vector2Int(0, 3)), "Only stepping stones are safe on hazard rows.");
        Require(!map.TryPlaceCoin(new Vector2Int(1, 3)) && !map.RegisterCellTrigger(new CellTriggerDefinition { id = "water", position = new Vector2Int(1, 3) }),
            "Coins and triggers cannot be placed in lethal cells.");
        Require(map.TryPlaceCoin(new Vector2Int(0, 3)) && map.TryMoveSteppingStone(new Vector2Int(0, 3), new Vector2Int(1, 3)),
            "A coin moves with its platform instead of preventing movement.");
        Invoke(map, "AdvanceSteppingStones", float.MaxValue);
        Require(map.IsLethal(new Vector2Int(0, 3)) && !map.IsLethal(new Vector2Int(1, 3)) &&
            map.GetCoinAmount(new Vector2Int(1, 3)) == 1, "Platform support and coin coordinates update together.");
        map.RemoveCoin(new Vector2Int(1, 3));
        Require(map.RegisterCellTrigger(new CellTriggerDefinition { id = "stone", position = new Vector2Int(1, 3) }) &&
            !map.TryMoveSteppingStone(new Vector2Int(1, 3), new Vector2Int(0, 3)), "A fixed cell trigger cannot be stranded.");
        map = StartMap(new[] { new HazardRowDefinition { row = 3, steppingStoneColumns = new[] { 0 } } });
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Require(MovePlayer(player, Vector2Int.up) && MovePlayer(player, Vector2Int.up) && MovePlayer(player, Vector2Int.up), "Reach a safe platform.");
        Require(map.TryMoveSteppingStone(player.Position, player.Position + Vector2Int.right) &&
            player.Position == new Vector2Int(1, 3) && GameStateManager.Instance.State == GameState.Playing,
            "The player is carried without being killed by the logical destination.");
        Invoke(map, "AdvanceSteppingStones", float.MaxValue);
        Require(player.transform.position == map.GetWorldPosition(player.Position), "The carried player arrives at the platform centre.");
        int score = UnityEngine.Object.FindFirstObjectByType<GameplayController>().Score;
        Require(MovePlayer(player, Vector2Int.right) && GameStateManager.Instance.State == GameState.GameOver &&
            !MovePlayer(player, Vector2Int.up) && UnityEngine.Object.FindFirstObjectByType<GameplayController>().Score == score,
            "Entering a lethal cell ends the run without granting further points.");

        map = StartMap(new[] { new HazardRowDefinition { row = 2, steppingStoneColumns = new[] { 0 } } });
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var camera = UnityEngine.Object.FindFirstObjectByType<GameplayCamera>();
        Invoke(camera, "Awake");
        Require(MovePlayer(player, Vector2Int.up), "Reach the row before the lethal forward cell.");
        Invoke(camera, "LateUpdate");
        Vector3 beforeDeath = camera.transform.position;
        Require(MovePlayer(player, Vector2Int.right) && MovePlayer(player, Vector2Int.up) && GameStateManager.Instance.State == GameState.GameOver,
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
            Require(Field(map, "_obstacles").As<System.Collections.IDictionary>().Count > 0, "Random obstacles are actually generated.");
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
                        MovePlayer(player, next - player.Position), "Generated map can be traversed under the actual backward limit.");
                }
                foreach (var coin in map.Coins)
                    Require(map.CanMoveTo(coin.Key) && !map.IsLethal(coin.Key) &&
                        map.GetContactDamage(coin.Key) == 0, "Every generated coin stays on a safe cell.");
                Require((bool)typeof(MapManager).GetMethod("HasValidRoutes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(map, null),
                    "All three patterns' coins stay reachable under the actual backward limit.");
                Require(GameStateManager.Instance.State == GameState.Playing, "Following a safe route never enters a lethal row cell.");
            }
        }
    }

    private static void CheckDamageObstacles()
    {
        var map = StartMap(Array.Empty<HazardRowDefinition>());
        var terrain = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
        var spike = AssetDatabase.LoadAssetAtPath<DamageTile>("Assets/Gameplay/SpikeTile.asset");
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        int healthEvents = 0;
        player.HealthChanged += _ => healthEvents++;
        Require(map.TrySetObstacle(Vector2Int.right, terrain) && !MovePlayer(player, Vector2Int.right) && player.Health == 3,
            "Terrain blocks entry without killing a player on a different coordinate.");
        ClearObstacles(map);
        Require(map.TrySetObstacle(Vector2Int.up, spike) && map.CanMoveTo(Vector2Int.up),
            "Damage obstacles permit entry.");
        Require(!map.TryPlaceCoin(Vector2Int.up) &&
            !map.RegisterCellTrigger(new CellTriggerDefinition { id = "spike", position = Vector2Int.up }),
            "Damage cells cannot host coins or triggers.");
        SetField(player, "_nextDamageTime", float.PositiveInfinity);
        Require(MovePlayer(player, Vector2Int.up) && player.Health == 0 && healthEvents == 1 &&
            GameStateManager.Instance.State == GameState.GameOver &&
            UnityEngine.Object.FindFirstObjectByType<GameplayController>().Score == 0,
            "Entering an obstacle kills immediately despite damage immunity and grants no fatal-step points.");
        map.DamagePlayerAt(player.Position);
        Require(!player.Die() && !MovePlayer(player, Vector2Int.up) && !player.TakeDamage(1) && healthEvents == 1,
            "Repeated overlap settles death and health notification only once.");
        bool invalidDamage = false;
        try { player.TakeDamage(0); } catch (ArgumentOutOfRangeException) { invalidDamage = true; }
        Require(invalidDamage, "Invalid damage remains rejected.");

        var patrol = new MovingObstacleDefinition { position = new Vector2Int(-2, 2),
            movement = ObstacleMovement.Patrol, direction = Vector2Int.right, distance = 2 };
        map = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { patrol });
        for (int step = 0; step < 5; step++)
        {
            Invoke(map, "AdvanceMovingObstacles", step * 2f);
            var mover = Field(map, "_movingObstacles").As<System.Collections.IList>()[0];
            var position = (Vector2Int)Field(mover, "Position");
            Require(position.y == 2 && position.x >= -2 && position.x <= 0,
                "Rolling obstacles stay in their authored row and range.");
        }
        var chase = new MovingObstacleDefinition { position = new Vector2Int(0, 3), movement = ObstacleMovement.Chase };
        map = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { chase });
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Require(MovePlayer(player, Vector2Int.up), "Approach the chasing monster.");
        SetField(player, "_nextDamageTime", float.PositiveInfinity);
        Invoke(map, "AdvanceMovingObstacles", 0f);
        Invoke(map, "AdvanceMovingObstacles", 2f);
        Require(player.Health == 3, "Reserving the player's coordinate does not cause early damage.");
        Invoke(map, "AdvanceMovingObstacles", 4f);
        Require(player.Health == 0 && GameStateManager.Instance.State == GameState.GameOver,
            "A monster entering the player's coordinate causes immediate death.");
        Require(chase.position == new Vector2Int(0, 3) && patrol.position == new Vector2Int(-2, 2),
            "Runtime movement preserves authored positions.");
        map = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { chase });
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        terrain = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
        Require(map.TrySetObstacle(new Vector2Int(0, 2), terrain) &&
            map.TryGetNextStep(new Vector2Int(0, 3), player.Position, out var next) && next.x != 0 && next.y == 3,
            "Monsters route around impassable terrain.");
        patrol.direction = Vector2Int.up;
        bool rejected = false;
        try { StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { patrol }); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { rejected = true; }
        Require(rejected, "Vertical rolling-rock patrols are rejected.");
    }

    private static void CheckChaserLifetimeAndBridges()
    {
        var chase = new MovingObstacleDefinition { position = new Vector2Int(2, 1), movement = ObstacleMovement.Chase };
        var map = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { chase });
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        SetField(player, "_nextDamageTime", float.PositiveInfinity);
        var movers = (System.Collections.IList)Field(map, "_movingObstacles");
        var originalMonster = movers[0];
        var placements = (System.Collections.IList)Field(map, "_placedPatterns");
        var originalView = (MapPattern)Field(placements[0], "View");
        for (int column = 0; column < MapManager.HalfWidth; column++)
            Require(MovePlayer(player, Vector2Int.left), "Keep the lifetime traversal separate from fatal monster contact.");
        for (int row = 1; row <= MapPattern.MaximumLength * 2; row++)
        {
            Require(MovePlayer(player, Vector2Int.up), "Traverse two full pattern boundaries while the monster follows.");
            if (row == MapPattern.MaximumLength)
            {
                Require(originalView != null && placements.Count == 3 && map.GetComponentsInChildren<MapPattern>().Length == 3,
                    "The previous pattern remains intact after the first boundary.");
                Require(!map.TryGetNextStep(player.Position, player.Position + Vector2Int.down * 2, out _),
                    "Retaining the previous floor does not allow a player path beyond the one-row backward limit.");
            }
            if (row == MapPattern.MaximumLength * 2)
                Require(originalView == null && movers.Contains(originalMonster),
                    "A following monster survives removal of its original pattern.");
            Invoke(map, "AdvanceMovingObstacles", row * 2f);
        }
        var monsterPosition = (Vector2Int)Field(originalMonster, "Position");
        while (Math.Abs(player.Position.x - monsterPosition.x) + Math.Abs(player.Position.y - monsterPosition.y) <
            MapManager.MonsterDespawnDistance - 1)
            Require(MovePlayer(player, Vector2Int.up) && movers.Contains(originalMonster), "A monster remains alive within seven cells.");
        Require(movers.Contains(originalMonster), "A monster remains alive at a distance of seven cells.");
        Require(MovePlayer(player, Vector2Int.up) && !movers.Contains(originalMonster) && map.GetContactDamage(monsterPosition) == 0,
            "Exactly eight cells of separation removes the monster and its contact damage.");
        map.LoadRows(player.FurthestRow);
        Require(!movers.Contains(originalMonster), "A despawned monster does not respawn on row reload.");

        chase.position = new Vector2Int(0, 3);
        map = StartMap(new[] { new HazardRowDefinition { row = 4, steppingStoneColumns = new[] { -1, 0, 1 } } }, moving: new[] { chase });
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        SetField(player, "_nextDamageTime", float.PositiveInfinity);
        Require(MovePlayer(player, Vector2Int.left), "Approach a bridge without occupying the monster's cell.");
        for (int row = 1; row <= 3; row++) Require(MovePlayer(player, Vector2Int.up), "Reach the river bank.");
        Require(map.TryGetNextStep(player.Position, new Vector2Int(-1, 4), out var next) && next == new Vector2Int(-1, 4),
            "The player can path across a stepping stone while a monster waits at the bank.");
        Require(!map.TryMoveObstacle(new Vector2Int(0, 3), new Vector2Int(0, 4)), "Monsters cannot directly enter a stepping stone.");
        Require(MovePlayer(player, Vector2Int.up) && MovePlayer(player, Vector2Int.up), "The player can cross the stepping stone.");
        Require(!map.TryGetNextStep(new Vector2Int(0, 3), player.Position, out _), "Monster pathfinding cannot cross the river row.");
        Invoke(map, "AdvanceMovingObstacles", float.MaxValue);
        Require(map.GetContactDamage(new Vector2Int(0, 3)) > 0, "A nearby river-blocked monster remains at the bank.");
        for (int row = 6; row <= 9; row++) Require(MovePlayer(player, Vector2Int.up), "Leave the blocked monster behind.");
        Require(map.GetContactDamage(new Vector2Int(0, 3)) > 0 && MovePlayer(player, Vector2Int.up) &&
            map.GetContactDamage(new Vector2Int(0, 3)) == 0, "A river-blocked monster despawns at eight cells.");

        chase.position = new Vector2Int(0, 3);
        map = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { chase });
        player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        movers = (System.Collections.IList)Field(map, "_movingObstacles");
        originalMonster = movers[0];
        for (int column = 1; column <= MapManager.HalfWidth; column++)
            Require(MovePlayer(player, Vector2Int.right), "Move laterally to a Manhattan distance of seven cells.");
        for (int row = 1; row <= 6; row++)
            Require(MovePlayer(player, Vector2Int.up) && movers.Contains(originalMonster), "A diagonally separated monster remains within seven cells.");
        Require(movers.Contains(originalMonster) && MovePlayer(player, Vector2Int.up) && !movers.Contains(originalMonster),
            "Both coordinate differences count toward the eight-cell despawn threshold.");
    }

    private static void CheckSmoothObstacleContact()
    {
        foreach (float duration in new[] { .5f, 1f, 2f })
        {
            var definition = new MovingObstacleDefinition { position = new Vector2Int(-2, 2),
                movement = ObstacleMovement.Patrol, distance = 2, stepInterval = duration };
            var map = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { definition });
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            Require(MovePlayer(player, Vector2Int.left) && MovePlayer(player, Vector2Int.up) && MovePlayer(player, Vector2Int.up),
                "Stand in the future destination before movement begins.");
            float started = Time.time;
            Invoke(map, "AdvanceMovingObstacles", started);
            var mover = Field(map, "_movingObstacles").As<System.Collections.IList>()[0];
            var view = (MapObstacle)Field(mover, "View");
            var source = definition.position;
            var target = player.Position;
            Require((Vector2Int)Field(mover, "Position") == source && (Vector2Int)Field(mover, "Destination") == target &&
                map.GetContactDamage(source) == 0 && map.GetContactDamage(target) == 0 && player.Health == 3,
                "The source and destination stay reserved but neither deals contact damage during transit.");
            Require(!map.TryMoveObstacle(source, source + Vector2Int.left) &&
                !map.TryMoveObstacle(target, target + Vector2Int.up) &&
                (Vector2Int)Field(mover, "Destination") == target,
                "An in-flight obstacle rejects redirection from its source or destination.");
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Gameplay/ObstacleTile.asset");
            Require(!map.TrySetObstacle(source, tile) && !map.TryPlaceCoin(source) &&
                !map.RegisterCellTrigger(new CellTriggerDefinition { id = "source", position = source }) &&
                !map.TrySetObstacle(target, tile) && !map.TryPlaceCoin(target) &&
                !map.RegisterCellTrigger(new CellTriggerDefinition { id = "reserved", position = target }),
                "Placement cannot overwrite a reserved destination.");
            var other = new Vector2Int(0, 2);
            Require(map.TrySetObstacle(other, tile) && !map.TryMoveObstacle(other, target),
                "A second obstacle cannot enter the same reserved destination.");
            Invoke(map, "AdvanceMovingObstacles", started + duration * .5f);
            Require(Mathf.Approximately(view.transform.position.x, map.GetWorldPosition(source).x + .5f) &&
                player.Health == 3 && map.GetContactDamage(source) == 0 && map.GetContactDamage(target) == 0,
                "The sprite advances at its configured speed with no contact damage in either cell.");
            GameStateManager.Instance.SetState(GameState.Paused);
            Vector3 paused = view.transform.position;
            Invoke(map, "Update");
            Require(view.transform.position == paused && player.Health == 3, "Pausing freezes motion and delayed contact.");
            GameStateManager.Instance.SetState(GameState.Playing);
            Invoke(map, "AdvanceMovingObstacles", started + duration * .99f);
            Require(player.Health == 3 && map.GetContactDamage(target) == 0, "Contact does not switch before the full step duration.");
            SetField(player, "_nextDamageTime", float.PositiveInfinity);
            Invoke(map, "AdvanceMovingObstacles", started + duration);
            Require((Vector2Int)Field(mover, "Position") == target && view.transform.position == map.GetWorldPosition(target) &&
                map.GetContactDamage(source) == 0 && map.GetContactDamage(target) > 0 && player.Health == 0 &&
                GameStateManager.Instance.State == GameState.GameOver,
                "Arrival transfers the hit coordinate and kills immediately despite immunity.");
        }

        var patrol = new MovingObstacleDefinition { position = new Vector2Int(-2, 2),
            movement = ObstacleMovement.Patrol, distance = 2, stepInterval = 1f };
        var dodgeMap = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { patrol });
        var dodger = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Require(MovePlayer(dodger, Vector2Int.left) && MovePlayer(dodger, Vector2Int.up) && MovePlayer(dodger, Vector2Int.up),
            "Enter the future destination.");
        Invoke(dodgeMap, "AdvanceMovingObstacles", 0f);
        Invoke(dodgeMap, "AdvanceMovingObstacles", .5f);
        Require(MovePlayer(dodger, Vector2Int.up), "Leave the destination while the obstacle is moving.");
        Invoke(dodgeMap, "AdvanceMovingObstacles", 1f);
        var resumedMover = Field(dodgeMap, "_movingObstacles").As<System.Collections.IList>()[0];
        Require(dodger.Health == 3 && (bool)Field(resumedMover, "IsMoving") &&
            dodgeMap.GetContactDamage(new Vector2Int(-1, 2)) == 0,
            "Dodging avoids arrival contact and the next movement starts immediately without a rest.");

        var sourceMap = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { patrol });
        var entrant = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Require(MovePlayer(entrant, Vector2Int.left) && MovePlayer(entrant, Vector2Int.left) && MovePlayer(entrant, Vector2Int.up),
            "Approach the source cell.");
        Invoke(sourceMap, "AdvanceMovingObstacles", 0f);
        Invoke(sourceMap, "AdvanceMovingObstacles", .5f);
        Require(MovePlayer(entrant, Vector2Int.up) && entrant.Health == 3,
            "Entering the source cell during transit does not cause damage.");
        sourceMap.DamagePlayerAt(entrant.Position);
        Require(entrant.Health == 3, "Direct contact checks also ignore an in-flight obstacle.");

        var chase = new MovingObstacleDefinition { position = new Vector2Int(0, 3), movement = ObstacleMovement.Chase };
        var chaseMap = StartMap(Array.Empty<HazardRowDefinition>(), moving: new[] { chase });
        var chased = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Invoke(chaseMap, "AdvanceMovingObstacles", 0f);
        var monster = Field(chaseMap, "_movingObstacles").As<System.Collections.IList>()[0];
        var destination = (Vector2Int)Field(monster, "Destination");
        Require(MovePlayer(chased, Vector2Int.left), "Change the player's position while a monster is moving.");
        Invoke(chaseMap, "AdvanceMovingObstacles", chase.stepInterval * .5f);
        Require((Vector2Int)Field(monster, "Destination") == destination &&
            (Vector2Int)Field(monster, "Position") == chase.position &&
            !chaseMap.TryMoveObstacle(chase.position, chase.position + Vector2Int.left),
            "A chasing monster keeps its original destination until arrival despite a new player position.");
        Invoke(chaseMap, "AdvanceMovingObstacles", chase.stepInterval);
        Require((Vector2Int)Field(monster, "Position") == destination &&
            ((MapObstacle)Field(monster, "View")).transform.position == chaseMap.GetWorldPosition(destination),
            "The monster finishes its committed step before choosing its next direction.");
    }

    private static void CheckMovingSteppingStones()
    {
        var map = StartMap(new[]
        {
            new HazardRowDefinition { row = 3, steppingStones = new[]
            {
                new SteppingStoneDefinition { column = -2, movement = SteppingStoneMovement.PingPong, speed = 2f, distance = 1 },
                new SteppingStoneDefinition { column = 2, movement = SteppingStoneMovement.Stationary, speed = 5f }
            } }
        });
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        Require(MovePlayer(player, Vector2Int.up) && MovePlayer(player, Vector2Int.up) &&
            MovePlayer(player, Vector2Int.left) && MovePlayer(player, Vector2Int.left), "Reach the bank next to a moving platform.");
        Invoke(map, "AdvanceSteppingStones", 0f);
        Require(MovePlayer(player, Vector2Int.up) && player.Position == new Vector2Int(-1, 3),
            "A platform already in motion accepts a rider without a recursive move event.");
        int score = UnityEngine.Object.FindFirstObjectByType<GameplayController>().Score;
        Invoke(map, "AdvanceSteppingStones", 0.25f);
        Require(Mathf.Approximately(player.transform.position.x, map.GetWorldPosition(new Vector2Int(-2, 3)).x + 0.5f) &&
            !map.IsLethal(player.Position) && GameStateManager.Instance.State == GameState.Playing,
            "At two cells per second, rider and platform travel half a cell in a quarter second.");
        GameStateManager.Instance.SetState(GameState.Paused);
        Vector3 paused = player.transform.position;
        Invoke(map, "Update");
        Require(player.transform.position == paused, "Paused game updates do not move platforms or riders.");
        GameStateManager.Instance.SetState(GameState.Playing);
        Invoke(map, "AdvanceSteppingStones", 0.5f);
        Require(!map.IsLethal(new Vector2Int(2, 3)) && player.Position == new Vector2Int(-2, 3) &&
            UnityEngine.Object.FindFirstObjectByType<GameplayController>().Score == score,
            "The moving platform reverses while the second stays stationary and carries do not award distance points.");
        Require(MovePlayer(player, Vector2Int.up), "Disembark onto the next safe row.");
        var template = Field(map, "_patterns").As<MapPattern[]>()[0];
        SetField(template, "_lavaRows", new[] { new HazardRowDefinition { row = 3, steppingStones = new[]
        {
            new SteppingStoneDefinition { column = 0, movement = SteppingStoneMovement.PingPong, speed = 0f }
        } } });
        bool rejected = false;
        try { template.Validate(); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "Zero platform speed is rejected before map generation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target);

    private static T As<T>(this object value) => (T)value;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
