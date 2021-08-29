using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace UserBackup
{
    public class BackupWorker
    {
        private readonly BackupCounters _counters;
        private readonly ConcurrentQueue<BackupFile> _queue;
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

        public BackupWorker(BackupCounters counters, ConcurrentQueue<BackupFile> queue, BackupLogger logger, int t)
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
            while (!_signalled)
            {
                try
                {
                    if (_queue.TryDequeue(out var file))
                    {
                        File.Copy(file.Source, file.Dest);
                        Interlocked.Increment(ref _counters.CopiedFiles);
                        lock (_counters.CopiedSize_lock) // Lock access to be thread safe
                        {
                            _counters.CopiedSize += (double)file.Size / (double)1000000;
                        }
                    }
                    else Thread.Sleep(1); // Slow down CPU
                }
                catch (Exception ex)
                {
                    _logger.Submit($"ERROR Worker #{t}: {ex}");
                    Interlocked.Increment(ref _counters.ErrorCount);
                }
            }
            Console.WriteLine($"Worker Thread {t} is stopping.");
        }

        public void Stop()
        {
            _signalled = true;
        }
    }
}
