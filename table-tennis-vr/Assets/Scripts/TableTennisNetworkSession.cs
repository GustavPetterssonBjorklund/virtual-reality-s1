using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityTransport = Unity.Netcode.Transports.UTP.UnityTransport;

public sealed class TableTennisNetworkSession : MonoBehaviour
{
    [SerializeField] private int maxConnections = 1;
    [SerializeField] private string connectionType = "dtls";

    private NetworkManager networkManager;
    private UnityTransport transport;
    [Header("Lobby UI")]
    [SerializeField] private Canvas networkCanvas;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI codeText;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    private bool isBusy;
    private TouchScreenKeyboard questKeyboard;
    private Coroutine keyboardActivationRoutine;
    private EventTrigger.Entry joinCodePointerClickEntry;
    private string lastKeyboardStatus;
    private int lastKeyboardTextLength = -1;

    public bool IsConnected => networkManager != null && networkManager.IsListening;
    public bool IsServer => networkManager != null && networkManager.IsServer;

    private void Awake()
    {
        RuntimeDiagnostics.Log($"Network session Awake. Platform={Application.platform}, isEditor={Application.isEditor}, persistentDataPath={Application.persistentDataPath}");
        EnsureNetworkManager();
        if (!InitializeNetworkPanel())
        {
            return;
        }

        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        Application.focusChanged += HandleApplicationFocusChanged;
    }

