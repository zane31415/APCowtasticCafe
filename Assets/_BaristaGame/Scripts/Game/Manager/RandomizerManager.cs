using System.Collections.Generic;
using UnityEngine;

public class RandomizerManager : MonoBehaviour
{
    public static RandomizerManager Instance { get; private set; }

    [Header("Tracking")]
    public Dictionary<string, int> IngredientServeCounts = new Dictionary<string, int>();
    public List<string> CheckedLocations = new List<string>();

    private BaseGameMode _gameMode;
    private List<string> _recentEvents = new List<string>();
    private List<string> _itemPool = new List<string>();
    private List<string> _dialogueLogs = new List<string>();

    private bool _cheatsEnabled = false;

    // Configurable Ingredient Milestones
    public int DrinksPerCheck = 3;
    public int NumberOfChecks = 3;

    // Shop Queue
    public List<string> ShopLocationQueue = new List<string>();
    public int MaxShopLocations = 20;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeItemPool();
            InitializeShopQueue();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        _gameMode = BaseGameMode.instance;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            AddCheatMoney(1000);
        }
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

    private void InitializeItemPool()
    {
        // 11 Ingredients (Fillings + Toppings)
        string[] ingredients = { "Espresso", "Coffee", "Chocolate", "Tea", "Milk", "BreastMilk", "Cream", "Sugar", "Ice", "Boba", "Sprinkles" };
        foreach (var ing in ingredients) _itemPool.Add("Ingredient_" + ing);

        // 20 Stretchy Candy
        for (int i = 0; i < 20; i++) _itemPool.Add("StretchyCandy");

        // 5 Fullness Tolerance
        for (int i = 0; i < 5; i++) _itemPool.Add("FullnessTolerance");

        // 5 Happiness Upgrade
        for (int i = 0; i < 5; i++) _itemPool.Add("HappinessUpgrade");

        // 5 Max Flow Upgrade
        for (int i = 0; i < 5; i++) _itemPool.Add("MaxFlowUpgrade");
        
        // 5 Min Flow Upgrade
        for (int i = 0; i < 5; i++) _itemPool.Add("MinFlowUpgrade");

        // Fillers
        for (int i = 0; i < 10; i++) _itemPool.Add("Filler");
    }

    public void HandleLocation(string locationId)
    {
        if (CheckedLocations.Contains(locationId)) return;

        CheckedLocations.Add(locationId);
        string logMsg = $"[Location] {locationId}";
        Debug.Log(logMsg);
        AddRecentEvent(logMsg);

        // Mock AP: Trigger an item grant when a location is checked
        if (_itemPool.Count > 0)
        {
            int index = Random.Range(0, _itemPool.Count);
            string itemId = _itemPool[index];
            _itemPool.RemoveAt(index);
            Log($"Received {itemId} from {locationId}");
            HandleItem(itemId);
        }
    }

    public void HandleItem(string itemId)
    {
        string logMsg = $"[Item Received] {itemId}";
        Debug.Log(logMsg);
        AddRecentEvent(logMsg);

        if (itemId.StartsWith("Ingredient_"))
        {
            string ingredientName = itemId.Replace("Ingredient_", "");
            UnlockIngredient(ingredientName);
        }
        else if (itemId == "StretchyCandy")
        {
            // Consistent with ButtonUpgrade default UpgradeTimes
            _gameMode.BuyMaxSize(1);
        }
        else if (itemId == "FullnessTolerance")
        {
            _gameMode.BuyTolerance(_gameMode.UpgradesFullTolerance + 1, _gameMode.BustHappinessDecreaseTolerance);
        }
        else if (itemId == "HappinessUpgrade")
        {
            _gameMode.BuyHappyness(1);
        }
        else if (itemId == "MaxFlowUpgrade")
        {
            _gameMode.MaxProductionRate += 5;
        }
        else if (itemId == "MinFlowUpgrade")
        {
            _gameMode.MinProductionRate -= 5;
            if (_gameMode.MinProductionRate < 0) _gameMode.MinProductionRate = 0;
        }
        else if (itemId == "Filler")
        {
            // Do nothing
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
        {
            IngredientServeCounts[ingredientName] = 0;
        }

        IngredientServeCounts[ingredientName]++;
        int count = IngredientServeCounts[ingredientName];

        if (count % DrinksPerCheck == 0 && (count / DrinksPerCheck) <= NumberOfChecks)
        {
            HandleLocation($"loc_serve_{count}_{ingredientName.ToLower()}");
        }
    }

    private void InitializeShopQueue()
    {
        ShopLocationQueue.Clear();
        for (int i = 1; i <= MaxShopLocations; i++)
        {
            ShopLocationQueue.Add($"loc_shop_{i}");
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

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.yellow;

        GUILayout.BeginArea(new Rect(10, 10, 400, 500));
        GUI.Box(new Rect(0, 0, 400, 500), "RANDOMIZER DEBUG");
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

        GUILayout.Space(20);
        foreach (var ev in _recentEvents)
        {
            GUILayout.Label(ev, style);
        }
        GUILayout.EndArea();
    }
}
