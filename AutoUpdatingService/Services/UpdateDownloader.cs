using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AutoUpdatingService.Config;
using AutoUpdatingService.Logging;

namespace AutoUpdatingService.Services
{
    public class UpdateDownloader
    {
        private readonly ServiceLogger _logger;
        private readonly ServiceConfig _config;
        private readonly HttpClient _httpClient;

        public UpdateDownloader(ServiceLogger logger, ServiceConfig config)
        {
            _logger = logger;
            _config = config;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30)  // Allow longer timeout for large downloads
            };
        }

        public async Task<string> DownloadUpdateAsync(string downloadUrl, CancellationToken cancellationToken)
        {
            string destinationFilePath = null;
            
            try
            {
                _logger.LogInfo($"Downloading update from {downloadUrl}");
                
                // Create a unique filename based on timestamp
                string fileName = $"update_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                destinationFilePath = Path.Combine(_config.DownloadDirectory, fileName);
                
                // Ensure download directory exists
                if (!Directory.Exists(_config.DownloadDirectory))
                {
                    Directory.CreateDirectory(_config.DownloadDirectory);
                }
                
                // Download the file
                HttpResponseMessage response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                // Get content length for progress tracking if available
                long? totalBytes = response.Content.Headers.ContentLength;
                _logger.LogInfo($"Download size: {FormatFileSize(totalBytes ?? 0)}");
                
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    // Buffer for reading data
                    byte[] buffer = new byte[8192];
                    long totalBytesRead = 0;
                    int bytesRead;
                    
                    // Download with progress tracking
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        
                        totalBytesRead += bytesRead;
                        
                        // Log progress for larger files (every 10%)
                        if (totalBytes.HasValue && totalBytes.Value > 1024 * 1024)
                        {
                            double progress = (double)totalBytesRead / totalBytes.Value;
                            if (progress % 0.1 < 0.01)  // Log approximately every 10%
                            {
                                _logger.LogInfo($"Download progress: {progress:P0}");
                            }
                        }
                    }
                }
                
                _logger.LogInfo($"Download completed: {destinationFilePath}");
                
                // Verify downloaded file
                if (VerifyDownloadedFile(destinationFilePath))
                {
                    return destinationFilePath;
                }
                else
                {
                    _logger.LogError("Downloaded file failed verification");
                    File.Delete(destinationFilePath);
                    return null;
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInfo("Download cancelled");
                
                // Clean up incomplete file
                if (!string.IsNullOrEmpty(destinationFilePath) && File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error downloading update: {ex.Message}", ex);
                
                // Clean up incomplete file
                if (!string.IsNullOrEmpty(destinationFilePath) && File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);
                }
                
                return null;
            }
        }

        private bool VerifyDownloadedFile(string filePath)
        {
            try
            {
                // Basic file verification - check if file exists and has content
                FileInfo fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    _logger.LogError($"Downloaded file is empty or does not exist: {filePath}");
                    return false;
                }
                
                // Calculate file hash (SHA256) for logging and potential verification
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    string hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    _logger.LogInfo($"File hash (SHA256): {hashString}");
                }
                
                // TODO: If the server provides a checksum, we could verify it here

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error verifying downloaded file: {ex.Message}", ex);
                return false;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
    }
}
