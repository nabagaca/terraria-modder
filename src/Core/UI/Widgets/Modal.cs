using System;
using TerrariaModder.Core.Input;

namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Modal overlay widget. Draws a dimming background and centered content panel.
    /// Sets WidgetInput.BlockInput for background panels while open.
    ///
    /// Usage: BeginDraw("Title", contentHeight) → draw content → EndDraw()
    /// </summary>
    public class Modal
    {
        private bool _isOpen;
        private int _x, _y, _totalHeight;

        private const int HeaderHeight = 32;
        private const int Padding = 12;

        public Modal(int width = 480)
        {
            Width = width;
        }

        /// <summary>Whether the modal is currently open.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>Modal width.</summary>
        public int Width { get; set; }

        /// <summary>X position of the content area.</summary>
        public int ContentX => _x + Padding;

        /// <summary>Y position of the content area (below header).</summary>
        public int ContentY => _y + HeaderHeight;

        /// <summary>Available width for content.</summary>
        public int ContentWidth => Width - Padding * 2;

        /// <summary>Open the modal.</summary>
        public void Open()
        {
            _isOpen = true;
        }

        /// <summary>Close the modal.</summary>
        public void Close()
        {
            _isOpen = false;
        }

        /// <summary>Toggle open/close.</summary>
        public void Toggle()
        {
            _isOpen = !_isOpen;
        }

        /// <summary>
        /// Begin drawing the modal. Returns false if closed.
        /// Draws dimming overlay, centered panel, and header with title.
        /// </summary>
        /// <param name="title">Modal title.</param>
        /// <param name="contentHeight">Height of your content (modal sizes to fit).</param>
        public bool BeginDraw(string title, int contentHeight)
        {
            if (!_isOpen) return false;

            _totalHeight = HeaderHeight + contentHeight + Padding;

            // Center on screen
            _x = (UIRenderer.ScreenWidth - Width) / 2;
            _y = (UIRenderer.ScreenHeight - _totalHeight) / 2;

            // Dimming overlay (full screen)
            UIRenderer.DrawRect(0, 0, UIRenderer.ScreenWidth, UIRenderer.ScreenHeight,
                new Color4(0, 0, 0, 150));

            // Panel background
            UIRenderer.DrawPanel(_x, _y, Width, _totalHeight, UIColors.PanelBg);
            UIRenderer.DrawRectOutline(_x, _y, Width, _totalHeight, UIColors.Border);

            // Header
            UIRenderer.DrawRect(_x, _y, Width, HeaderHeight, UIColors.HeaderBg);
            UIRenderer.DrawTextShadow(title, _x + Padding, _y + 8, UIColors.TextTitle);

            // Close button
            int closeX = _x + Width - 35;
            int closeY = _y + 3;
            bool closeHover = UIRenderer.IsMouseOver(closeX, closeY, 30, 28);
            UIRenderer.DrawRect(closeX, closeY, 30, 28,
                closeHover ? UIColors.CloseBtnHover : UIColors.CloseBtn);
            UIRenderer.DrawText("X", closeX + 11, closeY + 7, UIColors.Text);

            if (closeHover && UIRenderer.MouseLeftClick)
            {
                UIRenderer.ConsumeClick();
                Close();
                return false;
            }

            // Escape to close (only when not in text input/keybind capture)
            if (!UIRenderer.IsWaitingForKeyInput && InputState.IsKeyJustPressed(KeyCode.Escape))
            {
                Close();
                return false;
            }

            // Clip content area so nothing draws outside the modal
            UIRenderer.BeginClip(_x, _y + HeaderHeight, Width, _totalHeight - HeaderHeight);

            return true;
        }

        /// <summary>
        /// End drawing the modal. Consumes all clicks on the modal area
        /// and the dimming overlay.
        /// </summary>
        public void EndDraw()
        {
            // End content clipping
            UIRenderer.EndClip();

            // Consume all clicks on dimming overlay (prevent background interaction)
            if (UIRenderer.MouseLeftClick) UIRenderer.ConsumeClick();
            if (UIRenderer.MouseRightClick) UIRenderer.ConsumeRightClick();
            if (UIRenderer.ScrollWheel != 0) UIRenderer.ConsumeScroll();
        }
    }
}
