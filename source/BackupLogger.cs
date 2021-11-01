using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace UserBackup
{
    public class BackupLogger : IDisposable
    {
        private readonly BackupCounters _counters;
        private string _dest;
        private StreamWriter _LogFile;
        private readonly Stopwatch _sw;
        private readonly object _LogFileLock = new object();

        public BackupLogger(BackupCounters counters)
        {
            _counters = counters;
            _sw = new Stopwatch();
        }

        public void Start(string dest)
        {
            if (_disposed || _sw.IsRunning) return;
            _dest = dest;
            lock (_LogFileLock)
            {
                _LogFile = File.AppendText(Path.Combine(dest, "log.txt"));
                _LogFile.AutoFlush = true;
            }
            _sw.Start();
            Write($"** UserBackup Version {Program.AssemblyVersion}, Starting Backup...");
        }

        public void Write(string entry, LogMessage msgType = LogMessage.Normal)
        {
            try
            {
                Console.WriteLine(entry);
                lock (_LogFileLock) // Lock access to Stream to be thread safe
                {
                    _LogFile?.WriteLine($"{DateTime.Now}: {entry}"); // Only write if StreamWriter is *not* null, method can continue after disposal (log to console only)
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR writing to Logfile: {ex}");
            }
            finally
            {
                if (msgType is LogMessage.Error) Interlocked.Increment(ref _counters.ErrorCount);
            }
        }

        public void Stop()
        {
            if (_disposed || !_sw.IsRunning) return;
            _sw.Stop();
            TimeSpan ts = _sw.Elapsed;
            var output = new StringBuilder();
            output.Append($"** Backup completed to {_dest}\n" +
                $"Total Files Backed Up: {_counters.CopiedFiles} of {_counters.TotalFiles}\n" +
                $"Backup Size (MB): {_counters.CopiedSize / 1000000}\n" +
                $"ERROR Count: {_counters.ErrorCount}\n" +
                $"Duration: ");
            if (ts.Hours > 0) output.Append($"{ts.Hours} Hours ");
            if (ts.Minutes > 0) output.Append($"{ts.Minutes} Minutes ");
            if (ts.Seconds > 0) output.Append($"{ts.Seconds} Seconds ");
            if (ts.Minutes == 0) output.Append($"{ts.Milliseconds} Milliseconds");
            Write(output.ToString()); // Log completion
        }

        // Public implementation of Dispose pattern callable by consumers.
        private bool _disposed = false;
        public void Dispose() => Dispose(true);

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Dispose managed state (managed objects).
                lock (_LogFileLock)
                {
                    _LogFile?.Dispose(); // Dispose Stream
                    _LogFile = null; // Remove object reference
                }
            }

            _disposed = true;
        }
    }

    public enum LogMessage
    {
        Normal,
        Error
    }
}
