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
            while (!_signalled)
            {
                if (_queue.TryDequeue(out var file))
                {
                    try
                    {
                        File.Copy(file.Source, file.Dest); // Use Native Copy API, optimized best for each platform
                        Interlocked.Increment(ref _counters.CopiedFiles);
                        Interlocked.Add(ref _counters.CopiedSize, file.Size);
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
