using System;
using System.IO;
using System.Reflection;
using CrossTheBoard;
using CrossTheBoard.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>Run in an isolated validation project: -executeMethod CollectionRegressionChecks.Run.</summary>
public static class CollectionRegressionChecks
{
    public static void Run()
    {
        try
        {
            // Refuse to touch the real player's save directory.
            Require(Application.companyName == "CodexValidation", "Use an isolated project with companyName CodexValidation.");
            CheckCatalog();
            EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
            var save = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
            Singleton(typeof(SaveManager), save);
            CheckSave(save);
            CheckAchievements(save);
            CheckGameplay(save);
            CheckDeathSettlement();
            CheckMenu();
            Debug.Log("[CollectionRegressionChecks] PASS: migration, purchases, rollback, coin placement/collection, occupancy, row/cell events, unlocks, death settlement/failure, preview/confirmation, selected gameplay character and menu navigation.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckCatalog()
    {
        var data = new SaveData();
        Require(CharacterCatalog.Characters.Count == 10 && data.unlockedCharacterIds.Count == 1, "Ten characters; exactly one starter.");
        int shop = 0, special = 0;
        foreach (var character in CharacterCatalog.Characters)
        {
            if (character.UnlockCondition == CharacterUnlockCondition.ShopPurchase) shop++;
            else if (character.UnlockCondition != CharacterUnlockCondition.Default) special++;
            Require(CharacterCatalog.GetSprite(character.Id) != null, "Every character has a portrait.");
        }
        Require(shop == 4 && special == 5, "Four shop-only and five special unlocks.");
        Reject(() => CharacterCatalog.SelectCharacter(data, "robot"));
        Reject(() => CharacterCatalog.PurchaseCharacter(data, "robot"));
        data.bestDistance = 25;
        data.totalCoinsCollected = 100;
        data.totalForwardCells = 100;
        CharacterCatalog.EvaluateUnlocks(data);
        Require(data.unlockedCharacterIds.Count == 6 && !CharacterCatalog.IsUnlocked(data, "robot"), "All five conditions unlock without unlocking shop characters.");
        data.coins = 100;
        CharacterCatalog.PurchaseCharacter(data, "robot");
        Require(data.coins == 50 && CharacterCatalog.IsUnlocked(data, "robot"), "Purchase debits exact price.");
        Reject(() => CharacterCatalog.PurchaseCharacter(data, "robot"));
        Require(data.coins == 50, "Duplicate purchase does not charge twice.");
        CharacterCatalog.SelectCharacter(data, "robot");
        Require(data.selectedCharacterId == "robot" && data.coins == 50, "Selecting a character does not spend currency.");
    }

    private static void CheckSave(SaveManager save)
    {
        string legacy = Path.Combine(Application.temporaryCachePath, "collection-v1.json");
        File.WriteAllText(legacy, "{\"version\":1,\"coins\":17,\"bgmVolume\":0.4,\"effectsVolume\":0.6,\"achievements\":[]}");
        object[] args = { legacy, null };
        bool loaded = (bool)typeof(SaveManager).GetMethod("TryRead", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(save, args);
        var migrated = (SaveData)args[1];
        Require(loaded && migrated.version == SaveData.CurrentVersion && migrated.coins == 17 && migrated.bgmVolume == 0.4f && migrated.unlockedCharacterIds.Count == 1,
            "Version 1 wallet/audio migrate without granting extra characters.");
        File.Delete(legacy);
        string previous = Path.Combine(Application.temporaryCachePath, "collection-v2.json");
        File.WriteAllText(previous, "{\"version\":2,\"coins\":75,\"achievements\":[],\"selectedCharacterId\":\"robot\",\"unlockedCharacterIds\":[\"slime\",\"robot\"],\"selectedSkinId\":\"slime_sun\",\"unlockedSkinIds\":[\"slime_sun\"]}");
        args = new object[] { previous, null };
        loaded = (bool)typeof(SaveManager).GetMethod("TryRead", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(save, args);
        migrated = (SaveData)args[1];
        Require(loaded && migrated.version == SaveData.CurrentVersion && migrated.coins == 75 && migrated.selectedCharacterId == "robot" &&
            CharacterCatalog.IsUnlocked(migrated, "robot") && !JsonUtility.ToJson(migrated).Contains("Skin"),
            "Version 2 ignores removed skin fields while preserving wallet and characters.");
        File.Delete(previous);
        string versionThree = Path.Combine(Application.temporaryCachePath, "collection-v3.json");
        File.WriteAllText(versionThree, "{\"version\":3,\"coins\":75,\"achievements\":[],\"selectedCharacterId\":\"robot\",\"unlockedCharacterIds\":[\"slime\",\"robot\"],\"bestDistance\":25}");
        args = new object[] { versionThree, null };
        loaded = (bool)typeof(SaveManager).GetMethod("TryRead", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(save, args);
        migrated = (SaveData)args[1];
        Require(loaded && migrated.version == SaveData.CurrentVersion && migrated.coins == 75 && migrated.bestDistance == 25 &&
            migrated.lastScore == 0 && migrated.bestScore == 0 && migrated.lastRunCoins == 0,
            "Version 3 retains existing progress and defaults new run-result fields.");
        File.Delete(versionThree);
        Property(save, "Data", new SaveData { coins = 80 });
        int events = 0;
        save.DataChanged += () => events++;
        Require(save.TryUpdate(data => CharacterCatalog.PurchaseCharacter(data, "robot")), "Purchase persists.");
        Require(save.Data.coins == 30 && events == 1, "Successful transaction publishes one refresh.");
        var before = save.Data;
        Reject(() => save.TryUpdate(data => CharacterCatalog.PurchaseCharacter(data, "cat")));
        Require(ReferenceEquals(before, save.Data) && save.Data.coins == 30 && events == 1, "Failed purchase keeps original data.");
        string blockedTemporaryPath = save.SavePath + ".tmp";
        Require(!File.Exists(blockedTemporaryPath) && !Directory.Exists(blockedTemporaryPath), "Test temporary path must be unused.");
        Directory.CreateDirectory(blockedTemporaryPath);
        try
        {
            Require(!save.TryUpdate(data => data.coins += 10) && ReferenceEquals(before, save.Data) && events == 1,
                "Actual disk write failure rolls back candidate and publishes no successful update.");
        }
        finally { Directory.Delete(blockedTemporaryPath); }
        Property(save, "CanSave", false);
        Require(!save.TryUpdate(data => data.coins += 10) && save.Data.coins == 30, "Disabled saves cannot mutate wallet.");
        Property(save, "CanSave", true);
        Require(save.Load() && save.Data.coins == 30 && CharacterCatalog.IsUnlocked(save.Data, "robot"), "Wallet and ownership survive reload.");
        before = save.Data;
        Action<SaveData>[] invalidUpdates =
        {
            data => data.bgmVolume = float.NaN,
            data => data.achievements = null,
            data => data.unlockedCharacterIds = null,
            data => data.coins = -1,
            data => data.lastScore = -1,
            data => data.lastRunCoins = -1,
            data => data.lastScore = data.bestScore + 1,
            data => data.achievements.AddRange(new[]
            {
                new AchievementProgress { id = "duplicate" },
                new AchievementProgress { id = "duplicate" }
            })
        };
        foreach (var update in invalidUpdates)
        {
            bool rejected = false;
            try { save.TryUpdate(update); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected && ReferenceEquals(before, save.Data) && events == 1, "Invalid candidates never replace or persist the original save.");
        }
        bool nestedUpdateRan = false;
        bool nestedCallsRejected = false;
        Action nestedListener = () =>
        {
            nestedCallsRejected = !save.Save() && !save.Load() && !save.TryUpdate(data => nestedUpdateRan = true);
        };
        save.DataChanged += nestedListener;
        try
        {
            Require(save.TryUpdate(data => data.coins++) && nestedCallsRejected && !nestedUpdateRan && save.Data.coins == 31,
                "Nested persistence leaves the outer transaction intact.");
        }
        finally { save.DataChanged -= nestedListener; }
        int healthyListenerCalls = 0;
        Action brokenListener = () => throw new InvalidOperationException("Expected regression listener failure.");
        Action healthyListener = () => healthyListenerCalls++;
        save.DataChanged += brokenListener;
        save.DataChanged += healthyListener;
        try
        {
            Require(save.TryUpdate(data => data.coins++) && healthyListenerCalls == 1 && save.Data.coins == 32,
                "A logged listener failure cannot roll back a disk commit or suppress later listeners.");
        }
        finally
        {
            save.DataChanged -= brokenListener;
            save.DataChanged -= healthyListener;
        }
        Require(save.TryUpdate(data => data.coins = 30) && save.Load() && save.Data.coins == 30,
            "Persistence guards are released after callback failure.");
        Require(save.TryUpdate(data =>
        {
            nestedCallsRejected = !save.Save() && !save.Load() && !save.TryUpdate(nested => nestedUpdateRan = true);
        }) && nestedCallsRejected && !nestedUpdateRan && save.Data.coins == 30,
            "Candidate update callbacks cannot reenter persistence either.");
    }

    private static void CheckAchievements(SaveManager save)
    {
        var tracker = UnityEngine.Object.FindFirstObjectByType<AchievementTracker>();
        var original = tracker.Database;
        var database = ScriptableObject.CreateInstance<AchievementDatabase>();
        Set(database, "_achievements", new[] { new AchievementDefinition { id = "regression", goal = "Regression", target = 2, reward = 7 } });
        database.Validate();
        Set(tracker, "_database", database);
        int progressEvents = 0;
        Action listener = () => progressEvents++;
        tracker.ProgressChanged += listener;
        string blockedPath = save.SavePath + ".tmp";
        try
        {
            var before = save.Data;
            Require(!File.Exists(blockedPath) && !Directory.Exists(blockedPath), "Achievement test temporary path must be unused.");
            Directory.CreateDirectory(blockedPath);
            try
            {
                Require(!tracker.AddProgress("regression") && ReferenceEquals(before, save.Data) && progressEvents == 0,
                    "Achievement write failure preserves the entire original save and publishes no progress.");
            }
            finally { Directory.Delete(blockedPath); }
            int coins = save.Data.coins;
            Require(tracker.AddProgress("regression") && tracker.GetProgress("regression") == 1 && save.Data.coins == coins,
                "Partial achievement progress persists without rewarding coins.");
            Require(tracker.AddProgress("regression", int.MaxValue) && tracker.IsAchieved("regression") &&
                tracker.GetProgress("regression") == 2 && save.Data.coins == coins + 7 && progressEvents == 2,
                "Achievement completion caps progress and grants its reward once.");
            Require(tracker.AddProgress("regression") && save.Data.coins == coins + 7 && progressEvents == 2,
                "Completed achievements do not save or reward again.");
            Require(save.TryUpdate(data =>
            {
                data.coins = int.MaxValue;
                data.achievements.Clear();
            }), "Prepare reward overflow boundary.");
            before = save.Data;
            bool rejected = false;
            try { tracker.AddProgress("regression", 2); }
            catch (OverflowException) { rejected = true; }
            Require(rejected && ReferenceEquals(before, save.Data) && save.Data.achievements.Count == 0 && progressEvents == 2,
                "Reward overflow cannot partially complete an achievement.");
            Require(save.TryUpdate(data => data.coins = coins), "Restore wallet after boundary checks.");
        }
        finally
        {
            tracker.ProgressChanged -= listener;
            Set(tracker, "_database", original);
            UnityEngine.Object.DestroyImmediate(database);
        }
    }

    private static void CheckGameplay(SaveManager save)
    {
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var gameplay = UnityEngine.Object.FindFirstObjectByType<GameplayController>();
        var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        Singleton(typeof(GameStateManager), UnityEngine.Object.FindFirstObjectByType<GameStateManager>());
        ((Tilemap)Field(map, "_structures")).ClearAllTiles();
        Set(map, "_coinChance", 1f);
        GameplayRegressionChecks.ConfigureMap(map, Array.Empty<HazardRowDefinition>(), 42);
        Set(gameplay, "_moveAchievementIds", Array.Empty<string>());
        save.TryUpdate(data => CharacterCatalog.SelectCharacter(data, "robot"));
        Invoke(gameplay, "Start");
        Require(gameplay.ActiveCharacterId == "robot", "Saved character is used next run.");
        Require(player.Position == new Vector2Int(0, 1) && map.Coins.Count == MapManager.Width * (MapPattern.MaximumLength * 3 - 2) &&
            map.GetCoinAmount(new Vector2Int(0, 1)) == 0 && map.GetCoinAmount(Vector2Int.zero) == 0,
            "Whole-pattern generation populates all three prefabs and skips the start/back rows.");
        var obstacle = ScriptableObject.CreateInstance<Tile>();
        var occupied = new Vector2Int(-3, 2);
        Require(!map.TrySetObstacle(occupied, obstacle), "Obstacle cannot overlap coin.");
        Require(!map.RegisterCellTrigger(new CellTriggerDefinition { id = "occupied", position = occupied }), "Trigger cannot overlap coin.");
        map.RemoveCoin(occupied);
        Require(map.TrySetObstacle(occupied, obstacle) && !map.TryPlaceCoin(occupied), "Coin cannot overlap obstacle.");
        var triggerPosition = new Vector2Int(0, 3);
        map.RemoveCoin(triggerPosition);
        var cell = new CellTriggerDefinition { id = "cell", position = triggerPosition };
        var row = new RowTriggerDefinition { id = "row", row = 3 };
        int cellEvents = 0, rowEvents = 0;
        cell.onReached.AddListener(() => cellEvents++);
        row.onReached.AddListener(() => rowEvents++);
        Require(map.RegisterCellTrigger(cell) && map.RegisterRowTrigger(row), "Register independent cell/row events.");
        Require(!map.TryPlaceCoin(triggerPosition), "Coin cannot overlap trigger.");
        Require(!map.TrySetObstacle(triggerPosition, obstacle), "Obstacle cannot replace trigger.");
        int wallet = save.Data.coins;
        Require(player.TryMove(Vector2Int.up), "Move onto coin.");
        Require(save.Data.coins == wallet + 1 && gameplay.Score == 110 && gameplay.CollectedCoins == 1 && map.GetCoinAmount(new Vector2Int(0, 2)) == 0,
            "Coin collection persists and awards ten bonus points plus new-row score.");
        var overflowCell = new Vector2Int(1, 2);
        var beforeOverflow = save.Data;
        Set(gameplay, "_pointsPerCoin", int.MaxValue);
        bool overflowRejected = false;
        try { player.TryMove(Vector2Int.right); }
        catch (OverflowException) { overflowRejected = true; }
        finally { Set(gameplay, "_pointsPerCoin", 10); }
        Require(overflowRejected && ReferenceEquals(beforeOverflow, save.Data) && map.GetCoinAmount(overflowCell) == 1 &&
            gameplay.Score == 110 && gameplay.CollectedCoins == 1, "Coin score overflow fails before wallet commit or coin removal.");
        Require(player.TryMove(Vector2Int.left), "Return from the uncollected overflow coin after restoring its normal point value.");
        Require(player.TryMove(Vector2Int.down) && player.TryMove(Vector2Int.up), "Revisit collected coin.");
        Require(save.Data.coins == wallet + 1 && gameplay.Score == 110 && save.Data.totalForwardCells == 1, "Revisits cannot farm coins, points or unlock progress.");
        Require(player.TryMove(Vector2Int.up), "Enter trigger row.");
        Require(player.TryMove(Vector2Int.right), "Move laterally in event row.");
        Require(player.TryMove(Vector2Int.left), "Revisit cell.");
        Require(player.TryMove(Vector2Int.down) && player.TryMove(Vector2Int.up), "Revisit event row.");
        Require(rowEvents == 1 && cellEvents == 1, "Cell and row events default to once per run.");
        var failureCell = new Vector2Int(0, 4);
        wallet = save.Data.coins;
        Property(save, "CanSave", false);
        Require(player.TryMove(Vector2Int.up), "Movement remains possible when saving is unavailable.");
        Require(map.GetCoinAmount(failureCell) == 1 && save.Data.coins == wallet, "Persistence failure does not consume coin or change wallet.");
        Property(save, "CanSave", true);
        Require(player.TryMove(Vector2Int.down) && player.TryMove(Vector2Int.up), "Retry collection after save recovers.");
        Require(map.GetCoinAmount(failureCell) == 0 && save.Data.coins == wallet + 1 && save.Data.totalForwardCells == 3, "Pending progress commits exactly once with recovered collection.");
        map.LoadRows(player.FurthestRow);
        Require(map.GetCoinAmount(failureCell) == 0, "Loading same rows does not respawn collected coins.");
        for (int i = 3; i < 10; i++) Require(player.TryMove(Vector2Int.up), "Advance toward special unlock.");
        Require(CharacterCatalog.IsUnlocked(save.Data, "frog") && save.Data.bestDistance == 10, "Gameplay automatically unlocks distance character.");
        Require(save.Load() && save.Data.bestDistance == 10 && CharacterCatalog.IsUnlocked(save.Data, "frog"), "Run progression and unlock persist.");
        int finalScore = gameplay.Score;
        int runCoins = gameplay.CollectedCoins;
        wallet = save.Data.coins;
        int totalCoins = save.Data.totalCoinsCollected;
        GameStateManager.Instance.SetState(GameState.GameOver);
        Require(save.Data.lastScore == finalScore && save.Data.bestScore >= finalScore && save.Data.lastRunCoins == runCoins &&
            save.Data.coins == wallet && save.Data.totalCoinsCollected == totalCoins,
            "Death records final results without paying already-collected coins twice.");
        var settled = save.Data;
        Invoke(gameplay, "OnStateChanged", GameState.GameOver);
        Require(ReferenceEquals(settled, save.Data), "Repeated death notifications do not settle the same run again.");
        Require(save.Load() && save.Data.lastScore == finalScore && save.Data.lastRunCoins == runCoins && save.Data.coins == wallet,
            "Final score and run coins survive save reload.");
        UnityEngine.Object.DestroyImmediate(obstacle);
    }

    private static void CheckDeathSettlement()
    {
        foreach (bool canSave in new[] { false, true })
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
            var save = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
            Singleton(typeof(SaveManager), save);
            Require(save.Load(), "Load the last settled run.");
            Singleton(typeof(GameStateManager), UnityEngine.Object.FindFirstObjectByType<GameStateManager>());
            var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
            GameplayRegressionChecks.ConfigureMap(map, new[] { new HazardRowDefinition { row = 3, steppingStoneColumns = new[] { 0 } } });
            Set(map, "_coinChance", 0f);
            var gameplay = UnityEngine.Object.FindFirstObjectByType<GameplayController>();
            Set(gameplay, "_moveAchievementIds", Array.Empty<string>());
            Invoke(gameplay, "Start");
            var player = gameplay.Player;
            var original = save.Data;
            int totalForwardCells = original.totalForwardCells;
            Property(save, "CanSave", false);
            Require(player.TryMove(Vector2Int.up) && gameplay.Score == 100 && save.Data.totalForwardCells == totalForwardCells,
                "Unsaved safe progress remains pending during the run.");
            Property(save, "CanSave", canSave);
            Require(player.TryMove(Vector2Int.right) && player.TryMove(Vector2Int.up) && GameStateManager.Instance.State == GameState.GameOver,
                "An unsafe cell ends the run without extra score or coins.");
            Require(gameplay.Score == 100 && gameplay.CollectedCoins == 0 && save.Data.coins == original.coins,
                "The fatal forward row grants no points or wallet payment.");
            if (canSave)
                Require(save.Data.lastScore == 100 && save.Data.lastRunCoins == 0 && save.Data.totalForwardCells == totalForwardCells + 1,
                    "Death flushes exactly the pending safe row, not the fatal row.");
            else
                Require(ReferenceEquals(original, save.Data), "Failed death settlement preserves all previous save fields.");
            var settled = save.Data;
            Invoke(gameplay, "OnStateChanged", GameState.GameOver);
            Require(ReferenceEquals(settled, save.Data), "Neither failed nor successful death handling repeats its transaction.");
            Property(save, "CanSave", true);
            Require(save.Load() && save.Data.lastScore == settled.lastScore && save.Data.totalForwardCells == settled.totalForwardCells,
                "Settled or preserved data survives disk reload.");
        }
    }

    private static void CheckMenu()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenuScene.unity");
        var save = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
        Singleton(typeof(SaveManager), save);
        save.Load();
        var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuBootstrap>();
        var view = UnityEngine.Object.FindFirstObjectByType<MainMenuCollectionView>();
        var sections = (GameObject[])Field(menu, "_sections");
        var navigation = (Button[])Field(menu, "_navigation");
        Require(view != null && sections.Length == 5 && navigation.Length == 5, "Five sections and navigation buttons are authored in the scene.");
        int canvasObjectCount = navigation[0].GetComponentInParent<Canvas>().GetComponentsInChildren<Transform>(true).Length;
        foreach (var section in sections) Require(section != null, "Every section is serialized.");
        foreach (var button in navigation) Require(button != null && button.onClick.GetPersistentEventCount() == 2, "Navigation and status reset callbacks are saved in the scene.");
        var serialized = new SerializedObject(view);
        var reference = serialized.GetIterator();
        while (reference.NextVisible(true))
        {
            if (reference.propertyType == SerializedPropertyType.ObjectReference)
                Require(reference.objectReferenceValue != null, "Collection references are complete: " + reference.propertyPath);
        }
        foreach (var image in navigation[0].GetComponentInParent<Canvas>().GetComponentsInChildren<Image>(true))
        {
            if (image.sprite != null) Require(AssetDatabase.Contains(image.sprite), "Menu sprites are saved assets, not generated in memory.");
        }
        foreach (var button in navigation[0].GetComponentInParent<Canvas>().GetComponentsInChildren<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
        }
        Invoke(view, "Awake");
        Invoke(view, "Start");
        Require(canvasObjectCount == navigation[0].GetComponentInParent<Canvas>().GetComponentsInChildren<Transform>(true).Length,
            "Initializing collections creates no UI objects.");
        menu.SelectSection(1);
        var panel = sections[1].transform;
        Require(panel.Find("robot").GetComponent<Button>().interactable && !panel.Find("cat").GetComponent<Button>().interactable, "Owned/locked character cards differ and locked cards are not selectable.");
        view.PreviewCharacter("cat");
        Require(save.Data.selectedCharacterId == "robot", "Locked preview does not change selection.");
        view.PreviewCharacter("slime");
        Require(save.Data.selectedCharacterId == "robot", "Unlocked preview does not commit immediately.");
        view.ConfirmCharacter();
        Require(save.Data.selectedCharacterId == "slime" && save.Load() && save.Data.selectedCharacterId == "slime", "Confirm commits next-game selection and survives reload.");
        navigation[2].onClick.Invoke();
        Require(sections[2].activeSelf && !sections[1].activeSelf, "Serialized navigation opens the correct shop.");
        Require(((Text)Field(view, "_status")).text == string.Empty, "Changing tabs clears transient collection messages.");
        Require(sections[2].transform.Find("Buy cat/Purchase").GetComponent<Button>().interactable == false, "Insufficient wallet disables shop purchase.");
        save.TryUpdate(data => data.coins = 200);
        var buyCat = sections[2].transform.Find("Buy cat/Purchase").GetComponent<Button>();
        Require(buyCat.interactable, "Wallet refresh enables affordable purchases.");
        buyCat.onClick.Invoke();
        Require(save.Data.coins == 100 && CharacterCatalog.IsUnlocked(save.Data, "cat") && panel.Find("cat").GetComponent<Button>().interactable,
            "Shop purchase debits coins and refreshes unlocked card immediately.");
        Require(sections[2].transform.Find("slime_sun") == null && sections[2].transform.Find("Skin heading") == null,
            "Shop contains no removed skin controls.");
        Require(canvasObjectCount == navigation[0].GetComponentInParent<Canvas>().GetComponentsInChildren<Transform>(true).Length,
            "Purchases and selection refresh existing UI without creating objects.");
        Canvas.ForceUpdateCanvases();
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            CaptureMenu(menu, navigation);
    }

    private static void CaptureMenu(MainMenuBootstrap menu, Button[] navigation)
    {
        var canvas = navigation[0].GetComponentInParent<Canvas>();
        var cameraObject = new GameObject("Validation preview camera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 960;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.075f, 0.11f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100;
        var texture = new RenderTexture(1080, 1920, 24);
        camera.targetTexture = texture;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.enabled = false;
        canvas.scaleFactor = 1;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 10;
        for (int i = 0; i < navigation.Length; i++)
        {
            menu.SelectSection(i);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
            pixels.Apply();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationPreview-" + i + ".png"));
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Debug.Log("[CollectionRegressionChecks] Preview: " + path);
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(pixels);
        }
        camera.targetTexture = null;
        texture.Release();
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected invalid operation was accepted.");
    }
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
    private static void Singleton(Type type, object value) => type.GetProperty("Instance").SetValue(null, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
