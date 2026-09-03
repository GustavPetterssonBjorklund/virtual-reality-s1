using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TableTennisMenu : MonoBehaviour
{
    private TableTennisMatch match;
    private TableTennisMRPlacement mrPlacement;
    private Button startButton;
    private Button tableLockButton;
    private TMP_Text tableLockLabel;
    private bool lastTableLockState;

    private void Awake()
    {
        match = GetComponent<TableTennisMatch>();
        mrPlacement = GetComponent<TableTennisMRPlacement>();

        GameObject buttonObject = GameObject.Find("Start Match Button");
        if (buttonObject != null)
        {
            startButton = buttonObject.GetComponent<Button>();
            if (startButton != null)
            {
                startButton.onClick.AddListener(StartOrRestartMatch);
            }
        }

        TryCreateTableLockButton();
    }

    private void Update()
    {
        if (mrPlacement == null || tableLockButton == null)
        {
            TryCreateTableLockButton();
            return;
        }

        bool locked = mrPlacement.IsTableLocked;
        if (locked == lastTableLockState)
        {
            return;
        }

        lastTableLockState = locked;
        if (tableLockLabel != null)
        {
            tableLockLabel.text = locked ? "UNLOCK TABLE" : "LOCK TABLE";
        }
    }

    private void CreateTableLockButton()
    {
        Button sourceButton = startButton;
        if (sourceButton == null)
        {
            GameObject createButton = GameObject.Find("Network Session Panel/Panel/CREATE Button");
            if (createButton != null)
            {
                sourceButton = createButton.GetComponent<Button>();
            }
        }

        if (sourceButton == null)
        {
            return;
        }

        GameObject buttonObject = Instantiate(sourceButton.gameObject, sourceButton.transform.parent);
        buttonObject.name = "Table Lock Button";

        RectTransform sourceRect = sourceButton.GetComponent<RectTransform>();
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        Vector2 offset = sourceButton == startButton ? new Vector2(0f, -90f) : new Vector2(0f, -70f);
        buttonRect.anchoredPosition = sourceRect.anchoredPosition + offset;

        tableLockButton = buttonObject.GetComponent<Button>();
        tableLockButton.onClick.RemoveAllListeners();
        tableLockButton.onClick.AddListener(ToggleTableLock);
        tableLockLabel = buttonObject.GetComponentInChildren<TMP_Text>(true);
        lastTableLockState = mrPlacement != null && mrPlacement.IsTableLocked;
        if (tableLockLabel != null)
        {
            tableLockLabel.text = lastTableLockState ? "UNLOCK TABLE" : "LOCK TABLE";
        }
    }

    private void TryCreateTableLockButton()
    {
        if (tableLockButton != null || mrPlacement == null)
        {
            return;
        }

        if (startButton == null)
        {
            GameObject startButtonObject = GameObject.Find("Start Match Button");
            if (startButtonObject != null)
            {
                startButton = startButtonObject.GetComponent<Button>();
            }
        }

        CreateTableLockButton();
    }


    private void ToggleTableLock()
    {
        if (mrPlacement != null)
        {
            mrPlacement.ToggleTableLock();
        }
    }

    public void StartOrRestartMatch()
    {
        if (mrPlacement != null && !mrPlacement.IsPlaced)
        {
            mrPlacement.PlaceUsingFallback();
        }

        if (mrPlacement != null && !mrPlacement.CanStartMatch)
        {
            RuntimeDiagnostics.LogWarning("Match start blocked until MR placement and headset calibration are complete.");
            return;
        }

        if (match != null && match.NetworkObject != null)
        {
            match.RequestResetMatch();
        }
    }

    private void OnDestroy()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartOrRestartMatch);
        }

        if (tableLockButton != null)
        {
            tableLockButton.onClick.RemoveListener(ToggleTableLock);
        }
    }
}
