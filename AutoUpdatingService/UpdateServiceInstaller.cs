using System;
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace AutoUpdatingService
{
    [RunInstaller(true)]
    public class UpdateServiceInstaller : Installer
    {
        private ServiceProcessInstaller processInstaller;
        private ServiceInstaller serviceInstaller;

        public UpdateServiceInstaller()
        {
            try
            {
                // Service Process Installer
                processInstaller = new ServiceProcessInstaller();
                processInstaller.Account = ServiceAccount.LocalSystem;
                processInstaller.Username = null;
                processInstaller.Password = null;

                // Service Installer
                serviceInstaller = new ServiceInstaller();
                serviceInstaller.DisplayName = "Auto Updating Service";
                serviceInstaller.Description = "A Windows service that checks for and installs updates automatically";
                serviceInstaller.ServiceName = "AutoUpdatingService";
                serviceInstaller.StartType = ServiceStartMode.Automatic;
                
                // This allows the service to handle power events and prevents OS from killing it
                serviceInstaller.ServicesDependedOn = new string[] { "Tcpip", "Dhcp" };
                serviceInstaller.DelayedAutoStart = true;

                // Add installers to collection
                Installers.Add(processInstaller);
                Installers.Add(serviceInstaller);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during installer initialization: {ex.Message}");
                throw;
            }
        }
    }
}
