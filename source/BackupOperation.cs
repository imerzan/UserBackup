using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;

namespace UserBackup
{
    public class BackupOperation
    {
        private string[] _users;
        private string _dest;
        private bool _scanCompleted;
        private readonly OSPlatform _platform;
        private readonly BackupCounters _counters;
        private readonly ConcurrentQueue<BackupFile> _queue;
        private readonly BackupLogger _logger;
        private readonly BackupWorker[] _workers;
        private readonly System.Timers.Timer _timer;
        private readonly EnumerationOptions _enumOptions = new EnumerationOptions()
        {
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false
        };
        private readonly List<string> _ExcludedDirectories = new List<string>() // Excluded Directories in root %UserProfile% , use lowercase
        {
            "library",
            "applications",
            "appdata",
            "dropbox",
            "box",
            "box sync",
            "onedrive",
            "google drive"
        };

        public BackupOperation(OSPlatform platform, string dest, int threads)
        {
            _platform = platform;
            _dest = dest;
            _scanCompleted = false;
            _counters = new BackupCounters();
            _queue = new ConcurrentQueue<BackupFile>();
            _logger = new BackupLogger();
            _workers = new BackupWorker[threads];
            _timer = new System.Timers.Timer(25000);
            _timer.Elapsed += this.ProgressUpdate;
            _timer.AutoReset = true;
        }

