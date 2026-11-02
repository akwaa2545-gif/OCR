using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    public sealed class StagedEmployeeFile : IDisposable
    {
        internal StagedEmployeeFile(FileWriteRequest request, FileStream content, string directory)
        {
            Request = request;
            Content = new ReadOnlyStagedStream(content, directory);
        }

        public FileWriteRequest Request { get; }
        public Stream Content { get; }

        public void Dispose() => Content.Dispose();
    }

    public static class FileStager
    {
        public static async Task<StagedEmployeeFile> StageAsync(
            Stream source,
            string employeeCode,
            FileKind kind,
            string filename,
            string createdBy,
            FileStorageOptions options,
            CancellationToken ct,
            string? sourceKey = null
        )
        {
            if (source == null || !source.CanRead)
                throw new ArgumentException("A readable source is required.", nameof(source));
            FileValidation.EmployeeCode(employeeCode);
            FileValidation.Kind(kind);
            var limit =
                kind == FileKind.Photo ? options.MaxPhotoBytes : options.MaxCertificateBytes;
            var directory = PrivateStagingDirectory.Create();
            FileStream? staged = null;
            try
            {
                staged = new FileStream(
                    Path.Combine(directory, "content.tmp"),
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose
                );
                StagingFilePermissions.Restrict(staged);
                var buffer = new byte[81920];
                long length = 0;
                var prefix = new byte[16];
                var prefixLength = 0;
                using (var hash = SHA256.Create())
                {
                    int count;
                    while (
                        (
                            count = await source
                                .ReadAsync(buffer, 0, buffer.Length, ct)
                                .ConfigureAwait(false)
                        ) > 0
                    )
                    {
                        length += count;
                        if (length > limit)
                            throw new InvalidDataException(
                                "Attachment exceeds its configured size limit."
                            );
                        var take = Math.Min(count, prefix.Length - prefixLength);
                        if (take > 0)
                        {
                            Buffer.BlockCopy(buffer, 0, prefix, prefixLength, take);
                            prefixLength += take;
                        }
                        hash.TransformBlock(buffer, 0, count, null, 0);
                        await staged.WriteAsync(buffer, 0, count, ct).ConfigureAwait(false);
                    }
                    if (length == 0)
                        throw new InvalidDataException("Attachment is empty.");
                    hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var mime = DetectMime(prefix, prefixLength, kind);
                    staged.Position = 0;
                    return new StagedEmployeeFile(
                        new FileWriteRequest(
                            employeeCode,
                            kind,
                            filename,
                            mime,
                            length,
                            hash.Hash!,
                            createdBy,
                            sourceKey
                        ),
                        staged,
                        directory
                    );
                }
            }
            catch
            {
                try
                {
                    staged?.Dispose();
                }
                finally
                {
                    Directory.Delete(directory);
                }
                throw;
            }
        }

        private static string DetectMime(byte[] prefix, int length, FileKind kind)
        {
            if (kind == FileKind.Certificate)
            {
                if (length >= 5 && Encoding.ASCII.GetString(prefix, 0, 5) == "%PDF-")
                    return "application/pdf";
                throw new InvalidDataException("Certificate content is not a PDF.");
            }
            if (length >= 3 && prefix[0] == 255 && prefix[1] == 216 && prefix[2] == 255)
                return "image/jpeg";
            if (
                length >= 8
                && prefix[0] == 137
                && prefix[1] == 80
                && prefix[2] == 78
                && prefix[3] == 71
                && prefix[4] == 13
                && prefix[5] == 10
                && prefix[6] == 26
                && prefix[7] == 10
            )
                return "image/png";
            if (
                length >= 6
                && (
                    Encoding.ASCII.GetString(prefix, 0, 6) == "GIF87a"
                    || Encoding.ASCII.GetString(prefix, 0, 6) == "GIF89a"
                )
            )
                return "image/gif";
            if (length >= 2 && prefix[0] == 66 && prefix[1] == 77)
                return "image/bmp";
            throw new InvalidDataException(
                "Unsupported image content; JPEG, PNG, GIF or BMP is required."
            );
        }
    }
}
