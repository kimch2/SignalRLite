using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SignalRLite;
using SignalRLite.Encoders;

/// <summary>
/// On-device diagnostic runner for SignalRLite.
/// 
/// Setup:
///   1. Import this Sample via Package Manager → SignalR Lite → Samples → Device Test
///   2. Create a new scene, add a Canvas (Screen Space - Overlay)
///   3. Attach this script to a GameObject
///   4. Assign the ScrollRect, LogText, StatusText, and HubUrl fields in the Inspector
///   5. Build & run on device; tap the buttons to run each test
///
/// All results appear in the on-screen log panel (no Xcode / adb required).
/// </summary>
public class SignalRLiteDeviceTest : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Server")]
    [Tooltip("Full hub URL, e.g. https://your-server.com/testhub")]
    public string HubUrl = "https://your-server.com/testhub";

    [Tooltip("Use MessagePack binary protocol instead of JSON.\n" +
             "Requires SIGNALRLITE_MESSAGEPACK_CSHARP define and server-side AddMessagePackProtocol().")]
    public bool UseMessagePack = false;

    [Header("UI References (optional – falls back to Debug.Log)")]
    public Text   StatusText;
    public Text   LogText;
    public ScrollRect LogScroll;

    // ── State ─────────────────────────────────────────────────────────────────

    private HubConnection _hub;
    private readonly List<string> _logLines = new List<string>();

    // Test result tracking
    private bool _connectEventFired;
    private bool _disconnectEventFired;
    private bool _echoOk;
    private bool _getTimeOk;
    private bool _complexTypeOk;
    private bool _reconnectFired;

    // Per-test timing
    private float _testStartTime;

    // Summary accumulator: (name, pass, elapsedMs)
    private readonly List<(string name, bool pass, int ms)> _results = new List<(string, bool, int)>();

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        Application.logMessageReceived += OnUnityLog;
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnUnityLog;
        _hub?.StartClose();
    }

    // ── Public buttons (wire in Inspector) ───────────────────────────────────

    public void RunAllTests()      => StartCoroutine(CoRunAll());
    public void RunMessagePackTest()=> StartCoroutine(CoRunMessagePack());
    public void TestConnect()      => StartCoroutine(CoConnect());
    public void TestEcho()         => StartCoroutine(CoEcho());
    public void TestGetTime()      => StartCoroutine(CoGetTime());
    public void TestComplex()      => StartCoroutine(CoComplex());
    public void TestReconnect()    => StartCoroutine(CoReconnect());
    public void Disconnect()       { _hub?.StartClose(); SetStatus("Disconnected"); }

    // ── Test coroutines ───────────────────────────────────────────────────────

    private IEnumerator CoRunAll()
    {
        ClearLog();
        _results.Clear();
        float suiteStart = Time.realtimeSinceStartup;

        Log("=== SignalRLite Device Test ===");
        Log($"Platform : {Application.platform}");
        Log($"Unity    : {Application.unityVersion}");
        Log($"URL      : {HubUrl}");
        Log("");

        yield return CoConnect();
        yield return new WaitForSeconds(0.5f);

        if (!_connectEventFired)
        {
            Log("[SKIP] Not connected – aborting remaining tests");
            PrintSummary(suiteStart);
            yield break;
        }

        // T1 disconnected the hub; reconnect for the remaining tests.
        Log("[Setup] Reconnecting for T2–T4 …");
        _connectEventFired = false;
        CreateHub();
        _hub.OnConnected += _ => _connectEventFired = true;
        _hub.StartConnect();
        yield return WaitFor(() => _connectEventFired, 10f);
        if (!_connectEventFired)
        {
            Log("[SKIP] Reconnect failed – aborting remaining tests");
            PrintSummary(suiteStart);
            yield break;
        }
        Log("[Setup] Reconnected.");

        yield return CoEcho();
        yield return new WaitForSeconds(0.3f);
        yield return CoGetTime();
        yield return new WaitForSeconds(0.3f);
        yield return CoComplex();
        yield return new WaitForSeconds(0.3f);

        PrintSummary(suiteStart);
    }

    private void PrintSummary(float suiteStart)
    {
        int totalMs = Mathf.RoundToInt((Time.realtimeSinceStartup - suiteStart) * 1000f);
        int pass = 0, fail = 0, skip = 0;
        foreach (var r in _results)
        {
            if (r.pass) pass++; else fail++;
        }

        Log("");
        Log("╔══════════════════════════════════════╗");
        Log("║         Test Summary                 ║");
        Log("╠══════════════════════════════════════╣");
        foreach (var (name, p, ms) in _results)
        {
            string icon   = p ? "✓ PASS" : "✗ FAIL";
            string timing = ms >= 0 ? $"{ms,5} ms" : " skipped";
            Log($"║  {icon}  {name,-18} {timing}  ║");
        }
        Log("╠══════════════════════════════════════╣");
        Log($"║  Passed : {pass}   Failed : {fail}   Total : {pass + fail}        ║");
        Log($"║  Suite elapsed : {totalMs} ms                ║");
        Log("╚══════════════════════════════════════╝");

        SetStatus(fail == 0 ? $"All {pass} tests passed" : $"{fail} FAILED / {pass} passed");
    }

    // ── Test 1: Connect & Disconnect ─────────────────────────────────────────

    private IEnumerator CoConnect()
    {
        Log("[T1] Connect & Disconnect …");
        _connectEventFired    = false;
        _disconnectEventFired = false;
        float t0 = Time.realtimeSinceStartup;

        CreateHub();
        _hub.OnConnected    += _ => { _connectEventFired = true; };
        // If the first attempt triggers a reconnect cycle, OnReconnected fires instead of OnConnected.
        _hub.OnReconnected  += _ => { _connectEventFired = true; };
        _hub.OnReconnecting += _ => Log("[T1] Reconnecting (first attempt failed, retrying…)");
        _hub.OnDisconnected += (_, __) => { _disconnectEventFired = true; };
        _hub.StartConnect();

        yield return WaitFor(() => _connectEventFired, 15f);

        int ms = Ms(t0);
        if (_connectEventFired)
        {
            Log($"[T1] PASS – OnConnected fired ({ms} ms)");
            SetStatus("Connected");
            RecordResult("T1 Connect", true, ms);
        }
        else
        {
            Log($"[T1] FAIL – OnConnected did not fire within 15 s");
            SetStatus("Connect failed");
            RecordResult("T1 Connect", false, ms);
            yield break;
        }

        _hub.StartClose();
        yield return WaitFor(() => _disconnectEventFired, 5f);
        bool discOk = _disconnectEventFired;
        Log($"[T1] Disconnect: {(discOk ? "PASS" : "FAIL")}");
        RecordResult("T1 Disconnect", discOk, Ms(t0));
    }

    // ── Test 2: Echo (string Invoke) ─────────────────────────────────────────

    private IEnumerator CoEcho()
    {
        Log("[T2] Echo …");
        _echoOk = false;
        float t0 = Time.realtimeSinceStartup;

        if (!EnsureConnected()) { Log("[T2] SKIP – not connected"); RecordResult("T2 Echo", false, -1); yield break; }

        const string payload = "hello-device";
        _hub.Invoke<string>("Echo", (result, err) =>
        {
            if (err != null) Log($"[T2] FAIL – error: {err}");
            else if (result == payload) { _echoOk = true; Log($"[T2] PASS – got: {result} ({Ms(t0)} ms)"); }
            else Log($"[T2] FAIL – expected '{payload}' got '{result}'");
        }, payload);

        yield return WaitFor(() => _echoOk, 5f);
        if (!_echoOk) Log("[T2] FAIL – timed out");
        RecordResult("T2 Echo", _echoOk, Ms(t0));
    }

    // ── Test 3: GetTime (string return) ──────────────────────────────────────

    private IEnumerator CoGetTime()
    {
        Log("[T3] GetTime …");
        _getTimeOk = false;
        float t0 = Time.realtimeSinceStartup;

        if (!EnsureConnected()) { Log("[T3] SKIP – not connected"); RecordResult("T3 GetTime", false, -1); yield break; }

        _hub.Invoke<string>("GetTime", (result, err) =>
        {
            if (err != null) { Log($"[T3] FAIL – {err}"); return; }
            _getTimeOk = !string.IsNullOrEmpty(result);
            Log(_getTimeOk ? $"[T3] PASS – {result} ({Ms(t0)} ms)" : "[T3] FAIL – empty result");
        });

        yield return WaitFor(() => _getTimeOk, 5f);
        if (!_getTimeOk) Log("[T3] FAIL – timed out");
        RecordResult("T3 GetTime", _getTimeOk, Ms(t0));
    }

    // ── Test 4: Complex type (JsonUtility deserialise) ────────────────────────

    private IEnumerator CoComplex()
    {
        Log("[T4] Complex type (GetPlayer) …");
        _complexTypeOk = false;
        float t0 = Time.realtimeSinceStartup;

        if (!EnsureConnected()) { Log("[T4] SKIP – not connected"); RecordResult("T4 ComplexType", false, -1); yield break; }

        _hub.Invoke<PlayerData>("GetPlayer", (player, err) =>
        {
            if (err != null) { Log($"[T4] FAIL – {err}"); return; }
            if (player == null) { Log("[T4] FAIL – null result (IL2CPP strip?)"); return; }
            _complexTypeOk = !string.IsNullOrEmpty(player.Name);
            Log(_complexTypeOk
                ? $"[T4] PASS – Name={player.Name} Score={player.Score} ({Ms(t0)} ms)"
                : $"[T4] FAIL – Name is null/empty (check JsonUtility PascalCase)");
        }, "TestDevice");

        yield return WaitFor(() => _complexTypeOk, 5f);
        if (!_complexTypeOk) Log("[T4] FAIL – timed out");
        RecordResult("T4 ComplexType", _complexTypeOk, Ms(t0));
    }

    // ── Test 5: Background / reconnect simulation ─────────────────────────────

    private IEnumerator CoReconnect()
    {
        Log("[T5] Reconnect simulation …");
        Log("[T5] Put the app in background now – waiting 35 s …");
        _reconnectFired = false;

        if (!EnsureConnected()) { Log("[T5] SKIP – not connected"); yield break; }

        _hub.OnReconnecting += _ => { _reconnectFired = true; Log("[T5] OnReconnecting fired"); };
        _hub.OnReconnected  += _ => Log("[T5] OnReconnected fired");

        yield return new WaitForSeconds(35f);

        if (_reconnectFired) Log("[T5] PASS – reconnect triggered");
        else                 Log("[T5] INFO – no reconnect triggered (connection may still be alive)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void CreateHub(bool forceMessagePack = false)
    {
        _hub?.StartClose();

        var options = new HubOptions
        {
            PingInterval    = TimeSpan.FromSeconds(15),
            PingTimeout     = TimeSpan.FromSeconds(30),
            ReconnectDelays = new TimeSpan?[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                null,
            },
        };

#if SIGNALRLITE_MESSAGEPACK_CSHARP
        if (UseMessagePack || forceMessagePack)
        {
            options.Protocol = new MessagePackCSharpProtocol();
            Log("[Hub] Protocol = MessagePack (binary)");
        }
        else
        {
            Log("[Hub] Protocol = JSON");
        }
#else
        if (UseMessagePack || forceMessagePack)
            Log("[Hub] WARNING: UseMessagePack=true but SIGNALRLITE_MESSAGEPACK_CSHARP is not defined – falling back to JSON");
        else
            Log("[Hub] Protocol = JSON");
#endif

        _hub = new HubConnection(HubUrl, options);
        _hub.OnError += (_, err) => Log($"[Error] {err}");
    }

    // ── MessagePack end-to-end test ───────────────────────────────────────────

    private IEnumerator CoRunMessagePack()
    {
        ClearLog();
        _results.Clear();
        float suiteStart = Time.realtimeSinceStartup;

        Log("=== SignalRLite MessagePack Test ===");
        Log($"Platform : {Application.platform}");
        Log($"URL      : {HubUrl}");

#if SIGNALRLITE_MESSAGEPACK_CSHARP
        Log("Protocol : MessagePack (SIGNALRLITE_MESSAGEPACK_CSHARP defined)");
#else
        Log("Protocol : JSON (SIGNALRLITE_MESSAGEPACK_CSHARP NOT defined – test aborted)");
        Log("[FAIL] Define SIGNALRLITE_MESSAGEPACK_CSHARP in Player Settings first.");
        PrintSummary(suiteStart);
        yield break;
#endif
        Log("");

        // ── MP-T1: Connect with MessagePack ──────────────────────────────────
        Log("[MP-T1] Connect with MessagePack …");
        _connectEventFired = false;
        float t0 = Time.realtimeSinceStartup;
        CreateHub(forceMessagePack: true);
        _hub.OnConnected   += _ => _connectEventFired = true;
        _hub.OnReconnected += _ => _connectEventFired = true;
        _hub.OnReconnecting += _ => Log("[MP-T1] Reconnecting (first attempt failed, retrying…)");
        _hub.StartConnect();
        yield return WaitFor(() => _connectEventFired, 15f);

        int ms = Ms(t0);
        bool mp1 = _connectEventFired;
        Log(mp1 ? $"[MP-T1] PASS – Connected ({ms} ms)" : "[MP-T1] FAIL – timeout (server may not support MessagePack)");
        RecordResult("MP-T1 Connect", mp1, ms);

        if (!mp1) { PrintSummary(suiteStart); yield break; }

        // ── MP-T2: Echo (binary round-trip) ──────────────────────────────────
        Log("[MP-T2] Echo (MessagePack binary) …");
        _echoOk = false;
        t0 = Time.realtimeSinceStartup;
        const string echoPayload = "msgpack-echo";
        _hub.Invoke<string>("Echo", (result, err) =>
        {
            if (err != null) Log($"[MP-T2] FAIL – {err}");
            else if (result == echoPayload) { _echoOk = true; Log($"[MP-T2] PASS – '{result}' ({Ms(t0)} ms)"); }
            else Log($"[MP-T2] FAIL – expected '{echoPayload}' got '{result}'");
        }, echoPayload);
        yield return WaitFor(() => _echoOk, 5f);
        if (!_echoOk) Log("[MP-T2] FAIL – timed out");
        RecordResult("MP-T2 Echo", _echoOk, Ms(t0));

        // ── MP-T3: GetTime (string return) ───────────────────────────────────
        Log("[MP-T3] GetTime (MessagePack) …");
        _getTimeOk = false;
        t0 = Time.realtimeSinceStartup;
        _hub.Invoke<string>("GetTime", (result, err) =>
        {
            if (err != null) { Log($"[MP-T3] FAIL – {err}"); return; }
            _getTimeOk = !string.IsNullOrEmpty(result);
            Log(_getTimeOk ? $"[MP-T3] PASS – {result} ({Ms(t0)} ms)" : "[MP-T3] FAIL – empty");
        });
        yield return WaitFor(() => _getTimeOk, 5f);
        if (!_getTimeOk) Log("[MP-T3] FAIL – timed out");
        RecordResult("MP-T3 GetTime", _getTimeOk, Ms(t0));

        // ── MP-T4: Complex type (MessagePackObject) ───────────────────────────
        Log("[MP-T4] Complex type GetPlayer (MessagePack) …");
        _complexTypeOk = false;
        t0 = Time.realtimeSinceStartup;
        _hub.Invoke<PlayerData>("GetPlayer", (player, err) =>
        {
            if (err != null) { Log($"[MP-T4] FAIL – {err}"); return; }
            if (player == null) { Log("[MP-T4] FAIL – null result"); return; }
            _complexTypeOk = !string.IsNullOrEmpty(player.Name);
            Log(_complexTypeOk
                ? $"[MP-T4] PASS – Name={player.Name} Score={player.Score} ({Ms(t0)} ms)"
                : "[MP-T4] FAIL – Name empty");
        }, "TestDevice");
        yield return WaitFor(() => _complexTypeOk, 5f);
        if (!_complexTypeOk) Log("[MP-T4] FAIL – timed out");
        RecordResult("MP-T4 ComplexType", _complexTypeOk, Ms(t0));

        // ── MP-T5: Server Push (On callback) ─────────────────────────────────
        Log("[MP-T5] Server push (On callback via MessagePack) …");
        bool pushOk = false;
        t0 = Time.realtimeSinceStartup;
        _hub.On<string>("ReceiveMessage", msg =>
        {
            pushOk = !string.IsNullOrEmpty(msg);
            Log(pushOk ? $"[MP-T5] PASS – received: '{msg}' ({Ms(t0)} ms)" : "[MP-T5] FAIL – empty push");
        });
        _hub.Send("BroadcastToSelf", "hello-from-msgpack");
        yield return WaitFor(() => pushOk, 5f);
        if (!pushOk) Log("[MP-T5] SKIP – server has no BroadcastToSelf or push not received");
        RecordResult("MP-T5 ServerPush", pushOk, Ms(t0));

        _hub.StartClose();
        yield return new WaitForSeconds(0.5f);

        PrintSummary(suiteStart);
    }

    private bool EnsureConnected() =>
        _hub != null && _hub.State == HubConnectionState.Connected;

    /// <summary>Waits up to <paramref name="timeoutSec"/> seconds for condition to become true.</summary>
    private static IEnumerator WaitFor(Func<bool> condition, float timeoutSec)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSec;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private void Log(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Debug.Log(line);
        _logLines.Add(line);
        if (LogText != null)
        {
            LogText.text = string.Join("\n", _logLines);
            if (LogScroll != null)
                Canvas.ForceUpdateCanvases();
        }
    }

    private void RecordResult(string name, bool pass, int ms) =>
        _results.Add((name, pass, ms));

    private static int Ms(float startTime) =>
        Mathf.RoundToInt((Time.realtimeSinceStartup - startTime) * 1000f);

    private void SetStatus(string msg)
    {
        if (StatusText != null) StatusText.text = msg;
    }

    private void OnUnityLog(string message, string stackTrace, LogType type)
    {
        // Mirror Debug.LogError to on-screen log so it's visible without adb/Xcode
        if (type == LogType.Error || type == LogType.Exception)
            if (LogText != null)
            {
                _logLines.Add($"[ERR] {message}");
                LogText.text = string.Join("\n", _logLines);
            }
    }

    private void ClearLog()
    {
        _logLines.Clear();
        if (LogText != null) LogText.text = "";
    }

    // ── Data types (must match server PascalCase) ─────────────────────────────

    [Serializable]
    public class PlayerData
    {
        public string Name;
        public int    Score;
    }
}
