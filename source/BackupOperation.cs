using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        private readonly List<string> _ExcludedDirectories = new List<string>() // Excluded Directories in root %UserProfile%
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
            _platform = platform;
            _dest = dest;
            _scanCompleted = false;
            _counters = new BackupCounters();
            _queue = new ConcurrentQueue<BackupFile>();
            _logger = new BackupLogger();
            _workers = new BackupWorker[threads];
            _timer = new System.Timers.Timer(5000);
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
                PromptSource();
                PromptDest();
                PromptName();
                BackupProfile();
                return 0;
            }
            catch (Exception ex) // Catastrophic failure, abort backup
            {
                _timer?.Stop();
                _logger?.Submit($"***FATAL ERROR*** on backup {_dest}\n{ex}");
                _logger?.Close();
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
                    driveList.Add(winDrive.Name); // Take first 2 chars ( example C: )
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
            _users = users.Split(',');
            for (int i = 0; i < _users.Length; i++)
            {
                if (!int.TryParse(_users[i].Trim(), out int id)) throw new Exception("Invalid user id number(s) entered!");
                _users[i] = userDirs[id];
                if (!Directory.Exists(_users[i])) throw new IOException($"User Path {_users[i]} does not exist!");
            }
        }

        private void PromptDest()
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

        private void PromptName()
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
                if (_platform == OSPlatform.OSX) // Mac Only
                {
                    try // Safari bookmarks
                    {
                        var safari = new FileInfo(Path.Combine(user, "Library/Safari/Bookmarks.plist"));
                        if (safari.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest, userName, "SafariBookmarks"));
                            _queue.Enqueue(new BackupFile()
                            {
                                Source = safari.FullName,
                                Dest = Path.Combine(dest.FullName, safari.Name),
                                Size = safari.Length
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing Safari Bookmarks: {ex}");
                        Interlocked.Increment(ref _counters.ErrorCount);
                    }
                }
                if (_platform == OSPlatform.Windows) // Windows Only
                {
                    try // Microsoft Edge Bookmarks (Chromium Version)
                    {
                        var edge = new FileInfo(Path.Combine(user, @"AppData\Local\Microsoft\Edge\User Data\Default\Bookmarks"));
                        if (edge.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest, userName, "EdgeBookmarks"));
                            _queue.Enqueue(new BackupFile()
                            {
                                Source = edge.FullName,
                                Dest = Path.Combine(dest.FullName, edge.Name),
                                Size = edge.Length
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing Microsoft Edge Bookmarks: {ex}");
                        Interlocked.Increment(ref _counters.ErrorCount);
                    }
                }
                try // Chrome Bookmarks
                {
                    if (_platform == OSPlatform.OSX)
                    {
                        var chrome = new FileInfo(Path.Combine(user, "Library/Application Support/Google/Chrome/Default/Bookmarks"));
                        if (chrome.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest, userName, "ChromeBookmarks"));
                            _queue.Enqueue(new BackupFile()
                            {
                                Source = chrome.FullName,
                                Dest = Path.Combine(dest.FullName, chrome.Name),
                                Size = chrome.Length
                            });
                        }
                    }
                    else if (_platform == OSPlatform.Windows)
                    {
                        var chrome = new FileInfo(Path.Combine(user, @"AppData\Local\Google\Chrome\User Data\Default\Bookmarks"));
                        if (chrome.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest, userName, "ChromeBookmarks"));
                            _queue.Enqueue(new BackupFile()
                            {
                                Source = chrome.FullName,
                                Dest = Path.Combine(dest.FullName, chrome.Name),
                                Size = chrome.Length
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Submit($"ERROR processing Chrome Bookmarks: {ex}");
                    Interlocked.Increment(ref _counters.ErrorCount);
                }
                try // Firefox Bookmarks
                {
                    if (_platform == OSPlatform.OSX)
                    {
                        var firefox = new DirectoryInfo(Path.Combine(user, "Library/Application Support/Firefox/Profiles"));
                        if (firefox.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest, userName, "FirefoxBookmarks"));
                            ProcessDirectory(firefox, dest);
                        }
                    }
                    else if (_platform == OSPlatform.Windows)
                    {
                        var firefox = new DirectoryInfo(Path.Combine(user, @"AppData\Roaming\Mozilla\Firefox\Profiles"));
                        if (firefox.Exists)
                        {
                            var dest = Directory.CreateDirectory(Path.Combine(_dest, userName, "FirefoxBookmarks"));
                            ProcessDirectory(firefox, dest);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Submit($"ERROR processing Firefox Bookmarks: {ex}");
                    Interlocked.Increment(ref _counters.ErrorCount);
                }
                ProcessDirectory(new DirectoryInfo(user), new DirectoryInfo(_dest), true); // Start recursive call
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
            sw.Stop(); // End Stopwatch
            _logger.Close(); // Close Logfile
        }

        private void ProcessDirectory(DirectoryInfo directory, DirectoryInfo backupDest, bool isRoot = false)
        {
            if (directory.FullName.Contains(_dest, StringComparison.OrdinalIgnoreCase)) // Loop Protection
            {
                _logger.Submit($"WARNING - Backup loop detected! Skipping folder {directory.FullName}");
                return;
            }
            backupDest = backupDest.CreateSubdirectory(directory.Name); // Make sure destination is created before Enqueuing
            if (!backupDest.Exists) throw new IOException($"Unable to create destination directory {backupDest.FullName}");
            try
            {
                foreach (var file in directory.EnumerateFiles("*", _enumOptions)) // Process Files
                {
                    try
                    {
                        if (_platform == OSPlatform.OSX && file.Attributes.HasFlag(FileAttributes.Hidden)) // macOS Bug, need to check hidden flag directly, not caught in enumeration (ToDo in .NET6)
                        {
                            continue;
                        }
                        _queue.Enqueue(new BackupFile()
                        {
                            Source = file.FullName,
                            Dest = Path.Combine(backupDest.FullName, file.Name),
                            Size = file.Length
                        });
                        _counters.TotalFiles++;
                        _counters.TotalSize += (double)file.Length / (double)1000000; // Megabytes
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
                _logger.Submit($"ERROR enumerating files in directory {directory.FullName}\n{ex}");
                Interlocked.Increment(ref _counters.ErrorCount);
            }

            try // Get subdirs
            {
                foreach (var subdirectory in directory.EnumerateDirectories("*", _enumOptions))
                {
                    try
                    {
                        if (_platform == OSPlatform.OSX && subdirectory.Attributes.HasFlag(FileAttributes.Hidden)) // macOS Bug, need to check hidden flag directly, not caught in enumeration (ToDo in .NET6)
                        {
                            continue;
                        }
                        if (subdirectory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) // Ignore .App Folders (applications)
                        {
                            continue;
                        }
                        if (isRoot) // %UserProfile% Root
                        {
                            if (_ExcludedDirectories.FirstOrDefault(x => x.Equals(subdirectory.Name, StringComparison.OrdinalIgnoreCase)) is not null) // Directory is excluded
                            {
                                continue;
                            }
                            if (subdirectory.Name.StartsWith('.')) // Ignore .Directories in %UserProfile%
                            {
                                continue;
                            }
                        }
                        ProcessDirectory(subdirectory, backupDest); // Process subdir (recurse)
                    }
                    catch (Exception ex)
                    {
                        _logger.Submit($"ERROR processing subdir {subdirectory.FullName}\n{ex}");
                        Interlocked.Increment(ref _counters.ErrorCount);
                    }
                } // End ForEach subdir
            }
            catch (Exception ex)
            {
                _logger.Submit($"ERROR enumerating subdirs in directory {directory.FullName}\n{ex}");
                Interlocked.Increment(ref _counters.ErrorCount);
            }
        }
    }
}