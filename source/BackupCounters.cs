namespace UserBackup
{
    public class BackupCounters // Use Interlocked to modify values
    {
        public int TotalFiles = 0;
        public int CopiedFiles = 0;
        public int ErrorCount = 0;
        public long TotalSize = 0;
        public long CopiedSize = 0;
    }
}