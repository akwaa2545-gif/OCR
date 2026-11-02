using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    internal sealed class ReadOnlyStagedStream : Stream
    {
        private readonly Stream inner;
        private readonly string directory;

        internal ReadOnlyStagedStream(Stream inner, string directory)
        {
            this.inner = inner;
            this.directory = directory;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct
        ) => inner.ReadAsync(buffer, offset, count, ct);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush() => inner.Flush();

        public override void SetLength(long value) =>
            throw new NotSupportedException("Staged attachments are immutable.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Staged attachments are immutable.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    inner.Dispose();
                }
                finally
                {
                    if (Directory.Exists(directory))
                        Directory.Delete(directory);
                }
            }
            base.Dispose(disposing);
        }
    }
}
