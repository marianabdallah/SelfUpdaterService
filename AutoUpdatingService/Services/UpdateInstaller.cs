using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AutoUpdatingService.Config;
using AutoUpdatingService.Logging;
using AutoUpdatingService.Models;

namespace AutoUpdatingService.Services
{
    public class UpdateInstaller
    {
        private readonly ServiceLogger _logger;
        private readonly ServiceConfig _config;
        private readonly JavaScriptSerializer _serializer;
        private readonly string _serviceDirectory;

        public UpdateInstaller(ServiceLogger logger, ServiceConfig config)
        {
            _logger = logger;
            _config = config;
            _serializer = new JavaScriptSerializer();
            
            // Get the directory where the service is installed
            _serviceDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public async Task<bool> InstallUpdateAsync(string updateFilePath, VersionInfo versionInfo, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInfo($"Installing update from {updateFilePath}");
                
                // Create backup of current installation
                string backupPath = CreateBackup();
                if (string.IsNullOrEmpty(backupPath))
                {
                    _logger.LogError("Failed to create backup, aborting update");
                    return false;
                }
                
                // Extract update to temporary location
                string tempExtractPath = Path.Combine(_config.DownloadDirectory, $"extract_{DateTime.Now:yyyyMMdd_HHmmss}");
                
                if (await ExtractUpdateAsync(updateFilePath, tempExtractPath, cancellationToken))
                {
                    // Verify extracted files
                    if (!VerifyExtractedFiles(tempExtractPath))
                    {
                        _logger.LogError("Extracted files failed verification, aborting update");
                        CleanupTempDirectories(tempExtractPath);
                        return false;
                    }
                    
                    // Prepare update parameters and launch external updater
                    if (!PrepareUpdateAndLaunchInstaller(tempExtractPath, backupPath, versionInfo))
                    {
                        _logger.LogError("Failed to prepare update, aborting");
                        CleanupTempDirectories(tempExtractPath);
                        return false;
                    }
                    
                    // Now we can stop the service - the external process will handle the rest
                    if (!StopService())
                    {
                        _logger.LogError("Failed to stop service after launching updater. External updater will attempt to complete the update anyway.");
                        // We continue regardless because the external updater is already running
                    }
                    
                    _logger.LogInfo("Update process handed off to external updater");
                    
                    // Don't clean up temp directories here, the external updater will do that
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to extract update files");
                    CleanupTempDirectories(tempExtractPath);
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInfo("Installation cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error installing update: {ex.Message}", ex);
                return false;
            }
        }

        private string CreateBackup()
        {
            try
            {
                _logger.LogInfo("Creating backup of current installation");
                
                // Create backup directory
                string backupDirectory = Path.Combine(_config.BackupDirectory, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(backupDirectory);
                
                // Copy all files from service directory to backup directory
                foreach (string filePath in Directory.GetFiles(_serviceDirectory))
                {
                    string fileName = Path.GetFileName(filePath);
                    string destFile = Path.Combine(backupDirectory, fileName);
                    File.Copy(filePath, destFile, true);
                }
                
                // Backup subdirectories (if any)
                foreach (string dirPath in Directory.GetDirectories(_serviceDirectory))
                {
                    string dirName = Path.GetFileName(dirPath);
                    string destDir = Path.Combine(backupDirectory, dirName);
                    CopyDirectory(dirPath, destDir);
                }
                
                _logger.LogInfo($"Backup created at {backupDirectory}");
                return backupDirectory;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating backup: {ex.Message}", ex);
                return null;
            }
        }

        private async Task<bool> ExtractUpdateAsync(string updateFilePath, string extractPath, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInfo($"Extracting update to {extractPath}");
                
                // Create extract directory
                Directory.CreateDirectory(extractPath);
                
                // Extract the update using Task.Run to do this on a background thread
                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(updateFilePath, extractPath);
                }, cancellationToken);
                
                _logger.LogInfo("Update extracted successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error extracting update: {ex.Message}", ex);
                return false;
            }
        }

        private bool VerifyExtractedFiles(string extractPath)
        {
            try
            {
                _logger.LogInfo("Verifying extracted files");
                
                // Check if essential files exist
                string executablePath = Path.Combine(extractPath, "AutoUpdatingService.exe");
                string configPath = Path.Combine(extractPath, "AutoUpdatingService.exe.config");
                
                if (!File.Exists(executablePath))
                {
                    _logger.LogError("Missing executable in the update package");
                    return false;
                }
                
                if (!File.Exists(configPath))
                {
                    _logger.LogError("Missing configuration file in the update package");
                    return false;
                }
                
                // TODO: Add more specific verifications as needed for your service
                
                _logger.LogInfo("Extracted files verified successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error verifying extracted files: {ex.Message}", ex);
                return false;
            }
        }

