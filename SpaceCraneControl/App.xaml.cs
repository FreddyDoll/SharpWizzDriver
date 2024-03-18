using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpWizzDriver;
using System.Windows;
using Windows.Devices.Enumeration;

namespace SpaceCraneControl
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                var host = Host.CreateDefaultBuilder()
                      .ConfigureServices(ConfigureSevices).Build();

                var logger = host.Services.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Host Building Complete");

                var state = host.Services.GetRequiredService<BuWizzState>();
                foreach (var pu in state.PuPorts)
                    pu.Mode = PuPortFunction.PuSpeedServo;
                state.Ports[0].Function = "Winde";
                state.Ports[1].Function = "Vorne Knicken";
                state.Ports[2].Function = "Vorne Heben";
                state.Ports[3].Function = "Gegengewicht";
                state.Ports[4].Function = "";
                state.Ports[5].Function = "Drehen";
                state.Name = "BuWizz3";

                var mainWindow = host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();

                await host.StartAsync();
                logger.LogInformation("Waiting For Host Stopping");

                var deviceWatcher = host.Services.GetRequiredService<DeviceWatcher>();
                deviceWatcher.Start();
                logger.LogInformation("BLE Watcher started");

                await host.WaitForShutdownAsync();
                logger.LogInformation("Host Stopped");

                mainWindow.Close();

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unhandled Exception in Application:{ex.Message}");
            }
        }

        void ConfigureSevices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddSingleton<BuWizzConnection>();
            services.AddSingleton<BuWizzState>();
            services.AddSingleton(s => CreateBLEDeviceWatcher(s));
            services.AddSingleton<BuWizz>();
            services.AddSingleton<MainWindow>();
        }

        private DeviceWatcher CreateBLEDeviceWatcher(IServiceProvider s)
        {
            string[] requestedProperties =
            {
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.Bluetooth.Le.IsConnectable"
            };
            string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

            var ret = DeviceInformation.CreateWatcher(
                aqsAllBluetoothLEDevices,
                requestedProperties,
                DeviceInformationKind.AssociationEndpoint
            );

            return ret;
        }
    }

}
