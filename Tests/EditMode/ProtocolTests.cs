using System.Collections.Generic;
using NUnit.Framework;
using SignalRLite.Encoders;
using SignalRLite.Messages;

namespace SignalRLite.Tests
{
    /// <summary>
    /// Tests for JsonProtocol encode / decode with the 0x1E record separator.
    /// All tests run in Edit Mode (no Unity runtime needed).
    /// </summary>
    [TestFixture]
    public class ProtocolTests
    {
        private static readonly JsonProtocol Protocol = new JsonProtocol();
        private const char Sep = JsonProtocol.Separator; // 0x1E

        // ── EncodeHandshake ──────────────────────────────────────────────────

        [Test]
        public void EncodeHandshake_EndsWithSeparator()
        {
            string msg = Protocol.HandshakeRequest;
            Assert.IsTrue(msg.EndsWith(Sep.ToString()), "must end with 0x1E");
        }

        [Test]
        public void EncodeHandshake_ContainsJsonProtocol()
        {
            string msg = Protocol.HandshakeRequest;
            Assert.IsTrue(msg.Contains("\"protocol\":\"json\""));
            Assert.IsTrue(msg.Contains("\"version\":1"));
        }

        // ── EncodePing ───────────────────────────────────────────────────────

        [Test]
        public void EncodePing_IsType6()
        {
            string msg = Protocol.EncodeText(new SignalRMessage { Type = MessageType.Ping });
            Assert.IsTrue(msg.Contains("\"type\":6"), "ping must have type=6");
            Assert.IsTrue(msg.EndsWith(Sep.ToString()));
        }

        // ── EncodeSend ───────────────────────────────────────────────────────

        [Test]
        public void EncodeSend_HasCorrectFields()
        {
            string msg = Protocol.EncodeText(new SignalRMessage
            {
                Type = MessageType.Invocation, Target = "BroadcastMessage",
                Arguments = new object[] { "Hello" }
                // No InvocationId → treated as fire-and-forget
            });
            Assert.IsTrue(msg.Contains("\"type\":1"));
            Assert.IsTrue(msg.Contains("\"target\":\"BroadcastMessage\""));
            Assert.IsTrue(msg.Contains("\"nonBlocking\":true"));
            Assert.IsTrue(msg.Contains("\"Hello\""));
            Assert.IsTrue(msg.EndsWith(Sep.ToString()));
        }

        [Test]
        public void EncodeSend_NoArgs()
        {
            string msg = Protocol.EncodeText(new SignalRMessage
                { Type = MessageType.Invocation, Target = "Ping", Arguments = new object[0] });
            Assert.IsTrue(msg.Contains("\"arguments\":[]"));
        }

        [Test]
        public void EncodeSend_MultipleArgs()
        {
            string msg = Protocol.EncodeText(new SignalRMessage
                { Type = MessageType.Invocation, Target = "Chat", Arguments = new object[] { "Alice", "hi" } });
            Assert.IsTrue(msg.Contains("\"Alice\""));
            Assert.IsTrue(msg.Contains("\"hi\""));
        }

        // ── EncodeInvocation ─────────────────────────────────────────────────

        [Test]
        public void EncodeInvocation_HasInvocationId()
        {
            string msg = Protocol.EncodeText(new SignalRMessage
            {
                Type = MessageType.Invocation, InvocationId = "42",
                Target = "GetTime", Arguments = new object[0]
            });
            Assert.IsTrue(msg.Contains("\"invocationId\":\"42\""));
            Assert.IsTrue(msg.Contains("\"target\":\"GetTime\""));
            Assert.IsFalse(msg.Contains("\"nonBlocking\""), "tracked invocation should NOT have nonBlocking");
        }

        // ── EncodeCompletion ─────────────────────────────────────────────────

        [Test]
        public void EncodeCompletion_WithResult()
        {
            string msg = Protocol.EncodeText(new SignalRMessage
                { Type = MessageType.Completion, InvocationId = "1", Result = "success" });
            Assert.IsTrue(msg.Contains("\"type\":3"));
            Assert.IsTrue(msg.Contains("\"invocationId\":\"1\""));
            Assert.IsTrue(msg.Contains("\"result\":\"success\""));
            Assert.IsFalse(msg.Contains("\"error\""));
        }

        [Test]
        public void EncodeCompletion_WithError()
        {
            string msg = Protocol.EncodeText(new SignalRMessage
                { Type = MessageType.Completion, InvocationId = "2", Error = "Something failed" });
            Assert.IsTrue(msg.Contains("\"error\":\"Something failed\""));
            Assert.IsFalse(msg.Contains("\"result\""));
        }

        // ── ParseMessages ────────────────────────────────────────────────────

        [Test]
        public void ParseMessages_EmptyString_ReturnsEmpty()
        {
            var msgs = Protocol.ParseText("");
            Assert.AreEqual(0, msgs.Count);
        }

        [Test]
        public void ParseMessages_HandshakeResponse_EmptyObject()
        {
            string data = $"{{}}{Sep}";
            var msgs = Protocol.ParseText(data);
            Assert.AreEqual(1, msgs.Count);
            Assert.AreEqual(MessageType.Handshake, msgs[0].Type);
        }