    private void OnDestroy()
    {
        Application.focusChanged -= HandleApplicationFocusChanged;
        RemoveNetworkPanelListeners();
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void Update()
    {
        if (questKeyboard == null || joinCodeInput == null)
        {
            return;
        }

        joinCodeInput.SetTextWithoutNotify((questKeyboard.text ?? string.Empty).ToUpperInvariant());
        string keyboardStatus = questKeyboard.status.ToString();
        int keyboardTextLength = questKeyboard.text == null ? 0 : questKeyboard.text.Length;
        if (keyboardStatus != lastKeyboardStatus || keyboardTextLength != lastKeyboardTextLength)
        {
            RuntimeDiagnostics.Log($"Join keyboard update. status={keyboardStatus}, textLength={keyboardTextLength}, inputTextLength={joinCodeInput.text.Length}");
            lastKeyboardStatus = keyboardStatus;
            lastKeyboardTextLength = keyboardTextLength;
        }
        if (questKeyboard.status != TouchScreenKeyboard.Status.Visible)
        {
            RuntimeDiagnostics.Log($"Join keyboard closed. finalStatus={keyboardStatus}, finalTextLength={keyboardTextLength}");
            questKeyboard = null;
        }
    }

    private void HandleApplicationFocusChanged(bool hasFocus)
    {
        RuntimeDiagnostics.Log($"Application focus changed. hasFocus={hasFocus}, keyboardExists={questKeyboard != null}, keyboardVisible={TouchScreenKeyboard.visible}");
    }

    public async void CreateSession()
    {
        if (isBusy || IsConnected)
        {
            return;
        }

        isBusy = true;
        SetStatus("Creating session...");

        try
        {
            await InitializeServices();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));

            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (!networkManager.StartHost())
            {
                throw new InvalidOperationException("NGO could not start the host.");
            }

            codeText.text = $"JOIN CODE\n{joinCode}";
            SetStatus("Hosting. Give the join code to Player 2.");
        }
        catch (Exception exception)
        {
            SetStatus($"Host failed: {exception.Message}");
            Debug.LogException(exception);
        }
        finally
        {
            isBusy = false;
        }
    }

    public void JoinSessionFromInput()
    {
        RuntimeDiagnostics.Log($"Join button pressed. inputExists={joinCodeInput != null}, inputTextLength={(joinCodeInput == null ? 0 : joinCodeInput.text.Length)}, selected={IsJoinCodeInputSelected()}");
        DeactivateJoinCodeInput();
        JoinSession(joinCodeInput == null ? string.Empty : joinCodeInput.text);
    }

    public void ResetBall()
    {
        FindFirstObjectByType<TableTennisMatch>()?.DebugResetBall();
    }

    public void ResetRackets()
    {
        TableTennisNetworkRacket[] rackets = FindObjectsByType<TableTennisNetworkRacket>(FindObjectsSortMode.None);
        foreach (TableTennisNetworkRacket racket in rackets)
        {
            racket.RequestResetToSpawn();
        }
    }

    public async void JoinSession(string joinCode)
    {
        if (isBusy || IsConnected || string.IsNullOrWhiteSpace(joinCode))
        {
            SetStatus("Enter a join code first.");
            return;
        }

        isBusy = true;
        SetStatus("Joining session...");

        try
        {
            await InitializeServices();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim());
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));

            if (!networkManager.StartClient())
            {
                throw new InvalidOperationException("NGO could not start the client.");
            }

            codeText.text = $"JOINED\n{joinCode.Trim().ToUpperInvariant()}";
            SetStatus("Connected. Waiting for the host to start the match.");
            DeactivateJoinCodeInput();
        }
        catch (Exception exception)
        {
            SetStatus($"Join failed: {exception.Message}");
            Debug.LogException(exception);
        }
        finally
        {
            isBusy = false;
        }
    }

    private async System.Threading.Tasks.Task InitializeServices()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private void EnsureNetworkManager()
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            GameObject managerObject = new GameObject("NetworkManager");
            DontDestroyOnLoad(managerObject);
            networkManager = managerObject.AddComponent<NetworkManager>();
            transport = managerObject.AddComponent<UnityTransport>();
            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.EnableSceneManagement = true;
        }
        else
        {
            transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = networkManager.gameObject.AddComponent<UnityTransport>();
                networkManager.NetworkConfig.NetworkTransport = transport;
            }
        }

        networkManager.NetworkConfig.TickRate = 60;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!networkManager.IsServer || clientId == NetworkManager.ServerClientId)
        {
            return;
        }

        SetStatus("Player 2 connected.");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (networkManager.IsServer && clientId != NetworkManager.ServerClientId)
        {
            SetStatus("Player 2 disconnected.");
        }
    }

    private bool InitializeNetworkPanel()
    {
        if (networkCanvas == null || statusText == null || codeText == null || joinCodeInput == null || hostButton == null || joinButton == null)
        {
            Debug.LogError("Network Session Panel references are missing. Assign the movable scene UI on Table Tennis Table.", this);
            enabled = false;
            return false;
        }

        networkCanvas.worldCamera = Camera.main != null ? Camera.main : FindFirstCamera();
        joinCodeInput.shouldHideSoftKeyboard = false;
        joinCodeInput.shouldHideMobileInput = false;
        hostButton.onClick.AddListener(CreateSession);
        joinButton.onClick.AddListener(JoinSessionFromInput);
        joinCodeInput.onSelect.AddListener(HandleJoinCodeInputSelected);
        AddJoinCodePointerClickListener();
        RuntimeDiagnostics.Log($"Network canvas initialized. worldCamera={(networkCanvas.worldCamera == null ? "null" : networkCanvas.worldCamera.name)}");
        return true;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log($"[Network] {message}");
    }

    /// <summary>Lets the colocated alignment flow reuse the lobby status display.</summary>
    public void ReportStatus(string message)
    {
        SetStatus(message);
    }

    private void AddJoinCodePointerClickListener()
    {
        EventTrigger trigger = joinCodeInput.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = joinCodeInput.gameObject.AddComponent<EventTrigger>();
        }

        trigger.triggers ??= new System.Collections.Generic.List<EventTrigger.Entry>();
        joinCodePointerClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        joinCodePointerClickEntry.callback.AddListener(HandleJoinCodeInputClicked);
        trigger.triggers.Add(joinCodePointerClickEntry);
    }

    private void HandleJoinCodeInputClicked(BaseEventData _)
    {
        joinCodeInput.ActivateInputField();
        HandleJoinCodeInputSelected();
    }

    private void RemoveNetworkPanelListeners()
    {
        hostButton?.onClick.RemoveListener(CreateSession);
        joinButton?.onClick.RemoveListener(JoinSessionFromInput);
        joinCodeInput?.onSelect.RemoveListener(HandleJoinCodeInputSelected);

        if (joinCodeInput == null || joinCodePointerClickEntry == null)
        {
            return;
        }

        EventTrigger trigger = joinCodeInput.GetComponent<EventTrigger>();
        trigger?.triggers?.Remove(joinCodePointerClickEntry);
        joinCodePointerClickEntry = null;
    }

    private void HandleJoinCodeInputSelected(string _) => HandleJoinCodeInputSelected();

    private void HandleJoinCodeInputSelected()
    {
        if (joinCodeInput == null)
        {
            RuntimeDiagnostics.LogWarning("Join input activation requested, but the input field is null.");
            return;
        }

        RuntimeDiagnostics.Log($"Join input selected. active={joinCodeInput.gameObject.activeInHierarchy}, enabled={joinCodeInput.enabled}, interactable={joinCodeInput.interactable}, selected={IsJoinCodeInputSelected()}, eventSystemExists={EventSystem.current != null}, keyboardSupported={TouchScreenKeyboard.isSupported}, shouldHideSoftKeyboard={joinCodeInput.shouldHideSoftKeyboard}, shouldHideMobileInput={joinCodeInput.shouldHideMobileInput}");
        if (keyboardActivationRoutine != null)
        {
            StopCoroutine(keyboardActivationRoutine);
        }

        keyboardActivationRoutine = StartCoroutine(OpenQuestKeyboard());
    }

    private IEnumerator OpenQuestKeyboard()
    {
        yield return null;

        if (joinCodeInput == null || !TouchScreenKeyboard.isSupported)
        {
            RuntimeDiagnostics.LogWarning($"Quest keyboard unavailable. inputExists={joinCodeInput != null}, supported={TouchScreenKeyboard.isSupported}");
            keyboardActivationRoutine = null;
            yield break;
        }

        RuntimeDiagnostics.Log($"Opening Quest keyboard. inputTextLength={joinCodeInput.text.Length}, keyboardType={joinCodeInput.keyboardType}, characterLimit={joinCodeInput.characterLimit}");
        questKeyboard = TouchScreenKeyboard.Open(
            joinCodeInput.text,
            joinCodeInput.keyboardType,
            false,
            false,
            false,
            false,
            "Enter join code",
            joinCodeInput.characterLimit);
        if (questKeyboard == null)
        {
            RuntimeDiagnostics.LogError("TouchScreenKeyboard.Open returned null.");
        }
        else
        {
            lastKeyboardStatus = questKeyboard.status.ToString();
            lastKeyboardTextLength = questKeyboard.text == null ? 0 : questKeyboard.text.Length;
            RuntimeDiagnostics.Log($"Quest keyboard requested. status={lastKeyboardStatus}, active={questKeyboard.active}, visible={TouchScreenKeyboard.visible}, area={TouchScreenKeyboard.area}, applicationFocused={Application.isFocused}, textLength={lastKeyboardTextLength}");
        }
        keyboardActivationRoutine = null;
    }

    private void DeactivateJoinCodeInput()
    {
        if (joinCodeInput == null)
        {
            return;
        }

        RuntimeDiagnostics.Log($"Deactivating join input. focused={joinCodeInput.isFocused}, selected={IsJoinCodeInputSelected()}, keyboardExists={questKeyboard != null}");
        joinCodeInput.DeactivateInputField();
        questKeyboard = null;
        lastKeyboardStatus = null;
        lastKeyboardTextLength = -1;
        if (keyboardActivationRoutine != null)
        {
            StopCoroutine(keyboardActivationRoutine);
            keyboardActivationRoutine = null;
        }
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == joinCodeInput.gameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private static Camera FindFirstCamera()
    {
        Camera[] cameras = Camera.allCameras;
        return cameras.Length == 0 ? null : cameras[0];
    }

    private bool IsJoinCodeInputSelected()
    {
        return joinCodeInput != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == joinCodeInput.gameObject;
    }

}