        private bool StopService()
        {
            try
            {
                _logger.LogInfo("Stopping service");
                
                // Use SC command to stop the service
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"stop AutoUpdatingService",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit(30000); // Wait up to 30 seconds
                    
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"Failed to stop service: {error}");
                        return false;
                    }
                }
                
                _logger.LogInfo("Service stopped successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping service: {ex.Message}", ex);
                return false;
            }
        }

        private bool InstallFiles(string sourcePath)
        {
            try
            {
                _logger.LogInfo("Installing update files");
                
                // Copy all files from the extracted directory to the service directory
                foreach (string filePath in Directory.GetFiles(sourcePath))
                {
                    string fileName = Path.GetFileName(filePath);
                    string destFile = Path.Combine(_serviceDirectory, fileName);
                    
                    // Delete the existing file if it exists
                    if (File.Exists(destFile))
                    {
                        File.Delete(destFile);
                    }
                    
                    // Copy the new file
                    File.Copy(filePath, destFile, true);
                }
                
                // Copy subdirectories
                foreach (string dirPath in Directory.GetDirectories(sourcePath))
                {
                    string dirName = Path.GetFileName(dirPath);
                    string destDir = Path.Combine(_serviceDirectory, dirName);
                    
                    // Delete the existing directory if it exists
                    if (Directory.Exists(destDir))
                    {
                        Directory.Delete(destDir, true);
                    }
                    
                    // Copy the new directory
                    CopyDirectory(dirPath, destDir);
                }
                
                _logger.LogInfo("Update files installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error installing update files: {ex.Message}", ex);
                return false;
            }
        }

        private bool RestoreFromBackup(string backupPath)
        {
            try
            {
                _logger.LogInfo($"Restoring from backup: {backupPath}");
                
                // Copy all files from the backup directory to the service directory
                foreach (string filePath in Directory.GetFiles(backupPath))
                {
                    string fileName = Path.GetFileName(filePath);
                    string destFile = Path.Combine(_serviceDirectory, fileName);
                    
                    // Delete the existing file if it exists
                    if (File.Exists(destFile))
                    {
                        File.Delete(destFile);
                    }
                    
                    // Copy the backup file
                    File.Copy(filePath, destFile, true);
                }
                
                // Copy subdirectories
                foreach (string dirPath in Directory.GetDirectories(backupPath))
                {
                    string dirName = Path.GetFileName(dirPath);
                    string destDir = Path.Combine(_serviceDirectory, dirName);
                    
                    // Delete the existing directory if it exists
                    if (Directory.Exists(destDir))
                    {
                        Directory.Delete(destDir, true);
                    }
                    
                    // Copy the backup directory
                    CopyDirectory(dirPath, destDir);
                }
                
                _logger.LogInfo("Restore from backup completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error restoring from backup: {ex.Message}", ex);
                return false;
            }
        }

        private void CleanupTempDirectories(string tempExtractPath)
        {
            try
            {
                _logger.LogInfo("Cleaning up temporary directories");
                
                if (Directory.Exists(tempExtractPath))
                {
                    Directory.Delete(tempExtractPath, true);
                }
                
                // Remove old backups (keep last N backups as configured)
                CleanupOldBackups();
                
                // Remove old downloaded updates (keep last N updates as configured)
                CleanupOldDownloads();
                
                _logger.LogInfo("Cleanup completed");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during cleanup: {ex.Message}", ex);
                // Continue even if cleanup fails
            }
        }

        private void CleanupOldBackups()
        {
            try
            {
                if (!Directory.Exists(_config.BackupDirectory))
                {
                    return;
                }
                
                // Get all backup directories sorted by creation time (descending)
                DirectoryInfo[] backupDirs = new DirectoryInfo(_config.BackupDirectory)
                    .GetDirectories("backup_*")
                    .OrderByDescending(d => d.CreationTime)
                    .ToArray();
                
                // Delete all but the latest N backups
                int maxBackups = _config.MaxUpdateHistoryCount;
                for (int i = maxBackups; i < backupDirs.Length; i++)
                {
                    _logger.LogInfo($"Deleting old backup: {backupDirs[i].FullName}");
                    backupDirs[i].Delete(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cleaning up old backups: {ex.Message}", ex);
            }
        }

        private void CleanupOldDownloads()
        {
            try
            {
                if (!Directory.Exists(_config.DownloadDirectory))
                {
                    return;
                }
                
                // Get all downloaded update files sorted by creation time (descending)
                FileInfo[] downloadFiles = new DirectoryInfo(_config.DownloadDirectory)
                    .GetFiles("update_*.zip")
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();
                
                // Delete all but the latest N downloads
                int maxDownloads = _config.MaxUpdateHistoryCount;
                for (int i = maxDownloads; i < downloadFiles.Length; i++)
                {
                    _logger.LogInfo($"Deleting old download: {downloadFiles[i].FullName}");
                    downloadFiles[i].Delete();
                }
                
                // Delete any temporary extraction directories
                DirectoryInfo[] extractDirs = new DirectoryInfo(_config.DownloadDirectory)
                    .GetDirectories("extract_*")
                    .OrderByDescending(d => d.CreationTime)
                    .ToArray();
                
                for (int i = 1; i < extractDirs.Length; i++) // Keep only the most recent one in case needed for debugging
                {
                    _logger.LogInfo($"Deleting old extraction directory: {extractDirs[i].FullName}");
                    extractDirs[i].Delete(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cleaning up old downloads: {ex.Message}", ex);
            }
        }

        private bool PrepareUpdateAndLaunchInstaller(string tempExtractPath, string backupPath, VersionInfo versionInfo)
        {
            try
            {
                _logger.LogInfo("Preparing to launch external updater process");
                
                // Copy the updater utility if it exists in the extracted files
                string updaterPath = Path.Combine(_serviceDirectory, "ServiceUpdater.exe");
                
                // If no updater found, use a copy of the main exe with a different name
                if (!File.Exists(updaterPath))
                {
                    string mainExecutable = Path.Combine(_serviceDirectory, "AutoUpdatingService.exe");
                    if (File.Exists(mainExecutable))
                    {
                        File.Copy(mainExecutable, updaterPath, true);
                        _logger.LogInfo("Created ServiceUpdater.exe from AutoUpdatingService.exe");
                    }
                    else
                    {
                        _logger.LogError("Could not find main executable for creating updater");
                        return false;
                    }
                }
                
                // Create an update parameters file with all the info the updater needs
                string paramsFile = Path.Combine(_config.DownloadDirectory, "update_params.json");
                var updateParams = new
                {
                    ServiceName = "AutoUpdatingService",
                    SourcePath = tempExtractPath,
                    TargetPath = _serviceDirectory,
                    BackupPath = backupPath,
                    Version = versionInfo.Version,
                    PreviousVersion = versionInfo.CurrentVersion,
                    UpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ReleaseNotes = versionInfo.ReleaseNotes
                };
                
                // Save the update parameters to a JSON file
                string json = _serializer.Serialize(updateParams);
                File.WriteAllText(paramsFile, json);
                
                // Launch the updater process
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = $"-applyupdate \"{paramsFile}\"",
                    CreateNoWindow = false,
                    UseShellExecute = true // Run with elevated privileges
                };
                process.StartInfo = startInfo;
                
                _logger.LogInfo("Launching external updater process");
                bool started = process.Start();
                
                if (started)
                {
                    _logger.LogInfo("External updater process launched successfully");
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to launch external updater process");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error preparing update: {ex.Message}", ex);
                return false;
            }
        }
        
        private void CopyDirectory(string sourceDir, string destDir)
        {
            // Create the destination directory
            Directory.CreateDirectory(destDir);
            
            // Copy all files
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                string destFile = Path.Combine(destDir, fileName);
                File.Copy(filePath, destFile, true);
            }
            
            // Copy all subdirectories
            foreach (string subDirPath in Directory.GetDirectories(sourceDir))
            {
                string subDirName = Path.GetFileName(subDirPath);
                string destSubDir = Path.Combine(destDir, subDirName);
                CopyDirectory(subDirPath, destSubDir);
            }
        }

        private async Task RecordUpdateHistoryAsync(VersionInfo versionInfo)
        {
            try
            {
                _logger.LogInfo("Recording update in history");
                
                // Create an update history record
                UpdateHistory updateHistory = await LoadUpdateHistoryAsync();
                
                // Add the new update
                updateHistory.Updates.Add(new UpdateRecord
                {
                    Version = versionInfo.Version,
                    PreviousVersion = versionInfo.CurrentVersion,
                    UpdateDate = DateTime.Now,
                    ReleaseNotes = versionInfo.ReleaseNotes
                });
                
                // Trim the history to the maximum number of entries
                if (updateHistory.Updates.Count > _config.MaxUpdateHistoryCount)
                {
                    updateHistory.Updates = updateHistory.Updates
                        .OrderByDescending(u => u.UpdateDate)
                        .Take(_config.MaxUpdateHistoryCount)
                        .ToList();
                }
                
                // Save the updated history
                await SaveUpdateHistoryAsync(updateHistory);
                
                _logger.LogInfo("Update recorded in history");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error recording update history: {ex.Message}", ex);
                // Continue even if recording history fails
            }
        }

        private async Task<UpdateHistory> LoadUpdateHistoryAsync()
        {
            try
            {
                if (File.Exists(_config.UpdateHistoryFilePath))
                {
                    string json = await File.ReadAllTextAsync(_config.UpdateHistoryFilePath);
                    return _serializer.Deserialize<UpdateHistory>(json) ?? new UpdateHistory();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading update history: {ex.Message}", ex);
            }
            
            return new UpdateHistory();
        }

        private async Task SaveUpdateHistoryAsync(UpdateHistory updateHistory)
        {
            try
            {
                string json = _serializer.Serialize(updateHistory);
                await File.WriteAllTextAsync(_config.UpdateHistoryFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving update history: {ex.Message}", ex);
            }
        }
    }
}
