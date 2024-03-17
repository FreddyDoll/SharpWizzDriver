using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpWizzDriver;
using SharpWizzDriver.CallParameters;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Windows.Gaming.Input;

namespace SpaceCraneControl
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

        Gamepad? _gamepad;

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

            Gamepad.GamepadAdded += Gamepad_GamepadAdded;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        Stopwatch _swGamepad = Stopwatch.StartNew();
        ExtendedMotorDataArgs GamepadMappingSpaceCrane(TimeSpan elapsed)
        {
            var t = new ExtendedMotorDataArgs();
            if (_gamepad is null || GamepadReading is null)
                return t;

            t.PuMotors[0].TargetValue = (GamepadReading.Value.RightTrigger - GamepadReading.Value.LeftTrigger) * 126;  //Winde
            t.PuMotors[1].TargetValue = GamepadReading.Value.LeftThumbstickX * 126;  //vorne knicken
            t.PuMotors[2].TargetValue = GamepadReading.Value.LeftThumbstickY * 126; //Vorne Heben
            t.PuMotors[3].TargetValue = GamepadReading.Value.RightThumbstickY * 126;  //Gegengewicht

            t.PfMotors[0].TargetValue = 0;
            t.PfMotors[1].TargetValue = GamepadReading.Value.RightThumbstickX; //Drehen

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
            GamepadReading = _gamepad?.GetCurrentReading();

            if (((GamepadReading?.Buttons ?? GamepadButtons.None) & GamepadButtons.View) == GamepadButtons.View)
                if (BuWizz.IsMoving)
                    BuWizz.StopCommand.Execute(null);

            if (((GamepadReading?.Buttons ?? GamepadButtons.None) & GamepadButtons.Menu) == GamepadButtons.Menu)
                if (!BuWizz.IsMoving)
                    RunGamepadControlCommand.Execute(null);
        }

        private void Gamepad_GamepadAdded(object? sender, Gamepad e)
        {
            _gamepad = e;
            _logger.LogInformation($"Gamepad set");
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