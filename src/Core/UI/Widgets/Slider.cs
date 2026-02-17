using System;

namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Horizontal slider widget with drag support.
    /// One instance per slider (tracks drag state internally).
    /// </summary>
    public class Slider
    {
        private bool _dragging;

        /// <summary>
        /// Draw an integer slider. Returns the (possibly changed) value.
        /// </summary>
        public int Draw(int x, int y, int width, int height, int value, int min, int max)
        {
            if (max <= min) return value;
            if (width <= 12) return value; // Too narrow for thumb

            // Track
            int trackY = y + height / 2 - 3;
            UIRenderer.DrawRect(x, trackY, width, 6, UIColors.SliderTrack);

            // Thumb position
            float pct = (float)(value - min) / (max - min);
            int thumbW = 12;
            int thumbX = x + (int)(pct * (width - thumbW));

            bool thumbHover = WidgetInput.IsMouseOver(thumbX, y, thumbW, height);
            UIRenderer.DrawRect(thumbX, y, thumbW, height,
                (_dragging || thumbHover) ? UIColors.SliderThumbHover : UIColors.SliderThumb);

            // Start drag on thumb click
            if (thumbHover && WidgetInput.MouseLeftClick)
            {
                _dragging = true;
                WidgetInput.ConsumeClick();
            }

            // Drag tracking
            if (_dragging)
            {
                if (WidgetInput.MouseLeft)
                {
                    float newPct = Math.Max(0f, Math.Min(1f, (WidgetInput.MouseX - x - thumbW / 2f) / (width - thumbW)));
                    value = min + (int)(newPct * (max - min));
                }
                else
                {
                    _dragging = false;
                }
            }
            // Click-to-seek on track
            else if (WidgetInput.IsMouseOver(x, y, width, height) && WidgetInput.MouseLeftClick)
            {
                float newPct = Math.Max(0f, Math.Min(1f, (WidgetInput.MouseX - x - thumbW / 2f) / (width - thumbW)));
                value = min + (int)(newPct * (max - min));
                WidgetInput.ConsumeClick();
            }

            return Math.Max(min, Math.Min(max, value));
        }

        /// <summary>
        /// Draw a float slider. Returns the (possibly changed) value.
        /// </summary>
        public float Draw(int x, int y, int width, int height, float value, float min, float max)
        {
            if (max <= min) return value;
            if (width <= 12) return value; // Too narrow for thumb

            // Track
            int trackY = y + height / 2 - 3;
            UIRenderer.DrawRect(x, trackY, width, 6, UIColors.SliderTrack);

            // Thumb position
            float pct = (value - min) / (max - min);
            int thumbW = 12;
            int thumbX = x + (int)(pct * (width - thumbW));

            bool thumbHover = WidgetInput.IsMouseOver(thumbX, y, thumbW, height);
            UIRenderer.DrawRect(thumbX, y, thumbW, height,
                (_dragging || thumbHover) ? UIColors.SliderThumbHover : UIColors.SliderThumb);

            // Start drag on thumb click
            if (thumbHover && WidgetInput.MouseLeftClick)
            {
                _dragging = true;
                WidgetInput.ConsumeClick();
            }

            // Drag tracking
            if (_dragging)
            {
                if (WidgetInput.MouseLeft)
                {
                    float newPct = Math.Max(0f, Math.Min(1f, (WidgetInput.MouseX - x - thumbW / 2f) / (width - thumbW)));
                    value = min + newPct * (max - min);
                }
                else
                {
                    _dragging = false;
                }
            }
            // Click-to-seek on track
            else if (WidgetInput.IsMouseOver(x, y, width, height) && WidgetInput.MouseLeftClick)
            {
                float newPct = Math.Max(0f, Math.Min(1f, (WidgetInput.MouseX - x - thumbW / 2f) / (width - thumbW)));
                value = min + newPct * (max - min);
                WidgetInput.ConsumeClick();
            }

            return Math.Max(min, Math.Min(max, value));
        }
    }
}
