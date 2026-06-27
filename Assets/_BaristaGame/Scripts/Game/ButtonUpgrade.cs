using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.Localization.Components; // LocalizeStringEvent

public class ButtonUpgrade : MonoBehaviour
{

    [Header("References")]
    public TextMeshProUGUI MoneyText;

    // The title text on a shop-converted button (the one we populate with the
    // item name). Captured in ConfigureAsShopSlot; its localizer is disabled.
    private TMP_Text shopLabel;
    // Other text on the button that we keep blanked (old subtitle, etc.).
    private List<TMP_Text> shopBlankTexts = new List<TMP_Text>();

    [Header("Settings")]

    public float CostInitial = 10;
    public float CostMultipler = 2;

    [Tooltip("Only for the Tollerance Upgrade, Reduces in Percentage the happyness degree")]
    public AnimationCurve TolleranceIncreasePercentage;

    [Min(0)]
    [Tooltip("How Many times can the upgrade be applied? 0 is Unlimited Times. If limit reached, it will turn itshelf off.")]
    public int MaxUpgrades = 0;
    public Color ColorReachedMaxUpgrades = Color.gray;

    public UpgradeType TypeOfUpgrade;
    [Tooltip("This tells how many times/how strong this upgarde get Applied, if you put -1 in the value it will substract it")]
    public int UpgradeTimes = 1;

    [Tooltip("For ShopLocation upgrades, which slot index in the queue does this button represent?")]
    public int ShopSlotIndex = 0;

    public bool DoSupriseGrowthAnimation = false;

    private UpgradeManager manager;
    private BaseGameMode gamemode;

    [ReadOnly]
    public int UpgradedTimes = 0;

    [ReadOnly][SerializeField]
    private float currentCost = 0;

    private Button button;

    private AudioSource audioSource;
    public AudioClip clickClip;

    public static Dictionary<KeyBindingManager.BindableActions, ButtonUpgrade> buttonUpgrades;

    private void ReplaceButtonUpgradeInDictionary(KeyBindingManager.BindableActions bind)
    {
        if(buttonUpgrades.ContainsKey(bind))
        {
            buttonUpgrades.Remove(bind);
        }
        buttonUpgrades.Add(bind, this);
    }

    public void Initialize()
    {
        audioSource = Camera.main.GetComponent<AudioSource>();
        if (buttonUpgrades == null)
        {
            buttonUpgrades = new Dictionary<KeyBindingManager.BindableActions, ButtonUpgrade> ();
        }
        RandomizerManager.Instance.AddRecentEvent($"Button Title: {TypeOfUpgrade}");
        switch(TypeOfUpgrade)
        {
            case UpgradeType.Production:
                if(UpgradeTimes > 0)
                {
                    ReplaceButtonUpgradeInDictionary(KeyBindingManager.BindableActions.productionUpgrade);
                }
                else
                {
                    ReplaceButtonUpgradeInDictionary(KeyBindingManager.BindableActions.productionDowngrade);
                }
                break;
            case UpgradeType.MaxSize:
                ReplaceButtonUpgradeInDictionary(KeyBindingManager.BindableActions.sizeUpgrade);
                break;
            case UpgradeType.Happyness:
                ReplaceButtonUpgradeInDictionary(KeyBindingManager.BindableActions.happinessPurchase);
                break;
            case UpgradeType.MilkFullness:
                ReplaceButtonUpgradeInDictionary(KeyBindingManager.BindableActions.milkNowUpgrade);
                break;
            case UpgradeType.TolleranceBeeingFull:
                ReplaceButtonUpgradeInDictionary(KeyBindingManager.BindableActions.toleranceUpgrade);
                break;
            case UpgradeType.InitialUpgarde:
                ReplaceButtonUpgradeInDictionary(KeyBindingManager.BindableActions.initialUpgrade);
                break;
        }
        UpdateReferences();
    }

    public void OnEnable()
    {
        UpdateReferences();
        UpdateVisuals();
    }

    private void Update()
    {
        UpdateVisuals();
    }

    private void UpdateReferences()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (manager == null)
        {
            manager = UpgradeManager.Instance;
        }

        if (gamemode == null)
        {
            gamemode = BaseGameMode.instance;
        }

