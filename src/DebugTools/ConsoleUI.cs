using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using TerrariaModder.Core;
using TerrariaModder.Core.Debug;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Input;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.UI;

namespace DebugTools
{
    /// <summary>
    /// In-game debug console with command history, tab completion, and scrollable output.
    /// Extracted from the DebugConsole mod.
    /// </summary>
    internal class ConsoleUI
    {
        private ILogger _log;
        private bool _initialized;

        // Console state
        private bool _isOpen;
        private string _inputText = "";
        private readonly List<OutputLine> _outputLines = new List<OutputLine>();
        private readonly object _outputLock = new object();
        private int _scrollOffset;
        private const int MaxInputLength = 500;

        // Command history
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex = -1;
        private string _savedInput = "";
        private const int MaxHistory = 50;
        private const int MaxOutputLines = 200;

        // UI layout
        private const int ConsolePadding = 8;
        private const int LineHeight = 18;
        private const int InputHeight = 24;
        private const int HeaderHeight = 24;

        // Cursor blink
        private int _cursorBlinkTimer;
        private bool _cursorVisible = true;

        // Tab completion state
        private string _tabPrefix;
        private List<string> _tabMatches;
        private int _tabIndex;

        public void Initialize(ModContext context)
        {
            _log = context.Logger;

            // Register keybind
            context.RegisterKeybind("toggle-console", "Toggle Console", "Open/close the debug console", "Ctrl+OemTilde", OnToggle);

            // Subscribe to frame events
            FrameEvents.OnPreUpdate += OnUpdate;
            UIRenderer.RegisterPanelDraw("debug-console", OnDraw);

            // Subscribe to command output and clear events
            CommandRegistry.OnOutput += OnCommandOutput;
            CommandRegistry.OnClearOutput += OnClearOutput;

            // Register console-specific commands
            context.RegisterCommand("echo", "Print text to the console output", args =>
            {
                string text = args.Length > 0 ? string.Join(" ", args) : "";
                CommandRegistry.Write(text);
            });

            _initialized = true;
            _log.Info("Debug console initialized - Press Ctrl+` to open");
        }

        public void Close()
        {
            if (_isOpen)
                CloseConsole();
        }

        public void Cleanup()
        {
            if (_isOpen)
                CloseConsole();

            if (!_initialized) return;

            FrameEvents.OnPreUpdate -= OnUpdate;
            UIRenderer.UnregisterPanelDraw("debug-console");
            CommandRegistry.OnOutput -= OnCommandOutput;
            CommandRegistry.OnClearOutput -= OnClearOutput;

            lock (_outputLock)
            {
                _outputLines.Clear();
            }
            _commandHistory.Clear();

            _initialized = false;
        }

        private void OnCommandOutput(string message)
        {
            AddOutput(message);
        }

        private void OnClearOutput()
        {
            lock (_outputLock)
            {
                _outputLines.Clear();
                _scrollOffset = 0;
            }
        }

        private void OnToggle()
        {
            if (_isOpen)
                CloseConsole();
            else
                OpenConsole();
        }

        private void OpenConsole()
        {
            _isOpen = true;
            _inputText = "";
            _historyIndex = -1;
            _cursorBlinkTimer = 0;
            _cursorVisible = true;
            _tabPrefix = null;

            // Force-close chat so it doesn't compete for text input
            Main.drawingPlayerChat = false;

            // Register panel bounds for mouse/scroll blocking
            int consoleHeight = (int)(UIRenderer.ScreenHeight * 0.4f);
            UIRenderer.RegisterPanelBounds("debug-console", 0, 0, UIRenderer.ScreenWidth, consoleHeight);

            // Console always has text input — block keyboard
            UIRenderer.RegisterKeyInputBlock("debug-console");
            UIRenderer.EnableTextInput();
            UIRenderer.ClearInput();
            _log.Debug("Console opened");
        }

        private void CloseConsole()
        {
            _isOpen = false;
            UIRenderer.UnregisterPanelBounds("debug-console");
            UIRenderer.UnregisterKeyInputBlock("debug-console");
            UIRenderer.DisableTextInput();
            _tabPrefix = null;
            _log.Debug("Console closed");
        }

