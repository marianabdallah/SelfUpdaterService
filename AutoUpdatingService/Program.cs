using System;
using System.ServiceProcess;
using System.Linq;

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
                    
                    default:
                        Console.WriteLine("Unknown command. Available commands:");
                        Console.WriteLine("-install   : Install the service");
                        Console.WriteLine("-uninstall : Uninstall the service");
                        Console.WriteLine("-debug     : Run the service in debug mode");
                        break;
                }
            }
            else
            {
                Console.WriteLine("No command specified. Available commands:");
                Console.WriteLine("-install   : Install the service");
                Console.WriteLine("-uninstall : Uninstall the service");
                Console.WriteLine("-debug     : Run the service in debug mode");
            }
        }
    }
}
