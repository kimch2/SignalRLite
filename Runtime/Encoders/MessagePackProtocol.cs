#if SIGNALRLITE_GAMEDEVWARE_MESSAGEPACK
// SignalR MessagePack protocol implementation using GameDevWare.Serialization.MessagePack.
// Requires: Asset Store package + scripting define SIGNALRLITE_GAMEDEVWARE_MESSAGEPACK.
// Includes Vector2/3/4 serializers and DateTime extension type handler.

using System;
using System.Collections.Generic;
using GameDevWare.Serialization;
using GameDevWare.Serialization.MessagePack;
using GameDevWare.Serialization.Serializers;
using SignalRLite.Messages;
using UnityEngine;

namespace SignalRLite.Encoders
{
    public sealed class MessagePackProtocolSerializationOptions
    {
        /// <summary>
        /// Factory that returns a TypeSerializer for the given enum Type.
        /// Default: <see cref="EnumNumberSerializer"/> (enums as numbers).
        /// </summary>
        public Func<Type, TypeSerializer> EnumSerializerFactory;
    }

    /// <summary>
    /// SignalR MessagePack binary protocol backed by the
    /// <a href="https://assetstore.unity.com/packages/tools/network/json-messagepack-serialization-59918">
    /// Json &amp; MessagePack Serialization</a> asset-store package (GameDevWare).
    /// </summary>
    public sealed class MessagePackProtocol : ISignalRProtocol
    {
        // ── ISignalRProtocol ─────────────────────────────────────────────────

        public string Name     => "messagepack";
        public bool   IsBinary => true;

        public string HandshakeRequest
            => "{\"protocol\":\"messagepack\",\"version\":1}\x1e";

        private Func<string, Type[]> _getArgTypes;
        private Func<string, Type>   _getReturnType;

        public Func<string, Type[]> GetArgTypes   { set => _getArgTypes   = value; }
        public Func<string, Type>   GetReturnType { set => _getReturnType = value; }

        public string EncodeText(SignalRMessage msg)  => null;
        public List<SignalRMessage> ParseText(string text) => new List<SignalRMessage>();

        // ── Options ──────────────────────────────────────────────────────────

        public MessagePackProtocolSerializationOptions Options { get; set; }

        public MessagePackProtocol()
            : this(new MessagePackProtocolSerializationOptions
            {
                EnumSerializerFactory = enumType => new EnumNumberSerializer(enumType)
            })
        { }

        public MessagePackProtocol(MessagePackProtocolSerializationOptions options)
        {
            this.Options = options;

            GameDevWare.Serialization.Json.DefaultSerializers.Clear();
            GameDevWare.Serialization.Json.DefaultSerializers.AddRange(new TypeSerializer[]
            {
                new BinarySerializer(),
                new DateTimeOffsetSerializer(),
                new DateTimeSerializer(),
                new GuidSerializer(),
                new StreamSerializer(),
                new UriSerializer(),
                new VersionSerializer(),
                new TimeSpanSerializer(),
                new DictionaryEntrySerializer(),

                new Vector2Serializer(),
                new Vector3Serializer(),
                new Vector4Serializer(),

                new PrimitiveSerializer(typeof(bool)),
                new PrimitiveSerializer(typeof(byte)),
                new PrimitiveSerializer(typeof(decimal)),
                new PrimitiveSerializer(typeof(double)),
                new PrimitiveSerializer(typeof(short)),
                new PrimitiveSerializer(typeof(int)),
                new PrimitiveSerializer(typeof(long)),
                new PrimitiveSerializer(typeof(sbyte)),
                new PrimitiveSerializer(typeof(float)),
                new PrimitiveSerializer(typeof(ushort)),
                new PrimitiveSerializer(typeof(uint)),
                new PrimitiveSerializer(typeof(ulong)),
                new PrimitiveSerializer(typeof(string)),
            });
        }

        // ── EncodeBytes ──────────────────────────────────────────────────────

