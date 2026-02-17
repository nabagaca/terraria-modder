using System;
using System.Collections.Generic;
using TerrariaModder.Core.UI;
using StorageHub.Storage;
using TerrariaModder.Core.UI.Widgets;

namespace StorageHub.UI.Components
{
    /// <summary>
    /// Grid component for displaying items in a scrollable grid.
    ///
    /// NOTE: Currently uses text-based rendering. Item texture rendering will be
    /// added in a later phase once the core functionality is working.
    ///
    /// Design decisions:
    /// - Virtual scrolling: Only renders visible items for performance
    /// - Snapshot-based: Works with ItemSnapshot copies, never real Item objects
    /// - Event-driven: Calls callbacks on click, doesn't modify items directly
    /// </summary>
    public class ItemSlotGrid
    {
        // Grid layout
        private const int SlotSize = 44;      // Terraria item slot size
        private const int SlotPadding = 4;
        private const int SlotInner = SlotSize - SlotPadding * 2;

        // Cached layout
        private int _x, _y, _width, _height;
        private int _columns;

        // Right-click hold for rapid pickup
        private int _rightClickHoldIndex = -1;
        private int _rightClickHoldItemId = -1; // Track item ID to detect scroll changes
        private int _rightClickHoldTicks = 0;
        private const int RapidPickupStartTicks = 15; // Wait 15 frames before rapid mode
        private const int RapidPickupInterval = 3;    // Pick up every 3 frames

        /// <summary>
        /// Calculate the number of columns that fit in the given width.
        /// </summary>
        public int CalculateColumns(int width)
        {
            return Math.Max(1, width / SlotSize);
        }

        /// <summary>
        /// Calculate the total height needed for a given number of items.
        /// </summary>
        public int CalculateTotalHeight(int itemCount, int columns)
        {
            int rows = (itemCount + columns - 1) / columns;
            return rows * SlotSize;
        }

        /// <summary>
        /// Get the slot size (for scroll calculations).
        /// </summary>
        public static int GetSlotSize() => SlotSize;

        /// <summary>
        /// Draw the item grid.
        /// </summary>
        /// <param name="items">List of items to display.</param>
        /// <param name="x">Left position.</param>
        /// <param name="y">Top position.</param>
        /// <param name="width">Available width.</param>
        /// <param name="height">Available height.</param>
        /// <param name="scrollOffset">Current scroll offset in pixels.</param>
        /// <param name="onItemClick">Callback when an item is clicked. Receives (item, index, isRightClick, isShiftHeld, isPingMode).</param>
        /// <param name="favoriteItems">Set of favorited item IDs for star display.</param>
        /// <param name="onToggleFavorite">Callback when favorite is toggled (middle-click). Receives (item, index).</param>
        /// <param name="pingMode">If true, left-click pings chest location instead of taking item.</param>
        /// <returns>Index of hovered item, or -1 if none.</returns>
        public int Draw(
            IReadOnlyList<ItemSnapshot> items,
            int x, int y, int width, int height,
            int scrollOffset,
            Action<ItemSnapshot, int, bool, bool, bool> onItemClick,
            HashSet<int> favoriteItems = null,
            Action<ItemSnapshot, int> onToggleFavorite = null,
            bool pingMode = false)
        {
            _x = x;
            _y = y;
            _width = width;
            _height = height;
            _columns = CalculateColumns(width);

            int hoveredIndex = -1;

            // Calculate visible range - only draw items that fit fully within the view
            // This approach matches ItemSpawner and avoids relying on GPU scissor clipping
            int visibleRows = height / SlotSize;
            int startRow = scrollOffset / SlotSize;
            int startIndex = startRow * _columns;

            // Draw visible items - only items that fit fully within the view area
            for (int i = 0; i < visibleRows * _columns && startIndex + i < items.Count; i++)
            {
                int itemIndex = startIndex + i;
                if (itemIndex < 0 || itemIndex >= items.Count) continue;

                int row = i / _columns;
                int col = i % _columns;

                int slotX = x + col * SlotSize;
                int slotY = y + row * SlotSize;

                var item = items[itemIndex];

                // Only consider hovered if mouse is within the scroll area bounds
                bool isInScrollBounds = WidgetInput.IsMouseOver(x, y, width, height);
                bool isHovered = isInScrollBounds && WidgetInput.IsMouseOver(slotX, slotY, SlotSize - 2, SlotSize - 2);
                bool isFavorite = favoriteItems?.Contains(item.ItemId) ?? false;

                if (isHovered) hoveredIndex = itemIndex;

                DrawSlot(slotX, slotY, item, isHovered, isFavorite, pingMode);

                // Handle click
                if (isHovered)
                {
                    if (WidgetInput.MouseLeftClick)
                    {
                        onItemClick?.Invoke(item, itemIndex, false, WidgetInput.IsShiftHeld, pingMode);
                        WidgetInput.ConsumeClick();
                    }
                    else if (WidgetInput.MouseRightClick)
                    {
                        // First click - record both index and item ID
                        _rightClickHoldIndex = itemIndex;
                        _rightClickHoldItemId = item.ItemId;
                        _rightClickHoldTicks = 0;
                        onItemClick?.Invoke(item, itemIndex, true, WidgetInput.IsShiftHeld, pingMode);
                        WidgetInput.ConsumeRightClick();
                    }
                    else if (WidgetInput.MouseRight && _rightClickHoldIndex == itemIndex && _rightClickHoldItemId == item.ItemId)
                    {
                        // Holding right click on same item (verify by both index AND item ID)
                        _rightClickHoldTicks++;
                        if (_rightClickHoldTicks >= RapidPickupStartTicks)
                        {
                            // Rapid pickup mode
                            if ((_rightClickHoldTicks - RapidPickupStartTicks) % RapidPickupInterval == 0)
                            {
                                onItemClick?.Invoke(item, itemIndex, true, WidgetInput.IsShiftHeld, pingMode);
                            }
                        }
                    }
                    else if (WidgetInput.MouseRight && _rightClickHoldIndex == itemIndex && _rightClickHoldItemId != item.ItemId)
                    {
                        // Item at this position changed (scrolled) - reset hold state
                        _rightClickHoldIndex = -1;
                        _rightClickHoldItemId = -1;
                        _rightClickHoldTicks = 0;
                    }
                    else if (WidgetInput.MouseMiddleClick)
                    {
                        onToggleFavorite?.Invoke(item, itemIndex);
                        WidgetInput.ConsumeMiddleClick();
                    }
                }
            }

            // Reset hold state when not holding right click (MUST be outside loop)
            if (!WidgetInput.MouseRight && _rightClickHoldIndex >= 0)
            {
                _rightClickHoldIndex = -1;
                _rightClickHoldItemId = -1;
                _rightClickHoldTicks = 0;
            }

            return hoveredIndex;
        }