        private void ProgressUpdate(Object source, ElapsedEventArgs e)
        {
            try
            {
                if (!_scanCompleted)
                    Console.WriteLine($"** SCANNING - {_counters.CopiedFiles} of {_counters.TotalFiles} files copied ({(int)_counters.CopiedSize} of {(int)_counters.TotalSize} MB)");
                else
                {
                    var pctComplete = (int)((_counters.CopiedSize / _counters.TotalSize) * 100);
                    Console.WriteLine($"** {pctComplete}% COMPLETE - {_counters.CopiedFiles} of {_counters.TotalFiles} files copied ({(int)_counters.CopiedSize} of {(int)_counters.TotalSize} MB)");
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
                GetSource();
                GetDest();
                GetName();
                BackupProfile();
                return 0;
            }
            catch (Exception ex) // Catastrophic failure, abort backup
            {
                _timer?.Stop();
                _logger?.Submit($"***FATAL ERROR*** on backup {_dest}\n{ex}");
                _logger?.Close();
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return -1;
            }
        }

        private void GetSource()
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
                    string drive = volume.Substring(0, 2); // Take first 2 chars ( example C: )
                    if (WindowsInterop.IsDriveLocked(drive))
                    {
                        Console.Write($"\nDrive {drive} is locked by Bitlocker, would you like to unlock (y/n)? ");
                        if (Console.ReadKey().KeyChar == 'y')
                        {
                            WindowsInterop.UnlockDrive(drive);
                        }
                    }
                }
                userDirs.Parse(volume);
            }
            Console.WriteLine(userDirs.ToString());
            Console.Write("Enter User(s) by id# to backup>> ");
            string users = Console.ReadLine().Trim();
            if (users == String.Empty) throw new Exception("User id(s) cannot be blank!");
            _users = users.Split(',');
            for (int i = 0; i < _users.Length; i++)
            {
                if (!int.TryParse(_users[i].Trim(), out int id)) throw new Exception("Invalid user id number(s) entered!");
                _users[i] = userDirs[id];
                if (!Directory.Exists(_users[i])) throw new IOException($"User Path {_users[i]} does not exist!");
            }
        }

        private void GetDest()
        {
            if (_dest is null)
            {
                Console.WriteLine($"\nDEFAULT Destination Path: {Path.Combine(Directory.GetCurrentDirectory(), "Backups")}");
                Console.Write("\nEnter Dest Path (blank=default)>> ");
                _dest = Console.ReadLine().Trim().TrimEnd(Path.DirectorySeparatorChar);
                if (_dest == String.Empty) _dest = Path.Combine(Directory.GetCurrentDirectory(), "Backups");
            }
            _dest = Path.GetFullPath(_dest.TrimEnd(Path.DirectorySeparatorChar));
            var create = Directory.CreateDirectory(_dest);
            if (!create.Exists) throw new IOException($"Unable to create destination folder {_dest}");
        }

        private void GetName()
        {
            Console.Write($"\nEnter Backup Name (default:{Environment.MachineName})>> ");
            string backupName = Console.ReadLine().Trim().Replace(Path.DirectorySeparatorChar.ToString(), String.Empty);
            if (backupName == String.Empty) backupName = Environment.MachineName;
            _dest = Path.Combine(_dest, backupName);
            if (Directory.Exists(_dest))
            {
                _dest += $"_{Path.GetRandomFileName()}"; // Rename trailing directory name
            }
            var create = Directory.CreateDirectory(_dest); // Create backup sub-folder
            if (!create.Exists) throw new IOException($"Unable to create destination folder {_dest}");
        }

        private void BackupProfile()
        {
            _logger.Open(Path.Combine(_dest, "log.txt")); // Open Logfile
            var sw = new BackupStopwatch(_logger, _counters, _dest); // Stopwatch to track backup duration
            for (int i = 0; i < _workers.Length; i++) // Start Workers
            {
                _workers[i] = new BackupWorker(_counters, _queue, _logger, i);
            }
            _timer.Start(); // Start timer for progress updates
            foreach (var user in _users) // Iterate *all* selected users
            {
                var userName = Path.GetFileName(user);
                _logger.Submit($"** Scanning User '{userName}'");
                if (_platform == OSPlatform.Windows) // Windows Only
                {
                    try // Microsoft Edge Bookmarks (Chromium Version)
                    {
                        if (Directory.Exists(Path.Combine(user, @"AppData\Local\Microsoft\Edge\User Data\Default")))
                        {
                            if (File.Exists(Path.Combine(user, @"AppData\Local\Microsoft\Edge\User Data\Default\Bookmarks")))
                            {
                                Directory.CreateDirectory(Path.Combine(_dest, userName, "EdgeBookmarks"));
                                File.Copy(Path.Combine(user, @"AppData\Local\Microsoft\Edge\User Data\Default\Bookmarks"), Path.Combine(_dest, userName, "EdgeBookmarks", "Bookmarks"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR copying Microsoft Edge Bookmarks: {ex}");
                        Interlocked.Increment(ref _counters.ErrorCount);
                    }
                }
                try // Chrome Bookmarks
                {
                    if (_platform == OSPlatform.OSX)
                    {
                        if (Directory.Exists(Path.Combine(user, "Library/Application Support/Google/Chrome/Default")))
                        {
                            if (File.Exists(Path.Combine(user, "Library/Application Support/Google/Chrome/Default/Bookmarks")))
                            {
                                Directory.CreateDirectory(Path.Combine(_dest, userName, "ChromeBookmarks"));
                                File.Copy(Path.Combine(user, "Library/Application Support/Google/Chrome/Default/Bookmarks"), Path.Combine(_dest, userName, "ChromeBookmarks", "Bookmarks"));
                            }
                        }
                    }
                    else if (_platform == OSPlatform.Windows)
                    {
                        if (Directory.Exists(Path.Combine(user, @"AppData\Local\Google\Chrome\User Data\Default")))
                        {
                            if (File.Exists(Path.Combine(user, @"AppData\Local\Google\Chrome\User Data\Default\Bookmarks")))
                            {
                                Directory.CreateDirectory(Path.Combine(_dest, userName, "ChromeBookmarks"));
                                File.Copy(Path.Combine(user, @"AppData\Local\Google\Chrome\User Data\Default\Bookmarks"), Path.Combine(_dest, userName, "ChromeBookmarks", "Bookmarks"));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Submit($"ERROR copying Chrome Bookmarks: {ex}");
                    Interlocked.Increment(ref _counters.ErrorCount);
                }
                try // Firefox Bookmarks
                {
                    if (_platform == OSPlatform.OSX)
                    {
                        if (Directory.Exists(Path.Combine(user, "Library/Application Support/Firefox/Profiles")))
                        {
                            Directory.CreateDirectory(Path.Combine(_dest, userName, "FirefoxBookmarks"));
                            ProcessDirectory(new DirectoryInfo(Path.Combine(user, "Library/Application Support/Firefox/Profiles")), Path.Combine(_dest, userName, "FirefoxBookmarks"));
                        }
                    }
                    else if (_platform == OSPlatform.Windows)
                    {
                        if (Directory.Exists(Path.Combine(user, @"AppData\Roaming\Mozilla\Firefox\Profiles")))
                        {
                            Directory.CreateDirectory(Path.Combine(_dest, userName, "FirefoxBookmarks"));
                            ProcessDirectory(new DirectoryInfo(Path.Combine(user, @"AppData\Roaming\Mozilla\Firefox\Profiles")), Path.Combine(_dest, userName, "FirefoxBookmarks"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Submit($"ERROR copying Firefox Bookmarks: {ex}");
                    Interlocked.Increment(ref _counters.ErrorCount);
                }
                if (_platform == OSPlatform.OSX) // Mac Only
                {
                    try // Safari bookmarks
                    {
                        if (File.Exists(Path.Combine(user, "Library/Safari/Bookmarks.plist")))
                        {
                            Directory.CreateDirectory(Path.Combine(_dest, userName, "SafariBookmarks"));
                            File.Copy(Path.Combine(user, "Library/Safari/Bookmarks.plist"), Path.Combine(_dest, userName, "SafariBookmarks", "Bookmarks.plist"));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR copying Safari Bookmarks, full disk access privileges required (see security&privacy): {ex}");
                        Interlocked.Increment(ref _counters.ErrorCount);
                    }
                }
                ProcessDirectory(new DirectoryInfo(user), _dest, true); // Start recursive call
            } // User Backup Completed (ForEach)
            _logger.Submit("** Backup scan completed! Waiting for queue to be completed by Worker Threads...");
            _scanCompleted = true; // Set 'Scan Completed' flag
            while (!_queue.IsEmpty) Thread.Sleep(50); // Slow down CPU
            _logger.Submit("** Queue has been completed! Signalling worker threads to wrap up...");
            foreach (var wrk in _workers) wrk.Stop();
            while (true) // Wait for all worker threads to complete
            {
                bool workerRunning = false;
                foreach (var wrk in _workers) if (wrk.IsAlive) workerRunning = true;
                if (!workerRunning) break;
                Thread.Sleep(50); // Slow down CPU
            }
            _timer.Stop(); // Stop timer for progress updates
            sw.Stop(); // End Stopwatch
            _logger.Close(); // Close Logfile
        }

        private void ProcessDirectory(DirectoryInfo directory, string backupDest, bool isRoot = false)
        {
            if (directory.FullName.Contains(_dest, StringComparison.OrdinalIgnoreCase)) // Loop Protection
            {
                _logger.Submit($"WARNING - Backup loop detected! Skipping folder {directory.FullName}");
                return;
            }
            backupDest = Path.Combine(backupDest, directory.Name);
            var createDest = Directory.CreateDirectory(backupDest); // Make sure destination is created before Enqueuing
            if (!createDest.Exists) throw new IOException($"Unable to create destination directory {backupDest}");
            try
            {
                foreach (var file in directory.EnumerateFiles("*", _enumOptions)) // Process Files
                {
                    try
                    {
                        if (_platform == OSPlatform.OSX && file.Attributes.HasFlag(FileAttributes.Hidden)) // macOS Bug, need to check hidden flag directly, not caught in enumeration (Fixed in .NET6)
                        {
                            continue;
                        }
                        _queue.Enqueue(new BackupFile()
                        {
                            Source = file.FullName,
                            Dest = Path.Combine(backupDest, file.Name),
                            Size = file.Length
                        });
                        _counters.TotalFiles++;
                        _counters.TotalSize += (double)file.Length / (double)1000000;
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing file {file.FullName}\n{ex}");
                        Interlocked.Increment(ref _counters.ErrorCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Submit($"ERROR iterating files in directory {directory.FullName}\n{ex}");
                Interlocked.Increment(ref _counters.ErrorCount);
            }

            try // Get subdirs
            {
                foreach (var subdirectory in directory.EnumerateDirectories("*", _enumOptions))
                {
                    try
                    {
                        if (_platform == OSPlatform.OSX && subdirectory.Attributes.HasFlag(FileAttributes.Hidden)) // macOS Bug, need to check hidden flag directly, not caught in enumeration (Fixed in .NET6)
                        {
                            continue;
                        }
                        if (subdirectory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) // Ignore .App Folders (applications)
                        {
                            _logger.Submit($"Skipping .App {subdirectory.FullName}");
                            continue;
                        }
                        if (isRoot) // %UserProfile% Root
                        {
                            if (_ExcludedDirectories.Contains(subdirectory.Name.ToLower()))
                            {
                                _logger.Submit($"Skipping Excluded Directory {subdirectory.FullName}");
                                continue;
                            }
                            if (subdirectory.Name.StartsWith('.')) // Ignore .Directories in %UserProfile%
                            {
                                _logger.Submit($"Skipping .Directory {subdirectory.FullName}");
                                continue;
                            }
                        }
                        ProcessDirectory(subdirectory, backupDest); // Process subdir (recurse)
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing folder {subdirectory.FullName}\n{ex}");
                        Interlocked.Increment(ref _counters.ErrorCount);
                    }
                } // End ForEach subdir
            }
            catch (Exception ex)
            {
                _logger.Submit($"ERROR iterating subdirs in directory {directory.FullName}\n{ex}");
                Interlocked.Increment(ref _counters.ErrorCount);
            }
        }
    }
}