        public byte[] EncodeBytes(SignalRMessage msg)
        {
            using var stream = new System.IO.MemoryStream(256);

            // 5-byte placeholder for VarInt length prefix
            stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte(0);
            stream.WriteByte(0); stream.WriteByte(0);

            var buffer  = new byte[MsgPackWriter.DEFAULT_BUFFER_SIZE];
            var context = new SerializationContext
            {
                Options              = SerializationOptions.SuppressTypeInformation,
                EnumSerializerFactory = this.Options.EnumSerializerFactory,
                ExtensionTypeHandler  = CustomMessagePackExtensionTypeHandler.Instance,
            };
            var writer = new MsgPackWriter(stream, context, buffer);

            switch (msg.Type)
            {
                case MessageType.StreamItem:
                    writer.WriteArrayBegin(4);
                    writer.WriteNumber(2);
                    WriteHeaders(writer);
                    writer.WriteString(msg.InvocationId);
                    WriteValue(writer, msg.Item);
                    writer.WriteArrayEnd();
                    break;

                case MessageType.Completion:
                    byte resultKind = (byte)(!string.IsNullOrEmpty(msg.Error) ? 1
                                            : msg.Result != null               ? 3 : 2);
                    writer.WriteArrayBegin(resultKind == 2 ? 4 : 5);
                    writer.WriteNumber(3);
                    WriteHeaders(writer);
                    writer.WriteString(msg.InvocationId);
                    writer.WriteNumber(resultKind);
                    if (resultKind == 1)      writer.WriteString(msg.Error);
                    else if (resultKind == 3) WriteValue(writer, msg.Result);
                    writer.WriteArrayEnd();
                    break;

                case MessageType.Invocation:
                case MessageType.StreamInvocation:
                    writer.WriteArrayBegin(msg.StreamIds != null ? 6 : 5);
                    writer.WriteNumber((int)msg.Type);
                    WriteHeaders(writer);
                    writer.WriteString(msg.InvocationId);
                    writer.WriteString(msg.Target);
                    writer.WriteArrayBegin(msg.Arguments?.Length ?? 0);
                    if (msg.Arguments != null)
                        foreach (var a in msg.Arguments) WriteValue(writer, a);
                    writer.WriteArrayEnd();
                    if (msg.StreamIds != null)
                    {
                        writer.WriteArrayBegin(msg.StreamIds.Length);
                        foreach (var s in msg.StreamIds) WriteValue(writer, s);
                        writer.WriteArrayEnd();
                    }
                    writer.WriteArrayEnd();
                    break;

                case MessageType.CancelInvocation:
                    writer.WriteArrayBegin(3);
                    writer.WriteNumber(5);
                    WriteHeaders(writer);
                    writer.WriteString(msg.InvocationId);
                    writer.WriteArrayEnd();
                    break;

                case MessageType.Ping:
                    writer.WriteArrayBegin(1);
                    writer.WriteNumber(6);
                    writer.WriteArrayEnd();
                    break;

                case MessageType.Close:
                    writer.WriteArrayBegin(string.IsNullOrEmpty(msg.Error) ? 1 : 2);
                    writer.WriteNumber(7);
                    if (!string.IsNullOrEmpty(msg.Error)) writer.WriteString(msg.Error);
                    writer.WriteArrayEnd();
                    break;

                case MessageType.Ack:
                    writer.WriteArrayBegin(2);
                    writer.WriteNumber(8);
                    writer.WriteNumber(msg.SequenceId);
                    writer.WriteArrayEnd();
                    break;

                case MessageType.Sequence:
                    writer.WriteArrayBegin(2);
                    writer.WriteNumber(9);
                    writer.WriteNumber(msg.SequenceId);
                    writer.WriteArrayEnd();
                    break;
            }

            writer.Flush();

            int length        = (int)stream.Position;
            int contentLength = length - 5;
            var buf           = stream.GetBuffer();

            byte prefixBytes = GetRequiredBytesForLengthPrefix(contentLength);
            WriteLengthAsVarInt(buf, 5 - prefixBytes, contentLength);

            byte[] result = new byte[contentLength + prefixBytes];
            Array.Copy(buf, 5 - prefixBytes, result, 0, result.Length);
            return result;
        }

        // ── ParseBytes ───────────────────────────────────────────────────────

