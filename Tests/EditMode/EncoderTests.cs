using NUnit.Framework;
using SignalRLite.Encoders;
using SignalRLite.Messages;

namespace SignalRLite.Tests
{
    /// <summary>
    /// Edit-Mode tests for JsonProtocol properties and message types
    /// not covered by ProtocolTests.cs.
    /// </summary>
    [TestFixture]
    public class EncoderTests
    {
        private static readonly JsonProtocol P = new JsonProtocol();
        private const char Sep = JsonProtocol.Separator;

        // ── Protocol identity ────────────────────────────────────────────────

        [Test]
        public void JsonProtocol_Name_IsJson()
            => Assert.AreEqual("json", P.Name);

        [Test]
        public void JsonProtocol_IsBinary_IsFalse()
            => Assert.IsFalse(P.IsBinary);

        [Test]
        public void JsonProtocol_EncodeBytes_ReturnsNull()
            => Assert.IsNull(P.EncodeBytes(new SignalRMessage { Type = MessageType.Ping }));

        [Test]
        public void JsonProtocol_ParseBytes_ReturnsEmptyList()
        {
            var msgs = P.ParseBytes(new byte[0], 0, 0);
            Assert.IsNotNull(msgs);
            Assert.AreEqual(0, msgs.Count);
        }

        [Test]
        public void JsonProtocol_HandshakeRequest_ContainsMessagepack_False()
        {
            // JSON protocol handshake must say "json", not "messagepack".
            Assert.IsTrue(P.HandshakeRequest.Contains("\"json\""));
            Assert.IsFalse(P.HandshakeRequest.Contains("messagepack"));
        }

        // ── EncodeText: additional message types ──────────────────────────────

        [Test]
        public void EncodeText_StreamItem_HasType2()
        {
            string msg = P.EncodeText(new SignalRMessage
            {
                Type         = MessageType.StreamItem,
                InvocationId = "10",
                Item         = "chunk1",
            });
            Assert.IsTrue(msg.Contains("\"type\":2"));
            Assert.IsTrue(msg.Contains("\"invocationId\":\"10\""));
            Assert.IsTrue(msg.Contains("\"item\":\"chunk1\""));
            Assert.IsTrue(msg.EndsWith(Sep.ToString()));
        }

        [Test]
        public void EncodeText_CancelInvocation_HasType5()
        {
            string msg = P.EncodeText(new SignalRMessage
            {
                Type         = MessageType.CancelInvocation,
                InvocationId = "99",
            });
            Assert.IsTrue(msg.Contains("\"type\":5"));
            Assert.IsTrue(msg.Contains("\"invocationId\":\"99\""));
            Assert.IsTrue(msg.EndsWith(Sep.ToString()));
        }

        [Test]
        public void EncodeText_Close_NoError_HasType7()
        {
            string msg = P.EncodeText(new SignalRMessage { Type = MessageType.Close });
            Assert.IsTrue(msg.Contains("\"type\":7"));
            Assert.IsFalse(msg.Contains("\"error\""));
            Assert.IsTrue(msg.EndsWith(Sep.ToString()));
        }

        [Test]
        public void EncodeText_Close_WithError()
        {
            string msg = P.EncodeText(new SignalRMessage
            {
                Type  = MessageType.Close,
                Error = "server-shutdown",
            });
            Assert.IsTrue(msg.Contains("\"error\":\"server-shutdown\""));
        }

        [Test]
        public void EncodeText_Close_AllowReconnect_True()
        {
            string msg = P.EncodeText(new SignalRMessage
            {
                Type           = MessageType.Close,
                Error          = "shutdown",
                AllowReconnect = true,
            });
            Assert.IsTrue(msg.Contains("\"allowReconnect\":true"));
        }

        [Test]
        public void EncodeText_UnknownType_ReturnsNull()
        {
            // Ack / Sequence are not text-encoded — EncodeText should return null.
            Assert.IsNull(P.EncodeText(new SignalRMessage { Type = MessageType.Ack }));
            Assert.IsNull(P.EncodeText(new SignalRMessage { Type = MessageType.Sequence }));
        }

        // ── ParseText: Ack and Sequence ───────────────────────────────────────

        [Test]
        public void ParseText_Ack_HasType8()
        {
            string data = $"{{\"type\":8,\"sequenceId\":42}}{Sep}";
            var msgs = P.ParseText(data);
            Assert.AreEqual(1, msgs.Count);
            Assert.AreEqual(MessageType.Ack, msgs[0].Type);
            Assert.AreEqual(42L, msgs[0].SequenceId);
        }

        [Test]
        public void ParseText_Sequence_HasType9()
        {
            string data = $"{{\"type\":9,\"sequenceId\":7}}{Sep}";
            var msgs = P.ParseText(data);
            Assert.AreEqual(1, msgs.Count);
            Assert.AreEqual(MessageType.Sequence, msgs[0].Type);
            Assert.AreEqual(7L, msgs[0].SequenceId);
        }

        // ── ConvertTo (default JSON converter) ────────────────────────────────

        [Test]
        public void ConvertTo_String_FromString()
            => Assert.AreEqual("hello", P.ConvertTo(typeof(string), "hello"));

        [Test]
        public void ConvertTo_Int_FromLong()
            => Assert.AreEqual(5, P.ConvertTo(typeof(int), 5L));

        [Test]
        public void ConvertTo_Double_FromDouble()
            => Assert.AreEqual(3.14, (double)P.ConvertTo(typeof(double), 3.14), 0.001);

        [Test]
        public void ConvertTo_Null_ReturnsNull()
            => Assert.IsNull(P.ConvertTo(typeof(string), null));

        [Test]
        public void ConvertTo_AlreadyCorrectType()
        {
            var obj = P.ConvertTo(typeof(string), "abc");
            Assert.AreEqual("abc", obj);
        }

        // ── JsonProtocol with custom IEncoder ─────────────────────────────────

        [Test]
        public void JsonProtocol_WithEncoder_UsesEncoderConvertTo()
        {
            var encoder  = new StubEncoder("custom-result");
            var protocol = new JsonProtocol(encoder);

            object result = protocol.ConvertTo(typeof(string), "anything");
            Assert.AreEqual("custom-result", result);
        }

        // ── Stub encoder for testing the IEncoder plug-in contract ────────────

        private sealed class StubEncoder : IEncoder
        {
            private readonly string _fixedResult;
            public StubEncoder(string fixedResult) => _fixedResult = fixedResult;
            public object ConvertTo(System.Type toType, object obj) => _fixedResult;
            public T      DecodeAs<T>(byte[] data, int offset, int count) => default;
            public string Encode<T>(T value) => $"\"{value}\"";
        }
    }
}
