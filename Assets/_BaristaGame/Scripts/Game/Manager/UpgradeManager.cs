using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Components; // Required for LocalizeStringEvent

public class UpgradeManager : MonoBehaviour
{
    public GameObject InitialUpgradePanel;
    public GameObject UpgradePanel;

    public bool UpgardePanelVisibility = false;

    [Header("Upgrades")]
    [ReadOnly]
    public bool HasInitialUpgrade = false;

    // Constants for new shop logic
    public const int FLOW_UP_COST = 50;
    public const int FLOW_DOWN_COST = 50;
    public const int SHOP_LOCATION_COST = 300;

    [Space(5)]
    public List<Upgrade> upgrades;

    [Header("Debug Only")]
    [ReadOnly]
    public float AvailableMoney = 0;
    public static UpgradeManager Instance;

    private BaseGameMode gamemode;

    public void Awake()
    {
        Instance = this;

        for (int i = 0; i < upgrades.Count; i++)
        {
            if (upgrades[i].button != null)
            {
                int index = i;
                upgrades[i].button.onClick.AddListener(delegate { BuyUpgrade(upgrades[index].button); });
            }
        }

        UpgradePanel.SetActive(UpgardePanelVisibility);
    }

    // Start is called before the first frame update
    void Start()
    {
        gamemode = BaseGameMode.instance;
    }

    // Update is called once per frame
    void Update()
    {
        if (UpgardePanelVisibility == true)
        {
            AvailableMoney = gamemode.Money;
            UpdateButtonsVisuals();
        }
    }

    public void TogglePanelActive()
    {
        UpgardePanelVisibility = !UpgardePanelVisibility;
        if (HasInitialUpgrade == true)
        {
            UpgradePanel.SetActive(UpgardePanelVisibility);
        }
        else
        {
            InitialUpgradePanel.SetActive(UpgardePanelVisibility);
        }
    }

    public void SetPanelActive(bool state)
    {
        UpgardePanelVisibility = state;
        if (HasInitialUpgrade == true)
        {
            UpgradePanel.SetActive(state);
        }
        else
        {
            InitialUpgradePanel.SetActive(state);
        }
    }

    public void SetTooltipText(string text)
    {
        // Assuming TooltipTrigger has a reference here
        tooltipTrigger.SetTextHeader(text);
    }

    public void BuyUpgrade(Button button)
    {
        if (RandomizerManager.Instance != null) RandomizerManager.Instance.AddRecentEvent("Buy Upgrade: " + button.name);

        int index = -1;
        for (int i = 0; i < upgrades.Count; i++)
        {
            if (upgrades[i].button == button)
            {
                index = i;
                break;
            }
        }

        if (index == -1) return;

        if (index == -1) return;

        // Logic moved to ButtonUpgrade.cs
        // This method is likely not even called if the buttons are wired to ButtonUpgrade.OnClick explicitly.

        UpdateButtonsVisuals();
    }


    /// <summary>
    /// This will be the first Upgrade which start to let her grow
    /// </summary>
    public void BuyInitialUpgarde()
    {
        if (RandomizerManager.Instance != null) RandomizerManager.Instance.AddRecentEvent("Buy Upgrade: Initial");

        // Removed randomizer check from initial cookie as per user request
        
        if (gamemode == null)
        {
            gamemode = BaseGameMode.instance;
        }
        gamemode.UpgradeCanGrow = true;
        if (UpgardePanelVisibility)
        {
            TogglePanelActive();
        }
        HasInitialUpgrade = true;
        InitialUpgradePanel.SetActive(false);
        UpdateButtonsVisuals();
    }

    public void BuyShopSlot(int slotIndex)
    {
        if (RandomizerManager.Instance != null)
        {
            RandomizerManager.Instance.AddRecentEvent($"UpgradeManager: BuyShopSlot. Slot Index: {slotIndex}");
            RandomizerManager.Instance.AddRecentEvent($"Randomizer will handle shop slot: {slotIndex}");
            RandomizerManager.Instance.HandleLocation($"loc_shop_slot_{slotIndex}");
        }
    }

    void UpdateButtonsVisuals()
    {
        if (RandomizerManager.Instance == null)
        {
            return;
        }
        
        // Visual updates are now handled by individual ButtonUpgrade components.
        // This method is kept to satisfy potential callers but does nothing.
    }
}

[System.Serializable]
public class Upgrade
{
    public Button button;
    public TextMeshProUGUI PriceText;
    public int Costs = 10;
    public float CostsModifer = 2;
    public int UpgradedTimes = 1;
    //public int MaxUpgrades = 10;

    public int Happyness = 0;
    public int MaxBust = 0;
    public int Production = 0;
}
