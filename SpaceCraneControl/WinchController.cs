using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Reflection.Metadata;

namespace SpaceCraneControl
{
    public partial class WinchControllerParameters : ObservableObject
    {
        [ObservableProperty]
        string name = "paramSet";

        [ObservableProperty]
        double p = 100.0;
        [ObservableProperty]
        double d = 0;

        [ObservableProperty]
        double maxTarget = 0;
        [ObservableProperty]
        double minTarget = 3.14;

        [ObservableProperty]
        double deadband = 0.01;

        [ObservableProperty]
        double maxOutput = 126;
        [ObservableProperty]
        double minOutput = -126;
    }

    public partial class WinchController : ObservableObject
    {
        public ObservableCollection<WinchControllerParameters> AvailableParameters { get; set; } = new();

        [ObservableProperty]
        WinchControllerParameters parameters = new();

        double lastErr = 0;

        public void Init()
        {
            lastErr = 0;
        }

        public double Process(double targetAngle, double angle)
        {
            var err = targetAngle - angle;
            var diff = lastErr - err;
            lastErr = err;

            var setp = err * Parameters.P + diff * Parameters.D;

            if (setp > Parameters.MaxOutput)
                return Parameters.MaxOutput;
            else if (setp < Parameters.MinOutput)
                return Parameters.MinOutput;
            else if (Math.Abs(err) > Parameters.Deadband)
                return setp;
            else
                return 0;
        }
    }
}