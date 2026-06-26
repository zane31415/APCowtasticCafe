using System.Collections.Generic;
using UnityEngine;

public class RandomizerManager : MonoBehaviour
{
    public static RandomizerManager Instance { get; private set; }

    [Header("Tracking")]
    public Dictionary<string, int> IngredientServeCounts = new Dictionary<string, int>();
    public List<string> CheckedLocations = new List<string>();

    private BaseGameMode _gameMode;

    private ArchipelagoClient _archipelagoClient
    {
        get { return ArchipelagoClient.Instance; }
    }

    private List<string> _dialogueLogs  = new List<string>();
    private List<string> _pendingItems  = new List<string>();  // items received before game scene loaded

    private bool _gameReady = false;

    // Configurable Ingredient Milestones
    public int DrinksPerCheck = 3;
    public int NumberOfChecks = 3;

    // Shop Queue
    public List<string> ShopLocationQueue = new List<string>();
    public int MaxShopLocations = 10;

    // Per-location info from slot data: location name -> (item display, isLocal).
    private Dictionary<string, LocInfo> _locInfo = new Dictionary<string, LocInfo>();
    private struct LocInfo { public string Item; public bool Local; }

    // Barista announcement queue, paced so messages don't stomp each other.
    private Queue<string> _announceQueue = new Queue<string>();
    private float _nextAnnounceTime = 0f;
    private const float AnnounceInterval = 4f;

    // Shop pricing is per-slot, not per-purchase: "Shop Slot #N" costs
    // BasePrice + Step*(N-1), capped at PriceCap. The apworld mirrors this to
    // gate later slots behind ingredient count ($50 of price = 1 ingredient).
    public float ShopBasePrice = 50f;
    public float ShopPriceStep = 25f;
    public float ShopPriceCap  = 550f;

    // Goal is the in-game "barista overflows" good end (see GameWinManager),
    // not a candy count. RequiredCandy is just informational for the overlay.
    public const int RequiredCandy = 19;
    public int MilkCapacityCount = 0;

