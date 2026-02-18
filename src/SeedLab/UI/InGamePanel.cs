using System;
using System.Collections.Generic;
using TerrariaModder.Core.Input;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.UI;

namespace SeedLab.UI
{
    /// <summary>
    /// In-game panel for toggling seed features with normal/advanced mode.
    /// </summary>
    public class InGamePanel
    {
        private const string PanelId = "seed-lab";

        // Layout
        private const int CharWidth = 10;
        private const int PanelWidth = 420;
        private const int MinPanelHeight = 200;
        private const int MaxPanelHeight = 600;
        private const int HeaderHeight = 35;
        private const int ToolbarRowHeight = 34;
        private const int ToolbarHeight = ToolbarRowHeight * 2;
        private const int Padding = 8;
        private const int ToggleHeight = 24;
        private const int ToggleSpacing = 2;
        private const int SeedHeaderHeight = 22;
        private const int GroupHeaderHeight = 22;
        private const int ScrollbarWidth = 10;
        private const int CheckboxSize = 16;
        private const int ButtonHeight = 26;
        private const int ButtonPadX = 20;

        // Preset naming
        private const int PresetInputWidth = 140;
        private const int PresetInputHeight = 26;

        private readonly ILogger _log;
        private readonly FeatureManager _manager;
        private readonly PresetManager _presetManager;

        // Panel state
        private bool _visible;
        private int _panelX = -1, _panelY = -1;
        private bool _isDragging;
        private int _dragOffsetX, _dragOffsetY;

        // Scroll state
        private int _scrollOffset;
        private int _contentHeight;
        private bool _scrollDragging;

        // Modal state
        private bool _showInfoModal;

        // Preset input state
        private bool _presetNaming;
        private string _presetNameText = "";

        // Collapsed seeds
        private readonly HashSet<string> _collapsedSeeds = new HashSet<string>();

        // Preset popup
        private bool _showPresetList;

