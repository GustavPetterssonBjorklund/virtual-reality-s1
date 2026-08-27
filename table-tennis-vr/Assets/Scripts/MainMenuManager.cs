using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    // Method to load SampleScene
    public void LoadSampleScene()
    {
        SceneManager.LoadScene("SampleScene");
    }


    public void QuitGame()
    {
        // Closes the application in a built game (.exe)
        Application.Quit();

        // Stops Play Mode if you are testing inside the Unity Editor
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
    // public void RestartLevel() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);
}