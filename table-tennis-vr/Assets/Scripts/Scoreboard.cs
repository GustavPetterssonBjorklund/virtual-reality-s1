using UnityEngine;
using TMPro;

public class Scoreboard : MonoBehaviour
{
    public TMP_Text player1ScoreText;
    public TMP_Text player2ScoreText;

    private TableTennisMatch match;

    void Awake()
    {
        match = FindFirstObjectByType<TableTennisMatch>();
    }

    public void Plus1()
    {
        match?.RequestAdjustScore(1, 1);
    }

    public void Minus1()
    {
        match?.RequestAdjustScore(1, -1);
    }

    public void Plus2()
    {
        match?.RequestAdjustScore(2, 1);
    }

    public void Minus2()
    {
        match?.RequestAdjustScore(2, -1);
    }

    private void Update()
    {
        if (match == null)
            return;

        player1ScoreText.text = match.PlayerOneScore.ToString();
        player2ScoreText.text = match.PlayerTwoScore.ToString();
    }
}
