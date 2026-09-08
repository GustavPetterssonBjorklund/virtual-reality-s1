using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    const string HapticsPreference = "haptics_enabled";

    [SerializeField] GameObject mainMenuPanel;
    [SerializeField] GameObject settingsPanel;
    [SerializeField] Button settingsButton;
    [SerializeField] Button backButton;
    [SerializeField] Slider masterVolumeSlider;
    [SerializeField] Toggle hapticsToggle;

    void Awake()
    {
        settingsButton.onClick.AddListener(OpenSettings);
        backButton.onClick.AddListener(CloseSettings);
        masterVolumeSlider.value = AudioListener.volume;
        masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
        hapticsToggle.isOn = PlayerPrefs.GetInt(HapticsPreference, 1) == 1;
        hapticsToggle.onValueChanged.AddListener(SetHapticsEnabled);
        UpdateHapticsVisual(hapticsToggle.isOn);
    }

    public void LoadSampleScene() => SceneManager.LoadScene("SampleScene");

    // Kept separate so the post-match navigation can diverge from the main menu later.
    public void PlayAgainYes() => SceneManager.LoadScene("SampleScene");

    public void PlayAgainNo() => SceneManager.LoadScene("SampleScene");

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void OpenSettings()
    {
        mainMenuPanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    static void SetMasterVolume(float value) => AudioListener.volume = value;

    void SetHapticsEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(HapticsPreference, enabled ? 1 : 0);
        PlayerPrefs.Save();
        UpdateHapticsVisual(enabled);
    }

    void UpdateHapticsVisual(bool enabled)
    {
        if (hapticsToggle.graphic == null)
            return;

        var knob = hapticsToggle.graphic.rectTransform;
        knob.anchorMin = enabled ? new Vector2(0.54f, 0.12f) : new Vector2(0.08f, 0.12f);
        knob.anchorMax = enabled ? new Vector2(0.92f, 0.88f) : new Vector2(0.46f, 0.88f);
        knob.offsetMin = Vector2.zero;
        knob.offsetMax = Vector2.zero;
    }
}
