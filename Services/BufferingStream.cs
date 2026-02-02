using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace SubsonicUWP.Services
{
    public sealed class BufferingStream : IRandomAccessStream
    {
        private readonly Stream _readStream;
        private readonly PlaybackService.DownloadContext _context;
        private ulong _position;
        private readonly bool _isSharedMemoryStream;
        private byte[] _copyBuffer;

        public BufferingStream(Stream readStream, PlaybackService.DownloadContext context)
        {
            _readStream = readStream;
            _context = context;
            _position = 0;
            _isSharedMemoryStream = readStream is MemoryStream;
        }

        public bool CanRead => true;
        public bool CanWrite => false;
        public ulong Position => _position;
        
        public ulong Size 
        {
            get => (ulong)_context.TotalBytes;
            set => throw new NotSupportedException();
        }

        public IInputStream GetInputStreamAt(ulong position)
        {
            Seek(position);
            return this;
        }

        public IOutputStream GetOutputStreamAt(ulong position) => throw new NotSupportedException();

        public void Seek(ulong position)
        {
            if (position > Size) position = Size;
            _position = position;
        }

        public IRandomAccessStream CloneStream()
        {
            if (_readStream is FileStream fs)
            {
                // FileStream: Create new handle
                var newFs = new FileStream(fs.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return new BufferingStream(newFs, _context);
            }
            else
            {
                // MemoryStream/Other: Share the instance (Thread-Safe via Lock in ReadAsync)
                // Note: We don't dispose shared streams in Clone
                return new BufferingStream(_readStream, _context);
            }
        }

        public void Dispose() 
        {
            if (!(_readStream is MemoryStream))
                _readStream?.Dispose();
        }

        public IAsyncOperationWithProgress<IBuffer, uint> ReadAsync(IBuffer buffer, uint count, InputStreamOptions options)
        {
            return AsyncInfo.Run<IBuffer, uint>(async (cancellationToken, progress) =>
            {
                // Wait Strategy
                // For RAM (Shared Memory), we read immediately (0 threshold) to avoid latency.
                // For Disk, we batch reads (64KB) to reduce IOPS.
                // UPDATED: Even for RAM, we want chunks (16KB) to avoid overhead of tiny reads.
                int MIN_READ_THRESHOLD = 16 * 1024; 

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    long available = _context.DownloadedBytes - (long)_position;
                    bool isComplete = _context.IsComplete;

                    if (available > 0 && (isComplete || available >= MIN_READ_THRESHOLD || available >= count))
                    {
                        int toRead = (int)Math.Min(available, count);
                        
                        // Lazy Allocation of Scratch Buffer
                        if (_copyBuffer == null || _copyBuffer.Length < toRead)
                        {
                            // Allocate slightly more than needed to avoid frequent resizing
                            _copyBuffer = new byte[Math.Max(toRead, 81920)]; 
                        }

                        int read = 0;

                        if (_isSharedMemoryStream)
                        {
                            // CRITICAL: Lock for MemoryStream safety (Pos + Read)
                            lock (_context.StreamLock) 
                            {
                                if (_readStream.Position != (long)_position)
                                    _readStream.Position = (long)_position;
                                read = _readStream.Read(_copyBuffer, 0, toRead);
                            }
                        }
                        else
                        {
                            // FileStream: Async preferred, own handle
                            if (_readStream.Position != (long)_position)
                                _readStream.Position = (long)_position;
                            read = await _readStream.ReadAsync(_copyBuffer, 0, toRead, cancellationToken).ConfigureAwait(false);
                        }

                        if (read == 0 && !isComplete)
                        {
                            await _context.WaitForDataAsync().ConfigureAwait(false);
                            continue;
                        }

                        // Copy to Target Buffer
                        if (read > 0)
                        {
                            buffer.Length = 0; 
                            using (var view = buffer.AsStream()) // Wraps the IBuffer
                            {
                                await view.WriteAsync(_copyBuffer, 0, read);
                            }
                            buffer.Length = (uint)read;
                        }
                        else
                        {
                            buffer.Length = 0;
                        }

                        _position += (ulong)read;
                        return buffer; // Return the filled buffer
                    }

                    if (isComplete && available <= 0)
                    {
                        buffer.Length = 0;
                        return buffer; // EOF
                    }

                    await _context.WaitForDataAsync().ConfigureAwait(false);
                }
            });
        }

        public IAsyncOperation<bool> FlushAsync() => Task.FromResult(true).AsAsyncOperation();
        public IAsyncOperationWithProgress<uint, uint> WriteAsync(IBuffer buffer) => throw new NotSupportedException();
    }
}
