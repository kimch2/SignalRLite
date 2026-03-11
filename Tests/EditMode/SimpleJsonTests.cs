using System.Collections.Generic;
using NUnit.Framework;
using SignalRLite.Utility;

namespace SignalRLite.Tests
{
    [TestFixture]
    public class SimpleJsonTests
    {
        // ── Parse ────────────────────────────────────────────────────────────

        [Test]
        public void Parse_Null_ReturnsNull()
        {
            Assert.IsNull(SimpleJson.Parse("null"));
        }

        [Test]
        public void Parse_BoolTrue_ReturnsTrue()
        {
            Assert.AreEqual(true, SimpleJson.Parse("true"));
        }

        [Test]
        public void Parse_BoolFalse_ReturnsFalse()
        {
            Assert.AreEqual(false, SimpleJson.Parse("false"));
        }

        [Test]
        public void Parse_Integer_ReturnsLong()
        {
            var result = SimpleJson.Parse("42");
            Assert.AreEqual(42L, result);
        }

        [Test]
        public void Parse_NegativeInteger()
        {
            Assert.AreEqual(-7L, SimpleJson.Parse("-7"));
        }

        [Test]
        public void Parse_Double_ReturnsDouble()
        {
            var result = SimpleJson.Parse("3.14");
            Assert.IsInstanceOf<double>(result);
            Assert.AreEqual(3.14, (double)result, 1e-9);
        }

        [Test]
        public void Parse_String_ReturnsString()
        {
            Assert.AreEqual("hello", SimpleJson.Parse("\"hello\""));
        }

        [Test]
        public void Parse_StringWithEscapes()
        {
            Assert.AreEqual("a\nb", SimpleJson.Parse("\"a\\nb\""));
            Assert.AreEqual("a\tb", SimpleJson.Parse("\"a\\tb\""));
            Assert.AreEqual("a\"b", SimpleJson.Parse("\"a\\\"b\""));
        }

        [Test]
        public void Parse_EmptyObject_ReturnsDictionary()
        {
            var result = SimpleJson.Parse("{}") as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_FlatObject()
        {
            var result = SimpleJson.Parse("{\"type\":6,\"key\":\"val\"}") as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(6L, result["type"]);
            Assert.AreEqual("val", result["key"]);
        }

        [Test]
        public void Parse_NestedObject()
        {
            var result = SimpleJson.Parse("{\"a\":{\"b\":1}}") as Dictionary<string, object>;
            Assert.IsNotNull(result);
            var inner = result["a"] as Dictionary<string, object>;
            Assert.IsNotNull(inner);
            Assert.AreEqual(1L, inner["b"]);
        }

        [Test]
        public void Parse_ArrayOfInts()
        {
            var result = SimpleJson.Parse("[1,2,3]") as List<object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(1L, result[0]);
            Assert.AreEqual(3L, result[2]);
        }

        [Test]
        public void Parse_ArrayOfStrings()
        {
            var result = SimpleJson.Parse("[\"a\",\"b\"]") as List<object>;
            Assert.IsNotNull(result);
            Assert.AreEqual("a", result[0]);
            Assert.AreEqual("b", result[1]);
        }

        [Test]
        public void Parse_MixedArray()
        {
            var result = SimpleJson.Parse("[1,\"two\",true,null]") as List<object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(1L,    result[0]);
            Assert.AreEqual("two", result[1]);
            Assert.AreEqual(true,  result[2]);
            Assert.IsNull(result[3]);
        }

        [Test]
        public void Parse_EmptyArray()
        {
            var result = SimpleJson.Parse("[]") as List<object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_ObjectWithNullValue()
        {
            var result = SimpleJson.Parse("{\"x\":null}") as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.IsNull(result["x"]);
        }

        [Test]
        public void Parse_WhitespaceAroundValues()
        {
            var result = SimpleJson.Parse("  {  \"k\"  :  42  }  ") as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(42L, result["k"]);
        }

        // ── Stringify ────────────────────────────────────────────────────────

        [Test]
        public void Stringify_Null()
        {
            Assert.AreEqual("null", SimpleJson.Stringify(null));
        }

        [Test]
        public void Stringify_Bool()
        {
            Assert.AreEqual("true",  SimpleJson.Stringify(true));
            Assert.AreEqual("false", SimpleJson.Stringify(false));
        }

        [Test]
        public void Stringify_Int()
        {
            Assert.AreEqual("42",  SimpleJson.Stringify(42));
            Assert.AreEqual("-7",  SimpleJson.Stringify(-7));
        }

        [Test]
        public void Stringify_Long()
        {
            Assert.AreEqual("1000000", SimpleJson.Stringify(1000000L));
        }

        [Test]
        public void Stringify_Double()
        {
            string s = SimpleJson.Stringify(3.14);
            Assert.AreEqual(3.14, double.Parse(s), 1e-9);
        }

        [Test]
        public void Stringify_String_QuotedAndEscaped()
        {
            Assert.AreEqual("\"hello\"",  SimpleJson.Stringify("hello"));
            Assert.AreEqual("\"a\\nb\"",  SimpleJson.Stringify("a\nb"));
            Assert.AreEqual("\"a\\\"b\"", SimpleJson.Stringify("a\"b"));
        }

        [Test]
        public void Stringify_Dictionary()
        {
            var dict = new Dictionary<string, object> { { "k", "v" } };
            string s = SimpleJson.Stringify(dict);
            Assert.AreEqual("{\"k\":\"v\"}", s);
        }

        [Test]
        public void Stringify_List()
        {
            var list = new List<object> { 1L, "two", true };
            string s = SimpleJson.Stringify(list);
            Assert.AreEqual("[1,\"two\",true]", s);
        }

        // ── Round-trip ───────────────────────────────────────────────────────

        [Test]
        public void RoundTrip_Object()
        {
            string original = "{\"name\":\"Alice\",\"score\":99,\"active\":true}";
            var parsed  = SimpleJson.Parse(original) as Dictionary<string, object>;
            string back = SimpleJson.Stringify(parsed);

            // Re-parse the stringified version and check values
            var check = SimpleJson.Parse(back) as Dictionary<string, object>;
            Assert.IsNotNull(check);
            Assert.AreEqual("Alice", check["name"]);
            Assert.AreEqual(99L,     check["score"]);
            Assert.AreEqual(true,    check["active"]);
        }

        [Test]
        public void RoundTrip_NestedArray()
        {
            string json   = "{\"ids\":[1,2,3]}";
            var    parsed = SimpleJson.Parse(json) as Dictionary<string, object>;
            string back   = SimpleJson.Stringify(parsed);

            var check = SimpleJson.Parse(back) as Dictionary<string, object>;
            Assert.IsNotNull(check);
            var ids = check["ids"] as List<object>;
            Assert.IsNotNull(ids);
            Assert.AreEqual(3, ids.Count);
        }
    }
}
