using System;
// Runtime player diagnostics are persisted under Application.persistentDataPath.

using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Keeps a copy of Unity player logs in persistent storage so diagnostics survive a build run.
/// The file is especially useful on a headset where the Unity Console is unavailable.
/// </summary>
public static class RuntimeDiagnostics
{
    private static readonly object Sync = new object();
    private static string logPath;
    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        logPath = Path.Combine(Application.persistentDataPath, "table-tennis-vr.log");
        Application.logMessageReceivedThreaded += HandleUnityLog;

        WriteLine($"--- Runtime started {DateTime.UtcNow:O} ---");
        WriteLine($"Application={Application.identifier}, version={Application.version}, platform={Application.platform}, device={SystemInfo.deviceModel}, persistentDataPath={Application.persistentDataPath}");
        Debug.Log($"[Diagnostics] Build log path: {logPath}");
    }

    public static void Log(string message)
    {
        Debug.Log($"[Diagnostics] {message}");
    }

    public static void LogWarning(string message)
    {
        Debug.LogWarning($"[Diagnostics] {message}");
    }

    public static void LogError(string message)
    {
        Debug.LogError($"[Diagnostics] {message}");
    }

    private static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        string suffix = string.IsNullOrEmpty(stackTrace) ? string.Empty : $"\n{stackTrace}";
        WriteLine($"{DateTime.UtcNow:O} [{type}] {condition}{suffix}");
    }

    private static void WriteLine(string line)
    {
        if (string.IsNullOrEmpty(logPath))
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never cause a player failure or recurse through Unity's logger.
        }
    }
}
