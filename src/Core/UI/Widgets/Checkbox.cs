namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Stateless checkbox widget. Draws a box with check/partial indicator.
    /// Returns true when clicked.
    /// </summary>
    public static class Checkbox
    {
        /// <summary>
        /// Draw just the checkbox box. Returns true if clicked.
        /// </summary>
        public static bool Draw(int x, int y, int size, bool isChecked, bool partial = false)
        {
            bool hover = WidgetInput.IsMouseOver(x, y, size, size);
            UIRenderer.DrawRect(x, y, size, size, hover ? UIColors.InputFocusBg : UIColors.InputBg);
            UIRenderer.DrawRectOutline(x, y, size, size, UIColors.Border);

            if (isChecked)
                UIRenderer.DrawRect(x + 3, y + 3, size - 6, size - 6, UIColors.Success);
            else if (partial)
                UIRenderer.DrawRect(x + 3, y + size / 2 - 2, size - 6, 4, UIColors.Warning);

            if (hover && WidgetInput.MouseLeftClick)
            {
                WidgetInput.ConsumeClick();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Draw a checkbox with a label to its right. Returns true if clicked.
        /// Clicking anywhere in the row (checkbox or label) toggles it.
        /// </summary>
        public static bool DrawWithLabel(int x, int y, int width, int height,
            string label, bool isChecked, bool partial = false)
        {
            int boxSize = System.Math.Min(height - 4, 16);
            int boxY = y + (height - boxSize) / 2;

            bool rowHover = WidgetInput.IsMouseOver(x, y, width, height);

            // Draw box
            UIRenderer.DrawRect(x, boxY, boxSize, boxSize, rowHover ? UIColors.InputFocusBg : UIColors.InputBg);
            UIRenderer.DrawRectOutline(x, boxY, boxSize, boxSize, UIColors.Border);

            if (isChecked)
                UIRenderer.DrawRect(x + 3, boxY + 3, boxSize - 6, boxSize - 6, UIColors.Success);
            else if (partial)
                UIRenderer.DrawRect(x + 3, boxY + boxSize / 2 - 2, boxSize - 6, 4, UIColors.Warning);

            // Draw label (truncated to available space)
            int labelX = x + boxSize + 6;
            int labelMaxWidth = width - boxSize - 6;
            string displayLabel = TextUtil.Truncate(label, labelMaxWidth);
            int textY = y + (height - 14) / 2;
            UIRenderer.DrawText(displayLabel, labelX, textY, UIColors.Text);

            if (rowHover && WidgetInput.MouseLeftClick)
            {
                WidgetInput.ConsumeClick();
                return true;
            }
            return false;
        }
    }
}
