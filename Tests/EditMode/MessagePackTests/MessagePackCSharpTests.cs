#if SIGNALRLITE_MESSAGEPACK_CSHARP

using System;
using System.Collections.Generic;
using NUnit.Framework;
using SignalRLite.Encoders;
using SignalRLite.Messages;

namespace SignalRLite.Tests
{
    /// <summary>
    /// Edit-Mode tests for <see cref="MessagePackCSharpProtocol"/>.
    /// Requires: com.neuecc.messagepack UPM package + scripting define SIGNALRLITE_MESSAGEPACK_CSHARP.
    /// </summary>
    [TestFixture]
    public class MessagePackCSharpTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        /// Creates a protocol with type resolvers wired up.
        private static MessagePackCSharpProtocol MakeProtocol(
            Func<string, Type[]>  argTypes   = null,
            Func<string, Type>    returnType = null)
        {
            var p = new MessagePackCSharpProtocol();
            p.GetArgTypes   = argTypes   ?? (_ => null);
            p.GetReturnType = returnType ?? (_ => null);
            return p;
        }

        /// Encode then parse in one call — returns the first parsed message.
        private static SignalRMessage RoundTrip(MessagePackCSharpProtocol p, SignalRMessage msg)
        {
            byte[] encoded = p.EncodeBytes(msg);
            Assert.IsNotNull(encoded, "EncodeBytes returned null");
            var list = p.ParseBytes(encoded, 0, encoded.Length);
            Assert.AreEqual(1, list.Count, "ParseBytes should yield exactly 1 message");
            return list[0];
        }

        // ── Protocol identity ─────────────────────────────────────────────────

        [Test]
        public void Name_IsMessagepack()
            => Assert.AreEqual("messagepack", MakeProtocol().Name);

        [Test]
        public void IsBinary_IsTrue()
            => Assert.IsTrue(MakeProtocol().IsBinary);

        [Test]
        public void HandshakeRequest_ContainsMessagepack()
            => Assert.IsTrue(MakeProtocol().HandshakeRequest.Contains("messagepack"));

        [Test]
        public void EncodeText_ReturnsNull()
            => Assert.IsNull(MakeProtocol().EncodeText(new SignalRMessage { Type = MessageType.Ping }));

