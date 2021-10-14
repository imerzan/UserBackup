using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace UserBackup
{
    public class BackupOperation
    {
        private bool _scanCompleted;
        private DirectoryInfo _dest;
        private readonly List<DirectoryInfo> _users;
        private readonly OSPlatform _platform;
        private readonly BackupCounters _counters;
        private readonly BackupQueue _queue;
        private readonly BackupLogger _logger;
        private readonly BackupWorker[] _workers;
        private readonly System.Timers.Timer _timer;
        private static readonly EnumerationOptions _EnumOptions = new EnumerationOptions()
        {
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        private static readonly List<string> _ExcludedDirectories = new List<string>() // Excluded Directories in root %UserProfile%
        {
            "Library",
            "Applications",
            "AppData",
            "Dropbox",
            "Box",
            "Box Sync",
            "OneDrive",
            "Google Drive"
        };

        public BackupOperation(OSPlatform platform, string dest, int threads)
        {
            if (dest is not null) _dest = new DirectoryInfo(dest.TrimEnd(Path.DirectorySeparatorChar));
            _platform = platform;
            _users = new List<DirectoryInfo>();
            _scanCompleted = false;
            _counters = new BackupCounters();
            _queue = new BackupQueue(_counters);
            _logger = new BackupLogger(_counters);
            _workers = new BackupWorker[threads];
            _timer = new System.Timers.Timer(5000);
            _timer.Elapsed += this.ProgressUpdate;
            _timer.AutoReset = true;
        }

        private void ProgressUpdate(Object source, ElapsedEventArgs e)
        {
            try
            {
                int totalMB = (int)(Interlocked.Read(ref _counters.TotalSize) / 1000000);
                int copiedMB = (int)(Interlocked.Read(ref _counters.CopiedSize) / 1000000);
                if (!_scanCompleted)
                    Console.WriteLine($"** SCANNING - {_counters.CopiedFiles} of {_counters.TotalFiles} files copied ({copiedMB} of {totalMB} MB)");
                else
                {
                    int pctComplete = (int)(copiedMB * 100.0 / totalMB + 0.5);
                    Console.WriteLine($"** {pctComplete}% COMPLETE - {_counters.CopiedFiles} of {_counters.TotalFiles} files copied ({copiedMB} of {totalMB} MB)");
                }
            }
            catch { }
        }

        public int RunBackup() // Return Value passed to Program.Main() Exit Code
        {
            try
            {
                if (_platform == OSPlatform.Windows)
                {
                    if (!WindowsInterop.IsAdministrator()) throw new UnauthorizedAccessException("Insufficient privileges! Restart program as Administrator (Run-As-Admin).");
                }
                PromptSource();
                PromptDest();
                PromptName();
                BackupProfile();
                return 0;
            }
            catch (Exception ex) // Catastrophic failure, abort backup
            {
                _timer?.Stop();
                _logger?.Submit($"***FATAL ERROR*** on backup {_dest}: {ex}");
                _logger?.Dispose();
                _queue?.Clear();
                foreach (var wrk in _workers) wrk?.Stop();
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return -1;
            }
        }

        private void PromptSource()
        {
            Console.WriteLine("Scanning available drives/volumes...");
            string[] allRoots = null;
            if (_platform == OSPlatform.Windows)
            {
                var winDrives = DriveInfo.GetDrives();
                List<string> driveList = new List<string>();
                foreach (var winDrive in winDrives)
                {
                    driveList.Add(winDrive.Name);
                }
                allRoots = driveList.ToArray();
            }
            else if (_platform == OSPlatform.OSX) allRoots = Directory.GetDirectories("/Volumes");
            var userDirs = new UserDirectories();
            foreach (var volume in allRoots)
            {
                if (_platform == OSPlatform.Windows)
                {
                    if (WindowsInterop.IsDriveLocked(volume))
                    {
                        Console.Write($"\nDrive {volume} is locked by Bitlocker, would you like to unlock (y/n)? ");
                        if (Console.ReadKey().Key is ConsoleKey.Y)
                        {
                            WindowsInterop.UnlockDrive(volume);
                        }
                    }
                }
                userDirs.Parse(volume);
            }
            Console.WriteLine(userDirs.ToString());
            Console.Write("Enter User(s) by id# to backup>> ");
            string users = Console.ReadLine().Trim();
            if (users == String.Empty) throw new Exception("User id(s) cannot be blank!");
            foreach (var x in users.Split(','))
            {
                if (!int.TryParse(x.Trim(), out int id)) throw new Exception("Invalid user id number(s) entered!");
                var user = new DirectoryInfo(userDirs[id]);
                if (!user.Exists) throw new IOException($"User Path {user.FullName} does not exist!");
                _users.Add(user);
            }
        }

        private void PromptDest()
        {
            if (_dest is null)
            {
                Console.WriteLine($"\nDEFAULT Destination Path: {Path.Combine(Directory.GetCurrentDirectory(), "Backups")}");
                Console.Write("\nEnter Dest Path (blank=default)>> ");
                string dest = Console.ReadLine().Trim().TrimEnd(Path.DirectorySeparatorChar);
                if (dest == String.Empty) _dest = new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "Backups"));
                else _dest = new DirectoryInfo(dest);
            }
            _dest.Create(); // Attempt to create dest folder
            if (!_dest.Exists) throw new IOException($"Unable to create destination folder {_dest}");
        }

        private void PromptName()
        {
            Console.Write($"\nEnter Backup Name (default:{Environment.MachineName})>> ");
            string backupName = Console.ReadLine().Trim().Replace(Path.DirectorySeparatorChar.ToString(), String.Empty);
            if (backupName == String.Empty) backupName = Environment.MachineName;
            if (Directory.Exists(Path.Combine(_dest.FullName, backupName)))
            {
                _dest = _dest.CreateSubdirectory($"{backupName}_{Path.GetRandomFileName()}"); // Rename trailing directory name
            }
            else _dest = _dest.CreateSubdirectory(backupName);
            if (!_dest.Exists) throw new IOException($"Unable to create destination folder {_dest}");
        }

        private void BackupProfile()
        {
            _logger.OpenLogfile(_dest.FullName); // Open Logfile, begin stopwatch
            for (int i = 0; i < _workers.Length; i++) // Start Workers
            {
                _workers[i] = new BackupWorker(_counters, _queue, _logger, i+1);
            }
            _timer.Start(); // Start timer for progress updates
            foreach (var user in _users) // Iterate *all* selected users
            {
                _logger.Submit($"** Scanning User '{user.Name}'");
                if (_platform == OSPlatform.OSX) // Mac Only
                {
                    try // Safari bookmarks
                    {
                        var safari = new FileInfo(Path.Combine(user.FullName, "Library/Safari/Bookmarks.plist"));
                        if (safari.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest.FullName, user.Name, "SafariBookmarks"));
                            _queue.Enqueue(safari.FullName, Path.Combine(dest.FullName, safari.Name), safari.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing Safari Bookmarks: {ex}", LogMessage.Error);
                    }
                }
                if (_platform == OSPlatform.Windows) // Windows Only
                {
                    try // Microsoft Edge Bookmarks (Chromium Version)
                    {
                        var edge = new FileInfo(Path.Combine(user.FullName, @"AppData\Local\Microsoft\Edge\User Data\Default\Bookmarks"));
                        if (edge.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest.FullName, user.Name, "EdgeBookmarks"));
                            _queue.Enqueue(edge.FullName, Path.Combine(dest.FullName, edge.Name), edge.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing Microsoft Edge Bookmarks: {ex}", LogMessage.Error);
                    }
                }
                try // Chrome Bookmarks
                {
                    if (_platform == OSPlatform.OSX)
                    {
                        var chrome = new FileInfo(Path.Combine(user.FullName, "Library/Application Support/Google/Chrome/Default/Bookmarks"));
                        if (chrome.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest.FullName, user.Name, "ChromeBookmarks"));
                            _queue.Enqueue(chrome.FullName, Path.Combine(dest.FullName, chrome.Name), chrome.Length);
                        }
                    }
                    else if (_platform == OSPlatform.Windows)
                    {
                        var chrome = new FileInfo(Path.Combine(user.FullName, @"AppData\Local\Google\Chrome\User Data\Default\Bookmarks"));
                        if (chrome.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest.FullName, user.Name, "ChromeBookmarks"));
                            _queue.Enqueue(chrome.FullName, Path.Combine(dest.FullName, chrome.Name), chrome.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Submit($"ERROR processing Chrome Bookmarks: {ex}", LogMessage.Error);
                }
                try // Firefox Bookmarks
                {
                    if (_platform == OSPlatform.OSX)
                    {
                        var firefox = new DirectoryInfo(Path.Combine(user.FullName, "Library/Application Support/Firefox/Profiles"));
                        if (firefox.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest.FullName, user.Name, "FirefoxBookmarks"));
                            ProcessDirectory(firefox, dest);
                        }
                    }
                    else if (_platform == OSPlatform.Windows)
                    {
                        var firefox = new DirectoryInfo(Path.Combine(user.FullName, @"AppData\Roaming\Mozilla\Firefox\Profiles"));
                        if (firefox.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest.FullName, user.Name, "FirefoxBookmarks"));
                            ProcessDirectory(firefox, dest);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Submit($"ERROR processing Firefox Bookmarks: {ex}", LogMessage.Error);
                }
                ProcessDirectory(user, _dest, true); // Start recursive call
            } // User Backup Completed (ForEach)
            _logger.Submit("** Backup scan completed! Waiting for queue to be completed by Worker Threads...");
            _scanCompleted = true; // Set 'Scan Completed' flag
            while (!_queue.IsEmpty) Thread.Sleep(50); // Slow down CPU
            _logger.Submit("** Queue has been completed! Signalling worker threads to wrap up once finished...");
            foreach (var wrk in _workers) wrk.Stop();
            while (true) // Wait for all worker threads to complete
            {
                bool workerRunning = false;
                foreach (var wrk in _workers) if (wrk.IsAlive) workerRunning = true;
                if (!workerRunning) break;
                Thread.Sleep(50); // Slow down CPU
            }
            _timer.Stop(); // Stop timer for progress updates
            _logger.Completed(); // Log completion of backup operation
            _logger.Dispose(); // Close logfile
        }

        private void ProcessDirectory(DirectoryInfo directory, DirectoryInfo backupDest, bool isRoot = false)
        {
            if (directory.FullName.Contains(_dest.FullName, StringComparison.OrdinalIgnoreCase)) // Loop Protection
            {
                _logger.Submit($"WARNING - Backup loop detected! Skipping folder {directory.FullName}");
                return;
            }
            backupDest = backupDest.CreateSubdirectory(directory.Name); // Make sure destination is created before Enqueuing
            if (!backupDest.Exists) throw new IOException($"Unable to create destination directory {backupDest.FullName}");
            try
            {
                foreach (var file in directory.EnumerateFiles("*", _EnumOptions))
                {
                    try
                    {
                        _queue.Enqueue(file.FullName, Path.Combine(backupDest.FullName, file.Name), file.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing file {file.FullName}: {ex}", LogMessage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Submit($"ERROR enumerating files in directory {directory.FullName}: {ex}", LogMessage.Error);
            }

            try // Get subdirs
            {
                Parallel.ForEach(directory.EnumerateDirectories("*", _EnumOptions), subdirectory =>
                {
                    try
                    {
                        if (subdirectory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) // Ignore .App Folders (applications)
                        {
                            return;
                        }
                        if (isRoot) // %UserProfile% Root
                        {
                            if (_ExcludedDirectories.FirstOrDefault(x => x.Equals(subdirectory.Name, StringComparison.OrdinalIgnoreCase)) is not null) // Directory is excluded
                            {
                                return;
                            }
                            if (subdirectory.Name.StartsWith('.')) // Ignore .Directories in %UserProfile%
                            {
                                return;
                            }
                        }
                        ProcessDirectory(subdirectory, backupDest); // Process subdir (recurse)
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing subdir {subdirectory.FullName}: {ex}", LogMessage.Error);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Submit($"ERROR enumerating subdirs in directory {directory.FullName}: {ex}", LogMessage.Error);
            }
        }
    }
}