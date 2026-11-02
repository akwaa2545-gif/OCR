using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    public sealed class StoredFileLease : IDisposable
    {
        private readonly IDisposable resources;

        public StoredFileLease(StoredFileMetadata metadata, Stream content, IDisposable resources)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Content = new IntegrityReadStream(content, metadata.ByteLength, metadata.Sha256);
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public StoredFileMetadata Metadata { get; }
        public Stream Content { get; }

        public void Dispose()
        {
            try
            {
                Content.Dispose();
            }
            finally
            {
                resources.Dispose();
            }
        }
    }

    internal sealed class IntegrityReadStream : Stream
    {
        private readonly Stream inner;
        private readonly bool leaveOpen;
        private readonly long expectedLength;
        private readonly byte[] expectedHash;
        private readonly SHA256 hash = SHA256.Create();
        private long length;
        private bool verified;

        internal IntegrityReadStream(
            Stream inner,
            long expectedLength,
            byte[] expectedHash,
            bool leaveOpen = false
        )
        {
            this.leaveOpen = leaveOpen;
            this.inner = inner;
            this.expectedLength = expectedLength;
            this.expectedHash = expectedHash;
        }

        private int Observe(byte[] buffer, int offset, int count)
        {
            if (verified)
            {
                if (count > 0)
                    throw new InvalidDataException(
                        "Stored attachment exceeds its declared length."
                    );
                return 0;
            }
            length += count;
            if (length > expectedLength)
                throw new InvalidDataException("Stored attachment length is corrupt.");
            if (count > 0)
                hash.TransformBlock(buffer, offset, count, null, 0);
            if (count == 0 || length == expectedLength)
            {
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                verified = true;
                if (length != expectedLength || !Equal(hash.Hash!, expectedHash))
                    throw new InvalidDataException("Stored attachment integrity check failed.");
            }
            return count;
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            var difference = left.Length ^ right.Length;
            for (var i = 0; i < left.Length && i < right.Length; i++)
                difference |= left[i] ^ right[i];
            return difference == 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            count == 0
                ? inner.Read(buffer, offset, count)
                : Observe(buffer, offset, inner.Read(buffer, offset, count));

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct
        )
        {
            var read = await inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
            return count == 0 ? read : Observe(buffer, offset, read);
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => expectedLength;
        public override long Position
        {
            get => length;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hash.Dispose();
                if (!leaveOpen)
                    inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
