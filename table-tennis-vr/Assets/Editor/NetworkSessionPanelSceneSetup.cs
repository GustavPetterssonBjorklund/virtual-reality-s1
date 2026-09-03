using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public static class NetworkSessionPanelSceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private static readonly Color PanelBlue = new(0.12f, 0.58f, 0.9f, 1f);
    private static readonly Color Navy = new(0.012f, 0.045f, 0.12f, 1f);
    private static readonly Color TranslucentWhite = new(1f, 1f, 1f, 0.25f);

    [MenuItem("Tools/Table Tennis/Create Network Session Panel")]
    public static void CreatePanel()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var session = Object.FindFirstObjectByType<TableTennisNetworkSession>();
        if (session == null)
            throw new MissingComponentException("SampleScene has no TableTennisNetworkSession.");

        var existing = GameObject.Find("UI/Network Session Panel");
        if (existing != null)
            Object.DestroyImmediate(existing);

        var uiRoot = GameObject.Find("UI");
        if (uiRoot == null)
            uiRoot = new GameObject("UI");

        var canvasObject = new GameObject("Network Session Panel", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
        canvasObject.transform.SetParent(uiRoot.transform, false);
        canvasObject.transform.SetPositionAndRotation(new Vector3(2.8f, 1.55f, -0.48f), Quaternion.Euler(0f, 90f, 0f));
        canvasObject.transform.localScale = Vector3.one * 0.0025f;
        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(700f, 460f);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var panel = UiObject("Panel", canvasObject.transform, new Vector2(700f, 460f), Vector2.zero);
        var panelBackground = panel.AddComponent<Image>();
        panelBackground.sprite = null;
        panelBackground.type = Image.Type.Simple;
        panelBackground.color = PanelBlue;

        var title = Text("NETWORK SESSION Label", panel.transform, "NETWORK SESSION", 38f, new Vector2(0f, 183f), new Vector2(560f, 58f));
        title.fontStyle = FontStyles.Bold;

        var statusCard = UiObject("Status Card", panel.transform, new Vector2(590f, 58f), new Vector2(0f, 112f));
        statusCard.AddComponent<Image>().color = TranslucentWhite;
        var status = Text("Status", statusCard.transform, "Not connected", 22f, Vector2.zero, new Vector2(550f, 44f));

        var codeCard = UiObject("Session Code Card", panel.transform, new Vector2(590f, 82f), new Vector2(0f, 36f));
        codeCard.AddComponent<Image>().color = TranslucentWhite;
        var code = Text("Code", codeCard.transform, "Create a session or enter a code", 27f, Vector2.zero, new Vector2(550f, 68f));
        code.fontStyle = FontStyles.Bold;

        var joinCard = UiObject("Join Card", panel.transform, new Vector2(590f, 82f), new Vector2(0f, -145f));
        joinCard.AddComponent<Image>().color = TranslucentWhite;
        var input = Input(joinCard.transform, new Vector2(-140f, 0f));
        var join = Button(joinCard.transform, "JOIN", new Vector2(120f, 0f), new Vector2(260f, 55f));
        var create = Button(panel.transform, "CREATE SESSION", new Vector2(0f, -62f), new Vector2(500f, 55f));

        var serialized = new SerializedObject(session);
        serialized.FindProperty("networkCanvas").objectReferenceValue = canvas;
        serialized.FindProperty("statusText").objectReferenceValue = status;
        serialized.FindProperty("codeText").objectReferenceValue = code;
        serialized.FindProperty("joinCodeInput").objectReferenceValue = input;
        serialized.FindProperty("hostButton").objectReferenceValue = create;
        serialized.FindProperty("joinButton").objectReferenceValue = join;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Created and wired UI/Network Session Panel in SampleScene.");
    }

    private static GameObject UiObject(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        var rect = (RectTransform)gameObject.transform;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.pivot = Vector2.one * 0.5f;
        return gameObject;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, string value, float fontSize, Vector2 position, Vector2 size)
    {
        var text = UiObject(name, parent, size, position).AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        return text;
    }

    private static TMP_InputField Input(Transform parent, Vector2 position)
    {
        var root = UiObject("Join Code Input", parent, new Vector2(220f, 55f), position);
        var background = root.AddComponent<Image>();
        background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        background.type = Image.Type.Sliced;
        background.color = Color.white;
        var value = Text("Text", root.transform, "", 24f, Vector2.zero, new Vector2(200f, 45f));
        value.color = Navy;
        var placeholder = Text("Placeholder", root.transform, "ENTER CODE", 20f, Vector2.zero, new Vector2(200f, 45f));
        placeholder.color = new Color(Navy.r, Navy.g, Navy.b, 0.62f);
        var input = root.AddComponent<TMP_InputField>();
        input.textComponent = value;
        input.placeholder = placeholder;
        input.characterLimit = 12;
        input.contentType = TMP_InputField.ContentType.Alphanumeric;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.keyboardType = TouchScreenKeyboardType.ASCIICapable;
        input.shouldHideSoftKeyboard = false;
        input.shouldHideMobileInput = false;
        var serializedInput = new SerializedObject(input);
        serializedInput.FindProperty("m_HideSoftKeyboard").boolValue = false;
        serializedInput.FindProperty("m_HideMobileInput").boolValue = false;
        serializedInput.ApplyModifiedPropertiesWithoutUndo();
        return input;
    }

    private static Button Button(Transform parent, string label, Vector2 position, Vector2 size)
    {
        var root = UiObject(label + " Button", parent, size, position);
        var background = root.AddComponent<Image>();
        background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        background.type = Image.Type.Sliced;
        background.color = Color.white;
        var button = root.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        var text = Text("Label", root.transform, label, 22f, Vector2.zero, size - new Vector2(12f, 8f));
        text.fontStyle = FontStyles.Bold;
        text.color = Navy;
        text.raycastTarget = false;
        root.AddComponent<UIButtonHoverStyle>();
        return button;
    }

}
