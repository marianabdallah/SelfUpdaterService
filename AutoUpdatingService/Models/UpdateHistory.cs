using System;
using System.Collections.Generic;

namespace AutoUpdatingService.Models
{
    public class UpdateHistory
    {
        public List<UpdateRecord> Updates { get; set; } = new List<UpdateRecord>();
    }

    public class UpdateRecord
    {
        // Version that was installed
        public string Version { get; set; }
        
        // Previous version (before update)
        public string PreviousVersion { get; set; }
        
        // Date and time when the update was installed
        public DateTime UpdateDate { get; set; }
        
        // Release notes for this version
        public string ReleaseNotes { get; set; }
        
        // Indicates if the update was successful
        public bool WasSuccessful { get; set; } = true;
        
        // Error message if the update failed
        public string ErrorMessage { get; set; }
    }
}
