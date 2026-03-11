using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SignalRLite;
using SignalRLite.Messages;
using SignalRLite.Transport;
using SignalRLite.Utility;
using UnityEngine;
using UnityEngine.TestTools;

namespace SignalRLite.Tests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // ConvertTo<T> tests  (no network needed)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestFixture]
    public class ConvertToTests
    {
        [Test] public void ConvertTo_String_FromString()
            => Assert.AreEqual("hi", HubConnection.ConvertTo<string>("hi"));

        [Test] public void ConvertTo_String_FromInt()
            => Assert.AreEqual("42", HubConnection.ConvertTo<string>(42L));

        [Test] public void ConvertTo_Int_FromLong()
            => Assert.AreEqual(7, HubConnection.ConvertTo<int>(7L));

        [Test] public void ConvertTo_Float_FromDouble()
            => Assert.AreEqual(3.14f, HubConnection.ConvertTo<float>(3.14), 0.001f);

        [Test] public void ConvertTo_Bool_FromBool()
            => Assert.IsTrue(HubConnection.ConvertTo<bool>(true));

        [Test] public void ConvertTo_Null_ReturnsDefault()
        {
            Assert.AreEqual(0,     HubConnection.ConvertTo<int>(null));
            Assert.IsNull(         HubConnection.ConvertTo<string>(null));
            Assert.AreEqual(false, HubConnection.ConvertTo<bool>(null));
        }

        [Test] public void ConvertTo_AlreadyCorrectType()
            => Assert.AreEqual("abc", HubConnection.ConvertTo<string>("abc"));

        [Test] public void ConvertTo_Long_FromDouble()
            => Assert.AreEqual(5L, HubConnection.ConvertTo<long>(5.0));

        [Test]
        public void ConvertTo_SerializableClass()
        {
            // Simulate server sending {"Name":"Bob","Score":99} as a Dictionary
            var dict = new Dictionary<string, object>
            {
                { "Name", "Bob" },
                { "Score", 99L },
            };

            var result = HubConnection.ConvertTo<PlayerData>(dict);
            Assert.IsNotNull(result);
            Assert.AreEqual("Bob", result.Name);
            Assert.AreEqual(99,    result.Score);
        }

        [Serializable]
        public class PlayerData
        {
            public string Name;
            public int    Score;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // State machine tests (no network needed)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestFixture]
    public class HubConnectionStateTests
    {
        // ── Helper ───────────────────────────────────────────────────────────

        /// Creates a hub backed by MockWebSocketClient (no real network needed).
        private static HubConnection CreateMockHub(bool skipNegotiation = true)
            => new HubConnection("ws://localhost/hub", new HubOptions
            {
                SkipNegotiation    = skipNegotiation,
                WebSocketFactory   = _ => new MockWebSocketClient(),
            });

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void InitialState_IsDisconnected()
        {
            var hub = new HubConnection("ws://localhost:5000/hub");
            Assert.AreEqual(HubConnectionState.Disconnected, hub.State);
        }

        [Test]
        public void StartConnect_ChangesStateToConnecting()
        {
            var hub = CreateMockHub();
            hub.StartConnect();
            Assert.AreEqual(HubConnectionState.Connecting, hub.State);
            hub.StartClose();
        }

        [Test]
        public void StartClose_ResetsStateToDisconnected()
        {
            var hub = CreateMockHub();
            hub.StartConnect();
            hub.StartClose();
            Assert.AreEqual(HubConnectionState.Disconnected, hub.State);
        }

        [Test]
        public void CallingStartConnect_Twice_IsIdempotent()
        {
            var hub = CreateMockHub();
            hub.StartConnect();
            hub.StartConnect(); // second call should be ignored
            Assert.AreEqual(HubConnectionState.Connecting, hub.State);
            hub.StartClose();
        }

        [Test]
        public void SimulateConnected_HubBecomesConnected()
        {
            MockWebSocketClient mock = null;
            var hub = new HubConnection("ws://localhost/hub", new HubOptions
            {
                SkipNegotiation  = true,
                WebSocketFactory = url => { mock = new MockWebSocketClient(); return mock; },
            });

            hub.StartConnect();
            mock.SimulateConnected(); // SimulateOpen + SimulateHandshakeOk
            Assert.AreEqual(HubConnectionState.Connected, hub.State);
            hub.StartClose();
        }

        [Test]
        public void SimulateClose_HubBecomesDisconnected()
        {
            MockWebSocketClient mock = null;
            var hub = new HubConnection("ws://localhost/hub", new HubOptions
            {
                SkipNegotiation  = true,
                WebSocketFactory = url => { mock = new MockWebSocketClient(); return mock; },
            });

            hub.StartConnect();
            mock.SimulateConnected();
            Assert.AreEqual(HubConnectionState.Connected, hub.State);

            mock.SimulateClose("test closed");
            Assert.AreEqual(HubConnectionState.Reconnecting, hub.State); // auto-reconnect kicks in
            hub.StartClose();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // On / Off subscription registration tests
    // ═══════════════════════════════════════════════════════════════════════════

    [TestFixture]
    public class SubscriptionTests
    {
        // We test subscription by verifying callbacks are invoked when we manually
        // push a parsed message into the hub via the public message-processing path.
        // We use reflection to call the internal HandleMessages method.

        private HubConnection CreateConnectedHub(out System.Reflection.MethodInfo handleMessages)
        {
            var hub = new HubConnection("ws://localhost/hub");
            handleMessages = typeof(HubConnection)
                .GetMethod("HandleMessages",
                           System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return hub;
        }

        private void InjectMessages(HubConnection hub, System.Reflection.MethodInfo method, List<SignalRMessage> msgs)
        {
            method.Invoke(hub, new object[] { msgs });
        }

        [Test]
        public void On_NoArgs_IsInvoked()
        {
            var hub = CreateConnectedHub(out var handleMessages);
            bool called = false;
            hub.On("Tick", () => called = true);

            InjectMessages(hub, handleMessages, new List<SignalRMessage>
            {
                new SignalRMessage { Type = MessageType.Invocation, Target = "Tick", Arguments = Array.Empty<object>() }
            });

            Assert.IsTrue(called);
        }

        [Test]
        public void On_OneArg_ReceivesValue()
        {
            var hub = CreateConnectedHub(out var handleMessages);
            string received = null;
            hub.On<string>("ReceiveMessage", msg => received = msg);

            InjectMessages(hub, handleMessages, new List<SignalRMessage>
            {
                new SignalRMessage
                {
                    Type      = MessageType.Invocation,
                    Target    = "ReceiveMessage",
                    Arguments = new object[] { "Hello!" }
                }
            });

            Assert.AreEqual("Hello!", received);
        }

        [Test]
        public void On_TwoArgs_ReceivesBothValues()
        {
            var hub = CreateConnectedHub(out var handleMessages);
            string user = null; int score = -1;
            hub.On<string, int>("ScoreUpdate", (u, s) => { user = u; score = s; });

            InjectMessages(hub, handleMessages, new List<SignalRMessage>
            {
                new SignalRMessage
                {
                    Type      = MessageType.Invocation,
                    Target    = "ScoreUpdate",
                    Arguments = new object[] { "Alice", 100L }
                }
            });

            Assert.AreEqual("Alice", user);
            Assert.AreEqual(100,     score);
        }

        [Test]
        public void Off_RemovesSubscription()
        {
            var hub = CreateConnectedHub(out var handleMessages);
            int count = 0;
            hub.On("Event", () => count++);
            hub.Off("Event");

            InjectMessages(hub, handleMessages, new List<SignalRMessage>
            {
                new SignalRMessage { Type = MessageType.Invocation, Target = "Event", Arguments = Array.Empty<object>() }
            });

            Assert.AreEqual(0, count);
        }

        [Test]
        public void MultipleHandlers_ForSameMethod_AllCalled()
        {
            var hub = CreateConnectedHub(out var handleMessages);
            int count = 0;
            hub.On("Tick", () => count++);
            hub.On("Tick", () => count++);

            InjectMessages(hub, handleMessages, new List<SignalRMessage>
            {
                new SignalRMessage { Type = MessageType.Invocation, Target = "Tick", Arguments = Array.Empty<object>() }
            });

            Assert.AreEqual(2, count);
        }

        [Test]
        public void Subscription_IsCaseInsensitive()
        {
            var hub = CreateConnectedHub(out var handleMessages);
            bool called = false;
            hub.On("recEivEmEssAge", () => called = true);   // register with mixed case

            InjectMessages(hub, handleMessages, new List<SignalRMessage>
            {
                new SignalRMessage
                {
                    Type      = MessageType.Invocation,
                    Target    = "ReceiveMessage",    // server sends PascalCase
                    Arguments = Array.Empty<object>()
                }
            });

            Assert.IsTrue(called);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Invoke completion dispatch tests
    // Use hub.Invoke() to register, then inject a Completion via HandleMessages.
    // EnsureConnected() only warns – _transport?.Send is null-safe, so the
    // invocation entry IS created even without an active connection.
    // ═══════════════════════════════════════════════════════════════════════════

    [TestFixture]
    public class InvocationDispatchTests
    {
        private static System.Reflection.MethodInfo GetHandleMessages(HubConnection hub)
            => hub.GetType().GetMethod("HandleMessages",
                   System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static void InjectMsg(HubConnection hub, System.Reflection.MethodInfo method,
                                      params SignalRMessage[] msgs)
            => method.Invoke(hub, new object[] { new List<SignalRMessage>(msgs) });

        [Test]
        public void Completion_DispatchedToCallback()
        {
            var hub = new HubConnection("ws://localhost/hub");
            var injectMethod = GetHandleMessages(hub);

            object receivedResult = null;
            // hub.Invoke registers the callback internally (invocationId = "1")
            hub.Invoke("GetTime", (result, _) => receivedResult = result);

            InjectMsg(hub, injectMethod, new SignalRMessage
            {
                Type         = MessageType.Completion,
                InvocationId = "1",
                Result       = "2024-01-01",
            });

            Assert.AreEqual("2024-01-01", receivedResult);
        }

        [Test]
        public void Completion_WithError_DispatchedToCallback()
        {
            var hub = new HubConnection("ws://localhost/hub");
            var injectMethod = GetHandleMessages(hub);

            string receivedError = null;
            hub.Invoke("Fail", (_, err) => receivedError = err);

            InjectMsg(hub, injectMethod, new SignalRMessage
            {
                Type         = MessageType.Completion,
                InvocationId = "1",
                Error        = "Server error",
            });

            Assert.AreEqual("Server error", receivedError);
        }

        [Test]
        public void Completion_RemovedAfterDispatch()
        {
            var hub = new HubConnection("ws://localhost/hub");
            var injectMethod = GetHandleMessages(hub);

            int callCount = 0;
            hub.Invoke("GetData", (r, e) => callCount++);

            // First completion fires callback
            InjectMsg(hub, injectMethod, new SignalRMessage
            {
                Type = MessageType.Completion, InvocationId = "1", Result = "x"
            });
            // Second completion for same ID should NOT fire again
            InjectMsg(hub, injectMethod, new SignalRMessage
            {
                Type = MessageType.Completion, InvocationId = "1", Result = "x"
            });

            Assert.AreEqual(1, callCount, "Completion callback should fire exactly once");
        }

        [Test]
        public void Typed_Invoke_ConvertsResult()
        {
            var hub = new HubConnection("ws://localhost/hub");
            var injectMethod = GetHandleMessages(hub);

            int receivedScore = -1;
            hub.Invoke<int>("GetScore", (score, _) => receivedScore = score);

            // Server returns score as long (JSON numbers are parsed as long)
            InjectMsg(hub, injectMethod, new SignalRMessage
            {
                Type = MessageType.Completion, InvocationId = "1", Result = 42L
            });

            Assert.AreEqual(42, receivedScore);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SignalRLiteRunner singleton tests
    // ═══════════════════════════════════════════════════════════════════════════

    [TestFixture]
    public class RunnerTests
    {
        [UnityTest]
        public IEnumerator Runner_IsSingleton()
        {
            var a = SignalRLiteRunner.Instance;
            var b = SignalRLiteRunner.Instance;
            Assert.AreSame(a, b);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Runner_UpdateCallback_IsCalledEachFrame()
        {
            int callCount = 0;
            Action cb = () => callCount++;
            SignalRLiteRunner.Instance.RegisterUpdate(cb);

            yield return null; // wait 1 frame
            yield return null; // wait 2 frames

            SignalRLiteRunner.Instance.UnregisterUpdate(cb);
            Assert.GreaterOrEqual(callCount, 2, "Callback should have been called at least twice");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Integration tests – require the local SignalRTestServer to be running.
    //
    //   Start server:  dotnet run --project C:\Projects\mlgame\SignalRTestServer
    //   Hub URL:        http://localhost:5000/testhub
    //
    // Set ServerRunning = true before running these tests in the Unity Test Runner.
    // ═══════════════════════════════════════════════════════════════════════════

    [TestFixture]
    [Category("Integration")]
    public class IntegrationTests
    {
        // ── Toggle this flag when the local server is running ─────────────────
        // Start server: dotnet run --project C:\Projects\mlgame\SignalRTestServer
        private const bool   ServerRunning = true;
        private const string HubUrl        = "http://localhost:5000/testhub";

        private HubConnection _hub;
        private bool          _connected;
        private string        _lastError;

        // Wait until connected (or error / timeout)
        private IEnumerator WaitForConnect(float timeoutSec = 8f)
        {
            float t = timeoutSec;
            while (!_connected && _lastError == null && t > 0f)
            {
                t -= Time.deltaTime;
                yield return null;
            }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!ServerRunning || SignalRLiteConfig.DefaultWebSocketFactory == null) yield break;

            _connected = false;
            _lastError = null;

            _hub = new HubConnection(HubUrl, new HubOptions
            {
                SkipNegotiation  = false,
                WebSocketFactory = SignalRLiteConfig.DefaultWebSocketFactory,
            });
            _hub.OnConnected += _ => _connected = true;
            _hub.OnError     += (_, e) => _lastError = e;
            _hub.StartConnect();

            yield return WaitForConnect();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _hub?.StartClose();
            yield return new WaitForSeconds(0.3f);
        }

        // ── Helper: skip if server not running ────────────────────────────────

        private bool Skip()
        {
            if (!ServerRunning)
            {
                Assert.Pass("Skipped – set ServerRunning=true to run integration tests");
                return true;
            }
            if (SignalRLiteConfig.DefaultWebSocketFactory == null)
            {
                Assert.Pass("Skipped – add scripting define SIGNALRLITE_UNITYWSSOCKET to run integration tests");
                return true;
            }
            if (_lastError != null) { Assert.Fail($"Connection error: {_lastError}"); return true; }
            if (!_connected)        { Assert.Fail("Not connected after timeout");      return true; }
            return false;
        }

        // ── Test 1: Basic connect / disconnect ────────────────────────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test01_Connect_And_Disconnect()
        {
            if (Skip()) yield break;

            Assert.AreEqual(HubConnectionState.Connected, _hub.State);
            Assert.IsNull(_lastError);

            bool disconnected = false;
            _hub.OnDisconnected += (_, _) => disconnected = true;
            _hub.StartClose();

            yield return new WaitForSeconds(1f);
            Assert.IsTrue(disconnected, "Disconnected event should fire");
        }

        // ── Test 2: Send broadcast → ReceiveMessage ───────────────────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test02_SendMessage_Broadcast()
        {
            if (Skip()) yield break;

            string received = null;
            _hub.On<string>("ReceiveMessage", msg => received = msg);

            _hub.Send("SendMessage", "hello-world");

            yield return WaitFor(() => received != null, 5f);
            Assert.AreEqual("hello-world", received);
        }

        // ── Test 3: Chat (2 args) ────────────────────────────────────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test03_Chat_TwoArgs()
        {
            if (Skip()) yield break;

            string gotUser = null, gotText = null;
            _hub.On<string, string>("ReceiveFromUser", (u, t) => { gotUser = u; gotText = t; });

            _hub.Send("Chat", "Alice", "hi there");

            yield return WaitFor(() => gotUser != null, 5f);
            Assert.AreEqual("Alice",    gotUser);
            Assert.AreEqual("hi there", gotText);
        }

        // ── Test 4: Echo (Invoke with result) ────────────────────────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test04_Echo_Invocation()
        {
            if (Skip()) yield break;

            string echoResult = null;
            string echoError  = null;
            _hub.Invoke<string>("Echo", (r, e) => { echoResult = r; echoError = e; }, "ping-42");

            yield return WaitFor(() => echoResult != null || echoError != null, 5f);
            Assert.IsNull(echoError, $"Echo should not error: {echoError}");
            Assert.AreEqual("ping-42", echoResult);
        }

        // ── Test 5: GetTime (Invoke returning string) ─────────────────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test05_GetTime_ReturnsString()
        {
            if (Skip()) yield break;

            string timeStr = null;
            string err     = null;
            _hub.Invoke<string>("GetTime", (r, e) => { timeStr = r; err = e; });

            yield return WaitFor(() => timeStr != null || err != null, 5f);
            Assert.IsNull(err);
            Assert.IsNotNull(timeStr, "GetTime should return a non-null string");
            Assert.IsTrue(timeStr.Contains("T"), $"Expected ISO-8601 format, got: {timeStr}");
        }

        // ── Test 6: GetPlayer (Invoke returning complex type) ─────────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test06_GetPlayer_ComplexType()
        {
            if (Skip()) yield break;

            PlayerData result = null;
            string     err    = null;
            _hub.Invoke<PlayerData>("GetPlayer", (r, e) => { result = r; err = e; }, "Bob");

            yield return WaitFor(() => result != null || err != null, 5f);
            Assert.IsNull(err, $"GetPlayer should not error: {err}");
            Assert.IsNotNull(result);
            Assert.AreEqual("Bob", result.Name);
            Assert.AreEqual(99,    result.Score);
        }

        // ── Test 7: DirectEcho (server sends only to caller) ──────────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test07_DirectEcho_OnlyToCaller()
        {
            if (Skip()) yield break;

            string received = null;
            _hub.On<string>("ReceiveMessage", msg => received = msg);

            _hub.Send("DirectEcho", "secret-msg");

            yield return WaitFor(() => received != null, 5f);
            Assert.AreEqual("secret-msg", received);
        }

        // ── Test 8: Fail (server throws → Completion with error) ─────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test08_Fail_ReturnsError()
        {
            if (Skip()) yield break;

            object result    = null;
            string errorMsg  = null;
            _hub.Invoke("Fail", (r, e) => { result = r; errorMsg = e; }, "test-reason");

            yield return WaitFor(() => result != null || errorMsg != null, 5f);
            Assert.IsNotNull(errorMsg, "Fail should produce an error Completion");
            Assert.IsTrue(errorMsg.Contains("Intentional failure"),
                          $"Expected 'Intentional failure' in: {errorMsg}");
        }

        // ── Test 9: ScoreUpdate (2 typed args) ───────────────────────────────

        [UnityTest]
        [Timeout(12000)]
        public IEnumerator Test09_ScoreUpdate_TwoTypedArgs()
        {
            if (Skip()) yield break;

            string gotUser  = null;
            int    gotScore = -1;
            _hub.On<string, int>("ScoreUpdate", (u, s) => { gotUser = u; gotScore = s; });

            _hub.Send("ScoreUpdate", "Player1", 500);

            yield return WaitFor(() => gotUser != null, 5f);
            Assert.AreEqual("Player1", gotUser);
            Assert.AreEqual(500, gotScore);
        }

        // ── Test 10: Auto-reconnect after server timeout ──────────────────────

        [UnityTest]
        [Timeout(20000)]
        public IEnumerator Test10_Reconnect_OnDisconnect()
        {
            if (Skip()) yield break;

            bool reconnecting = false;
            bool reconnected  = false;
            _hub.OnReconnecting += _ => reconnecting = true;
            _hub.OnReconnected  += _ => reconnected  = true;

            // Force disconnect by closing the transport internally via reflection
            var transport = typeof(HubConnection)
                .GetField("_transport",
                          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(_hub);

            if (transport == null)
            {
                Assert.Pass("Transport is null – skipping reconnect test");
                yield break;
            }

            var closeMethod = transport.GetType().GetMethod("Close");
            closeMethod?.Invoke(transport, null);

            // Wait up to 15 s for reconnect cycle
            yield return WaitFor(() => reconnected, 15f);
            Assert.IsTrue(reconnecting, "Should have entered Reconnecting state");
            Assert.IsTrue(reconnected,  "Should have reconnected");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// Coroutine: spins until condition() is true or timeoutSec expires.
        private static IEnumerator WaitFor(Func<bool> condition, float timeoutSec)
        {
            float t = timeoutSec;
            while (!condition() && t > 0f)
            {
                t -= Time.deltaTime;
                yield return null;
            }
        }

        [Serializable]
        public class PlayerData
        {
            public string Name;
            public int    Score;
        }
    }
}
