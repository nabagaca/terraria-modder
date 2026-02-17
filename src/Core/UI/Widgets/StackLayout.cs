namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Vertical layout tracker. Manages the current Y position and auto-advances
    /// after each widget. Eliminates manual Y-coordinate math.
    ///
    /// This is a struct — created per-frame on the stack, zero GC pressure.
    /// Pass by ref when mutation is needed across method boundaries.
    /// </summary>
    public struct StackLayout
    {
        private readonly int _startY;
        private readonly int _spacing;
        private int _currentY;

        /// <summary>
        /// Create a new stack layout starting at the given position.
        /// </summary>
        /// <param name="x">Left edge of the layout area.</param>
        /// <param name="y">Top of the layout area (first widget draws here).</param>
        /// <param name="width">Available width for widgets.</param>
        /// <param name="spacing">Vertical spacing between widgets (default 4px).</param>
        public StackLayout(int x, int y, int width, int spacing = 4)
        {
            X = x;
            _startY = y;
            _currentY = y;
            Width = width;
            _spacing = spacing;
        }

        /// <summary>Left edge of the layout area.</summary>
        public int X { get; }

        /// <summary>Starting Y position.</summary>
        public int Y => _startY;

        /// <summary>Available width for widgets.</summary>
        public int Width { get; }

        /// <summary>Current Y cursor position (where the next widget will draw).</summary>
        public int CurrentY => _currentY;

        /// <summary>Total height consumed so far.</summary>
        public int TotalHeight => _currentY - _startY;

        /// <summary>
        /// Reserve space and advance the Y cursor. Returns the Y at which to draw
        /// custom content (before the advance).
        /// </summary>
        public int Advance(int height)
        {
            int y = _currentY;
            _currentY += height + _spacing;
            return y;
        }

        /// <summary>
        /// Add vertical space without drawing anything.
        /// </summary>
        public void Space(int pixels)
        {
            _currentY += pixels;
        }

        // -- Integrated widget helpers (draw + advance) --

        /// <summary>
        /// Draw a full-width button and advance. Returns true if clicked.
        /// </summary>
        public bool Button(string text, int height = 26)
        {
            bool clicked = Widgets.Button.Draw(X, _currentY, Width, height, text);
            _currentY += height + _spacing;
            return clicked;
        }

        /// <summary>
        /// Draw a button at a specific X position with specific width.
        /// Does NOT advance Y — use for side-by-side buttons, then call Advance() once.
        /// </summary>
        public bool ButtonAt(int x, int width, string text, int height = 26)
        {
            return Widgets.Button.Draw(x, _currentY, width, height, text);
        }

        /// <summary>
        /// Draw a full-width toggle and advance. Returns true if clicked.
        /// </summary>
        public bool Toggle(string text, bool active, int height = 26)
        {
            bool clicked = Widgets.Toggle.Draw(X, _currentY, Width, height, text, active);
            _currentY += height + _spacing;
            return clicked;
        }

        /// <summary>
        /// Draw a toggle at a specific X position with specific width.
        /// Does NOT advance Y — use for side-by-side toggles, then call Advance() once.
        /// </summary>
        public bool ToggleAt(int x, int width, string text, bool active, int height = 26)
        {
            return Widgets.Toggle.Draw(x, _currentY, width, height, text, active);
        }

        /// <summary>
        /// Draw a checkbox with label and advance. Returns true if clicked.
        /// </summary>
        public bool Checkbox(string label, bool isChecked, bool partial = false, int height = 24)
        {
            bool clicked = Widgets.Checkbox.DrawWithLabel(X, _currentY, Width, height, label, isChecked, partial);
            _currentY += height + _spacing;
            return clicked;
        }

        /// <summary>
        /// Draw a section header (divider + label) and advance.
        /// </summary>
        public void SectionHeader(string title, int height = 22)
        {
            Widgets.SectionHeader.Draw(X, _currentY, Width, title, height);
            _currentY += height + _spacing;
        }

        /// <summary>
        /// Draw a text label and advance. Truncates to available width.
        /// </summary>
        public void Label(string text, Color4 color, int height = 18)
        {
            string display = TextUtil.Truncate(text, Width);
            int textY = _currentY + (height - 14) / 2;
            UIRenderer.DrawText(display, X, textY, color);
            _currentY += height + _spacing;
        }

        /// <summary>
        /// Draw a text label with default text color.
        /// </summary>
        public void Label(string text, int height = 18)
        {
            Label(text, UIColors.Text, height);
        }

        /// <summary>
        /// Draw a horizontal divider line and advance.
        /// </summary>
        public void Divider(int height = 8)
        {
            int lineY = _currentY + height / 2;
            UIRenderer.DrawRect(X, lineY, Width, 1, UIColors.Divider);
            _currentY += height + _spacing;
        }
    }
}
