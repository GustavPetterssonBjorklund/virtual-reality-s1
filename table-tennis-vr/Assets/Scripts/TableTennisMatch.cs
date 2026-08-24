using UnityEngine;

public sealed class TableTennisMatch : MonoBehaviour
{
    [SerializeField] private int pointsToWin = 11;
    [SerializeField] private int requiredLead = 2;
    [SerializeField] private float resetDelay = 0.25f;

    public int PlayerOneScore { get; private set; }
    public int PlayerTwoScore { get; private set; }
    public bool IsGameOver { get; private set; }
    public int Winner { get; private set; }

    private TableTennisBall ball;
    private Vector3 servePosition;

    private void Awake()
    {
        ball = FindFirstObjectByType<TableTennisBall>();
        if (ball != null)
        {
            servePosition = ball.transform.position;
        }
    }

    public void AwardPoint(int player)
    {
        if (IsGameOver || (player != 1 && player != 2))
        {
            return;
        }

        if (player == 1)
        {
            PlayerOneScore++;
        }
        else
        {
            PlayerTwoScore++;
        }

        Debug.Log($"Point for Player {player}: {PlayerOneScore}-{PlayerTwoScore}");

        if (HasWon(PlayerOneScore, PlayerTwoScore))
        {
            IsGameOver = true;
            Winner = 1;
            Debug.Log("Player 1 wins the match.");
        }
        else if (HasWon(PlayerTwoScore, PlayerOneScore))
        {
            IsGameOver = true;
            Winner = 2;
            Debug.Log("Player 2 wins the match.");
        }
        else
        {
            Invoke(nameof(ResetBall), resetDelay);
        }
    }

    public void ResetMatch()
    {
        PlayerOneScore = 0;
        PlayerTwoScore = 0;
        IsGameOver = false;
        Winner = 0;
        CancelInvoke(nameof(ResetBall));
        ResetBall();
    }

    private bool HasWon(int score, int opponentScore)
    {
        return score >= pointsToWin && score - opponentScore >= requiredLead;
    }

    private void ResetBall()
    {
        if (ball != null)
        {
            ball.ResetForServe(servePosition);
        }
    }
}