using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace LockedFileOwner
{

    class Program
    {

        public static RM_PROCESS_INFO[] FindLockerProcesses(string path) {
            int handle;
            if (NativeMethods.RmStartSession(out handle, 0, strSessionKey: Guid.NewGuid().ToString()) != RmResult.ERROR_SUCCESS)
                throw new Exception("Could not begin session. Unable to determine file lockers.");

            try {
                string[] resources = { path }; // Just checking on one resource.

                if (NativeMethods.RmRegisterResources(handle, (uint)resources.LongLength, resources, 0, null, 0, null) != RmResult.ERROR_SUCCESS)
                    throw new Exception("Could not register resource.");

                // The first try is done expecting at most ten processes to lock the file.
                uint arraySize = 10;
                RmResult result;
                do {
                    var array = new RM_PROCESS_INFO[arraySize];
                    uint arrayCount;
                    RM_REBOOT_REASON lpdwRebootReasons;
                    result = NativeMethods.RmGetList(handle, out arrayCount, ref arraySize, array, out lpdwRebootReasons);
                    if (result == RmResult.ERROR_SUCCESS) {
                        // Adjust the array length to fit the actual count.

                        Array.Resize(ref array, (int)arrayCount);
                        return array;
                    } else if (result == RmResult.ERROR_MORE_DATA) {
                        // We need to initialize a bigger array. We only set the size, and do another iteration.
                        // (the out parameter arrayCount contains the expected value for the next try)
                        arraySize = arrayCount;
                    } else {
                        throw new Exception("Could not list processes locking resource. Failed to get size of result.");
                    }
                } while (result != RmResult.ERROR_SUCCESS);
            } finally {
                NativeMethods.RmEndSession(handle);
            }
            return new RM_PROCESS_INFO[0];
        }

        private static string GetProcessUser(Process process) {
            IntPtr processHandle = IntPtr.Zero;
            try {
                NativeMethods.OpenProcessToken(process.Handle, 8, out processHandle);
                WindowsIdentity wi = new WindowsIdentity(processHandle);
                string user = wi.Name;
                return user.Contains(@"\") ? user.Substring(user.IndexOf(@"\") + 1) : user;
            } catch {
                return null;
            } finally {
                if (processHandle != IntPtr.Zero) {
                    NativeMethods.CloseHandle(processHandle);
                }
            }
        }

        public static bool IsAdministrator() {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        public static void StartAsAdmin(string fileName,string arguments) {
            var proc = new Process {
                StartInfo =
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                }
            };

            proc.Start();
        }

        private static void ConsoleExit(string message) {
            Console.Error.WriteLine(message);
            Console.ReadKey();
            Environment.Exit(0);
        }

        static void Main(string[] args) {
            string message;
            if (args.Length == 0) {
                message = "No known parameter specified, use LockedFileOwner.exe -h to get help";
                ConsoleExit(message);
            }
            if (args[0].Equals("-h")) {
                message = "LockedFileOwner Help :\n -f  used to specify the filepath of the locked file, the file path must include the file with extension ";
                ConsoleExit(message);
            }
            if (!args[0].Equals("-f") || args.Count() < 2) {
                message = "Parameter not recognized, use LockedFileOwner.exe -h to get help";
                ConsoleExit(message);
            }
            if (!IsAdministrator()) {
                message = "Restart as Administrator";
                ConsoleExit(message);
            }

            string filename = args[1];
            Process processes;
            RM_PROCESS_INFO[] rm = null;

            try {
                rm = FindLockerProcesses(filename);
            } catch (Exception ex) {
                message = "Error getting process handles\nError: " + ex.Message;
                ConsoleExit(message);
            }

            if (rm == null || rm.Length == 0) {
                message = "No process is locking the file";
                ConsoleExit(message);
            }

            Console.WriteLine("Locked File : "+filename);

            for (int i = 0; i < rm.Count(); i++) {
                try {
                    processes = Process.GetProcessById(rm[i].Process.dwProcessId);
                    Console.WriteLine(" Process ID: " + processes.Id + "  Name: " + processes.ProcessName);
                    Console.WriteLine("  Owner: " + GetProcessUser(processes));
                }
                catch (Exception ex) {
                    Console.Error.WriteLine("Process no longer running, unable to get information\nError: " + ex.Message);
                }
            }
            Console.ReadKey();
        }

    }
}