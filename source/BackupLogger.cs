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
        private readonly Stopwatch _sw;
        private string _dest;
        private StreamWriter _LogFile;
        private readonly object _LogFileLock = new object();

        public BackupLogger(BackupCounters counters)
        {
            _counters = counters;
            _sw = new Stopwatch();
        }

        public void Open(string dest)
        {
            _dest = dest;
            string log = Path.Combine(dest, "log.txt");
            lock (_LogFileLock)
            {
                _LogFile = File.AppendText(log);
                _LogFile.AutoFlush = true;
                Console.WriteLine($"Opened Logfile at {log}");
            }
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
                    _LogFile?.WriteLine($"{DateTime.Now}: {entry}");
                }
            }
            catch { }
            finally
            {
                if (msgType is LogMessage.Error) Interlocked.Increment(ref _counters.ErrorCount);
            }
        }

        public void Close()
        {
            _sw.Stop();
            TimeSpan ts = _sw.Elapsed;
            var output = new StringBuilder();
            output.Append($"** Backup completed to {_dest}\n" +
                $"Total Files Backed Up: {_counters.CopiedFiles} of {_counters.TotalFiles}\n" +
                $"Backup Size (MB): {_counters.CopiedSize}\n" +
                $"ERROR Count: {_counters.ErrorCount}\n" +
                $"Duration: ");
            if (ts.Hours > 0) output.Append($"{ts.Hours} Hours ");
            if (ts.Minutes > 0) output.Append($"{ts.Minutes} Minutes ");
            if (ts.Seconds > 0) output.Append($"{ts.Seconds} Seconds ");
            if (ts.Minutes == 0) output.Append($"{ts.Milliseconds} Milliseconds");
            Submit(output.ToString()); // Log completion
            Dispose();
        }

        public void Dispose()
        {
            lock (_LogFileLock)
            {
                _LogFile?.Dispose();
                _LogFile = null;
            }
        }
    }

    public enum LogMessage
    {
        Normal,
        Error
    }
}
