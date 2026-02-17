using TerrariaModder.Core.Input;

namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Input helper that gates all hover/click through a BlockInput flag.
    /// Set BlockInput before drawing child widgets; they automatically respect it.
    /// Replaces per-mod input blocking patterns.
    /// </summary>
    public static class WidgetInput
    {
        /// <summary>
        /// When true, IsMouseOver returns false and click/held properties return false.
        /// Set by DraggablePanel.BeginDraw() based on ShouldBlockForHigherPriorityPanel.
        /// </summary>
        public static bool BlockInput { get; set; }

        public static int MouseX => UIRenderer.MouseX;
        public static int MouseY => UIRenderer.MouseY;
        public static bool MouseLeftClick => !BlockInput && UIRenderer.MouseLeftClick;
        public static bool MouseLeft => !BlockInput && UIRenderer.MouseLeft;
        public static bool MouseRightClick => !BlockInput && UIRenderer.MouseRightClick;
        public static bool MouseRight => !BlockInput && UIRenderer.MouseRight;
        public static bool MouseMiddleClick => !BlockInput && UIRenderer.MouseMiddleClick;
        public static bool MouseMiddle => !BlockInput && UIRenderer.MouseMiddle;
        public static int ScrollWheel => BlockInput ? 0 : UIRenderer.ScrollWheel;

        public static int ScreenWidth => UIRenderer.ScreenWidth;
        public static int ScreenHeight => UIRenderer.ScreenHeight;

        public static bool IsMouseOver(int x, int y, int w, int h)
            => !BlockInput && UIRenderer.IsMouseOver(x, y, w, h);

        public static void ConsumeClick() => UIRenderer.ConsumeClick();
        public static void ConsumeRightClick() => UIRenderer.ConsumeRightClick();
        public static void ConsumeScroll() => UIRenderer.ConsumeScroll();
        public static void ConsumeMiddleClick() => UIRenderer.ConsumeMiddleClick();

        public static bool IsShiftHeld => InputState.IsShiftDown();
        public static bool IsCtrlHeld => InputState.IsCtrlDown();
        public static bool IsAltHeld => InputState.IsAltDown();

        public static bool ShouldBlockForHigherPriorityPanel(string myPanelId)
            => UIRenderer.ShouldBlockForHigherPriorityPanel(myPanelId);
    }
}
