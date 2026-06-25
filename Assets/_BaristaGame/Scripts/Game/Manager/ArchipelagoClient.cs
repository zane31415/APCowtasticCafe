using System;
using System.Collections.Generic;
using UnityEngine;

public class ArchipelagoClient : MonoBehaviour
{
    public static ArchipelagoClient Instance { get; private set; }

    public string SlotName  { get; private set; }
    public bool IsConnected     { get; private set; }
    public bool ConnectionFailed { get; private set; }

    // --- Debug counters (surfaced in RandomizerManager's overlay) ---
    public int    DbgRawMessages   { get; private set; }
    public int    DbgItemsReceived { get; private set; }
    public string DbgLastCmd       { get; private set; } = "(none)";
    public int    DbgCheckedFromServer { get; private set; }
    public int    PendingItemCount => _pendingItems.Count;

    private APNetworking _apNetworking;

    private RandomizerManager Randomizer =>
        _randomizerManager != null ? _randomizerManager
            : (_randomizerManager = RandomizerManager.Instance);
    private RandomizerManager _randomizerManager;

    private HashSet<long>  _checkedLocations = new HashSet<long>();
    private List<string>   _pendingItems     = new List<string>();  // received before RandomizerManager existed

    // ---------------------------------------------------------------------------
    // Item ID → name  (mirrors items.py ITEM_NAME_TO_ID)
    // ---------------------------------------------------------------------------
    private static readonly string[] IngredientOrder = {
        "Espresso", "Coffee", "Chocolate", "Tea", "Milk",
        "BreastMilk", "Cream", "Sugar", "Ice", "Boba", "Sprinkles"
    };

    private static readonly Dictionary<long, string> ItemIdToName;
    static ArchipelagoClient()
    {
        ItemIdToName = new Dictionary<long, string>();
        long id = 771771000L;
        foreach (var ing in IngredientOrder)
            ItemIdToName[id++] = $"Ingredient: {ing}";
        ItemIdToName[id++] = "Stretchy Candy";
        ItemIdToName[id++] = "Fullness Tolerance";
        ItemIdToName[id++] = "Happiness Upgrade";
        ItemIdToName[id++] = "Max Flow Upgrade";
        ItemIdToName[id++] = "Min Flow Upgrade";
        ItemIdToName[id++] = "Decoration";
    }

    // ---------------------------------------------------------------------------
    // Location name → ID  (mirrors locations.py formula)
    // ---------------------------------------------------------------------------
    private const long LocationBase       = 771771100L;
    private const int  MaxChecksPerIngred = 10;
    private const long ShopLocationBase   = 771771300L;

    private static long LocationNameToId(string locationName)
    {
        // Shop locations: "Shop Slot #{n}"
        if (locationName.StartsWith("Shop Slot #"))
        {
            if (int.TryParse(locationName.Substring("Shop Slot #".Length), out int slot))
                return ShopLocationBase + (slot - 1);
            return -1;
        }

        // Serve locations: "Serve {Ingredient} #{n}"
        if (locationName.StartsWith("Serve "))
        {
            int hashIdx = locationName.LastIndexOf('#');
            if (hashIdx < 0) return -1;

            string ingredient  = locationName.Substring(6, hashIdx - 7).Trim();
            int    checkNumber = int.Parse(locationName.Substring(hashIdx + 1));
            int    ingIdx      = Array.IndexOf(IngredientOrder, ingredient);
            if (ingIdx < 0) return -1;

            return LocationBase + ingIdx * MaxChecksPerIngred + (checkNumber - 1);
        }

        return -1;
    }

