using System;
using System.Collections.Generic;

namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Deferred tooltip renderer. Call Set() during hover, DrawDeferred() at end of panel draw.
    /// Only the last-set tooltip is drawn (last-writer-wins per frame).
    /// </summary>
    public static class Tooltip
    {
        private static string _text;
        private static string _title;
        private static bool _hasTooltip;

        /// <summary>
        /// Set a single-line tooltip. Call during hover detection.
        /// </summary>
        public static void Set(string text)
        {
            _text = text;
            _title = null;
            _hasTooltip = true;
        }

        /// <summary>
        /// Set a tooltip with title and description.
        /// </summary>
        public static void Set(string title, string description)
        {
            _title = title;
            _text = description;
            _hasTooltip = true;
        }

        /// <summary>
        /// Clear pending tooltip. Called automatically by DraggablePanel.BeginDraw().
        /// </summary>
        public static void Clear()
        {
            _hasTooltip = false;
            _text = null;
            _title = null;
            ItemTooltip.Clear();
        }

        /// <summary>
        /// Draw the deferred tooltip near the mouse cursor.
        /// Call at the END of your panel draw, after all content.
        /// Word-wraps long text and clamps to screen bounds.
        /// </summary>
        public static void DrawDeferred()
        {
            // Always give ItemTooltip a chance to draw, even if no text tooltip is pending
            ItemTooltip.DrawDeferred();

            if (!_hasTooltip || string.IsNullOrEmpty(_text)) return;

            int maxWidth = 300;
            int lineHeight = 16;
            int padding = 8;
            int maxContentWidth = maxWidth - padding * 2;

            var lines = new List<string>();

            // Add title if present
            if (!string.IsNullOrEmpty(_title))
                lines.Add(_title);

            // Word-wrap text using real font measurement
            foreach (string paragraph in _text.Split('\n'))
            {
                if (TextUtil.MeasureWidth(paragraph) <= maxContentWidth)
                {
                    lines.Add(paragraph);
                    continue;
                }

                string remaining = paragraph;
                while (TextUtil.MeasureWidth(remaining) > maxContentWidth)
                {
                    // Find last space that fits
                    int breakAt = -1;
                    for (int i = remaining.Length - 1; i > 0; i--)
                    {
                        if (remaining[i] == ' ' && TextUtil.MeasureWidth(remaining.Substring(0, i)) <= maxContentWidth)
                        {
                            breakAt = i;
                            break;
                        }
                    }
                    // No space found — force break at max fitting chars
                    if (breakAt <= 0)
                    {
                        breakAt = remaining.Length;
                        for (int i = 1; i < remaining.Length; i++)
                        {
                            if (TextUtil.MeasureWidth(remaining.Substring(0, i)) > maxContentWidth)
                            {
                                breakAt = Math.Max(1, i - 1);
                                break;
                            }
                        }
                    }
                    lines.Add(remaining.Substring(0, breakAt));
                    remaining = remaining.Substring(breakAt).TrimStart();
                }
                if (remaining.Length > 0)
                    lines.Add(remaining);
            }

            // Calculate tooltip dimensions using real measurement
            int tooltipWidth = 0;
            foreach (var line in lines)
                tooltipWidth = Math.Max(tooltipWidth, TextUtil.MeasureWidth(line));
            tooltipWidth = Math.Min(tooltipWidth + padding * 2, maxWidth);

            int titleLines = string.IsNullOrEmpty(_title) ? 0 : 1;
            int tooltipHeight = lines.Count * lineHeight + padding * 2;

            // Position near mouse, clamped to screen
            int tx = WidgetInput.MouseX + 16;
            int ty = WidgetInput.MouseY + 16;

            if (tx + tooltipWidth > WidgetInput.ScreenWidth - 4)
                tx = WidgetInput.MouseX - tooltipWidth - 4;
            if (ty + tooltipHeight > WidgetInput.ScreenHeight - 4)
                ty = WidgetInput.MouseY - tooltipHeight - 4;

            tx = Math.Max(4, tx);
            ty = Math.Max(4, ty);

            // Draw background
            UIRenderer.DrawRect(tx, ty, tooltipWidth, tooltipHeight, UIColors.TooltipBg);
            UIRenderer.DrawRectOutline(tx, ty, tooltipWidth, tooltipHeight, UIColors.Border);

            // Draw lines
            int ly = ty + padding;
            for (int i = 0; i < lines.Count; i++)
            {
                Color4 color = (i == 0 && titleLines > 0) ? UIColors.TextTitle : UIColors.Text;
                UIRenderer.DrawText(lines[i], tx + padding, ly, color);
                ly += lineHeight;
            }

            _hasTooltip = false;
        }
    }
}
