#if SIGNALRLITE_MESSAGEPACK_CSHARP
// SignalR MessagePack protocol implementation using MessagePack-CSharp (neuecc/MessagePack-CSharp).
// Requires: com.neuecc.messagepack UPM package + scripting define SIGNALRLITE_MESSAGEPACK_CSHARP.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using MessagePack;
using SignalRLite.Messages;

namespace SignalRLite.Encoders
{
    // ── IBufferWriter<byte> backed by MemoryStream ────────────────────────────
    // (Same role as BufferPoolBufferWriter in the original, without BufferPool.)

    sealed class MemoryStreamBufferWriter : IBufferWriter<byte>
    {
        private readonly MemoryStream _stream;
        private byte[]                _rentedBuf;

        public MemoryStreamBufferWriter(MemoryStream stream) => _stream = stream;

        public void Advance(int count)
        {
            _stream.Write(_rentedBuf, 0, count);
            ArrayPool<byte>.Shared.Return(_rentedBuf);
            _rentedBuf = null;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            _rentedBuf = ArrayPool<byte>.Shared.Rent(Math.Max(sizeHint, 256));
            return new Memory<byte>(_rentedBuf, 0, _rentedBuf.Length);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            _rentedBuf = ArrayPool<byte>.Shared.Rent(Math.Max(sizeHint, 256));
            return new Span<byte>(_rentedBuf, 0, _rentedBuf.Length);
        }
    }

    // ── Protocol ─────────────────────────────────────────────────────────────

    /// <summary>
    /// SignalR MessagePack binary protocol backed by MessagePack-CSharp (neuecc/MessagePack-CSharp).
    /// </summary>
    public sealed class MessagePackCSharpProtocol : ISignalRProtocol
    {
        // ── ISignalRProtocol ─────────────────────────────────────────────────

        public string Name     => "messagepack";
        public bool   IsBinary => true;

        // The handshake is always a JSON text frame, even for MessagePack sessions.
        public string HandshakeRequest
            => "{\"protocol\":\"messagepack\",\"version\":1}\x1e";

        private Func<string, Type[]> _getArgTypes;
        private Func<string, Type>   _getReturnType;

        public Func<string, Type[]> GetArgTypes   { set => _getArgTypes   = value; }
        public Func<string, Type>   GetReturnType { set => _getReturnType = value; }

        public string EncodeText(SignalRMessage msg) => null;

        public List<SignalRMessage> ParseText(string text) => new List<SignalRMessage>();

        // ── EncodeBytes ──────────────────────────────────────────────────────

        public byte[] EncodeBytes(SignalRMessage msg)
        {
            switch (msg.Type)
            {
                case MessageType.StreamItem:
                    // [2, Headers, InvocationId, Item]
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter bw) =>
                    {
                        w.WriteArrayHeader(4);
                        w.Write(2);
                        WriteHeaders(ref w);
                        WriteString(ref w, msg.InvocationId);
                        WriteValue(ref w, bw, msg.Item);
                    });

