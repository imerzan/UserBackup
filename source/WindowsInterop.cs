using System;
using System.Management;
using System.Security.Principal;
using System.Linq;

namespace UserBackup
{
    /// <summary>
    /// Windows Interop Code
    /// </summary>
    internal static class WindowsInterop
    {
#pragma warning disable CA1416
        public static bool IsAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        public static bool IsDriveLocked(string driveParam) // Requires Admin
        {
            driveParam = driveParam.TrimEnd('\\'); // Remove trailing slash
            using var wmi = new ManagementObjectSearcher(@"root\cimv2\Security\MicrosoftVolumeEncryption", "SELECT * FROM Win32_EncryptableVolume");
            using var drive = wmi.Get().OfType<ManagementObject>()
                .FirstOrDefault(x => x.GetPropertyValue("DriveLetter").ToString().Equals(driveParam, StringComparison.OrdinalIgnoreCase));
            if (drive is null) return false;
            else
            {
                using var status = drive.InvokeMethod("GetLockStatus", null, null);
                switch (Convert.ToUInt32(status["LockStatus"]))
                {
                    case 1:
                        return true; // Locked,Encrypted
                    default:
                        return false;
                }
            }
        }

        public static void UnlockDrive(string driveParam) // Requires Admin
        {
            driveParam = driveParam.TrimEnd('\\'); // Remove trailing slash
            using var wmi = new ManagementObjectSearcher(@"root\cimv2\Security\MicrosoftVolumeEncryption", "SELECT * FROM Win32_EncryptableVolume");
            using var drive = wmi.Get().OfType<ManagementObject>()
                .FirstOrDefault(x => x.GetPropertyValue("DriveLetter").ToString().Equals(driveParam, StringComparison.OrdinalIgnoreCase));
            if (drive is null) throw new NullReferenceException($"Unable to parse WMI object of drive {driveParam}");
            else
            {
                { // scope 1
                    using var inParams = drive.GetMethodParameters("GetKeyProtectors");
                    inParams["KeyProtectorType"] = (uint)3; // Numerical password
                    using var protectors = drive.InvokeMethod("GetKeyProtectors", inParams, null);
                    Console.WriteLine($"\nKey Identifiers for {driveParam}");
                    foreach (var protector in (string[])protectors["VolumeKeyProtectorID"])
                    {
                        Console.WriteLine(protector);
                    }
                }
                { // scope 2
                    Console.WriteLine("\nUse the above identifiers to obtain a Bitlocker Recovery Key from MBAM. Enter the recovery key below to unlock the drive.\n");
                    Console.Write("BL Recovery Key>> ");
                    using var inParams = drive.GetMethodParameters("UnlockWithNumericalPassword");
                    inParams["NumericalPassword"] = Console.ReadLine().Trim();
                    using var unlock = drive.InvokeMethod("UnlockWithNumericalPassword", inParams, null);
                    switch (Convert.ToUInt32(unlock["returnValue"]))
                    {
                        case 0x0: // S_OK
                            Console.WriteLine($"Drive {driveParam} unlocked successfully!");
                            return;
                        case 0x80310008: // FVE_E_NOT_ACTIVATED
                            throw new Exception("FVE_E_NOT_ACTIVATED: BitLocker is not enabled on the volume. Add a key protector to enable BitLocker.");
                        case 0x80310033: // FVE_E_PROTECTOR_NOT_FOUND
                            throw new Exception("FVE_E_PROTECTOR_NOT_FOUND: The volume does not have a key protector of the type Numerical Password." +
                                "The NumericalPassword parameter has a valid format, but you cannot use a numerical password to unlock the volume.");
                        case 0x80310027: // FVE_E_FAILED_AUTHENTICATION
                            throw new Exception("FVE_E_FAILED_AUTHENTICATION: The NumericalPassword parameter cannot unlock the volume." +
                                "One or more key protectors of the type Numerical Password exist, but the specified NumericalPassword parameter cannot unlock the volume.");
                        case 0x80310035: // FVE_E_INVALID_PASSWORD_FORMAT
                            throw new Exception("FVE_E_INVALID_PASSWORD_FORMAT: The NumericalPassword parameter does not have a valid format.");
                        default:
                            throw new Exception($"Unknown error unlocking drive {driveParam}");
                    }
                }
            }
        }
#pragma warning restore CA1416

    }
}
