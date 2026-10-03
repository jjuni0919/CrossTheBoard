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
            Require(ground.cellBounds.size.x == 7 && ground.cellBounds.size.y == MapManager.VisibleRows,
                "Loaded map is seven columns wide with the configured visible rows.");
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
            Require(!player.TryMove(Vector2Int.right) && player.Position.x == 3, "Seven-column right boundary is enforced.");
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

            Debug.Log("[GameplayRegressionChecks] PASS: backward limit, forward-only camera/map, repeat-proof scoring, bonuses, state guards and score events.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
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
