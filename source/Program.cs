using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UserBackup
{
    class Program
    {
        public static readonly string AssemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        static int Main(string[] args) // Program Entry Point
        {
            string destParam = null;
            int threadsParam = 16; // Default:16
            foreach (string arg in args) // Process Arguments
            {
                if (arg.Trim().StartsWith("dest=", StringComparison.OrdinalIgnoreCase))
                {
                    string[] dest = arg.Trim().Split(new[] { '=' }, 2);
                    if (dest.Length == 2)
                    {
                        destParam = dest[1].Trim();
                        Console.WriteLine($"Backup Dest set to {destParam}");
                    }
                }
                else if (arg.Trim().StartsWith("threads=", StringComparison.OrdinalIgnoreCase))
                {
                    string[] threads = arg.Trim().Split(new[] { '=' }, 2);
                    if (threads.Length == 2)
                    {
                        if (int.TryParse(threads[1].Trim(), out threadsParam))
                        {
                            Console.WriteLine($"Thread Count set to {threadsParam}");
                        }
                    }
                }
            }
            Console.WriteLine($"Welcome to UserBackup v{AssemblyVersion}\n" +
                $"Detected OS: {RuntimeInformation.OSDescription}");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var winMain = new BackupOperation(OSPlatform.Windows, destParam, threadsParam);
                return winMain.RunBackup();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var osxMain = new BackupOperation(OSPlatform.OSX, destParam, threadsParam);
                return osxMain.RunBackup();
            }
            else throw new PlatformNotSupportedException($"{RuntimeInformation.OSDescription} is not supported");
        }
    }
}
