using System;
using System.Collections.Generic;

namespace AutoUpdatingService.Models
{
    public class VersionInfo
    {
        // Version of the update (e.g. "1.2.3.4")
        public string Version { get; set; }
        
        // Minimum required version to update from
        public string MinimumRequiredVersion { get; set; }
        
        // Release date of this version
        public DateTime ReleaseDate { get; set; }
        
        // URL to download the update package
        public string DownloadUrl { get; set; }
        
        // SHA256 checksum of the update package (for verification)
        public string Checksum { get; set; }
        
        // Size of the update package in bytes
        public long FileSize { get; set; }
        
        // Release notes for this version
        public string ReleaseNotes { get; set; }
        
        // Indicates if this is a mandatory update
        public bool IsMandatory { get; set; }
        
        // Current version of the service (set by the update checker)
        public string CurrentVersion { get; set; }
        
        // Indicates if this version is newer than the current version
        public bool IsNewer { get; set; }
        
        // Additional metadata for the update
        public Dictionary<string, string> Metadata { get; set; }
        
        public VersionInfo()
        {
            Metadata = new Dictionary<string, string>();
        }
    }
}
