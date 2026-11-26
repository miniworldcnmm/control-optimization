using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace AndroidConsolizer.Patches
{
    /// <summary>The scroll and touch selection belong to one open chest.</summary>
    internal sealed class ChestBundleNote
    {
        internal const int ComponentId = 898;
        private const int Size = 64;
        private static readonly FieldInfo SourceItem = AccessTools.Field(typeof(ItemGrabMenu), "sourceItem");
        private static readonly FieldInfo NativeIcon = AccessTools.Field(typeof(ItemGrabMenu), "junimoNoteIcon");
        private static readonly FieldInfo SelectedSlot = AccessTools.Field(typeof(InventoryMenu), "currentlySelectedItem");

        internal readonly ItemGrabMenu Menu;
        internal readonly ClickableTextureComponent Icon;
        private readonly bool _drawIcon;
        private ClickableComponent[] _sidebar = Array.Empty<ClickableComponent>();
        private ClickableComponent _chestRow0Right;
        // Close, five sidebar buttons, and the two rectangular inventory grids.
        private readonly Rectangle[] _obstacles = new Rectangle[8];
        private Rectangle _safeArea;
        private Rectangle _position;
        private bool _drawn;
        private bool _touched;
        private InventoryMenu _touchGrid;
        private int _touchSlot;
        private int _touchTick = -1;

        private ChestBundleNote(ItemGrabMenu menu)
        {
            Menu = menu;
            Icon = NativeIcon?.GetValue(menu) as ClickableTextureComponent;
            if (Icon?.visible != true)
            {
                Icon = new ClickableTextureComponent(Rectangle.Empty, Game1.mouseCursors, new Rectangle(331, 374, 15, 14), 4f)
                {
                    hoverText = Game1.content.LoadString(@"Strings\UI:GameMenu_JunimoNote_Hover")
                };
                _drawIcon = true;
            }
            Icon.myID = ComponentId;
        }

        internal static ChestBundleNote Create(ItemGrabMenu menu)
        {
            if (ModEntry.Config?.EnableConsoleChests != true || menu.shippingBin || menu.source != ItemGrabMenu.source_chest
                || !BundleIconPulsePatches.ShouldShowJunimoNoteIcon())
                return null;
            var source = SourceItem?.GetValue(menu) as Item;
            if (IsAutoGrabber(source) || IsAutoGrabber(menu.context as Item))
                return null;
            var chest = source as Chest ?? menu.context as Chest;
            return chest?.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest
                ? new ChestBundleNote(menu) : null;
        }

        internal static bool IsAutoGrabber(Item item) => item?.QualifiedItemId == "(BC)165" || item?.Name == "Auto-Grabber";

        internal void Wire(ClickableComponent chestRow0Right, ClickableComponent sortChest, ClickableComponent fill, ClickableComponent color,
            ClickableComponent sortInventory, ClickableComponent trash)
        {
            _chestRow0Right = chestRow0Right;
            _sidebar = new[] { sortChest, fill, color, sortInventory, trash };
            UpdateLayout();
            var components = Menu.allClickableComponents;
            if (components != null)
            {
                components.RemoveAll(c => c != Icon && c?.myID == ComponentId);
                if (!components.Contains(Icon)) components.Add(Icon);
            }
        }

        internal void UpdateLayout()
        {
            var safe = Utility.getSafeArea();
            if (_sidebar.Length == 0) return;
            bool moved = _safeArea != safe;
            _safeArea = safe;
            void Obstacle(int index, Rectangle bounds)
            {
                if (_obstacles[index] != bounds) moved = true;
                _obstacles[index] = bounds;
            }
            Obstacle(0, VisibleBounds(Menu.upperRightCloseButton));
            for (int i = 0; i < _sidebar.Length; i++) Obstacle(i + 1, VisibleBounds(_sidebar[i]));
            Obstacle(6, GridBounds(Menu.ItemsToGrabMenu));
            Obstacle(7, GridBounds(Menu.inventory));
            if (moved) _position = FindPosition(safe, _obstacles);
            Icon.bounds = _position;
            bool paletteOpen = Menu.chestColorPicker?.visible == true || ItemGrabMenuPatches.ColorPickerOpen;
            Icon.visible = !_position.IsEmpty && !(paletteOpen && OverlapsPalette())
                && ModEntry.Config?.EnableConsoleChests == true && BundleIconPulsePatches.ShouldShowJunimoNoteIcon();
            UpdateNavigation(Icon.visible && !paletteOpen);
        }

        private void UpdateNavigation(bool reachable)
        {
            var close = Menu.upperRightCloseButton;
            var sortChest = _sidebar[0];
            var fill = _sidebar[1];
            var color = _sidebar[2];
            bool leftOfSort = reachable && sortChest?.visible == true && Icon.bounds.Center.X < sortChest.bounds.Center.X;
            bool fallback = reachable && !leftOfSort;

            if (_chestRow0Right != null && sortChest != null)
                _chestRow0Right.rightNeighborID = leftOfSort ? ComponentId : sortChest.myID;
            if (sortChest != null)
            {
                sortChest.leftNeighborID = leftOfSort ? ComponentId : _chestRow0Right?.myID ?? -1;
                sortChest.rightNeighborID = close?.myID ?? (fallback ? ComponentId : -1);
                if (close != null) sortChest.upNeighborID = close.myID;
            }
            if (close != null)
                close.downNeighborID = fallback ? ComponentId : (sortChest ?? fill)?.myID ?? -1;
            if (fill != null)
                fill.rightNeighborID = fallback && Icon.bounds.Center.X > fill.bounds.Center.X ? ComponentId : close?.myID ?? -1;
            if (color != null)
                color.rightNeighborID = fallback && Icon.bounds.Center.X > color.bounds.Center.X ? ComponentId : close?.myID ?? -1;

            Icon.upNeighborID = close?.myID ?? -1;
            Icon.rightNeighborID = leftOfSort ? sortChest.myID : -1;
            if (leftOfSort)
            {
                Icon.leftNeighborID = _chestRow0Right?.myID ?? -1;
                Icon.downNeighborID = (fill ?? color ?? _sidebar[3])?.myID ?? -1;
                return;
            }

            ClickableComponent nearest = null, below = null;
            foreach (var button in _sidebar)
            {
                if (button?.visible != true) continue;
                int dy = button.bounds.Center.Y - _position.Center.Y;
                if (nearest == null || Math.Abs(dy) < Math.Abs(nearest.bounds.Center.Y - _position.Center.Y))
                    nearest = button;
                if (dy > 0 && (below == null || button.bounds.Center.Y < below.bounds.Center.Y))
                    below = button;
            }
            Icon.leftNeighborID = nearest?.myID ?? -1;
            Icon.downNeighborID = (below ?? nearest)?.myID ?? -1;
        }

        private bool OverlapsPalette()
        {
            var picker = Menu.chestColorPicker;
            if (picker == null) return true;
            var panel = new Rectangle(picker.xPositionOnScreen, picker.yPositionOnScreen, picker.width, picker.height);
            if (_position.Intersects(panel)) return true;

            // The Android touch layout may expose a flat row of components for a
            // 7x3 palette. Include those visual rows even before controller navigation.
            if (Menu.allClickableComponents != null)
                foreach (var component in Menu.allClickableComponents)
                {
                    if (component.myID < 4343 || component.myID > 4363) continue;
                    var swatch = component.bounds;
                    if (!ItemGrabMenuPatches.ColorPickerOpen && component.myID == 4343)
                        swatch = new Rectangle(swatch.X, swatch.Y, swatch.Width * 7, swatch.Height * 3);
                    if (_position.Intersects(swatch)) return true;
                }
            return false;
        }

        private static Rectangle VisibleBounds(ClickableComponent component) =>
            component?.visible == true ? component.bounds : Rectangle.Empty;

        private static Rectangle GridBounds(InventoryMenu grid)
        {
            Rectangle bounds = Rectangle.Empty;
            if (grid?.inventory != null)
                foreach (var slot in grid.inventory)
                    if (slot.visible)
                        bounds = bounds.IsEmpty ? slot.bounds : Rectangle.Union(bounds, slot.bounds);
            return bounds;
        }

        // Prefer the gap between the chest grid and its organize button.
        internal static Rectangle FindPosition(Rectangle safe, Rectangle[] obstacles)
        {
            if (safe.Width < Size || safe.Height < Size) return Rectangle.Empty;
            var sortChest = obstacles[1];
            if (!sortChest.IsEmpty)
            {
                var preferred = new Rectangle(
                    Math.Clamp(sortChest.Left - 12 - Size, safe.Left, safe.Right - Size),
                    Math.Clamp(sortChest.Center.Y - Size / 2, safe.Top, safe.Bottom - Size), Size, Size);
                if (preferred.Right <= sortChest.Left && Fits(preferred, obstacles)) return preferred;

                var chestGrid = obstacles[6];
                if (!chestGrid.IsEmpty && preferred.Intersects(chestGrid) && sortChest.Left - chestGrid.Right >= Size)
                {
                    preferred.X = (chestGrid.Right + sortChest.Left - Size) / 2;
                    if (safe.Contains(preferred) && Fits(preferred, obstacles)) return preferred;
                }
            }

            var close = obstacles[0].IsEmpty ? new Rectangle(safe.Right - Size, safe.Top, Size, 0) : obstacles[0];
            int x = Math.Clamp(close.Center.X - Size / 2, safe.Left, safe.Right - Size);
            int y = Math.Clamp(close.Bottom + 12, safe.Top, safe.Bottom - Size);
            var candidate = new Rectangle(x, y, Size, Size);
            if (Fits(candidate, obstacles)) return candidate;
            candidate.X = Math.Clamp(obstacles[1].IsEmpty ? safe.Right - Size : obstacles[1].Right + 12, safe.Left, safe.Right - Size);
            if (Fits(candidate, obstacles)) return candidate;

            Rectangle best = Rectangle.Empty;
            long bestDistance = long.MaxValue;
            // Prefer a nearby vertical gap in the sidebar before using another column.
            void SearchColumn(int column)
            {
                if (column < safe.Left || column > safe.Right - Size) return;
                void TryRow(int row)
                {
                    var rect = new Rectangle(column, row, Size, Size);
                    if (!safe.Contains(rect) || !Fits(rect, obstacles)) return;
                    long distance = Math.Abs((long)row - y) + Math.Abs((long)column - x);
                    if (distance >= bestDistance) return;
                    best = rect;
                    bestDistance = distance;
                }
                TryRow(y);
                TryRow(safe.Top);
                TryRow(safe.Bottom - Size);
                foreach (var obstacle in obstacles)
                {
                    if (obstacle.IsEmpty) continue;
                    TryRow(obstacle.Bottom);
                    TryRow(obstacle.Top - Size);
                }
            }
            SearchColumn(x);
            SearchColumn(candidate.X);
            if (!best.IsEmpty) return best;
            SearchColumn(safe.Right - Size);
            SearchColumn(safe.Left);
            foreach (var obstacle in obstacles)
            {
                if (obstacle.IsEmpty) continue;
                SearchColumn(obstacle.Right);
                SearchColumn(obstacle.Left - Size);
            }
            return best;
        }

        private static bool Fits(Rectangle candidate, Rectangle[] obstacles)
        {
            foreach (var obstacle in obstacles)
                if (!obstacle.IsEmpty && candidate.Intersects(obstacle)) return false;
            return true;
        }

        internal void RecordTouch(int x, int y)
        {
            _touched = true;
            _touchTick = Game1.ticks;
            _touchGrid = Menu.ItemsToGrabMenu;
            _touchSlot = HitSlot(_touchGrid, x, y);
            if (_touchSlot >= 0) return;
            _touchGrid = Menu.inventory;
            _touchSlot = HitSlot(_touchGrid, x, y);
            if (_touchSlot < 0) _touchGrid = null;
        }

        private static int HitSlot(InventoryMenu grid, int x, int y)
        {
            var slots = grid?.inventory;
            if (slots != null)
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i].bounds.Contains(x, y)) return i;
            return -1;
        }

        private static Item ItemAt(InventoryMenu grid, int index)
        {
            var items = grid?.actualInventory;
            return items != null && index >= 0 && index < items.Count ? items[index] : null;
        }

        private static int Selection(InventoryMenu grid) =>
            grid != null && SelectedSlot?.GetValue(grid) is int index ? index : -1;

        internal Item TouchItem()
        {
            var held = Menu.heldItem ?? Game1.player?.CursorSlotItem;
            if (held != null) return held;
            if (Mouse.GetState().LeftButton == ButtonState.Pressed)
            {
                if (_touchTick != Game1.ticks) RecordTouch(Game1.getMouseX(), Game1.getMouseY());
                // A stale hoveredItem must never resurrect an empty or transferred slot.
                if (_touchGrid?.actualInventory != null) return ItemAt(_touchGrid, _touchSlot);
                return _touchGrid != null ? Menu.hoveredItem : null;
            }
            if (_touched)
                return _touchGrid != null && Selection(_touchGrid) == _touchSlot ? ItemAt(_touchGrid, _touchSlot) : null;
            var chest = ItemAt(Menu.ItemsToGrabMenu, Selection(Menu.ItemsToGrabMenu));
            var player = ItemAt(Menu.inventory, Selection(Menu.inventory));
            return chest != null && player != null ? null : chest ?? player;
        }

        internal void BeginDraw() => _drawn = false;

        internal void DrawIcon(SpriteBatch b)
        {
            if (!_drawIcon || _drawn || !Icon.visible || ModEntry.Config?.EnableConsoleChests != true) return;
            Icon.draw(b);
            _drawn = true;
        }

        internal void DrawOverlay(SpriteBatch b)
        {
            if (!Icon.visible || ModEntry.Config?.EnableConsoleChests != true) return;
            DrawIcon(b); // Fallback if neither InventoryMenu.draw overload ran.
            bool snapped = Game1.options.gamepadControls && Menu.currentlySnappedComponent == Icon;
            if (!snapped) return;
            if (Menu.heldItem == null && ItemGrabMenuPatches.SwapHeldItem == null && Game1.player?.CursorSlotItem == null)
                IClickableMenu.drawHoverText(b, Icon.hoverText, Game1.smallFont);
            if (Game1.options.hardwareCursor) return;
            var pointer = Icon.bounds.Center;
            b.Draw(Game1.mouseCursors, new Vector2(pointer.X, pointer.Y),
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44, 16, 16),
                Color.White, 0f, Vector2.Zero, 4f + Game1.dialogueButtonScale / 150f, SpriteEffects.None, 1f);
        }
    }
}
