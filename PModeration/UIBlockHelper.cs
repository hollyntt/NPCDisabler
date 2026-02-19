// ────────────────────────────────────────────────
//  UIBlockHelper.cs
//  Adds a Block button to WhoListDataEntry objects.
// ────────────────────────────────────────────────
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

namespace PModeration;

public static class UIBlockHelper
{
    private const string BLOCK_BUTTON_NAME = "BlockButton";
    private const string ICON_CHILD_NAME   = "_icon_steamProfile";

    private static Sprite _blockSprite;
    private static Sprite _unblockSprite;
    private static bool   _spritesLoaded = false;

    private static void EnsureSpritesLoaded()
    {
        if (_spritesLoaded) return;
        _spritesLoaded = true;

        _blockSprite   = LoadSprite("PModeration.icons.block.png");
        _unblockSprite = LoadSprite("PModeration.icons.unblock.png");
    }

    private static Sprite LoadSprite(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Plugin.Log.LogWarning($"UIBlockHelper: Embedded resource '{resourceName}' not found.");
            return null;
        }

        byte[] data = new byte[stream.Length];
        stream.Read(data, 0, data.Length);

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(data))
        {
            Plugin.Log.LogWarning($"UIBlockHelper: Failed to decode '{resourceName}'");
            return null;
        }

        tex.filterMode = FilterMode.Bilinear;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

        if (Plugin.CfgDebugMode.Value)
            Plugin.Log.LogInfo($"UIBlockHelper: Loaded '{resourceName}' successfully.");

        return sprite;
    }

    public static void AddOrUpdateBlockButton(GameObject entryObj, Player player)
    {
        if (entryObj == null || player == null || !ulong.TryParse(player._steamID, out ulong steamID))
        {
            Plugin.Log.LogWarning("UIBlockHelper: Invalid entry or player");
            return;
        }

        EnsureSpritesLoaded();

        // Already exists — just refresh state
        Transform existing = entryObj.transform.Find(BLOCK_BUTTON_NAME);
        if (existing != null)
        {
            UpdateButtonState(existing.gameObject, steamID);
            return;
        }

        // Reference button to clone from
        WhoListDataEntry dataEntry = entryObj.GetComponent<WhoListDataEntry>();
        GameObject refBtnObj = dataEntry?._steamProfileButton?.gameObject
                            ?? dataEntry?._dataEntryButton?.gameObject
                            ?? entryObj.GetComponentInChildren<Button>(true)?.gameObject;

        if (refBtnObj == null)
        {
            Plugin.Log.LogWarning($"UIBlockHelper: No button found to clone in '{entryObj.name}'");
            return;
        }

        // Clone
        GameObject blockBtnObj = UnityEngine.Object.Instantiate(refBtnObj, refBtnObj.transform.parent);
        blockBtnObj.name = BLOCK_BUTTON_NAME;
        blockBtnObj.transform.SetSiblingIndex(refBtnObj.transform.GetSiblingIndex() + 1);

        Button blockBtn = blockBtnObj.GetComponent<Button>();
        if (blockBtn == null)
        {
            Plugin.Log.LogWarning("UIBlockHelper: Cloned object has no Button component");
            UnityEngine.Object.Destroy(blockBtnObj);
            return;
        }

        // Disable Unity's transition system so it doesn't fight our icon/tint
        blockBtn.transition = Selectable.Transition.None;

        // Clear leftover text
        var label = blockBtnObj.GetComponentInChildren<Text>(true);
        if (label != null) label.text = string.Empty;

        // Wire up click
        ulong capturedID    = steamID;
        string capturedName = player._nickname;
        blockBtn.onClick.RemoveAllListeners();
        blockBtn.onClick.AddListener(() =>
        {
            if (PModerationAPI.IsPlayerBlocked(capturedID))
            {
                PModerationAPI.UnblockPlayer(capturedID);
                if (Plugin.CfgDebugMode.Value)
                    Plugin.Log.LogInfo($"Unblocked {capturedName} ({capturedID}) via UI button");
            }
            else
            {
                PModerationAPI.BlockPlayer(capturedID);
                if (Plugin.CfgDebugMode.Value)
                    Plugin.Log.LogInfo($"Blocked {capturedName} ({capturedID}) via UI button");
            }

            Plugin.Instance.ForceRefreshAll();
            UpdateButtonState(blockBtnObj, capturedID);
        });

        UpdateButtonState(blockBtnObj, steamID);

        if (Plugin.CfgDebugMode.Value)
            Plugin.Log.LogInfo($"UIBlockHelper: Added Block button for {player._nickname} ({steamID})");
    }

    private static void UpdateButtonState(GameObject btnObj, ulong steamID)
    {
        if (btnObj == null) return;
        bool isBlocked = PModerationAPI.IsPlayerBlocked(steamID);

        Transform iconChild = btnObj.transform.Find(ICON_CHILD_NAME);
        Image img = iconChild?.GetComponent<Image>() ?? btnObj.GetComponent<Image>();

        if (img != null)
        {
            // Swap sprite: block icon when not blocked, unblock icon when blocked
            img.sprite = isBlocked ? _unblockSprite : _blockSprite;
            img.color  = Color.white; // no tint needed — icons are self-explanatory
        }

        if (Plugin.CfgDebugMode.Value)
            Plugin.Log.LogInfo($"UIBlockHelper: UpdateButtonState — isBlocked={isBlocked}, img={(img != null ? img.name : "null")}");
    }
}