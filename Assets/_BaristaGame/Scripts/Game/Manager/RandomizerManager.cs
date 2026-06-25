using System.Collections.Generic;
using UnityEngine;

public class RandomizerManager : MonoBehaviour
{
    public static RandomizerManager Instance { get; private set; }

    [Header("Tracking")]
    public Dictionary<string, int> IngredientServeCounts = new Dictionary<string, int>();
    public List<string> CheckedLocations = new List<string>();

    private BaseGameMode _gameMode;
    private ArchipelagoClient _archipelagoClient;
    private List<string> _recentEvents  = new List<string>();
    private List<string> _dialogueLogs  = new List<string>();
    private List<string> _pendingItems  = new List<string>();  // items received before game scene loaded

    private bool _gameReady = false;

    private bool _cheatsEnabled = false;

    // Configurable Ingredient Milestones
    public int DrinksPerCheck = 3;
    public int NumberOfChecks = 3;

    // Shop Queue
    public List<string> ShopLocationQueue = new List<string>();
    public int MaxShopLocations = 20;

    // Shop pricing (escalating). Each purchase costs Base * Multiplier^(purchases so far).
    public float ShopBaseCost = 300f;
    public float ShopCostMultiplier = 1.5f;
    private int _shopPurchaseCount = 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeShopQueue();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        _archipelagoClient = ArchipelagoClient.Instance;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
            AddCheatMoney(1000);

