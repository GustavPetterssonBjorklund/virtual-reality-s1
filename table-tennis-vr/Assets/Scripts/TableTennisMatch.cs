using Unity.Netcode;
using UnityEngine;

public sealed class TableTennisMatch : NetworkBehaviour
{
    [SerializeField] private int pointsToWin = 11;
    [SerializeField] private int requiredLead = 2;
    [SerializeField] private float resetDelay = 0.25f;

    private readonly NetworkVariable<int> playerOneScore = new();
    private readonly NetworkVariable<int> playerTwoScore = new();
    private readonly NetworkVariable<bool> isGameOver = new();
    private readonly NetworkVariable<int> winner = new();

    public int PlayerOneScore => IsOffline ? offlinePlayerOneScore : playerOneScore.Value;
    public int PlayerTwoScore => IsOffline ? offlinePlayerTwoScore : playerTwoScore.Value;
    public bool IsGameOver => IsOffline ? offlineIsGameOver : isGameOver.Value;
    public int Winner => IsOffline ? offlineWinner : winner.Value;
    public int PointsToWin => pointsToWin;
    public int RequiredLead => requiredLead;

    private TableTennisBall ball;
    private Vector3 servePosition;
    private int offlinePlayerOneScore;
    private int offlinePlayerTwoScore;
    private bool offlineIsGameOver;
    private int offlineWinner;

    private bool IsOffline => !IsSpawned && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);

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
        if (IsOffline)
        {
            AwardOfflinePoint(player);
            return;
        }

        if (!IsServer)
        {
            return;
        }

        if (IsGameOver || (player != 1 && player != 2))
        {
            return;
        }

        if (player == 1)
        {
            playerOneScore.Value++;
        }
        else
        {
            playerTwoScore.Value++;
        }

        Debug.Log($"Point for Player {player}: {PlayerOneScore}-{PlayerTwoScore}");

        if (HasWon(PlayerOneScore, PlayerTwoScore))
        {
            isGameOver.Value = true;
            winner.Value = 1;
            Debug.Log("Player 1 wins the match.");
        }
        else if (HasWon(PlayerTwoScore, PlayerOneScore))
        {
            isGameOver.Value = true;
            winner.Value = 2;
            Debug.Log("Player 2 wins the match.");
        }
        else
        {
            Invoke(nameof(ResetBall), resetDelay);
        }
    }

    public void RequestResetMatch()
    {
        if (IsOffline)
        {
            ResetOfflineMatch();
            return;
        }

        if (IsServer)
        {
            ResetMatch();
            return;
        }

        RequestResetMatchServerRpc();
    }

    /// <summary>
    /// Starts a match for debug/testing with only the current player connected.
    /// This intentionally does not check MR readiness or the connected-player count.
    /// </summary>
    public void ForceStartSoloMatch()
    {
        if (IsOffline)
        {
            ResetOfflineMatch();
            return;
        }

        if (IsServer)
        {
            ResetMatch();
            return;
        }

        ForceStartSoloMatchServerRpc();
    }

    public void DebugAwardPoint(int player)
    {
        if (!IsOffline)
        {
            return;
        }

        AwardPoint(player);
    }

    public void DebugResetBall()
    {
        if (IsOffline)
        {
            CancelInvoke(nameof(ResetBall));
            ResetBall();
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestResetMatchServerRpc()
    {
        ResetMatch();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ForceStartSoloMatchServerRpc()
    {
        ResetMatch();
    }

    private void ResetMatch()
    {
        if (!IsServer)
        {
            return;
        }

        playerOneScore.Value = 0;
        playerTwoScore.Value = 0;
        isGameOver.Value = false;
        winner.Value = 0;
        CancelInvoke(nameof(ResetBall));
        ResetBall();
    }

    private void AwardOfflinePoint(int player)
    {
        if (offlineIsGameOver || (player != 1 && player != 2))
        {
            return;
        }

        if (player == 1)
        {
            offlinePlayerOneScore++;
        }
        else
        {
            offlinePlayerTwoScore++;
        }

        if (HasWon(PlayerOneScore, PlayerTwoScore))
        {
            offlineIsGameOver = true;
            offlineWinner = 1;
        }
        else if (HasWon(PlayerTwoScore, PlayerOneScore))
        {
            offlineIsGameOver = true;
            offlineWinner = 2;
        }
        else
        {
            Invoke(nameof(ResetBall), resetDelay);
        }
    }

    private void ResetOfflineMatch()
    {
        offlinePlayerOneScore = 0;
        offlinePlayerTwoScore = 0;
        offlineIsGameOver = false;
        offlineWinner = 0;
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
