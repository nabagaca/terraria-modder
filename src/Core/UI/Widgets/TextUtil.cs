using System;

namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Text measurement and truncation utilities.
    /// Uses actual font measurement via UIRenderer.MeasureText when available,
    /// falls back to 7px per character estimate.
    /// </summary>
    public static class TextUtil
    {
        /// <summary>
        /// Measure the pixel width of a text string using the actual game font.
        /// </summary>
        public static int MeasureWidth(string text)
            => UIRenderer.MeasureText(text);

        /// <summary>
        /// Truncate text to fit within a maximum pixel width, adding "..." if needed.
        /// Returns the original text if it already fits.
        /// Uses real font measurement for accuracy.
        /// </summary>
        public static string Truncate(string text, int maxPixelWidth)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (maxPixelWidth <= 0) return "";
            if (MeasureWidth(text) <= maxPixelWidth) return text;

            // Binary search for the longest prefix that fits with "..."
            int ellipsisWidth = MeasureWidth("...");
            int availableWidth = maxPixelWidth - ellipsisWidth;
            if (availableWidth <= 0)
                return text.Length > 0 ? text.Substring(0, 1) : "";

            // Start from the end and work backwards
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (MeasureWidth(text.Substring(0, mid)) <= availableWidth)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            if (lo <= 0) return text.Length > 0 ? text.Substring(0, 1) : "";
            return text.Substring(0, lo) + "...";
        }

        /// <summary>
        /// Get the visible tail of text that fits within a pixel width.
        /// Used for text inputs where the cursor/end should always be visible.
        /// Uses real font measurement for accuracy.
        /// </summary>
        public static string VisibleTail(string text, int maxPixelWidth)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (maxPixelWidth <= 0) return "";
            if (MeasureWidth(text) <= maxPixelWidth) return text;

            // Binary search for the longest suffix that fits
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (MeasureWidth(text.Substring(mid)) <= maxPixelWidth)
                    hi = mid;
                else
                    lo = mid + 1;
            }

            if (lo >= text.Length) return text.Substring(text.Length - 1);
            return text.Substring(lo);
        }
    }
}
