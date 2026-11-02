using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;

namespace OperatorTrainingRecord
{
    public static class DesktopPhotoCommand
    {
        public static string CreateLegacyPhotoPath(string directory, string employeeCode, string extension)
        {
            if (string.IsNullOrWhiteSpace(employeeCode) || employeeCode.Length > 10)
                throw new ArgumentException("Invalid employee code.", "employeeCode");
            foreach (char character in employeeCode)
                if (!((character >= '0' && character <= '9') || (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z')))
                    throw new ArgumentException("Employee codes in filenames must be alphanumeric.", "employeeCode");
            extension = (extension ?? "").ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".gif" && extension != ".bmp")
                throw new ArgumentException("Unsupported photo extension.", "extension");
            var result = Path.Combine(directory, employeeCode + "-" + Guid.NewGuid().ToString("N").Substring(0, 12) + extension);
            if (result.Length > 50) throw new IOException("The legacy photo path exceeds its storage limit.");
            return result;
        }

        public static void CopyNewLegacyPhoto(string source, string destination)
        {
            bool created = false;
            try
            {
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    input.CopyTo(output);
                }
            }
            catch
            {
                if (created)
                {
                    try { File.Delete(destination); }
                    catch (Exception cleanupError) { System.Diagnostics.Trace.TraceError("Partial photo cleanup failed ({0}).", cleanupError.GetType().Name); }
                }
                throw;
            }
        }

        public static SqlCommand ForLegacyPhotoUpdate(
            string employeeCode,
            string legacyPhoto,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(employeeCode) || employeeCode.Length > 10)
                throw new ArgumentException("Employee code must contain one to ten characters.", "employeeCode");
            if (string.IsNullOrWhiteSpace(legacyPhoto) || legacyPhoto.Length > 50)
                throw new ArgumentException("The legacy photo path exceeds its storage limit.", "legacyPhoto");
            if (connection == null) throw new ArgumentNullException("connection");
            var command = new SqlCommand(
                "UPDATE dbo.tblEmployee SET Photo=@legacyPhoto WHERE EmpCode=@employeeCode",
                connection, transaction);
            command.Parameters.Add("@employeeCode", SqlDbType.NVarChar, 10).Value = employeeCode;
            command.Parameters.Add("@legacyPhoto", SqlDbType.NVarChar, 50).Value = legacyPhoto;
            return command;
        }

        public static SqlCommand ForTransaction(
            SqlCommand template,
            SqlConnection connection,
            SqlTransaction transaction,
            bool clearLegacyPhoto)
        {
            if (template == null) throw new ArgumentNullException("template");
            if (connection == null) throw new ArgumentNullException("connection");

            var command = new SqlCommand(template.CommandText, connection, transaction);
            foreach (SqlParameter parameter in template.Parameters)
            {
                var copy = (SqlParameter)((ICloneable)parameter).Clone();
                if (string.Equals(copy.ParameterName, "@sEmpCode", StringComparison.OrdinalIgnoreCase))
                {
                    copy.SqlDbType = SqlDbType.NVarChar;
                    copy.Size = 10;
                    copy.Value = Convert.ToString(copy.Value, CultureInfo.InvariantCulture);
                }
                if (clearLegacyPhoto && string.Equals(copy.ParameterName, "@sPhoto", StringComparison.OrdinalIgnoreCase))
                    copy.Value = DBNull.Value;
                command.Parameters.Add(copy);
            }
            return command;
        }
    }
}
