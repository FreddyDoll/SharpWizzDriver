using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpWizzDriver;
using SharpWizzDriver.CallParameters;
using SharpWizzDriver.Telemetry;
using System.Diagnostics;
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
using Windows.Gaming.Input;

namespace SharpWizzGamepadControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    [INotifyPropertyChanged]
    public partial class MainWindow : Window
    {
        public BuWizz BuWizz { get; }

        [ObservableProperty]
        GamepadReading? gamepadReading;
        [ObservableProperty]
        double leftThumbX = 0;

        Gamepad? _gamepad;

        IHostApplicationLifetime _lifetime;
        bool _closeAccepted = false;
        ILogger<MainWindow> _logger;
        int _frameCounter = 0;

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

            Gamepad.GamepadAdded += Gamepad_GamepadAdded;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        Stopwatch _swGamepad = Stopwatch.StartNew();
        ExtendedMotorDataArgs GamepadMappingSpaceCrane(TimeSpan elapsed)
        {
            var t = new ExtendedMotorDataArgs();
            if (_gamepad is null || GamepadReading is null)
                return t;

            t.PuMotors[0].TargetValue = GamepadReading.Value.RightThumbstickY * 126; //1 Vorne Heben
            t.PuMotors[1].TargetValue = GamepadReading.Value.LeftThumbstickY * 126; //2 
            t.PuMotors[2].TargetValue = GamepadReading.Value.LeftThumbstickY * 126; //3 Gegengewicht
            t.PuMotors[3].TargetValue = GamepadReading.Value.LeftThumbstickX * 126; //4 vorne knicken

            t.PfMotors[0].TargetValue = GamepadReading.Value.RightTrigger - GamepadReading.Value.LeftTrigger; //A Hoch/runter
            t.PfMotors[1].TargetValue = GamepadReading.Value.RightThumbstickX; //B Drehen

            _logger.LogInformation($"MotorDataUpdated: {_swGamepad.Elapsed.TotalMilliseconds}");
            _swGamepad.Restart();

            return t;
        }

        [RelayCommand]
        async Task RunGamepadControl()
        {
            try
            {
                await BuWizz.RunExtendedMotorData(GamepadMappingSpaceCrane, TimeSpan.FromMilliseconds(200), _lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
            }
        }

        #region callbacks
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_frameCounter % 5 == 0)
            {
                //plotMain.Refresh();
                //plotAccel.Refresh();
                //plotKinematik.Refresh();
                //plotPID.Refresh();
            }
            GamepadReading = _gamepad?.GetCurrentReading();
            LeftThumbX = GamepadReading?.LeftThumbstickX ?? 0.0;

            _frameCounter++;
        }

        private void Gamepad_GamepadAdded(object? sender, Gamepad e)
        {
            _gamepad = e;
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closeAccepted)
                return;

            _closeAccepted = true;
            e.Cancel = true;
            _lifetime.StopApplication();
        }
        #endregion
    }
}