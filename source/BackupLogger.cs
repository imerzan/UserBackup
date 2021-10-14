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
        private readonly Stopwatch _sw;
        private string _dest;
        private StreamWriter _LogFile;
        private readonly object _LogFileLock = new object();

        public BackupLogger(BackupCounters counters)
        {
            _counters = counters;
            _sw = new Stopwatch();
        }

        public void OpenLogfile(string dest)
        {
            if (_disposed) throw new ObjectDisposedException("BackupLogger was disposed!");
            _dest = dest;
            string log = Path.Combine(dest, "log.txt");
            lock (_LogFileLock)
            {
                _LogFile = File.AppendText(log);
                _LogFile.AutoFlush = true;
            }
            Console.WriteLine($"Opened Logfile at {log}");
            _sw.Start();
            Submit($"** UserBackup Version {Program.AssemblyVersion}, Starting Backup...");
        }

        public void Submit(string entry, LogMessage msgType = LogMessage.Normal)
        {
            if (_disposed) throw new ObjectDisposedException("BackupLogger was disposed!");
            try
            {
                Console.WriteLine(entry);
                lock (_LogFileLock) // Lock access to Stream to be thread safe
                {
                    _LogFile?.WriteLine($"{DateTime.Now}: {entry}"); // Only write if Logfile has been already opened
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

        public void Completed()
        {
            if (_disposed) throw new ObjectDisposedException("BackupLogger was disposed!");
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
            Submit(output.ToString()); // Log completion
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
                    _LogFile?.Dispose();
                    _LogFile = null;
                }
                _sw?.Stop();
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
