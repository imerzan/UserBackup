using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UserBackup
{
    public class UserDirectories
    {
        private readonly Dictionary<int, string> _userDirs;
        public string this[int key]
        {
            get
            {
                return _userDirs[key];
            }
        }

        public UserDirectories()
        {
            _userDirs = new Dictionary<int, string>();
        }

        public void Parse(string volume)
        {
            try
            {
                var volumeSubdirs = Directory.GetDirectories(volume);
                string usersFolder = volumeSubdirs.First(x => Path.GetFileName(x).Equals("Users", StringComparison.OrdinalIgnoreCase)); // Will throw if not found
                var allUsers = Directory.GetDirectories(usersFolder);
                foreach (var user in allUsers)
                {
                    try
                    {
                        var userLibraries = Directory.GetDirectories(user);
                        userLibraries.First(x => Path.GetFileName(x).Equals("Desktop", StringComparison.OrdinalIgnoreCase)); // Will throw if not found
                        _userDirs.Add(_userDirs.Count, user);
                    } catch { }
                }
            } catch { }
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