        public List<SignalRMessage> ParseBytes(byte[] data, int offset, int length)
        {
            var messages = new List<SignalRMessage>();

            while (offset < offset + length && offset < data.Length)
            {
                int msgLen = (int)ReadVarInt(data, ref offset);
                if (msgLen == 0 || offset + msgLen > data.Length) break;

                using var stream = new System.IO.MemoryStream(data, offset, msgLen);
                offset += msgLen;

                var buff    = new byte[MsgPackReader.DEFAULT_BUFFER_SIZE];
                var context = new SerializationContext
                {
                    Options              = SerializationOptions.SuppressTypeInformation,
                    ExtensionTypeHandler  = CustomMessagePackExtensionTypeHandler.Instance,
                };
                var reader  = new MsgPackReader(stream, context, Endianness.BigEndian, buff);

                reader.NextToken();
                reader.NextToken();

                int messageType = reader.ReadByte();
                switch ((MessageType)messageType)
                {
                    case MessageType.Invocation:       messages.Add(ReadInvocation(reader));      break;
                    case MessageType.StreamItem:        messages.Add(ReadStreamItem(reader));      break;
                    case MessageType.Completion:        messages.Add(ReadCompletion(reader));      break;
                    case MessageType.StreamInvocation:  messages.Add(ReadStreamInvocation(reader)); break;
                    case MessageType.CancelInvocation:  messages.Add(ReadCancelInvocation(reader)); break;
                    case MessageType.Ping:
                        messages.Add(new SignalRMessage { Type = MessageType.Ping });
                        break;
                    case MessageType.Close: messages.Add(ReadClose(reader)); break;
                    case MessageType.Ack:
                        messages.Add(new SignalRMessage { Type = MessageType.Ack,      SequenceId = reader.ReadInt64() });
                        break;
                    case MessageType.Sequence:
                        messages.Add(new SignalRMessage { Type = MessageType.Sequence, SequenceId = reader.ReadInt64() });
                        break;
                }

                reader.NextToken();
            }

            return messages;
        }

        // ── ConvertTo ────────────────────────────────────────────────────────

        public object ConvertTo(Type toType, object obj)
        {
            if (obj == null) return null;
            if (toType.IsEnum)      return Enum.Parse(toType, obj.ToString(), true);
            if (toType.IsPrimitive) return Convert.ChangeType(obj, toType);
            if (toType == typeof(string)) return obj.ToString();
            if (toType.IsGenericType && toType.Name == "Nullable`1")
                return Convert.ChangeType(obj, toType.GetGenericArguments()[0]);
            return obj;
        }

        // ── Private message readers ───────────────────────────────────────────

        private SignalRMessage ReadInvocation(MsgPackReader reader)
        {
            ReadHeaders(reader);
            string invocationId = reader.ReadString();
            string target       = reader.ReadString();
            object[] arguments  = ReadArguments(reader, target);
            string[] streamIds  = ReadStreamIds(reader);
            return new SignalRMessage
            {
                Type = MessageType.Invocation, InvocationId = invocationId,
                Target = target, Arguments = arguments, StreamIds = streamIds,
            };
        }

        private SignalRMessage ReadStreamInvocation(MsgPackReader reader)
        {
            ReadHeaders(reader);
            string invocationId = reader.ReadString();
            string target       = reader.ReadString();
            object[] arguments  = ReadArguments(reader, target);
            string[] streamIds  = ReadStreamIds(reader);
            return new SignalRMessage
            {
                Type = MessageType.StreamInvocation, InvocationId = invocationId,
                Target = target, Arguments = arguments, StreamIds = streamIds,
            };
        }

        private SignalRMessage ReadCompletion(MsgPackReader reader)
        {
            ReadHeaders(reader);
            string invocationId = reader.ReadString();
            byte   resultKind   = reader.ReadByte();
            switch (resultKind)
            {
                case 1: return new SignalRMessage { Type = MessageType.Completion, InvocationId = invocationId, Error = reader.ReadString() };
                case 2: return new SignalRMessage { Type = MessageType.Completion, InvocationId = invocationId };
                case 3:
                    object item = ReadItem(reader, invocationId);
                    return new SignalRMessage { Type = MessageType.Completion, InvocationId = invocationId, Item = item, Result = item };
                default:
                    throw new NotImplementedException("Unknown resultKind: " + resultKind);
            }
        }

        private SignalRMessage ReadStreamItem(MsgPackReader reader)
        {
            ReadHeaders(reader);
            string invocationId = reader.ReadString();
            object item         = ReadItem(reader, invocationId);
            return new SignalRMessage { Type = MessageType.StreamItem, InvocationId = invocationId, Item = item };
        }

        private SignalRMessage ReadCancelInvocation(MsgPackReader reader)
        {
            ReadHeaders(reader);
            return new SignalRMessage { Type = MessageType.CancelInvocation, InvocationId = reader.ReadString() };
        }