        private void OnUpdate()
        {
            if (!_isOpen) return;

            // Keep chat closed while console is open (belt-and-suspenders —
            // CurrentInputTextTakerOverride in EnableTextInput already blocks OpenPlayerChat,
            // but this handles any edge cases where chat slips through)
            Main.drawingPlayerChat = false;

            // Keep text input enabled during Update phase
            UIRenderer.EnableTextInput();
        }

        private void OnDraw()
        {
            if (!_isOpen) return;

            try
            {
                // Keep text input enabled during Draw phase
                UIRenderer.EnableTextInput();
                UIRenderer.HandleIME();

                // Check for escape to close
                if (UIRenderer.CheckInputEscape())
                {
                    CloseConsole();
                    return;
                }

                // Handle special keys before reading text input
                HandleSpecialKeys();

                // Read text input from Terraria's text input system
                string newText = UIRenderer.GetInputText(_inputText);
                if (newText != _inputText)
                {
                    // Cap input length to prevent visual overflow
                    if (newText != null && newText.Length > MaxInputLength)
                        newText = newText.Substring(0, MaxInputLength);
                    _inputText = newText ?? "";
                    _tabPrefix = null; // Reset tab completion on any text change
                }

                // Cursor blink
                _cursorBlinkTimer++;
                if (_cursorBlinkTimer >= 30)
                {
                    _cursorBlinkTimer = 0;
                    _cursorVisible = !_cursorVisible;
                }

                DrawConsole();
            }
            catch (Exception ex)
            {
                _log.Error($"Console error: {ex.Message}");
            }
        }

        private void HandleSpecialKeys()
        {
            // Enter - execute command
            if (InputState.IsKeyJustPressed(KeyCode.Enter))
            {
                ExecuteInput();
                return;
            }

            // Up arrow - previous command in history
            if (InputState.IsKeyJustPressed(KeyCode.Up))
            {
                NavigateHistory(-1);
                return;
            }

            // Down arrow - next command in history
            if (InputState.IsKeyJustPressed(KeyCode.Down))
            {
                NavigateHistory(1);
                return;
            }

            // Tab - command completion
            if (InputState.IsKeyJustPressed(KeyCode.Tab))
            {
                HandleTabCompletion();
                return;
            }

            // Page Up / Page Down - scroll output
            if (InputState.IsKeyJustPressed(KeyCode.PageUp))
            {
                int visibleLines = GetVisibleLineCount();
                lock (_outputLock)
                {
                    int maxScroll = Math.Max(0, _outputLines.Count - visibleLines);
                    _scrollOffset = Math.Min(_scrollOffset + visibleLines / 2, maxScroll);
                }
                return;
            }

            if (InputState.IsKeyJustPressed(KeyCode.PageDown))
            {
                int visibleLines = GetVisibleLineCount();
                lock (_outputLock)
                {
                    _scrollOffset = Math.Max(0, _scrollOffset - visibleLines / 2);
                }
                return;
            }
        }

        private void ExecuteInput()
        {
            string input = _inputText.Trim();
            _inputText = "";
            _historyIndex = -1;
            _tabPrefix = null;

            if (string.IsNullOrEmpty(input))
                return;

            // Add to history (avoid consecutive duplicates)
            if (_commandHistory.Count == 0 || _commandHistory[_commandHistory.Count - 1] != input)
            {
                _commandHistory.Add(input);
                if (_commandHistory.Count > MaxHistory)
                    _commandHistory.RemoveAt(0);
            }

            // Show the command in output
            AddOutput($"> {input}", UIColors.ConsoleCommand.R, UIColors.ConsoleCommand.G, UIColors.ConsoleCommand.B);

            // Execute via CommandRegistry
            bool found = CommandRegistry.Execute(input);
            if (!found)
            {
                AddOutput($"Unknown command: {input.Split(' ')[0]}", UIColors.Error.R, UIColors.Error.G, UIColors.Error.B);
                AddOutput("Type 'help' for a list of commands.", UIColors.TextHint.R, UIColors.TextHint.G, UIColors.TextHint.B);
            }
        }

