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
            CheckGameplay(save);
            CheckMenu();
            Debug.Log("[CollectionRegressionChecks] PASS: migration, purchases, rollback, coin placement/collection, occupancy, row/cell events, unlocks, preview/confirmation, selected gameplay character and menu navigation.");
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
        CharacterCatalog.PurchaseSkin(data, "slime_sun");
        CharacterCatalog.SelectSkin(data, "slime_sun");
        Require(data.coins == 25 && data.selectedSkinId == "slime_sun", "Skin purchase and equip.");
        CharacterCatalog.SelectCharacter(data, "robot");
        Require(data.selectedSkinId == CharacterCatalog.DefaultSkinId, "Changing characters resets incompatible skin.");
    }

    private static void CheckSave(SaveManager save)
    {
        string legacy = Path.Combine(Application.temporaryCachePath, "collection-v1.json");
        File.WriteAllText(legacy, "{\"version\":1,\"coins\":17,\"bgmVolume\":0.4,\"effectsVolume\":0.6,\"achievements\":[]}");
        object[] args = { legacy, null };
        bool loaded = (bool)typeof(SaveManager).GetMethod("TryRead", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(save, args);
        var migrated = (SaveData)args[1];
        Require(loaded && migrated.version == 2 && migrated.coins == 17 && migrated.bgmVolume == 0.4f && migrated.unlockedCharacterIds.Count == 1,
            "Version 1 wallet/audio migrate without granting extra characters.");
        File.Delete(legacy);
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
    }

    private static void CheckGameplay(SaveManager save)
    {
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var gameplay = UnityEngine.Object.FindFirstObjectByType<GameplayController>();
        var map = UnityEngine.Object.FindFirstObjectByType<MapManager>();
        Singleton(typeof(GameStateManager), UnityEngine.Object.FindFirstObjectByType<GameStateManager>());
        ((Tilemap)Field(map, "_structures")).ClearAllTiles();
        Set(map, "_coinChance", 1f);
        Set(map, "_coinSeed", 42);
        Set(gameplay, "_moveAchievementIds", Array.Empty<string>());
        save.TryUpdate(data => CharacterCatalog.SelectCharacter(data, "robot"));
        Invoke(gameplay, "Start");
        Require(gameplay.ActiveCharacterId == "robot", "Saved character is used next run.");
        Require(map.Coins.Count == 91 && map.GetCoinAmount(Vector2Int.zero) == 0, "Deterministic full-density generation skips start/back rows.");
        var obstacle = ScriptableObject.CreateInstance<Tile>();
        var occupied = new Vector2Int(-3, 1);
        Require(!map.TrySetObstacle(occupied, obstacle), "Obstacle cannot overlap coin.");
        Require(!map.RegisterCellTrigger(new CellTriggerDefinition { id = "occupied", position = occupied }), "Trigger cannot overlap coin.");
        map.RemoveCoin(occupied);
        Require(map.TrySetObstacle(occupied, obstacle) && !map.TryPlaceCoin(occupied), "Coin cannot overlap obstacle.");
        var triggerPosition = new Vector2Int(0, 2);
        map.RemoveCoin(triggerPosition);
        var cell = new CellTriggerDefinition { id = "cell", position = triggerPosition };
        var row = new RowTriggerDefinition { id = "row", row = 2 };
        int cellEvents = 0, rowEvents = 0;
        cell.onReached.AddListener(() => cellEvents++);
        row.onReached.AddListener(() => rowEvents++);
        Require(map.RegisterCellTrigger(cell) && map.RegisterRowTrigger(row), "Register independent cell/row events.");
        Require(!map.TryPlaceCoin(triggerPosition), "Coin cannot overlap trigger.");
        Require(!map.TrySetObstacle(triggerPosition, obstacle), "Obstacle cannot replace trigger.");
        int wallet = save.Data.coins;
        Require(player.TryMove(Vector2Int.up), "Move onto coin.");
        Require(save.Data.coins == wallet + 1 && gameplay.Score == 110 && gameplay.CollectedCoins == 1 && map.GetCoinAmount(new Vector2Int(0, 1)) == 0,
            "Coin collection persists and awards ten bonus points plus new-row score.");
        Require(player.TryMove(Vector2Int.down) && player.TryMove(Vector2Int.up), "Revisit collected coin.");
        Require(save.Data.coins == wallet + 1 && gameplay.Score == 110 && save.Data.totalForwardCells == 1, "Revisits cannot farm coins, points or unlock progress.");
        Require(player.TryMove(Vector2Int.up), "Enter trigger row.");
        Require(player.TryMove(Vector2Int.right), "Move laterally in event row.");
        Require(player.TryMove(Vector2Int.left), "Revisit cell.");
        Require(player.TryMove(Vector2Int.down) && player.TryMove(Vector2Int.up), "Revisit event row.");
        Require(rowEvents == 1 && cellEvents == 1, "Cell and row events default to once per run.");
        var failureCell = new Vector2Int(0, 3);
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
        UnityEngine.Object.DestroyImmediate(obstacle);
    }

    private static void CheckMenu()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenuScene.unity");
        var save = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
        Singleton(typeof(SaveManager), save);
        save.Load();
        var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuBootstrap>();
        using var view = new MainMenuCollectionView(menu, (GameObject[])Field(menu, "_sections"), (Button[])Field(menu, "_navigation"), ((Text)Field(menu, "_achievementSummary")).font);
        Set(menu, "_sections", view.Sections);
        Set(menu, "_navigation", view.Navigation);
        Require(view.Sections.Length == 5 && view.Navigation.Length == 5, "Existing four navigation slots expand with character tab.");
        menu.SelectSection(1);
        var panel = view.Sections[1].transform;
        Require(panel.Find("robot").GetComponent<Button>().interactable && !panel.Find("cat").GetComponent<Button>().interactable, "Owned/locked character cards differ and locked cards are not selectable.");
        view.PreviewCharacter("cat");
        Require(save.Data.selectedCharacterId == "robot", "Locked preview does not change selection.");
        view.PreviewCharacter("slime");
        Require(save.Data.selectedCharacterId == "robot", "Unlocked preview does not commit immediately.");
        view.ConfirmCharacter();
        Require(save.Data.selectedCharacterId == "slime" && save.Load() && save.Data.selectedCharacterId == "slime", "Confirm commits next-game selection and survives reload.");
        view.Navigation[2].onClick.Invoke();
        Require(view.Sections[2].activeSelf && !view.Sections[1].activeSelf, "Rebound navigation opens correct shop after adding tab.");
        Require(view.Sections[2].transform.Find("Buy cat/Purchase").GetComponent<Button>().interactable == false, "Insufficient wallet disables shop purchase.");
        save.TryUpdate(data => data.coins = 200);
        var buyCat = view.Sections[2].transform.Find("Buy cat/Purchase").GetComponent<Button>();
        Require(buyCat.interactable, "Wallet refresh enables affordable purchases.");
        buyCat.onClick.Invoke();
        Require(save.Data.coins == 100 && CharacterCatalog.IsUnlocked(save.Data, "cat") && panel.Find("cat").GetComponent<Button>().interactable,
            "Shop purchase debits coins and refreshes unlocked card immediately.");
        view.Sections[2].transform.Find("slime_sun/Buy or equip").GetComponent<Button>().onClick.Invoke();
        Require(save.Data.unlockedSkinIds.Contains("slime_sun"), "Shop button purchases skin.");
        view.Sections[2].transform.Find("slime_sun/Buy or equip").GetComponent<Button>().onClick.Invoke();
        Require(save.Data.selectedSkinId == "slime_sun", "Owned skin button equips it.");
        Canvas.ForceUpdateCanvases();
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            CaptureMenu(menu, view);
    }

    private static void CaptureMenu(MainMenuBootstrap menu, MainMenuCollectionView view)
    {
        var canvas = view.Navigation[0].GetComponentInParent<Canvas>();
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
        for (int i = 0; i < 3; i++)
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
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
