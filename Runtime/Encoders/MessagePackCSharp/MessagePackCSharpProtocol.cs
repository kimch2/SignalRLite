// SignalR MessagePack protocol implementation using MessagePack-CSharp (neuecc/MessagePack-CSharp).
// Enable with scripting define: SIGNALRLITE_MESSAGEPACK_CSHARP

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MessagePack;
using SignalRLite.Messages;

namespace SignalRLite.Encoders
{
    // ── IBufferWriter<byte> backed by MemoryStream ────────────────────────────

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

    /// <summary>
    /// SignalR MessagePack binary protocol backed by MessagePack-CSharp (neuecc/MessagePack-CSharp).
    /// </summary>
    public sealed class MessagePackCSharpProtocol : ISignalRProtocol
    {
        public string Name     => "messagepack";
        public bool   IsBinary => true;

        public string HandshakeRequest
            => "{\"protocol\":\"messagepack\",\"version\":1}\x1e";

        // ASP.NET Core SignalR MessagePack protocol uses a contractless resolver by default,
        // which serializes plain C# classes without [MessagePackObject] attributes.
        // We build the chain explicitly to always include DynamicContractlessObjectResolver,
        // bypassing the !NET_STANDARD_2_0 guard inside ContractlessStandardResolver that
        // excludes it when Unity projects use the .NET Standard API compatibility level.
        private MessagePackSerializerOptions _options;

        /// <summary>
        /// Default constructor. Builds a resolver chain that always includes
        /// <see cref="MessagePack.Resolvers.DynamicContractlessObjectResolver"/> so plain C# classes
        /// without <c>[MessagePackObject]</c> attributes work out of the box (Mono JIT / Unity Editor),
        /// matching ASP.NET Core SignalR's default MessagePack behaviour.
        /// On IL2CPP builds, types still need <c>[MessagePackObject]</c> + mpc codegen.
        /// </summary>
        public MessagePackCSharpProtocol()
            => _options = BuildDefaultOptions();

        private static MessagePackSerializerOptions BuildDefaultOptions()
        {
            // Use ContractlessStandardResolver as the base.
            // When DynamicContractlessObjectResolver is excluded at compile time
            // (Unity + NET_STANDARD_2_0), plain C# types fall back to ContractlessReflectionFallback
            // in ReadItem/ReadArguments instead.
            return MessagePack.Resolvers.ContractlessStandardResolver.Options;
        }

