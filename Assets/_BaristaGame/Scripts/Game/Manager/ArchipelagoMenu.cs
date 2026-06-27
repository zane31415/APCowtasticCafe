using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to the Manager GameObject in MainMenu.unity.
/// Hides the Arcade Mode UI and injects an Archipelago connection panel.
/// </summary>
public class ArchipelagoMenu : MonoBehaviour
{
    private const string DefaultHost    = "archipelago.gg";
    private const int    DefaultPort    = 38281;
    private const string GameSceneName  = "Game_Arcade";

    private TMP_InputField    _hostInput;
    private TMP_InputField    _portInput;
    private TMP_InputField    _slotInput;
    private TMP_InputField    _passwordInput;
    private TextMeshProUGUI   _statusLabel;
    private Button            _connectButton;

    // Cafe colour palette
    private static readonly Color ColCream  = new Color(0.96f, 0.93f, 0.84f);
    private static readonly Color ColBrown  = new Color(0.35f, 0.20f, 0.10f);
    private static readonly Color ColDark   = new Color(0.20f, 0.12f, 0.06f);
    private static readonly Color ColTan    = new Color(0.70f, 0.52f, 0.30f);
    private static readonly Color ColGhost  = new Color(0.70f, 0.60f, 0.50f, 0.75f);

    private void Start()
    {
        HideArcadeMode();
        var apClient = ArchipelagoClient.Instance;
        if (apClient != null && apClient.IsConnected)
            InjectAlreadyConnectedPanel(apClient.SlotName);
        else
            InjectConnectPanel();
    }

    // -------------------------------------------------------------------------
    // Already-connected panel (shown when returning to main menu mid-session)
    // -------------------------------------------------------------------------

    private void InjectAlreadyConnectedPanel(string slotName)
    {
        var mainMenu = GameObject.Find("Main Menu");
        if (mainMenu == null)
        {
            Debug.LogError("[AP] 'Main Menu' not found — falling back to connect panel");
            InjectConnectPanel();
            return;
        }

        var panel = BuildPanel(mainMenu);
        AddTitle(panel, "Archipelago Multiworld");
        AddLabel(panel, $"Connected as: {slotName}", 16);
        AddButton(panel, "Play Randomizer", () => LevelManager.ChangeScene(GameSceneName));
        _statusLabel = AddLabel(panel, "", 14);
        AddButton(panel, "Disconnect", () =>
        {
            ArchipelagoClient.Instance?.Disconnect();
            LevelManager.ChangeScene("MainMenu");
        });
    }

    // -------------------------------------------------------------------------
    // Scene surgery
    // -------------------------------------------------------------------------

    private void HideArcadeMode()
    {
        var buttonArcade = GameObject.Find("Button Arcade");
        if (buttonArcade != null)
            buttonArcade.SetActive(false);
        else
            Debug.LogWarning("[AP] 'Button Arcade' not found — skipping hide");

        var arcadeMode = GameObject.Find("Arcade Mode");
        if (arcadeMode != null)
            arcadeMode.SetActive(false);
        else
            Debug.LogWarning("[AP] 'Arcade Mode' not found — skipping hide");
    }

    private void InjectConnectPanel()
    {
        var mainMenu = GameObject.Find("Main Menu");
        if (mainMenu == null)
        {
            Debug.LogError("[AP] 'Main Menu' GameObject not found — cannot inject connect panel");
            return;
        }

        var panel = BuildPanel(mainMenu);

        AddTitle(panel, "Archipelago Multiworld");

        string savedHost = PlayerPrefs.GetString("AP_Host", DefaultHost);
        string savedPort = PlayerPrefs.GetString("AP_Port", DefaultPort.ToString());
        string savedSlot = PlayerPrefs.GetString("AP_Slot", "");

        _hostInput     = AddRow(panel, "Host",      savedHost);
        _portInput     = AddRow(panel, "Port",      savedPort,
                                 TMP_InputField.ContentType.IntegerNumber);
        _slotInput     = AddRow(panel, "Slot Name", savedSlot);
        _passwordInput = AddRow(panel, "Password",  "",
                                 TMP_InputField.ContentType.Password);

        _connectButton = AddButton(panel, "Connect", OnConnectClicked);

        _statusLabel = AddLabel(panel, "Enter connection details above.", 14);
        _statusLabel.color = ColGhost;
    }

    // -------------------------------------------------------------------------
    // Connection logic
    // -------------------------------------------------------------------------

    private void OnConnectClicked()
    {
        string host     = _hostInput.text.Trim();
        string portText = _portInput.text.Trim();
        string slot     = _slotInput.text.Trim();
        string password = _passwordInput.text;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(slot))
        {
            SetStatus("Host and Slot Name are required.", error: true);
            return;
        }

        if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
        {
            SetStatus("Invalid port number.", error: true);
            return;
        }

        PlayerPrefs.SetString("AP_Host", host);
        PlayerPrefs.SetString("AP_Port", portText);
        PlayerPrefs.SetString("AP_Slot", slot);
        PlayerPrefs.Save();

