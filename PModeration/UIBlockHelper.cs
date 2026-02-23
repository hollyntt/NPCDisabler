using UnityEngine;
using UnityEngine.UI;
using System.Reflection;
using System.Linq;
using Object = UnityEngine.Object;

namespace PModeration
{
    public static class UIBlockHelper
    {
        private const string BLOCK_BUTTON_NAME = "BlockButton";
        private const string GLOBALNAME_BUTTON_NAME = "GlobalNameButton";
        private const string HEADER_NAME = "_dolly_whoInfo_header";
        private const string REF_BUTTON_NAME = "_button_steamProfile";
        
        // Text objects that squish our buttons
        private const string GLOBAL_TEXT_NAME = "_text_globalName";
        private const string CHAR_TEXT_NAME = "_text_characterName";

        private static Sprite _blockSprite;
        private static Sprite _unblockSprite;
        private static Sprite _globalShowSprite;
        private static Sprite _globalHideSprite;
        private static bool _spritesLoaded = false;

        private static void EnsureSpritesLoaded()
        {
            if (_spritesLoaded) return;
            _spritesLoaded = true;

            // Brute-force find the resources to avoid namespace typo issues
            _blockSprite = LoadSpriteSmart("block.png");
            _unblockSprite = LoadSpriteSmart("unblock.png");
            _globalShowSprite = LoadSpriteSmart("globalshow.png");
            _globalHideSprite = LoadSpriteSmart("globalhide.png");
        }

        private static Sprite LoadSpriteSmart(string fileNameEndsWith)
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Find full name like "PModeration.icons.block.png" or "PModeration.block.png"
            string resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileNameEndsWith, System.StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(resourceName))
            {
                Plugin.Log.LogError($"[UIBlockHelper] MISSING RESOURCE ending with: '{fileNameEndsWith}'");
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            byte[] data = new byte[stream.Length];
            stream.Read(data, 0, data.Length);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(data);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        public static void AddOrUpdateBlockButton(GameObject entryObj, Player player)
        {
            AddOrUpdateButton(entryObj, player, BLOCK_BUTTON_NAME, false);
        }

        public static void AddOrUpdateGlobalNameButton(GameObject entryObj, Player player)
        {
            AddOrUpdateButton(entryObj, player, GLOBALNAME_BUTTON_NAME, true);
        }

        private static void AddOrUpdateButton(GameObject entryObj, Player player, string buttonName, bool isGlobalName)
        {
            if (entryObj == null || player == null || !ulong.TryParse(player._steamID, out ulong steamID))
                return;

            Transform header = FindChildRecursive(entryObj.transform, HEADER_NAME);
            if (header == null) return;

            // --- LAYOUT FIX ---
            ApplyLayoutConstraint(header, CHAR_TEXT_NAME, 65f);
            ApplyLayoutConstraint(header, GLOBAL_TEXT_NAME, 55f);

            // Widen header to fit our two extra buttons
            RectTransform headerRect = header.GetComponent<RectTransform>();
            if (headerRect != null && headerRect.sizeDelta.x < 400f)
            {
                headerRect.sizeDelta = new Vector2(headerRect.sizeDelta.x + 40f, headerRect.sizeDelta.y);
                headerRect.localScale = new Vector3(0.9f, 1f, 1f);
            }
            // ----------------------------------------

            // Check if button exists
            Transform existingBtn = header.Find(buttonName);
            if (existingBtn != null)
            {
                UpdateButtonState(existingBtn.gameObject, steamID, isGlobalName);
                return;
            }

            // Find reference
            Transform refBtn = header.Find(REF_BUTTON_NAME);
            if (refBtn == null) return;

            // Clone
            GameObject btnObj = Object.Instantiate(refBtn.gameObject, header);
            btnObj.name = buttonName;
            btnObj.SetActive(true);
            btnObj.transform.SetSiblingIndex(refBtn.GetSiblingIndex() + 1);

            // Setup Logic
            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                bool wasActive = isGlobalName ? Plugin.Instance.IsGlobalNameHidden(steamID) : Plugin.Instance.IsBlocked(steamID);
                
                if (wasActive)
                {
                    if (isGlobalName) Plugin.Instance.UnhideGlobalName(steamID);
                    else Plugin.Instance.RemoveBlock(steamID);
                }
                else
                {
                    if (isGlobalName) Plugin.Instance.HideGlobalName(steamID);
                    else Plugin.Instance.AddBlock(steamID, player._nickname);
                }
                UpdateButtonState(btnObj, steamID, isGlobalName);
            });

            UpdateButtonState(btnObj, steamID, isGlobalName);
        }

        private static void ApplyLayoutConstraint(Transform header, string childName, float width)
        {
            Transform textObj = header.Find(childName);
            if (textObj != null)
            {
                var layout = textObj.GetComponent<LayoutElement>();
                if (layout == null) layout = textObj.gameObject.AddComponent<LayoutElement>();
                
                // Flexible width 0 means "Don't expand to fill empty space"
                // Preferred width sets the target size
                layout.flexibleWidth = 0f; 
                layout.preferredWidth = width;
            }
        }

        private static void UpdateButtonState(GameObject btnObj, ulong steamID, bool isGlobalName)
        {
            if (btnObj == null) return;
            EnsureSpritesLoaded();

            bool isActive = isGlobalName ? Plugin.Instance.IsGlobalNameHidden(steamID) : Plugin.Instance.IsBlocked(steamID);

            Image img = null;
            // Robust icon finder
            Transform iconTrans = FindChildRecursive(btnObj.transform, "_icon_steamProfile") ?? 
                                  FindChildRecursive(btnObj.transform, "Icon") ?? 
                                  FindChildRecursive(btnObj.transform, "Image");
            
            if (iconTrans != null) img = iconTrans.GetComponent<Image>();
            else img = btnObj.GetComponent<Image>();

            if (img != null)
            {
                if (isGlobalName)
                {
                    // Hidden = Show 'Eye' (Show) | Visible = Show 'Cross' (Hide)
                    img.sprite = isActive ? _globalShowSprite : _globalHideSprite;
                }
                else
                {
                    // Blocked = Show Unblock | Not Blocked = Show Block
                    img.sprite = isActive ? _unblockSprite : _blockSprite;
                }
                img.color = Color.white;
            }
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Equals(name)) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}