        private void NavigateHistory(int direction)
        {
            if (_commandHistory.Count == 0)
                return;

            if (_historyIndex == -1 && direction == -1)
            {
                // Starting to navigate up - save current input
                _savedInput = _inputText;
                _historyIndex = _commandHistory.Count - 1;
            }
            else if (_historyIndex == -1 && direction == 1)
            {
                // At bottom, pressing down does nothing
                return;
            }
            else
            {
                _historyIndex += direction;
            }

            if (_historyIndex < 0)
            {
                _historyIndex = 0;
                return;
            }

            if (_historyIndex >= _commandHistory.Count)
            {
                // Past the end - restore saved input
                _historyIndex = -1;
                _inputText = _savedInput;
                _tabPrefix = null;
                return;
            }

            _inputText = _commandHistory[_historyIndex];
            _tabPrefix = null;
        }

        private void HandleTabCompletion()
        {
            if (string.IsNullOrEmpty(_inputText))
                return;

            // Get the command name portion (first word)
            string prefix = _inputText.Split(' ')[0].ToLowerInvariant();

            // If tab prefix changed or first tab press, rebuild match list
            if (_tabPrefix != prefix)
            {
                _tabPrefix = prefix;
                _tabMatches = CommandRegistry.GetCommands()
                    .Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Name)
                    .OrderBy(n => n)
                    .ToList();
                _tabIndex = 0;
            }
            else
            {
                // Cycle to next match
                _tabIndex = (_tabIndex + 1) % Math.Max(1, _tabMatches.Count);
            }

            if (_tabMatches != null && _tabMatches.Count > 0)
            {
                // Replace the command portion, keep any args
                string args = "";
                int spaceIdx = _inputText.IndexOf(' ');
                if (spaceIdx >= 0)
                    args = _inputText.Substring(spaceIdx);

                _inputText = _tabMatches[_tabIndex] + args;

                if (_tabMatches.Count > 1)
                {
                    // Show available completions
                    AddOutput($"  [{string.Join(", ", _tabMatches)}]", UIColors.TextHint.R, UIColors.TextHint.G, UIColors.TextHint.B);
                }
            }
        }

