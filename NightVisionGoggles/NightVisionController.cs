using System;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace pp.RaftMods.NightVisionGoggles
{
    /// <summary>
    /// Night vision mode enumeration.
    /// Defines the three visual modes available for night vision effect.
    /// </summary>
    public enum NightVisionMode
    {
        Standard = 0,
        Bright = 1,
        Thermal = 2
    }

    /// <summary>
    /// Controller component that manages night vision state and coordinates between components.
    /// Handles toggle and mode switch input, manages active/inactive state transitions,
    /// consumes durability over time when active, and coordinates effect and UI updates.
    /// </summary>
    public class NightVisionController : MonoBehaviour
    {
        // State fields
        private bool isActive = false;
        private NightVisionMode currentMode = NightVisionMode.Standard;
        private float durabilityTimer = 0f;
        private float diagnosticTimer = 0f;

        // Ambient light / FOV / HDR state (saved to restore on deactivation)
        private Color originalAmbientLight;
        private bool originalAmbientLightSaved = false;
        private float originalFOV = 70f;
        private bool originalFOVSaved = false;
        private float targetFOV = 70f;
        private bool originalHDR = false;
        private bool hdrOriginalSaved = false;

        // PostProcessing state (GPU shader via UnityEngine.PostProcessing v1)
        private PostProcessingBehaviour postProcessBehaviour;
        private PostProcessingProfile ppOriginalProfile;
        private PostProcessingProfile ppNVProfile;
        private ColorGradingModel.Settings originalGradingSettings;
        private bool originalGradingEnabled;
        private bool ppOriginalSaved = false;
        private bool ppWasAddedByUs = false;

        // Component references
        private NightVisionEffect effect;
        private NightVisionUI ui;
        private NightVisionGogglesEquipment equipment;
        private Network_Player player;
        private Camera playerCamera;
        private Slot_Equip equippedSlot;
        
        /// <summary>
        /// Initializes the controller with required references.
        /// Called by NightVisionGogglesEquipment.Equip() after component is added.
        /// </summary>
        public void Initialize(NightVisionGogglesEquipment equipment, Network_Player player, Camera camera, Slot_Equip slot)
        {
            this.equipment = equipment;
            this.player = player;
            this.playerCamera = camera;
            this.equippedSlot = slot;
            
        }

        /// <summary>
        /// Unity Start method called on the first frame.
        /// Creates UI components that need to be initialized immediately.
        /// </summary>
        private void Start()
        {
            try
            {
                // Create UI overlay component
                // The UI component will be created on a separate GameObject to manage the overlay
                GameObject uiObject = new GameObject("NightVisionUI");
                ui = uiObject.AddComponent<NightVisionUI>();
                
                if (ui != null)
                {
                    // Initialize UI with reference to this controller
                    ui.Initialize(this);
                }
                else
                {
                    Debug.LogError("[NightVisionController] Failed to create UI component");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionController] Start failed: " + ex);
            }
        }
        
        /// <summary>
        /// Unity OnDestroy method called when component is destroyed.
        /// Cleans up effect and UI components, restores ambient light and FOV,
        /// and resets mode to Standard.
        /// </summary>
        private void OnDestroy()
        {
            try
            {
                // Destroy effect component if active
                if (effect != null)
                {
                    Destroy(effect);
                    effect = null;
                }

                // Destroy UI component
                if (ui != null)
                {
                    ui.SetBatteryVisible(false);
                    Destroy(ui.gameObject);
                    ui = null;
                }

                // CRITICAL: Always restore ambient light, HDR, PP, and FOV on destruction
                RestoreAmbientLight();
                RestoreHDR();
                DeactivatePostProcessing();
                RestoreFOV();

                // Reset mode to Standard for next equip
                currentMode = NightVisionMode.Standard;

            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionController] OnDestroy failed: " + ex);
            }
        }
        
        /// <summary>
        /// Gets the current active state.
        /// </summary>
        public bool IsActive
        {
            get { return isActive; }
        }
        
        /// <summary>
        /// Gets the current night vision mode.
        /// </summary>
        public NightVisionMode CurrentMode
        {
            get { return currentMode; }
        }
        
        /// <summary>
        /// Gets the equipment reference.
        /// </summary>
        public NightVisionGogglesEquipment Equipment
        {
            get { return equipment; }
        }
        
        /// <summary>
        /// Unity Update method called every frame.
        /// Polls for toggle key input, mode cycle key input, and checks for water state changes.
        /// </summary>
        private void Update()
        {
            try
            {
                // Diagnostic: log every 5 seconds to confirm Update is running
                diagnosticTimer += Time.deltaTime;
                if (diagnosticTimer >= 5.0f)
                {
                    diagnosticTimer = 0f;
                    Debug.Log("[NightVisionController] Update running, isActive=" + isActive + ", equipment=" + (equipment != null) + ", slot=" + (equippedSlot != null) + ", camera=" + (playerCamera != null));
                }

                // Check for toggle key input (V default, configurable via ExtraSettingsAPI)
                if (NightVisionGoggles.IsESATogglePressed())
                {

                    // Check if input should be blocked
                    if (CanvasHelper.ActiveMenu != MenuType.None)
                    {

                        return;
                    }

                    // Toggle night vision state
                    ToggleNightVision();
                }

                // Check for water state changes while active
                if (isActive && player != null)
                {
                    // Check if player is underwater (fully submerged)
                    if (player.PersonController != null && 
                        player.PersonController.SubmersionState == UltimateWater.SubmersionState.Full)
                    {
                        // Force deactivation due to water

                        ToggleNightVision();
                        
                        // Play malfunction sound (if available)
                        if (equipment != null && equipment.powerDownSound != null)
                        {
                            AudioSource.PlayClipAtPoint(equipment.powerDownSound, player.transform.position);
                        }
                    }
                }
                
                // Update durability consumption timer while active
                if (isActive)
                {
                    durabilityTimer += Time.deltaTime;

                    // Consume every X seconds based on rate.
                    // Rate 1.0 = 1 use/sec (interval 1s). Rate 0.85 = 15% slower (interval 1.18s).
                    float interval = 1f / 0.85f;
                    if (durabilityTimer >= interval)
                    {
                        durabilityTimer = 0f;
                        ConsumeDurability();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionController] Update failed: " + ex);
            }
        }
        
        /// <summary>
        /// Toggles night vision between active and inactive states.
        /// Creates or destroys the effect component, modifies ambient light and camera FOV,
        /// and plays audio feedback.
        /// </summary>
        private void ToggleNightVision()
        {
            try
            {
                if (isActive)
                {
                    // Deactivate night vision
                    isActive = false;

                    // Destroy effect component
                    if (effect != null)
                    {
                        Destroy(effect);
                        effect = null;
                    }

                    // Restore ambient light
                    RestoreAmbientLight();

                    // Restore HDR
                    RestoreHDR();

                    // Deactivate PostProcessing (GPU shader)
                    DeactivatePostProcessing();

                    // Restore camera FOV
                    RestoreFOV();

                    // Play deactivation sound
                    if (equipment != null && equipment.deactivationSound != null && player != null)
                    {
                        AudioSource.PlayClipAtPoint(equipment.deactivationSound, player.transform.position);
                    }

                    // Display status message
                    if (ui != null)
                    {
                        ui.ShowStatusMessage("NIGHT VISION: OFF", 1.5f);
                        ui.SetBatteryVisible(false);
                    }

                }
                else
                {
                    // Check if activation is allowed
                    if (!CanActivate())
                    {

                        return;
                    }

                    // Activate night vision
                    isActive = true;

                    // Boost ambient light to actually illuminate dark areas
                    BoostAmbientLight();

                    // Enable HDR so postExposure works
                    EnableHDR();

                    // Activate PostProcessing (GPU shader: exposure + green tint)
                    ActivatePostProcessing();

                    // Adjust camera FOV to simulate goggle magnification
                    AdjustFOV();

                    // Create effect component on camera
                    if (playerCamera != null)
                    {
                        effect = playerCamera.gameObject.AddComponent<NightVisionEffect>();
                        if (effect != null)
                        {
                            effect.Initialize(this);
                            // Apply current mode to the effect
                            effect.SetMode(currentMode);
                        }
                    }

                    // Play activation sound
                    if (equipment != null && equipment.activationSound != null && player != null)
                    {
                        AudioSource.PlayClipAtPoint(equipment.activationSound, player.transform.position);
                    }

                    // Display status message
                    if (ui != null)
                    {
                        ui.ShowStatusMessage("NIGHT VISION: ON", 1.5f);
                        ui.SetBatteryVisible(true);
                    }

                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionController] ToggleNightVision failed: " + ex);
            }
        }

        /// <summary>
        /// Boosts the global ambient light so dark areas become visible.
        /// This emulates the ISO gain of real night vision devices.
        /// The original ambient light is saved for restoration on deactivation.
        /// </summary>
        private void BoostAmbientLight()
        {
            try
            {
                if (!originalAmbientLightSaved)
                {
                    originalAmbientLight = RenderSettings.ambientLight;
                    originalAmbientLightSaved = true;
                }

                // Multiply all channels by 4x to brighten the scene.
                // The green tint is applied separately via NightVisionEffect UI overlay.
                float boost = 4f;
                Color boosted = new Color(
                    originalAmbientLight.r * boost,
                    originalAmbientLight.g * boost,
                    originalAmbientLight.b * boost,
                    1f
                );
                RenderSettings.ambientLight = boosted;
            }
            catch (Exception ex) { Debug.LogError("[NightVisionController] BoostAmbientLight: " + ex); }
        }

        /// <summary>
        /// Restores the original ambient light that was saved before BoostAmbientLight().
        /// </summary>
        private void RestoreAmbientLight()
        {
            try
            {
                if (originalAmbientLightSaved)
                {
                    RenderSettings.ambientLight = originalAmbientLight;
                    originalAmbientLightSaved = false;

                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionController] RestoreAmbientLight failed: " + ex);
            }
        }

        /// <summary>
        /// Adjusts the camera FOV to simulate goggle magnification.
        /// Night vision goggles typically have a narrower field of view due to the optics.
        /// </summary>
        private void AdjustFOV()
        {
            try
            {
                if (playerCamera == null) return;

                if (!originalFOVSaved)
                {
                    originalFOV = playerCamera.fieldOfView;
                    originalFOVSaved = true;
                }

                // Reduce FOV by ~25% to simulate goggle magnification
                // Smaller FOV = more zoomed in (like binoculars)
                targetFOV = originalFOV * 0.75f;
                playerCamera.fieldOfView = targetFOV;

            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionController] AdjustFOV failed: " + ex);
            }
        }

        /// <summary>
        /// Restores the original camera FOV that was saved before AdjustFOV().
        /// </summary>
        private void RestoreFOV()
        {
            try
            {
                if (originalFOVSaved && playerCamera != null)
                {
                    playerCamera.fieldOfView = originalFOV;
                    originalFOVSaved = false;

                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionController] RestoreFOV failed: " + ex);
            }
        }

        /// <summary>
        /// Enables HDR on the camera. PostExposure in ColorGrading only works
        /// when the camera renders to an HDR buffer. Without HDR, all values
        /// are clamped to [0,1] before post-processing and exposure has no effect.
        /// </summary>
        private void EnableHDR()
        {
            try
            {
                if (playerCamera == null) return;
                if (!hdrOriginalSaved)
                {
                    originalHDR = playerCamera.allowHDR;
                    hdrOriginalSaved = true;
                }
                playerCamera.allowHDR = true;
            }
            catch (Exception ex) { Debug.LogError("[NightVisionController] EnableHDR: " + ex); }
        }

        private void RestoreHDR()
        {
            try
            {
                if (playerCamera != null && hdrOriginalSaved)
                {
                    playerCamera.allowHDR = originalHDR;
                    hdrOriginalSaved = false;
                }
            }
            catch (Exception ex) { Debug.LogError("[NightVisionController] RestoreHDR: " + ex); }
        }

        // ========================================================================
        // POST PROCESSING (GPU shader via UnityEngine.PostProcessing v1)
        // ========================================================================

        private void ActivatePostProcessing()
        {
            try
            {
                if (playerCamera == null) return;

                postProcessBehaviour = playerCamera.GetComponent<PostProcessingBehaviour>();
                if (postProcessBehaviour == null)
                {
                    postProcessBehaviour = playerCamera.gameObject.AddComponent<PostProcessingBehaviour>();
                    ppWasAddedByUs = true;
                }

                // Save original profile ONCE
                if (!ppOriginalSaved)
                {
                    ppOriginalProfile = postProcessBehaviour.profile;
                    if (ppOriginalProfile != null)
                    {
                        originalGradingSettings = ppOriginalProfile.colorGrading.settings;
                        originalGradingEnabled = ppOriginalProfile.colorGrading.enabled;
                    }
                    ppOriginalSaved = true;
                }

                // Create or reuse NV profile (clone of original or fresh)
                if (ppNVProfile == null)
                {
                    ppNVProfile = ppOriginalProfile != null
                        ? ScriptableObject.Instantiate(ppOriginalProfile)
                        : ScriptableObject.CreateInstance<PostProcessingProfile>();
                    ppNVProfile.hideFlags = HideFlags.HideAndDontSave;
                }

                // Set NV color grading on the cloned profile
                var s = ppNVProfile.colorGrading.settings;
                s.basic.postExposure = 3.35f;
                s.basic.temperature = 0f;
                s.basic.tint = -100f;
                s.basic.saturation = 0f;
                s.basic.contrast = 1.3f;
                ppNVProfile.colorGrading.settings = s;
                ppNVProfile.colorGrading.enabled = true;

                // SWAP profile — this triggers ResetHistory() which forces PP to re-read all settings
                postProcessBehaviour.profile = ppNVProfile;
            }
            catch (Exception ex) { Debug.LogError("[NightVisionController] ActivatePP: " + ex); }
        }

        private void DeactivatePostProcessing()
        {
            try
            {
                if (postProcessBehaviour == null) return;
                if (!ppOriginalSaved) return;

                if (ppOriginalProfile != null)
                {
                    // Restore original profile with original settings
                    ppOriginalProfile.colorGrading.settings = originalGradingSettings;
                    ppOriginalProfile.colorGrading.enabled = originalGradingEnabled;
                    postProcessBehaviour.profile = ppOriginalProfile;
                }
                else
                {
                    // Camera had no profile originally — remove ours
                    postProcessBehaviour.profile = null;
                    if (ppWasAddedByUs)
                    {
                        Destroy(postProcessBehaviour);
                        postProcessBehaviour = null;
                        ppWasAddedByUs = false;
                    }
                }

                ppOriginalSaved = false;
            }
            catch (Exception ex) { Debug.LogError("[NightVisionController] DeactivatePP: " + ex); }
        }

        /// <summary>
        /// Validates that goggles are equipped, player is not underwater, and has durability remaining.
        /// </summary>
        /// <returns>True if activation is allowed, false otherwise</returns>
        private bool CanActivate()
        {
            // Check if equipment is still equipped
            if (equipment == null || equippedSlot == null)
            {

                return false;
            }
            
            // Check if player is underwater
            if (player != null && player.PersonController != null && 
                player.PersonController.SubmersionState == UltimateWater.SubmersionState.Full)
            {

                return false;
            }
            
            // Check if durability is available
            float durabilityPercent = equipment.GetDurabilityPercent();
            if (durabilityPercent <= 0f)
            {

                // Play power-down sound to indicate battery is depleted
                if (equipment.powerDownSound != null && player != null)
                {
                    AudioSource.PlayClipAtPoint(equipment.powerDownSound, player.transform.position);
                }
                
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Consumes durability from the equipped goggles.
        /// Called once per second while night vision is active.
        /// Automatically deactivates if durability reaches zero.
        /// </summary>
        private void ConsumeDurability()
        {
            try
            {
                if (equippedSlot == null || equippedSlot.itemInstance == null)
                {
                    return;
                }

                // Decrement Uses directly: 1 use per consumption tick
                int newUses = Mathf.Max(0, equippedSlot.itemInstance.Uses - 1);
                equippedSlot.itemInstance.Uses = newUses;

                // Update the slot UI (durability bar)
                equippedSlot.RefreshComponents();

                // Check if durability is depleted
                float durabilityPercent = equipment.GetDurabilityPercent();
                if (durabilityPercent <= 0f)
                {

                    // Force deactivation
                    ToggleNightVision();

                    // Play power-down sound
                    if (equipment != null && equipment.powerDownSound != null && player != null)
                    {
                        AudioSource.PlayClipAtPoint(equipment.powerDownSound, player.transform.position);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionController] ConsumeDurability failed: " + ex);
            }
        }
    }
}
