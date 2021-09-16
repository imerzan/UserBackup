using System;
using System.IO;

namespace UserBackup
{
    public class BackupLogger
    {
        private StreamWriter _LogFile;
        private readonly object _LogFileLock = new object();

        public void Open(string logfileDest)
        {
            lock (_LogFileLock)
            {
                if (_LogFile is null)
                {
                    _LogFile = File.AppendText(logfileDest);
                    _LogFile.AutoFlush = true;
                    Console.WriteLine($"Opened Logfile at {logfileDest}");
                }
            }
        }

        public void Submit(string entry)
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
        }

        public void Close()
        {
            lock (_LogFileLock)
            {
                if (_LogFile is not null)
                {
                    _LogFile.Dispose();
                    _LogFile = null; // Remove object reference
                    Console.WriteLine("Closed Logfile...");
                }
            }
        }
    }
}
