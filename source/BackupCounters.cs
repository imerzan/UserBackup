namespace UserBackup
{
    public class BackupCounters
    {
        public int TotalFiles = 0;
        public int CopiedFiles = 0;
        public int ErrorCount = 0;
        public double TotalSize = 0;
        public double CopiedSize = 0;
        public readonly object CopiedSize_lock = new object(); // Use lock to set 'CopiedSize'
    }
}