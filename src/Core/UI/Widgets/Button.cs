namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Stateless button widget. Draws a clickable rectangle with centered text.
    /// Returns true when clicked.
    /// </summary>
    public static class Button
    {
        /// <summary>
        /// Draw a button with default theme colors. Returns true if clicked.
        /// </summary>
        public static bool Draw(int x, int y, int width, int height, string text)
        {
            return Draw(x, y, width, height, text, UIColors.Button, UIColors.ButtonHover, UIColors.TextDim);
        }

        /// <summary>
        /// Draw a button with custom colors. Returns true if clicked.
        /// </summary>
        public static bool Draw(int x, int y, int width, int height, string text,
            Color4 normalBg, Color4 hoverBg, Color4 textColor)
        {
            bool hover = WidgetInput.IsMouseOver(x, y, width, height);
            UIRenderer.DrawRect(x, y, width, height, hover ? hoverBg : normalBg);

            // Truncate and center text
            int pad = 6;
            string display = TextUtil.Truncate(text, width - pad * 2);
            int textWidth = TextUtil.MeasureWidth(display);
            int textX = x + (width - textWidth) / 2;
            int textY = y + (height - 14) / 2;
            UIRenderer.DrawText(display, textX, textY, textColor);

            if (hover && WidgetInput.MouseLeftClick)
            {
                WidgetInput.ConsumeClick();
                return true;
            }
            return false;
        }
    }
}