        private SignalRMessage ReadClose(MsgPackReader reader)
        {
            string error      = reader.ReadString();
            bool   allowRecon = false;
            try { allowRecon  = reader.ReadBoolean(); } catch { }
            return new SignalRMessage { Type = MessageType.Close, Error = error, AllowReconnect = allowRecon };
        }

        private object ReadItem(MsgPackReader reader, string invocationId)
        {
            if (long.TryParse(invocationId, out _))
            {
                Type itemType = _getReturnType?.Invoke(invocationId);
                return itemType != null
                    ? reader.ReadValue(itemType)
                    : reader.ReadValue(typeof(object));
            }
            return reader.ReadValue(typeof(object));
        }

        private string[] ReadStreamIds(MsgPackReader reader)
            => reader.ReadValue(typeof(string[])) as string[];

        private object[] ReadArguments(MsgPackReader reader, string target)
        {
            Type[] argTypes = _getArgTypes?.Invoke(target);
            if (argTypes == null)
                return reader.ReadValue(typeof(object[])) as object[];

            reader.NextToken();
            var args = new object[argTypes.Length];
            for (int i = 0; i < argTypes.Length; i++)
                args[i] = reader.ReadValue(argTypes[i]);
            reader.NextToken();
            return args;
        }

        private static Dictionary<string, string> ReadHeaders(MsgPackReader reader)
            => reader.ReadValue(typeof(Dictionary<string, string>)) as Dictionary<string, string>;

        // ── Encode helpers ────────────────────────────────────────────────────

        private static void WriteValue(MsgPackWriter writer, object value)
        {
            if (value == null) writer.WriteNull();
            else               writer.WriteValue(value, value.GetType());
        }

        private static void WriteHeaders(MsgPackWriter writer)
        {
            writer.WriteObjectBegin(0);
            writer.WriteObjectEnd();
        }

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

    // ── DateTime extension type handler ──────────────────────────────────────

    public sealed class CustomMessagePackExtensionTypeHandler : MessagePackExtensionTypeHandler
    {
        public const int  EXTENSION_TYPE_DATE_TIME = -1;
        public const int  DATE_TIME_SIZE           = 8;
        public const long BclSecondsAtUnixEpoch    = 62135596800;
        public const int  NanosecondsPerTick        = 100;
        public static readonly DateTime UnixEpoch   = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly Type[] DefaultExtensionTypes = new[] { typeof(DateTime) };
        public static CustomMessagePackExtensionTypeHandler Instance = new CustomMessagePackExtensionTypeHandler();

        public override IEnumerable<Type> ExtensionTypes => DefaultExtensionTypes;

        public override bool TryRead(sbyte type, ArraySegment<byte> data, out object value)
        {
            if (data.Array == null) throw new ArgumentNullException("data");
            value = default;
            if (type != EXTENSION_TYPE_DATE_TIME) return false;

            switch (data.Count)
            {
                case 4:
                {
                    var intValue = unchecked((int)FromBytes(data.Array, data.Offset, 4));
                    value = UnixEpoch.AddSeconds(unchecked((uint)intValue));
                    return true;
                }
                case 8:
                {
                    ulong ulongValue = unchecked((ulong)FromBytes(data.Array, data.Offset, 8));
                    long  nanoseconds = (long)(ulongValue >> 34);
                    ulong seconds     = ulongValue & 0x00000003ffffffffL;
                    value = UnixEpoch.AddSeconds(seconds).AddTicks(nanoseconds / NanosecondsPerTick);
                    return true;
                }
                case 12:
                {
                    var  intValue  = unchecked((int)FromBytes(data.Array, data.Offset, 4));
                    long longValue = FromBytes(data.Array, data.Offset, 8);
                    value = UnixEpoch.AddSeconds(longValue)
                                     .AddTicks(unchecked((uint)intValue) / NanosecondsPerTick);
                    return true;
                }
                default:
                    throw new Exception($"DateTime extension length was {data.Count}; expected 4, 8, or 12.");
            }
        }

