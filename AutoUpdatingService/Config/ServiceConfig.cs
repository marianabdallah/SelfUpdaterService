using System;
using System.Configuration;
using System.IO;
using AutoUpdatingService.Logging;

namespace AutoUpdatingService.Config
{
    public class ServiceConfig
    {
        private readonly ServiceLogger _logger;

        // Default configuration values
        public string UpdateCheckUrl { get; private set; } = "http://example.com/updates/version.json";
        public int UpdateCheckIntervalMinutes { get; private set; } = 60;
        public string DownloadDirectory { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AutoUpdatingService", 
            "Downloads");
        public string BackupDirectory { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AutoUpdatingService", 
            "Backups");
        public string LogDirectory { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
            "AutoUpdatingService", 
            "Logs");
        public int MaxUpdateHistoryCount { get; private set; } = 10;
        public string UpdateHistoryFilePath { get; private set; }

        public ServiceConfig(ServiceLogger logger = null)
        {
            _logger = logger ?? new ServiceLogger();
            LoadConfiguration();
            EnsureDirectoriesExist();
        }

        private void LoadConfiguration()
        {
            try
            {
                UpdateCheckUrl = GetConfigValue("UpdateCheckUrl", UpdateCheckUrl);
                UpdateCheckIntervalMinutes = GetConfigValue("UpdateCheckIntervalMinutes", UpdateCheckIntervalMinutes);
                DownloadDirectory = GetConfigValue("DownloadDirectory", DownloadDirectory);
                BackupDirectory = GetConfigValue("BackupDirectory", BackupDirectory);
                LogDirectory = GetConfigValue("LogDirectory", LogDirectory);
                MaxUpdateHistoryCount = GetConfigValue("MaxUpdateHistoryCount", MaxUpdateHistoryCount);
                
                UpdateHistoryFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AutoUpdatingService", 
                    "updateHistory.json");
                
                _logger.LogInfo("Configuration loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading configuration: {ex.Message}", ex);
                // Fall back to default values
            }
        }

        private string GetConfigValue(string key, string defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private int GetConfigValue(string key, int defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrEmpty(value) || !int.TryParse(value, out int result))
            {
                return defaultValue;
            }
            return result;
        }

        private void EnsureDirectoriesExist()
        {
            try
            {
                EnsureDirectoryExists(DownloadDirectory);
                EnsureDirectoryExists(BackupDirectory);
                EnsureDirectoryExists(LogDirectory);
                EnsureDirectoryExists(Path.GetDirectoryName(UpdateHistoryFilePath));
                
                _logger.LogInfo("Service directories verified");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating directories: {ex.Message}", ex);
                throw;
            }
        }

        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                _logger.LogInfo($"Created directory: {path}");
            }
        }
    }
}