        private void DrawSlot(int x, int y, ItemSnapshot item, bool isHovered, bool isFavorite, bool pingMode = false)
        {
            // Slot background - tint with accent in ping mode
            Color4 slotBg;
            if (pingMode && isHovered)
                slotBg = UIColors.Accent.WithAlpha(180);
            else if (isHovered)
                slotBg = UIColors.ItemHoverBg;
            else if (pingMode)
                slotBg = UIColors.Accent.WithAlpha(100);
            else
                slotBg = UIColors.ItemBg;
            UIRenderer.DrawRect(x, y, SlotSize - 2, SlotSize - 2, slotBg);

            // Border
            if (isHovered)
            {
                UIRenderer.DrawRectOutline(x, y, SlotSize - 2, SlotSize - 2, UIColors.Accent, 1);
            }

            if (!item.IsEmpty)
            {
                // Draw actual item texture
                int iconSize = SlotInner - 4;
                int iconX = x + SlotPadding + 2;
                int iconY = y + SlotPadding + 2;
                UIRenderer.DrawItem(item.ItemId, iconX, iconY, iconSize, iconSize);

                // Stack count (bottom right, with shadow for readability)
                if (item.Stack > 1)
                {
                    // Show full stack count up to 9999, then "9999+" for larger stacks
                    string stackText = item.Stack > 9999 ? "9999+" : item.Stack.ToString();
                    // Right-align text using measured width
                    int stackTextW = TextUtil.MeasureWidth(stackText);
                    int stackTextX = x + SlotSize - stackTextW - 4;
                    // Draw shadow then text
                    UIRenderer.DrawText(stackText, stackTextX, y + SlotSize - 17, 0, 0, 0);
                    UIRenderer.DrawText(stackText, stackTextX - 1, y + SlotSize - 18, UIColors.Text);
                }

                // Favorite star (top right)
                if (isFavorite)
                {
                    UIRenderer.DrawText("★", x + SlotSize - 16, y + 2, 255, 215, 0);
                }

                // Rarity bars removed — tooltip shows rarity via name color
            }
        }



    }
}