        // Deferred tooltip (drawn last so it's on top)
        private string _tooltipText;
        private int _tooltipMouseX, _tooltipMouseY;

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (_visible)
                {
                    if (_panelX < 0) _panelX = (UIRenderer.ScreenWidth - PanelWidth) / 2;
                    if (_panelY < 0) _panelY = (UIRenderer.ScreenHeight - MaxPanelHeight) / 2;
                    UIRenderer.RegisterPanelBounds(PanelId, _panelX, _panelY, PanelWidth, MaxPanelHeight);
                }
                else
                {
                    UIRenderer.UnregisterPanelBounds(PanelId);
                    _showInfoModal = false;
                    if (_presetNaming) ExitPresetNaming();
                    _showPresetList = false;
                }
            }
        }

        public InGamePanel(ILogger log, FeatureManager manager, PresetManager presetManager)
        {
            _log = log;
            _manager = manager;
            _presetManager = presetManager;
        }

        public void Draw()
        {
            if (!_visible || !_manager.Initialized) return;

            // Escape to close
            if (InputState.IsKeyJustPressed(KeyCode.Escape))
            {
                if (_showInfoModal)
                    _showInfoModal = false;
                else if (_presetNaming)
                    ExitPresetNaming();
                else if (_showPresetList)
                    _showPresetList = false;
                else
                    Visible = false;
                return;
            }

            bool blockInput = UIRenderer.ShouldBlockForHigherPriorityPanel(PanelId);
            // Block main panel interaction when modal/overlay is open
            bool modalActive = _showInfoModal || _showPresetList || _presetNaming;

            // Clear deferred tooltip each frame
            _tooltipText = null;

            try
            {
                // Clamp position
                _panelX = Math.Max(0, Math.Min(_panelX, UIRenderer.ScreenWidth - PanelWidth));
                _panelY = Math.Max(0, Math.Min(_panelY, UIRenderer.ScreenHeight - MaxPanelHeight));

                HandleDragging(blockInput || modalActive);

                // Calculate content height
                _contentHeight = CalculateContentHeight();
                int panelHeight = Math.Min(MaxPanelHeight, Math.Max(MinPanelHeight, _contentHeight + HeaderHeight + ToolbarHeight + Padding * 2));

                int x = _panelX, y = _panelY;
                UIRenderer.RegisterPanelBounds(PanelId, x, y, PanelWidth, panelHeight);

                // Background
                UIRenderer.DrawPanel(x, y, PanelWidth, panelHeight, UIColors.PanelBg);

                // Header
                DrawHeader(x, y, blockInput);

                // Toolbar (mode toggle, preset buttons)
                DrawToolbar(x, y + HeaderHeight, blockInput || _showInfoModal);

                // Content area with clipping
                int contentY = y + HeaderHeight + ToolbarHeight;
                int contentAreaHeight = panelHeight - HeaderHeight - ToolbarHeight - Padding;
                bool contentBlocked = blockInput || modalActive;

                // Clamp scroll
                int maxScroll = Math.Max(0, _contentHeight - contentAreaHeight);
                _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));

                // Virtual scrolling with strict bounds — items outside viewTop..viewBottom are skipped entirely
                DrawContent(x, contentY - _scrollOffset, contentBlocked, contentY, contentY + contentAreaHeight);

                // Scrollbar
                if (_contentHeight > contentAreaHeight)
                {
                    DrawScrollbar(x + PanelWidth - ScrollbarWidth - 2, contentY, ScrollbarWidth,
                        contentAreaHeight, _contentHeight, contentBlocked);
                }

                // Handle scroll wheel (blocked by modals and overlays)
                if (UIRenderer.IsMouseOver(x, y, PanelWidth, panelHeight) && !blockInput && !modalActive)
                {
                    int scroll = UIRenderer.ScrollWheel;
                    if (scroll != 0)
                    {
                        _scrollOffset -= scroll * 30;
                        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
                        UIRenderer.ConsumeScroll();
                    }
                }

                // Draw deferred tooltip on top of all content
                if (!string.IsNullOrEmpty(_tooltipText))
                    DrawTooltip(_tooltipMouseX, _tooltipMouseY, _tooltipText);

                // Draw overlays BEFORE consuming remaining clicks so overlay handlers get first pick
                if (_showInfoModal)
                    DrawInfoModal(blockInput);

                if (_showPresetList)
                    DrawPresetList(blockInput);

                // Consume remaining clicks in panel area to prevent game click-through
                if (UIRenderer.IsMouseOver(x, y, PanelWidth, panelHeight) && !blockInput)
                {
                    if (UIRenderer.MouseLeftClick) UIRenderer.ConsumeClick();
                    if (UIRenderer.MouseRightClick) UIRenderer.ConsumeRightClick();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"[SeedLab] Draw error: {ex}");
            }
        }

        #region Header & Toolbar

        private void DrawHeader(int x, int y, bool blockInput)
        {
            UIRenderer.DrawRect(x, y, PanelWidth, HeaderHeight, UIColors.HeaderBg);

            // Draw mod icon
            TerrariaModder.Core.PluginLoader.LoadModIcons();
            var icon = TerrariaModder.Core.PluginLoader.GetMod("seed-lab")?.IconTexture ?? TerrariaModder.Core.PluginLoader.DefaultIcon;
            int titleX = x + 10;
            if (icon != null)
            {
                UIRenderer.DrawTexture(icon, x + 8, y + 6, 22, 22);
                titleX = x + 34;
            }
            UIRenderer.DrawTextShadow("Seed Lab", titleX, y + 9, UIColors.TextTitle);

            // Close button
            int closeX = x + PanelWidth - 35;
            bool closeHover = UIRenderer.IsMouseOver(closeX, y + 3, 30, 30) && !blockInput;
            UIRenderer.DrawRect(closeX, y + 3, 30, 30, closeHover ? UIColors.CloseBtnHover : UIColors.CloseBtn);
            UIRenderer.DrawText("X", closeX + 11, y + 10, UIColors.Text);

            if (closeHover && UIRenderer.MouseLeftClick)
            {
                Visible = false;
                UIRenderer.ConsumeClick();
            }
        }

        private void DrawToolbar(int x, int y, bool blockInput)
        {
            UIRenderer.DrawRect(x, y, PanelWidth, ToolbarHeight, UIColors.SectionBg);

            // --- Row 1: Mode toggle + More Info ---
            int row1Y = y + (ToolbarRowHeight - ButtonHeight) / 2;
            int bx = x + Padding;

            string modeLabel = _manager.AdvancedMode ? "Advanced" : "Normal";
            int modeW = modeLabel.Length * CharWidth + ButtonPadX;
            bool modeHover = UIRenderer.IsMouseOver(bx, row1Y, modeW, ButtonHeight) && !blockInput;
            UIRenderer.DrawRect(bx, row1Y, modeW, ButtonHeight,
                _manager.AdvancedMode ? UIColors.Warning.WithAlpha(100) : UIColors.Button);
            if (modeHover)
                UIRenderer.DrawRect(bx, row1Y, modeW, ButtonHeight, UIColors.ButtonHover);
            UIRenderer.DrawText(modeLabel, bx + ButtonPadX / 2, row1Y + (ButtonHeight - 16) / 2, UIColors.Text);

            if (modeHover && UIRenderer.MouseLeftClick)
            {
                _manager.AdvancedMode = !_manager.AdvancedMode;
                UIRenderer.ConsumeClick();
            }
            bx += modeW + 6;

            if (_manager.AdvancedMode)
            {
                UIRenderer.DrawText("!", bx, row1Y + (ButtonHeight - 16) / 2, UIColors.Warning);
                bx += 14;
                int infoW = "More Info".Length * CharWidth + 4;
                bool infoHover = UIRenderer.IsMouseOver(bx, row1Y, infoW, ButtonHeight) && !blockInput;
                UIRenderer.DrawText("More Info", bx, row1Y + (ButtonHeight - 16) / 2, infoHover ? UIColors.AccentText : UIColors.Warning);
                if (infoHover && UIRenderer.MouseLeftClick)
                {
                    _showInfoModal = true;
                    UIRenderer.ConsumeClick();
                }
            }

            // --- Row 2: Save + Presets (or naming UI) ---
            int row2Y = y + ToolbarRowHeight + (ToolbarRowHeight - ButtonHeight) / 2;

            // Thin separator between rows
            UIRenderer.DrawRect(x + Padding, y + ToolbarRowHeight, PanelWidth - Padding * 2, 1, UIColors.Border);

            if (_presetNaming)
            {
                // Layout: [input field] [Confirm] [Cancel] left-to-right
                int lx = x + Padding;

                UIRenderer.DrawRect(lx, row2Y, PresetInputWidth, PresetInputHeight, UIColors.InputBg);
                UIRenderer.DrawRectOutline(lx, row2Y, PresetInputWidth, PresetInputHeight, UIColors.Border);

                // Use UIRenderer text input system
                UIRenderer.EnableTextInput();
                UIRenderer.HandleIME();
                _presetNameText = UIRenderer.GetInputText(_presetNameText);
                if (_presetNameText.Length > 20)
                    _presetNameText = _presetNameText.Substring(0, 20);

                string displayText = _presetNameText + "_";
                int inputMaxChars = (PresetInputWidth - 10) / CharWidth;
                UIRenderer.DrawText(TruncateText(displayText, inputMaxChars), lx + 5, row2Y + (PresetInputHeight - 16) / 2, UIColors.Text);

                if (UIRenderer.CheckInputEscape())
                    ExitPresetNaming();

                if (InputState.IsKeyJustPressed(KeyCode.Enter) && _presetNameText.Length > 0)
                {
                    _presetManager.SavePreset(_presetNameText, _manager);
                    ExitPresetNaming();
                }

                lx += PresetInputWidth + 4;

                // Confirm button
                int confirmW = "Confirm".Length * CharWidth + ButtonPadX;
                bool confirmHover = UIRenderer.IsMouseOver(lx, row2Y, confirmW, ButtonHeight) && !blockInput;
                UIRenderer.DrawRect(lx, row2Y, confirmW, ButtonHeight, confirmHover ? UIColors.Success.WithAlpha(150) : UIColors.Button);
                UIRenderer.DrawText("Confirm", lx + (confirmW - "Confirm".Length * CharWidth) / 2, row2Y + (ButtonHeight - 16) / 2, UIColors.Text);

                if (confirmHover && UIRenderer.MouseLeftClick && _presetNameText.Length > 0)
                {
                    _presetManager.SavePreset(_presetNameText, _manager);
                    ExitPresetNaming();
                    UIRenderer.ConsumeClick();
                }
                lx += confirmW + 4;

                // Cancel button
                int cancelW = "Cancel".Length * CharWidth + ButtonPadX;
                bool cancelHover = UIRenderer.IsMouseOver(lx, row2Y, cancelW, ButtonHeight) && !blockInput;
                UIRenderer.DrawRect(lx, row2Y, cancelW, ButtonHeight, cancelHover ? UIColors.ButtonHover : UIColors.Button);
                UIRenderer.DrawText("Cancel", lx + (cancelW - "Cancel".Length * CharWidth) / 2, row2Y + (ButtonHeight - 16) / 2, UIColors.TextDim);

                if (cancelHover && UIRenderer.MouseLeftClick)
                {
                    ExitPresetNaming();
                    UIRenderer.ConsumeClick();
                }
            }
            else
            {
                // Save button (left)
                int lx = x + Padding;
                int saveW = "Save".Length * CharWidth + ButtonPadX;
                bool saveHover = UIRenderer.IsMouseOver(lx, row2Y, saveW, ButtonHeight) && !blockInput;
                UIRenderer.DrawRect(lx, row2Y, saveW, ButtonHeight, saveHover ? UIColors.ButtonHover : UIColors.Button);
                UIRenderer.DrawText("Save", lx + (saveW - "Save".Length * CharWidth) / 2, row2Y + (ButtonHeight - 16) / 2, UIColors.TextDim);

                if (saveHover && UIRenderer.MouseLeftClick)
                {
                    _presetNaming = true;
                    _presetNameText = "";
                    UIRenderer.ClearInput();
                    UIRenderer.RegisterKeyInputBlock("seed-lab-preset");
                    UIRenderer.ConsumeClick();
                }

                // Presets button (right)
                int rightX = x + PanelWidth - Padding;
                int presetsW = "Presets".Length * CharWidth + ButtonPadX;
                int presetsX = rightX - presetsW;
                bool presetsHover = UIRenderer.IsMouseOver(presetsX, row2Y, presetsW, ButtonHeight) && !blockInput;
                UIRenderer.DrawRect(presetsX, row2Y, presetsW, ButtonHeight, presetsHover ? UIColors.ButtonHover : UIColors.Button);
                UIRenderer.DrawText("Presets", presetsX + (presetsW - "Presets".Length * CharWidth) / 2, row2Y + (ButtonHeight - 16) / 2, UIColors.TextDim);

                if (presetsHover && UIRenderer.MouseLeftClick)
                {
                    _showPresetList = !_showPresetList;
                    UIRenderer.ConsumeClick();
                }
            }
        }

        private void ExitPresetNaming()
        {
            _presetNaming = false;
            _presetNameText = "";
            UIRenderer.DisableTextInput();
            UIRenderer.ClearInput();
            UIRenderer.UnregisterKeyInputBlock("seed-lab-preset");
        }

        public void Update()
        {
            if (!_visible || !_presetNaming) return;
            UIRenderer.EnableTextInput();
        }

        #endregion

        #region Content Drawing

        private int CalculateContentHeight()
        {
            int height = 0;
            foreach (var seed in SeedFeatures.Seeds)
            {
                height += SeedHeaderHeight + ToggleSpacing;

                if (_collapsedSeeds.Contains(seed.Id)) continue;

                if (_manager.AdvancedMode)
                {
                    foreach (var group in seed.Groups)
                    {
                        height += GroupHeaderHeight + ToggleSpacing;
                        foreach (var feature in group.Features)
                        {
                            height += ToggleHeight + ToggleSpacing;
                        }
                    }
                }
                else
                {
                    foreach (var group in seed.Groups)
                    {
                        height += ToggleHeight + ToggleSpacing;
                    }
                }

                height += Padding;
            }
            return height + Padding;
        }

        private void DrawContent(int x, int y, bool blockInput, int viewTop, int viewBottom)
        {
            int cy = y + Padding;
            int contentWidth = PanelWidth - Padding * 2 - (_contentHeight > MaxPanelHeight - HeaderHeight - ToolbarHeight ? ScrollbarWidth + 4 : 0);

            foreach (var seed in SeedFeatures.Seeds)
            {
                cy = DrawSeedSection(x + Padding, cy, contentWidth, seed, blockInput, viewTop, viewBottom);
            }
        }

        private int DrawSeedSection(int x, int y, int width, SeedDefinition seed, bool blockInput, int viewTop, int viewBottom)
        {
            // Seed header (collapsible)
            bool collapsed = _collapsedSeeds.Contains(seed.Id);
            bool anyEnabled = _manager.IsSeedAnyEnabled(seed.Id);
            bool headerVisible = (y >= viewTop) && (y + SeedHeaderHeight <= viewBottom);

            if (headerVisible)
            {
                // Header background
                UIRenderer.DrawRect(x, y, width, SeedHeaderHeight, UIColors.HeaderBg.WithAlpha(180));

                // Collapse arrow
                string arrow = collapsed ? ">" : "v";
                UIRenderer.DrawText(arrow, x + 4, y + 4, UIColors.TextDim);

                // Seed name
                UIRenderer.DrawTextShadow(seed.DisplayName.ToUpper(), x + 18, y + 4,
                    anyEnabled ? UIColors.TextTitle : UIColors.TextDim);

                // Seed-level indicator
                if (anyEnabled)
                    UIRenderer.DrawRect(x + width - 8, y + 4, 4, 14, UIColors.Success);

                // Click to collapse/expand
                bool headerHover = UIRenderer.IsMouseOver(x, y, width, SeedHeaderHeight) && !blockInput;
                if (headerHover && UIRenderer.MouseLeftClick)
                {
                    if (collapsed)
                        _collapsedSeeds.Remove(seed.Id);
                    else
                        _collapsedSeeds.Add(seed.Id);
                    UIRenderer.ConsumeClick();
                }
            }

            y += SeedHeaderHeight + ToggleSpacing;

            if (collapsed) return y;

            if (_manager.AdvancedMode)
            {
                // Advanced: show groups with individual feature toggles
                foreach (var group in seed.Groups)
                {
                    // Group header
                    bool groupHeaderVisible = (y >= viewTop) && (y + GroupHeaderHeight <= viewBottom);
                    if (groupHeaderVisible)
                    {
                        UIRenderer.DrawText(group.DisplayName, x + 8, y + 3, UIColors.AccentText);

                        // Group description tooltip on hover
                        bool groupHeaderHover = UIRenderer.IsMouseOver(x, y, width, GroupHeaderHeight) && !blockInput;
                        if (groupHeaderHover)
                            SetTooltip(group.Description);
                    }
                    y += GroupHeaderHeight + ToggleSpacing;

                    // Individual features
                    foreach (var feature in group.Features)
                    {
                        y = DrawFeatureToggle(x + 16, y, width - 16, feature, blockInput, viewTop, viewBottom);
                    }
                }
            }
            else
            {
                // Normal: show groups as single toggles
                foreach (var group in seed.Groups)
                {
                    y = DrawGroupToggle(x + 8, y, width - 8, group, blockInput, viewTop, viewBottom);
                }
            }

            y += Padding;
            return y;
        }

        private int DrawGroupToggle(int x, int y, int width, FeatureGroupDefinition group, bool blockInput, int viewTop, int viewBottom)
        {
            // Strict bounds: skip if any part is outside the viewport
            if (y < viewTop || y + ToggleHeight > viewBottom)
                return y + ToggleHeight + ToggleSpacing;

            bool enabled = _manager.IsGroupEnabled(group.Id);
            bool partial = _manager.IsGroupPartial(group.Id);
            bool hover = UIRenderer.IsMouseOver(x, y, width, ToggleHeight) && !blockInput;

            // Background
            Color4 bg;
            if (enabled)
                bg = hover ? UIColors.Success.WithAlpha(80) : UIColors.Success.WithAlpha(50);
            else if (partial)
                bg = hover ? UIColors.Warning.WithAlpha(80) : UIColors.Warning.WithAlpha(50);
            else
                bg = hover ? UIColors.ButtonHover : UIColors.Button;
            UIRenderer.DrawRect(x, y, width, ToggleHeight, bg);

            // Checkbox
            DrawCheckbox(x + 4, y + (ToggleHeight - CheckboxSize) / 2, enabled, partial);

            // Label (truncate to fit)
            int groupMaxChars = (width - CheckboxSize - 14) / CharWidth;
            UIRenderer.DrawText(TruncateText(group.DisplayName, groupMaxChars), x + CheckboxSize + 10, y + 4,
                enabled ? UIColors.Text : UIColors.TextDim);

            // Tooltip on hover
            if (hover)
                SetTooltip(group.Description);

            // Click to toggle
            if (hover && UIRenderer.MouseLeftClick)
            {
                _manager.ToggleGroup(group.Id);
                _manager.SaveState();
                UIRenderer.ConsumeClick();
            }

            return y + ToggleHeight + ToggleSpacing;
        }

        private int DrawFeatureToggle(int x, int y, int width, FeatureDefinition feature, bool blockInput, int viewTop, int viewBottom)
        {
            // Strict bounds: skip if any part is outside the viewport
            if (y < viewTop || y + ToggleHeight > viewBottom)
                return y + ToggleHeight + ToggleSpacing;

            bool enabled = _manager.IsFeatureEnabled(feature.Id);
            bool hover = UIRenderer.IsMouseOver(x, y, width, ToggleHeight) && !blockInput;

            // Background
            Color4 bg = enabled
                ? (hover ? UIColors.Success.WithAlpha(60) : UIColors.Success.WithAlpha(35))
                : (hover ? UIColors.ButtonHover : new Color4(40, 40, 55, 180));
            UIRenderer.DrawRect(x, y, width, ToggleHeight, bg);

            // Checkbox
            DrawCheckbox(x + 4, y + (ToggleHeight - CheckboxSize) / 2, enabled, false);

            // Label (truncate to fit)
            int featureMaxChars = (width - CheckboxSize - 14) / CharWidth;
            UIRenderer.DrawText(TruncateText(feature.DisplayName, featureMaxChars), x + CheckboxSize + 10, y + 4,
                enabled ? UIColors.Text : UIColors.TextDim);

            // Tooltip on hover
            if (hover)
                SetTooltip(feature.Description);

            // Click to toggle
            if (hover && UIRenderer.MouseLeftClick)
            {
                _manager.ToggleFeature(feature.Id);
                _manager.SaveState();
                UIRenderer.ConsumeClick();
            }

            return y + ToggleHeight + ToggleSpacing;
        }

        private void DrawCheckbox(int x, int y, bool on, bool partial)
        {
            UIRenderer.DrawRect(x, y, CheckboxSize, CheckboxSize, UIColors.InputBg);
            UIRenderer.DrawRectOutline(x, y, CheckboxSize, CheckboxSize, UIColors.Border);

            if (on)
            {
                // Filled check
                UIRenderer.DrawRect(x + 3, y + 3, CheckboxSize - 6, CheckboxSize - 6, UIColors.Success);
            }
            else if (partial)
            {
                // Partial indicator (dash)
                UIRenderer.DrawRect(x + 3, y + 6, CheckboxSize - 6, 4, UIColors.Warning);
            }
        }

        #endregion

        #region Overlays

        private void DrawInfoModal(bool blockInput)
        {
            // Dim background
            UIRenderer.DrawRect(0, 0, UIRenderer.ScreenWidth, UIRenderer.ScreenHeight, new Color4(0, 0, 0, 150));

            int mw = 480;
            int mPad = 16;
            int lineH = 20;
            int headerH = 34;
            int maxCharsPerLine = (mw - mPad * 2) / CharWidth;

            // Full description as a single string, word-wrapped
            string fullText =
                "Normal mode groups related effects together so they stay balanced. " +
                "Advanced mode lets you toggle individual effects independently.\n\n" +
                "Some things to know:\n\n" +
                "- Enabling part of a boss's changes but not all of them can make fights unbalanced\n\n" +
                "- Some visual effects may look odd without their paired gameplay changes\n\n" +
                "- If something breaks, switch back to Normal mode - it resets to safe grouped defaults";

            // Word-wrap into lines
            var lines = new List<string>();
            foreach (string paragraph in fullText.Split('\n'))
            {
                if (paragraph.Length == 0)
                {
                    lines.Add("");
                    continue;
                }
                string[] words = paragraph.Split(' ');
                string currentLine = "";
                foreach (string word in words)
                {
                    if (currentLine.Length == 0)
                        currentLine = word;
                    else if (currentLine.Length + 1 + word.Length <= maxCharsPerLine)
                        currentLine += " " + word;
                    else
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                }
                if (currentLine.Length > 0) lines.Add(currentLine);
            }

            // Calculate modal height from content
            int btnH = 28;
            int mh = headerH + mPad + lines.Count * lineH + mPad + btnH + mPad;

            int mx = (UIRenderer.ScreenWidth - mw) / 2;
            int my = (UIRenderer.ScreenHeight - mh) / 2;

            UIRenderer.DrawPanel(mx, my, mw, mh, UIColors.PanelBg);
            UIRenderer.DrawRect(mx, my, mw, headerH, UIColors.HeaderBg);
            UIRenderer.DrawTextShadow("Advanced Mode", mx + 12, my + 9, UIColors.TextTitle);

            int ty = my + headerH + mPad;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length > 0)
                    UIRenderer.DrawText(lines[i], mx + mPad, ty, UIColors.TextDim);
                ty += lineH;
            }

            // Got it button
            int btnW = "Got it".Length * CharWidth + ButtonPadX;
            int btnX = mx + (mw - btnW) / 2;
            int btnY = my + mh - btnH - mPad;
            bool btnHover = UIRenderer.IsMouseOver(btnX, btnY, btnW, btnH) && !blockInput;
            UIRenderer.DrawRect(btnX, btnY, btnW, btnH, btnHover ? UIColors.ButtonHover : UIColors.Button);
            UIRenderer.DrawText("Got it", btnX + (btnW - "Got it".Length * CharWidth) / 2, btnY + (btnH - 16) / 2, UIColors.Text);

            if (btnHover && UIRenderer.MouseLeftClick)
            {
                _showInfoModal = false;
                UIRenderer.ConsumeClick();
            }
        }

        private void DrawPresetList(bool blockInput)
        {
            int listW = 220;
            int listX = _panelX + PanelWidth - listW - Padding;
            int listY = _panelY + HeaderHeight + ToolbarHeight + 4;

            var presets = _presetManager.Presets;
            int itemH = 28;
            int listH = Math.Max(itemH + 8, (presets.Count + 1) * itemH + 8);

            UIRenderer.DrawPanel(listX, listY, listW, listH, new Color4(25, 25, 40, 250));
            UIRenderer.DrawRectOutline(listX, listY, listW, listH, UIColors.Border);

            int iy = listY + 4;

            if (presets.Count == 0)
            {
                UIRenderer.DrawText("No presets saved", listX + 10, iy + 6, UIColors.TextHint);
                iy += itemH;
            }
            else
            {
                foreach (var kvp in presets)
                {
                    bool hover = UIRenderer.IsMouseOver(listX + 2, iy, listW - 4, itemH) && !blockInput;
                    if (hover)
                        UIRenderer.DrawRect(listX + 2, iy, listW - 4, itemH, UIColors.ButtonHover);

                    int presetMaxChars = (listW - 44) / CharWidth;
                    UIRenderer.DrawText(TruncateText(kvp.Key, presetMaxChars), listX + 10, iy + 6, UIColors.Text);

                    // Delete button (right side)
                    int delX = listX + listW - 28;
                    bool delHover = UIRenderer.IsMouseOver(delX, iy + 2, 24, 24) && !blockInput;
                    if (delHover)
                        UIRenderer.DrawRect(delX, iy + 2, 24, 24, UIColors.CloseBtnHover);
                    UIRenderer.DrawText("x", delX + 7, iy + 6, delHover ? UIColors.Error : UIColors.TextHint);

                    if (delHover && UIRenderer.MouseLeftClick)
                    {
                        _presetManager.DeletePreset(kvp.Key);
                        UIRenderer.ConsumeClick();
                        return; // Collection modified, exit
                    }

                    // Click to apply
                    if (hover && !delHover && UIRenderer.MouseLeftClick)
                    {
                        _presetManager.ApplyPreset(kvp.Key, _manager);
                        _manager.SaveState();
                        _showPresetList = false;
                        UIRenderer.ConsumeClick();
                        return;
                    }

                    iy += itemH;
                }
            }

            // Click outside to close (consume click to prevent pass-through)
            if (UIRenderer.MouseLeftClick && !UIRenderer.IsMouseOver(listX, listY, listW, listH))
            {
                _showPresetList = false;
                UIRenderer.ConsumeClick();
            }
        }

        #endregion

        #region Helpers

        private void SetTooltip(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _tooltipText = text;
                _tooltipMouseX = UIRenderer.MouseX;
                _tooltipMouseY = UIRenderer.MouseY;
            }
        }

        private static string TruncateText(string text, int maxChars)
        {
            if (maxChars < 4 || string.IsNullOrEmpty(text)) return text ?? "";
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars - 3) + "...";
        }

        private void DrawTooltip(int tipMouseX, int tipMouseY, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            int maxWidth = 350;
            int lineHeight = 18;
            int pad = 8;
            int maxCharsPerLine = (maxWidth - pad * 2) / CharWidth;

            // Word-wrap
            var lines = new List<string>();
            string[] words = text.Split(' ');
            string currentLine = "";
            foreach (string word in words)
            {
                if (currentLine.Length == 0)
                    currentLine = word;
                else if (currentLine.Length + 1 + word.Length <= maxCharsPerLine)
                    currentLine += " " + word;
                else
                {
                    lines.Add(currentLine);
                    currentLine = word;
                }
            }
            if (currentLine.Length > 0) lines.Add(currentLine);

            // Truncate any absurdly long lines
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length > maxCharsPerLine + 5)
                    lines[i] = lines[i].Substring(0, maxCharsPerLine + 2) + "...";
            }

            int boxW = maxWidth;
            int boxH = lines.Count * lineHeight + pad * 2;

            // Position below-right of cursor, clamped to screen
            int tx = tipMouseX + 12;
            int ty = tipMouseY + 18;
            if (tx + boxW > UIRenderer.ScreenWidth) tx = tipMouseX - boxW - 4;
            if (ty + boxH > UIRenderer.ScreenHeight) ty = tipMouseY - boxH - 4;
            if (tx < 0) tx = 0;
            if (ty < 0) ty = 0;

            UIRenderer.DrawRect(tx, ty, boxW, boxH, UIColors.TooltipBg);
            UIRenderer.DrawRectOutline(tx, ty, boxW, boxH, UIColors.Border);
            for (int i = 0; i < lines.Count; i++)
            {
                UIRenderer.DrawText(lines[i], tx + pad, ty + pad + i * lineHeight, UIColors.TextDim);
            }
        }

        private void DrawScrollbar(int x, int y, int w, int areaH, int contentH, bool blockInput)
        {
            // Track
            UIRenderer.DrawRect(x, y, w, areaH, UIColors.ScrollTrack);

            // Thumb
            float ratio = (float)areaH / contentH;
            int thumbH = Math.Max(20, (int)(areaH * ratio));
            float scrollRatio = contentH > areaH ? (float)_scrollOffset / (contentH - areaH) : 0;
            int thumbY = y + (int)(scrollRatio * (areaH - thumbH));

            bool thumbHover = UIRenderer.IsMouseOver(x, thumbY, w, thumbH) && !blockInput;
            UIRenderer.DrawRect(x, thumbY, w, thumbH, (thumbHover || _scrollDragging) ? UIColors.SliderThumbHover : UIColors.ScrollThumb);

            // Drag handling — click thumb or track to start
            if (!_scrollDragging && UIRenderer.MouseLeftClick && !blockInput
                && UIRenderer.IsMouseOver(x, y, w, areaH))
                _scrollDragging = true;

            if (_scrollDragging)
            {
                if (UIRenderer.MouseLeft)
                {
                    float mouseRatio = Math.Max(0, Math.Min(1, (float)(UIRenderer.MouseY - y - thumbH / 2) / (areaH - thumbH)));
                    _scrollOffset = (int)(mouseRatio * (contentH - areaH));
                }
                else
                    _scrollDragging = false;
            }
        }

        private void HandleDragging(bool blockInput)
        {
            bool inHeader = UIRenderer.IsMouseOver(_panelX, _panelY, PanelWidth - 40, HeaderHeight) && !blockInput;

            if (UIRenderer.MouseLeftClick && inHeader && !_isDragging)
            {
                _isDragging = true;
                _dragOffsetX = UIRenderer.MouseX - _panelX;
                _dragOffsetY = UIRenderer.MouseY - _panelY;
            }

            if (_isDragging)
            {
                if (UIRenderer.MouseLeft)
                {
                    _panelX = UIRenderer.MouseX - _dragOffsetX;
                    _panelY = UIRenderer.MouseY - _dragOffsetY;
                }
                else
                    _isDragging = false;
            }
        }

        #endregion
    }
}