        if (!_gameReady)
            TryActivateGame();
    }

    /// <summary>
    /// Called every frame until BaseGameMode is available, then flushes any
    /// items that arrived before the game scene finished loading.
    /// </summary>
    private void TryActivateGame()
    {
        if (BaseGameMode.instance == null) return;

        _gameMode  = BaseGameMode.instance;
        _gameReady = true;

        AutoBuySecretCandy();
        ConvertShopButtons();

        foreach (string item in _pendingItems)
            ApplyItem(item);
        _pendingItems.Clear();
    }

    /// <summary>The "secret candy" (initial upgrade) now starts purchased for free.</summary>
    private void AutoBuySecretCandy()
    {
        if (UpgradeManager.Instance != null && !UpgradeManager.Instance.HasInitialUpgrade)
        {
            UpgradeManager.Instance.BuyInitialUpgarde();
            AddRecentEvent("Auto-bought Secret Candy");
        }
    }

    /// <summary>
    /// Repurposes the six barista-control upgrade buttons into AP shop-location
    /// buttons (slot indices 0-5 into the shop queue). Done at runtime so no
    /// scene/prefab editing is needed.
    /// </summary>
    private void ConvertShopButtons()
    {
        var buttons = FindObjectsOfType<ButtonUpgrade>(true);

        // Skip the InitialUpgrade ("secret candy") button; convert the rest.
        // Order by name so slot assignment is deterministic across runs.
        var shopButtons = new List<ButtonUpgrade>();
        foreach (var b in buttons)
            if (b.TypeOfUpgrade != ButtonUpgrade.UpgradeType.InitialUpgarde)
                shopButtons.Add(b);
        shopButtons.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        for (int i = 0; i < shopButtons.Count; i++)
        {
            shopButtons[i].TypeOfUpgrade = ButtonUpgrade.UpgradeType.ShopLocation;
            shopButtons[i].ShopSlotIndex = i;
            shopButtons[i].MaxUpgrades   = 0; // unlimited; shop branch governs availability
        }
        AddRecentEvent($"Converted {shopButtons.Count} shop buttons");
    }

    public void AddCheatMoney(float amount)
    {
        if (_gameMode == null) _gameMode = BaseGameMode.instance;
        if (_gameMode != null)
        {
            _gameMode.AddMoney(amount);
            Log($"Cheat: Added {amount} money");
        }
    }

    private void ToggleCheatScripts(bool state)
    {
        _cheatsEnabled = state;
        var mouseCheat = FindObjectOfType<CheatStatsMouse>(true);
        var cupCheat = FindObjectOfType<CheatStatsCup>(true);

        if (mouseCheat != null) mouseCheat.gameObject.SetActive(state);
        if (cupCheat != null) cupCheat.gameObject.SetActive(state);
    }

    public void HandleLocation(string locationName)
    {
        if (CheckedLocations.Contains(locationName)) return;

        CheckedLocations.Add(locationName);
        string logMsg = $"[Location] {locationName}";
        Debug.Log(logMsg);
        AddRecentEvent(logMsg);

        _archipelagoClient?.CheckLocationByName(locationName);
    }

    public void HandleItem(string itemId)
    {
        Debug.Log($"[Item Received] {itemId}");
        AddRecentEvent($"[Item] {itemId}");

        if (!_gameReady)
        {
            _pendingItems.Add(itemId);
            return;
        }

        ApplyItem(itemId);
    }

    private void ApplyItem(string itemId)
    {
        if (itemId.StartsWith("Ingredient: "))
        {
            string ingredientName = itemId.Substring("Ingredient: ".Length);
            UnlockIngredient(ingredientName);
        }
        else if (itemId == "Stretchy Candy")
        {
            _gameMode.BuyMaxSize(1);
        }
        else if (itemId == "Fullness Tolerance")
        {
            _gameMode.BuyTolerance(_gameMode.UpgradesFullTolerance + 1, _gameMode.BustHappinessDecreaseTolerance);
        }
        else if (itemId == "Happiness Upgrade")
        {
            _gameMode.BuyHappyness(1);
        }
        else if (itemId == "Max Flow Upgrade")
        {
            _gameMode.MaxProductionRate += 5;
        }
        else if (itemId == "Min Flow Upgrade")
        {
            _gameMode.MinProductionRate -= 5;
            if (_gameMode.MinProductionRate < 0) _gameMode.MinProductionRate = 0;
        }
        else if (itemId == "Decoration" || itemId == "Victory")
        {
            // Nothing to do for cosmetic fillers or the goal item.
        }
    }

    public void Log(string message)
    {
        _dialogueLogs.Add(message);
    }

    public string GetAndClearLogs()
    {
        if (_dialogueLogs.Count == 0) return "";
        string joined = string.Join(". ", _dialogueLogs);
        _dialogueLogs.Clear();
        return joined;
    }

    public void AddRecentEvent(string msg)
    {
        _recentEvents.Insert(0, msg);
        if (_recentEvents.Count > 10) _recentEvents.RemoveAt(10);
    }

    private void UnlockIngredient(string name)
    {
        if (FillingTool.fillingTools == null) return;

        foreach (var tool in FillingTool.fillingTools.Values)
        {
            if (tool == null) continue;

            if (tool.isTopping)
            {
                if (tool.CupToppings.ToString().Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    tool.SetUnlockedState(true);
                    return;
                }
            }
            else
            {
                if (tool.MashineFilling.ToString().Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    tool.SetUnlockedState(true);
                    return;
                }
            }
        }
    }

    public void TrackIngredientServe(string ingredientName)
    {
        if (string.IsNullOrEmpty(ingredientName)) return;

        if (!IngredientServeCounts.ContainsKey(ingredientName))
            IngredientServeCounts[ingredientName] = 0;

        IngredientServeCounts[ingredientName]++;
        int count = IngredientServeCounts[ingredientName];

        if (count % DrinksPerCheck == 0)
        {
            int checkNumber = count / DrinksPerCheck;
            if (checkNumber <= NumberOfChecks)
            {
                // Location name must match the apworld's location_name() format.
                HandleLocation($"Serve {ingredientName} #{checkNumber}");
            }
        }
    }

    /// <summary>
    /// Called by ArchipelagoClient when slot data arrives after a successful connection.
    /// </summary>
    public void ApplySlotData(int drinksPerCheck, int checksPerIngredient, int shopLocations)
    {
        DrinksPerCheck   = drinksPerCheck;
        NumberOfChecks   = checksPerIngredient;
        MaxShopLocations = shopLocations;
        InitializeShopQueue();
        Debug.Log($"[AP] Slot data applied: {drinksPerCheck} drinks/check, {checksPerIngredient} checks/ingredient, {shopLocations} shop locations");
    }

    private void InitializeShopQueue()
    {
        ShopLocationQueue.Clear();
        for (int i = 1; i <= MaxShopLocations; i++)
        {
            // Name must match the apworld's shop_location_name() format.
            ShopLocationQueue.Add($"Shop Slot #{i}");
        }
    }

    public List<string> GetNextShopLocations(int count)
    {
        List<string> nextLocs = new List<string>();
        for (int i = 0; i < count && i < ShopLocationQueue.Count; i++)
        {
            nextLocs.Add(ShopLocationQueue[i]);
        }
        return nextLocs;
    }

    public int GetShopQueueCount() => ShopLocationQueue.Count;

    /// <summary>Cost of the next shop purchase (escalates with each purchase made).</summary>
    public float GetCurrentShopCost() => ShopBaseCost * Mathf.Pow(ShopCostMultiplier, _shopPurchaseCount);

    public void BuyShopLocation(string locationId)
    {
        if (ShopLocationQueue.Contains(locationId))
        {
            ShopLocationQueue.Remove(locationId);
            _shopPurchaseCount++;
            HandleLocation(locationId);
        }
    }

    public bool CanIncreaseFlow()
    {
        if (_gameMode == null) return false;
        return _gameMode.ProductionRate < _gameMode.MaxProductionRate;
    }

    public bool CanDecreaseFlow()
    {
        if (_gameMode == null) return false;
        return _gameMode.ProductionRate > _gameMode.MinProductionRate;
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 16;
        style.normal.textColor = Color.yellow;

        GUIStyle status = new GUIStyle();
        status.fontSize = 15;
        status.normal.textColor = Color.cyan;

        GUILayout.BeginArea(new Rect(10, 10, 440, 600));
        GUI.Box(new Rect(0, 0, 440, 600), "RANDOMIZER DEBUG");
        GUILayout.Space(30);

        if (GUILayout.Button("Cheat: +1000 Money"))
        {
            AddCheatMoney(1000);
        }

        bool newCheats = GUILayout.Toggle(_cheatsEnabled, "Enable Overlay Cheats");
        if (newCheats != _cheatsEnabled)
        {
            ToggleCheatScripts(newCheats);
        }

        // --- Live connection / sync status ---
        GUILayout.Space(10);
        var ap = _archipelagoClient != null ? _archipelagoClient : ArchipelagoClient.Instance;
        if (ap != null)
        {
            GUILayout.Label($"AP Connected: {ap.IsConnected}", status);
            GUILayout.Label($"Raw msgs: {ap.DbgRawMessages}  | Last cmd: {ap.DbgLastCmd}", status);
            GUILayout.Label($"Items recv: {ap.DbgItemsReceived}  | AP pending: {ap.PendingItemCount}", status);
            GUILayout.Label($"Server checked: {ap.DbgCheckedFromServer}", status);
        }
        else
        {
            GUILayout.Label("AP client: NULL", status);
        }

        GUILayout.Label($"GameReady: {_gameReady}  | GameMode: {(_gameMode != null)}", status);
        GUILayout.Label($"RM pending items: {_pendingItems.Count}", status);
        GUILayout.Label($"Locations sent: {CheckedLocations.Count}  | DPC:{DrinksPerCheck} NoC:{NumberOfChecks}", status);

        GUILayout.Space(10);
        foreach (var ev in _recentEvents)
        {
            GUILayout.Label(ev, style);
        }
        GUILayout.EndArea();
    }
}