        /// <summary>
        /// Constructor with custom serializer options.
        /// Use this overload when your types are annotated with <c>[MessagePackObject]</c> / <c>[Key]</c>
        /// and you need a specific resolver or security settings.
        /// </summary>
        public MessagePackCSharpProtocol(MessagePackSerializerOptions options)
            => _options = options ?? BuildDefaultOptions();

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
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter bw) =>
                    {
                        w.WriteArrayHeader(4); w.Write(2); WriteHeaders(ref w);
                        WriteString(ref w, msg.InvocationId); WriteValue(ref w, bw, msg.Item);
                    });

                case MessageType.Completion:
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter bw) =>
                    {
                        byte rk = (byte)(!string.IsNullOrEmpty(msg.Error) ? 1 : msg.Result != null ? 3 : 2);
                        w.WriteArrayHeader(rk == 2 ? 4 : 5); w.Write(3); WriteHeaders(ref w);
                        WriteString(ref w, msg.InvocationId); w.Write(rk);
                        if (rk == 1)      WriteString(ref w, msg.Error);
                        else if (rk == 3) WriteValue(ref w, bw, msg.Result);
                    });

                case MessageType.Invocation:
                case MessageType.StreamInvocation:
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter bw) =>
                    {
                        w.WriteArrayHeader(msg.StreamIds != null ? 6 : 5);
                        w.Write((int)msg.Type); WriteHeaders(ref w);
                        WriteString(ref w, msg.InvocationId); WriteString(ref w, msg.Target);
                        w.WriteArrayHeader(msg.Arguments?.Length ?? 0);
                        if (msg.Arguments != null) foreach (var a in msg.Arguments) WriteValue(ref w, bw, a);
                        if (msg.StreamIds != null)
                        {
                            w.WriteArrayHeader(msg.StreamIds.Length);
                            foreach (var s in msg.StreamIds) WriteValue(ref w, bw, s);
                        }
                    });

                case MessageType.CancelInvocation:
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    {
                        w.WriteArrayHeader(3); w.Write(5); WriteHeaders(ref w);
                        WriteString(ref w, msg.InvocationId);
                    });

                case MessageType.Ping:
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    { w.WriteArrayHeader(1); w.Write(6); });

                case MessageType.Close:
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    {
                        bool hasErr = !string.IsNullOrEmpty(msg.Error);
                        w.WriteArrayHeader(hasErr ? 3 : 1);
                        w.Write(7);
                        if (hasErr) { WriteString(ref w, msg.Error); w.Write(msg.AllowReconnect); }
                    });

                case MessageType.Ack:
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    { w.WriteArrayHeader(2); w.Write(8); w.Write(msg.SequenceId); });

                case MessageType.Sequence:
                    return Frame((ref MessagePackWriter w, MemoryStreamBufferWriter _) =>
                    { w.WriteArrayHeader(2); w.Write(9); w.Write(msg.SequenceId); });

                default: return null;
            }
        }

        // ── ParseBytes ───────────────────────────────────────────────────────

        public List<SignalRMessage> ParseBytes(byte[] data, int offset, int length)
        {
            var messages  = new List<SignalRMessage>();
            int endOffset = offset + length;
            while (offset < endOffset && offset < data.Length)
            {
                int msgLen = (int)ReadVarInt(data, ref offset);
                if (msgLen == 0 || offset + msgLen > data.Length) break;

                var reader = new MessagePackReader(new ReadOnlyMemory<byte>(data, offset, msgLen));
                offset += msgLen;

                int arrayLen = reader.ReadArrayHeader();  // must be saved – used by Invocation/Close readers
                int msgType  = reader.ReadByte();
                switch ((MessageType)msgType)
                {
                    case MessageType.Invocation:       messages.Add(ReadInvocation(ref reader, arrayLen));       break;
                    case MessageType.StreamItem:        messages.Add(ReadStreamItem(ref reader));                 break;
                    case MessageType.Completion:        messages.Add(ReadCompletion(ref reader));                 break;
                    case MessageType.StreamInvocation:  messages.Add(ReadStreamInvocation(ref reader, arrayLen)); break;
                    case MessageType.CancelInvocation:  messages.Add(ReadCancelInvocation(ref reader));           break;
                    case MessageType.Ping:
                        messages.Add(new SignalRMessage { Type = MessageType.Ping }); break;
                    case MessageType.Close:            messages.Add(ReadClose(ref reader, arrayLen));            break;
                    case MessageType.Ack:
                        messages.Add(new SignalRMessage { Type = MessageType.Ack,      SequenceId = reader.ReadInt64() }); break;
                    case MessageType.Sequence:
                        messages.Add(new SignalRMessage { Type = MessageType.Sequence, SequenceId = reader.ReadInt64() }); break;
                }
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

        // ── Private readers ───────────────────────────────────────────────────

        private SignalRMessage ReadInvocation(ref MessagePackReader reader, int arrayLen)
        {
            ReadHeaders(ref reader);
            string id = reader.ReadString(), target = reader.ReadString();
            var args      = ReadArguments(ref reader, target);
            var streamIds = arrayLen >= 6 ? ReadStreamIds(ref reader) : null;
            return new SignalRMessage { Type = MessageType.Invocation, InvocationId = id, Target = target,
                Arguments = args, StreamIds = streamIds };
        }

        private SignalRMessage ReadStreamInvocation(ref MessagePackReader reader, int arrayLen)
        {
            ReadHeaders(ref reader);
            string id = reader.ReadString(), target = reader.ReadString();
            var args      = ReadArguments(ref reader, target);
            var streamIds = arrayLen >= 6 ? ReadStreamIds(ref reader) : null;
            return new SignalRMessage { Type = MessageType.StreamInvocation, InvocationId = id, Target = target,
                Arguments = args, StreamIds = streamIds };
        }

        private SignalRMessage ReadCompletion(ref MessagePackReader reader)
        {
            ReadHeaders(ref reader);
            string id = reader.ReadString(); byte rk = reader.ReadByte();
            switch (rk)
            {
                case 1: return new SignalRMessage { Type = MessageType.Completion, InvocationId = id, Error = reader.ReadString() };
                case 2: return new SignalRMessage { Type = MessageType.Completion, InvocationId = id };
                case 3:
                    object item = ReadItem(ref reader, id);
                    return new SignalRMessage { Type = MessageType.Completion, InvocationId = id, Item = item, Result = item };
                default: throw new NotImplementedException("Unknown resultKind: " + rk);
            }
        }

        private SignalRMessage ReadStreamItem(ref MessagePackReader reader)
        {
            ReadHeaders(ref reader); string id = reader.ReadString();
            return new SignalRMessage { Type = MessageType.StreamItem, InvocationId = id, Item = ReadItem(ref reader, id) };
        }

        private SignalRMessage ReadCancelInvocation(ref MessagePackReader reader)
        {
            ReadHeaders(ref reader);
            return new SignalRMessage { Type = MessageType.CancelInvocation, InvocationId = reader.ReadString() };
        }

        private SignalRMessage ReadClose(ref MessagePackReader reader, int arrayLen)
        {
            string error    = arrayLen >= 2 ? reader.ReadString() : null;
            bool   allowRec = arrayLen >= 3 && reader.ReadBoolean();
            return new SignalRMessage { Type = MessageType.Close, Error = error, AllowReconnect = allowRec };
        }

        private object ReadItem(ref MessagePackReader reader, string invocationId)
        {
            if (long.TryParse(invocationId, out long _))
            {
                Type t = _getReturnType?.Invoke(invocationId);
                if (t != null)
                {
                    var raw = reader.ReadRaw();
                    try   { return MessagePackSerializer.Deserialize(t, raw, _options); }
                    catch (MessagePackSerializationException)
                          { return ContractlessReflectionFallback(t, raw, _options); }
                }
            }
            reader.Skip(); return null;
        }

        private string[] ReadStreamIds(ref MessagePackReader reader)
        {
            int count = reader.ReadArrayHeader();
            if (count == 0) return null;
            var r = new string[count];
            for (int i = 0; i < count; i++) r[i] = reader.ReadString();
            return r;
        }

        private object[] ReadArguments(ref MessagePackReader reader, string target)
        {
            Type[] argTypes = _getArgTypes?.Invoke(target);
            if (argTypes == null) { reader.Skip(); return null; }
            int count = reader.ReadArrayHeader();
            object[] args = new object[argTypes.Length];
            for (int i = 0; i < argTypes.Length && i < count; i++)
            {
                var raw = reader.ReadRaw();
                try   { args[i] = MessagePackSerializer.Deserialize(argTypes[i], raw, _options); }
                catch (MessagePackSerializationException)
                      { args[i] = ContractlessReflectionFallback(argTypes[i], raw, _options); }
            }
            for (int i = argTypes.Length; i < count; i++) reader.Skip();
            return args;
        }

        private static Dictionary<string, string> ReadHeaders(ref MessagePackReader reader)
        {
            int count = reader.ReadMapHeader();
            if (count == 0) return null;
            var r = new Dictionary<string, string>(count);
            for (int i = 0; i < count; i++) r[reader.ReadString()] = reader.ReadString();
            return r;
        }

        // ── Reflection fallback for contractless types ────────────────────────
        // Used when DynamicContractlessObjectResolver is excluded from compilation
        // (Unity + NET_STANDARD_2_0).  Reads a MessagePack array (or map) and maps
        // each element to a public field/property in MetadataToken (declaration) order,
        // matching the array format that DynamicContractlessObjectResolver produces
        // on the ASP.NET Core server side.

        // Per-type caches so reflection cost is paid only once per type.
        private static readonly Dictionary<Type, (Type mType, Action<object, object> setter)[]>
            _memberArrayCache = new Dictionary<Type, (Type, Action<object, object>)[]>();
        private static readonly Dictionary<Type, Dictionary<string, (Type mType, Action<object, object> setter)>>
            _memberMapCache   = new Dictionary<Type, Dictionary<string, (Type, Action<object, object>)>>();

        private static object ContractlessReflectionFallback(
            Type t, ReadOnlySequence<byte> raw, MessagePackSerializerOptions opts)
        {
            var reader = new MessagePackReader(raw);
            if (reader.TryReadNil()) return null;

            if (reader.NextMessagePackType == MessagePackType.Map)
                return ContractlessReflectionMap(t, ref reader, opts);

            if (reader.NextMessagePackType != MessagePackType.Array)
            {
                reader.Skip();
                return null;
            }

            var obj     = Activator.CreateInstance(t);
            var members = GetContractlessMembers(t);
            int len     = reader.ReadArrayHeader();

            for (int i = 0; i < len; i++)
            {
                var elemRaw = reader.ReadRaw();    // advances reader before try/catch
                if (i >= members.Length) continue; // already consumed the bytes above
                var (mType, setter) = members[i];
                object val;
                try   { val = MessagePackSerializer.Deserialize(mType, elemRaw, opts); }
                catch { val = mType.IsValueType ? Activator.CreateInstance(mType) : null; }
                setter(obj, val);
            }
            return obj;
        }

        private static object ContractlessReflectionMap(
            Type t, ref MessagePackReader reader, MessagePackSerializerOptions opts)
        {
            var obj    = Activator.CreateInstance(t);
            var byName = GetContractlessMembersByName(t);
            int len    = reader.ReadMapHeader();
            for (int i = 0; i < len; i++)
            {
                var key     = reader.ReadString();
                var elemRaw = reader.ReadRaw();
                if (key != null && byName.TryGetValue(key, out var info))
                {
                    var (mType, setter) = info;
                    object val;
                    try   { val = MessagePackSerializer.Deserialize(mType, elemRaw, opts); }
                    catch { val = mType.IsValueType ? Activator.CreateInstance(mType) : null; }
                    setter(obj, val);
                }
            }
            return obj;
        }

        // Returns (memberType, setter) in MetadataToken order (declaration order).
        // Result is cached per type to avoid repeated reflection on every message.
        private static (Type mType, Action<object, object> setter)[] GetContractlessMembers(Type t)
        {
            if (_memberArrayCache.TryGetValue(t, out var cached)) return cached;
            var result = t
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Cast<MemberInfo>()
                .Concat(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanWrite).Cast<MemberInfo>())
                .OrderBy(m => m.MetadataToken)
                .Select(m => m is FieldInfo f
                    ? (f.FieldType, (Action<object, object>)((o, v) => f.SetValue(o, v)))
                    : (((PropertyInfo)m).PropertyType,
                       (Action<object, object>)((o, v) => ((PropertyInfo)m).SetValue(o, v))))
                .ToArray();
            _memberArrayCache[t] = result;
            return result;
        }

        // Returns members by name (case-insensitive). Cached per type.
        private static Dictionary<string, (Type mType, Action<object, object> setter)>
            GetContractlessMembersByName(Type t)
        {
            if (_memberMapCache.TryGetValue(t, out var cached)) return cached;
            var d = new Dictionary<string, (Type, Action<object, object>)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                d[f.Name] = (f.FieldType, (o, v) => f.SetValue(o, v));
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                .Where(p => p.CanWrite))
                d[p.Name] = (p.PropertyType, (o, v) => p.SetValue(o, v));
            _memberMapCache[t] = d;
            return d;
        }

        // ── Encode helpers ────────────────────────────────────────────────────

        private delegate void WriteDelegate(ref MessagePackWriter w, MemoryStreamBufferWriter bw);

        private byte[] Frame(WriteDelegate write)
        {
            using var ms = new MemoryStream(64);
            ms.Write(new byte[5], 0, 5);
            var bw = new MemoryStreamBufferWriter(ms);
            var w  = new MessagePackWriter(bw);
            write(ref w, bw); w.Flush();
            return Finalize(ms);
        }

        private static byte[] Finalize(MemoryStream ms)
        {
            int    len  = (int)ms.Position - 5;
            byte[] buf  = ms.GetBuffer();
            byte   ps   = GetRequiredBytesForLengthPrefix(len);
            WriteLengthAsVarInt(buf, 5 - ps, len);
            byte[] r = new byte[len + ps];
            Array.Copy(buf, 5 - ps, r, 0, r.Length);
            return r;
        }

        private void WriteValue(ref MessagePackWriter w, MemoryStreamBufferWriter bw, object item)
        {
            if (item == null) { w.WriteNil(); return; }
            w.Flush(); MessagePackSerializer.Serialize(item.GetType(), bw, item, _options);
        }

        private static void WriteString(ref MessagePackWriter w, string s)
        {
            if (s == null) { w.WriteNil(); return; }
            int len = System.Text.Encoding.UTF8.GetByteCount(s);
            byte[] buf = ArrayPool<byte>.Shared.Rent(len);
            try { System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, buf, 0); w.WriteString(new ReadOnlySpan<byte>(buf, 0, len)); }
            finally { ArrayPool<byte>.Shared.Return(buf); }
        }

        private static void WriteHeaders(ref MessagePackWriter w) => w.WriteMapHeader(0);

        // ── VarInt ────────────────────────────────────────────────────────────

        public static byte GetRequiredBytesForLengthPrefix(int length)
        {
            byte b = 0; do { length >>= 7; b++; } while (length > 0); return b;
        }

        public static int WriteLengthAsVarInt(byte[] data, int offset, int length)
        {
            do { byte c = (byte)(length & 0x7f); length >>= 7; if (length > 0) c |= 0x80; data[offset++] = c; } while (length > 0);
            return offset;
        }

        public static uint ReadVarInt(byte[] data, ref int offset)
        {
            uint len = 0; int n = 0; byte b;
            do { b = data[offset + n]; len |= (uint)(b & 0x7f) << (n * 7); n++; }
            while (offset + n < data.Length && (b & 0x80) != 0);
            offset += n; return len;
        }
    }
}
