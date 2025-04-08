using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AutoUpdatingService.Config;
using AutoUpdatingService.Logging;
using AutoUpdatingService.Models;

namespace AutoUpdatingService.Services
{
    public class UpdateChecker
    {
        private readonly ServiceLogger _logger;
        private readonly ServiceConfig _config;
        private readonly HttpClient _httpClient;
        private readonly JavaScriptSerializer _serializer;

        public UpdateChecker(ServiceLogger logger, ServiceConfig config)
        {
            _logger = logger;
            _config = config;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            _serializer = new JavaScriptSerializer();
        }

        public async Task<VersionInfo> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInfo($"Checking for updates at {_config.UpdateCheckUrl}");
                
                // Get current version
                string currentVersion = GetCurrentVersion();
                _logger.LogInfo($"Current version: {currentVersion}");
                
                // Get version info from server
                HttpResponseMessage response = await _httpClient.GetAsync(_config.UpdateCheckUrl, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    VersionInfo versionInfo = _serializer.Deserialize<VersionInfo>(json);
                    
                    // Set current version for comparison
                    versionInfo.CurrentVersion = currentVersion;
                    
                    // Compare versions
                    bool isNewer = CompareVersions(versionInfo.Version, currentVersion);
                    versionInfo.IsNewer = isNewer;
                    
                    _logger.LogInfo($"Server version: {versionInfo.Version}, IsNewer: {versionInfo.IsNewer}");
                    
                    return versionInfo;
                }
                else
                {
                    _logger.LogError($"Failed to get version info: {response.StatusCode}");
                    return null;
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInfo("Update check cancelled");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking for updates: {ex.Message}", ex);
                return null;
            }
        }

        private string GetCurrentVersion()
        {
            try
            {
                // Get version from assembly
                Assembly assembly = Assembly.GetExecutingAssembly();
                Version version = assembly.GetName().Version;
                return version.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting current version: {ex.Message}", ex);
                return "0.0.0.0";
            }
        }

        private bool CompareVersions(string newVersion, string currentVersion)
        {
            try
            {
                Version v1 = new Version(newVersion);
                Version v2 = new Version(currentVersion);
                
                return v1 > v2;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error comparing versions: {ex.Message}", ex);
                return false;
            }
        }
    }
}