        _connectButton.interactable = false;
        SetStatus("Connecting...");
        StartCoroutine(DoConnect($"{host}:{port}", slot, password));
    }

    private IEnumerator DoConnect(string serverUrl, string slot, string password)
    {
        var apClient = ArchipelagoClient.Instance;
        if (apClient == null)
        {
            SetStatus("Error: ArchipelagoClient singleton missing.", error: true);
            _connectButton.interactable = true;
            yield break;
        }

        // Connect() is async void; it sets IsConnected when done.
        apClient.Connect(serverUrl, slot, password);

        float elapsed = 0f;
        while (!apClient.IsConnected && !apClient.ConnectionFailed && elapsed < 6f)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (apClient.IsConnected)
        {
            SetStatus($"Connected as {slot}!");
            yield return new WaitForSecondsRealtime(0.6f);
            LevelManager.ChangeScene(GameSceneName);
        }
        else
        {
            SetStatus("Connection failed. Check details and try again.", error: true);
            _connectButton.interactable = true;
        }
    }

    private void SetStatus(string msg, bool error = false)
    {
        if (_statusLabel == null) return;
        _statusLabel.text  = msg;
        _statusLabel.color = error ? new Color(1f, 0.45f, 0.35f) : ColGhost;
    }

    // -------------------------------------------------------------------------
    // UI builders
    // -------------------------------------------------------------------------

    private GameObject BuildPanel(GameObject parent)
    {
        var go = new GameObject("ArchipelagoPanel");
        go.transform.SetParent(parent.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.5f, 0.5f);
        rect.anchorMax        = new Vector2(0.5f, 0.5f);
        rect.pivot            = new Vector2(0.5f, 0.5f);
        rect.sizeDelta        = new Vector2(430f, 330f);
        rect.anchoredPosition = Vector2.zero;

        var img   = go.AddComponent<Image>();
        img.color = ColBrown;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(24, 24, 20, 20);
        vlg.spacing              = 10;
        vlg.childAlignment       = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        return go;
    }

    private void AddTitle(GameObject parent, string text)
    {
        var lbl = AddLabel(parent, text, 22);
        lbl.fontStyle = FontStyles.Bold;

        var le = lbl.gameObject.GetComponent<LayoutElement>() ?? lbl.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 40f;
    }

    private TextMeshProUGUI AddLabel(GameObject parent, string text, int size = 15)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent.transform, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = ColCream;
        tmp.alignment = TextAlignmentOptions.Center;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = size * 1.6f;

        return tmp;
    }

    private TMP_InputField AddRow(
        GameObject parent,
        string label,
        string defaultValue,
        TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
    {
        var row = new GameObject("Row_" + label);
        row.transform.SetParent(parent.transform, false);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing              = 8;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment       = TextAnchor.MiddleLeft;

        var rowLe = row.AddComponent<LayoutElement>();
        rowLe.preferredHeight = 36f;

        // Label
        var lblGo = new GameObject("Lbl");
        lblGo.transform.SetParent(row.transform, false);
        var lblLe  = lblGo.AddComponent<LayoutElement>();
        lblLe.preferredWidth   = 100f;
        lblLe.flexibleWidth    = 0;
        var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
        lblTmp.text      = label + ":";
        lblTmp.fontSize  = 15;
        lblTmp.color     = ColCream;
        lblTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // Input background
        var inputGo   = new GameObject("Input");
        inputGo.transform.SetParent(row.transform, false);
        var inputLe   = inputGo.AddComponent<LayoutElement>();
        inputLe.flexibleWidth    = 1;
        inputLe.preferredHeight  = 36f;
        inputGo.AddComponent<RectTransform>();
        var bg        = inputGo.AddComponent<Image>();
        bg.color      = ColDark;

        var inputField       = inputGo.AddComponent<TMP_InputField>();
        inputField.targetGraphic = bg;
        inputField.contentType   = contentType;

        // Text area (required for TMP_InputField to work correctly)
        var textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputGo.transform, false);
        var taRect   = textArea.AddComponent<RectTransform>();
        taRect.anchorMin  = Vector2.zero;
        taRect.anchorMax  = Vector2.one;
        taRect.offsetMin  = new Vector2(6, 2);
        taRect.offsetMax  = new Vector2(-6, -2);
        textArea.AddComponent<RectMask2D>();
        inputField.textViewport = taRect;

        // Editable text
        var textGo   = new GameObject("Text");
        textGo.transform.SetParent(textArea.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var textTmp  = textGo.AddComponent<TextMeshProUGUI>();
        textTmp.fontSize  = 15;
        textTmp.color     = ColCream;
        textTmp.alignment = TextAlignmentOptions.MidlineLeft;
        inputField.textComponent = textTmp;

        // Placeholder
        var phGo     = new GameObject("Placeholder");
        phGo.transform.SetParent(textArea.transform, false);
        var phRect   = phGo.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = Vector2.zero;
        phRect.offsetMax = Vector2.zero;
        var phTmp    = phGo.AddComponent<TextMeshProUGUI>();
        phTmp.text      = defaultValue;
        phTmp.fontSize  = 15;
        phTmp.color     = ColGhost;
        phTmp.fontStyle = FontStyles.Italic;
        phTmp.alignment = TextAlignmentOptions.MidlineLeft;
        inputField.placeholder = phTmp;

        inputField.text = defaultValue;

        return inputField;
    }

    private Button AddButton(GameObject parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        var go  = new GameObject("Button_" + text);
        go.transform.SetParent(parent.transform, false);

        var bg  = go.AddComponent<Image>();
        bg.color = ColTan;

        var btn    = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.85f, 0.65f, 0.38f);
        colors.pressedColor     = new Color(0.50f, 0.36f, 0.18f);
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 42f;

        var textGo   = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp      = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = 16;
        tmp.color     = ColBrown;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;

        return btn;
    }
}
