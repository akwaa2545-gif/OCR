using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    internal static class StagedContentIntegrity
    {
        internal static async Task VerifyAsync(
            Stream content,
            FileWriteRequest request,
            CancellationToken ct
        )
        {
            if (content == null || !content.CanRead || !content.CanSeek)
                throw new ArgumentException(
                    "Stage attachment content into a seekable stream before publication."
                );
            var start = content.Position;
            if (content.Length - start != request.ByteLength)
                throw new InvalidDataException("Staged attachment length changed.");
            var buffer = new byte[81920];
            long length = 0;
            try
            {
                using (var hash = SHA256.Create())
                {
                    int count;
                    while (
                        (
                            count = await content
                                .ReadAsync(buffer, 0, buffer.Length, ct)
                                .ConfigureAwait(false)
                        ) > 0
                    )
                    {
                        length += count;
                        hash.TransformBlock(buffer, 0, count, null, 0);
                    }
                    hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var expected = request.Sha256;
                    var difference = 0;
                    for (var i = 0; i < expected.Length; i++)
                        difference |= expected[i] ^ hash.Hash![i];
                    if (length != request.ByteLength || difference != 0)
                        throw new InvalidDataException(
                            "Staged attachment integrity changed before publication."
                        );
                }
            }
            finally
            {
                content.Position = start;
            }
        }
    }
}
