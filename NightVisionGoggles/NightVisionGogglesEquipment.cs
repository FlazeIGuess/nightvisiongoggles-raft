using System;
using UnityEngine;

namespace pp.RaftMods.NightVisionGoggles
{
    /// <summary>
    /// Night Vision Goggles equipment class that integrates with Raft's equipment system.
    /// Extends Equipment base class and manages the lifecycle of night vision components.
    /// </summary>
    public class NightVisionGogglesEquipment : Equipment
    {
        // Configuration fields
        [Header("Audio Configuration")]
        [Tooltip("Sound played when night vision is activated")]
        public AudioClip activationSound;
        
        [Tooltip("Sound played when night vision is deactivated")]
        public AudioClip deactivationSound;
        
        [Tooltip("Sound played when mode is switched")]
        public AudioClip modeSwitchSound;
        
        [Tooltip("Sound played when battery is depleted")]
        public AudioClip powerDownSound;
        
        [Tooltip("Sound played for low battery warning")]
        public AudioClip warningBeepSound;
        
        [Header("Durability Configuration")]
        [Tooltip("Durability consumption rate per second while active")]
        public float durabilityConsumptionRate = 0.85f;
        
        // Component references
        private GameObject controllerObject;
        private NightVisionController controller;
        private Network_Player localPlayer;
        private Camera playerCamera;
        
        /// <summary>
        /// Called when the equipment is equipped in a slot.
        /// Initializes the night vision system by getting player and camera references,
        /// then creates the controller component on a separate GameObject to avoid interfering with camera components.
        /// </summary>
        /// <param name="equippedSlot">The equipment slot this item is equipped in</param>
        public override void Equip(Slot_Equip equippedSlot)
        {
            base.Equip(equippedSlot);

            Debug.Log("[NightVisionGoggles] Equip() called - slot: " + (equippedSlot != null ? equippedSlot.name : "null"));

            try
            {
                // Get local Network_Player reference
                localPlayer = ComponentManager<Network_Player>.Value;

                if (localPlayer == null)
                {
                    Debug.LogError("[NightVisionGoggles] Failed to get local Network_Player reference");
                    return;
                }

                // Get player camera reference
                playerCamera = localPlayer.Camera;

                if (playerCamera == null)
                {
                    Debug.LogError("[NightVisionGoggles] Failed to get player camera reference");
                    return;
                }

                // Create a separate GameObject for the controller to avoid interfering with camera components like MouseLook
                controllerObject = new GameObject("NightVisionController");
                controllerObject.transform.SetParent(playerCamera.transform, false);

                // Add NightVisionController component to the separate GameObject
                controller = controllerObject.AddComponent<NightVisionController>();

                if (controller != null)
                {
                    // Initialize controller with references
                    controller.Initialize(this, localPlayer, playerCamera, equippedSlot);

                }
                else
                {
                    Debug.LogError("[NightVisionGoggles] Failed to create NightVisionController component");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionGoggles] Equip failed: " + ex);
            }
        }
        
        /// <summary>
        /// Called when the equipment is unequipped from a slot.
        /// Cleans up all night vision components and references.
        /// </summary>
        public override void UnEquip()
        {
            try
            {
                // Destroy controller GameObject (which will clean up controller, effect and UI)
                if (controllerObject != null)
                {
                    Destroy(controllerObject);
                    controllerObject = null;
                }
                
                controller = null;
                
                // Clear references
                localPlayer = null;
                playerCamera = null;

            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionGoggles] UnEquip failed: " + ex);
            }
            
            base.UnEquip();
        }
        
        /// <summary>
        /// Gets the current durability as a percentage (0.0 to 1.0).
        /// Used by UI to display battery indicator.
        /// Note: In Raft, ItemInstance.Uses represents "uses REMAINING" (not consumed).
        /// A fresh item has Uses == MaxUses. As the item is used, Uses decreases toward 0.
        /// </summary>
        /// <returns>Durability percentage (0.0 = empty, 1.0 = full), or 0 if slot is null</returns>
        public float GetDurabilityPercent()
        {
            if (equippedSlot == null || equippedSlot.itemInstance == null)
            {
                return 0f;
            }

            ItemInstance itemInstance = equippedSlot.itemInstance;
            if (itemInstance == null || itemInstance.baseItem == null)
            {
                return 0f;
            }

            Item_Base item = itemInstance.baseItem;
            if (item.MaxUses <= 0)
            {
                return 0f;
            }

            // Uses IS already "remaining", so durability is simply Uses / MaxUses
            return (float)itemInstance.Uses / item.MaxUses;
        }
    }
}
