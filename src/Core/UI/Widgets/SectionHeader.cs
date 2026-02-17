namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Stateless section divider with label text.
    /// </summary>
    public static class SectionHeader
    {
        /// <summary>
        /// Draw a section header (divider line + label). Returns height consumed.
        /// </summary>
        public static int Draw(int x, int y, int width, string title, int height = 22)
        {
            // Reserve space for divider line (at least 20px)
            string display = TextUtil.Truncate(title, width - 28);
            int textY = y + (height - 14) / 2;
            UIRenderer.DrawText(display, x, textY, UIColors.TextHint);

            // Draw divider line after text
            int lineX = x + TextUtil.MeasureWidth(display) + 8;
            int lineY = y + height / 2;
            int lineWidth = width - (lineX - x);
            if (lineWidth > 0)
                UIRenderer.DrawRect(lineX, lineY, lineWidth, 1, UIColors.Divider);

            return height;
        }
    }
}