        [Test]
        public void ParseText_ReturnsEmptyList()
        {
            var list = MakeProtocol().ParseText("{\"type\":6}");
            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);
        }

        // ── VarInt framing ────────────────────────────────────────────────────

        [Test]
        public void VarInt_SmallValue_RoundTrip()
        {
            byte[] buf    = new byte[10];
            int    offset = 0;
            MessagePackCSharpProtocol.WriteLengthAsVarInt(buf, offset, 42);
            uint decoded = MessagePackCSharpProtocol.ReadVarInt(buf, ref offset);
            Assert.AreEqual(42u, decoded);
        }

        [Test]
        public void VarInt_LargeValue_RoundTrip()
        {
            byte[] buf    = new byte[10];
            int    offset = 0;
            MessagePackCSharpProtocol.WriteLengthAsVarInt(buf, offset, 16384);
            uint decoded = MessagePackCSharpProtocol.ReadVarInt(buf, ref offset);
            Assert.AreEqual(16384u, decoded);
        }

        // ── Ping round-trip ───────────────────────────────────────────────────

        [Test]
        public void Ping_RoundTrip()
        {
            var p   = MakeProtocol();
            var msg = RoundTrip(p, new SignalRMessage { Type = MessageType.Ping });
            Assert.AreEqual(MessageType.Ping, msg.Type);
        }

        // ── Invocation round-trip ─────────────────────────────────────────────

        [Test]
        public void Invocation_NoArgs_RoundTrip()
        {
            var p = MakeProtocol(argTypes: _ => Type.EmptyTypes);
            var msg = RoundTrip(p, new SignalRMessage
            {
                Type         = MessageType.Invocation,
                Target       = "hello",
                Arguments    = Array.Empty<object>(),
                InvocationId = "1",
            });

            Assert.AreEqual(MessageType.Invocation, msg.Type);
            Assert.AreEqual("hello", msg.Target);
            Assert.AreEqual("1", msg.InvocationId);
        }

        [Test]
        public void Invocation_StringArg_RoundTrip()
        {
            var p = MakeProtocol(argTypes: _ => new[] { typeof(string) });
            var msg = RoundTrip(p, new SignalRMessage
            {
                Type      = MessageType.Invocation,
                Target    = "echo",
                Arguments = new object[] { "hello world" },
            });

            Assert.AreEqual(MessageType.Invocation, msg.Type);
            Assert.AreEqual("echo", msg.Target);
            Assert.IsNotNull(msg.Arguments);
            Assert.AreEqual("hello world", msg.Arguments[0]);
        }

        [Test]
        public void Invocation_MultipleArgs_RoundTrip()
        {
            var p = MakeProtocol(argTypes: _ => new[] { typeof(string), typeof(int), typeof(bool) });
            var msg = RoundTrip(p, new SignalRMessage
            {
                Type      = MessageType.Invocation,
                Target    = "multi",
                Arguments = new object[] { "abc", 99, true },
            });

            Assert.AreEqual("abc", msg.Arguments[0]);
            Assert.AreEqual(99,    Convert.ToInt32(msg.Arguments[1]));
            Assert.AreEqual(true,  msg.Arguments[2]);
        }

        // ── Completion round-trip ─────────────────────────────────────────────

        [Test]
        public void Completion_WithError_RoundTrip()
        {
            var p = MakeProtocol();
            var msg = RoundTrip(p, new SignalRMessage
            {
                Type         = MessageType.Completion,
                InvocationId = "5",
                Error        = "something went wrong",
            });

            Assert.AreEqual(MessageType.Completion, msg.Type);
            Assert.AreEqual("5", msg.InvocationId);
            Assert.AreEqual("something went wrong", msg.Error);
        }

        [Test]
        public void Completion_WithResult_RoundTrip()
        {
            var p = MakeProtocol(returnType: _ => typeof(string));
            var msg = RoundTrip(p, new SignalRMessage
            {
                Type         = MessageType.Completion,
                InvocationId = "7",
                Result       = "done",
            });

            Assert.AreEqual(MessageType.Completion, msg.Type);
            Assert.AreEqual("done", msg.Result);
        }

        [Test]
        public void Completion_VoidResult_RoundTrip()
        {
            var p = MakeProtocol();
            var msg = RoundTrip(p, new SignalRMessage
            {
                Type         = MessageType.Completion,
                InvocationId = "3",
            });

            Assert.AreEqual(MessageType.Completion, msg.Type);
            Assert.IsNull(msg.Error);
            Assert.IsNull(msg.Result);
        }

        // ── Close round-trip ──────────────────────────────────────────────────

        [Test]
        public void Close_WithError_AllowReconnect_RoundTrip()
        {
            var p = MakeProtocol();
            var msg = RoundTrip(p, new SignalRMessage
            {
                Type           = MessageType.Close,
                Error          = "server shutdown",
                AllowReconnect = true,
            });

            Assert.AreEqual(MessageType.Close, msg.Type);
            Assert.AreEqual("server shutdown", msg.Error);
            Assert.IsTrue(msg.AllowReconnect);
        }

        // ── ParseBytes: loop boundary fix (Bug #1) ───────────────────────────

        [Test]
        public void ParseBytes_LengthBounds_OnlyFirstMessage()
        {
            var p     = MakeProtocol();
            byte[] p1 = p.EncodeBytes(new SignalRMessage { Type = MessageType.Ping });
            byte[] p2 = p.EncodeBytes(new SignalRMessage { Type = MessageType.Ping });

            byte[] combined = new byte[p1.Length + p2.Length];
            Buffer.BlockCopy(p1, 0, combined, 0,         p1.Length);
            Buffer.BlockCopy(p2, 0, combined, p1.Length, p2.Length);

            // length = p1.Length must exclude the second message
            var msgs = p.ParseBytes(combined, 0, p1.Length);
            Assert.AreEqual(1, msgs.Count, "length parameter must bound the parse window");
        }

        [Test]
        public void ParseBytes_OffsetAndLength_SecondMessageOnly()
        {
            var p     = MakeProtocol();
            byte[] p1 = p.EncodeBytes(new SignalRMessage { Type = MessageType.Ping });
            byte[] p2 = p.EncodeBytes(new SignalRMessage { Type = MessageType.Ping });

            byte[] combined = new byte[p1.Length + p2.Length];
            Buffer.BlockCopy(p1, 0, combined, 0,         p1.Length);
            Buffer.BlockCopy(p2, 0, combined, p1.Length, p2.Length);

            var msgs = p.ParseBytes(combined, p1.Length, p2.Length);
            Assert.AreEqual(1, msgs.Count);
            Assert.AreEqual(MessageType.Ping, msgs[0].Type);
        }

        [Test]
        public void ParseBytes_MultipleMessages_AllParsed()
        {
            var p = MakeProtocol();
            var encoded = new List<byte>();
            encoded.AddRange(p.EncodeBytes(new SignalRMessage { Type = MessageType.Ping }));
            encoded.AddRange(p.EncodeBytes(new SignalRMessage { Type = MessageType.Ping }));
            encoded.AddRange(p.EncodeBytes(new SignalRMessage { Type = MessageType.Ping }));

            byte[] buf  = encoded.ToArray();
            var    msgs = p.ParseBytes(buf, 0, buf.Length);
            Assert.AreEqual(3, msgs.Count);
        }

        // ── ParseBytes: extra args skip fix (Bug #2) ─────────────────────────

        [Test]
        public void ParseBytes_ExtraArgs_SkippedWithoutCursorMisalignment()
        {
            // Encode with 2 args
            var encoder = MakeProtocol(argTypes: _ => new[] { typeof(string), typeof(int) });
            byte[] encoded = encoder.EncodeBytes(new SignalRMessage
            {
                Type      = MessageType.Invocation,
                Target    = "greet",
                Arguments = new object[] { "hello", 42 },
            });

            // Parse knowing only 1 arg type — the extra int must be skipped.
            // Without the fix, ReadStreamIds reads wrong bytes and throws or returns garbage.
            var parser = MakeProtocol(argTypes: _ => new[] { typeof(string) });
            List<SignalRMessage> msgs = null;
            Assert.DoesNotThrow(() => msgs = parser.ParseBytes(encoded, 0, encoded.Length),
                "Extra args must be skipped without throwing");

            Assert.AreEqual(1,                   msgs.Count);
            Assert.AreEqual(MessageType.Invocation, msgs[0].Type);
            Assert.AreEqual("greet",             msgs[0].Target);
            Assert.IsNull(msgs[0].StreamIds,     "StreamIds must be null, not garbled from unread extra args");
        }
    }
}

#endif
