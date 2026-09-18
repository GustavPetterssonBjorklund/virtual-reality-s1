using UnityEngine;
using TMPro;

public class Scoreboard : MonoBehaviour
{
    public TMP_Text player1ScoreText;
    public TMP_Text player2ScoreText;

    private int player1Score = 0;
    private int player2Score = 0;

    void Start()
    {
        UpdateScoreboard();
    }

    public void Plus1()
    {
        player1Score++;
        UpdateScoreboard();
    }

    public void Minus1()
    {
        if (player1Score > 0)
        {
            player1Score--;
        }

        UpdateScoreboard();
    }

    public void Plus2()
    {
        player2Score++;
        UpdateScoreboard();
    }

    public void Minus2()
    {
        if (player2Score > 0)
        {
            player2Score--;
        }

        UpdateScoreboard();
    }

    private void UpdateScoreboard()
    {
        player1ScoreText.text = player1Score.ToString();
        player2ScoreText.text = player2Score.ToString();
    }
}
