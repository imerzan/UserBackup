using System;
using System.Diagnostics;
using System.Text;

namespace UserBackup
{
    public class BackupStopwatch
    {
        private readonly Stopwatch _sw;
        private readonly BackupLogger _logger;
        private readonly BackupCounters _counters;
        private readonly string _dest;

        public BackupStopwatch(BackupLogger logger, BackupCounters counters, string dest)
        {
            _logger = logger;
            _counters = counters;
            _dest = dest;
            _sw = new Stopwatch();
            _sw.Start();
            logger.Submit($"** UserBackup Version {Program.AssemblyVersion}, Starting Backup...");
        }

        public void Stop()
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
            _logger.Submit(output.ToString()); // Log completion
        }
    }
}