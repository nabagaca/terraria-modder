namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Stateless tab strip widget. Returns the selected tab index.
    /// </summary>
    public static class TabBar
    {
        /// <summary>
        /// Draw a tab strip. Returns the new active tab index.
        /// If no tab was clicked, returns the current activeTab value.
        /// </summary>
        public static int Draw(int x, int y, int width, string[] tabNames, int activeTab, int height = 28)
        {
            int tabCount = tabNames.Length;
            if (tabCount == 0) return activeTab;

            int tabWidth = width / tabCount;

            for (int i = 0; i < tabCount; i++)
            {
                int tabX = x + i * tabWidth;
                int tabW = (i == tabCount - 1) ? (width - i * tabWidth) : tabWidth - 2;
                bool isActive = activeTab == i;
                bool isHovered = WidgetInput.IsMouseOver(tabX, y, tabW, height);

                // Background
                Color4 bg = isActive ? UIColors.ItemActiveBg
                    : (isHovered ? UIColors.SectionBg : UIColors.InputBg);
                UIRenderer.DrawRect(tabX, y, tabW, height, bg);

                // Active indicator (bottom bar)
                if (isActive)
                    UIRenderer.DrawRect(tabX, y + height - 2, tabW, 2, UIColors.Accent);

                // Tab name (truncated and centered)
                int tabPad = 6;
                string tabLabel = TextUtil.Truncate(tabNames[i], tabW - tabPad * 2);
                int textWidth = TextUtil.MeasureWidth(tabLabel);
                int textX = tabX + (tabW - textWidth) / 2;
                int textY = y + (height - 14) / 2;
                UIRenderer.DrawText(tabLabel, textX, textY, isActive ? UIColors.AccentText : UIColors.TextDim);

                // Click
                if (isHovered && WidgetInput.MouseLeftClick)
                {
                    WidgetInput.ConsumeClick();
                    return i;
                }
            }

            return activeTab;
        }
    }
}
