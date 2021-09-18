using System;
using System.IO;
using System.Threading;

namespace UserBackup
{
    public class BackupWorker
    {
        private readonly BackupCounters _counters;
        private readonly BackupQueue _queue;
        private readonly BackupLogger _logger;
        private readonly Thread _worker;
        private volatile bool _signalled;
        
        public bool IsAlive
        {
            get
            {
                return _worker.IsAlive; // True = worker still running
            }
        }

        public BackupWorker(BackupCounters counters, BackupQueue queue, BackupLogger logger, int t)
        {
            _signalled = false;
            _counters = counters;
            _queue = queue;
            _logger = logger;
            _worker = new Thread(() => Worker(t)) { IsBackground = true };
            _worker.Start();
        }
        private void Worker(int t) // Worker Thread, works on Queue
        {
            Console.WriteLine($"Worker Thread {t} is starting.");
            Span<byte> buffer = stackalloc byte[256000]; // 256kb Stack Allocated Copy buffer
            while (!_signalled)
            {
                if (_queue.TryDequeue(out var file))
                {
                    try
                    {
                        using (var fsIn = new FileStream(file.Source, FileMode.Open, FileAccess.Read))
                        using (var fsOut = new FileStream(file.Dest, FileMode.Create, FileAccess.Write))
                        {
                            int bytesRead;
                            while ((bytesRead = fsIn.Read(buffer)) > 0) // Read Source File
                            {
                                fsOut.Write(buffer.Slice(0, bytesRead)); // Copy to destination
                            }
                        }
                        Interlocked.Increment(ref _counters.CopiedFiles);
                        lock (_counters.CopiedSize_lock) // Lock access to be thread safe
                        {
                            _counters.CopiedSize += (double)file.Size / (double)1000000;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR copying file {file.Source}: {ex}", LogMessage.Error);
                    }
                }
                else Thread.Sleep(1); // Slow down CPU
            }
            Console.WriteLine($"Worker Thread {t} is stopping.");
        }

        public void Stop()
        {
            _signalled = true;
        }
    }
}
