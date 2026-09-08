using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WinnerLoserCanvasFollower : MonoBehaviour
{
    [SerializeField] private float forwardOffset = 1.5f;
    [SerializeField] private float verticalOffset = 1f;
    [Header("Result Animation")]
    [SerializeField] private GameObject winnerResult;
    [SerializeField] private GameObject loserResult;
    [SerializeField] private float animationDuration = 0.55f;
    [SerializeField] private float spinAngle = 24f;

    private Transform head;
    private Coroutine resultAnimation;
    private Vector3 winnerScale;
    private Vector3 loserScale;

    private void Awake()
    {
        winnerScale = winnerResult.transform.localScale;
        loserScale = loserResult.transform.localScale;
        winnerResult.SetActive(false);
        loserResult.SetActive(false);
        CreateTestControls();
    }

    private void LateUpdate()
    {
        if (head == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
                return;

            head = mainCamera.transform;
        }

        Vector3 horizontalForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.0001f)
            horizontalForward = Vector3.ProjectOnPlane(head.up, Vector3.up);

        horizontalForward.Normalize();
        transform.SetPositionAndRotation(
            head.position + Vector3.up * verticalOffset + horizontalForward * forwardOffset,
            Quaternion.LookRotation(horizontalForward, Vector3.up));
    }

    public void ShowWinner()
    {
        ShowResult(winnerResult, loserResult, winnerScale, -spinAngle);
    }

    public void ShowLoser()
    {
        ShowResult(loserResult, winnerResult, loserScale, spinAngle);
    }

    private void ShowResult(GameObject result, GameObject otherResult, Vector3 finalScale, float initialSpin)
    {
        if (resultAnimation != null)
            StopCoroutine(resultAnimation);

        otherResult.SetActive(false);
        resultAnimation = StartCoroutine(AnimateResult(result.transform, finalScale, initialSpin));
    }

    private IEnumerator AnimateResult(Transform result, Vector3 finalScale, float initialSpin)
    {
        result.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < animationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / animationDuration);
            float scaleT = 1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f);
            float angle = initialSpin * Mathf.Pow(1f - t, 2f) * Mathf.Cos(t * Mathf.PI * 2.5f);

            result.localScale = finalScale * Mathf.LerpUnclamped(0.65f, 1f, scaleT);
            result.localRotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }

        result.localScale = finalScale;
        result.localRotation = Quaternion.identity;
        resultAnimation = null;
    }

    private void CreateTestControls()
    {
        GameObject controls = new GameObject("Result Test Controls", typeof(RectTransform));
        RectTransform controlsRect = controls.GetComponent<RectTransform>();
        controlsRect.SetParent(transform, false);
        controlsRect.localPosition = new Vector3(0f, -0.72f, -0.01f);
        controlsRect.localScale = Vector3.one * 0.002f;
        controlsRect.sizeDelta = new Vector2(400f, 64f);

        CreateButton(controlsRect, "SHOW WINNER", new Vector2(-100f, 0f), new Color(0.05f, 0.62f, 0.86f), ShowWinner);
        CreateButton(controlsRect, "SHOW LOSER", new Vector2(100f, 0f), new Color(0.94f, 0.32f, 0.16f), ShowLoser);
    }

    private static void CreateButton(RectTransform parent, string label, Vector2 position, Color color, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.SetParent(parent, false);
        buttonRect.anchoredPosition = position;
        buttonRect.sizeDelta = new Vector2(184f, 54f);

        Image background = buttonObject.GetComponent<Image>();
        background.color = color;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(action);

        ColorBlock colors = button.colors;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
        button.colors = colors;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(buttonRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 20f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
    }
}
