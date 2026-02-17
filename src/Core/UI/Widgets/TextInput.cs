namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Text input field with focus management, IME support, and escape-to-unfocus.
    /// One instance per text field. Requires Update() call in Update phase.
    /// </summary>
    public class TextInput
    {
        private string _text = "";
        private bool _focused;
        private bool _textChanged;
        private readonly string _placeholder;
        private readonly int _maxLength;

        public TextInput(string placeholder = "", int maxLength = 500)
        {
            _placeholder = placeholder;
            _maxLength = maxLength;
        }

        /// <summary>
        /// Current text content.
        /// </summary>
        public string Text
        {
            get => _text;
            set
            {
                string newText = value ?? "";
                if (newText.Length > _maxLength)
                    newText = newText.Substring(0, _maxLength);
                if (_text != newText)
                {
                    _text = newText;
                    _textChanged = true;
                }
            }
        }

        /// <summary>
        /// Whether the text field is focused.
        /// </summary>
        public bool IsFocused => _focused;

        /// <summary>
        /// Whether the text changed since last check. Resets to false after reading.
        /// </summary>
        public bool HasChanged
        {
            get
            {
                bool changed = _textChanged;
                _textChanged = false;
                return changed;
            }
        }

        /// <summary>
        /// Panel ID for RegisterKeyInputBlock when focused.
        /// Set this to your panel ID to block keyboard input during typing.
        /// </summary>
        public string KeyBlockId { get; set; }

        /// <summary>
        /// Call during Update phase to maintain text input state.
        /// Text input must be enabled in BOTH Update and Draw phases.
        /// </summary>
        public void Update()
        {
            if (_focused)
                UIRenderer.EnableTextInput();
        }

        /// <summary>
        /// Draw the text input field. Returns current text.
        /// Handles: click-to-focus, IME, escape-to-unfocus, max length, cursor.
        /// </summary>
        public string Draw(int x, int y, int width, int height = 28)
        {
            int padding = 8;

            bool isHovered = WidgetInput.IsMouseOver(x, y, width, height);

            // Background
            UIRenderer.DrawRect(x, y, width, height, _focused ? UIColors.InputFocusBg : UIColors.InputBg);

            // Focus border
            if (_focused)
                UIRenderer.DrawRectOutline(x, y, width, height, UIColors.Accent, 1);

            // Clear button FIRST (before focus handler, which would consume the click)
            const int ClearBtnWidth = 42;
            const int ClearBtnHeight = 18;
            bool clearClicked = false;
            if (!string.IsNullOrEmpty(_text))
            {
                int clearX = x + width - ClearBtnWidth - 4;
                int clearY = y + (height - ClearBtnHeight) / 2;
                bool clearHover = WidgetInput.IsMouseOver(clearX, clearY, ClearBtnWidth, ClearBtnHeight);

                UIRenderer.DrawRect(clearX, clearY, ClearBtnWidth, ClearBtnHeight, clearHover ? UIColors.CloseBtnHover : UIColors.CloseBtn);
                UIRenderer.DrawText("Clear", clearX + 5, clearY + 1, UIColors.TextDim);

                if (clearHover && WidgetInput.MouseLeftClick)
                {
                    _text = "";
                    _textChanged = true;
                    UIRenderer.ClearInput();
                    WidgetInput.ConsumeClick();
                    clearClicked = true;
                }
            }

            // Click to focus (skip if clear button was clicked)
            if (!clearClicked && isHovered && WidgetInput.MouseLeftClick)
            {
                WidgetInput.ConsumeClick();
                if (!_focused)
                {
                    _focused = true;
                    UIRenderer.ClearInput();
                    if (!string.IsNullOrEmpty(KeyBlockId))
                        UIRenderer.RegisterKeyInputBlock(KeyBlockId);
                }
            }

            // Text input handling when focused
            if (_focused)
            {
                UIRenderer.EnableTextInput();
                UIRenderer.HandleIME();

                string oldText = _text;
                _text = UIRenderer.GetInputText(_text);

                // Enforce max length
                if (_text.Length > _maxLength)
                    _text = _text.Substring(0, _maxLength);

                if (_text != oldText)
                    _textChanged = true;

                // Escape to unfocus
                if (UIRenderer.CheckInputEscape())
                    Unfocus();
            }

            // Draw text or placeholder (overflow-safe)
            bool hasText = !string.IsNullOrEmpty(_text);
            int textAreaWidth = width - padding * 2;
            if (hasText) textAreaWidth -= ClearBtnWidth + 8; // reserve space for clear button

            string displayText;
            Color4 textColor;
            if (_focused)
            {
                // Show tail so cursor is always visible
                displayText = TextUtil.VisibleTail(_text + "|", textAreaWidth);
                textColor = UIColors.Text;
            }
            else if (!hasText)
            {
                displayText = TextUtil.Truncate(_placeholder, textAreaWidth);
                textColor = UIColors.TextHint;
            }
            else
            {
                displayText = TextUtil.Truncate(_text, textAreaWidth);
                textColor = UIColors.Text;
            }

            int textY = y + (height - 16) / 2;
            UIRenderer.DrawText(displayText, x + padding, textY, textColor);

            return _text;
        }

        /// <summary>
        /// Programmatically focus the text input.
        /// </summary>
        public void Focus()
        {
            if (!_focused)
            {
                _focused = true;
                UIRenderer.ClearInput();
                if (!string.IsNullOrEmpty(KeyBlockId))
                    UIRenderer.RegisterKeyInputBlock(KeyBlockId);
            }
        }

        /// <summary>
        /// Unfocus the text input and disable text input mode.
        /// </summary>
        public void Unfocus()
        {
            if (_focused)
            {
                _focused = false;
                UIRenderer.DisableTextInput();
                if (!string.IsNullOrEmpty(KeyBlockId))
                    UIRenderer.UnregisterKeyInputBlock(KeyBlockId);
            }
        }

        /// <summary>
        /// Clear the text and mark as changed.
        /// </summary>
        public void Clear()
        {
            if (!string.IsNullOrEmpty(_text))
            {
                _text = "";
                _textChanged = true;
            }
        }
    }
}
