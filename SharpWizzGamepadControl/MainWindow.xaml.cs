using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpWizzDriver;
using SharpWizzDriver.CallParameters;
using SharpWizzDriver.Telemetry;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SharpWizzGamepadControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public BuWizz BuWizz { get; }

        IHostApplicationLifetime _lifetime;
        bool _closeAccepted = false;
        ILogger<MainWindow> _logger;

        public MainWindow(
            ILogger<MainWindow> logger,
            BuWizz buWizz,
            IHostApplicationLifetime lifetime
            )
        {
            _logger = logger;
            _lifetime = lifetime;
            BuWizz = buWizz;
            InitializeComponent();
            DataContext = this;
        }

        MotorDataArgs GetMotorTargets(TimeSpan elapsed)
        {
            _logger.LogInformation($"get targets: {elapsed.TotalSeconds}");
            var t = new MotorDataArgs();
            for (int n = 0; n < t.PfMotors.Length; n++)
                t.PfMotors[n].TargetValue = Math.Sin(elapsed.TotalSeconds);
            return t;
        }

        [RelayCommand]
        async Task RunGamepadControl()
        {
            try
            {
                Queue<MotorDataArgs> path = new Queue<MotorDataArgs> ();
                for (int i = 0; i < 1000; i++)
                {
                    var t = new MotorDataArgs();
                    for (int n = 0; n < t.PfMotors.Length; n++)
                        t.PfMotors[n].TargetValue = Math.Sin(i/100.0);
                    path.Enqueue(t);
                }

                await BuWizz.RunMotorData((t) => path.Dequeue(), TimeSpan.FromMilliseconds(50), _lifetime.ApplicationStopping);
                //await BuWizz.RunMotorData(GetMotorTargets, TimeSpan.FromMilliseconds(50), _lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
            }
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closeAccepted)
                return;

            _closeAccepted = true;
            e.Cancel = true;
            _lifetime.StopApplication();
        }
    }
}