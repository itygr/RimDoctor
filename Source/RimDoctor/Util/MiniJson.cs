/*
 * MiniJSON — a tiny, dependency-free JSON parser/serializer.
 * Based on the public-domain / MIT implementation by Calvin Rien
 * (https://gist.github.com/darktable/1411710), itself derived from Patrick van
 * Bergen's port. RimWorld ships no JSON library, so RimDoctor bundles this to
 * parse logAdvice.json / communityRules.json and any refreshed rule data.
 *
 * Deserialize returns nested Dictionary<string,object> / List<object> /
 * string / double / long / bool / null.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RimDoctor.Util
{
    public static class Json
    {
        public static object Deserialize(string json)
        {
            if (json == null)
                return null;
            return Parser.Parse(json);
        }

        sealed class Parser : IDisposable
        {
            const string WordBreak = "{}[],:\"";

            enum Token { None, CurlyOpen, CurlyClose, SquareOpen, SquareClose, Colon, Comma, String, Number, True, False, Null }

            System.IO.StringReader json;

            Parser(string jsonString) { json = new System.IO.StringReader(jsonString); }

            public static object Parse(string jsonString)
            {
                using (var instance = new Parser(jsonString))
                    return instance.ParseValue();
            }

            public void Dispose() { json.Dispose(); json = null; }

            Dictionary<string, object> ParseObject()
            {
                var table = new Dictionary<string, object>();
                json.Read(); // {
                while (true)
                {
                    switch (NextToken)
                    {
                        case Token.None: return null;
                        case Token.Comma: continue;
                        case Token.CurlyClose: return table;
                        default:
                            string name = ParseString();
                            if (name == null) return null;
                            if (NextToken != Token.Colon) return null;
                            json.Read(); // :
                            table[name] = ParseValue();
                            break;
                    }
                }
            }

            List<object> ParseArray()
            {
                var array = new List<object>();
                json.Read(); // [
                bool parsing = true;
                while (parsing)
                {
                    Token nextToken = NextToken;
                    switch (nextToken)
                    {
                        case Token.None: return null;
                        case Token.Comma: continue;
                        case Token.SquareClose: parsing = false; break;
                        default: array.Add(ParseByToken(nextToken)); break;
                    }
                }
                return array;
            }

            object ParseValue() => ParseByToken(NextToken);

            object ParseByToken(Token token)
            {
                switch (token)
                {
                    case Token.String: return ParseString();
                    case Token.Number: return ParseNumber();
                    case Token.CurlyOpen: return ParseObject();
                    case Token.SquareOpen: return ParseArray();
                    case Token.True: return true;
                    case Token.False: return false;
                    case Token.Null: return null;
                    default: return null;
                }
            }

            string ParseString()
            {
                var s = new StringBuilder();
                json.Read(); // opening quote
                bool parsing = true;
                while (parsing)
                {
                    if (json.Peek() == -1) break;
                    char c = NextChar;
                    switch (c)
                    {
                        case '"': parsing = false; break;
                        case '\\':
                            if (json.Peek() == -1) { parsing = false; break; }
                            char escaped = NextChar;
                            switch (escaped)
                            {
                                case '"': s.Append('"'); break;
                                case '\\': s.Append('\\'); break;
                                case '/': s.Append('/'); break;
                                case 'b': s.Append('\b'); break;
                                case 'f': s.Append('\f'); break;
                                case 'n': s.Append('\n'); break;
                                case 'r': s.Append('\r'); break;
                                case 't': s.Append('\t'); break;
                                case 'u':
                                    var hex = new char[4];
                                    for (int i = 0; i < 4; i++) hex[i] = NextChar;
                                    s.Append((char)Convert.ToInt32(new string(hex), 16));
                                    break;
                            }
                            break;
                        default: s.Append(c); break;
                    }
                }
                return s.ToString();
            }

            object ParseNumber()
            {
                string number = NextWord;
                if (number.IndexOf('.') == -1 && number.IndexOf('E') == -1 && number.IndexOf('e') == -1)
                {
                    long.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out long parsedInt);
                    return parsedInt;
                }
                double.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedDouble);
                return parsedDouble;
            }

            void EatWhitespace()
            {
                while (json.Peek() != -1 && char.IsWhiteSpace((char)json.Peek()))
                    json.Read();
            }

            char NextChar => Convert.ToChar(json.Read());

            string NextWord
            {
                get
                {
                    var word = new StringBuilder();
                    while (json.Peek() != -1 && WordBreak.IndexOf((char)json.Peek()) == -1)
                    {
                        word.Append(NextChar);
                        if (json.Peek() == -1) break;
                    }
                    return word.ToString();
                }
            }

            Token NextToken
            {
                get
                {
                    EatWhitespace();
                    if (json.Peek() == -1) return Token.None;
                    switch ((char)json.Peek())
                    {
                        case '{': return Token.CurlyOpen;
                        case '}': json.Read(); return Token.CurlyClose;
                        case '[': return Token.SquareOpen;
                        case ']': json.Read(); return Token.SquareClose;
                        case ',': json.Read(); return Token.Comma;
                        case '"': return Token.String;
                        case ':': return Token.Colon;
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                        case '-': return Token.Number;
                    }
                    switch (NextWord)
                    {
                        case "false": return Token.False;
                        case "true": return Token.True;
                        case "null": return Token.Null;
                    }
                    return Token.None;
                }
            }
        }

        // ----- Small typed accessors so callers don't cast everywhere -----

        public static Dictionary<string, object> AsObject(object o) => o as Dictionary<string, object>;
        public static List<object> AsList(object o) => o as List<object>;

        public static string Str(Dictionary<string, object> o, string key, string fallback = null)
        {
            if (o != null && o.TryGetValue(key, out var v) && v != null)
                return v.ToString();
            return fallback;
        }

        public static IEnumerable<string> StrList(object o)
        {
            var list = o as List<object>;
            if (list == null) yield break;
            foreach (var item in list)
                if (item != null) yield return item.ToString();
        }
    }
}
