using System;
using UnityEngine.UIElements;

namespace Unity.Connect.Share.Editor
{
    /// <summary>
    /// An utility class for common UIElements setup method
    /// </summary>
    public static class UIElementsUtils
    {
        /// <summary>
        /// Initializes the frontend and backend of a Button 
        /// </summary>
        /// <param name="buttonName">The name of the button in the UXML file</param>
        /// <param name="onClickAction">What method will be called when the button is clicked?</param>
        /// <param name="isEnabled">is this button enabled by default?</param>
        /// <param name="parent">The parent VisualElement of the button</param>
        /// <param name="text">The text the button will display</param>
        /// <param name="tooltip">The tooltip the button will display when hovered</param>
        /// <param name="showIfEnabled">Should the button be showed when enabled?</param>
        public static void SetupButton(string buttonName, Action onClickAction, bool isEnabled, VisualElement parent, string text = "", string tooltip = "", bool showIfEnabled = true)
        {
            Button button = parent.Query<Button>(buttonName);
            button.SetEnabled(isEnabled);
            button.clickable = new Clickable(() => onClickAction.Invoke());
            if (!string.IsNullOrEmpty(text))
            {
                button.text = text;
            }
            button.tooltip = string.IsNullOrEmpty(tooltip) ? button.text : tooltip;
            if (!showIfEnabled || !isEnabled) { return; }
            Show(button);
        }

        /// <summary>
        /// Initializes the frontend and backend of a Label 
        /// </summary>
        /// <param name="labelName">The name of the label in the UXML file</param>
        /// <param name="text">The text the label will display</param>
        /// <param name="parent">The parent VisualElement of the label</param>
        /// <param name="manipulator">A Manipulator implementation that defiens custom interactions with this label</param>
        public static void SetupLabel(string labelName, string text, VisualElement parent, Manipulator manipulator = null)
        {
            Label label = parent.Query<Label>(labelName);
            label.text = text;
            if (manipulator == null) { return; }

            label.AddManipulator(manipulator);
        }

        /// <summary>
        /// Hides a visual element
        /// </summary>
        /// <param name="elementName">the name of the element to hide</param>
        /// <param name="parent">The parent VisualElement of the element to hide</param>
        public static void Hide(string elementName, VisualElement parent) { Hide(parent.Query<VisualElement>(elementName)); }

        /// <summary>
        /// Shows an hidden visual element
        /// </summary>
        /// <param name="elementName">the name of the element to show</param>
        /// <param name="parent">The parent VisualElement of the element to show</param>
        public static void Show(string elementName, VisualElement parent) { Show(parent.Query<VisualElement>(elementName)); }

        /// <summary>
        /// Hides a visual element
        /// </summary>
        /// <param name="element">the element to hide</param>
        public static void Hide(VisualElement element) { element.style.display = DisplayStyle.None; }

        /// <summary>
        /// Shows an hidden visual element
        /// </summary>
        /// <param name="element">the element to show</param>
        public static void Show(VisualElement element) { element.style.display = DisplayStyle.Flex; }

        /// <summary>
        /// Removes a stylesheet from a visual element
        /// </summary>
        /// <param name="styleSheet">The stylesheet to remove</param>
        /// <param name="target">The target visual element</param>
        public static void RemoveStyleSheet(StyleSheet styleSheet, VisualElement target)
        {
            if (!styleSheet) { return; }
            if (!target.styleSheets.Contains(styleSheet)) { return; }
            target.styleSheets.Remove(styleSheet);
        }
    }
}
