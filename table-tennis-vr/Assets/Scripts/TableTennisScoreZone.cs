using Unity.Netcode;
using UnityEngine;

public sealed class TableTennisScoreZone : MonoBehaviour
{
    [SerializeField] private int pointForPlayer = 1;

    private TableTennisMatch match;

    private void Awake()
    {
        match = FindFirstObjectByType<TableTennisMatch>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (other.GetComponentInParent<TableTennisBall>() == null || match == null)
        {
            return;
        }

        match.AwardPoint(pointForPlayer);
    }
}
