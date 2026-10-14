using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace ChitakTech
{
    // KSP's Mono can leave blocking NamedPipeStream operations alive during domain
    // shutdown. These local Windows handles deliberately use synchronous NOWAIT
    // operations: no pending native I/O, overlapped buffers, or IO cancellation
    // callbacks can outlive an individual call. Waiting happens only in managed code.
    internal sealed class PollingPipeStream : Stream
    {
        private const int NoData = 232, PipeListening = 536, PipeConnected = 535;
        private const int PollMilliseconds = 25;
        private readonly SafeFileHandle handle;
        private int disposed;

        private PollingPipeStream(SafeFileHandle value) { handle = value; }

        internal static PollingPipeStream Connect(string name)
        {
            var pipe = CreateFile(@"\\.\pipe\" + name, 0xC0000000, 0,
                IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (pipe.IsInvalid) { int error = Marshal.GetLastWin32Error(); pipe.Dispose(); throw Failure(error); }
            uint mode = 1; // PIPE_READMODE_BYTE | PIPE_NOWAIT
            if (!SetNamedPipeHandleState(pipe, ref mode, IntPtr.Zero, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error(); pipe.Dispose(); throw Failure(error);
            }
            return new PollingPipeStream(pipe);
        }

        internal static PollingPipeStream CreateServer(string name)
        {
            // Duplex, byte stream, NOWAIT, reject remote clients, one instance.
            var pipe = CreateNamedPipe(@"\\.\pipe\" + name, 3, 1 | 8, 1, 8192, 8192, 0, IntPtr.Zero);
            if (pipe.IsInvalid) { int error = Marshal.GetLastWin32Error(); pipe.Dispose(); throw Failure(error); }
            return new PollingPipeStream(pipe);
        }

        internal void WaitForConnection()
        {
            while (true)
            {
                ThrowIfDisposed();
                if (ConnectNamedPipe(handle, IntPtr.Zero)) return;
                int error = Marshal.GetLastWin32Error();
                if (error == PipeConnected) return;
                if (error != PipeListening) throw Failure(error);
                Thread.Sleep(PollMilliseconds);
            }
        }

        private static IOException Failure(int error)
            => new IOException("Local pipe error " + error + ".", new Win32Exception(error));

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(PollingPipeStream));
        }

        private static void Validate(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
        }

        private int TryRead(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            // Pin only for this immediate native call, never across a wait.
            var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                uint read;
                if (ReadFile(handle, IntPtr.Add(pinned.AddrOfPinnedObject(), offset), (uint)count, out read, IntPtr.Zero))
                    return (int)read;
                int error = Marshal.GetLastWin32Error();
                if (error == NoData) return -1;
                if (error == 109) return 0; // ERROR_BROKEN_PIPE: EOF
                throw Failure(error);
            }
            finally { pinned.Free(); }
        }

        private int TryWrite(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                uint written;
                // Bound each write: some pipe implementations accept zero bytes if
                // a NOWAIT request exceeds their entire buffer quota.
                if (!WriteFile(handle, IntPtr.Add(pinned.AddrOfPinnedObject(), offset), (uint)Math.Min(count, 4096), out written, IntPtr.Zero))
                    throw Failure(Marshal.GetLastWin32Error());
                return (int)written;
            }
            finally { pinned.Free(); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Validate(buffer, offset, count);
            ThrowIfDisposed();
            if (count == 0) return 0;
            while (true)
            {
                int read = TryRead(buffer, offset, count);
                if (read >= 0) return read;
                Thread.Sleep(PollMilliseconds);
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            Validate(buffer, offset, count);
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            if (count == 0) return 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int read = TryRead(buffer, offset, count);
                if (read >= 0) return read;
                await Task.Delay(PollMilliseconds, token).ConfigureAwait(false);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Validate(buffer, offset, count);
            ThrowIfDisposed();
            while (count > 0)
            {
                int written = TryWrite(buffer, offset, count);
                offset += written; count -= written;
                if (written == 0) Thread.Sleep(PollMilliseconds);
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            Validate(buffer, offset, count);
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            while (count > 0)
            {
                token.ThrowIfCancellationRequested();
                int written = TryWrite(buffer, offset, count);
                offset += written; count -= written;
                if (written == 0) await Task.Delay(PollMilliseconds, token).ConfigureAwait(false);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) handle.Dispose();
            base.Dispose(disposing);
        }
        // Do not FlushFileBuffers: that waits for the peer to consume data.
        public override void Flush() { ThrowIfDisposed(); }
        public override bool CanRead => Volatile.Read(ref disposed) == 0;
        public override bool CanWrite => CanRead;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
            IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateNamedPipe(string name, uint openMode, uint pipeMode,
            uint instances, uint outputSize, uint inputSize, uint timeout, IntPtr security);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetNamedPipeHandleState(SafeFileHandle pipe, ref uint mode, IntPtr count, IntPtr timeout);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ConnectNamedPipe(SafeFileHandle pipe, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle pipe, IntPtr buffer, uint count, out uint read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle pipe, IntPtr buffer, uint count, out uint written, IntPtr overlapped);
    }
}
