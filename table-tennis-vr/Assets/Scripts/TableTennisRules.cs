using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class TableTennisRules : MonoBehaviour
{
    public enum Player
    {
        P1,
        P2
    }

    public enum GameState
    {
        WaitingForServe,

        ExpectServerTable,
        ExpectReceiverTable,

        ExpectP1Racket,
        ExpectP2Table,

        ExpectP2Racket,
        ExpectP1Table,

        PointOver,
        GameOver
    }

    public enum BallEvent
    {
        P1Racket,
        P2Racket,
        P1Table,
        P2Table,
        Ground,
        Net
    }

    public GameState state = GameState.WaitingForServe;

    public Player currentServer = Player.P2;

    public int p1Score = 0;
    public int p2Score = 0;

    public Rigidbody ballRigidbody;

    public Transform p1ServePosition;
    public Transform p2ServePosition;

    // P1 occupies negative table-local X; P2 occupies positive X.
    [SerializeField] private Transform tableFrame;
    [SerializeField] private float tableHalfLength = 1.37f;
    [SerializeField] private float tableHalfWidth = 0.7625f;
    [SerializeField] private float tableHeight = 0.76f;
    [SerializeField] private float ballRadius = 0.02f;
    private Vector3 incomingBallVelocity;
    private bool hasVelocitySample;

    public bool HasAuthority => NetworkManager.Singleton == null ||
        !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;

    private void Awake()
    {
        if (tableFrame == null)
        {
            foreach (BallEventTrigger trigger in FindObjectsByType<BallEventTrigger>(FindObjectsSortMode.None))
            {
                if (trigger.rules == this && trigger.eventType == BallEvent.P1Table)
                {
                    tableFrame = trigger.transform.parent;
                    break;
                }
            }
        }
    }

    private void FixedUpdate()
    {
        if (HasAuthority)
        {
            TryFinishOutBall();
            if (ballRigidbody != null)
            {
                // Sample before physics changes the velocity at racket contact.
                incomingBallVelocity = ballRigidbody.linearVelocity;
                hasVelocitySample = true;
            }
        }
    }

    // Once an unreturned shot passes the receiver's end line, later racket
    // contact cannot turn that already-lost shot into an obstruction point.
    private bool TryFinishOutBall()
    {
        if (tableFrame == null || ballRigidbody == null)
            return false;

        Player receiver;
        if (state == GameState.ExpectReceiverTable)
            receiver = currentServer == Player.P1 ? Player.P2 : Player.P1;
        else if (state == GameState.ExpectP1Table)
            receiver = Player.P1;
        else if (state == GameState.ExpectP2Table)
            receiver = Player.P2;
        else
            return false;

        Vector3 localBall = tableFrame.InverseTransformPoint(ballRigidbody.position);
        bool pastEnd = receiver == Player.P1
            ? localBall.x < -tableHalfLength - ballRadius
            : localBall.x > tableHalfLength + ballRadius;
        if (!pastEnd)
            return false;

        GivePoint(receiver);
        return true;
    }

    private void GivePoint(Player player)
    {
        if (player == Player.P1) GivePointToP1();
        else GivePointToP2();
    }

    private void HandleReceiverRacket(Player receiver)
    {
        bool missedCourt = false;
        if (tableFrame != null && ballRigidbody != null)
        {
            Vector3 position = tableFrame.InverseTransformPoint(ballRigidbody.position);
            Vector3 velocity = tableFrame.InverseTransformDirection(
                hasVelocitySample ? incomingBallVelocity : ballRigidbody.linearVelocity);
            // An incoming ball outside the court and travelling away is not
            // obstructed by collecting it. A ball still approaching is different.
            bool belowAndFalling = position.y < tableHeight - ballRadius && velocity.y <= 0f;
            bool wideAndLeaving = (position.z > tableHalfWidth + ballRadius && velocity.z >= 0f) ||
                (position.z < -tableHalfWidth - ballRadius && velocity.z <= 0f);
            missedCourt = belowAndFalling || wideAndLeaving;
        }
        GivePoint(missedCourt ? receiver : (receiver == Player.P1 ? Player.P2 : Player.P1));
    }

    public void BallEventHappened(BallEvent ballEvent)
    {
        if (!HasAuthority || TryFinishOutBall())
            return;

        Debug.Log(
            "Ball event: " + ballEvent +
            " | State: " + state +
            " | Server: " + currentServer
        );

        switch (state)
        {
            // =========================
            // WAITING FOR SERVE
            // =========================

            case GameState.WaitingForServe:

                if (currentServer == Player.P1)
                {
                    if (ballEvent == BallEvent.P1Racket)
                    {
                        state = GameState.ExpectServerTable;
                    }
                }
                else
                {
                    if (ballEvent == BallEvent.P2Racket)
                    {
                        state = GameState.ExpectServerTable;
                    }
                }

                break;


            // =========================
            // SERVE: SERVER'S OWN SIDE
            // =========================

            case GameState.ExpectServerTable:

                if (currentServer == Player.P1)
                {
                    if (ballEvent == BallEvent.P1Table)
                    {
                        state = GameState.ExpectReceiverTable;
                    }
                    else
                    {
                        GivePointToP2();
                    }
                }
                else
                {
                    if (ballEvent == BallEvent.P2Table)
                    {
                        state = GameState.ExpectReceiverTable;
                    }
                    else
                    {
                        GivePointToP1();
                    }
                }

                break;


            // =========================
            // SERVE: RECEIVER'S SIDE
            // =========================

            case GameState.ExpectReceiverTable:

                if (currentServer == Player.P1)
                {
                    if (ballEvent == BallEvent.P2Table)
                    {
                        state = GameState.ExpectP2Racket;
                    }
                    else if (ballEvent == BallEvent.P2Racket)
                    {
                        HandleReceiverRacket(Player.P2);
                    }
                    else
                    {
                        GivePointToP2();
                    }
                }
                else
                {
                    if (ballEvent == BallEvent.P1Table)
                    {
                        state = GameState.ExpectP1Racket;
                    }
                    else if (ballEvent == BallEvent.P1Racket)
                    {
                        HandleReceiverRacket(Player.P1);
                    }
                    else
                    {
                        GivePointToP1();
                    }
                }

                break;

            // =========================
            // NORMAL RALLY
            // =========================

            case GameState.ExpectP1Racket:

                if (ballEvent == BallEvent.P1Racket)
                {
                    state = GameState.ExpectP2Table;
                }
                else if (ballEvent == BallEvent.P1Table)
                {
                    GivePointToP2();
                }
                else if (ballEvent == BallEvent.Ground)
                {
                    GivePointToP2();
                }
                else if (ballEvent == BallEvent.P2Racket)
                {
                    GivePointToP1();
                }
                else if (ballEvent == BallEvent.Net)
                {
                    GivePointToP1();
                }
                else if (ballEvent == BallEvent.P2Table)
                {
                    GivePointToP2();
                }

                break;


            case GameState.ExpectP2Table:

                if (ballEvent == BallEvent.P2Table)
                {
                    state = GameState.ExpectP2Racket;
                }
                else if (ballEvent == BallEvent.P1Table)
                {
                    GivePointToP2();
                }
                else if (ballEvent == BallEvent.Ground)
                {
                    GivePointToP2();
                }
                else if (ballEvent == BallEvent.P1Racket)
                {
                    GivePointToP2();
                }
                else if (ballEvent == BallEvent.P2Racket)
                {
                    HandleReceiverRacket(Player.P2);
                }
                else if (ballEvent == BallEvent.Net)
                {
                    GivePointToP2();
                }

                break;


            case GameState.ExpectP2Racket:

                if (ballEvent == BallEvent.P2Racket)
                {
                    state = GameState.ExpectP1Table;
                }
                else if (ballEvent == BallEvent.P2Table)
                {
                    GivePointToP1();
                }
                else if (ballEvent == BallEvent.Ground)
                {
                    GivePointToP1();
                }
                else if (ballEvent == BallEvent.P1Racket)
                {
                    GivePointToP2();
                }
                else if (ballEvent == BallEvent.Net)
                {
                    GivePointToP2();
                }
                else if (ballEvent == BallEvent.P1Table)
                {
                    GivePointToP1();
                }

                break;


            case GameState.ExpectP1Table:

                if (ballEvent == BallEvent.P1Table)
                {
                    state = GameState.ExpectP1Racket;
                }
                else if (ballEvent == BallEvent.P2Table)
                {
                    GivePointToP1();
                }
                else if (ballEvent == BallEvent.Ground)
                {
                    GivePointToP1();
                }
                else if (ballEvent == BallEvent.P1Racket)
                {
                    HandleReceiverRacket(Player.P1);
                }
                else if (ballEvent == BallEvent.P2Racket)
                {
                    GivePointToP1();
                }
                else if (ballEvent == BallEvent.Net)
                {
                    GivePointToP1();
                }

                break;


            case GameState.PointOver:
                // Ignore everything until a new point starts.
                break;
            

            case GameState.GameOver:
                // Ignore all ball events after the game has ended.
                break;
        }

        Debug.Log("New state: " + state);
    }


    void GivePointToP1()
    {
        p1Score++;

        Debug.Log("POINT TO P1");
        Debug.Log("Score: P1 " + p1Score + " - " + p2Score + " P2");

        state = GameState.PointOver;

        if (HasPlayerWonGame())
        {
            return;
        }

        StartCoroutine(StartNextPointAfterDelay());

        UpdateServer();
    }


    void GivePointToP2()
    {
        p2Score++;

        Debug.Log("POINT TO P2");
        Debug.Log("Score: P1 " + p1Score + " - " + p2Score + " P2");

        state = GameState.PointOver;

        if (HasPlayerWonGame())
        {
            return;
        }
        
        StartCoroutine(StartNextPointAfterDelay());

        UpdateServer();
    }


    void UpdateServer()
    {
        int totalPoints = p1Score + p2Score;

        // At 10-10 or later, serve changes every point.
        if (p1Score >= 10 && p2Score >= 10)
        {
            SwitchServer();
            return;
        }

        // Before deuce, serve changes every 2 points.
        if (totalPoints % 2 == 0)
        {
            SwitchServer();
        }
    }


    void SwitchServer()
    {
        if (currentServer == Player.P1)
        {
            currentServer = Player.P2;
        }
        else
        {
            currentServer = Player.P1;
        }

        Debug.Log("SERVER CHANGED TO " + currentServer);
    }


        public void StartNewPoint()
        {
            if (!HasAuthority) return;
            if (state == GameState.GameOver)
            {
                Debug.Log("Cannot start new point. Game is over.");
                return;
            }

            state = GameState.WaitingForServe;

            hasVelocitySample = false;

            Debug.Log("NEW POINT");
            Debug.Log("Server: " + currentServer);
        }

    bool HasPlayerWonGame()
    {
        if (p1Score >= 11 && p1Score - p2Score >= 2)
        {
            Debug.Log("P1 WINS THE GAME!");
            state = GameState.GameOver;
            return true;
        }

        if (p2Score >= 11 && p2Score - p1Score >= 2)
        {
            Debug.Log("P2 WINS THE GAME!");
            state = GameState.GameOver;
            return true;
        }

        return false;
    }

    IEnumerator StartNextPointAfterDelay()
    {
        yield return new WaitForSeconds(1f);

        if (!HasAuthority) yield break;

        ResetBall();

        StartNewPoint();
    }

    void ResetBall()
    {
        if (!HasAuthority) return;
        if (ballRigidbody == null)
        {
            Debug.LogError("Ball Rigidbody has not been assigned!");
            return;
        }

        Transform servePosition =
            currentServer == Player.P1
            ? p1ServePosition
            : p2ServePosition;

        if (servePosition == null)
        {
            Debug.LogError("Serve position has not been assigned!");
            return;
        }

        ballRigidbody.linearVelocity = Vector3.zero;
        ballRigidbody.angularVelocity = Vector3.zero;

        ballRigidbody.position = servePosition.position;
        ballRigidbody.rotation = servePosition.rotation;
    }


    public void StartNewGame()
    {
        if (!HasAuthority) return;

        StopAllCoroutines();
        hasVelocitySample = false;
        p1Score = 0;
        p2Score = 0;

        currentServer = Player.P2;

        state = GameState.WaitingForServe;

        ResetBall();

        Debug.Log("NEW GAME");
        Debug.Log("Server: " + currentServer);
    }
}