    // ---------------------------------------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        _apNetworking = APNetworking.Instance;
    }

    private void Update()
    {
        // Process messages during the handshake too, not only after IsConnected.
        if (_apNetworking != null && _apNetworking.IsConnected())
            ProcessIncomingMessages();

        // Flush items that arrived before RandomizerManager was ready.
        if (_pendingItems.Count > 0 && Randomizer != null)
        {
            foreach (string item in _pendingItems)
                Randomizer.HandleItem(item);
            _pendingItems.Clear();
        }
    }

    // ---------------------------------------------------------------------------
    // Connection
    // ---------------------------------------------------------------------------

    public async void Connect(string serverUrl, string slotName, string password = "")
    {
        if (IsConnected)
        {
            Debug.LogWarning("[AP] Already connected");
            return;
        }

        SlotName        = slotName;
        ConnectionFailed = false;

        if (_apNetworking == null)
        {
            Debug.LogError("[AP] APNetworking not found");
            return;
        }

        Debug.Log($"[AP] Connecting to {serverUrl} as '{slotName}'");

        bool ok = await _apNetworking.ConnectAsync(serverUrl);
        if (!ok)
        {
            Debug.LogError("[AP] WebSocket connection failed");
            return;
        }

        // Send AP Connect packet (wrapped in array as required by the protocol).
        // items_handling 7 = 0b111: receive remote + starting + goal items.
        string pw = (password ?? "").Replace("\"", "\\\"");
        string sn = slotName.Replace("\"", "\\\"");
        string uuid   = Guid.NewGuid().ToString();
        string packet =
            $"[{{\"cmd\":\"Connect\",\"game\":\"Cowtastic Cafe\"," +
            $"\"name\":\"{sn}\",\"password\":\"{pw}\"," +
            $"\"uuid\":\"{uuid}\"," +
            $"\"version\":{{\"major\":0,\"minor\":5,\"build\":0,\"class\":\"Version\"}}," +
            $"\"items_handling\":7,\"tags\":[]}}]";

        await _apNetworking.SendMessageAsync(packet);
        // IsConnected is set when the server replies with {"cmd":"Connected"}.
    }

    public void Disconnect()
    {
        IsConnected = false;
        _apNetworking?.Disconnect();
        Debug.Log("[AP] Disconnected");
    }

    // ---------------------------------------------------------------------------
    // Sending
    // ---------------------------------------------------------------------------

    public void CheckLocationByName(string locationName)
    {
        long id = LocationNameToId(locationName);
        if (id < 0)
        {
            Debug.LogWarning($"[AP] Unknown location name: '{locationName}'");
            return;
        }
        CheckLocation(id);
    }

    private async void CheckLocation(long locationId)
    {
        if (!IsConnected)
        {
            Debug.LogWarning("[AP] CheckLocation called while not connected");
            return;
        }

        if (!_checkedLocations.Add(locationId)) return; // already sent

        Debug.Log($"[AP] LocationChecks → {locationId}");
        string packet = $"[{{\"cmd\":\"LocationChecks\",\"locations\":[{locationId}]}}]";
        await _apNetworking.SendMessageAsync(packet);
    }

    // ---------------------------------------------------------------------------
    // Receiving
    // ---------------------------------------------------------------------------

    private void ProcessIncomingMessages()
    {
        while (_apNetworking.HasMessages())
        {
            string msg = _apNetworking.DequeueMessage();
            DbgRawMessages++;
            DbgLastCmd = ExtractFirstCmd(msg);
            Debug.Log($"[AP] RAW ({msg.Length} chars): {Truncate(msg, 300)}");
            try   { DispatchMessage(msg); }
            catch (Exception e) { Debug.LogError($"[AP] Message error: {e.Message}\n{msg}"); }
        }
    }

    private void DispatchMessage(string msg)
    {
        // A single WebSocket frame is a JSON array that may contain MULTIPLE command
        // objects (e.g. Connected + the initial ReceivedItems are commonly batched).
        // So we must check each command independently with `if`, NOT `else if`,
        // or batched commands get silently dropped.
        if (msg.Contains("\"cmd\":\"Connected\""))
            HandleConnected(msg);
        if (msg.Contains("\"cmd\":\"ConnectionRefused\""))
            HandleConnectionRefused(msg);
        if (msg.Contains("\"cmd\":\"ReceivedItems\""))
            HandleReceivedItems(msg);
        if (msg.Contains("\"cmd\":\"RoomInfo\""))
            Debug.Log("[AP] RoomInfo received");
        if (msg.Contains("\"cmd\":\"PrintJSON\""))
            Debug.Log($"[AP] Server: {Truncate(msg, 300)}");
    }

    private static string ExtractFirstCmd(string msg)
    {
        const string key = "\"cmd\":\"";
        int idx = msg.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return "?";
        int start = idx + key.Length;
        int end = msg.IndexOf('"', start);
        return end < 0 ? "?" : msg.Substring(start, end - start);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";

    private void HandleConnected(string msg)
    {
        IsConnected = true;
        Debug.Log("[AP] Server confirmed connection");

        int drinksPerCheck      = ExtractInt(msg, "drinks_per_check",      3);
        int checksPerIngredient = ExtractInt(msg, "checks_per_ingredient",  3);
        int shopLocations       = ExtractInt(msg, "shop_locations",         20);
        Randomizer?.ApplySlotData(drinksPerCheck, checksPerIngredient, shopLocations);

        // Seed our local sent-set with locations the server already has,
        // so we don't re-send them this session.
        foreach (long id in ExtractLongArray(msg, "checked_locations"))
            _checkedLocations.Add(id);
        DbgCheckedFromServer = _checkedLocations.Count;
    }

    private void HandleConnectionRefused(string msg)
    {
        Debug.LogError($"[AP] Connection refused: {msg}");
        IsConnected      = false;
        ConnectionFailed = true;
    }

    private void HandleReceivedItems(string msg)
    {
        // Packet: [{"cmd":"ReceivedItems","index":N,"items":[{"item":ID,"location":L,"player":P,"flags":F},...]}]
        // Extract every "item": number from the items array.
        int searchFrom = 0;
        const string itemKey = "\"item\":";
        while (true)
        {
            int keyIdx = msg.IndexOf(itemKey, searchFrom, StringComparison.Ordinal);
            if (keyIdx < 0) break;

            int valStart = keyIdx + itemKey.Length;
            while (valStart < msg.Length && msg[valStart] == ' ') valStart++;
            int valEnd = valStart;
            while (valEnd < msg.Length && (char.IsDigit(msg[valEnd]) || msg[valEnd] == '-')) valEnd++;

            if (long.TryParse(msg.Substring(valStart, valEnd - valStart), out long itemId))
            {
                string itemName = ItemIdToName.TryGetValue(itemId, out string n) ? n : $"Unknown({itemId})";
                DbgItemsReceived++;
                Debug.Log($"[AP] Received item: {itemName} ({itemId})");
                if (Randomizer != null)
                    Randomizer.HandleItem(itemName);
                else
                    _pendingItems.Add(itemName);
            }

            searchFrom = valEnd;
        }
    }

    /// <summary>Extracts integers from a JSON array field like "checked_locations": [1, 2, 3].</summary>
    private static List<long> ExtractLongArray(string json, string key)
    {
        var result = new List<long>();
        string search = $"\"{key}\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return result;
        int open = json.IndexOf('[', idx);
        int close = json.IndexOf(']', open + 1);
        if (open < 0 || close < 0) return result;

        int i = open + 1;
        while (i < close)
        {
            while (i < close && !(char.IsDigit(json[i]) || json[i] == '-')) i++;
            int start = i;
            while (i < close && (char.IsDigit(json[i]) || json[i] == '-')) i++;
            if (i > start && long.TryParse(json.Substring(start, i - start), out long v))
                result.Add(v);
        }
        return result;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static int ExtractInt(string json, string key, int fallback)
    {
        string search = $"\"{key}\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return fallback;
        int start = idx + search.Length;
        while (start < json.Length && json[start] == ' ') start++;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
        return int.TryParse(json.Substring(start, end - start), out int v) ? v : fallback;
    }

    private void OnDestroy()
    {
        if (IsConnected) Disconnect();
    }
}