        CalcCurrentCost();

    }

    public void UpdateVisuals()
    {
        if (TypeOfUpgrade == UpgradeType.ShopLocation)
        {
            UpdateShopVisuals();
            return;
        }

        if (MaxUpgrades > 0 && UpgradedTimes >= MaxUpgrades)
        {
            button.interactable = false;
            button.enabled = false;
            button.targetGraphic.color = ColorReachedMaxUpgrades;


            if (MoneyText != null)
            {
                MoneyText.text = Statics.ButtonMaxUpgrades;
            }
        }
        else
        {
            if (gamemode == null)
            {
                UpdateReferences();
            }

            if (MoneyText != null)
            {
                MoneyText.text = currentCost.ToString("0.00") + " " + Statics.CurrencySymbol;
            }

            if (currentCost < gamemode.Money)
            {
                button.interactable = true;
            }
            else
            {
                button.interactable = false;
            }
        }
    }

    /// <summary>
    /// Turns this button into AP shop slot #slotIndex. Disables the title's
    /// LocalizeStringEvent (which would otherwise overwrite the text we set)
    /// and captures the title TMP so UpdateShopVisuals can populate it.
    /// </summary>
    public void ConfigureAsShopSlot(int slotIndex)
    {
        TypeOfUpgrade = UpgradeType.ShopLocation;
        ShopSlotIndex = slotIndex;
        MaxUpgrades   = 0; // unlimited; the shop branch governs availability

        // The hover tooltip describes the old upgrade's mechanics, which no
        // longer exist in the rando. Disable any tooltip triggers on this
        // converted button so nothing shows on hover.
        foreach (var tip in GetComponentsInChildren<TooltipTrigger>(true))
            tip.enabled = false;

        // The localized title is the label we want to own. Disable each
        // localizer under this button and capture the TMP it drove (which is
        // not the price text). The price text has no localizer, so it's safe.
        foreach (var loc in GetComponentsInChildren<LocalizeStringEvent>(true))
        {
            loc.enabled = false;
            var t = loc.GetComponent<TMP_Text>();
            if (t == null) t = loc.GetComponentInChildren<TMP_Text>(true);
            if (t != null && t != MoneyText) shopLabel = t;
        }

        // Fallback if no localizer was found: first child TMP that isn't the price.
        if (shopLabel == null)
        {
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            {
                if (t != MoneyText) { shopLabel = t; break; }
            }
        }

        // Any remaining text on the button (e.g. the original "Production+"
        // subtitle) is clutter — blank it and disable its localizer so it
        // doesn't repopulate. We keep only the item label and the price.
        shopBlankTexts.Clear();
        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
        {
            if (t == MoneyText || t == shopLabel) continue;
            var loc = t.GetComponent<LocalizeStringEvent>();
            if (loc != null) loc.enabled = false;
            t.text = "";
            shopBlankTexts.Add(t);
        }
    }

    private void UpdateShopVisuals()
    {
        if (button == null || gamemode == null)
        {
            UpdateReferences();
        }

        var rm = RandomizerManager.Instance;
        int available = rm != null ? rm.GetShopQueueCount() : 0;

        // This slot has nothing left to sell - disable and mark sold out.
        if (rm == null || ShopSlotIndex >= available)
        {
            button.interactable = false;
            if (MoneyText != null) MoneyText.text = Statics.ButtonMaxUpgrades;
            if (shopLabel != null) shopLabel.text = "Sold Out";
            return;
        }

        // Label the button with what this slot will hand out. Set every frame
        // so it stays correct as the queue advances after each purchase.
        if (shopLabel != null) shopLabel.text = rm.GetShopItemDisplayForSlot(ShopSlotIndex);
        for (int i = 0; i < shopBlankTexts.Count; i++)
            if (shopBlankTexts[i] != null) shopBlankTexts[i].text = "";

        currentCost = rm.GetShopCostForSlot(ShopSlotIndex);
        if (MoneyText != null)
        {
            MoneyText.text = currentCost.ToString("0.00") + " " + Statics.CurrencySymbol;
        }
        button.interactable = currentCost <= gamemode.Money;
    }

    private void CalcCurrentCost()
    {
        if (TypeOfUpgrade == UpgradeType.ShopLocation)
        {
            currentCost = RandomizerManager.Instance != null
                ? RandomizerManager.Instance.GetShopCostForSlot(ShopSlotIndex)
                : 0f;
            return;
        }

        if (UpgradedTimes == 0)
        {
            currentCost = CostInitial;
        }
        else
        {
            float additionalCost = CostInitial;
            for (int i = 0; i < (UpgradedTimes); i++)
            {
                additionalCost = additionalCost * CostMultipler;
            }

           currentCost = additionalCost;
        }
    }

    public void OnClick()
    {
        DoUpgrade();
        CalcCurrentCost();
    }

    private void DoUpgrade()
    {
        RandomizerManager.Instance.AddRecentEvent($"Button Upgrade: {TypeOfUpgrade}");
        CalcCurrentCost();

        // Shop slots have no upcoming location once the queue is drained.
        if (TypeOfUpgrade == UpgradeType.ShopLocation)
        {
            var rm = RandomizerManager.Instance;
            if (rm == null || ShopSlotIndex >= rm.GetShopQueueCount())
            {
                return; // sold out
            }
        }

        if (MaxUpgrades > 0 && UpgradedTimes >= MaxUpgrades)
        {
            return;
        }
        if (currentCost > gamemode.Money)
        {
            return;
        }

        UpgradedTimes++;

        gamemode.SubMoney(currentCost);


        switch (TypeOfUpgrade)
        {
            case UpgradeType.InitialUpgarde:
                manager.BuyInitialUpgarde();
                audioSource.PlayOneShot(clickClip);
                CafeVisualsController.instance.SetStatsLightning(true);
                break;
            case UpgradeType.Production:
                gamemode.BuyUpgradeProduction(UpgradeTimes);
                break;
            case UpgradeType.MaxSize:
                gamemode.BuyMaxSize(UpgradeTimes);
                break;
            case UpgradeType.Happyness:
                gamemode.BuyHappyness(UpgradeTimes);
                break;
            case UpgradeType.MilkFullness:
                gamemode.BuyMilkFullness(UpgradeTimes, DoSupriseGrowthAnimation);
                break;
            case UpgradeType.TolleranceBeeingFull:
                gamemode.BuyTolerance(UpgradedTimes, TolleranceIncreasePercentage);
                break;
            case UpgradeType.ShopLocation:
                if (RandomizerManager.Instance != null)
                {
                    var availableLocs = RandomizerManager.Instance.GetNextShopLocations(ShopSlotIndex + 1);
                    if (ShopSlotIndex < availableLocs.Count)
                    {
                        RandomizerManager.Instance.BuyShopLocation(availableLocs[ShopSlotIndex]);
                    }
                }
                break;
            default:
                break;
        }


    }

    private void OnDestroy()
    {
        buttonUpgrades = null;
    }

    public enum UpgradeType
    {
        InitialUpgarde = 0,
        Production = 1,
        MaxSize = 2,
        Happyness = 3,
        MilkFullness = 4,
        TolleranceBeeingFull = 5,
        ShopLocation = 6,
    }
}


