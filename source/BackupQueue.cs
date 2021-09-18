using System.Collections.Concurrent;

namespace UserBackup
{
    public class BackupQueue // Wrapper Class for ConcurrentQueue, will accumulate counters on Enqueuing
    {
        private readonly ConcurrentQueue<BackupFile> _queue;
        private readonly BackupCounters _counters;
        public bool IsEmpty
        {
            get
            {
                return _queue.IsEmpty;
            }
        }

        public BackupQueue(BackupCounters counters)
        {
            _queue = new ConcurrentQueue<BackupFile>();
            _counters = counters;
        }
        public void Enqueue(string source, string dest, long size)
        {
            _queue.Enqueue(new BackupFile() // Thread safe
            {
                Source = source,
                Dest = dest,
                Size = size
            });
            // Below not thread safe, this is OK since it is only called from the primary thread
            _counters.TotalFiles++;
            _counters.TotalSize += (double)size / (double)1000000; // Megabytes
        }

        public bool TryDequeue(out BackupFile file) // Thread safe
        {
            return _queue.TryDequeue(out file);
        }

        public void Clear()
        {
            _queue.Clear();
        }
    }
}
