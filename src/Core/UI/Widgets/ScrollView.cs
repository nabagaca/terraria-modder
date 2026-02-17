using System;

namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Scrollable content area with virtual scrolling and scrollbar.
    /// Uses virtual scrolling (skip off-screen items), NOT BeginClip/EndClip,
    /// to avoid the non-nesting clip region limitation.
    ///
    /// Usage: Begin() → draw content using IsVisible() to cull → End()
    /// </summary>
    public class ScrollView
    {
        private const int ScrollbarWidth = 8;
        private const int ScrollbarMinThumb = 20;
        private const int ScrollSpeed = 40;

        private int _scrollOffset;
        private int _x, _y, _width, _height;
        private int _contentHeight;
        private bool _scrollbarDragging;
        private int _scrollbarDragStartY;
        private int _scrollbarDragStartOffset;

        /// <summary>
        /// Current scroll offset in pixels.
        /// </summary>
        public int ScrollOffset
        {
            get => _scrollOffset;
            set => _scrollOffset = Math.Max(0, Math.Min(value, MaxScroll));
        }

        /// <summary>
        /// Total content height (set during Begin).
        /// </summary>
        public int ContentHeight => _contentHeight;

        /// <summary>
        /// Visible area height (set during Begin).
        /// </summary>
        public int ViewHeight => _height;

        /// <summary>
        /// Maximum scroll offset.
        /// </summary>
        public int MaxScroll => Math.Max(0, _contentHeight - _height);

        /// <summary>
        /// Whether scrolling is needed (content taller than view).
        /// </summary>
        public bool NeedsScrolling => _contentHeight > _height;

        /// <summary>
        /// X position of the content area.
        /// </summary>
        public int ContentX => _x;

        /// <summary>
        /// Y position adjusted for scroll offset.
        /// Draw items at ContentY + (index * itemHeight).
        /// Items above ViewTop or below ViewBottom should be skipped.
        /// </summary>
        public int ContentY => _y - _scrollOffset;

        /// <summary>
        /// Content width (reduced by scrollbar width when scrolling is needed).
        /// </summary>
        public int ContentWidth => NeedsScrolling ? _width - ScrollbarWidth - 4 : _width;

        /// <summary>
        /// Top of the visible window in content coordinates.
        /// </summary>
        public int ViewTop => _scrollOffset;

        /// <summary>
        /// Bottom of the visible window in content coordinates.
        /// </summary>
        public int ViewBottom => _scrollOffset + _height;

        /// <summary>
        /// Begin a scrollable area. Sets up coordinates and clamps scroll.
        /// Draw content between Begin/End using IsVisible() for culling.
        /// </summary>
        public void Begin(int x, int y, int width, int height, int contentHeight)
        {
            _x = x;
            _y = y;
            _width = width;
            _height = height;
            _contentHeight = contentHeight;

            // Clamp scroll offset
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, MaxScroll));
        }

        /// <summary>
        /// Check if an item at the given Y position (in content coordinates, 0-based)
        /// with the given height is visible in the scroll view.
        /// Strict on both edges: item must fit fully within the view
        /// (prevents spillover since there's no GPU clip).
        /// </summary>
        public bool IsVisible(int itemY, int itemHeight)
        {
            int itemBottom = itemY + itemHeight;
            return itemY >= _scrollOffset && itemBottom <= _scrollOffset + _height;
        }

        /// <summary>
        /// End the scrollable area. Handles scroll wheel input and draws scrollbar.
        /// </summary>
        public void End()
        {
            HandleScrollInput();
            DrawScrollbar();
        }

        /// <summary>
        /// Reset scroll to top.
        /// </summary>
        public void ResetScroll()
        {
            _scrollOffset = 0;
            _scrollbarDragging = false;
        }

        /// <summary>
        /// Scroll to ensure a Y position (in content coordinates) is visible.
        /// </summary>
        public void ScrollToY(int y)
        {
            if (y < _scrollOffset)
                _scrollOffset = y;
            else if (y + 20 > _scrollOffset + _height)
                _scrollOffset = y + 20 - _height;

            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, MaxScroll));
        }

        /// <summary>
        /// Scroll to ensure a specific item (by index and item height) is visible.
        /// </summary>
        public void ScrollToItem(int itemIndex, int itemHeight)
        {
            int itemTop = itemIndex * itemHeight;
            int itemBottom = itemTop + itemHeight;

            if (itemTop < _scrollOffset)
                _scrollOffset = itemTop;
            else if (itemBottom > _scrollOffset + _height)
                _scrollOffset = itemBottom - _height;

            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, MaxScroll));
        }

        /// <summary>
        /// Get the first visible item index for items of a given height.
        /// Call after Begin().
        /// </summary>
        public int GetVisibleStartIndex(int itemHeight)
        {
            if (itemHeight <= 0) return 0;
            return _scrollOffset / itemHeight;
        }

        /// <summary>
        /// Get the number of visible items for items of a given height.
        /// Returns enough to cover partial items at top and bottom edges.
        /// Call after Begin().
        /// </summary>
        public int GetVisibleCount(int itemHeight)
        {
            if (itemHeight <= 0) return 0;
            return (_height / itemHeight) + 2;
        }

        /// <summary>
        /// Get the Y offset for drawing the first visible item.
        /// Handles partial item visibility at the top edge.
        /// Call after Begin().
        /// </summary>
        public int GetFirstItemYOffset(int itemHeight)
        {
            if (itemHeight <= 0) return 0;
            return -(_scrollOffset % itemHeight);
        }

        private void HandleScrollInput()
        {
            // Scrollbar drag
            if (_scrollbarDragging)
            {
                if (WidgetInput.MouseLeft)
                {
                    int thumbHeight = GetThumbHeight();
                    int trackHeight = _height - thumbHeight;
                    if (trackHeight > 0)
                    {
                        int deltaY = WidgetInput.MouseY - _scrollbarDragStartY;
                        float pct = (float)deltaY / trackHeight;
                        _scrollOffset = _scrollbarDragStartOffset + (int)(pct * MaxScroll);
                        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, MaxScroll));
                    }
                }
                else
                {
                    _scrollbarDragging = false;
                }
                return;
            }

            // Mouse wheel
            if (WidgetInput.IsMouseOver(_x, _y, _width, _height))
            {
                int scroll = WidgetInput.ScrollWheel;
                if (scroll != 0)
                {
                    WidgetInput.ConsumeScroll();
                    int direction = scroll > 0 ? -1 : 1;
                    _scrollOffset += direction * ScrollSpeed;
                    _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, MaxScroll));
                }
            }
        }

        private void DrawScrollbar()
        {
            if (!NeedsScrolling) return;

            int scrollbarX = _x + _width - ScrollbarWidth - 2;

            // Track
            UIRenderer.DrawRect(scrollbarX, _y, ScrollbarWidth, _height, UIColors.ScrollTrack);

            // Thumb
            int thumbHeight = GetThumbHeight();
            float scrollPct = MaxScroll > 0 ? (float)_scrollOffset / MaxScroll : 0;
            int thumbY = _y + (int)((_height - thumbHeight) * scrollPct);

            bool thumbHover = WidgetInput.IsMouseOver(scrollbarX, thumbY, ScrollbarWidth, thumbHeight);
            UIRenderer.DrawRect(scrollbarX, thumbY, ScrollbarWidth, thumbHeight,
                (_scrollbarDragging || thumbHover) ? UIColors.SliderThumbHover : UIColors.ScrollThumb);

            // Click on thumb → start drag
            if (thumbHover && WidgetInput.MouseLeftClick)
            {
                _scrollbarDragging = true;
                _scrollbarDragStartY = WidgetInput.MouseY;
                _scrollbarDragStartOffset = _scrollOffset;
                WidgetInput.ConsumeClick();
            }
            // Click on track → jump to position
            else if (!thumbHover && WidgetInput.IsMouseOver(scrollbarX, _y, ScrollbarWidth, _height)
                     && WidgetInput.MouseLeftClick)
            {
                int clickY = WidgetInput.MouseY - _y;
                float clickPct = (float)clickY / _height;
                _scrollOffset = (int)(clickPct * MaxScroll);
                _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, MaxScroll));
                _scrollbarDragging = true;
                _scrollbarDragStartY = WidgetInput.MouseY;
                _scrollbarDragStartOffset = _scrollOffset;
                WidgetInput.ConsumeClick();
            }
        }

        private int GetThumbHeight()
        {
            float viewRatio = _contentHeight > 0 ? (float)_height / _contentHeight : 1f;
            return Math.Max(ScrollbarMinThumb, (int)(_height * viewRatio));
        }
    }
}
