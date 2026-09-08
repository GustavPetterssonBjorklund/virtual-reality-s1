using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TableTennisMenu : MonoBehaviour
{
    private TableTennisMatch match;
    private TableTennisMRPlacement mrPlacement;
    private Button startButton;
    [SerializeField] private Button tableLockButton;
    [SerializeField] private TMP_Text tableLockLabel;
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

        RefreshTableLockLabel();
    }

    private void Update()
    {
        if (mrPlacement != null && mrPlacement.IsTableLocked != lastTableLockState)
        {
            RefreshTableLockLabel();
        }
    }

    private void RefreshTableLockLabel()
    {
        if (tableLockButton == null || tableLockLabel == null || mrPlacement == null)
        {
            return;
        }

        lastTableLockState = mrPlacement.IsTableLocked;
        tableLockLabel.text = lastTableLockState ? "UNLOCK TABLE" : "LOCK TABLE";
    }

    public void StartOrRestartMatch()
    {
        if (mrPlacement != null && !mrPlacement.IsPlaced)
        {
            mrPlacement.ConfirmCurrentPlacement();
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
    }
}
