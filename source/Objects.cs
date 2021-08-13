using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UserBackup
{
    public class UserDirectories
    {
        private readonly Dictionary<int, string> _userDirs;
        public string this[int index]
        {
            get
            {
                return _userDirs[index];
            }
        }

        public UserDirectories()
        {
            _userDirs = new Dictionary<int, string>();
        }

        public void Parse(string drive)
        {
            try
            {
                var rootSubDirs = Directory.GetDirectories(drive);
                foreach (var subdir in rootSubDirs)
                {
                    try
                    {
                        if (Path.GetFileName(subdir).Equals("users", StringComparison.OrdinalIgnoreCase)) // Check for 'Users' Folder
                        {
                            var userDirs = Directory.GetDirectories(subdir);
                            foreach (var user in userDirs)
                            {
                                try
                                {
                                    var subFolders = Directory.GetDirectories(user);
                                    foreach (var folder in subFolders)
                                    {
                                        if (Path.GetFileName(folder).Equals("desktop", StringComparison.OrdinalIgnoreCase)) // Check for 'Desktop' Folder
                                        {
                                            _userDirs.Add(_userDirs.Count, user);
                                            break; // UserProfile validated, break loop
                                        }
                                    }
                                }
                                catch { }
                            }
                            break; // Users folder located, break loop
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("\nID | Detected User Path");
            sb.AppendLine("----------------------------------");
            foreach (KeyValuePair<int, string> entry in _userDirs)
            {
                sb.AppendLine($"{entry.Key}) {entry.Value}");
            }
            sb.AppendLine("\n(NOTE: Separate multiple users by comma ',' ex: 0,1,2)");
            return sb.ToString();
        }
    }
    public struct BackupFile
    {
        public string Source { get; init; }
        public string Dest { get; init; }
        public long Size { get; init; }
    }
}