    // Cosmetic AP item name -> PermanentUnlock UnlockId, built from slot data.
    private Dictionary<string, string> _cosmeticToUnlockId = new Dictionary<string, string>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeShopQueue();
            // Ingredients are AP-gated; the player can't buy them with money.
            FillingTool.PurchasingEnabled = false;
        }
        else
        {
            Destroy(gameObject);
        }
    }


    private void Update()
    {
        if (!_gameReady)
            TryActivateGame();

        // Feed queued AP announcements to the barista, paced out.
        if (_announceQueue.Count > 0 && Time.unscaledTime >= _nextAnnounceTime
            && BaristaTalkManager.instance != null)
        {
            BaristaTalkManager.instance.AnnounceAP(_announceQueue.Dequeue());
            _nextAnnounceTime = Time.unscaledTime + AnnounceInterval;
        }
    }

    private void Announce(string message)
    {
        _announceQueue.Enqueue(message);
    }

    private static string PrettyItemName(string itemId)
    {
        return itemId.StartsWith("Ingredient: ")
            ? itemId.Substring("Ingredient: ".Length)
            : itemId;
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
        DisableCosmeticShop();

        foreach (string item in _pendingItems)
            ApplyItem(item);
        _pendingItems.Clear();
    }

    /// <summary>
    /// Cosmetics now come from the multiworld, so make them un-purchasable
    /// in the customize shop (equivalent to infinite cost), and build the
    /// AP-item-name -> UnlockId map used when cosmetic items are received.
    /// </summary>
    private void DisableCosmeticShop()
    {
        _cosmeticToUnlockId.Clear();
        foreach (var c in ArchipelagoClient.Cosmetics)
            _cosmeticToUnlockId[c.Name] = c.UnlockId;

        // For the rando, cosmetics start LOCKED (override any saved PlayerPrefs)
        // and can't be bought — they only come from the multiworld.
        foreach (var unlock in FindObjectsOfType<PermanentUnlock>(true))
        {
            unlock.CanBePurchased = false;
            unlock.UnlockCosts = float.MaxValue;
            unlock.Unlock(false);
        }

        // Reset milk to default so a previously-saved cosmetic milk doesn't carry
        // over; received milk cosmetics will re-apply it.
        if (MilkTypeController.instance != null)
            MilkTypeController.instance.SetMilkByPresetAndSavePreference(0);
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
            // ConfigureAsShopSlot also disables the title's LocalizeStringEvent
            // and captures the label TMP so we can populate it ourselves.
            shopButtons[i].ConfigureAsShopSlot(i);
        }
        AddRecentEvent($"Converted {shopButtons.Count} shop buttons");
    }

    public void HandleLocation(string locationName)
    {
        if (CheckedLocations.Contains(locationName)) return;

        CheckedLocations.Add(locationName);
        Debug.Log($"[Location] {locationName}");

        _archipelagoClient?.CheckLocationByName(locationName);

        // If this check's item belongs to another player, announce it as "sent".
        // (Local items announce as "unlocked" when we actually receive them.)
        if (_locInfo.TryGetValue(locationName, out LocInfo info) && !info.Local)
            Announce($"{info.Item} sent!");
    }

    public void HandleItem(string itemId)
    {
        Debug.Log($"[Item Received] {itemId}");

        // Tell the player what they just got. (Victory is never transmitted.)
        if (itemId != "Victory")
            Announce($"{PrettyItemName(itemId)} unlocked!");

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
            MilkCapacityCount++;
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
        else if (_cosmeticToUnlockId.ContainsKey(itemId))
        {
            UnlockCosmetic(itemId);
        }
        else if (itemId == "Decoration" || itemId == "Victory")
        {
            // Nothing to do for plain filler or the (never-transmitted) goal item.
        }
    }

    /// <summary>Unlocks the cosmetic whose AP item name was received.</summary>
    private void UnlockCosmetic(string itemId)
    {
        if (!_cosmeticToUnlockId.TryGetValue(itemId, out string unlockId)) return;

        foreach (var unlock in FindObjectsOfType<PermanentUnlock>(true))
        {
            if (unlock.UnlockId == unlockId)
            {
                unlock.Unlock(true);
                ApplyCosmetic(unlockId);  // equip it immediately for the surprise
                AddRecentEvent($"Unlocked + applied cosmetic: {itemId}");
                return;
            }
        }
        AddRecentEvent($"Cosmetic '{itemId}' -> id '{unlockId}' not found in scene");
    }

    // Milk-color cosmetics map to MilkTypeController presets (see SetMilkByPreset).
    private static readonly Dictionary<string, int> _milkPreset = new Dictionary<string, int>
    {
        { "Thick", 1 }, { "Creamy", 2 }, { "Chocolate", 3 }, { "Strawberry", 4 },
        { "Honey", 5 }, { "Blue", 6 }, { "Green", 7 }, { "Rspberry", 8 },
        { "Rainbow", 9 }, { "Space", 10 }, { "Void", 11 },
    };

    /// <summary>Immediately equips a cosmetic so the player sees it right away.</summary>
    private void ApplyCosmetic(string unlockId)
    {
        if (_milkPreset.TryGetValue(unlockId, out int preset))
        {
            MilkTypeController.instance?.SetMilkByPresetAndSavePreference(preset);
            return;
        }

        var barista = BaristaController.instance;
        if (barista == null) return;
        switch (unlockId)
        {
            case "Bikini":          barista.SetBaristaTop(2);     break; // bikini top
            case "TopOnly":         barista.SetBaristaTop(1);     break; // apron, bottom removed
            case "NoneApron":       barista.SetBaristaTop(-1);    break; // no top
            case "PoofyPantsPants": barista.SetUnderwearType(2);  break; // poofy pants
            case "UnderWearPants":  barista.SetUnderwearType(1);  break; // underwear
            case "NonePants":       barista.SetUnderwearType(-1); break; // no pants
        }
    }

    /// <summary>Called by GameWinManager when the barista-overflow good end fires.</summary>
    public void OnGameWon()
    {
        AddRecentEvent("GOAL: barista overflow good end reached!");
        _archipelagoClient?.SendGoalComplete();
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

    // Kept for external callers (ButtonUpgrade/UpgradeManager). The on-screen
    // debug overlay is gone; this just logs to the console now.
    public void AddRecentEvent(string msg)
    {
        Debug.Log($"[AP] {msg}");
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
    public void ApplySlotData(int drinksPerCheck, int checksPerIngredient, int shopLocations,
                              List<string> locNames, List<string> locItems, List<long> locLocal)
    {
        DrinksPerCheck   = drinksPerCheck;
        NumberOfChecks   = checksPerIngredient;
        MaxShopLocations = shopLocations;

        _locInfo.Clear();
        if (locNames != null && locItems != null)
        {
            for (int i = 0; i < locNames.Count && i < locItems.Count; i++)
            {
                bool local = locLocal != null && i < locLocal.Count && locLocal[i] != 0;
                _locInfo[locNames[i]] = new LocInfo { Item = locItems[i], Local = local };
            }
        }

        InitializeShopQueue();
        Debug.Log($"[AP] Slot data applied: {drinksPerCheck} drinks/check, {checksPerIngredient} checks/ingredient, {shopLocations} shop locations, {_locInfo.Count} location items");
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

    /// <summary>
    /// What the shop button at the given visible slot index will hand out,
    /// looked up from the slot-data location->item map.
    /// </summary>
    public string GetShopItemDisplayForSlot(int shopSlotIndex)
    {
        if (shopSlotIndex < 0 || shopSlotIndex >= ShopLocationQueue.Count) return "";
        string loc = ShopLocationQueue[shopSlotIndex]; // "Shop Slot #m"
        if (_locInfo.TryGetValue(loc, out LocInfo info)) return info.Item;
        return "?";
    }

    /// <summary>Fixed price of a given "Shop Slot #N" (per-slot, not per-purchase).</summary>
    public float GetShopSlotPrice(int slotNumber) =>
        Mathf.Min(ShopBasePrice + ShopPriceStep * (slotNumber - 1), ShopPriceCap);

    /// <summary>Cost shown on the shop button at the given visible queue position.</summary>
    public float GetShopCostForSlot(int shopSlotIndex)
    {
        if (shopSlotIndex < 0 || shopSlotIndex >= ShopLocationQueue.Count) return 0f;
        string loc = ShopLocationQueue[shopSlotIndex]; // "Shop Slot #m"
        int hash = loc.LastIndexOf('#');
        if (hash >= 0 && int.TryParse(loc.Substring(hash + 1), out int m))
            return GetShopSlotPrice(m);
        return 0f;
    }

    public void BuyShopLocation(string locationId)
    {
        if (ShopLocationQueue.Contains(locationId))
        {
            ShopLocationQueue.Remove(locationId);
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

}
