using System;
using System.Collections;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace pp.RaftMods.NightVisionGoggles
{
    /// <summary>
    /// Night Vision Goggles mod - Adds head equipment that enhances visibility in dark environments.
    /// Compatible with HML (Raft Mod Loader).
    /// </summary>
    public class NightVisionGoggles : Mod
    {
        public static NightVisionGoggles Instance { get; private set; }

        // -- ExtraSettingsAPI integration -----------------------------------------
        private static bool ESA_Loaded = false;
        private string esaToggleKeyName = null;

        // -- Mod fields -----------------------------------------------------------
        private Harmony harmony;
        private Item_Base nightVisionItem;
        private GameObject equipmentPrefab;
        private NightVisionGogglesEquipment equipmentComponent;
        private Coroutine injectCoroutine;

        public void Start()
        {
            try
            {
                Instance = this;

                // Apply Harmony patches
                harmony = new Harmony("pp.RaftMods.NightVisionGoggles");
                harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

                // Register the item
                RegisterNightVisionGoggles();

            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionGoggles] Start failed: " + ex);
            }
        }

        public void OnModUnload()
        {
            try
            {
                if (injectCoroutine != null)
                {
                    StopCoroutine(injectCoroutine);
                    injectCoroutine = null;
                }

                if (harmony != null)
                {
                    harmony.UnpatchAll("pp.RaftMods.NightVisionGoggles");
                }

                if (equipmentPrefab != null)
                {
                    UnityEngine.Object.Destroy(equipmentPrefab);
                    equipmentPrefab = null;
                }

                Instance = null;

            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionGoggles] OnModUnload failed: " + ex);
            }
        }

        /// <summary>
        /// Creates the equipment prefab GameObject. The prefab is NOT a child of any player yet;
        /// it gets injected into PlayerEquipment.equipment[] once the local player spawns.
        /// </summary>
        private GameObject CreateEquipmentPrefab(Item_Base item)
        {
            GameObject prefab = new GameObject("NightVisionGoggles_EquipmentPrefab");
            prefab.SetActive(false); // Inactive until injected into player

            // Add the custom equipment component
            NightVisionGogglesEquipment equipment = prefab.AddComponent<NightVisionGogglesEquipment>();
            equipment.equipableItem = item;
            equipment.sendNetworkInfo = false; // Local-only mod, no need to network
            equipmentComponent = equipment;

            return prefab;
        }

        /// <summary>
        /// Injects the equipment prefab into the local PlayerEquipment.equipment[] array.
        /// Called via coroutine because the local player may not exist when the mod starts.
        /// </summary>
        private IEnumerator InjectEquipmentCoroutine()
        {

            // Wait for the local player to spawn
            float waitStart = Time.realtimeSinceStartup;
            while (ComponentManager<Network_Player>.Value == null)
            {
                if (Time.realtimeSinceStartup - waitStart > 60f)
                {
                    Debug.LogError("[NightVisionGoggles] Timeout waiting for local player, aborting equipment injection");
                    injectCoroutine = null;
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
            Debug.Log("[NightVisionGoggles] Local player found after " + (Time.realtimeSinceStartup - waitStart).ToString("F1") + "s");

            // Get the PlayerEquipment component
            PlayerEquipment playerEquipment = null;
            while (playerEquipment == null)
            {
                playerEquipment = ComponentManager<Network_Player>.Value.GetComponentInChildren<PlayerEquipment>(true);
                if (playerEquipment == null)
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }

            // Re-parent the prefab under the player (so it gets picked up by GetComponentsInChildren)
            if (equipmentPrefab != null && playerEquipment != null)
            {
                Transform playerTransform = playerEquipment.transform.parent != null
                    ? playerEquipment.transform.parent
                    : playerEquipment.transform;

                equipmentPrefab.transform.SetParent(playerTransform, false);
                equipmentPrefab.SetActive(true);

                // Inject into PlayerEquipment.equipment[] via Traverse
                // PlayerEquipment.Awake() already ran, so we need to update the array
                try
                {
                    var traverse = Traverse.Create(playerEquipment);
                    Equipment[] currentEquipment = traverse.Field("equipment").GetValue<Equipment[]>();

                    if (currentEquipment == null)
                    {
                        currentEquipment = new Equipment[0];
                    }

                    // Check if our equipment is already in the array
                    foreach (Equipment eq in currentEquipment)
                    {
                        if (eq != null && eq.equipableItem != null && eq.equipableItem.UniqueIndex == nightVisionItem.UniqueIndex)
                        {

                            injectCoroutine = null;
                            yield break;
                        }
                    }

                    Equipment[] newEquipment = new Equipment[currentEquipment.Length + 1];
                    Array.Copy(currentEquipment, newEquipment, currentEquipment.Length);
                    newEquipment[currentEquipment.Length] = equipmentComponent;
                    traverse.Field("equipment").SetValue(newEquipment);

                    Debug.Log("[NightVisionGoggles] Equipment injected into PlayerEquipment (total: " + newEquipment.Length + ")");

                    // CRITICAL: If a save was restored before the equipment was injected,
                    // the goggles may already be in a head slot but Equip() was never called.
                    // Force a re-equip to initialize the controller.
                    StartCoroutine(ReEquipIfNeeded(playerEquipment));
                }
                catch (Exception ex)
                {
                    Debug.LogError("[NightVisionGoggles] Failed to inject equipment: " + ex);
                }
            }

            injectCoroutine = null;
        }

        /// <summary>
        /// Checks if the NightVisionGoggles are already in a head equip slot
        /// (e.g. from a save game restore) and force-re-equips them to trigger
        /// the Equip() callback which creates the controller.
        /// This fixes the bug where you need to unequip+re-equip after restart.
        /// </summary>
        private IEnumerator ReEquipIfNeeded(PlayerEquipment playerEquipment)
        {
            // Small delay to let the game finish setting up slots
            yield return new WaitForSeconds(0.3f);

            try
            {
                // Find any equipped head slot containing our goggles
                Slot_Equip headSlot = Slot_Equip.GetEquipSlotWithTag(EquipSlotType.Head);
                if (headSlot != null && !headSlot.IsEmpty &&
                    headSlot.itemInstance != null &&
                    headSlot.itemInstance.UniqueName == "NightVisionGoggles")
                {
                    // Force re-equip: this will call our NightVisionGogglesEquipment.Equip()
                    playerEquipment.Equip(headSlot.itemInstance, headSlot);
                    Debug.Log("[NightVisionGoggles] Re-equipped goggles after injection (save restore fix)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionGoggles] ReEquipIfNeeded failed: " + ex);
            }
        }

        private void RegisterNightVisionGoggles()
        {
            try
            {
                // Try to get an icon from an existing head equipment item
                Sprite icon = null;
                Item_Base referenceItem = ItemManager.GetItemByName("HeadLight");
                if (referenceItem != null && referenceItem.settings_Inventory != null)
                {
                    icon = referenceItem.settings_Inventory.Sprite;

                }

                // If still no icon, try another item
                if (icon == null)
                {
                    referenceItem = ItemManager.GetItemByName("Binoculars");
                    if (referenceItem != null && referenceItem.settings_Inventory != null)
                    {
                        icon = referenceItem.settings_Inventory.Sprite;

                    }
                }

                // If still no icon, create a simple colored texture as fallback
                if (icon == null)
                {
                    Debug.LogWarning("[NightVisionGoggles] No reference icon found, creating fallback texture");
                    Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    Color[] pixels = new Color[64 * 64];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = new Color(0f, 0.5f, 0f, 1f); // Green color
                    }
                    tex.SetPixels(pixels);
                    tex.Apply();
                    icon = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
                    icon.hideFlags = HideFlags.HideAndDontSave;
                    icon.name = "NightVisionGoggles_Icon";

                }

                // Create the Night Vision Goggles item
                nightVisionItem = ScriptableObject.CreateInstance<Item_Base>();
                nightVisionItem.hideFlags = HideFlags.HideAndDontSave;
                nightVisionItem.Initialize(9001, "NightVisionGoggles", 300);

                // CRITICAL: Initialize the private barGradient field on Item_Base.
                // Slot.RefreshComponents() calls BarGradient.Evaluate() which throws
                // NullReferenceException if barGradient is null. This also fixes the
                // durability bar gradient color (green to red).
                Gradient defaultGradient = new Gradient();
                defaultGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.2f, 0.8f, 0.2f), 0f), // Green when full
                        new GradientColorKey(new Color(0.8f, 0.8f, 0.2f), 0.5f), // Yellow at half
                        new GradientColorKey(new Color(0.8f, 0.2f, 0.2f), 1f) // Red when empty
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
                Traverse.Create(nightVisionItem).Field("barGradient").SetValue(defaultGradient);

                // Initialize settings_Inventory with constructor parameters
                nightVisionItem.settings_Inventory = new ItemInstance_Inventory(icon, "Item/NightVisionGoggles", 1);
                nightVisionItem.settings_Inventory.DisplayName = "Night Vision Goggles";
                nightVisionItem.settings_Inventory.Description = "Electronic goggles that enhance visibility in dark environments. Press V to toggle.";

                // Initialize settings_buildable to prevent BlockCreator NullReferenceException
                // Pass null for Block so HasBuildablePrefabs returns false (not buildable)
                nightVisionItem.settings_buildable = new ItemInstance_Buildable(null, false, false, false);

                // Initialize other settings to prevent ItemInstance constructor NullReferenceException
                // These must be initialized even if not used, otherwise AddItem fails
                // ItemInstance_Cookable(int cookingSlotsRequired, float cookTime, Cost cookingResult)
                nightVisionItem.settings_cookable = new ItemInstance_Cookable(1, 1f, null);
                // ItemInstance_Consumeable(float hungerYield, float bonusHungerYield, float thirstYield, float oxygenYield, bool isRaw, Cost itemAfterUse, FoodType foodType, FoodForm foodForm)
                nightVisionItem.settings_consumeable = new ItemInstance_Consumeable(0f, 0f, 0f, 0f, false, null, FoodType.None, FoodForm.Solid);
                // ItemInstance_Usable(string useButtonName, float useButtonCooldown, int consumeUseAmount, bool isUsable, bool allowHoldButton, PlayerAnimation animationOnSelect, PlayerAnimation animationOnUse, bool forceAnimationIndex, bool setTriggering, bool lockDuringCooldown, string resetTrigger)
                nightVisionItem.settings_usable = new ItemInstance_Usable("LeftClick", 0.2f, 0, false, false, PlayerAnimation.None, PlayerAnimation.None, false, false, false, "");

                // Initialize settings_recipe with constructor parameters
                nightVisionItem.settings_recipe = new ItemInstance_Recipe(CraftingCategory.Equipment, false, true, "", 0);

                // CRITICAL: Set amountToCraft to 1 using Traverse (it's a private field)
                Traverse.Create(nightVisionItem.settings_recipe).Field("amountToCraft").SetValue(1);

                // Get recipe items with correct names from Raft source
                Item_Base batteryItem = ItemManager.GetItemByName("Battery");
                Item_Base plasticItem = ItemManager.GetItemByName("Plastic");
                Item_Base circuitBoardItem = ItemManager.GetItemByName("CircuitBoard");
                Item_Base copperIngotItem = ItemManager.GetItemByName("CopperIngot");

                // Validate all items were found
                if (batteryItem == null || plasticItem == null || circuitBoardItem == null || copperIngotItem == null)
                {
                    Debug.LogError("[NightVisionGoggles] Failed to find recipe items:");
                    Debug.LogError("[NightVisionGoggles] Battery: " + (batteryItem != null ? "OK" : "MISSING"));
                    Debug.LogError("[NightVisionGoggles] Plastic: " + (plasticItem != null ? "OK" : "MISSING"));
                    Debug.LogError("[NightVisionGoggles] CircuitBoard: " + (circuitBoardItem != null ? "OK" : "MISSING"));
                    Debug.LogError("[NightVisionGoggles] CopperIngot: " + (copperIngotItem != null ? "OK" : "MISSING"));
                    Debug.LogWarning("[NightVisionGoggles] Item will be registered without recipe");
                }
                else
                {
                    // Set crafting recipe BEFORE registering
                    CostMultiple[] recipe = new CostMultiple[]
                    {
                        new CostMultiple(new Item_Base[] { batteryItem }, 1),
                        new CostMultiple(new Item_Base[] { plasticItem }, 4),
                        new CostMultiple(new Item_Base[] { circuitBoardItem }, 2),
                        new CostMultiple(new Item_Base[] { copperIngotItem }, 2)
                    };
                    nightVisionItem.settings_recipe.NewCost = recipe;

                }

                // Set equipment slot type (Head) - the actual Equipment component is created
                // separately and injected into the player's PlayerEquipment after the player spawns.
                nightVisionItem.settings_equipment = new ItemInstance_Equipment(EquipSlotType.Head);

                // Create the equipment prefab (inactive, not parented to anything yet)
                equipmentPrefab = CreateEquipmentPrefab(nightVisionItem);
                UnityEngine.Object.DontDestroyOnLoad(equipmentPrefab);
                Debug.Log("[NightVisionGoggles] Equipment prefab created (pending injection into player)");

                // Register with RAPI (only if not already registered, prevents double-registration on reload)
                if (ItemManager.GetItemByName("NightVisionGoggles") == null)
                {
                    RAPI.RegisterItem(nightVisionItem);
                }
                else
                {
                    Debug.LogWarning("[NightVisionGoggles] Item already registered, skipping RAPI.RegisterItem");
                }

                // CRITICAL: Always start the equipment injection coroutine, even if the item
                // is already registered. The previous run's equipment GameObject was destroyed
                // when the game closed, but PlayerEquipment.equipment[] only has vanilla entries
                // at startup. We need to re-inject our equipment every time the mod loads.
                // The coroutine itself checks for duplicates and skips if already injected.
                if (injectCoroutine == null)
                {
                    injectCoroutine = StartCoroutine(InjectEquipmentCoroutine());

                }

                Debug.Log("[NightVisionGoggles] var item = ItemManager.GetItemByName(\"NightVisionGoggles\");");
                Debug.Log("[NightVisionGoggles] ComponentManager<Network_Player>.Value.Inventory.AddItem(item.UniqueName, 1);");

            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionGoggles] RegisterNightVisionGoggles failed: " + ex);
            }
        }

        // ========================================================================
        // ExtraSettingsAPI integration
        // ========================================================================

        public static bool IsESAKeybindActive(string keyName)
        {
            if (!ESA_Loaded || keyName == null) return false;
            return MyInput.GetButton(keyName);
        }

        public static bool IsESATogglePressed()
        {
            if (!ESA_Loaded || Instance == null || Instance.esaToggleKeyName == null)
                return Input.GetKeyDown(KeyCode.V);
            return MyInput.GetButtonDown(Instance.esaToggleKeyName);
        }

        private void LoadSettings()
        {
            if (!ESA_Loaded) return;
            esaToggleKeyName = ExtraSettingsAPI_GetKeybindName("toggleKey");
        }

        // Called by ExtraSettingsAPI when settings are loaded
        void ExtraSettingsAPI_Load()
        {
            ESA_Loaded = true;
            LoadSettings();
        }

        void ExtraSettingsAPI_SettingsOpen()
        {
            LoadSettings();
        }

        void ExtraSettingsAPI_Unload()
        {
            ESA_Loaded = false;
            esaToggleKeyName = null;
        }

        void ExtraSettingsAPI_SettingsClose()
        {
            LoadSettings();
        }

        // Stub declarations (bodies replaced at runtime by ExtraSettingsAPI)
        [MethodImpl(MethodImplOptions.NoInlining)]
        public float ExtraSettingsAPI_GetSliderValue(string settingName) => 0f;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool ExtraSettingsAPI_GetCheckboxState(string settingName) => false;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string ExtraSettingsAPI_GetKeybindName(string settingName) => null;
    }
}
