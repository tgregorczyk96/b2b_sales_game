using System.Collections.Generic;
using System.IO;
using SalesSim.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Editor
{
    /// <summary>
    /// Adds the first game loop to SalesTestScene: "end conversation" button, balance label, run result overlay with
    /// sell / next run and the guidance usage, the run loop component, and wires the generated character and background sprites (if present).
    /// Run via "Sales Sim/Build Game Loop UI" or <c>-executeMethod SalesSim.Editor.GameLoopUiBuilder.Build</c>.
    /// Idempotent: replaces what an earlier run created.
    /// </summary>
    public static class GameLoopUiBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/SalesTestScene.unity";
        private const string CharacterFolder = "Assets/Game/Art/Generated/Characters";
        private const string BackgroundFolder = "Assets/Game/Art/Generated/Backgrounds";
        public const string CharacterSubject = "customer-salon-owner";
        public const string BackgroundSubject = "salon-reception";

        private static readonly Color PanelColor = new Color(0.13f, 0.15f, 0.19f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        private static readonly Color AccentButtonColor = new Color(0.36f, 0.72f, 0.46f, 1f);

        [MenuItem("Sales Sim/Build Game Loop UI")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find("UIRoot").transform;
            var controller = Object.FindAnyObjectByType<SalesConversationController>();

            foreach (var name in new[] { "EndConversationButton", "BalanceLabel", "ResultOverlay", "GameLoop" })
            {
                var old = root.Find(name);
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
            }

            var endButton = NewButton("EndConversationButton", root, "Gespräch beenden", ButtonColor, 24f);
            Anchor(endButton.GetComponent<RectTransform>(), 0.04f, 0.205f, 0.2f, 0.255f);

            var balance = NewText("BalanceLabel", root, 28f, Color.white);
            balance.fontStyle = FontStyles.Bold;
            balance.alignment = TextAlignmentOptions.MidlineLeft;
            balance.text = "Guthaben: 0 €";
            Anchor(balance.rectTransform, 0.215f, 0.205f, 0.4f, 0.255f);

            var (overlay, resultView) = BuildResultOverlay(root);

            var loopObject = NewUi("GameLoop", root).gameObject;
            var resultComponent = loopObject.AddComponent<RunResultView>();
            Assign(resultComponent, ("overlay", overlay), ("outcomeText", resultView["Outcome"]), ("qualityText", resultView["Quality"]),
                ("valueText", resultView["Value"]), ("closerText", resultView["Closer"]), ("payoutText", resultView["Payout"]),
                ("balanceText", resultView["Balance"]), ("guidanceText", resultView["Guidance"]),
                ("sellButton", resultView["SellButton"]), ("nextRunButton", resultView["NextRunButton"]));

            var loop = loopObject.AddComponent<SalesRunLoop>();
            Assign(loop, ("conversation", controller), ("resultView", resultComponent), ("balanceLabel", balance));
            Assign(controller, ("endButton", endButton.GetComponent<Button>()));

            WireCharacterSprites();
            WireBackground();

            overlay.transform.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[GameLoopUiBuilder] SalesTestScene game loop UI built.");
        }

        private static (GameObject Overlay, Dictionary<string, Object> Parts) BuildResultOverlay(Transform root)
        {
            var parts = new Dictionary<string, Object>();
            var overlay = NewUi("ResultOverlay", root);
            Anchor(overlay, 0f, 0f, 1f, 1f);
            overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.62f); // also blocks clicks behind it

            var panel = NewUi("Panel", overlay);
            Anchor(panel, 0.27f, 0.12f, 0.73f, 0.88f);
            panel.gameObject.AddComponent<Image>().color = PanelColor;
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 32, 32);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var title = NewText("Title", panel, 40f, Color.white);
            title.fontStyle = FontStyles.Bold;
            title.text = "Gespräch beendet";
            foreach (var part in new[] { "Outcome", "Quality", "Value", "Closer" })
            {
                parts[part] = NewText(part, panel, 26f, new Color(0.85f, 0.88f, 0.93f));
            }

            var payout = NewText("Payout", panel, 30f, new Color(0.55f, 0.9f, 0.62f));
            payout.fontStyle = FontStyles.Bold;
            parts["Payout"] = payout;
            parts["Balance"] = NewText("Balance", panel, 26f, Color.white);

            // Training metric only: shown, never priced.
            var guidance = NewText("Guidance", panel, 22f, new Color(0.72f, 0.8f, 0.92f));
            guidance.margin = new Vector4(0f, 10f, 0f, 0f);
            parts["Guidance"] = guidance;

            var spacer = NewUi("Spacer", panel);
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var row = NewUi("Buttons", panel);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 20f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
            parts["SellButton"] = NewButton("SellButton", row, "Lead an Closer verkaufen", AccentButtonColor, 24f).GetComponent<Button>();
            parts["NextRunButton"] = NewButton("NextRunButton", row, "Nächster Run", ButtonColor, 24f).GetComponent<Button>();

            overlay.gameObject.SetActive(false);
            return (overlay.gameObject, parts);
        }

        private static void WireCharacterSprites()
        {
            var character = Object.FindAnyObjectByType<CharacterView>();
            var entries = new List<(CharacterVisualState State, Sprite Sprite)>();
            foreach (CharacterVisualState state in System.Enum.GetValues(typeof(CharacterVisualState)))
            {
                var sprite = LatestSprite(CharacterFolder, $"{CharacterSubject}_{state.ToString().ToLowerInvariant()}_v");
                if (sprite != null)
                {
                    entries.Add((state, sprite));
                }
            }

            var so = new SerializedObject(character);
            var list = so.FindProperty("sprites");
            list.arraySize = entries.Count;
            for (var i = 0; i < entries.Count; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("state").enumValueIndex = (int)entries[i].State;
                element.FindPropertyRelative("sprite").objectReferenceValue = entries[i].Sprite;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            if (entries.Count > 0)
            {
                // A portrait card fills the character area; the placeholder mouth is not needed with sprites.
                var body = (Image)so.FindProperty("body").objectReferenceValue;
                Anchor(body.rectTransform, 0.04f, 0.11f, 0.96f, 1f);
            }

            Debug.Log($"[GameLoopUiBuilder] Character sprites wired: {entries.Count}");
        }

        private static void WireBackground()
        {
            var sprite = LatestSprite(BackgroundFolder, $"{BackgroundSubject}_");
            var background = GameObject.Find("Background")?.GetComponent<Image>();
            if (sprite == null || background == null)
            {
                return;
            }

            background.sprite = sprite;
            background.preserveAspect = false;
            background.color = new Color(0.55f, 0.55f, 0.6f, 1f); // dimmed so the UI stays readable
            Debug.Log("[GameLoopUiBuilder] Background sprite wired.");
        }

        /// <summary>The highest <c>_vNN</c> PNG with this prefix, imported as a sprite; null if there is none.</summary>
        private static Sprite LatestSprite(string folder, string prefix)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return null;
            }

            string best = null;
            foreach (var file in Directory.GetFiles(folder, prefix + "*.png"))
            {
                var path = file.Replace('\\', '/');
                if (best == null || string.CompareOrdinal(path, best) > 0)
                {
                    best = path;
                }
            }

            if (best == null)
            {
                return null;
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(best);
            // Single, not auto-sliced Multiple: every state keeps the full canvas so the states stay pixel-aligned.
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
                || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(best);
        }

        private static GameObject NewButton(string name, Transform parent, string label, Color color, float fontSize)
        {
            var rect = NewUi(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var text = NewText("Label", rect, fontSize, new Color(0.15f, 0.16f, 0.2f));
            text.alignment = TextAlignmentOptions.Center;
            text.text = label;
            Anchor(text.rectTransform, 0f, 0f, 1f, 1f);
            return rect.gameObject;
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
            text.raycastTarget = false;
            return text;
        }

        private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Assign(Object target, params (string Field, Object Value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                so.FindProperty(field).objectReferenceValue = value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
