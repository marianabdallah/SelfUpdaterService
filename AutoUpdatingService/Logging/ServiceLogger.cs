using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoUpdatingService.Logging
{
    public class ServiceLogger
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();
        private readonly EventLog _eventLog;
        private const string EventSource = "AutoUpdatingService";
        private const string EventLog = "Application";
        private const int MaxLogFileSize = 10 * 1024 * 1024; // 10 MB

        public ServiceLogger()
        {
            try
            {
                // Create log directory in ProgramData
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AutoUpdatingService",
                    "Logs");
                
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                
                // Create log file with date in name
                string logFileName = $"Service_{DateTime.Now:yyyyMMdd}.log";
                _logFilePath = Path.Combine(logDirectory, logFileName);
                
                // Set up event log source if it doesn't exist
                if (!EventLog.SourceExists(EventSource))
                {
                    EventLog.CreateEventSource(EventSource, EventLog);
                }
                
                _eventLog = new EventLog(EventLog)
                {
                    Source = EventSource
                };
                
                LogInfo("Service logger initialized");
            }
            catch (Exception ex)
            {
                // If we can't set up the logger, at least try to write to the event log
                try
                {
                    if (EventLog.SourceExists(EventSource))
                    {
                        using (EventLog eventLog = new EventLog(EventLog) { Source = EventSource })
                        {
                            eventLog.WriteEntry($"Error initializing logger: {ex.Message}", EventLogEntryType.Error);
                        }
                    }
                }
                catch
                {
                    // We tried our best, but if we can't log anywhere, we can't do much
                }
                
                // Store logs in temp directory as fallback
                string tempDirectory = Path.GetTempPath();
                string logFileName = $"AutoUpdatingService_{DateTime.Now:yyyyMMdd}.log";
                _logFilePath = Path.Combine(tempDirectory, logFileName);
            }
        }

        public void LogInfo(string message)
        {
            LogMessage("INFO", message);
        }

        public void LogWarning(string message)
        {
            LogMessage("WARNING", message);
            
            try
            {
                _eventLog.WriteEntry(message, EventLogEntryType.Warning);
            }
            catch { /* Ignore event log errors */ }
        }

        public void LogError(string message, Exception ex = null)
        {
            string logMessage = message;
            
            if (ex != null)
            {
                logMessage += Environment.NewLine + "Exception: " + ex.Message;
                logMessage += Environment.NewLine + "StackTrace: " + ex.StackTrace;
                
                if (ex.InnerException != null)
                {
                    logMessage += Environment.NewLine + "InnerException: " + ex.InnerException.Message;
                    logMessage += Environment.NewLine + "InnerStackTrace: " + ex.InnerException.StackTrace;
                }
            }
            
            LogMessage("ERROR", logMessage);
            
            try
            {
                _eventLog.WriteEntry(logMessage, EventLogEntryType.Error);
            }
            catch { /* Ignore event log errors */ }
        }

        private void LogMessage(string level, string message)
        {
            try
            {
                // Create timestamp and format message
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string threadId = Thread.CurrentThread.ManagedThreadId.ToString();
                string formattedMessage = $"[{timestamp}] [{level}] [Thread:{threadId}] {message}";
                
                // Log to file with lock to prevent concurrent access issues
                lock (_lockObject)
                {
                    // Check if log file has exceeded max size
                    if (File.Exists(_logFilePath))
                    {
                        FileInfo logFileInfo = new FileInfo(_logFilePath);
                        
                        if (logFileInfo.Length > MaxLogFileSize)
                        {
                            // Rotate log file - rename current log and create a new one
                            string archiveFileName = Path.Combine(
                                Path.GetDirectoryName(_logFilePath),
                                $"Service_{DateTime.Now:yyyyMMdd_HHmmss}.log.bak");
                            
                            File.Move(_logFilePath, archiveFileName);
                        }
                    }
                    
                    // Append to log file
                    using (StreamWriter writer = new StreamWriter(_logFilePath, true, Encoding.UTF8))
                    {
                        writer.WriteLine(formattedMessage);
                    }
                }
            }
            catch
            {
                // If we can't log to file, try event log as fallback
                try
                {
                    _eventLog.WriteEntry($"Failed to write to log file. Original message: {message}", EventLogEntryType.Warning);
                }
                catch
                {
                    // We tried our best
                }
            }
        }
    }
}
