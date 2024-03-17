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

        [ObservableProperty]
        double targetAngleHeben = 1;
        [ObservableProperty]
        double targetAngleKnicken = 2;
        public WinchController ContrHeben { get; } = new();
        public WinchController ContrKnicken { get; } = new();

        [ObservableProperty]
        double targetX = 1;
        [ObservableProperty]
        double targetY = 2;
        [ObservableProperty]
        double targetXfromWinch = 1;
        [ObservableProperty]
        double targetYfromWinch = 2;
        [ObservableProperty]
        double currentX = 1;
        [ObservableProperty]
        double currentY = 2;
        public static double TargetXmin => 1.1;
        public static double TargetXmax => 2.5;
        public static double TargetYmin => 1.1;
        public static double TargetYmax => 2.3;
        public static double InnerLength => 1.79;
        public static double OuterLength => 1.3;

        public FilteredAngle AngleHeben { get; } = new();
        public FilteredAngle AngKnicken { get; } = new();

        Gamepad? _gamepad;

        IHostApplicationLifetime _lifetime;
        bool _closeAccepted = false;
        ILogger<MainWindow> _logger;

        TimeSpan _writeTargetsInterval = TimeSpan.FromMilliseconds(200);

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


            ContrHeben.Parameters = new WinchControllerParameters
            {
                Name = "Heben",
                P = 4000,
                D = 0,
                Deadband = 0,
                MinTarget = 0.8,
                MaxTarget = 1.4,
                MinOutput = -127,
                MaxOutput = 127,
            };
            ContrKnicken.Parameters = new WinchControllerParameters
            {
                Name = "Knicken",
                P = 3000,
                D = 0,
                Deadband = 0,
                MinTarget = 1.7,
                MaxTarget = 3,
                MinOutput = -127,
                MaxOutput = 127,
            };


            Gamepad.GamepadAdded += Gamepad_GamepadAdded;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            BuWizz.Connection.TelemtryRecieved += Connection_TelemtryRecieved;
        }
        
        #region commands
        [RelayCommand]
        async Task RunGamepadControl()
        {
            try
            {
                await BuWizz.RunExtendedMotorData(dt => TargetsFromGamepad(), _writeTargetsInterval, _lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, nameof(RunGamepadControl));
            }
        }

        [RelayCommand]
        async Task RunWinchControl()
        {
            try
            {
                TargetAngleHeben = AngleHeben.Angle;
                TargetAngleKnicken = AngKnicken.Angle;

                TargetX = CurrentX;
                TargetY = CurrentY;

                ContrHeben.Init();
                ContrKnicken.Init();

                await BuWizz.RunExtendedMotorData(dt => TargetsFromWinchControllers(), _writeTargetsInterval, _lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(RunWinchControl));
            }
        }

        [RelayCommand]
        async Task RunKinematikControl()
        {
            try
            {
                TargetAngleHeben = AngleHeben.Angle;
                TargetAngleKnicken = AngKnicken.Angle;

                ContrHeben.Init();
                ContrKnicken.Init();

                await BuWizz.RunExtendedMotorData(dt => TargetsFromKinematik(), _writeTargetsInterval, _lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(RunKinematikControl));
            }
        }
        #endregion

        #region MotorTargets
        ExtendedMotorDataArgs TargetsFromGamepad()
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

            return t;
        }

        ExtendedMotorDataArgs TargetsFromWinchControllers()
        {
            var t = new ExtendedMotorDataArgs();

            t.PuMotors[0].TargetValue = 0;  //Winde
            t.PuMotors[1].TargetValue = ContrKnicken.Process(TargetAngleKnicken, AngKnicken.Filtered);  //vorne knicken
            t.PuMotors[2].TargetValue = ContrHeben.Process(TargetAngleHeben, AngleHeben.Filtered); //Vorne Heben
            t.PuMotors[3].TargetValue = 0;  //Gegengewicht

            t.PfMotors[0].TargetValue = 0;
            t.PfMotors[1].TargetValue = 0; //Drehen

            return t;
        }

        ExtendedMotorDataArgs TargetsFromKinematik()
        {
            double maxOffset = 0.04;
            double deadzone = 0.05;
            double speed = 0.002;
            var x = DeadzoneStick((GamepadReading?.LeftThumbstickX) ?? 0.0, deadzone);
            var y = DeadzoneStick(-(GamepadReading?.LeftThumbstickY) ?? 0.0, deadzone);

            var tx = TargetX + x * speed;
            var nTargetX = Math.Clamp(tx, TargetXmin, TargetXmax);

            var ty = TargetY + y * speed;
            var nTargetY = Math.Clamp(ty, TargetYmin, TargetYmax);

            if (x != 0 || y != 0)
            {
                var vecOff = Vec2(TargetX - nTargetX, TargetY - nTargetY);
                if (vecOff.L2Norm() > maxOffset)
                {
                    vecOff = vecOff.Normalize(2) * maxOffset;
                }
                TargetX += vecOff[0];
                TargetY += vecOff[1];
            }
            else
            {
                TargetX = CurrentX;
                TargetY = CurrentY;
            }

            (var h, var k) = InverseKinematik(TargetX, TargetY);

            if (h < ContrHeben.Parameters.MaxTarget && h > ContrHeben.Parameters.MinTarget)
                TargetAngleHeben = h;
            if (k < ContrKnicken.Parameters.MaxTarget && k > ContrKnicken.Parameters.MinTarget)
                TargetAngleKnicken = k;


            var targetsControllers = TargetsFromWinchControllers();

            var targetsGamePad = TargetsFromGamepad();

            targetsGamePad.PuMotors[1] = targetsControllers.PuMotors[1];
            targetsGamePad.PuMotors[2] = targetsControllers.PuMotors[2];

            return targetsGamePad;
        }
        #endregion

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

        private void Connection_TelemtryRecieved(object? sender, SharpWizzDriver.Telemetry.BuWizzTelemtry e)
        {
            AngleHeben.Process(Math.Atan2(e.AccelerometerXAxisValue, e.AccelerometerZAxisValue));

            var knicken = Math.Atan2(e.AccelerometerXAxisValue, e.AccelerometerYAxisValue);
            knicken = 1.879 * knicken - 0.321;
            AngKnicken.Process(knicken);

            (CurrentX, CurrentY) = ForwardKinematik(AngleHeben.Filtered, AngKnicken.Filtered);
            (TargetXfromWinch, TargetYfromWinch) = ForwardKinematik(TargetAngleHeben, TargetAngleKnicken);
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

        #region Helpers

        MathNet.Numerics.LinearAlgebra.Vector<double> Vec2(double x, double y) => MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense([x, y]);
        private double DeadzoneStick(double val, double deadzone)
        {
            if (Math.Abs(val) < deadzone)
                val = 0;
            return val;
        }

        double Squared(double v) => v * v;

        private (double, double) ForwardKinematik(double heben, double knicken)
        {
            var knicken1 = knicken - Math.PI / 2.0 + heben;
            var x = Math.Cos(heben) * InnerLength + Math.Sin(knicken1) * OuterLength;
            var y = Math.Sin(heben) * InnerLength - Math.Cos(knicken1) * OuterLength;
            return (x, y);
        }

        private (double, double) InverseKinematik(double x, double y)
        {
            var knicken = Math.Acos((Squared(InnerLength) + Squared(OuterLength) - (Squared(x) + Squared(y))) / (2 * InnerLength * OuterLength));
            var heben = Math.Asin(y / Math.Sqrt(Squared(x) + Squared(y))) + Math.Acos((Squared(InnerLength) + Squared(x) + Squared(y) - Squared(OuterLength)) / (2 * InnerLength * Math.Sqrt(x * x + y * y)));
            return (heben, knicken);
        }
        #endregion
    }
}