        public override bool TryWrite(object value, out sbyte type, ref ArraySegment<byte> data)
        {
            if (value is DateTime dateTime)
            {
                type = EXTENSION_TYPE_DATE_TIME;
                if (dateTime.Kind == DateTimeKind.Local) dateTime = dateTime.ToUniversalTime();

                long secondsSinceBcl = dateTime.Ticks / TimeSpan.TicksPerSecond;
                long seconds         = secondsSinceBcl - BclSecondsAtUnixEpoch;
                long nanoseconds     = (dateTime.Ticks % TimeSpan.TicksPerSecond) * NanosecondsPerTick;

                if ((seconds >> 34) == 0)
                {
                    ulong data64 = unchecked((ulong)((nanoseconds << 34) | seconds));
                    if ((data64 & 0xffffffff00000000L) == 0)
                    {
                        EnsureSegment(ref data, 4);
                        CopyBytes((uint)data64, 4, data.Array, data.Offset);
                        data = new ArraySegment<byte>(data.Array, data.Offset, DATE_TIME_SIZE);
                    }
                    else
                    {
                        EnsureSegment(ref data, 8);
                        CopyBytes(unchecked((long)data64), 8, data.Array, data.Offset);
                        data = new ArraySegment<byte>(data.Array, data.Offset, DATE_TIME_SIZE);
                    }
                }
                else
                {
                    EnsureSegment(ref data, 12);
                    CopyBytes((uint)nanoseconds, 4, data.Array, data.Offset);
                    CopyBytes(seconds, 8, data.Array, data.Offset + 4);
                    data = new ArraySegment<byte>(data.Array, data.Offset, DATE_TIME_SIZE);
                }
                return true;
            }
            type = default;
            return false;
        }

        private static void EnsureSegment(ref ArraySegment<byte> seg, int size)
        {
            if (seg.Array == null || seg.Count < size)
                seg = new ArraySegment<byte>(new byte[size]);
        }

        private static void CopyBytes(long value, int bytes, byte[] buffer, int index)
        {
            int endOffset = index + bytes - 1;
            for (int i = 0; i < bytes; i++) { buffer[endOffset - i] = unchecked((byte)(value & 0xff)); value >>= 8; }
        }

        private static long FromBytes(byte[] buffer, int startIndex, int bytesToConvert)
        {
            long ret = 0;
            for (int i = 0; i < bytesToConvert; i++) ret = unchecked((ret << 8) | buffer[startIndex + i]);
            return ret;
        }
    }

    // ── Unity Vector serializers ─────────────────────────────────────────────

    public sealed class Vector2Serializer : TypeSerializer
    {
        public override Type SerializedType => typeof(Vector2);
        public override object Deserialize(IJsonReader reader)
        {
            if (reader.Token == JsonToken.Null) return null;
            var v = new Vector2();
            reader.ReadArrayBegin();
            int idx = 0;
            while (reader.Token != JsonToken.EndOfArray) v[idx++] = reader.ReadSingle();
            reader.ReadArrayEnd(nextToken: false);
            return v;
        }
        public override void Serialize(IJsonWriter writer, object value)
        {
            var v = (Vector2)value;
            writer.WriteArrayBegin(2);
            writer.Write(v.x); writer.Write(v.y);
            writer.WriteArrayEnd();
        }
    }

    public sealed class Vector3Serializer : TypeSerializer
    {
        public override Type SerializedType => typeof(Vector3);
        public override object Deserialize(IJsonReader reader)
        {
            if (reader.Token == JsonToken.Null) return null;
            var v = new Vector3();
            reader.ReadArrayBegin();
            int idx = 0;
            while (reader.Token != JsonToken.EndOfArray) v[idx++] = reader.ReadSingle();
            reader.ReadArrayEnd(nextToken: false);
            return v;
        }
        public override void Serialize(IJsonWriter writer, object value)
        {
            var v = (Vector3)value;
            writer.WriteArrayBegin(3);
            writer.Write(v.x); writer.Write(v.y); writer.Write(v.z);
            writer.WriteArrayEnd();
        }
    }

    public sealed class Vector4Serializer : TypeSerializer
    {
        public override Type SerializedType => typeof(Vector4);
        public override object Deserialize(IJsonReader reader)
        {
            if (reader.Token == JsonToken.Null) return null;
            var v = new Vector4();
            reader.ReadArrayBegin();
            int idx = 0;
            while (reader.Token != JsonToken.EndOfArray) v[idx++] = reader.ReadSingle();
            reader.ReadArrayEnd(nextToken: false);
            return v;
        }
        public override void Serialize(IJsonWriter writer, object value)
        {
            var v = (Vector4)value;
            writer.WriteArrayBegin(4);
            writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); writer.Write(v.w);
            writer.WriteArrayEnd();
        }
    }
}
#endif
