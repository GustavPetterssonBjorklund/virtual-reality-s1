using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class TableTennisDebugPanel : MonoBehaviour
{
    private static bool DEBUG_ENABLE = true;

    private TableTennisMatch match;
    private TableTennisBall ball;
    private TableTennisSoloOpponent opponent;
    private TextMeshProUGUI telemetry;
    private TextMeshProUGUI modeText;
    private Toggle opponentToggle;
    private Slider skillSlider;
    private Slider reactionSlider;
    private Slider missSlider;
    private float launchSpeed = 4.5f;

    private void Awake()
    {
        if (!DEBUG_ENABLE)
        {
            enabled = false;
            return;
        }

        match = GetComponent<TableTennisMatch>();
        ball = FindFirstObjectByType<TableTennisBall>();
        opponent = GetComponent<TableTennisSoloOpponent>();
        if (opponent == null)
        {
            opponent = gameObject.AddComponent<TableTennisSoloOpponent>();
        }

        CreatePanel();
    }

    private void Update()
    {
        if (telemetry == null || match == null || ball == null)
        {
            return;
        }

        bool connected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (connected && opponent.IsEnabled)
        {
            opponent.SetEnabled(false);
            opponentToggle.SetIsOnWithoutNotify(false);
        }

        modeText.text = connected ? "NETWORK SESSION — DEBUG MUTATIONS DISABLED" : "OFFLINE SOLO TEST MODE";
        telemetry.text = $"SCORE   {match.PlayerOneScore} - {match.PlayerTwoScore}\n" +
                         $"BALL    {ball.transform.position:F2}\n" +
                         $"SPEED   {ball.LinearVelocity.magnitude:F2} m/s\n" +
                         $"FPS     {(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)):F0}\n" +
                         $"AI      {(opponent.IsEnabled ? "ACTIVE" : "OFF")}\n" +
                         $"FROZEN  {(ball.IsFrozen ? "YES" : "NO")}\n" +
                         $"LEAD    first to {match.PointsToWin}, lead {match.RequiredLead}";
    }

    private void CreatePanel()
    {
        GameObject canvasObject = new GameObject("Debug Options Panel", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasObject.transform.SetPositionAndRotation(new Vector3(2.8f, 1.55f, 1.55f), Quaternion.Euler(0f, 90f, 0f));
        canvasObject.transform.localScale = Vector3.one * 0.0025f;
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvasObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(760f, 650f);

        GameObject panel = CreateObject("Panel", canvasObject.transform);
        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.025f, 0.035f, 0.06f, 0.96f);
        SetRect(panel, new Vector2(760f, 650f), Vector2.zero);

        CreateText(panel.transform, "DEBUG OPTIONS", 30f, new Vector2(0f, 292f), new Vector2(700f, 45f));
        modeText = CreateText(panel.transform, "OFFLINE SOLO TEST MODE", 18f, new Vector2(0f, 248f), new Vector2(700f, 35f));
        telemetry = CreateText(panel.transform, "", 18f, new Vector2(-210f, 125f), new Vector2(300f, 210f));
        telemetry.alignment = TextAlignmentOptions.Left;

        opponentToggle = CreateToggle(panel.transform, "SOLO OPPONENT", new Vector2(175f, 205f), false, SetOpponentEnabled);
        skillSlider = CreateSlider(panel.transform, "AI SPEED", new Vector2(175f, 150f), 0.5f, 8f, opponent.MovementSpeed, value => opponent.MovementSpeed = value);
        reactionSlider = CreateSlider(panel.transform, "REACTION DELAY", new Vector2(175f, 95f), 0f, 0.5f, opponent.ReactionDelay, value => opponent.ReactionDelay = value);
        missSlider = CreateSlider(panel.transform, "MISS CHANCE", new Vector2(175f, 40f), 0f, 1f, opponent.MissChance, value => opponent.MissChance = value);

        CreateButton(panel.transform, "RESET MATCH", new Vector2(-210f, -45f), () => match.RequestResetMatch());
        CreateButton(panel.transform, "RESET BALL", new Vector2(-55f, -45f), () => match.DebugResetBall());
        CreateButton(panel.transform, "POINT P1", new Vector2(100f, -45f), () => match.DebugAwardPoint(1));
        CreateButton(panel.transform, "POINT P2", new Vector2(225f, -45f), () => match.DebugAwardPoint(2));
        CreateButton(panel.transform, "LAUNCH BALL", new Vector2(-125f, -115f), LaunchBall);
        CreateButton(panel.transform, "FREEZE BALL", new Vector2(75f, -115f), () => ball.DebugSetFrozen(!ball.IsFrozen));
        CreateText(panel.transform, "Launch speed: 4.5 m/s", 17f, new Vector2(250f, -115f), new Vector2(220f, 35f));
    }

    private void SetOpponentEnabled(bool enabled)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            opponentToggle.SetIsOnWithoutNotify(false);
            opponent.SetEnabled(false);
            return;
        }
        opponent.SetEnabled(enabled);
    }

    private void LaunchBall()
    {
        match.DebugResetBall();
        ball.DebugLaunch(new Vector3(-launchSpeed, 1.4f, Random.Range(-1.2f, 1.2f)));
    }

    private static GameObject CreateObject(string name, Transform parent)
    {
        GameObject value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }

    private static void SetRect(GameObject value, Vector2 size, Vector2 position)
    {
        RectTransform rect = value.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.pivot = Vector2.one * 0.5f;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string value, float size, Vector2 position, Vector2 dimensions)
    {
        GameObject textObject = CreateObject(value, parent);
        SetRect(textObject, dimensions, position);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        return text;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = CreateObject(label, parent);
        SetRect(buttonObject, new Vector2(140f, 48f), position);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.3f, 0.55f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        button.onClick.AddListener(action);
        CreateText(buttonObject.transform, label, 16f, Vector2.zero, new Vector2(135f, 45f));
        return button;
    }

    private static Toggle CreateToggle(Transform parent, string label, Vector2 position, bool value, UnityEngine.Events.UnityAction<bool> changed)
    {
        GameObject toggleObject = CreateObject(label, parent);
        SetRect(toggleObject, new Vector2(300f, 45f), position);
        Toggle toggle = toggleObject.AddComponent<Toggle>();
        Image image = toggleObject.AddComponent<Image>();
        image.color = new Color(0.08f, 0.1f, 0.16f, 1f);
        toggle.isOn = value;
        toggle.onValueChanged.AddListener(changed);
        CreateText(toggleObject.transform, "[ ]  " + label, 18f, Vector2.zero, new Vector2(300f, 45f));
        return toggle;
    }

    private static Slider CreateSlider(Transform parent, string label, Vector2 position, float min, float max, float value, UnityEngine.Events.UnityAction<float> changed)
    {
        GameObject sliderObject = CreateObject(label, parent);
        SetRect(sliderObject, new Vector2(330f, 45f), position);
        Slider slider = sliderObject.AddComponent<Slider>();
        Image background = sliderObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.1f, 0.16f, 1f);
        GameObject fill = CreateObject("Fill", sliderObject.transform);
        SetRect(fill, new Vector2(250f, 12f), new Vector2(35f, 0f));
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.18f, 0.55f, 0.9f, 1f);
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(changed);
        CreateText(sliderObject.transform, label, 15f, new Vector2(-105f, 0f), new Vector2(150f, 35f));
        return slider;
    }
}
