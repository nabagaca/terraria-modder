using System;
using TerrariaModder.Core.Input;

namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Top-level draggable panel container. Handles drag, header, close button,
    /// panel bounds registration, z-order blocking, escape-to-close, and
    /// catch-all click consumption.
    ///
    /// Usage: BeginDraw() → draw your content → EndDraw()
    /// </summary>
    public class DraggablePanel
    {
        private int _panelX = -1;
        private int _panelY = -1;
        private bool _isOpen;
        private bool _isDragging;
        private int _dragOffsetX;
        private int _dragOffsetY;
        private bool _blockInput;
        private bool _drawRegistered;

        /// <summary>
        /// Create a new draggable panel.
        /// </summary>
        /// <param name="panelId">Unique ID for z-order and bounds registration.</param>
        /// <param name="title">Title displayed in the header.</param>
        /// <param name="width">Panel width in pixels.</param>
        /// <param name="height">Panel height in pixels.</param>
        public DraggablePanel(string panelId, string title, int width, int height)
        {
            PanelId = panelId;
            Title = title;
            Width = width;
            Height = height;
        }

        // -- Properties --

        public string PanelId { get; }
        public string Title { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int X => _panelX;
        public int Y => _panelY;
        public bool IsOpen => _isOpen;
        public bool BlockInput => _blockInput;

        /// <summary>Y coordinate where content starts (below header).</summary>
        public int ContentX => _panelX + Padding;

        /// <summary>Y coordinate where content starts (below header).</summary>
        public int ContentY => _panelY + HeaderHeight;

        /// <summary>Available width for content (panel width minus padding on both sides).</summary>
        public int ContentWidth => Width - Padding * 2;

        /// <summary>Available height for content (below header, minus bottom padding).</summary>
        public int ContentHeight => Height - HeaderHeight - Padding;

        // -- Configuration --

        public int HeaderHeight { get; set; } = 35;
        public int Padding { get; set; } = 8;
        public bool ShowCloseButton { get; set; } = true;
        public bool Draggable { get; set; } = true;
        public bool CloseOnEscape { get; set; } = true;
        public Action OnClose { get; set; }

        /// <summary>
        /// Whether BeginDraw/EndDraw should clip the content area.
        /// Default true. Set to false when the panel manages its own clip regions
        /// (e.g., panels with tab bars, toolbars, and footers outside the scroll area).
        /// </summary>
        public bool ClipContent { get; set; } = true;

        // -- Lifecycle --

        /// <summary>
        /// Open the panel centered on screen.
        /// </summary>
        public void Open()
        {
            _panelX = -1;
            _panelY = -1;
            _isOpen = true;
            RegisterDraw();
        }

        /// <summary>
        /// Open the panel at a specific position.
        /// </summary>
        public void Open(int x, int y)
        {
            _panelX = x;
            _panelY = y;
            _isOpen = true;
            RegisterDraw();
        }

        /// <summary>
        /// Close the panel and unregister bounds.
        /// </summary>
        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            _isDragging = false;
            UIRenderer.UnregisterPanelBounds(PanelId);
            OnClose?.Invoke();
        }

        /// <summary>
        /// Toggle open/close.
        /// </summary>
        public void Toggle()
        {
            if (_isOpen)
                Close();
            else
                Open();
        }

        /// <summary>
        /// Register the panel's draw callback with UIRenderer.
        /// Call this once during mod initialization, passing your draw method.
        /// </summary>
        public void RegisterDrawCallback(Action drawCallback)
        {
            UIRenderer.RegisterPanelDraw(PanelId, drawCallback);
            _drawRegistered = true;
        }

        /// <summary>
        /// Unregister the panel's draw callback.
        /// </summary>
        public void UnregisterDrawCallback()
        {
            UIRenderer.UnregisterPanelDraw(PanelId);
            _drawRegistered = false;
        }

        // -- Draw Frame --

        /// <summary>
        /// Call at the start of your Draw callback. Returns false if panel is closed
        /// (skip all drawing). Handles escape, dragging, header, close button, bounds.
        /// Sets WidgetInput.BlockInput for child widgets.
        /// </summary>
        public bool BeginDraw()
        {
            if (!_isOpen) return false;

            // Escape to close (only when not blocked by higher panel and not in text input)
            if (CloseOnEscape && !UIRenderer.IsWaitingForKeyInput
                && InputState.IsKeyJustPressed(KeyCode.Escape))
            {
                Close();
                return false;
            }

            // Set block flag for this frame
            _blockInput = UIRenderer.ShouldBlockForHigherPriorityPanel(PanelId);
            WidgetInput.BlockInput = _blockInput;

            // Default position: centered
            if (_panelX < 0) _panelX = (UIRenderer.ScreenWidth - Width) / 2;
            if (_panelY < 0) _panelY = (UIRenderer.ScreenHeight - Height) / 2;

            // Dragging
            if (Draggable)
                HandleDragging();

            // Clamp to screen
            _panelX = Math.Max(0, Math.Min(_panelX, UIRenderer.ScreenWidth - Width));
            _panelY = Math.Max(0, Math.Min(_panelY, UIRenderer.ScreenHeight - Height));

            // Register bounds for click-through prevention
            UIRenderer.RegisterPanelBounds(PanelId, _panelX, _panelY, Width, Height);

            // Draw panel background
            UIRenderer.DrawPanel(_panelX, _panelY, Width, Height, UIColors.PanelBg);

            // Draw header
            UIRenderer.DrawRect(_panelX, _panelY, Width, HeaderHeight, UIColors.HeaderBg);
            UIRenderer.DrawTextShadow(Title, _panelX + 10, _panelY + 9, UIColors.TextTitle);

            // Close button
            if (ShowCloseButton)
            {
                int closeX = _panelX + Width - 35;
                int closeY = _panelY + 3;
                bool closeHover = WidgetInput.IsMouseOver(closeX, closeY, 30, 30);
                UIRenderer.DrawRect(closeX, closeY, 30, 30,
                    closeHover ? UIColors.CloseBtnHover : UIColors.CloseBtn);
                UIRenderer.DrawText("X", closeX + 11, closeY + 7, UIColors.Text);

                if (closeHover && WidgetInput.MouseLeftClick)
                {
                    WidgetInput.ConsumeClick();
                    Close();
                    return false;
                }
            }

            // Clear tooltip for this frame
            Tooltip.Clear();

            // Clip content area so nothing draws outside the panel
            if (ClipContent)
                UIRenderer.BeginClip(_panelX, _panelY + HeaderHeight, Width, Height - HeaderHeight);

            return true;
        }

        /// <summary>
        /// Call at the end of your Draw callback.
        /// Handles catch-all click consumption and clears BlockInput.
        /// </summary>
        public void EndDraw()
        {
            // End content clipping before drawing tooltip (tooltips can appear anywhere)
            if (ClipContent)
                UIRenderer.EndClip();

            // Draw deferred tooltip
            Tooltip.DrawDeferred();

            // Catch-all: consume any remaining clicks over the panel
            if (!_blockInput && UIRenderer.IsMouseOver(_panelX, _panelY, Width, Height))
            {
                if (UIRenderer.MouseLeftClick) UIRenderer.ConsumeClick();
                if (UIRenderer.MouseRightClick) UIRenderer.ConsumeRightClick();
                if (UIRenderer.ScrollWheel != 0) UIRenderer.ConsumeScroll();
            }

            WidgetInput.BlockInput = false;
        }

        // -- Internal --

        private void RegisterDraw()
        {
            // Bring to front when opening
            if (_drawRegistered)
                UIRenderer.BringToFront(PanelId);
        }

        private void HandleDragging()
        {
            // Drag handle = header area, excluding close button
            int headerWidth = ShowCloseButton ? Width - 40 : Width;
            bool inHeader = WidgetInput.IsMouseOver(_panelX, _panelY, headerWidth, HeaderHeight);

            if (WidgetInput.MouseLeftClick && inHeader && !_isDragging)
            {
                _isDragging = true;
                _dragOffsetX = WidgetInput.MouseX - _panelX;
                _dragOffsetY = WidgetInput.MouseY - _panelY;
                WidgetInput.ConsumeClick();
            }

            if (_isDragging)
            {
                if (WidgetInput.MouseLeft)
                {
                    _panelX = WidgetInput.MouseX - _dragOffsetX;
                    _panelY = WidgetInput.MouseY - _dragOffsetY;
                }
                else
                {
                    _isDragging = false;
                }
            }
        }
    }
}
