using UnityEngine;

public class BallBounceSound : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip bouncesound;

    private void OnCollisionEnter(Collision collision)
    {
        audioSource.PlayOneShot(bouncesound);
    }
}
