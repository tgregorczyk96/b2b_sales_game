using SalesSim.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Editor
{
    /// <summary>UI parts for copyable text, shared by the conversation and guidance builders.</summary>
    internal static class CopyUiParts
    {
        private static readonly Color ButtonColor = new Color(0.32f, 0.36f, 0.45f, 1f);
        private static readonly Color ButtonTextColor = new Color(0.9f, 0.93f, 0.98f, 1f);
        private static readonly Color SelectionColor = new Color(0.4f, 0.62f, 1f, 0.45f);

        /// <summary>A small "Kopieren" button with fixed size, for a header row.</summary>
        public static CopyTextButton NewCopyButton(Transform parent)
        {
            var rect = NewUi("CopyButton", parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = 104f;
            size.preferredHeight = 28f;
            size.flexibleWidth = 0f;
            size.flexibleHeight = 0f;

            var label = NewText("Label", rect, 16f, ButtonTextColor);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.text = CopyTextButton.IdleLabel;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var copy = rect.gameObject.AddComponent<CopyTextButton>();
            var so = new SerializedObject(copy);
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            return copy;
        }

        /// <summary>A header row: <paramref name="title"/> takes the space, the copy button sits on the right.</summary>
        public static RectTransform NewHeaderRow(Transform parent, TMP_Text title, out CopyTextButton copyButton)
        {
            var row = NewUi("Header", parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            title.rectTransform.SetParent(row, false);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            copyButton = NewCopyButton(row);
            return row;
        }

        /// <summary>
        /// Read-only, selectable multi-line text (<see cref="SelectableMessageText"/>): mouse selection and Ctrl+C like the
        /// chat, no typing. Standard TMP input layout: text area and text stretched by anchors; the field reports the
        /// text's preferred height to the surrounding layout.
        /// </summary>
        public static SelectableMessageText NewSelectableText(string name, Transform parent, float fontSize, Color color)
        {
            var root = NewUi(name, parent);
            var area = NewUi("Text Area", root);
            Stretch(area);
            area.gameObject.AddComponent<RectMask2D>();
            var text = NewText("Text", area, fontSize, color);
            Stretch(text.rectTransform);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = false;

            var body = root.gameObject.AddComponent<SelectableMessageText>();
            body.textViewport = area;
            body.textComponent = text;
            body.lineType = TMP_InputField.LineType.MultiLineNewline;
            body.readOnly = true;
            body.richText = false;
            body.onFocusSelectAll = false;
            body.resetOnDeActivation = true;
            body.selectionColor = SelectionColor;
            body.transition = Selectable.Transition.None;
            body.navigation = new Navigation { mode = Navigation.Mode.None };
            return body;
        }

        private static RectTransform NewUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color)
        {
            var text = NewUi(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = color;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
