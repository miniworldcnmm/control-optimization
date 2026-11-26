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
    /// <summary>Drive the vanilla scroll animation from the selected inventory item.</summary>
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
        private static ChestBundleNote _chest;
        private static readonly Dictionary<int, bool[]> Progress = new();
        private static int _progressTick = -1;
        private static string _lastError;
        internal static ChestBundleNote ForChest(ItemGrabMenu menu) => _chest?.Menu == menu ? _chest : null;
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

                // Layer the self-hosted scroll with the inventory controls, below native
                // tooltips and the pointer. Patch both 1-arg (PC) and 4-arg (Android) draw
                // overloads; ChestBundleNote._drawn ensures the icon renders at most once per frame.
                var inventoryDraw1 = AccessTools.DeclaredMethod(typeof(InventoryMenu), nameof(InventoryMenu.draw),
                    new[] { typeof(SpriteBatch) });
                if (inventoryDraw1 != null)
                    harmony.Patch(inventoryDraw1, postfix: new HarmonyMethod(typeof(BundleIconPulsePatches), nameof(InventoryDraw_Postfix)));

                var inventoryDraw4 = AccessTools.DeclaredMethod(typeof(InventoryMenu), nameof(InventoryMenu.draw),
                    new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(int) });
                if (inventoryDraw4 != null)
                    harmony.Patch(inventoryDraw4, postfix: new HarmonyMethod(typeof(BundleIconPulsePatches), nameof(InventoryDraw_Postfix)));

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

                _chest = null;
                _lastError = null;
                Progress.Clear();
                _progressTick = -1;
                if (newMenu is ItemGrabMenu grab)
                    _chest = ChestBundleNote.Create(grab);
                if (_chest != null || newMenu is GameMenu && ShouldShowJunimoNoteIcon())
                {
                    (Game1.getLocationFromName("CommunityCenter") as CommunityCenter)?.refreshBundlesIngredientsInfo();
                    if (_chest != null) SnapshotProgress();
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(OnMenuChanged), ex);
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
                LogError(nameof(GameMenu_Update_Prefix), ex);
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
                LogError(nameof(GameMenu_Draw_Prefix), ex);
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
                LogError(nameof(ItemGrabMenu_Update_Prefix), ex);
            }
        }

        private static void InventoryDraw_Postfix(InventoryMenu __instance, SpriteBatch b)
        {
            if (_chest != null && Game1.activeClickableMenu == _chest.Menu && __instance == _chest.Menu.ItemsToGrabMenu)
                _chest.DrawIcon(b);
        }

        private static void ItemGrabMenu_Draw_Prefix(ItemGrabMenu __instance)
        {
            try
            {
                ForChest(__instance)?.BeginDraw();
                ProcessItemGrabMenu(__instance, applyScale: true);
            }
            catch (Exception ex)
            {
                LogError(nameof(ItemGrabMenu_Draw_Prefix), ex);
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

            Item hoveredItem = GetInventoryPageHoveredItem(invPage);
            bool needed = EvaluateMenu(menu, hoveredItem);

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
            var note = ForChest(menu);
            if (note == null || ModEntry.Config?.EnableConsoleChests != true)
                return;
            if (_progressTick != Game1.ticks)
            {
                _progressTick = Game1.ticks;
                note.UpdateLayout();
                if (ProgressChanged())
                {
                    (Game1.getLocationFromName("CommunityCenter") as CommunityCenter)?.refreshBundlesIngredientsInfo();
                    SnapshotProgress();
                    _evalTick = -1;
                }
            }
            var hoveredItem = Game1.options.gamepadControls ? GetItemGrabHoveredItem(menu) : note.TouchItem();
            bool needed = note.Icon.visible && EvaluateMenu(menu, hoveredItem);
            SetBundleItemHovered(needed);
            var grabIcon = note.Icon;

            LogMenuInspectionIfVerbose(menu, grabIcon, null);

            if (needed)
            {
                AdvancePulserOncePerTick();
                _wasPulsing = true;

                SyncPulserField(_itemGrabJunimoNotePulserField, menu, _pulserMs);

                if (applyScale)
                    ApplyPulseScale(grabIcon);
            }
            else
            {
                if (_wasPulsing)
                {
                    RestoreItemGrabIcons(menu);
                    ClearPulseState();
                }
                else if (applyScale)
                    RestoreIconScale(grabIcon);
            }

            LogStateTransitionIfVerbose("ItemGrabMenu", hoveredItem, needed, grabIcon);
        }

        // ===== Item resolution & vanilla bundle check =====

        private static bool EvaluateMenu(IClickableMenu menu, Item item)
        {
            int tick = Game1.ticks;
            if (_evalTick == tick && ReferenceEquals(_evalMenu, menu) && ReferenceEquals(item, _evalItem))
            {
                return _evalNeeded;
            }

            bool needed = IsItemNeededForBundle(item);

            _evalTick = tick;
            _evalMenu = menu;
            _evalItem = item;
            _evalNeeded = needed;
            return needed;
        }

        // NetBundles' value indexer copies bool arrays. FieldDict exposes the live NetArrays
        // and its concrete dictionary enumerator allocates nothing on unchanged ticks.
        private static bool ProgressChanged()
        {
            var fields = Game1.netWorldState.Value.Bundles.FieldDict;
            if (fields.Count != Progress.Count) return true;
            foreach (var pair in fields)
            {
                if (!Progress.TryGetValue(pair.Key, out var previous) || previous.Length != pair.Value.Count)
                    return true;
                for (int i = 0; i < previous.Length; i++)
                    if (previous[i] != pair.Value[i]) return true;
            }
            return false;
        }

        private static void SnapshotProgress()
        {
            Progress.Clear();
            foreach (var pair in Game1.netWorldState.Value.Bundles.FieldDict)
            {
                var snapshot = new bool[pair.Value.Count];
                for (int i = 0; i < snapshot.Length; i++) snapshot[i] = pair.Value[i];
                Progress.Add(pair.Key, snapshot);
            }
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

            return slotItem ?? ItemGrabMenuPatches.SwapHeldItem ?? Game1.player?.CursorSlotItem ?? menu.heldItem;
        }

        /// <summary>
        /// Mirrors vanilla InventoryPage.ShouldShowJunimoNoteIcon() using only universal Farmer APIs
        /// (no direct reference to InventoryPage.ShouldShowJunimoNoteIcon for Android-vs-PC safety).
        /// </summary>
        internal static bool ShouldShowJunimoNoteIcon()
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

            var grabIcon = ForChest(menu)?.Icon;
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

        private static void LogError(string callback, Exception ex)
        {
            if (ModEntry.Config?.VerboseLogging != true || _lastError == ex.Message) return;
            _lastError = ex.Message;
            Monitor?.Log($"[BundlePulse] {callback}: {ex.Message}", LogLevel.Debug);
        }

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
                $"invIcon={_invPageJunimoNoteIconField != null})",
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
