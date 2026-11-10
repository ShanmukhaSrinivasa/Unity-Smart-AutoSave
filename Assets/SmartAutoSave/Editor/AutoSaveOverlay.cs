using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.Overlays;

namespace Shanmukha.AutoSave.Editor
{
    
    // Register this class as a floating overlay widget inside the Scene view!
    [Overlay(typeof(SceneView), "Smart AutoSave", defaultDisplay = true)]
    public class AutoSaveOverlay : Overlay
    {
        private Label _timerLabel;
        private Button _toggleButton;

        // Unity 6 Overlays use UIElements (CreatePanelContent) to draw sleek modern UI
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;

            // 1. Quick Pause/Resume Toggle Button
            _toggleButton = new Button(OnToggleClicked);
            _toggleButton.style.marginRight = 6;
            root.Add(_toggleButton);

            // 2. Live Countdown Label
            _timerLabel = new Label("AutoSave: --");
            _timerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_timerLabel);

            // 3. Settings gear button to open your main window quickly
            var settingsbutton = new Button(() => AutoSaveTool.ShowWindow()) {text ="⚙"};
            settingsbutton.style.marginLeft = 6;
            root.Add(settingsbutton);

            // Schedule a UI refresh every 200 milliseconds so the countdown ticks smoothly
            root.schedule.Execute(UpdateOverlayUI).Every(200);
            UpdateOverlayUI();

            return root;
        }

        private void OnToggleClicked()
        {
            AutoSaveTool.IsEnabled = !AutoSaveTool.IsEnabled;

            if(AutoSaveTool.IsEnabled)
            {
                AutoSaveTool.ResetTimer();
            }

            UpdateOverlayUI();
        }

        private void UpdateOverlayUI()
        {
            if(_timerLabel == null || _toggleButton == null)
            {
                return;
            }

            if(AutoSaveTool.IsEnabled)
            {
                _toggleButton.text = "● ON";
                _toggleButton.style.color = new StyleColor(new Color(0.3f, 0.85f, 0.4f)); // Green
                _timerLabel.text = $"Next: {AutoSaveTool.TimeRemaining:F0}";
            }
            else
            {
                _toggleButton.text = "■ OFF";
                _toggleButton.style.color = new StyleColor(new Color(0.9f, 0.4f, 0.4f)); // Red
                _timerLabel.text = "Paused";
            }
        }
    }
}