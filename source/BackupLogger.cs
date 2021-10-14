using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace UserBackup
{
    public class BackupLogger
    {
        private readonly BackupCounters _counters;
        private string _dest;
        private StreamWriter _LogFile;
        private Stopwatch _sw;
        private readonly object _LogFileLock = new object();

        public BackupLogger(BackupCounters counters)
        {
            _counters = counters;
        }

        public void OpenLogfile(string dest)
        {
            lock (_LogFileLock)
            {
                if (_LogFile is not null) return; // Already opened
                _dest = dest;
                string log = Path.Combine(dest, "log.txt");
                _LogFile = File.AppendText(log);
                _LogFile.AutoFlush = true;
                Console.WriteLine($"Opened Logfile at {log}");
            }
            _sw = new Stopwatch();
            _sw.Start();
            Submit($"** UserBackup Version {Program.AssemblyVersion}, Starting Backup...");
        }

        public void Submit(string entry, LogMessage msgType = LogMessage.Normal)
        {
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

        public void LogCompletion()
        {
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

        public void CloseLogfile()
        {
            lock (_LogFileLock)
            {
                if (_LogFile is null) return; // Already closed
                _LogFile.Dispose(); // Dispose Stream
                _LogFile = null; // Remove object reference
            }
            Console.WriteLine("Logfile closed.");
        }
    }

    public enum LogMessage
    {
        Normal,
        Error
    }
}