                case MessageType.Completion:
                    // [3, Headers, InvocationId, ResultKind, Result?]
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter bw) =>
                    {
                        byte resultKind = (byte)(!string.IsNullOrEmpty(msg.Error) ? 1
                                                : msg.Result != null               ? 3 : 2);
                        w.WriteArrayHeader(resultKind == 2 ? 4 : 5);
                        w.Write(3);
                        WriteHeaders(ref w);
                        WriteString(ref w, msg.InvocationId);
                        w.Write(resultKind);
                        if (resultKind == 1)      WriteString(ref w, msg.Error);
                        else if (resultKind == 3) WriteValue(ref w, bw, msg.Result);
                    });

                case MessageType.Invocation:
                case MessageType.StreamInvocation:
                    // [1|4, Headers, InvocationId, Target, [Arguments], [StreamIds]]
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter bw) =>
                    {
                        w.WriteArrayHeader(msg.StreamIds != null ? 6 : 5);
                        w.Write((int)msg.Type);
                        WriteHeaders(ref w);
                        WriteString(ref w, msg.InvocationId);
                        WriteString(ref w, msg.Target);
                        w.WriteArrayHeader(msg.Arguments?.Length ?? 0);
                        if (msg.Arguments != null)
                            foreach (var a in msg.Arguments) WriteValue(ref w, bw, a);
                        if (msg.StreamIds != null)
                        {
                            w.WriteArrayHeader(msg.StreamIds.Length);
                            foreach (var s in msg.StreamIds) WriteValue(ref w, bw, s);
                        }
                    });

                case MessageType.CancelInvocation:
                    // [5, Headers, InvocationId]
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    {
                        w.WriteArrayHeader(3);
                        w.Write(5);
                        WriteHeaders(ref w);
                        WriteString(ref w, msg.InvocationId);
                    });

                case MessageType.Ping:
                    // [6]
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    {
                        w.WriteArrayHeader(1);
                        w.Write(6);
                    });

                case MessageType.Close:
                    // [7, Error, AllowReconnect?]
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    {
                        w.WriteArrayHeader(string.IsNullOrEmpty(msg.Error) ? 1 : 2);
                        w.Write(7);
                        if (!string.IsNullOrEmpty(msg.Error)) WriteString(ref w, msg.Error);
                    });

                case MessageType.Ack:
                    // [8, SequenceId]
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    {
                        w.WriteArrayHeader(2);
                        w.Write(8);
                        w.Write(msg.SequenceId);
                    });

                case MessageType.Sequence:
                    // [9, SequenceId]
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    {
                        w.WriteArrayHeader(2);
                        w.Write(9);
                        w.Write(msg.SequenceId);
                    });

                default:
                    return null;
            }
        }

        // ── ParseBytes ───────────────────────────────────────────────────────

        public List<SignalRMessage> ParseBytes(byte[] data, int offset, int length)
        {
            var messages = new List<SignalRMessage>();
            while (offset < offset + length && offset < data.Length)
            {
                int msgLen = (int)ReadVarInt(data, ref offset);
                if (msgLen == 0 || offset + msgLen > data.Length) break;

                var reader = new MessagePackReader(
                    new ReadOnlyMemory<byte>(data, offset, msgLen));
                offset += msgLen;

                reader.ReadArrayHeader();
                int msgType = reader.ReadByte();

                switch ((MessageType)msgType)
                {
                    case MessageType.Invocation:       messages.Add(ReadInvocation(ref reader));   break;
                    case MessageType.StreamItem:        messages.Add(ReadStreamItem(ref reader));   break;
                    case MessageType.Completion:        messages.Add(ReadCompletion(ref reader));   break;
                    case MessageType.StreamInvocation:  messages.Add(ReadStreamInvocation(ref reader)); break;
                    case MessageType.CancelInvocation:  messages.Add(ReadCancelInvocation(ref reader)); break;
                    case MessageType.Ping:
                        messages.Add(new SignalRMessage { Type = MessageType.Ping });
                        break;
                    case MessageType.Close:            messages.Add(ReadClose(ref reader));        break;
                    case MessageType.Ack:
                        messages.Add(new SignalRMessage { Type = MessageType.Ack,      SequenceId = reader.ReadInt64() });
                        break;
                    case MessageType.Sequence:
                        messages.Add(new SignalRMessage { Type = MessageType.Sequence, SequenceId = reader.ReadInt64() });
                        break;
                }
            }
            return messages;
        }

        // ── ConvertTo ────────────────────────────────────────────────────────

        public object ConvertTo(Type toType, object obj)
        {
            if (obj == null) return null;
            if (toType.IsEnum)   return Enum.Parse(toType, obj.ToString(), true);
            if (toType.IsPrimitive) return Convert.ChangeType(obj, toType);
            if (toType == typeof(string)) return obj.ToString();
            if (toType.IsGenericType && toType.Name == "Nullable`1")
                return Convert.ChangeType(obj, toType.GetGenericArguments()[0]);
            return obj;
        }

        // ── Private message readers ───────────────────────────────────────────

        private SignalRMessage ReadInvocation(ref MessagePackReader reader)
        {
            ReadHeaders(ref reader);
            string invocationId = reader.ReadString();
            string target       = reader.ReadString();
            object[] arguments  = ReadArguments(ref reader, target);
            string[] streamIds  = ReadStreamIds(ref reader);

            return new SignalRMessage
            {
                Type         = MessageType.Invocation,
                InvocationId = invocationId,
                Target       = target,
                Arguments    = arguments,
                StreamIds    = streamIds,
            };
        }

        private SignalRMessage ReadStreamInvocation(ref MessagePackReader reader)
        {
            ReadHeaders(ref reader);
            string invocationId = reader.ReadString();
            string target       = reader.ReadString();
            object[] arguments  = ReadArguments(ref reader, target);
            string[] streamIds  = ReadStreamIds(ref reader);

            return new SignalRMessage
            {
                Type         = MessageType.StreamInvocation,
                InvocationId = invocationId,
                Target       = target,
                Arguments    = arguments,
                StreamIds    = streamIds,
            };
        }

        private SignalRMessage ReadCompletion(ref MessagePackReader reader)
        {
            ReadHeaders(ref reader);
            string invocationId = reader.ReadString();
            byte   resultKind   = reader.ReadByte();

            switch (resultKind)
            {
                case 1: // error
                    return new SignalRMessage
                    {
                        Type         = MessageType.Completion,
                        InvocationId = invocationId,
                        Error        = reader.ReadString(),
                    };

                case 2: // void
                    return new SignalRMessage
                    {
                        Type         = MessageType.Completion,
                        InvocationId = invocationId,
                    };

                case 3: // non-void
                    object item = ReadItem(ref reader, invocationId);
                    return new SignalRMessage
                    {
                        Type         = MessageType.Completion,
                        InvocationId = invocationId,
                        Item         = item,
                        Result       = item,
                    };

                default:
                    throw new NotImplementedException("Unknown resultKind: " + resultKind);
            }
        }

        private SignalRMessage ReadStreamItem(ref MessagePackReader reader)
        {
            ReadHeaders(ref reader);
            string invocationId = reader.ReadString();
            object item         = ReadItem(ref reader, invocationId);

            return new SignalRMessage
            {
                Type         = MessageType.StreamItem,
                InvocationId = invocationId,
                Item         = item,
            };
        }

        private SignalRMessage ReadCancelInvocation(ref MessagePackReader reader)
        {
            ReadHeaders(ref reader);
            string invocationId = reader.ReadString();
            return new SignalRMessage
            {
                Type         = MessageType.CancelInvocation,
                InvocationId = invocationId,
            };
        }

        private SignalRMessage ReadClose(ref MessagePackReader reader)
        {
            string error       = reader.ReadString();
            bool   allowRecon  = false;
            try { allowRecon   = reader.ReadBoolean(); } catch { }
            return new SignalRMessage
            {
                Type           = MessageType.Close,
                Error          = error,
                AllowReconnect = allowRecon,
            };
        }

        private object ReadItem(ref MessagePackReader reader, string invocationId)
        {
            if (long.TryParse(invocationId, out long longId))
            {
                Type itemType = _getReturnType?.Invoke(invocationId);
                if (itemType != null)
                    return MessagePackSerializer.Deserialize(itemType, reader.ReadRaw());
                reader.Skip();
                return null;
            }
            else
            {
                reader.Skip();
                return null;
            }
        }

        private string[] ReadStreamIds(ref MessagePackReader reader)
        {
            int count = reader.ReadArrayHeader();
            if (count == 0) return null;
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = reader.ReadString();
            return result;
        }

        private object[] ReadArguments(ref MessagePackReader reader, string target)
        {
            Type[] argTypes = _getArgTypes?.Invoke(target);

            if (argTypes == null)
            {
                reader.Skip();
                return null;
            }

            int      count = reader.ReadArrayHeader();
            object[] args  = new object[argTypes.Length];
            for (int i = 0; i < argTypes.Length && i < count; i++)
                args[i] = MessagePackSerializer.Deserialize(argTypes[i], reader.ReadRaw());
            return args;
        }

        private static Dictionary<string, string> ReadHeaders(ref MessagePackReader reader)
        {
            int count = reader.ReadMapHeader();
            if (count == 0) return null;
            var result = new Dictionary<string, string>(count);
            for (int i = 0; i < count; i++)
                result[reader.ReadString()] = reader.ReadString();
            return result;
        }

        // ── Encode helpers ────────────────────────────────────────────────────

        private delegate void WriteDelegate(ref MessagePackWriter writer, MemoryStreamBufferWriter bufWriter);

        private static byte[] Frame(WriteDelegate write)
        {
            using var ms  = new MemoryStream(64);
            // 5-byte placeholder for the VarInt length prefix
            ms.Write(new byte[5], 0, 5);
            var bufWriter = new MemoryStreamBufferWriter(ms);
            var writer    = new MessagePackWriter(bufWriter);
            write(ref writer, bufWriter);
            writer.Flush();
            return Finalize(ms);
        }

        private static byte[] Finalize(MemoryStream ms)
        {
            int    contentLen = (int)ms.Position - 5;
            byte[] buf        = ms.GetBuffer();

            byte prefixSize = GetRequiredBytesForLengthPrefix(contentLen);
            WriteLengthAsVarInt(buf, 5 - prefixSize, contentLen);

            byte[] result = new byte[contentLen + prefixSize];
            Array.Copy(buf, 5 - prefixSize, result, 0, result.Length);
            return result;
        }

        private static void WriteValue(
            ref MessagePackWriter writer, MemoryStreamBufferWriter bufWriter, object item)
        {
            if (item == null) { writer.WriteNil(); return; }
            writer.Flush();
            MessagePackSerializer.Serialize(item.GetType(), bufWriter, item);
        }

        private static void WriteString(ref MessagePackWriter writer, string str)
        {
            if (str == null) { writer.WriteNil(); return; }
            int    count  = System.Text.Encoding.UTF8.GetByteCount(str);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                System.Text.Encoding.UTF8.GetBytes(str, 0, str.Length, buffer, 0);
                writer.WriteString(new ReadOnlySpan<byte>(buffer, 0, count));
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private static void WriteHeaders(ref MessagePackWriter writer)
            => writer.WriteMapHeader(0);

        // ── VarInt framing (identical to original) ────────────────────────────

        public static byte GetRequiredBytesForLengthPrefix(int length)
        {
            byte bytes = 0;
            do { length >>= 7; bytes++; } while (length > 0);
            return bytes;
        }

        public static int WriteLengthAsVarInt(byte[] data, int offset, int length)
        {
            do
            {
                byte current = (byte)(length & 0x7f);
                length >>= 7;
                if (length > 0) current |= 0x80;
                data[offset++] = current;
            }
            while (length > 0);
            return offset;
        }

        public static uint ReadVarInt(byte[] data, ref int offset)
        {
            uint length   = 0;
            int  numBytes = 0;
            byte byteRead;
            do
            {
                byteRead  = data[offset + numBytes];
                length   |= (uint)(byteRead & 0x7f) << (numBytes * 7);
                numBytes++;
            }
            while (offset + numBytes < data.Length && (byteRead & 0x80) != 0);
            offset += numBytes;
            return length;
        }
    }
}
#endif
