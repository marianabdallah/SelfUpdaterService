using System;
using System.IO;
using System.ServiceProcess;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

namespace AutoUpdatingService
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                // If the service is run in user interactive mode (e.g. debugging)
                HandleCommandLineArgs(args);
            }
            else
            {
                // Run as a service
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new UpdateService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }

        private static void HandleCommandLineArgs(string[] args)
        {
            if (args.Length > 0)
            {
                string command = args[0].ToLower();
                
                switch (command)
                {
                    case "-install":
                        // Install service logic here (could use InstallUtil.exe)
                        Console.WriteLine("Installing service...");
                        System.Configuration.Install.ManagedInstallerClass.InstallHelper(new string[] { System.Reflection.Assembly.GetExecutingAssembly().Location });
                        Console.WriteLine("Service installed successfully.");
                        break;
                    
                    case "-uninstall":
                        // Uninstall service logic here
                        Console.WriteLine("Uninstalling service...");
                        System.Configuration.Install.ManagedInstallerClass.InstallHelper(new string[] { "/u", System.Reflection.Assembly.GetExecutingAssembly().Location });
                        Console.WriteLine("Service uninstalled successfully.");
                        break;
                    
                    case "-debug":
                        // Debug mode for testing
                        Console.WriteLine("Starting service in debug mode...");
                        var service = new UpdateService();
                        service.TestStartupAndStop(args.Skip(1).ToArray());
                        Console.WriteLine("Press any key to exit...");
                        Console.ReadKey();
                        break;
                        
                    case "-applyupdate":
                        // Update executor mode - this is used when we want to apply an update after the service stops
                        if (args.Length > 1)
                        {
                            Console.WriteLine("Applying service update...");
                            ApplyServiceUpdate(args[1]);
                        }
                        else
                        {
                            Console.WriteLine("Error: Update parameters file not specified");
                        }
                        break;
                    
                    default:
                        Console.WriteLine("Unknown command. Available commands:");
                        Console.WriteLine("-install     : Install the service");
                        Console.WriteLine("-uninstall   : Uninstall the service");
                        Console.WriteLine("-debug       : Run the service in debug mode");
                        Console.WriteLine("-applyupdate : Apply a pending update (internal use)");
                        break;
                }
            }
            else
            {
                Console.WriteLine("No command specified. Available commands:");
                Console.WriteLine("-install     : Install the service");
                Console.WriteLine("-uninstall   : Uninstall the service");
                Console.WriteLine("-debug       : Run the service in debug mode");
                Console.WriteLine("-applyupdate : Apply a pending update (internal use)");
            }
        }
        
        private static void ApplyServiceUpdate(string paramsFilePath)
        {
            try
            {
                Console.WriteLine("Reading update parameters...");
                if (!File.Exists(paramsFilePath))
                {
                    Console.WriteLine($"Error: Parameters file {paramsFilePath} not found");
                    return;
                }
                
                string json = File.ReadAllText(paramsFilePath);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                dynamic updateParams = serializer.Deserialize<dynamic>(json);
                
                string serviceName = updateParams["ServiceName"];
                string sourcePath = updateParams["SourcePath"];
                string targetPath = updateParams["TargetPath"];
                string backupPath = updateParams["BackupPath"];
                
                // Wait a moment to ensure service is fully stopped
                Console.WriteLine("Waiting for service to stop completely...");
                Thread.Sleep(5000);
                
                bool updateSuccess = false;
                try
                {
                    // Try to install the update
                    Console.WriteLine("Installing update files...");
                    if (!Directory.Exists(sourcePath))
                    {
                        throw new DirectoryNotFoundException($"Source directory {sourcePath} not found");
                    }
                    
                    if (!Directory.Exists(targetPath))
                    {
                        throw new DirectoryNotFoundException($"Target directory {targetPath} not found");
                    }
                    
                    CopyDirectory(sourcePath, targetPath);
                    updateSuccess = true;
                    Console.WriteLine("Update files installed successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during update installation: {ex.Message}");
                    Console.WriteLine("Rolling back to previous version...");
                    
                    try
                    {
                        // Attempt to restore from backup
                        if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                        {
                            CopyDirectory(backupPath, targetPath);
                            Console.WriteLine("Rollback successful");
                        }
                        else
                        {
                            Console.WriteLine("WARNING: Could not locate backup for rollback");
                        }
                    }
                    catch (Exception rollbackEx)
                    {
                        Console.WriteLine($"CRITICAL ERROR: Rollback failed: {rollbackEx.Message}");
                        // At this point we're in trouble, but we'll still try to restart the service
                    }
                }
                
                // Always attempt to restart the service, whether update succeeded or not
                try
                {
                    Console.WriteLine($"Restarting service: {serviceName}...");
                    using (Process process = new Process())
                    {
                        process.StartInfo = new ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = $"start {serviceName}",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        
                        process.Start();
                        process.WaitForExit(30000); // Wait up to 30 seconds
                        
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        
                        if (process.ExitCode != 0)
                        {
                            Console.WriteLine($"Warning: Could not restart service: {error}");
                            Console.WriteLine("Service may need to be started manually.");
                        }
                        else
                        {
                            Console.WriteLine("Service restarted successfully");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error restarting service: {ex.Message}");
                    Console.WriteLine("Service may need to be started manually.");
                }
                
                // Record update result
                try
                {
                    string resultPath = Path.Combine(
                        Path.GetDirectoryName(paramsFilePath), 
                        "update_result.json");
                    
                    var result = new {
                        Success = updateSuccess,
                        CompletionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ErrorMessage = updateSuccess ? null : "See logs for details"
                    };
                    
                    File.WriteAllText(resultPath, serializer.Serialize(result));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error recording update result: {ex.Message}");
                    // Best effort to record result
                }
                
                // Clean up
                try 
                {
                    if (updateSuccess && !string.IsNullOrEmpty(sourcePath) && Directory.Exists(sourcePath))
                    {
                        Console.WriteLine("Cleaning up extracted update files...");
                        Directory.Delete(sourcePath, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error cleaning up: {ex.Message}");
                    // Continue anyway
                }
                
                Console.WriteLine($"Update process complete. Result: {(updateSuccess ? "SUCCESS" : "FAILED")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical error in update process: {ex.Message}");
                
                // Last-ditch attempt to restart service
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo = new ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = "start AutoUpdatingService",
                            UseShellExecute = true
                        };
                        process.Start();
                    }
                }
                catch
                {
                    Console.WriteLine("CRITICAL: Could not restart service. Manual intervention required.");
                }
            }
        }
        
        private static void CopyDirectory(string sourceDir, string destDir)
        {
            // Create the destination directory
            Directory.CreateDirectory(destDir);
            
            // Copy all files
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                string destFile = Path.Combine(destDir, fileName);
                
                // Delete the existing file if it exists
                if (File.Exists(destFile))
                {
                    try
                    {
                        File.Delete(destFile);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not delete existing file {destFile}: {ex.Message}");
                        // Continue anyway
                    }
                }
                
                // Copy the new file
                File.Copy(filePath, destFile, true);
            }
            
            // Copy subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dirPath);
                string destSubDir = Path.Combine(destDir, dirName);
                
                // Delete the existing directory if it exists
                if (Directory.Exists(destSubDir))
                {
                    try
                    {
                        Directory.Delete(destSubDir, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not delete existing directory {destSubDir}: {ex.Message}");
                        // Continue anyway
                    }
                }
                
                // Copy the new directory
                CopyDirectory(dirPath, destSubDir);
            }
        }
    }
}
