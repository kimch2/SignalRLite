// Pluggable JSON serializer interface for JsonProtocol.
// Implement this interface to replace the built-in SimpleJson encoder.
//
// Macro                              Provides
// ─────────────────────────────────  ─────────────────────────────────────
// (none)                             JsonProtocol with SimpleJson (default)
// SIGNALRLITE_NEWTONSOFT_JSON        JsonDotNetEncoder (Newtonsoft.Json)
// SIGNALRLITE_MESSAGEPACK_CSHARP     MessagePackCSharpProtocol
// SIGNALRLITE_GAMEDEVWARE_MESSAGEPACK MessagePackProtocol (GameDevWare)

using System;

namespace SignalRLite.Encoders
{
    /// <summary>
    /// Pluggable JSON encoder for <see cref="JsonProtocol"/>.
    /// Implement to swap out SimpleJson for Newtonsoft.Json, LitJson, etc.
    /// </summary>
    public interface IEncoder
    {
        /// <summary>Converts <paramref name="obj"/> to <paramref name="toType"/>.</summary>
        object ConvertTo(Type toType, object obj);

        /// <summary>Deserializes JSON bytes to <typeparamref name="T"/>.</summary>
        T DecodeAs<T>(byte[] data, int offset, int count);

        /// <summary>Serializes <paramref name="value"/> to a JSON string.</summary>
        string Encode<T>(T value);
    }
}
