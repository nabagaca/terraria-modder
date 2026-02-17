namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Stateless toggle button. Like Button but colors based on active state.
    /// Returns true when clicked (caller is responsible for flipping state).
    /// </summary>
    public static class Toggle
    {
        /// <summary>
        /// Draw a toggle button. Returns true if clicked.
        /// Active state shows green (Success), inactive shows default button color.
        /// </summary>
        public static bool Draw(int x, int y, int width, int height, string text, bool active)
        {
            bool hover = WidgetInput.IsMouseOver(x, y, width, height);

            Color4 bg = active
                ? (hover ? UIColors.Success.WithAlpha(180) : UIColors.Success.WithAlpha(140))
                : (hover ? UIColors.ButtonHover : UIColors.Button);

            UIRenderer.DrawRect(x, y, width, height, bg);

            int pad = 6;
            string label = TextUtil.Truncate($"{text}: {(active ? "ON" : "OFF")}", width - pad * 2);
            int textWidth = TextUtil.MeasureWidth(label);
            int textX = x + (width - textWidth) / 2;
            int textY = y + (height - 14) / 2;
            UIRenderer.DrawText(label, textX, textY, active ? UIColors.Text : UIColors.TextDim);

            if (hover && WidgetInput.MouseLeftClick)
            {
                WidgetInput.ConsumeClick();
                return true;
            }
            return false;
        }
    }
}
