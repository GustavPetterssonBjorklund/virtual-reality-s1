using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TableTennisMenu : MonoBehaviour
{
    private TableTennisMatch match;
    private TableTennisMRPlacement mrPlacement;
    private Button startButton;
    [SerializeField] private Button tableLockButton;
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

        if (tableLockButton == null)
        {
            GameObject lockButtonObject = GameObject.Find("Table Lock Button");
            if (lockButtonObject != null)
            {
                tableLockButton = lockButtonObject.GetComponent<Button>();
            }
        }

        if (tableLockButton != null)
        {
            tableLockButton.onClick.AddListener(ToggleTableLock);
            tableLockLabel = tableLockButton.GetComponentInChildren<TMP_Text>(true);
            RefreshTableLockLabel();
        }
    }

    private void Update()
    {
        if (mrPlacement == null || tableLockButton == null)
        {
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

    private void RefreshTableLockLabel()
    {
        lastTableLockState = mrPlacement != null && mrPlacement.IsTableLocked;
        if (tableLockLabel != null)
        {
            tableLockLabel.text = lastTableLockState ? "UNLOCK TABLE" : "LOCK TABLE";
        }
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
