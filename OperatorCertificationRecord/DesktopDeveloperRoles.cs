using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace OperatorTrainingRecord
{
    /// <summary>Read-only compatibility reader for the web's authoritative Developer role store.</summary>
    internal static class DesktopDeveloperRoles
    {
        private const int MaxBytes = 5 * 1024 * 1024;
        private const string Unavailable = "Developer report exclusions are unavailable. Configure DeveloperRoles:StorePath to the shared Developer role store and try again.";

        internal static string[] ReadRoles(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) throw new FormatException("A role store path is required.");
                if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new FormatException("The role store must be a regular file.");
                string json;
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    if (input.Length > MaxBytes) throw new FormatException("The role store exceeds its storage limit.");
                    using (var memory = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (memory.Length + count > MaxBytes) throw new FormatException("The role store exceeds its storage limit.");
                            memory.Write(buffer, 0, count);
                        }
                        json = new UTF8Encoding(false, true).GetString(memory.ToArray());
                    }
                }
                if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
                JsonSyntax.Validate(json);
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 64 };
                var root = serializer.DeserializeObject(json);
                object[] members = root as object[];
                if (members == null)
                {
                    var envelope = Map(root);
                    if (Number(envelope, "schemaVersion") != 1 || Number(envelope, "membershipRevision") < 0)
                        throw new FormatException("The role envelope version is invalid.");
                    members = Array(envelope, "members");
                    ValidateEvents(Array(envelope, "pendingAuditEvents"));
                    var receipts = Get(envelope, "consumedCommands");
                    if (receipts != null) ValidateReceipts(AsArray(receipts));
                }
                if (members.Any(member => !(member is string) || !DesktopReportQuery.ValidCode((string)member)))
                    throw new FormatException("The role membership is invalid.");
                return members.Cast<string>().Select(code => code.Trim()).Distinct(StringComparer.Ordinal)
                    .OrderBy(code => code, StringComparer.Ordinal).ToArray();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is FormatException || error is InvalidOperationException ||
                error is System.Security.SecurityException || error is NotSupportedException)
            {
                throw new IOException(Unavailable, error);
            }
        }

        private static bool ValidCode(string value) => DesktopReportQuery.ValidCode(value);

        private static void ValidateEvents(object[] events)
        {
            if (events.Length > 1000) throw new FormatException("The role audit queue exceeds its limit.");
            var identifiers = new HashSet<Guid>();
            foreach (var item in events)
            {
                var record = Map(item);
                var uid = Identifier(record, "eventUid");
                var actor = Map(Get(record, "actor"));
                var changes = Array(record, "changes");
                var action = Text(record, "action");
                bool isAdmin = Boolean(actor, "isAdmin", true);
                bool isDeveloper = Boolean(actor, "isDeveloper", true);
                bool admin = isAdmin || isDeveloper;
                string field = (action == "admin-enable" || action == "admin-remove") && admin ? "Admin" :
                    (action == "developer-enable" || action == "developer-remove") && isDeveloper ? "Developer" : null;
                if (!identifiers.Add(uid) || Text(record, "originKind") != "RoleFile" || Identifier(record, "originId") != uid ||
                    Text(record, "part", "0") != "0" || Text(record, "category") != "roles" || Text(record, "outcome") != "Committed" ||
                    !ValidCode(Text(actor, "actorCode")) || Identifier(actor, "operationId") == Guid.Empty || field == null || changes.Length != 1)
                    throw new FormatException("The role audit event is invalid.");
                var change = Map(changes[0]);
                var fields = Array(change, "fields");
                if (Text(change, "entityType") != "access-role" || !ValidCode(Text(change, "employeeCode")) ||
                    !Boolean(change, "beforeExists") || !Boolean(change, "afterExists") || fields.Length != 1)
                    throw new FormatException("The role audit change is invalid.");
                var delta = Map(fields[0]);
                bool enabled = action.EndsWith("-enable", StringComparison.Ordinal);
                if (Text(delta, "field") != field || Text(delta, "before") != (enabled ? "false" : "true") ||
                    Text(delta, "after") != (enabled ? "true" : "false") || Boolean(delta, "redacted"))
                    throw new FormatException("The role audit delta is invalid.");
                ValidateRecordTypes(record, actor, change);
            }
        }

        private static void ValidateRecordTypes(IDictionary<string, object> record, IDictionary<string, object> actor, IDictionary<string, object> change)
        {
            // Also reject invalid known JSON field types, as the web's typed deserializer does.
            foreach (var name in new[] { "summary", "commandHash", "requestHash" }) Text(record, name);
            foreach (var name in new[] { "actorName", "requestId", "route" }) Text(actor, name);
            foreach (var name in new[] { "entityKey", "skillName", "tableKind", "label" }) Text(change, name);
            if (Get(record, "recoveryActionId") != null) Identifier(record, "recoveryActionId", true);
            if (Contains(record, "occurredUtc")) Timestamp(record, "occurredUtc", false);
        }

        private static void ValidateReceipts(object[] receipts)
        {
            if (receipts.Length > 10000) throw new FormatException("The role receipts exceed their limit.");
            var identifiers = new HashSet<Guid>();
            foreach (var item in receipts)
            {
                var receipt = Map(item);
                var role = Text(receipt, "role");
                var outcome = Text(receipt, "outcome");
                var hash = Text(receipt, "intentHash");
                Boolean(receipt, "enabled");
                if (!identifiers.Add(Identifier(receipt, "commandId")) || !ValidCode(Text(receipt, "actorCode")) ||
                    !ValidCode(Text(receipt, "targetCode")) || (role != "Admin" && role != "Developer") || hash == null ||
                    hash.Length != 64 || !hash.All(Uri.IsHexDigit) ||
                    (outcome != "Updated" && outcome != "Unchanged" && outcome != "LastMember"))
                    throw new FormatException("The role receipt is invalid.");
                Timestamp(receipt, "expiresUtc", true);
            }
        }

        private static IDictionary<string, object> Map(object value)
        {
            var result = value as IDictionary<string, object>;
            if (result == null) throw new FormatException("A JSON object was expected.");
            return result;
        }

        private static object Get(IDictionary<string, object> value, string name)
        {
            object found = null;
            foreach (var property in value)
                if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)) found = property.Value;
            return found;
        }

        private static bool Contains(IDictionary<string, object> value, string name) =>
            value.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

        private static string Text(IDictionary<string, object> value, string name, string fallback = null)
        {
            var raw = Get(value, name);
            if (raw == null) return Contains(value, name) ? null : fallback;
            if (!(raw is string)) throw new FormatException("A JSON string was expected.");
            return (string)raw;
        }

        private static object[] AsArray(object value) => value as object[] ?? throw new FormatException("A JSON array was expected.");
        private static object[] Array(IDictionary<string, object> value, string name) => AsArray(Get(value, name));

        private static long Number(IDictionary<string, object> value, string name)
        {
            var raw = Get(value, name);
            if (raw == null && !Contains(value, name)) return 0;
            if (!(raw is int) && !(raw is long)) throw new FormatException("A JSON integer was expected.");
            return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }

        private static bool Boolean(IDictionary<string, object> value, string name, bool nullable = false)
        {
            var raw = Get(value, name);
            if (raw == null && (nullable || !Contains(value, name))) return false;
            if (!(raw is bool)) throw new FormatException("A JSON boolean was expected.");
            return (bool)raw;
        }

        private static Guid Identifier(IDictionary<string, object> value, string name, bool allowEmpty = false)
        {
            Guid result;
            if (!Guid.TryParse(Text(value, name), out result) || (!allowEmpty && result == Guid.Empty))
                throw new FormatException("A valid JSON identifier was expected.");
            return result;
        }

        private static void Timestamp(IDictionary<string, object> value, string name, bool required)
        {
            DateTimeOffset result;
            string text = Text(value, name);
            if (text == null || !Regex.IsMatch(text, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}(?:T[0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,16})?)?(?:Z|[+-][0-9]{2}:[0-9]{2})?)?\z", RegexOptions.CultureInvariant) ||
                !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result) ||
                (required && result == default(DateTimeOffset))) throw new FormatException("A valid JSON timestamp was expected.");
        }

        // JavaScriptSerializer accepts JavaScript extensions. Guard its input with strict JSON grammar.
        private sealed class JsonSyntax
        {
            private readonly string text;
            private int position;
            private JsonSyntax(string text) { this.text = text; }

            internal static void Validate(string text)
            {
                var syntax = new JsonSyntax(text);
                syntax.Value(0);
                syntax.WhiteSpace();
                if (syntax.position != text.Length) throw new FormatException("Invalid JSON content.");
            }

            private void Value(int depth)
            {
                WhiteSpace();
                if (depth > 64 || position == text.Length) throw new FormatException("Invalid JSON value.");
                char token = text[position];
                if (token == '"') String();
                else if (token == '{') Container(true, depth + 1);
                else if (token == '[') Container(false, depth + 1);
                else if (token == 't') Literal("true");
                else if (token == 'f') Literal("false");
                else if (token == 'n') Literal("null");
                else NumberToken();
            }

            private void Container(bool isObject, int depth)
            {
                position++;
                WhiteSpace();
                char close = isObject ? '}' : ']';
                if (Take(close)) return;
                while (true)
                {
                    if (isObject)
                    {
                        String();
                        WhiteSpace();
                        if (!Take(':')) throw new FormatException("Invalid JSON property.");
                    }
                    Value(depth);
                    WhiteSpace();
                    if (Take(close)) return;
                    if (!Take(',')) throw new FormatException("Invalid JSON delimiter.");
                    WhiteSpace();
                }
            }

            private void String()
            {
                if (!Take('"')) throw new FormatException("A quoted JSON string was expected.");
                while (position < text.Length)
                {
                    char character = text[position++];
                    if (character == '"') return;
                    if (character < 32) throw new FormatException("Invalid JSON string character.");
                    if (character != '\\') continue;
                    if (position == text.Length) break;
                    char escape = text[position++];
                    if (escape == 'u')
                    {
                        for (int index = 0; index < 4; index++)
                            if (position == text.Length || !Uri.IsHexDigit(text[position++])) throw new FormatException("Invalid JSON unicode escape.");
                    }
                    else if ("\"\\/bfnrt".IndexOf(escape) < 0) throw new FormatException("Invalid JSON escape.");
                }
                throw new FormatException("Unterminated JSON string.");
            }

            private void NumberToken()
            {
                Take('-');
                if (!Take('0')) Digits(true);
                if (Take('.')) Digits(true);
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    Digits(true);
                }
            }

            private void Digits(bool required)
            {
                int start = position;
                while (position < text.Length && text[position] >= '0' && text[position] <= '9') position++;
                if (required && position == start) throw new FormatException("Invalid JSON number.");
            }

            private void Literal(string literal)
            {
                foreach (char character in literal)
                    if (!Take(character)) throw new FormatException("Invalid JSON literal.");
            }

            private bool Take(char character)
            {
                if (position == text.Length || text[position] != character) return false;
                position++;
                return true;
            }

            private void WhiteSpace()
            {
                while (position < text.Length && (text[position] == ' ' || text[position] == '\t' || text[position] == '\r' || text[position] == '\n')) position++;
            }
        }
    }
}
