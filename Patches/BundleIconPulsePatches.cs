using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace AndroidConsolizer.Patches
{
    /// <summary>
    /// Restores Community Center bundle icon pulsing (junimoNoteIcon) when navigating
    /// Inventory (GameMenu / InventoryPage) and Chests (ItemGrabMenu) with a controller.
    ///
    /// Root cause on Android:
    /// - Game1.Update resets GameMenu.bundleItemHovered = false every tick.
    /// - On PC, Game1.updateActiveMenu calls activeClickableMenu.performHoverAction()
    ///   every frame, which calls InventoryMenu.hover() -> CommunityCenter.couldThisIngredienteBeUsedInABundle()
    ///   -> sets GameMenu.bundleItemHovered = true and drives the sine-wave scale pulse
    ///   on junimoNoteIcon via junimoNotePulser.
    /// - On Android, touch mode triggers bundle detection on press/hold (receiveLeftClick /
    ///   leftClickHeld). With AndroidConsolizer active, controller navigation intercepts
    ///   those touch-sim presses for instant console pickup/transfer, InventoryManagementPatches
    ///   only calls performHoverAction once on slot change (leaving junimoNotePulser at 0 ->
    ///   sin(0) = 0 before Game1.Update clears bundleItemHovered on the next tick), and
    ///   ItemGrabMenuPatches never calls performHoverAction at all.
    ///
    /// Fix:
    /// - In controller mode (Game1.options.gamepadControls), evaluate the item at the
    ///   currently snapped inventory/chest slot (or cursor/swap held item when over an
    ///   empty slot) on GameMenu and ItemGrabMenu update/draw prefixes.
    /// - Query vanilla CommunityCenter.couldThisIngredienteBeUsedInABundle(Object),
    ///   write GameMenu.bundleItemHovered, sync junimoNotePulser if present, and drive
    ///   the vanilla sine-wave scale pulse on any active junimoNoteIcon (resolved via
    ///   reflection across GameMenu, InventoryPage, and ItemGrabMenu for Android/PC safety).
    /// - Touch mode (!gamepadControls) is left completely untouched.
    /// </summary>
    internal static class BundleIconPulsePatches
    {
        private static IMonitor Monitor;

        // Cached reflection fields (Android-vs-PC safe; GameMenu.junimoNoteIcon is Android-only,
        // while InventoryPage.junimoNoteIcon is PC-side and may be absent/null on Android).
        private static FieldInfo _bundleItemHoveredField;
        private static FieldInfo _gameMenuJunimoNoteIconField;
        private static FieldInfo _gameMenuJunimoNotePulserField;
        private static FieldInfo _invPageJunimoNoteIconField;
        private static FieldInfo _invPageJunimoNotePulserField;
        private static FieldInfo _itemGrabJunimoNoteIconField;
        private static FieldInfo _itemGrabJunimoNotePulserField;

        // Pulse animation state (mirrors vanilla junimoNotePulser: milliseconds accumulated while pulsing)
        private static int _pulserMs;
        private static int _lastPulseTick = -1;
        private static bool _wasPulsing;

        // Per-tick evaluation cache so Update_Prefix and Draw_Prefix don't duplicate bundle lookups
        private static int _evalTick = -1;
        private static IClickableMenu _evalMenu;
        private static bool _evalNeeded;
        private static Item _evalItem;

        // Remember each icon's initial resting scale in case Android leaves baseScale == 0
        private static readonly ConditionalWeakTable<ClickableTextureComponent, RestingScaleBox> _restingScales = new();

        // Edge-debounced VerboseLogging diagnostics
        private static string _lastDiagKey;
        private static IClickableMenu _lastInspectedMenu;

        private sealed class RestingScaleBox
        {
            public float Scale;
            public RestingScaleBox(float scale) => Scale = scale;
        }

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            try
            {
                _bundleItemHoveredField = AccessTools.Field(typeof(GameMenu), "bundleItemHovered");
                _gameMenuJunimoNoteIconField = AccessTools.Field(typeof(GameMenu), "junimoNoteIcon");
                _gameMenuJunimoNotePulserField = AccessTools.Field(typeof(GameMenu), "junimoNotePulser");
                _invPageJunimoNoteIconField = AccessTools.Field(typeof(InventoryPage), "junimoNoteIcon");
                _invPageJunimoNotePulserField = AccessTools.Field(typeof(InventoryPage), "junimoNotePulser");
                _itemGrabJunimoNoteIconField = AccessTools.Field(typeof(ItemGrabMenu), "junimoNoteIcon");
                _itemGrabJunimoNotePulserField = AccessTools.Field(typeof(ItemGrabMenu), "junimoNotePulser");

                var gameMenuUpdate = AccessTools.DeclaredMethod(typeof(GameMenu), nameof(GameMenu.update), new[] { typeof(GameTime) });
                if (gameMenuUpdate != null)
                {
                    harmony.Patch(
                        original: gameMenuUpdate,
                        prefix: new HarmonyMethod(typeof(BundleIconPulsePatches), nameof(GameMenu_Update_Prefix))
                    );
                }

                var gameMenuDraw = AccessTools.DeclaredMethod(typeof(GameMenu), nameof(GameMenu.draw), new[] { typeof(SpriteBatch) });
                if (gameMenuDraw != null)
                {
                    harmony.Patch(
                        original: gameMenuDraw,
                        prefix: new HarmonyMethod(typeof(BundleIconPulsePatches), nameof(GameMenu_Draw_Prefix))
                    );
                }

                var itemGrabUpdate = AccessTools.DeclaredMethod(typeof(ItemGrabMenu), nameof(ItemGrabMenu.update), new[] { typeof(GameTime) });
                if (itemGrabUpdate != null)
                {
                    harmony.Patch(
                        original: itemGrabUpdate,
                        prefix: new HarmonyMethod(typeof(BundleIconPulsePatches), nameof(ItemGrabMenu_Update_Prefix))
                    );
                }

                var itemGrabDraw = AccessTools.DeclaredMethod(typeof(ItemGrabMenu), nameof(ItemGrabMenu.draw), new[] { typeof(SpriteBatch) });
                if (itemGrabDraw != null)
                {
                    harmony.Patch(
                        original: itemGrabDraw,
                        prefix: new HarmonyMethod(typeof(BundleIconPulsePatches), nameof(ItemGrabMenu_Draw_Prefix))
                    );
                }

                Monitor.Log("BundleIconPulse patches applied successfully.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to apply BundleIconPulse patches: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Reset animation/cache state on menu transitions and ensure bundle ingredient data is fresh for chests.</summary>
        public static void OnMenuChanged(IClickableMenu oldMenu, IClickableMenu newMenu)
        {
            try
            {
                if (_wasPulsing)
                {
                    if (oldMenu is GameMenu oldGm)
                        RestoreGameMenuIcons(oldGm);
                    else if (oldMenu is ItemGrabMenu oldGrab)
                        RestoreItemGrabIcons(oldGrab);
                }

                _pulserMs = 0;
                _lastPulseTick = -1;
                _wasPulsing = false;
                _evalTick = -1;
                _evalMenu = null;
                _evalNeeded = false;
                _evalItem = null;
                _lastDiagKey = null;
                _lastInspectedMenu = null;

                // Ensure Remixed Bundles / freshly loaded saves have up-to-date bundle ingredient
                // lookup info when opening a chest directly before opening GameMenu.
                if (newMenu is ItemGrabMenu && ShouldShowJunimoNoteIcon())
                {
                    var cc = Game1.getLocationFromName("CommunityCenter") as CommunityCenter;
                    cc?.refreshBundlesIngredientsInfo();
                }
            }
            catch (Exception ex)
            {
                if (ModEntry.Config?.VerboseLogging == true)
                    Monitor?.Log($"[BundlePulse] OnMenuChanged error: {ex.Message}", LogLevel.Debug);
            }
        }

        // ===== Harmony prefixes =====

        private static void GameMenu_Update_Prefix(GameMenu __instance)
        {
            try
            {
                ProcessGameMenu(__instance, applyScale: false);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config?.VerboseLogging == true)
                    Monitor?.Log($"[BundlePulse] GameMenu_Update_Prefix error: {ex.Message}", LogLevel.Debug);
            }
        }

        private static void GameMenu_Draw_Prefix(GameMenu __instance)
        {
            try
            {
                ProcessGameMenu(__instance, applyScale: true);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config?.VerboseLogging == true)
                    Monitor?.Log($"[BundlePulse] GameMenu_Draw_Prefix error: {ex.Message}", LogLevel.Debug);
            }
        }

        private static void ItemGrabMenu_Update_Prefix(ItemGrabMenu __instance)
        {
            try
            {
                ProcessItemGrabMenu(__instance, applyScale: false);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config?.VerboseLogging == true)
                    Monitor?.Log($"[BundlePulse] ItemGrabMenu_Update_Prefix error: {ex.Message}", LogLevel.Debug);
            }
        }

        private static void ItemGrabMenu_Draw_Prefix(ItemGrabMenu __instance)
        {
            try
            {
                ProcessItemGrabMenu(__instance, applyScale: true);
            }
            catch (Exception ex)
            {
                if (ModEntry.Config?.VerboseLogging == true)
                    Monitor?.Log($"[BundlePulse] ItemGrabMenu_Draw_Prefix error: {ex.Message}", LogLevel.Debug);
            }
        }

        // ===== Core menu processing =====

        private static void ProcessGameMenu(GameMenu menu, bool applyScale)
        {
            if (menu == null)
                return;

            // Leave vanilla touch behavior untouched when not in controller mode
            if (!Game1.options.gamepadControls)
            {
                if (_wasPulsing)
                {
                    RestoreGameMenuIcons(menu);
                    ClearPulseState();
                }
                return;
            }

            // If a child menu (e.g., GMCM) is active over GameMenu, stop pulsing
            if (menu.GetChildMenu() != null)
            {
                if (_wasPulsing)
                {
                    RestoreGameMenuIcons(menu);
                    ClearPulseState();
                }
                return;
            }

            InventoryPage invPage = menu.currentTab == GameMenu.inventoryTab
                ? menu.GetCurrentPage() as InventoryPage
                : null;

            bool needed = EvaluateMenu(menu, () => GetInventoryPageHoveredItem(invPage), out Item hoveredItem);

            SetBundleItemHovered(needed);

            var gmIcon = _gameMenuJunimoNoteIconField?.GetValue(menu) as ClickableTextureComponent;
            var invIcon = invPage != null
                ? _invPageJunimoNoteIconField?.GetValue(invPage) as ClickableTextureComponent
                : null;

            LogMenuInspectionIfVerbose(menu, gmIcon, invIcon);

            if (needed)
            {
                AdvancePulserOncePerTick();
                _wasPulsing = true;

                SyncPulserField(_gameMenuJunimoNotePulserField, menu, _pulserMs);
                if (invPage != null)
                    SyncPulserField(_invPageJunimoNotePulserField, invPage, _pulserMs);

                if (applyScale)
                {
                    ApplyPulseScale(gmIcon);
                    if (invIcon != null && !ReferenceEquals(invIcon, gmIcon))
                        ApplyPulseScale(invIcon);
                }
            }
            else if (_wasPulsing)
            {
                RestoreGameMenuIcons(menu);
                ClearPulseState();
            }

            LogStateTransitionIfVerbose("GameMenu", hoveredItem, needed, gmIcon ?? invIcon);
        }

        private static void ProcessItemGrabMenu(ItemGrabMenu menu, bool applyScale)
        {
            if (menu == null)
                return;

            // Leave vanilla touch behavior untouched when not in controller mode
            if (!Game1.options.gamepadControls)
            {
                if (_wasPulsing)
                {
                    RestoreItemGrabIcons(menu);
                    ClearPulseState();
                }
                return;
            }

            bool needed = EvaluateMenu(menu, () => GetItemGrabHoveredItem(menu), out Item hoveredItem);

            SetBundleItemHovered(needed);

            var grabIcon = _itemGrabJunimoNoteIconField?.GetValue(menu) as ClickableTextureComponent;

            LogMenuInspectionIfVerbose(menu, grabIcon, null);

            if (needed)
            {
                AdvancePulserOncePerTick();
                _wasPulsing = true;

                SyncPulserField(_itemGrabJunimoNotePulserField, menu, _pulserMs);

                if (applyScale)
                    ApplyPulseScale(grabIcon);
            }
            else if (_wasPulsing)
            {
                RestoreItemGrabIcons(menu);
                ClearPulseState();
            }

            LogStateTransitionIfVerbose("ItemGrabMenu", hoveredItem, needed, grabIcon);
        }

        // ===== Item resolution & vanilla bundle check =====

        private static bool EvaluateMenu(IClickableMenu menu, Func<Item> resolveItem, out Item item)
        {
            int tick = Game1.ticks;
            if (_evalTick == tick && ReferenceEquals(_evalMenu, menu))
            {
                item = _evalItem;
                return _evalNeeded;
            }

            item = resolveItem();
            bool needed = IsItemNeededForBundle(item);

            _evalTick = tick;
            _evalMenu = menu;
            _evalItem = item;
            _evalNeeded = needed;
            return needed;
        }

        private static Item GetInventoryPageHoveredItem(InventoryPage invPage)
        {
            if (invPage == null)
                return null;

            Item slotItem = null;
            var snapped = invPage.currentlySnappedComponent;
            if (snapped != null)
            {
                IList<Item> items = invPage.inventory?.actualInventory ?? Game1.player?.Items;
                var slots = invPage.inventory?.inventory;
                if (items != null)
                {
                    int idx = slots != null ? slots.IndexOf(snapped) : -1;
                    if (idx < 0 && snapped.myID >= 0 && snapped.myID < items.Count)
                        idx = snapped.myID;

                    if (idx >= 0 && idx < items.Count)
                        slotItem = items[idx];
                }
            }

            // If cursor is on an empty slot or non-item slot while holding an item on cursor,
            // keep checking the held item (helpful when dragging toward trash/drop zone).
            return slotItem ?? Game1.player?.CursorSlotItem;
        }

        private static Item GetItemGrabHoveredItem(ItemGrabMenu menu)
        {
            if (menu == null)
                return null;

            Item slotItem = null;
            var snapped = menu.currentlySnappedComponent;
            if (snapped != null)
            {
                int chestIdx = menu.ItemsToGrabMenu?.inventory?.IndexOf(snapped) ?? -1;
                if (chestIdx >= 0
                    && menu.ItemsToGrabMenu.actualInventory != null
                    && chestIdx < menu.ItemsToGrabMenu.actualInventory.Count)
                {
                    slotItem = menu.ItemsToGrabMenu.actualInventory[chestIdx];
                }
                else
                {
                    int playerIdx = menu.inventory?.inventory?.IndexOf(snapped) ?? -1;
                    IList<Item> playerItems = menu.inventory?.actualInventory ?? Game1.player?.Items;
                    if (playerIdx >= 0 && playerItems != null && playerIdx < playerItems.Count)
                    {
                        slotItem = playerItems[playerIdx];
                    }
                }
            }

            return slotItem ?? ItemGrabMenuPatches.SwapHeldItem ?? Game1.player?.CursorSlotItem;
        }

        /// <summary>
        /// Mirrors vanilla InventoryPage.ShouldShowJunimoNoteIcon() using only universal Farmer APIs
        /// (no direct reference to InventoryPage.ShouldShowJunimoNoteIcon for Android-vs-PC safety).
        /// </summary>
        private static bool ShouldShowJunimoNoteIcon()
        {
            var player = Game1.player;
            var master = Game1.MasterPlayer;
            if (player == null || master == null)
                return false;

            if (!player.hasOrWillReceiveMail("canReadJunimoText"))
                return false;
            if (player.hasOrWillReceiveMail("JojaMember"))
                return false;
            if (!master.hasCompletedCommunityCenter())
                return true;

            return player.hasOrWillReceiveMail("hasSeenAbandonedJunimoNote")
                && !master.hasOrWillReceiveMail("ccMovieTheater");
        }

        private static bool IsItemNeededForBundle(Item item)
        {
            if (item is not SObject obj)
                return false;
            if (!ShouldShowJunimoNoteIcon())
                return false;

            var cc = Game1.getLocationFromName("CommunityCenter") as CommunityCenter;
            if (cc == null)
                return false;

            return cc.couldThisIngredienteBeUsedInABundle(obj);
        }

        // ===== Pulse animation helpers =====

        private static void AdvancePulserOncePerTick()
        {
            int tick = Game1.ticks;
            if (_lastPulseTick == tick)
                return;

            _lastPulseTick = tick;
            int elapsedMs = (int)(Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 16);
            if (elapsedMs <= 0 || elapsedMs > 100)
                elapsedMs = 16;

            _pulserMs += elapsedMs;
        }

        private static void ClearPulseState()
        {
            _pulserMs = 0;
            _lastPulseTick = -1;
            _wasPulsing = false;
            SetBundleItemHovered(false);
        }

        private static float GetRestingScale(ClickableTextureComponent icon)
        {
            if (icon.baseScale > 0f)
                return icon.baseScale;

            var box = _restingScales.GetValue(icon, c => new RestingScaleBox(c.scale > 0f ? c.scale : 4f));
            return box.Scale;
        }

        private static void ApplyPulseScale(ClickableTextureComponent icon)
        {
            if (icon == null || !icon.visible)
                return;

            float baseScale = GetRestingScale(icon);
            // Exact PC vanilla formula when baseScale == 4f: baseScale + sin(pulser / 100f) / 4f
            // Scales proportionally (±6.25%) if Android uses a different baseScale.
            float amplitude = baseScale / 16f;
            icon.scale = baseScale + (float)Math.Sin(_pulserMs / 100.0) * amplitude;
        }

        private static void RestoreIconScale(ClickableTextureComponent icon)
        {
            if (icon == null)
                return;
            icon.scale = GetRestingScale(icon);
        }

        private static void RestoreGameMenuIcons(GameMenu menu)
        {
            if (menu == null)
                return;

            var gmIcon = _gameMenuJunimoNoteIconField?.GetValue(menu) as ClickableTextureComponent;
            RestoreIconScale(gmIcon);
            SyncPulserField(_gameMenuJunimoNotePulserField, menu, 0);

            if (menu.pages != null && GameMenu.inventoryTab < menu.pages.Count
                && menu.pages[GameMenu.inventoryTab] is InventoryPage invPage)
            {
                var invIcon = _invPageJunimoNoteIconField?.GetValue(invPage) as ClickableTextureComponent;
                if (invIcon != null && !ReferenceEquals(invIcon, gmIcon))
                    RestoreIconScale(invIcon);
                SyncPulserField(_invPageJunimoNotePulserField, invPage, 0);
            }
        }

        private static void RestoreItemGrabIcons(ItemGrabMenu menu)
        {
            if (menu == null)
                return;

            var grabIcon = _itemGrabJunimoNoteIconField?.GetValue(menu) as ClickableTextureComponent;
            RestoreIconScale(grabIcon);
            SyncPulserField(_itemGrabJunimoNotePulserField, menu, 0);
        }

        private static void SetBundleItemHovered(bool value)
        {
            if (_bundleItemHoveredField != null)
                _bundleItemHoveredField.SetValue(null, value);
        }

        private static void SyncPulserField(FieldInfo field, object target, int value)
        {
            if (field != null && target != null)
                field.SetValue(target, value);
        }

        // ===== Diagnostics (VerboseLogging-gated & edge-debounced) =====

        private static void LogMenuInspectionIfVerbose(IClickableMenu menu, ClickableTextureComponent primary, ClickableTextureComponent secondary)
        {
            if (ModEntry.Config?.VerboseLogging != true)
                return;
            if (ReferenceEquals(_lastInspectedMenu, menu))
                return;

            _lastInspectedMenu = menu;
            Monitor?.Log(
                $"[BundlePulse] Menu opened: {menu.GetType().Name} | " +
                $"primaryIcon={(primary != null ? $"vis={primary.visible},scale={primary.scale:F2},base={primary.baseScale:F2},bounds={primary.bounds}" : "null")} | " +
                $"secondaryIcon={(secondary != null ? $"vis={secondary.visible},scale={secondary.scale:F2},base={secondary.baseScale:F2}" : "null")} | " +
                $"fields(bundleHovered={_bundleItemHoveredField != null},gmIcon={_gameMenuJunimoNoteIconField != null},gmPulser={_gameMenuJunimoNotePulserField != null}," +
                $"invIcon={_invPageJunimoNoteIconField != null},grabIcon={_itemGrabJunimoNoteIconField != null})",
                LogLevel.Debug);
        }

        private static void LogStateTransitionIfVerbose(string menuName, Item item, bool needed, ClickableTextureComponent icon)
        {
            if (ModEntry.Config?.VerboseLogging != true)
                return;

            string key = $"{menuName}|{item?.QualifiedItemId ?? "null"}|{item?.Quality ?? -1}|{needed}|{icon != null}";
            if (key == _lastDiagKey)
                return;

            _lastDiagKey = key;
            Monitor?.Log(
                $"[BundlePulse] {menuName}: item={item?.Name ?? "(none)"} ({item?.QualifiedItemId ?? "-"}, q={item?.Quality ?? -1}) " +
                $"-> needed={needed}, icon={(icon != null ? $"vis={icon.visible},scale={icon.scale:F2}/base={icon.baseScale:F2}" : "null")}",
                LogLevel.Debug);
        }
    }
}
