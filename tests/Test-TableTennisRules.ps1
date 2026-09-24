$ErrorActionPreference='Stop'
$stub=@'
namespace Unity.Netcode { public class NetworkManager { public static NetworkManager Singleton; public bool IsListening,IsServer; } }
namespace UnityEngine {
 public class SerializeField:System.Attribute {}
 public enum FindObjectsSortMode { None }
 public class MonoBehaviour { public Transform transform=new Transform(); protected static T[] FindObjectsByType<T>(FindObjectsSortMode m){return new T[0];} protected void StartCoroutine(System.Collections.IEnumerator r){} protected void StopAllCoroutines(){} }
 public class Rigidbody { public Vector3 position,linearVelocity,angularVelocity; public object rotation; }
 public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} public static Vector3 zero; }
 public class Transform { public Vector3 position; public object rotation; public Transform parent; public float yaw;
 public Vector3 InverseTransformDirection(Vector3 p){return new Vector3(p.x,p.y,p.z);} public Vector3 InverseTransformPoint(Vector3 p){double c=System.Math.Cos(yaw),s=System.Math.Sin(yaw);float x=p.x-position.x,z=p.z-position.z;return new Vector3((float)(c*x-s*z),p.y-position.y,(float)(s*x+c*z));} }
 public class WaitForSeconds { public WaitForSeconds(float f){} }
 public static class Debug { public static void Log(object o){} public static void LogError(object o){} }
}
public class BallEventTrigger:UnityEngine.MonoBehaviour { public TableTennisRules rules; public TableTennisRules.BallEvent eventType; }
'@
$repoRoot=Split-Path -Parent $PSScriptRoot
$source=Get-Content -Raw (Join-Path $repoRoot 'table-tennis-vr/Assets/Scripts/TableTennisRules.cs')
# Windows PowerShell's compiler predates expression-bodied properties.
$source=$source.Replace('public bool HasAuthority => NetworkManager.Singleton == null ||','public bool HasAuthority { get { return NetworkManager.Singleton == null ||').Replace('!NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;','!NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer; } }')
Add-Type -TypeDefinition ($source+$stub)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$count=0
foreach($p1 in @($true,$false)){
 $side=1
 $receiver='P2Racket'
 $table='P2Table'
 $rally='ExpectP2Table'
 if(!$p1){$side=-1;$receiver='P1Racket';$table='P1Table';$rally='ExpectP1Table'}
 foreach($case in @(
  @{Name='Serve obstruction';State='ExpectReceiverTable';X=1;Y=1;Event=$receiver;ServerWins=$true},
  @{Name='Rally obstruction';State=$rally;X=1;Y=1;Event=$receiver;ServerWins=$true},
  @{Name='Out serve';State='ExpectReceiverTable';X=2;Y=1;Event=$receiver;ServerWins=$false},
  @{Name='Out return';State=$rally;X=2;Y=1;Event=$receiver;ServerWins=$false},
  @{Name='Below table';State=$rally;X=1;Y=0.5;Event=$receiver;ServerWins=$false},
  @{Name='Wide leaving';State=$rally;X=1;Y=1;Event=$receiver;ServerWins=$false;Z=2;VZ=1},
  @{Name='Wide approaching';State=$rally;X=1;Y=1;Event=$receiver;ServerWins=$true;Z=2;VZ=-1},
  @{Name='Net wall';State=$rally;X=1;Y=1;Event='Net';ServerWins=$false},
  @{Name='Edge bounce';State=$rally;X=1.38;Y=0.78;Event=$table;NoPoint=$true},
  @{Name='Moved rotated table';State=$rally;X=2;Y=1;Event=$receiver;ServerWins=$false;Rotated=$true}
 )){
  $r=New-Object TableTennisRules
  if($p1){$r.currentServer=[TableTennisRules+Player]::P1}
  $r.state=[Enum]::Parse([TableTennisRules+GameState],$case.State)
  $r.ballRigidbody=New-Object UnityEngine.Rigidbody
  $r.ballRigidbody.position=New-Object UnityEngine.Vector3 ($side*$case.X),$case.Y,0
  if($case.Z){$r.ballRigidbody.position=New-Object UnityEngine.Vector3 ($side*$case.X),$case.Y,$case.Z;$r.ballRigidbody.linearVelocity=New-Object UnityEngine.Vector3 0,0,$case.VZ}
  $frame=New-Object UnityEngine.Transform
  if($case.Rotated){$frame.position=New-Object UnityEngine.Vector3 10,2,10;$frame.yaw=[Math]::PI/2;$r.ballRigidbody.position=New-Object UnityEngine.Vector3 10,3,(10-$side*2)}
  [TableTennisRules].GetField('tableFrame',$flags).SetValue($r,$frame)
  $r.BallEventHappened([Enum]::Parse([TableTennisRules+BallEvent],$case.Event))
  if($case.NoPoint){if(($r.p1Score+$r.p2Score)-ne 0){throw $case.Name}}
  else{
   $p1Wins=($p1 -eq $case.ServerWins)
   if($r.p1Score-ne [int]$p1Wins -or $r.p2Score-ne [int](!$p1Wins)){throw $case.Name}
   $r.BallEventHappened([TableTennisRules+BallEvent]::Ground)
   if(($r.p1Score+$r.p2Score)-ne 1){throw 'Duplicate point'}
  }
  $count++
 }
}
[Unity.Netcode.NetworkManager]::Singleton=New-Object Unity.Netcode.NetworkManager
[Unity.Netcode.NetworkManager]::Singleton.IsListening=$true
$r=New-Object TableTennisRules
$r.state=[TableTennisRules+GameState]::ExpectP2Table
$r.p1Score=7
$r.BallEventHappened([TableTennisRules+BallEvent]::Ground)
$r.StartNewPoint()
$r.StartNewGame()
if($r.p1Score-ne 7 -or $r.state-ne [TableTennisRules+GameState]::ExpectP2Table){throw 'Client changed rules'}
$count++
$routine=[TableTennisRules].GetMethod('StartNextPointAfterDelay',$flags).Invoke($r,$null)
$null=$routine.MoveNext()
if($routine.MoveNext() -or $r.state-ne [TableTennisRules+GameState]::ExpectP2Table){throw 'Client ran delayed reset'}
$count++
[Unity.Netcode.NetworkManager]::Singleton.IsServer=$true
$r.BallEventHappened([TableTennisRules+BallEvent]::Ground)
if($r.p2Score-ne 1){throw 'Host failed to score'}
$count++
"$count scenario checks passed, including both players, edge tolerance, moved table, client guards, pending resets, and host scoring."

