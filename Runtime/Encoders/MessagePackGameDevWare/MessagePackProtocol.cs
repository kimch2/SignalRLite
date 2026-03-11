// SignalR MessagePack protocol implementation using GameDevWare.Serialization.MessagePack.
// Enable with scripting define: SIGNALRLITE_GAMEDEVWARE_MESSAGEPACK

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
    /// Json &amp; MessagePack Serialization asset-store package (GameDevWare).
    /// </summary>
    public sealed class MessagePackProtocol : ISignalRProtocol
    {
        public string Name     => "messagepack";
        public bool   IsBinary => true;

        public string HandshakeRequest
            => "{\"protocol\":\"messagepack\",\"version\":1}\x1e";

        private Func<string, Type[]> _getArgTypes;
        private Func<string, Type>   _getReturnType;

        public Func<string, Type[]> GetArgTypes   { set => _getArgTypes   = value; }
        public Func<string, Type>   GetReturnType { set => _getReturnType = value; }

        public string EncodeText(SignalRMessage msg)       => null;
        public List<SignalRMessage> ParseText(string text) => new List<SignalRMessage>();

        public MessagePackProtocolSerializationOptions Options { get; set; }

        public MessagePackProtocol()
            : this(new MessagePackProtocolSerializationOptions
            {
                EnumSerializerFactory = enumType => new EnumNumberSerializer(enumType)
            }) { }

        public MessagePackProtocol(MessagePackProtocolSerializationOptions options)
        {
            this.Options = options;
            GameDevWare.Serialization.Json.DefaultSerializers.Clear();
            GameDevWare.Serialization.Json.DefaultSerializers.AddRange(new TypeSerializer[]
            {
                new BinarySerializer(), new DateTimeOffsetSerializer(), new DateTimeSerializer(),
                new GuidSerializer(), new StreamSerializer(), new UriSerializer(),
                new VersionSerializer(), new TimeSpanSerializer(), new DictionaryEntrySerializer(),
                new Vector2Serializer(), new Vector3Serializer(), new Vector4Serializer(),
                new PrimitiveSerializer(typeof(bool)),   new PrimitiveSerializer(typeof(byte)),
                new PrimitiveSerializer(typeof(decimal)), new PrimitiveSerializer(typeof(double)),
                new PrimitiveSerializer(typeof(short)),  new PrimitiveSerializer(typeof(int)),
                new PrimitiveSerializer(typeof(long)),   new PrimitiveSerializer(typeof(sbyte)),
                new PrimitiveSerializer(typeof(float)),  new PrimitiveSerializer(typeof(ushort)),
                new PrimitiveSerializer(typeof(uint)),   new PrimitiveSerializer(typeof(ulong)),
                new PrimitiveSerializer(typeof(string)),
            });
        }

        public byte[] EncodeBytes(SignalRMessage msg)
        {
            using var stream = new System.IO.MemoryStream(256);
            stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte(0);
            var ctx = new SerializationContext
            {
                Options               = SerializationOptions.SuppressTypeInformation,
                EnumSerializerFactory = this.Options.EnumSerializerFactory,
                ExtensionTypeHandler  = CustomMessagePackExtensionTypeHandler.Instance,
            };
            var writer = new MsgPackWriter(stream, ctx, new byte[MsgPackWriter.DEFAULT_BUFFER_SIZE]);

            switch (msg.Type)
            {
                case MessageType.StreamItem:
                    writer.WriteArrayBegin(4); writer.WriteNumber(2); WriteHeaders(writer);
                    writer.WriteString(msg.InvocationId); WriteValue(writer, msg.Item); writer.WriteArrayEnd(); break;
                case MessageType.Completion:
                    byte rk = (byte)(!string.IsNullOrEmpty(msg.Error) ? 1 : msg.Result != null ? 3 : 2);
                    writer.WriteArrayBegin(rk == 2 ? 4 : 5); writer.WriteNumber(3); WriteHeaders(writer);
                    writer.WriteString(msg.InvocationId); writer.WriteNumber(rk);
                    if (rk == 1) writer.WriteString(msg.Error); else if (rk == 3) WriteValue(writer, msg.Result);
                    writer.WriteArrayEnd(); break;
                case MessageType.Invocation:
                case MessageType.StreamInvocation:
                    writer.WriteArrayBegin(msg.StreamIds != null ? 6 : 5);
                    writer.WriteNumber((int)msg.Type); WriteHeaders(writer);
                    writer.WriteString(msg.InvocationId); writer.WriteString(msg.Target);
                    writer.WriteArrayBegin(msg.Arguments?.Length ?? 0);
                    if (msg.Arguments != null) foreach (var a in msg.Arguments) WriteValue(writer, a);
                    writer.WriteArrayEnd();
                    if (msg.StreamIds != null)
                    { writer.WriteArrayBegin(msg.StreamIds.Length); foreach (var s in msg.StreamIds) WriteValue(writer, s); writer.WriteArrayEnd(); }
                    writer.WriteArrayEnd(); break;
                case MessageType.CancelInvocation:
                    writer.WriteArrayBegin(3); writer.WriteNumber(5); WriteHeaders(writer);
                    writer.WriteString(msg.InvocationId); writer.WriteArrayEnd(); break;
                case MessageType.Ping:
                    writer.WriteArrayBegin(1); writer.WriteNumber(6); writer.WriteArrayEnd(); break;
                case MessageType.Close:
                    writer.WriteArrayBegin(string.IsNullOrEmpty(msg.Error) ? 1 : 2); writer.WriteNumber(7);
                    if (!string.IsNullOrEmpty(msg.Error)) writer.WriteString(msg.Error); writer.WriteArrayEnd(); break;
                case MessageType.Ack:
                    writer.WriteArrayBegin(2); writer.WriteNumber(8); writer.WriteNumber(msg.SequenceId); writer.WriteArrayEnd(); break;
                case MessageType.Sequence:
                    writer.WriteArrayBegin(2); writer.WriteNumber(9); writer.WriteNumber(msg.SequenceId); writer.WriteArrayEnd(); break;
            }
            writer.Flush();

            int len = (int)stream.Position - 5; var buf = stream.GetBuffer();
            byte ps = GetRequiredBytesForLengthPrefix(len); WriteLengthAsVarInt(buf, 5 - ps, len);
            byte[] r = new byte[len + ps]; Array.Copy(buf, 5 - ps, r, 0, r.Length); return r;
        }

        public List<SignalRMessage> ParseBytes(byte[] data, int offset, int length)
        {
            var messages = new List<SignalRMessage>(); int end = offset + length;
            while (offset < end && offset < data.Length)
            {
                int msgLen = (int)ReadVarInt(data, ref offset);
                if (msgLen == 0 || offset + msgLen > data.Length) break;
                using var stream = new System.IO.MemoryStream(data, offset, msgLen);
                offset += msgLen;
                var ctx = new SerializationContext { Options = SerializationOptions.SuppressTypeInformation, ExtensionTypeHandler = CustomMessagePackExtensionTypeHandler.Instance };
                var reader = new MsgPackReader(stream, ctx, Endianness.BigEndian, new byte[MsgPackReader.DEFAULT_BUFFER_SIZE]);
                reader.NextToken(); reader.NextToken();
                int mt = reader.ReadByte();
                switch ((MessageType)mt)
                {
                    case MessageType.Invocation:       messages.Add(ReadInvocation(reader));       break;
                    case MessageType.StreamItem:        messages.Add(ReadStreamItem(reader));       break;
                    case MessageType.Completion:        messages.Add(ReadCompletion(reader));       break;
                    case MessageType.StreamInvocation:  messages.Add(ReadStreamInvocation(reader)); break;
                    case MessageType.CancelInvocation:  messages.Add(ReadCancelInvocation(reader)); break;
                    case MessageType.Ping: messages.Add(new SignalRMessage { Type = MessageType.Ping }); break;
                    case MessageType.Close: messages.Add(ReadClose(reader)); break;
                    case MessageType.Ack:      messages.Add(new SignalRMessage { Type = MessageType.Ack,      SequenceId = reader.ReadInt64() }); break;
                    case MessageType.Sequence: messages.Add(new SignalRMessage { Type = MessageType.Sequence, SequenceId = reader.ReadInt64() }); break;
                }
                reader.NextToken();
            }
            return messages;
        }

        public object ConvertTo(Type toType, object obj)
        {
            if (obj == null) return null;
            if (toType.IsEnum)      return Enum.Parse(toType, obj.ToString(), true);
            if (toType.IsPrimitive) return Convert.ChangeType(obj, toType);
            if (toType == typeof(string)) return obj.ToString();
            if (toType.IsGenericType && toType.Name == "Nullable`1") return Convert.ChangeType(obj, toType.GetGenericArguments()[0]);
            return obj;
        }

        private SignalRMessage ReadInvocation(MsgPackReader r)
        {
            ReadHeaders(r); string id = r.ReadString(), t = r.ReadString();
            return new SignalRMessage { Type = MessageType.Invocation, InvocationId = id, Target = t, Arguments = ReadArguments(r, t), StreamIds = ReadStreamIds(r) };
        }
        private SignalRMessage ReadStreamInvocation(MsgPackReader r)
        {
            ReadHeaders(r); string id = r.ReadString(), t = r.ReadString();
            return new SignalRMessage { Type = MessageType.StreamInvocation, InvocationId = id, Target = t, Arguments = ReadArguments(r, t), StreamIds = ReadStreamIds(r) };
        }
        private SignalRMessage ReadCompletion(MsgPackReader r)
        {
            ReadHeaders(r); string id = r.ReadString(); byte rk = r.ReadByte();
            switch (rk)
            {
                case 1: return new SignalRMessage { Type = MessageType.Completion, InvocationId = id, Error = r.ReadString() };
                case 2: return new SignalRMessage { Type = MessageType.Completion, InvocationId = id };
                case 3: object item = ReadItem(r, id); return new SignalRMessage { Type = MessageType.Completion, InvocationId = id, Item = item, Result = item };
                default: throw new NotImplementedException("Unknown resultKind: " + rk);
            }
        }
        private SignalRMessage ReadStreamItem(MsgPackReader r) { ReadHeaders(r); string id = r.ReadString(); return new SignalRMessage { Type = MessageType.StreamItem, InvocationId = id, Item = ReadItem(r, id) }; }
        private SignalRMessage ReadCancelInvocation(MsgPackReader r) { ReadHeaders(r); return new SignalRMessage { Type = MessageType.CancelInvocation, InvocationId = r.ReadString() }; }
        private SignalRMessage ReadClose(MsgPackReader r) { string e = r.ReadString(); bool a = false; try { a = r.ReadBoolean(); } catch { } return new SignalRMessage { Type = MessageType.Close, Error = e, AllowReconnect = a }; }
        private object ReadItem(MsgPackReader r, string id) { if (long.TryParse(id, out _)) { Type t = _getReturnType?.Invoke(id); return t != null ? r.ReadValue(t) : r.ReadValue(typeof(object)); } return r.ReadValue(typeof(object)); }
        private string[] ReadStreamIds(MsgPackReader r) => r.ReadValue(typeof(string[])) as string[];
        private object[] ReadArguments(MsgPackReader r, string target)
        {
            Type[] at = _getArgTypes?.Invoke(target);
            if (at == null) return r.ReadValue(typeof(object[])) as object[];
            r.NextToken(); var args = new object[at.Length]; for (int i = 0; i < at.Length; i++) args[i] = r.ReadValue(at[i]); r.NextToken(); return args;
        }
        private static Dictionary<string, string> ReadHeaders(MsgPackReader r) => r.ReadValue(typeof(Dictionary<string, string>)) as Dictionary<string, string>;
        private static void WriteValue(MsgPackWriter w, object v) { if (v == null) w.WriteNull(); else w.WriteValue(v, v.GetType()); }
        private static void WriteHeaders(MsgPackWriter w) { w.WriteObjectBegin(0); w.WriteObjectEnd(); }

        public static byte GetRequiredBytesForLengthPrefix(int length) { byte b = 0; do { length >>= 7; b++; } while (length > 0); return b; }
        public static int WriteLengthAsVarInt(byte[] data, int offset, int length) { do { byte c = (byte)(length & 0x7f); length >>= 7; if (length > 0) c |= 0x80; data[offset++] = c; } while (length > 0); return offset; }
        public static uint ReadVarInt(byte[] data, ref int offset) { uint len = 0; int n = 0; byte b; do { b = data[offset + n]; len |= (uint)(b & 0x7f) << (n * 7); n++; } while (offset + n < data.Length && (b & 0x80) != 0); offset += n; return len; }
    }

    public sealed class CustomMessagePackExtensionTypeHandler : MessagePackExtensionTypeHandler
    {
        public const int  EXTENSION_TYPE_DATE_TIME = -1;
        public const int  DATE_TIME_SIZE           = 8;
        public const long BclSecondsAtUnixEpoch    = 62135596800;
        public const int  NanosecondsPerTick        = 100;
        public static readonly DateTime UnixEpoch  = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static CustomMessagePackExtensionTypeHandler Instance = new CustomMessagePackExtensionTypeHandler();
        private static readonly Type[] DefaultExtensionTypes = new[] { typeof(DateTime) };
        public override IEnumerable<Type> ExtensionTypes => DefaultExtensionTypes;

        public override bool TryRead(sbyte type, ArraySegment<byte> data, out object value)
        {
            if (data.Array == null) throw new ArgumentNullException("data");
            value = default; if (type != EXTENSION_TYPE_DATE_TIME) return false;
            switch (data.Count)
            {
                case 4:  value = UnixEpoch.AddSeconds(unchecked((uint)(int)FromBytes(data.Array, data.Offset, 4))); return true;
                case 8:  ulong u = unchecked((ulong)FromBytes(data.Array, data.Offset, 8)); value = UnixEpoch.AddSeconds(u & 0x00000003ffffffffL).AddTicks((long)(u >> 34) / NanosecondsPerTick); return true;
                case 12: value = UnixEpoch.AddSeconds(FromBytes(data.Array, data.Offset, 8)).AddTicks(unchecked((uint)(int)FromBytes(data.Array, data.Offset, 4)) / NanosecondsPerTick); return true;
                default: throw new Exception($"DateTime extension length was {data.Count}; expected 4, 8, or 12.");
            }
        }

        public override bool TryWrite(object value, out sbyte type, ref ArraySegment<byte> data)
        {
            if (!(value is DateTime dt)) { type = default; return false; }
            type = EXTENSION_TYPE_DATE_TIME;
            if (dt.Kind == DateTimeKind.Local) dt = dt.ToUniversalTime();
            long sec = dt.Ticks / TimeSpan.TicksPerSecond - BclSecondsAtUnixEpoch;
            long ns  = (dt.Ticks % TimeSpan.TicksPerSecond) * NanosecondsPerTick;
            if ((sec >> 34) == 0)
            {
                ulong d64 = unchecked((ulong)((ns << 34) | sec));
                if ((d64 & 0xffffffff00000000L) == 0) { EnsureSegment(ref data, 4); CopyBytes((uint)d64, 4, data.Array, data.Offset); }
                else { EnsureSegment(ref data, 8); CopyBytes(unchecked((long)d64), 8, data.Array, data.Offset); }
            }
            else { EnsureSegment(ref data, 12); CopyBytes((uint)ns, 4, data.Array, data.Offset); CopyBytes(sec, 8, data.Array, data.Offset + 4); }
            data = new ArraySegment<byte>(data.Array, data.Offset, DATE_TIME_SIZE); return true;
        }

        private static void EnsureSegment(ref ArraySegment<byte> s, int size) { if (s.Array == null || s.Count < size) s = new ArraySegment<byte>(new byte[size]); }
        private static void CopyBytes(long v, int bytes, byte[] buf, int idx) { int e = idx + bytes - 1; for (int i = 0; i < bytes; i++) { buf[e - i] = unchecked((byte)(v & 0xff)); v >>= 8; } }
        private static long FromBytes(byte[] buf, int start, int n) { long r = 0; for (int i = 0; i < n; i++) r = unchecked((r << 8) | buf[start + i]); return r; }
    }

    public sealed class Vector2Serializer : TypeSerializer
    {
        public override Type SerializedType => typeof(Vector2);
        public override object Deserialize(IJsonReader r) { if (r.Token == JsonToken.Null) return null; var v = new Vector2(); r.ReadArrayBegin(); int i = 0; while (r.Token != JsonToken.EndOfArray) v[i++] = r.ReadSingle(); r.ReadArrayEnd(nextToken: false); return v; }
        public override void Serialize(IJsonWriter w, object value) { var v = (Vector2)value; w.WriteArrayBegin(2); w.Write(v.x); w.Write(v.y); w.WriteArrayEnd(); }
    }

    public sealed class Vector3Serializer : TypeSerializer
    {
        public override Type SerializedType => typeof(Vector3);
        public override object Deserialize(IJsonReader r) { if (r.Token == JsonToken.Null) return null; var v = new Vector3(); r.ReadArrayBegin(); int i = 0; while (r.Token != JsonToken.EndOfArray) v[i++] = r.ReadSingle(); r.ReadArrayEnd(nextToken: false); return v; }
        public override void Serialize(IJsonWriter w, object value) { var v = (Vector3)value; w.WriteArrayBegin(3); w.Write(v.x); w.Write(v.y); w.Write(v.z); w.WriteArrayEnd(); }
    }

    public sealed class Vector4Serializer : TypeSerializer
    {
        public override Type SerializedType => typeof(Vector4);
        public override object Deserialize(IJsonReader r) { if (r.Token == JsonToken.Null) return null; var v = new Vector4(); r.ReadArrayBegin(); int i = 0; while (r.Token != JsonToken.EndOfArray) v[i++] = r.ReadSingle(); r.ReadArrayEnd(nextToken: false); return v; }
        public override void Serialize(IJsonWriter w, object value) { var v = (Vector4)value; w.WriteArrayBegin(4); w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); w.WriteArrayEnd(); }
    }
}
