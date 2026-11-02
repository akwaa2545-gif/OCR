using System;
using System.Data;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;

namespace OperatorTrainingRecord
{
    /// <summary>Archives and replaces exactly one stable qualification inside the file publication transaction.</summary>
    internal static class DesktopSkillMutation
    {
        private const string Columns = "EmpCode,ProcessName,OperatorTraining,TheoryTraining,OJTTraining,CertifiedDate,FullScore,ActualScore,TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,VerifierDate,DisQualifiedDate,DisQualifiedBy,TheReason,Remark,Download,OcrSkillRowId,CertificateFileId";
        internal static void Replace(SqlConnection connection, SqlTransaction transaction, string employee, Guid oldRow, Guid newRow,
            byte[] expectedVersion, IDictionary<string, object> values)
        {
            var columns = Columns;
            using (var probe = new SqlCommand("SELECT COL_LENGTH('dbo.tblQualified','PromotionId'),COL_LENGTH('dbo.tblQualified_Obsoleted','PromotionId'),COL_LENGTH('dbo.tblQualified','PromotedAt'),COL_LENGTH('dbo.tblQualified_Obsoleted','PromotedAt'),COL_LENGTH('dbo.tblQualified','PromotedBy'),COL_LENGTH('dbo.tblQualified_Obsoleted','PromotedBy')", connection, transaction))
            using (var reader = probe.ExecuteReader())
            {
                if (!reader.Read()) throw new InvalidOperationException("Unable to inspect archive schema.");
                var present = Enumerable.Range(0, 6).Select(i => !reader.IsDBNull(i)).ToArray();
                if (present.Any(v => v) && !present.All(v => v)) throw new InvalidOperationException("Promotion archive schema is incomplete.");
                if (present[0]) columns += ",PromotionId,PromotedAt,PromotedBy";
            }
            using (var archive = new SqlCommand("INSERT dbo.tblQualified_Obsoleted(" + columns + ") SELECT " + columns + " FROM dbo.tblQualified WITH (UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee AND OcrSkillRowId=@oldRow AND FileStorageVersion=@version AND (DisQualifiedBy='' OR DisQualifiedBy IS NULL)", connection, transaction))
            {
                AddOwner(archive, employee, oldRow, expectedVersion);
                if (archive.ExecuteNonQuery() != 1) throw new DBConcurrencyException("The selected skill changed. Reload and try again.");
            }
            var allowed = new HashSet<string>(new[] { "OperatorTraining", "TheoryTraining", "OJTTraining", "CertifiedDate", "FullScore", "ActualScore", "TestResult", "JudgmentTheory", "KnowledgeScore", "KnowledgeLevel", "SkillScore", "SkillLevel", "JudgmentPractice", "ExpiryDate", "Verifier", "Remark" }, StringComparer.Ordinal);
            if (values.Keys.Any(k => !allowed.Contains(k))) throw new ArgumentException("Unknown qualification field.");
            using (var update = new SqlCommand())
            {
                update.Connection = connection; update.Transaction = transaction;
                var assignments = values.Keys.Select((key, i) => "[" + key + "]=@value" + i).ToArray();
                update.CommandText = "UPDATE dbo.tblQualified SET " + string.Join(",", assignments) + ",VerifierDate=GETDATE(),OcrSkillRowId=@newRow,CertificateFileId=NULL WHERE EmpCode=@employee AND OcrSkillRowId=@oldRow AND FileStorageVersion=@version";
                AddOwner(update, employee, oldRow, expectedVersion);
                update.Parameters.Add("@newRow", SqlDbType.UniqueIdentifier).Value = newRow;
                var index = 0;
                foreach (var pair in values)
                {
                    var date = pair.Value is DateTime;
                    update.Parameters.Add("@value" + index++, date ? SqlDbType.Date : SqlDbType.NVarChar).Value = pair.Value ?? DBNull.Value;
                }
                if (update.ExecuteNonQuery() != 1) throw new DBConcurrencyException("The selected skill changed during publication.");
            }
        }

        private static void AddOwner(SqlCommand command, string employee, Guid row, byte[] version)
        {
            command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
            command.Parameters.Add("@oldRow", SqlDbType.UniqueIdentifier).Value = row;
            command.Parameters.Add("@version", SqlDbType.Binary, 8).Value = version;
        }

        internal static void UpdateLegacy(SqlConnection connection, SqlTransaction transaction, string employee, string process,
            Guid? row, byte[] version, bool hasSkillRowId, IDictionary<string, object> values, string legacyDownload)
        {
            var allowed = new HashSet<string>(new[] { "OperatorTraining", "TheoryTraining", "OJTTraining", "CertifiedDate", "FullScore", "ActualScore", "TestResult", "JudgmentTheory", "KnowledgeScore", "KnowledgeLevel", "SkillScore", "SkillLevel", "JudgmentPractice", "ExpiryDate", "Verifier", "Remark" }, StringComparer.Ordinal);
            if (values.Keys.Any(key => !allowed.Contains(key))) throw new ArgumentException("Unknown qualification field.");
            using (var command = new SqlCommand())
            {
                command.Connection = connection; command.Transaction = transaction;
                var assignments = values.Keys.Select((key, i) => "[" + key + "]=@value" + i).ToArray();
                command.CommandText = "UPDATE dbo.tblQualified SET " + string.Join(",", assignments) + ",VerifierDate=GETDATE(),Download=@download" +
                    (hasSkillRowId ? ",OcrSkillRowId=NEWID()" : "") + " WHERE EmpCode=@employee AND " +
                    (hasSkillRowId && row.HasValue ? "OcrSkillRowId=@row" : "ProcessName=@process") +
                    (version != null && row.HasValue ? " AND FileStorageVersion=@version" : "") +
                    " AND (DisQualifiedBy='' OR DisQualifiedBy IS NULL)";
                command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                command.Parameters.Add("@download", SqlDbType.NVarChar, 4000).Value = legacyDownload;
                if (hasSkillRowId && row.HasValue) command.Parameters.Add("@row", SqlDbType.UniqueIdentifier).Value = row.Value;
                else command.Parameters.Add("@process", SqlDbType.NVarChar, 100).Value = process;
                if (version != null && row.HasValue) command.Parameters.Add("@version", SqlDbType.Binary, 8).Value = version;
                var index = 0;
                foreach (var value in values.Values)
                    command.Parameters.Add("@value" + index++, value is DateTime ? SqlDbType.Date : SqlDbType.NVarChar).Value = value ?? DBNull.Value;
                var affected = command.ExecuteNonQuery();
                if (affected == 0 || (row.HasValue && affected != 1)) throw new DBConcurrencyException("The selected legacy skill changed. Reload and try again.");
            }
        }
    }
}
