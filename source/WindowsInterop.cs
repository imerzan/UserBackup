using System;
using System.Management;
using System.Security.Principal;

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
            using ManagementObjectSearcher Encryption = new ManagementObjectSearcher(@"root\cimv2\Security\MicrosoftVolumeEncryption", "SELECT * FROM Win32_EncryptableVolume");
            foreach (ManagementObject QueryObj in Encryption.Get())
            {
                var drive = QueryObj.GetPropertyValue("DriveLetter").ToString();
                if (!String.Equals(driveParam, drive, StringComparison.OrdinalIgnoreCase)) continue;
                var outParams = QueryObj.InvokeMethod("GetLockStatus", null, null);
                var status = Convert.ToUInt32(outParams["LockStatus"]);
                if (status == 1) return true; // Locked/Encrypted
            }
            return false;
        }

        public static void UnlockDrive(string driveParam) // Requires Admin
        {
            driveParam = driveParam.TrimEnd('\\'); // Remove trailing slash
            using ManagementObjectSearcher Encryption = new ManagementObjectSearcher(@"root\cimv2\Security\MicrosoftVolumeEncryption", "SELECT * FROM Win32_EncryptableVolume");
            foreach (ManagementObject QueryObj in Encryption.Get())
            {
                var drive = QueryObj.GetPropertyValue("DriveLetter").ToString();
                if (!String.Equals(driveParam, drive, StringComparison.OrdinalIgnoreCase)) continue;

                var inParams1 = QueryObj.GetMethodParameters("GetKeyProtectors");
                inParams1["KeyProtectorType"] = (uint)3;
                var outParams1 = QueryObj.InvokeMethod("GetKeyProtectors", inParams1, null);
                var protectors = (string[])outParams1["VolumeKeyProtectorID"];
                Console.WriteLine($"\nKey Identifiers for {driveParam}");
                foreach (var protector in protectors)
                {
                    Console.WriteLine(protector);
                }
                Console.WriteLine("\nUse the above identifiers to obtain a Bitlocker Recovery Key from MBAM. Enter the recovery key below to unlock the drive.\n");
                Console.Write("BL Recovery Key>> ");
                string key = Console.ReadLine().Trim();
                if (key == String.Empty) throw new Exception("Must provide a recovery key!");
                var inParams2 = QueryObj.GetMethodParameters("UnlockWithNumericalPassword");
                inParams2["NumericalPassword"] = key;
                var outParams2 = QueryObj.InvokeMethod("UnlockWithNumericalPassword", inParams2, null);
                switch (Convert.ToUInt32(outParams2["returnValue"]))
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
                        throw new Exception($"Unknown error unlocking drive {driveParam}!");
                }
            }
        }
#pragma warning restore CA1416

    }
}
