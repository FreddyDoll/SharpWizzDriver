using CommunityToolkit.Mvvm.ComponentModel;

namespace SharpWizzDriver
{
    public partial class BuWizzState:ObservableObject
    {
        [ObservableProperty]
        string name = "BuWizz3";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PuPorts))]
        [NotifyPropertyChangedFor(nameof(PfPorts))]
        BuWizzPort[] ports;

        [ObservableProperty]
        TimeSpan targetTransferPeriod = TimeSpan.FromMilliseconds(50);

        public IEnumerable<BuWizzPuPort> PuPorts => Ports.Select(p => p as BuWizzPuPort).Where(p => p is not null).Cast<BuWizzPuPort>();
        public IEnumerable<BuWizzPfPort> PfPorts => Ports.Select(p => p as BuWizzPfPort).Where(p => p is not null).Cast<BuWizzPfPort>();

        public BuWizzState()
        {
            Ports =
            [
                new BuWizzPuPort("1"),
                new BuWizzPuPort("2"),
                new BuWizzPuPort("3"),
                new BuWizzPuPort("4"),
                new BuWizzPfPort("A"),
                new BuWizzPfPort("B"),
            ];
        }
    }
}
