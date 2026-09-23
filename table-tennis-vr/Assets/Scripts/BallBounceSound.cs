using UnityEngine;

public class BallBounceSound : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip bounceSound;

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.relativeVelocity.magnitude > 0.5f)
        {
            audioSource.PlayOneShot(bounceSound);
        }
    }
}
