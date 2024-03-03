using CommunityToolkit.Mvvm.ComponentModel;
using SharpWizzDriver.Telemetry;

namespace SharpWizzDriver
{
    public partial class BuWizzPort: ObservableObject
    {
        [ObservableProperty]
        string function = "No Label";
        public string Name { get; }

        public BuWizzPort(string name)
        {
            Name = name;
        }
    }

    //The inheritance reflects that you can use a simple adapter.
    public partial class BuWizzPuPort : BuWizzPfPort
    {
        [ObservableProperty]
        string led = "#00ff00";

        [ObservableProperty]
        bool requestPidTelemetry = false;

        public BuWizzPuPort(string name) : base(name)
        {
        }
    }

    public partial class BuWizzPfPort: BuWizzPort
    {
        [ObservableProperty]
        double currentLimit = 1.5;
        public List<double> SensibleCurrentLimits => new() { 0.5, 0.7, 1.0, 1.2, 1.5, 1.7 };

        public BuWizzPfPort(string name) : base(name)
        {
        }
    }
}
