#if SIGNALRLITE_NEWTONSOFT_JSON
// IEncoder implementation using Newtonsoft.Json (Json.NET).
// Requires: Newtonsoft.Json (com.unity.nuget.newtonsoft-json or standalone).
// Enable with scripting define: SIGNALRLITE_NEWTONSOFT_JSON
//
// Usage:
//   new JsonProtocol(new JsonDotNetEncoder())
//   new JsonProtocol(new JsonDotNetEncoder(mySettings))

using System;
using System.Text;

namespace SignalRLite.Encoders
{
    /// <summary>
    /// <see cref="IEncoder"/> implementation that uses Newtonsoft.Json.
    /// Plug into <see cref="JsonProtocol"/> for richer JSON support (nullable, enums, etc.).
    /// </summary>
    public sealed class JsonDotNetEncoder : IEncoder
    {
        private readonly Newtonsoft.Json.JsonSerializerSettings _settings;

        public JsonDotNetEncoder() { }

        public JsonDotNetEncoder(Newtonsoft.Json.JsonSerializerSettings settings)
        {
            _settings = settings;
        }

        public object ConvertTo(Type toType, object obj)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(obj, _settings);
            return Newtonsoft.Json.JsonConvert.DeserializeObject(json, toType, _settings);
        }

        public T DecodeAs<T>(byte[] data, int offset, int count)
        {
            using var reader    = new System.IO.StreamReader(
                                      new System.IO.MemoryStream(data, offset, count));
            using var jsonReader = new Newtonsoft.Json.JsonTextReader(reader);
            return Newtonsoft.Json.JsonSerializer.CreateDefault(_settings).Deserialize<T>(jsonReader);
        }

        public string Encode<T>(T value)
            => Newtonsoft.Json.JsonConvert.SerializeObject(value, _settings);
    }
}
#endif
