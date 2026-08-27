using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// Just enough JSON for the archive file, and no more.
    ///
    /// <para><b>Why not <c>JsonUtility</c>.</b> It lives in UnityEngine, and the save's format
    /// may not — the two stores, the room record and <see cref="ArchiveFormat"/> are engine-free
    /// so the whole save can be exercised headlessly by <c>Tools/run-acceptance.sh save</c>
    /// without an editor. <c>JsonUtility</c> would also decide the shape of the file from the
    /// shape of the classes, which is exactly backwards: the file is a format with its own
    /// compatibility story, and the classes are free to move.</para>
    ///
    /// <para><b>Reading and writing are separate objects on purpose.</b> Writing is a stream —
    /// the archive is composed in one pass and never revisited — so a builder is the honest shape
    /// and costs one <see cref="StringBuilder"/>. Reading needs random access to a member that
    /// may be missing, which is a tree.</para>
    ///
    /// <para><b>Never throws on bad input.</b> A parse failure is a message and a false, because
    /// the one thing a save must not do is take the game down with it — see
    /// <see cref="ArchiveFormat"/> for what is done with the answer.</para>
    /// </summary>
    public static class Json
    {
        /// <summary>How deep a document may nest before it is refused. The archive is four deep;
        /// anything approaching this is a file built to break the parser, and a stack overflow
        /// cannot be caught.</summary>
        public const int MaxDepth = 32;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- writing -----------------------------------------------------------------------

        /// <summary>
        /// A JSON document, built in one pass and pretty-printed — two spaces a level, one member
        /// a line. Nobody has to read a save; everybody eventually does, and a save that can be
        /// read in a text editor is a save whose bugs can be seen.
        /// </summary>
        public sealed class Writer
        {
            readonly StringBuilder text = new StringBuilder();
            readonly List<bool> empty = new List<bool>();   // per depth: nothing written yet

            public Writer OpenObject() { return Open('{'); }
            public Writer OpenArray() { return Open('['); }

            /// <summary>Closes whichever is open. An empty one closes on its own line rather than
            /// as <c>{}</c>: it is one more line and one less special case.</summary>
            public Writer Close(char bracket)
            {
                if (empty.Count > 0) empty.RemoveAt(empty.Count - 1);

                text.Append('\n');
                Indent();
                text.Append(bracket);
                return this;
            }

            public Writer CloseObject() { return Close('}'); }
            public Writer CloseArray() { return Close(']'); }

            /// <summary>A member name. The value follows as its own call, so an object member and
            /// an array element go through the same writers.</summary>
            public Writer Name(string name)
            {
                Separate();
                WriteString(name);
                text.Append(": ");
                return this;
            }

            public Writer Value(string value)
            {
                Separate();
                if (value == null) text.Append("null");
                else WriteString(value);
                return this;
            }

            public Writer Value(double value)
            {
                Separate();

                // JSON has no NaN and no infinity. Neither can reach a pose the player made, and
                // a file carrying one would be unreadable by anything else — so it is written as
                // a number that is merely wrong rather than as a document that is broken.
                text.Append(double.IsNaN(value) || double.IsInfinity(value)
                    ? "0"
                    : value.ToString("R", Inv));
                return this;
            }

            public Writer Value(int value)
            {
                Separate();
                text.Append(value.ToString(Inv));
                return this;
            }

            public Writer Value(bool value)
            {
                Separate();
                text.Append(value ? "true" : "false");
                return this;
            }

            public Writer Field(string name, string value) { return Name(name).Value(value); }
            public Writer Field(string name, double value) { return Name(name).Value(value); }
            public Writer Field(string name, int value) { return Name(name).Value(value); }
            public Writer Field(string name, bool value) { return Name(name).Value(value); }

            public override string ToString() { return text.ToString() + "\n"; }

            Writer Open(char bracket)
            {
                Separate();
                text.Append(bracket);
                empty.Add(true);
                return this;
            }

            /// <summary>The comma, the newline and the indent — every value's preamble, so no
            /// caller has to remember whether it is the first one.</summary>
            void Separate()
            {
                // A name has just been written: the value belongs on the same line.
                if (text.Length > 0 && text[text.Length - 1] == ' ') return;

                if (empty.Count == 0) return;

                if (empty[empty.Count - 1]) empty[empty.Count - 1] = false;
                else text.Append(',');

                text.Append('\n');
                Indent();
            }

            void Indent() { text.Append(' ', empty.Count * 2); }

            void WriteString(string value)
            {
                text.Append('"');
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '"':  text.Append("\\\""); break;
                        case '\\': text.Append("\\\\"); break;
                        case '\n': text.Append("\\n"); break;
                        case '\r': text.Append("\\r"); break;
                        case '\t': text.Append("\\t"); break;
                        case '\b': text.Append("\\b"); break;
                        case '\f': text.Append("\\f"); break;
                        default:
                            if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", Inv));
                            else text.Append(c);
                            break;
                    }
                }
                text.Append('"');
            }
        }

        // ---- reading -----------------------------------------------------------------------

        /// <summary>
        /// A parsed value. <b>Missing is not an error</b>: an absent member reads as
        /// <see cref="Missing"/>, every accessor takes the answer to give when a value is not
        /// what was hoped for, and so a reader is a list of questions rather than a tower of
        /// null checks. What is genuinely absent and what is genuinely wrong are the caller's to
        /// tell apart, with <see cref="Has"/> and <see cref="IsNumber"/>.
        /// </summary>
        public sealed class Value
        {
            public enum Kind { Missing, Null, Bool, Number, String, Array, Object }

            /// <summary>The answer to every question about something that is not there. One
            /// shared instance: it is immutable and it is asked for constantly.</summary>
            public static readonly Value Missing = new Value(Kind.Missing);

            public readonly Kind Type;

            readonly bool flag;
            readonly double number;
            readonly string text;
            readonly List<Value> items;
            readonly List<string> names;

            Value(Kind type) { Type = type; }
            Value(bool value) { Type = Kind.Bool; flag = value; }
            Value(double value) { Type = Kind.Number; number = value; }
            Value(string value) { Type = Kind.String; text = value; }

            Value(Kind type, List<Value> items, List<string> names)
            {
                Type = type;
                this.items = items;
                this.names = names;
            }

            public static Value Of(bool value) { return new Value(value); }
            public static Value Of(double value) { return new Value(value); }
            public static Value Of(string value) { return new Value(value); }
            public static Value Null() { return new Value(Kind.Null); }
            public static Value Array(List<Value> items) { return new Value(Kind.Array, items, null); }

            public static Value Object(List<string> names, List<Value> values)
            {
                return new Value(Kind.Object, values, names);
            }

            public bool IsObject { get { return Type == Kind.Object; } }
            public bool IsArray { get { return Type == Kind.Array; } }
            public bool IsNumber { get { return Type == Kind.Number; } }
            public bool IsString { get { return Type == Kind.String; } }
            public bool Exists { get { return Type != Kind.Missing && Type != Kind.Null; } }

            /// <summary>Elements of an array, or nothing at all for anything else — so a caller
            /// may walk a member that turned out not to be a list without asking first.</summary>
            public IReadOnlyList<Value> Items { get { return items != null ? (IReadOnlyList<Value>)items : Empty; } }

            public int Count { get { return items != null ? items.Count : 0; } }

            /// <summary>A member by name, or <see cref="Missing"/>. Linear: an archive object has
            /// a handful of members and a dictionary per object would cost more than it saves.
            /// </summary>
            public Value this[string name]
            {
                get
                {
                    if (names == null) return Missing;

                    for (int i = 0; i < names.Count; i++)
                        if (names[i] == name) return items[i];

                    return Missing;
                }
            }

            public bool Has(string name) { return this[name].Exists; }

            public string AsString(string fallback)
            {
                return Type == Kind.String ? text : fallback;
            }

            public double AsDouble(double fallback)
            {
                return Type == Kind.Number ? number : fallback;
            }

            public int AsInt(int fallback)
            {
                return Type == Kind.Number ? (int)number : fallback;
            }

            public bool AsBool(bool fallback)
            {
                return Type == Kind.Bool ? flag : fallback;
            }

            static readonly Value[] Empty = new Value[0];
        }

        /// <summary>
        /// Parses a whole document. False with a message when the text is not JSON at all —
        /// which the archive treats as "no save", never as "an empty save".
        /// </summary>
        public static bool TryParse(string text, out Value root, out string error)
        {
            root = Value.Missing;
            error = null;

            if (string.IsNullOrEmpty(text)) { error = "empty"; return false; }

            int at = 0;
            if (!ParseValue(text, ref at, 0, out root, out error)) return false;

            SkipSpace(text, ref at);
            if (at != text.Length)
            {
                error = "trailing text at " + at;
                return false;
            }
            return true;
        }

        static bool ParseValue(string s, ref int at, int depth, out Value value, out string error)
        {
            value = Value.Missing;
            error = null;

            if (depth > MaxDepth) { error = "nested too deep"; return false; }

            SkipSpace(s, ref at);
            if (at >= s.Length) { error = "ended early"; return false; }

            char c = s[at];
            switch (c)
            {
                case '{': return ParseObject(s, ref at, depth, out value, out error);
                case '[': return ParseArray(s, ref at, depth, out value, out error);
                case '"':
                {
                    string text;
                    if (!ParseString(s, ref at, out text, out error)) return false;
                    value = Value.Of(text);
                    return true;
                }
                case 't':
                    if (!Literal(s, ref at, "true", out error)) return false;
                    value = Value.Of(true);
                    return true;
                case 'f':
                    if (!Literal(s, ref at, "false", out error)) return false;
                    value = Value.Of(false);
                    return true;
                case 'n':
                    if (!Literal(s, ref at, "null", out error)) return false;
                    value = Value.Null();
                    return true;
                default:
                    return ParseNumber(s, ref at, out value, out error);
            }
        }

        static bool ParseObject(string s, ref int at, int depth, out Value value, out string error)
        {
            value = Value.Missing;
            var names = new List<string>();
            var values = new List<Value>();

            at++;                                   // '{'
            SkipSpace(s, ref at);

            if (at < s.Length && s[at] == '}')
            {
                at++;
                value = Value.Object(names, values);
                error = null;
                return true;
            }

            while (true)
            {
                SkipSpace(s, ref at);

                string name;
                if (at >= s.Length || s[at] != '"') { error = "expected a member name at " + at; return false; }
                if (!ParseString(s, ref at, out name, out error)) return false;

                SkipSpace(s, ref at);
                if (at >= s.Length || s[at] != ':') { error = "expected ':' at " + at; return false; }
                at++;

                Value member;
                if (!ParseValue(s, ref at, depth + 1, out member, out error)) return false;

                names.Add(name);
                values.Add(member);

                SkipSpace(s, ref at);
                if (at >= s.Length) { error = "unclosed object"; return false; }

                if (s[at] == ',') { at++; continue; }
                if (s[at] == '}')
                {
                    at++;
                    value = Value.Object(names, values);
                    return true;
                }

                error = "expected ',' or '}' at " + at;
                return false;
            }
        }

        static bool ParseArray(string s, ref int at, int depth, out Value value, out string error)
        {
            value = Value.Missing;
            var items = new List<Value>();

            at++;                                   // '['
            SkipSpace(s, ref at);

            if (at < s.Length && s[at] == ']')
            {
                at++;
                value = Value.Array(items);
                error = null;
                return true;
            }

            while (true)
            {
                Value item;
                if (!ParseValue(s, ref at, depth + 1, out item, out error)) return false;
                items.Add(item);

                SkipSpace(s, ref at);
                if (at >= s.Length) { error = "unclosed array"; return false; }

                if (s[at] == ',') { at++; continue; }
                if (s[at] == ']')
                {
                    at++;
                    value = Value.Array(items);
                    return true;
                }

                error = "expected ',' or ']' at " + at;
                return false;
            }
        }

        static bool ParseString(string s, ref int at, out string text, out string error)
        {
            text = null;
            error = null;

            at++;                                   // '"'
            var built = new StringBuilder();

            while (at < s.Length)
            {
                char c = s[at++];

                if (c == '"') { text = built.ToString(); return true; }

                if (c != '\\') { built.Append(c); continue; }

                if (at >= s.Length) break;

                char escape = s[at++];
                switch (escape)
                {
                    case '"':  built.Append('"');  break;
                    case '\\': built.Append('\\'); break;
                    case '/':  built.Append('/');  break;
                    case 'n':  built.Append('\n'); break;
                    case 'r':  built.Append('\r'); break;
                    case 't':  built.Append('\t'); break;
                    case 'b':  built.Append('\b'); break;
                    case 'f':  built.Append('\f'); break;
                    case 'u':
                    {
                        if (at + 4 > s.Length) { error = "truncated \\u escape"; return false; }

                        int code;
                        if (!int.TryParse(s.Substring(at, 4), NumberStyles.HexNumber, Inv, out code))
                        { error = "bad \\u escape at " + at; return false; }

                        built.Append((char)code);
                        at += 4;
                        break;
                    }
                    default:
                        error = "unknown escape \\" + escape;
                        return false;
                }
            }

            error = "unclosed string";
            return false;
        }

        static bool ParseNumber(string s, ref int at, out Value value, out string error)
        {
            value = Value.Missing;
            int start = at;

            if (at < s.Length && (s[at] == '-' || s[at] == '+')) at++;
            while (at < s.Length && (char.IsDigit(s[at]) || s[at] == '.'
                                     || s[at] == 'e' || s[at] == 'E'
                                     || s[at] == '-' || s[at] == '+')) at++;

            double number;
            if (at == start || !double.TryParse(s.Substring(start, at - start),
                                                NumberStyles.Float, Inv, out number))
            {
                error = "not a number at " + start;
                return false;
            }

            value = Value.Of(number);
            error = null;
            return true;
        }

        static bool Literal(string s, ref int at, string word, out string error)
        {
            if (at + word.Length <= s.Length && string.CompareOrdinal(s, at, word, 0, word.Length) == 0)
            {
                at += word.Length;
                error = null;
                return true;
            }

            error = "expected " + word + " at " + at;
            return false;
        }

        static void SkipSpace(string s, ref int at)
        {
            while (at < s.Length && (s[at] == ' ' || s[at] == '\t' || s[at] == '\n' || s[at] == '\r')) at++;
        }
    }
}