        private void DrawConsole()
        {
            int screenWidth = UIRenderer.ScreenWidth;
            int screenHeight = UIRenderer.ScreenHeight;

            // Console covers full width, ~40% height from top
            int consoleWidth = screenWidth;
            int consoleHeight = (int)(screenHeight * 0.4f);

            // Update panel bounds each frame (handles resolution changes)
            UIRenderer.RegisterPanelBounds("debug-console", 0, 0, consoleWidth, consoleHeight);

            // Check if a higher-z-order panel should block our input
            bool blockInput = UIRenderer.ShouldBlockForHigherPriorityPanel("debug-console");

            try
            {
                // Background
                UIRenderer.DrawRect(0, 0, consoleWidth, consoleHeight, UIColors.PanelBg);

                // Bottom border
                UIRenderer.DrawRect(0, consoleHeight - 2, consoleWidth, 2, UIColors.Divider);

                // Header
                DrawHeader(consoleWidth);

                // Output area
                int outputTop = HeaderHeight;
                int outputBottom = consoleHeight - InputHeight - ConsolePadding;
                int outputHeight = outputBottom - outputTop;
                DrawOutput(ConsolePadding, outputTop, consoleWidth - ConsolePadding * 2, outputHeight);

                // Input line
                DrawInputLine(ConsolePadding, consoleHeight - InputHeight - 2, consoleWidth - ConsolePadding * 2);

                // Consume all mouse input in the console area
                // Skip when a higher-z panel overlaps us — let the front panel process the click.
                if (UIRenderer.IsMouseOver(0, 0, consoleWidth, consoleHeight) && !blockInput)
                {
                    if (UIRenderer.MouseLeftClick) UIRenderer.ConsumeClick();
                    if (UIRenderer.MouseRightClick) UIRenderer.ConsumeRightClick();

                    // Handle scroll wheel for output scrolling
                    int scroll = UIRenderer.ScrollWheel;
                    if (scroll != 0)
                    {
                        lock (_outputLock)
                        {
                            _scrollOffset += scroll > 0 ? 3 : -3;
                            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, _outputLines.Count - GetVisibleLineCount())));
                        }
                        UIRenderer.ConsumeScroll();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Console draw error: {ex.Message}");
            }
        }

        private void DrawHeader(int width)
        {
            // Header background
            UIRenderer.DrawRect(0, 0, width, HeaderHeight, UIColors.HeaderBg);

            // Title
            UIRenderer.DrawText("Debug Console", ConsolePadding, 4, UIColors.ConsoleCommand);

            // Close hint on right
            string hint = "[Ctrl+`] Close  [Esc] Close";
            int hintWidth = UIRenderer.MeasureText(hint);
            UIRenderer.DrawText(hint, width - hintWidth - ConsolePadding, 4, UIColors.TextHint);
        }

        private void DrawOutput(int x, int y, int width, int height)
        {
            int visibleLines = height / LineHeight;
            if (visibleLines <= 0) return;

            // Snapshot the output lines under lock to avoid concurrent modification
            OutputLine[] snapshot;
            int scrollOffset;
            lock (_outputLock)
            {
                snapshot = _outputLines.ToArray();
                scrollOffset = _scrollOffset;
            }

            int totalLines = snapshot.Length;
            int startIndex = Math.Max(0, totalLines - visibleLines - scrollOffset);
            int endIndex = Math.Min(totalLines, startIndex + visibleLines);

            int drawY = y;
            for (int i = startIndex; i < endIndex; i++)
            {
                var line = snapshot[i];
                UIRenderer.DrawText(line.Text, x, drawY, line.R, line.G, line.B);
                drawY += LineHeight;
            }

            // Scroll indicator
            if (scrollOffset > 0)
            {
                string scrollText = $"[+{scrollOffset} more below]";
                UIRenderer.DrawText(scrollText, x + width - UIRenderer.MeasureText(scrollText), y + height - LineHeight, UIColors.TextHint);
            }
        }

        private void DrawInputLine(int x, int y, int width)
        {
            // Input background
            UIRenderer.DrawRect(x - 2, y - 2, width + 4, InputHeight, UIColors.InputBg);

            // Prompt
            UIRenderer.DrawText(">", x, y + 3, UIColors.ConsolePrompt);

            // Input text (show tail if text exceeds available width)
            int textX = x + 14;
            int maxTextWidth = width - 18;
            string fullText = _inputText ?? "";
            string displayText = fullText;
            if (UIRenderer.MeasureText(displayText) > maxTextWidth)
            {
                // Show the rightmost portion so the cursor stays visible
                int ellipsisW = UIRenderer.MeasureText("...");
                for (int i = displayText.Length - 1; i > 0; i--)
                {
                    string tail = displayText.Substring(i);
                    if (UIRenderer.MeasureText(tail) + ellipsisW > maxTextWidth)
                    {
                        displayText = "..." + displayText.Substring(i + 1);
                        break;
                    }
                }
            }
            UIRenderer.DrawText(displayText, textX, y + 3, UIColors.TextDim);

            // Cursor
            if (_cursorVisible)
            {
                int cursorX = textX + UIRenderer.MeasureText(displayText);
                UIRenderer.DrawRect(cursorX, y + 2, 2, InputHeight - 4, UIColors.TextDim);
            }
        }

        private int GetVisibleLineCount()
        {
            int screenHeight = UIRenderer.ScreenHeight;
            int consoleHeight = (int)(screenHeight * 0.4f);
            int outputHeight = consoleHeight - HeaderHeight - InputHeight - ConsolePadding;
            return Math.Max(1, outputHeight / LineHeight);
        }

        private void AddOutput(string text, byte r = 200, byte g = 200, byte b = 200)
        {
            // Split multi-line text
            if (text != null && text.Contains("\n"))
            {
                foreach (var line in text.Split('\n'))
                    AddOutputLine(line, r, g, b);
            }
            else
            {
                AddOutputLine(text ?? "", r, g, b);
            }
        }

        private void AddOutputLine(string text, byte r, byte g, byte b)
        {
            lock (_outputLock)
            {
                _outputLines.Add(new OutputLine(text, r, g, b));
                if (_outputLines.Count > MaxOutputLines)
                    _outputLines.RemoveAt(0);

                // Auto-scroll to bottom when new output arrives (unless user scrolled up)
                if (_scrollOffset <= 3)
                    _scrollOffset = 0;
            }
        }

        private struct OutputLine
        {
            public string Text;
            public byte R, G, B;

            public OutputLine(string text, byte r, byte g, byte b)
            {
                Text = text;
                R = r;
                G = g;
                B = b;
            }
        }
    }
}