        [Test]
        public void ParseMessages_Ping()
        {
            string data = $"{{\"type\":6}}{Sep}";
            var msgs = Protocol.ParseText(data);
            Assert.AreEqual(1, msgs.Count);
            Assert.AreEqual(MessageType.Ping, msgs[0].Type);
        }

        [Test]
        public void ParseMessages_Invocation_OneStringArg()
        {
            string data = $"{{\"type\":1,\"target\":\"ReceiveMessage\",\"arguments\":[\"Hello\"]}}{Sep}";
            var msgs = Protocol.ParseText(data);
            Assert.AreEqual(1, msgs.Count);

            var msg = msgs[0];
            Assert.AreEqual(MessageType.Invocation, msg.Type);
            Assert.AreEqual("ReceiveMessage", msg.Target);
            Assert.IsNotNull(msg.Arguments);
            Assert.AreEqual(1, msg.Arguments.Length);
            Assert.AreEqual("Hello", msg.Arguments[0]);
        }

        [Test]
        public void ParseMessages_Invocation_TwoArgs()
        {
            string data = $"{{\"type\":1,\"target\":\"Chat\",\"arguments\":[\"Alice\",42]}}{Sep}";
            var msgs = Protocol.ParseText(data);
            var msg = msgs[0];
            Assert.AreEqual("Chat", msg.Target);
            Assert.AreEqual(2, msg.Arguments.Length);
            Assert.AreEqual("Alice", msg.Arguments[0]);
            Assert.AreEqual(42L,    msg.Arguments[1]);
        }

        [Test]
        public void ParseMessages_Completion_WithResult()
        {
            string data = $"{{\"type\":3,\"invocationId\":\"7\",\"result\":\"done\"}}{Sep}";
            var msgs = Protocol.ParseText(data);
            var msg = msgs[0];
            Assert.AreEqual(MessageType.Completion, msg.Type);
            Assert.AreEqual("7",    msg.InvocationId);
            Assert.AreEqual("done", msg.Result);
            Assert.IsNull(msg.Error);
        }

        [Test]
        public void ParseMessages_Completion_WithError()
        {
            string data = $"{{\"type\":3,\"invocationId\":\"3\",\"error\":\"oops\"}}{Sep}";
            var msgs = Protocol.ParseText(data);
            var msg = msgs[0];
            Assert.AreEqual(MessageType.Completion, msg.Type);
            Assert.AreEqual("oops", msg.Error);
            Assert.IsNull(msg.Result);
        }

        [Test]
        public void ParseMessages_Close_AllowReconnect()
        {
            string data = $"{{\"type\":7,\"allowReconnect\":true}}{Sep}";
            var msgs = Protocol.ParseText(data);
            var msg = msgs[0];
            Assert.AreEqual(MessageType.Close, msg.Type);
            Assert.IsTrue(msg.AllowReconnect);
        }

        [Test]
        public void ParseMessages_MultipleMessagesInOnePacket()
        {
            string data = $"{{\"type\":6}}{Sep}{{\"type\":1,\"target\":\"M\",\"arguments\":[1]}}{Sep}";
            var msgs = Protocol.ParseText(data);
            Assert.AreEqual(2, msgs.Count);
            Assert.AreEqual(MessageType.Ping,       msgs[0].Type);
            Assert.AreEqual(MessageType.Invocation, msgs[1].Type);
        }

        [Test]
        public void ParseMessages_IgnoresEmptySegments()
        {
            string data = $"{{\"type\":6}}{Sep}{Sep}";
            var msgs = Protocol.ParseText(data);
            Assert.AreEqual(1, msgs.Count);
        }

        // ── Round-trip: encode then parse ────────────────────────────────────

        [Test]
        public void RoundTrip_Send()
        {
            string encoded = Protocol.EncodeText(new SignalRMessage
            {
                Type = MessageType.Invocation, Target = "UpdateScore",
                Arguments = new object[] { "Player1", 100 }
            });
            var msgs = Protocol.ParseText(encoded);

            Assert.AreEqual(1, msgs.Count);
            var msg = msgs[0];
            Assert.AreEqual(MessageType.Invocation, msg.Type);
            Assert.AreEqual("UpdateScore", msg.Target);
            Assert.AreEqual(2, msg.Arguments.Length);
            Assert.AreEqual("Player1", msg.Arguments[0]);
            Assert.AreEqual(100L,      msg.Arguments[1]);
        }

        [Test]
        public void RoundTrip_Invocation()
        {
            string encoded = Protocol.EncodeText(new SignalRMessage
            {
                Type = MessageType.Invocation, InvocationId = "99",
                Target = "GetBalance", Arguments = new object[] { "user1" }
            });
            var msgs = Protocol.ParseText(encoded);

            Assert.AreEqual(1, msgs.Count);
            Assert.AreEqual("99",         msgs[0].InvocationId);
            Assert.AreEqual("GetBalance", msgs[0].Target);
        }

        [Test]
        public void RoundTrip_Completion()
        {
            string encoded = Protocol.EncodeText(new SignalRMessage
                { Type = MessageType.Completion, InvocationId = "5", Result = 42 });
            var msgs = Protocol.ParseText(encoded);

            Assert.AreEqual(1, msgs.Count);
            Assert.AreEqual(MessageType.Completion, msgs[0].Type);
            Assert.AreEqual("5", msgs[0].InvocationId);
            Assert.AreEqual(42L, msgs[0].Result);
        }
    }
}
