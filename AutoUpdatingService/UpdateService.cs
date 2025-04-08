using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using AutoUpdatingService.Config;
using AutoUpdatingService.Logging;
using AutoUpdatingService.Models;
using AutoUpdatingService.Services;

namespace AutoUpdatingService
{
    public partial class UpdateService : ServiceBase
    {
        private Timer _updateCheckTimer;
        private readonly ServiceLogger _logger;
        private readonly ServiceConfig _config;
        private readonly UpdateChecker _updateChecker;
        private readonly UpdateDownloader _updateDownloader;
        private readonly UpdateInstaller _updateInstaller;
        private bool _updateInProgress = false;
        private CancellationTokenSource _cancellationTokenSource;

        public UpdateService()
        {
            InitializeComponent();
            ServiceName = "AutoUpdatingService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = false;
            
            _logger = new ServiceLogger();
            _config = new ServiceConfig();
            _updateChecker = new UpdateChecker(_logger, _config);
            _updateDownloader = new UpdateDownloader(_logger, _config);
            _updateInstaller = new UpdateInstaller(_logger, _config);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        protected override void OnStart(string[] args)
        {
            _logger.LogInfo("Service starting");
            
            try
            {
                // Schedule update checks based on configured interval
                _updateCheckTimer = new Timer(
                    CheckForUpdates, 
                    null, 
                    TimeSpan.Zero, 
                    TimeSpan.FromMinutes(_config.UpdateCheckIntervalMinutes));
                
                _logger.LogInfo($"Service started. Will check for updates every {_config.UpdateCheckIntervalMinutes} minutes");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error starting service: {ex.Message}", ex);
                Stop();
            }
        }

        protected override void OnStop()
        {
            _logger.LogInfo("Service stopping");
            
            try
            {
                // Cancel any pending operations
                _cancellationTokenSource.Cancel();
                
                // Dispose timer
                _updateCheckTimer?.Dispose();
                _updateCheckTimer = null;
                
                _logger.LogInfo("Service stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping service: {ex.Message}", ex);
            }
        }

        private async void CheckForUpdates(object state)
        {
            if (_updateInProgress)
            {
                _logger.LogInfo("Update already in progress, skipping check");
                return;
            }

            try
            {
                _updateInProgress = true;
                _logger.LogInfo("Checking for updates...");
                
                // Check if an update is available
                VersionInfo latestVersion = await _updateChecker.CheckForUpdatesAsync(_cancellationTokenSource.Token);
                
                if (latestVersion != null && latestVersion.IsNewer)
                {
                    _logger.LogInfo($"New version found: {latestVersion.Version}. Current version: {latestVersion.CurrentVersion}");
                    
                    // Download the update
                    string downloadedFilePath = await _updateDownloader.DownloadUpdateAsync(latestVersion.DownloadUrl, _cancellationTokenSource.Token);
                    
                    if (!string.IsNullOrEmpty(downloadedFilePath))
                    {
                        _logger.LogInfo($"Update downloaded to: {downloadedFilePath}");
                        
                        // Install the update 
                        bool installSuccess = await _updateInstaller.InstallUpdateAsync(downloadedFilePath, latestVersion, _cancellationTokenSource.Token);
                        
                        if (installSuccess)
                        {
                            _logger.LogInfo("Update installed successfully. Restarting service...");
                            RestartService();
                        }
                        else
                        {
                            _logger.LogError("Failed to install update");
                        }
                    }
                    else
                    {
                        _logger.LogError("Failed to download update");
                    }
                }
                else
                {
                    _logger.LogInfo("No updates available");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during update check: {ex.Message}", ex);
            }
            finally
            {
                _updateInProgress = false;
            }
        }
        
        private void RestartService()
        {
            try
            {
                // Create a process to restart the service after we stop
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "cmd.exe",
                    Arguments = $"/C net stop {ServiceName} && net start {ServiceName}",
                    CreateNoWindow = true,
                    UseShellExecute = true
                };
                process.StartInfo = startInfo;
                process.Start();
                
                // Stop this instance of the service
                Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error restarting service: {ex.Message}", ex);
            }
        }
        
        // For testing in debug mode
        internal void TestStartupAndStop(string[] args)
        {
            this.OnStart(args);
            Console.WriteLine("Service started in debug mode. Press any key to stop...");
            Console.ReadKey();
            this.OnStop();
        }
        
        private void InitializeComponent()
        {
            // Service initialization code usually added by the designer
            this.ServiceName = "AutoUpdatingService";
        }
    }
}
