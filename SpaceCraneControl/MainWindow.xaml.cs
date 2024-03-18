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
                TargetX = CurrentX;
                TargetY = CurrentY;

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

        [RelayCommand]
        async Task RunDerivativeControl()
        {
            try
            {
                foreach (var item in BuWizz.State.PuPorts)
                    item.Mode = PuPortFunction.PuSpeedServo;

                await BuWizz.RunExtendedMotorData(dt => TargetsFromGamepadDerivative(), _writeTargetsInterval, _lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(RunDerivativeControl));
            }
        }

        [RelayCommand]
        async Task RunSpeedCalibration()
        {
            try
            {
                foreach (var item in BuWizz.State.PuPorts)
                    item.Mode = PuPortFunction.PuSpeedServo;

                await Task.Delay(1000);

                var speed = 20.0;
                var t = 10.0;
                _logger.LogInformation($"Knicken:");
                await Measure(AngKnicken, [0, speed, 0, 0], t);
                await Measure(AngKnicken,[0, -speed, 0, 0], t);
                _logger.LogInformation($"Heben:");
                await Measure(AngleHeben, [0, 0, speed, 0], t);
                await Measure(AngleHeben, [0, 0, -speed, 0], t);

            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(RunSpeedCalibration));
            }
        }

        private async Task<CancellationTokenSource> Measure(FilteredAngle ang,double[] speeds, double t)
        {
            var ang1 = ang.Angle;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
            cts.CancelAfter(TimeSpan.FromSeconds(t));
            await BuWizz.RunExtendedMotorData(dt => ConstantSpeedPU(speeds), _writeTargetsInterval, cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(2));
            var ang2 = ang.Angle;
            _logger.LogInformation($"Delta Angle {ang2 - ang1} [rad]; deltaWinch {speeds.First(s => Math.Abs(s) > 0.00001) * t} []");
            return cts;
        }
        #endregion

        #region MotorTargets
        ExtendedMotorDataArgs ConstantSpeedPU(double[] puMotors)
        {
            var ret = new ExtendedMotorDataArgs();
            if (puMotors.Length != ret.PuMotors.Length)
                throw new ArgumentException(nameof(puMotors));

            for (int i = 0; i < puMotors.Length; i++)
                ret.PuMotors[i].TargetValue = puMotors[i];
            return ret;
        }

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


        ExtendedMotorDataArgs TargetsFromGamepadDerivative()
        {
            var t = new ExtendedMotorDataArgs();
            if (_gamepad is null || GamepadReading is null)
                return t;
            var speed = 2.0;
            var sHK = InverseKinematikDerivative(CurrentX, CurrentY, -GamepadReading.Value.LeftThumbstickX*speed, GamepadReading.Value.LeftThumbstickY*speed);
            t.PuMotors[1].TargetValue = Math.Clamp(sHK.speedKnicken*127,-127,127);  //vorne knicken
            t.PuMotors[2].TargetValue = Math.Clamp(sHK.speedHeben * 127, -127, 127); //Vorne Heben


            t.PuMotors[0].TargetValue = (GamepadReading.Value.RightTrigger - GamepadReading.Value.LeftTrigger) * 126;  //Winde
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
                    RunDerivativeControlCommand.Execute(null);
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

        private (double x, double y) ForwardKinematik(double heben, double knicken)
        {
            var knicken1 = knicken - Math.PI / 2.0 + heben;
            var x = Math.Cos(heben) * InnerLength + Math.Sin(knicken1) * OuterLength;
            var y = Math.Sin(heben) * InnerLength - Math.Cos(knicken1) * OuterLength;
            return (x, y);
        }

        private (double heben, double knicken) InverseKinematik(double x, double y)
        {
            var knicken = Math.Acos((Squared(InnerLength) + Squared(OuterLength) - (Squared(x) + Squared(y))) / (2 * InnerLength * OuterLength));
            var heben = Math.Asin(y / Math.Sqrt(Squared(x) + Squared(y))) + Math.Acos((Squared(InnerLength) + Squared(x) + Squared(y) - Squared(OuterLength)) / (2 * InnerLength * Math.Sqrt(x * x + y * y)));
            return (heben, knicken);
        }
        private (double speedHeben, double speedKnicken) InverseKinematikDerivative(double x, double y, double speedX, double speedY)
        {
            // Derivative expressions for `knicken`
            double partialKnickenX = x / (InnerLength * OuterLength * Math.Sqrt(1 - Math.Pow((InnerLength * InnerLength + OuterLength * OuterLength - x * x - y * y) / (2 * InnerLength * OuterLength), 2)));
            double partialKnickenY = y / (InnerLength * OuterLength * Math.Sqrt(1 - Math.Pow((InnerLength * InnerLength + OuterLength * OuterLength - x * x - y * y) / (2 * InnerLength * OuterLength), 2)));

            // Derivative expressions for `heben`
            double partialHebenX = -x * y / (Math.Pow(x * x + y * y, 1.5) * Math.Sqrt(-y * y / (x * x + y * y) + 1))
                                   - (x / (InnerLength * Math.Sqrt(x * x + y * y)) - x * (InnerLength * InnerLength - OuterLength * OuterLength + x * x + y * y) / (2 * InnerLength * Math.Pow(x * x + y * y, 1.5)))
                                   / Math.Sqrt(1 - Math.Pow((InnerLength * InnerLength - OuterLength * OuterLength + x * x + y * y) / (2 * InnerLength * Math.Sqrt(x * x + y * y)), 2));
            double partialHebenY = (-y * y / Math.Pow(x * x + y * y, 1.5) + 1 / Math.Sqrt(x * x + y * y)) / Math.Sqrt(-y * y / (x * x + y * y) + 1)
                                   - (y / (InnerLength * Math.Sqrt(x * x + y * y)) - y * (InnerLength * InnerLength - OuterLength * OuterLength + x * x + y * y) / (2 * InnerLength * Math.Pow(x * x + y * y, 1.5)))
                                   / Math.Sqrt(1 - Math.Pow((InnerLength * InnerLength - OuterLength * OuterLength + x * x + y * y) / (2 * InnerLength * Math.Sqrt(x * x + y * y)), 2));

            // Calculate rates of change of `heben` and `knicken` with respect to time
            double speedHeben = partialHebenX * speedX + partialHebenY * speedY;
            double speedKnicken = partialKnickenX * speedX + partialKnickenY * speedY;

            return (speedHeben, speedKnicken);
        }
        #endregion
    }
}