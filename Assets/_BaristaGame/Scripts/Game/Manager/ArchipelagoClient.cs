using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class ArchipelagoClient : MonoBehaviour
{
    public static ArchipelagoClient Instance { get; private set; }

    public string SlotName  { get; private set; }
    public bool IsConnected     { get; private set; }
    public bool ConnectionFailed { get; private set; }
    public bool GoalSent         { get; private set; }

    // --- Debug counters (surfaced in RandomizerManager's overlay) ---
    public int    DbgRawMessages   { get; private set; }
    public int    DbgItemsReceived { get; private set; }
    public string DbgLastCmd       { get; private set; } = "(none)";
    public int    DbgCheckedFromServer { get; private set; }
    public int    TotalItemsReceived => _allItems.Count;

    private APNetworking _apNetworking;

    private RandomizerManager Randomizer =>
        _randomizerManager != null ? _randomizerManager
            : (_randomizerManager = RandomizerManager.Instance);
    private RandomizerManager _randomizerManager;

    private HashSet<long>  _checkedLocations = new HashSet<long>();
    private List<string>   _allItems         = new List<string>();  // all items ever received, in order (for scene-reload replay)

    // Slot data is cached here because Connected usually arrives on the menu,
    // before RandomizerManager (a game-scene singleton) exists. We flush it the
    // moment the manager appears, so settings aren't silently lost.
    private bool         _slotDataPending;
    private int          _sdDrinks, _sdChecks, _sdShop, _sdBasePrice = 50, _sdPriceStep = 25;
    private int          _sdMinDrinkQuality = 2, _sdDeathLink = 0, _sdDeathLinkSendQuality = 3, _sdDeathLinkPenalty = 20;
    private List<string> _sdLocNames = new List<string>();
    private List<string> _sdLocItems = new List<string>();
    private List<long>   _sdLocLocal = new List<long>();

    // How many received items we've already applied, so a reconnect resync
    // doesn't re-apply (and double-count) items or re-announce them.
    private int _receivedCount = 0;

    // ---------------------------------------------------------------------------
    // Ingredients (mirrors items.py — these drive item & location IDs and names)
    // ---------------------------------------------------------------------------
    // UNLOCKABLE ingredients, in item-ID order. MUST match items.py INGREDIENTS.
    // Tokens are the game's Fillings/Toppings enum names (some misspelled).
    private static readonly string[] IngredientItemOrder = {
        "Espresso", "Coffee", "Chocolate", "Tea", "Milk", "Cream", "Sugar",
        "Ice", "Boba", "Sprinkles", "WhipedCream", "CaramelSauce", "ChocolateSauce"
    };

    // ALL ingredients with serve locations, in location-ID order. MUST match
    // items.py SERVE_INGREDIENTS. Breast Milk is appended (no unlock item).
    public static readonly string[] ServeIngredientOrder = {
        "Espresso", "Coffee", "Chocolate", "Tea", "Milk", "Cream", "Sugar",
        "Ice", "Boba", "Sprinkles", "WhipedCream", "CaramelSauce", "ChocolateSauce",
        "BreastMilk"
    };

    // Game enum token -> Archipelago display name. MUST match items.py
    // INGREDIENT_DISPLAY. Tokens not listed display as-is.
    private static readonly Dictionary<string, string> IngredientDisplay =
        new Dictionary<string, string>
    {
        { "WhipedCream",    "Whipped Cream" },
        { "CaramelSauce",   "Caramel Sauce" },
        { "ChocolateSauce", "Cocoa Powder" },
        { "BreastMilk",     "Breast Milk" },
    };

    /// <summary>Enum token -> AP display name (e.g. "WhipedCream" -> "Whipped Cream").</summary>
    public static string DisplayIngredient(string token) =>
        IngredientDisplay.TryGetValue(token, out string d) ? d : token;

    /// <summary>AP display name -> enum token (inverse of DisplayIngredient).</summary>
    public static string TokenFromDisplay(string display)
    {
        foreach (var kv in IngredientDisplay)
            if (kv.Value == display) return kv.Key;
        return display;
    }

    // ---------------------------------------------------------------------------
    // Item ID → name  (mirrors items.py ITEM_NAME_TO_ID)
    // ---------------------------------------------------------------------------
    private static readonly Dictionary<long, string> ItemIdToName;
    static ArchipelagoClient()
    {
        ItemIdToName = new Dictionary<long, string>();
        long id = 771771000L;
        foreach (var ing in IngredientItemOrder)
            ItemIdToName[id++] = $"Ingredient: {DisplayIngredient(ing)}";
        ItemIdToName[id++] = "Stretchy Candy";
        ItemIdToName[id++] = "Fullness Tolerance";
        ItemIdToName[id++] = "Happiness Upgrade";
        ItemIdToName[id++] = "Milk Flow Increase";
        ItemIdToName[id++] = "Barista Smile :-)";

        // Cosmetics — MUST stay in the same order as items.py COSMETICS.
        foreach (var cos in CosmeticOrder)
            ItemIdToName[id++] = cos;
    }

    // Cosmetic display name -> in-game PermanentUnlock UnlockId.
    // Order of this array must match items.py COSMETICS exactly.
    public static readonly (string Name, string UnlockId)[] Cosmetics =
    {
        ("Barista Bikini",  "Bikini"),
        ("Top Only",        "TopOnly"),
        ("No Apron",        "NoneApron"),
        ("Poofy Pants",     "PoofyPantsPants"),
        ("Underwear",       "UnderWearPants"),
        ("No Pants",        "NonePants"),
        ("Blue Milk",       "Blue"),
        ("Chocolate Milk",  "Chocolate"),
        ("Creamy Milk",     "Creamy"),
        ("Green Milk",      "Green"),
        ("Honey Milk",      "Honey"),
        ("Rainbow Milk",    "Rainbow"),
        ("Raspberry Milk",  "Rspberry"),   // note: game data misspells this id
        ("Space Milk",      "Space"),
        ("Strawberry Milk", "Strawberry"),
        ("Thick Milk",      "Thick"),
        ("Void Milk",       "Void"),
    };

    private static readonly string[] CosmeticOrder = BuildCosmeticOrder();
    private static string[] BuildCosmeticOrder()
    {
        var arr = new string[Cosmetics.Length];
        for (int i = 0; i < Cosmetics.Length; i++) arr[i] = Cosmetics[i].Name;
        return arr;
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

        // Serve locations: "Serve {Display Ingredient} #{n}" (display name may
        // contain spaces, e.g. "Serve Whipped Cream #3").
        if (locationName.StartsWith("Serve "))
        {
            int hashIdx = locationName.LastIndexOf('#');
            if (hashIdx < 0) return -1;

            string ingredient  = locationName.Substring(6, hashIdx - 7).Trim();
            int    checkNumber = int.Parse(locationName.Substring(hashIdx + 1));
            int    ingIdx      = Array.IndexOf(ServeIngredientOrder, TokenFromDisplay(ingredient));
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

        // Flush slot data first so settings are applied before any queued items.
        if (_slotDataPending && Randomizer != null)
        {
            Randomizer.ApplySlotData(_sdDrinks, _sdChecks, _sdShop, _sdBasePrice, _sdPriceStep,
                                     _sdMinDrinkQuality, _sdDeathLink != 0, _sdDeathLinkSendQuality, _sdDeathLinkPenalty,
                                     _sdLocNames, _sdLocItems, _sdLocLocal);
            _slotDataPending = false;
        }

        // Items arriving before the game scene loads are held in _allItems and
        // replayed via ReplayTo() when the scene loads (see RandomizerManager.OnSceneLoaded).
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
        GoalSent         = false;

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
        if (msg.Contains("\"cmd\":\"Bounce\""))
            HandleBounce(msg);
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

        // Cache slot data; it's flushed to RandomizerManager in Update() once
        // that game-scene singleton exists (it usually doesn't yet, on the menu).
        _sdDrinks     = ExtractInt(msg, "drinks_per_check",      3);
        _sdChecks     = ExtractInt(msg, "checks_per_ingredient", 3);
        _sdShop       = ExtractInt(msg, "shop_locations",        10);
        _sdBasePrice            = ExtractInt(msg, "shop_base_price",          50);
        _sdPriceStep            = ExtractInt(msg, "shop_price_step",          25);
        _sdMinDrinkQuality      = ExtractInt(msg, "min_drink_quality",        2);
        _sdDeathLink            = ExtractInt(msg, "death_link",               0);
        _sdDeathLinkSendQuality = ExtractInt(msg, "death_link_send_quality",  3);
        _sdDeathLinkPenalty     = ExtractInt(msg, "death_link_penalty",       20);

        // If DeathLink is enabled, update our tags so the server routes bounces to us.
        if (_sdDeathLink != 0)
            SendConnectUpdate("[\"DeathLink\"]");
        _sdLocNames = ExtractStringArray(msg, "loc_names");
        _sdLocItems = ExtractStringArray(msg, "loc_items");
        _sdLocLocal = ExtractLongArray(msg, "loc_local");
        _slotDataPending = true;
        Debug.Log($"[AP] Slot data cached: drinks={_sdDrinks} checks={_sdChecks} shop={_sdShop} locs={_sdLocNames.Count}");

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
        // Packet: [{"cmd":"ReceivedItems","index":N,"items":[{"item":ID,...},...]}]
        // "index" is the position of the first item in this batch. On connect (or
        // reconnect) the server resends the full inventory with index 0; we must
        // only apply/announce items past _receivedCount so we don't double-count.
        int startIndex = ExtractInt(msg, "index", 0);
        // A packet with index 0 is the server's full-inventory resync on connect/reconnect.
        // Items in that batch are not "streaming" arrivals, so suppress the barista announcements.
        bool silent = (startIndex == 0);

        // Collect item IDs in order.
        var ids = new List<long>();
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
                ids.Add(itemId);
            searchFrom = valEnd;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            int globalIndex = startIndex + i;
            if (globalIndex < _receivedCount) continue;  // already applied
            _receivedCount = globalIndex + 1;

            string itemName = ItemIdToName.TryGetValue(ids[i], out string n) ? n : $"Unknown({ids[i]})";
            DbgItemsReceived++;
            Debug.Log($"[AP] Received item: {itemName} ({ids[i]})");
            _allItems.Add(itemName);
            if (Randomizer != null)
                Randomizer.HandleItem(itemName, silent);
            // else: item is in _allItems; RandomizerManager.OnSceneLoaded will replay it when game scene loads.
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

    /// <summary>
    /// Extracts a JSON string array like "shop_item_names": ["a", "b\"c", "d"].
    /// Hand-written because item names can come from other games and contain
    /// commas, quotes, and unicode escapes - the loose IndexOf approach we use
    /// elsewhere would mangle them.
    /// </summary>
    private static List<string> ExtractStringArray(string json, string key)
    {
        var result = new List<string>();
        string search = $"\"{key}\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return result;

        int i = json.IndexOf('[', idx);
        if (i < 0) return result;
        i++; // past '['

        while (i < json.Length)
        {
            // Skip whitespace and commas between elements.
            while (i < json.Length && (json[i] == ' ' || json[i] == ',' ||
                                       json[i] == '\n' || json[i] == '\r' || json[i] == '\t')) i++;
            if (i >= json.Length || json[i] == ']') break;
            if (json[i] != '"') break; // malformed / unexpected

            i++; // past opening quote
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char e = json[i + 1];
                    switch (e)
                    {
                        case '"':  sb.Append('"');  i += 2; break;
                        case '\\': sb.Append('\\'); i += 2; break;
                        case '/':  sb.Append('/');  i += 2; break;
                        case 'n':  sb.Append('\n'); i += 2; break;
                        case 't':  sb.Append('\t'); i += 2; break;
                        case 'r':  sb.Append('\r'); i += 2; break;
                        case 'b':  sb.Append('\b'); i += 2; break;
                        case 'f':  sb.Append('\f'); i += 2; break;
                        case 'u':
                            if (i + 5 < json.Length &&
                                int.TryParse(json.Substring(i + 2, 4),
                                    System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                i += 6;
                            }
                            else { sb.Append(e); i += 2; }
                            break;
                        default:   sb.Append(e); i += 2; break;
                    }
                }
                else if (c == '"')
                {
                    i++; // past closing quote
                    break;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            result.Add(sb.ToString());
        }
        return result;
    }

    /// <summary>Tell the server the goal is complete (ClientStatus.CLIENT_GOAL = 30).</summary>
    public async void SendGoalComplete()
    {
        if (GoalSent) return;
        GoalSent = true;
        Debug.Log("[AP] Sending goal completion (StatusUpdate 30)");
        if (_apNetworking != null)
            await _apNetworking.SendMessageAsync("[{\"cmd\":\"StatusUpdate\",\"status\":30}]");
    }

    /// <summary>
    /// Called by RandomizerManager when a new game scene loads.
    /// Re-applies cached slot data and silently re-queues all received items so
    /// the fresh scene starts with the correct unlocks even after a "try again".
    /// </summary>
    /// <summary>
    /// Returns the number of consecutive serve-location checks that have already
    /// been sent for <paramref name="ingredient"/>, starting from check #1.
    /// If #1 and #2 are sent but #3 is not, returns 2 even if later ones are sent.
    /// </summary>
    public int GetConsecutiveServeSent(string ingredient, int totalChecks)
    {
        int ingIdx = Array.IndexOf(ServeIngredientOrder, ingredient);
        if (ingIdx < 0) return 0;
        for (int n = 1; n <= totalChecks; n++)
        {
            long id = LocationBase + ingIdx * MaxChecksPerIngred + (n - 1);
            if (!_checkedLocations.Contains(id)) return n - 1;
        }
        return totalChecks;
    }

    public void ReplayTo(RandomizerManager rm)
    {
        rm.ApplySlotData(_sdDrinks, _sdChecks, _sdShop, _sdBasePrice, _sdPriceStep,
                         _sdMinDrinkQuality, _sdDeathLink != 0, _sdDeathLinkSendQuality, _sdDeathLinkPenalty,
                         _sdLocNames, _sdLocItems, _sdLocLocal);
        foreach (string item in _allItems)
            rm.RequeueItem(item);
    }

    /// <summary>Sends a ConnectUpdate packet to update our tags (e.g. to add "DeathLink").</summary>
    private async void SendConnectUpdate(string tagsJson)
    {
        if (_apNetworking == null) return;
        await _apNetworking.SendMessageAsync($"[{{\"cmd\":\"ConnectUpdate\",\"tags\":{tagsJson}}}]");
    }

    /// <summary>Sends a DeathLink bounce to all other DeathLink-opted players.</summary>
    public async void SendDeathLink(string cause)
    {
        if (_apNetworking == null || !IsConnected) return;
        double timestamp = (DateTime.UtcNow - new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
        string safeSlot  = (SlotName ?? "unknown").Replace("\"","\\\"");
        string safeCause = (cause ?? "").Replace("\"","\\\"");
        string packet = $"[{{\"cmd\":\"Bounce\",\"tags\":[\"DeathLink\"],\"data\":{{\"time\":{timestamp:F3},\"source\":\"{safeSlot}\",\"cause\":\"{safeCause}\"}}}}]";
        Debug.Log($"[AP] Sending DeathLink: {cause}");
        await _apNetworking.SendMessageAsync(packet);
    }

    private void HandleBounce(string msg)
    {
        if (!msg.Contains("\"DeathLink\"")) return;
        Debug.Log("[AP] DeathLink received");
        if (Randomizer != null)
            Randomizer.ReceiveDeathLink();
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
