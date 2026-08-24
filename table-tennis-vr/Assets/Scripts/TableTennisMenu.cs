using UnityEngine;
using UnityEngine.UI;

public sealed class TableTennisMenu : MonoBehaviour
{
    private TableTennisMatch match;
    private Button startButton;

    private void Awake()
    {
        match = GetComponent<TableTennisMatch>();

        GameObject buttonObject = GameObject.Find("Start Match Button");
        if (buttonObject != null)
        {
            startButton = buttonObject.GetComponent<Button>();
            if (startButton != null)
            {
                startButton.onClick.AddListener(StartOrRestartMatch);
            }
        }
    }

    public void StartOrRestartMatch()
    {
        if (match != null)
        {
            match.ResetMatch();
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
