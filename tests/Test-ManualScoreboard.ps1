param([string]$UnityData = 'C:/Unity/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$temp = Join-Path ([IO.Path]::GetTempPath()) ('scoreboard-test-' + [guid]::NewGuid())
$null = New-Item -ItemType Directory $temp
$stub = @'
using System;
using System.Reflection;
namespace UnityEngine {
 public class SerializeField:Attribute {}
 public class MonoBehaviour {
  public static object Found; public Transform transform=new Transform();
  protected static T FindFirstObjectByType<T>() where T:class { return Found as T; }
  protected void Invoke(string name,float delay) {} protected void CancelInvoke(string name) {}
 }
 public class Transform { public Vector3 position; public Vector3 InverseTransformPoint(Vector3 v)=>v; public Vector3 TransformPoint(Vector3 v)=>v; }
 public struct Vector3 {}
 public static class Mathf { public static int Max(int a,int b)=>Math.Max(a,b); }
 public static class Debug { public static void Log(object message) {} }
}
namespace Unity.Netcode {
 public class NetworkManager { public static NetworkManager Singleton; public bool IsListening; }
 public class NetworkBehaviour:UnityEngine.MonoBehaviour { public bool IsSpawned,IsServer; }
 public class NetworkVariable<T> { public T Value; }
 public enum SendTo { Server } public enum RpcInvokePermission { Everyone }
 public class RpcAttribute:Attribute { public RpcInvokePermission InvokePermission; public RpcAttribute(SendTo target) {} }
}
namespace TMPro { public class TMP_Text { public string text; } }
public class TableTennisBall { public UnityEngine.Transform transform=new UnityEngine.Transform(); public void ResetForServe(UnityEngine.Vector3 v) {} }
public class TableTennisMRPlacement { public void LockForMatch() {} }
public static class Test {
 static void Check(bool pass,string message) { if(!pass) throw new Exception(message); }
 static void Call(object obj,string method) { obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null); }
 public static void Main() {
  foreach(bool online in new[]{false,true}) foreach(int player in new[]{1,2}) {
   Unity.Netcode.NetworkManager.Singleton=online ? new Unity.Netcode.NetworkManager {IsListening=true} : null;
   var m=new TableTennisMatch {IsSpawned=online,IsServer=online};
   UnityEngine.MonoBehaviour.Found=m;
   var board=new Scoreboard {player1ScoreText=new TMPro.TMP_Text(),player2ScoreText=new TMPro.TMP_Text()};
   Call(board,"Awake");
   Action plus=player==1 ? (Action)board.Plus1 : board.Plus2;
   Action minus=player==1 ? (Action)board.Minus1 : board.Minus2;
   minus(); Check(m.PlayerOneScore==0 && m.PlayerTwoScore==0,"Score below zero");
   for(int i=0;i<10;i++) plus();
   Check(!m.IsGameOver,"Premature winner"); plus();
   Check(m.IsGameOver && m.Winner==player,"11-0 did not win");
   Call(board,"Update"); Check((player==1 ? board.player1ScoreText.text : board.player2ScoreText.text)=="11","Display not using match score");
   plus(); Check((player==1 ? m.PlayerOneScore : m.PlayerTwoScore)==11,"Scored after game over");
   minus(); Check(!m.IsGameOver && m.Winner==0,"Correction did not clear winner");
   m.RequestResetMatch();
   for(int i=0;i<10;i++){board.Plus1();board.Plus2();}
   plus(); Check(!m.IsGameOver,"11-10 ended game");
   plus(); Check(m.IsGameOver && m.Winner==player,"12-10 did not win");
   m.RequestResetMatch(); Check(!m.IsGameOver && m.Winner==0 && m.PlayerOneScore==0 && m.PlayerTwoScore==0,"Reset failed");
   m.RequestAdjustScore(3,1); m.RequestAdjustScore(1,20);
   Check(m.PlayerOneScore==0 && m.PlayerTwoScore==0,"Invalid adjustment accepted");
  }
  Console.WriteLine("PASS: manual buttons, both winners, 11-0, deuce, corrections, reset and validation in offline and host paths. RPC transport requires Unity multiplayer testing.");
 }
}
'@
$stubPath = Join-Path $temp 'Test.cs'
[IO.File]::WriteAllText($stubPath, $stub)
$exe = Join-Path $temp 'Test.exe'
$framework = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319'
& "$UnityData/NetCoreRuntime/dotnet.exe" "$UnityData/DotNetSdkRoslyn/csc.dll" /nologo /noconfig /nostdlib /langversion:9 "/r:$framework/mscorlib.dll" "/r:$framework/System.dll" "/out:$exe" $stubPath "$repo/table-tennis-vr/Assets/Scripts/Scoreboard.cs" "$repo/table-tennis-vr/Assets/Scripts/TableTennisMatch.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Scoring checks failed' }
