using TMPro;
using UnityEngine;

public sealed class TableTennisHUD : MonoBehaviour
{
    private TMP_Text scoreText;
    private TableTennisMatch match;

    private void Awake()
    {
        match = GetComponent<TableTennisMatch>();
        GameObject scoreObject = GameObject.Find("Scoreboard");
        if (scoreObject != null)
        {
            scoreText = scoreObject.GetComponent<TMP_Text>();
        }
    }

    private void Update()
    {
        if (match == null || scoreText == null)
        {
            return;
        }

        if (match.IsGameOver)
        {
            scoreText.text = $"PLAYER {match.Winner} WINS\n{match.PlayerOneScore} - {match.PlayerTwoScore}";
        }
        else
        {
            scoreText.text = $"PLAYER 1   {match.PlayerOneScore}  -  {match.PlayerTwoScore}   PLAYER 2";
        }
    }
}