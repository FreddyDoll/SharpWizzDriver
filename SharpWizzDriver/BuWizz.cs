using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SharpWizzDriver.CallParameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace SharpWizzDriver
{
    public partial class BuWizz : ObservableObject
    {
        public delegate Task<MotorDataArgs> GetMotorDataArgsAsync(TimeSpan elapsed, CancellationToken t);
        public delegate MotorDataArgs GetMotorDataArgs(TimeSpan elapsed);

        public delegate Task<ExtendedMotorDataArgs> GetExtendedMotorDataArgsAsync(TimeSpan elapsed, CancellationToken t);
        public delegate ExtendedMotorDataArgs GetExtendedMotorDataArgs(TimeSpan elapsed);

        delegate Task StepStream(TimeSpan elapsed, CancellationToken t);

        #region Properties
        public BuWizzState State { get; }
        public BuWizzConnection Connection { get; }
        public BuWizzPuPort? PidTelemtryTarget { get; private set; }
        public bool IsMoving => _cancelCommands is not null;
        public bool CanMove => !IsMoving && CanStop;
        public bool CanStop => Connection.ConnectionState == ConnectionStates.Connected;

        public List<TimeSpan> SensiblePeriods => new() { TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200) };


        #endregion

        ILogger<BuWizz>? _logger;
        DeviceWatcher _deviceWatcher;
        CancellationTokenSource? _cancelCommands;

        public BuWizz(DeviceWatcher watcher, BuWizzState state, BuWizzConnection connection, ILogger<BuWizz>? logger)
        {
            _logger = logger;
            _deviceWatcher = watcher;
            Connection = connection;
            State = state;

            connection.ConnectionChanged += Buwizz_ConnectionChanged;

            _deviceWatcher.Added += DeviceWatcher_Added;
            _deviceWatcher.Removed += (sender, info) =>
            {
                // removed must be not null or search won't be performed
            };
            _deviceWatcher.Updated += (sender, info) =>
            {
                // updated must be not null or search won't be performed
            };

            State.PropertyChanged += StateChanged;
            AddPortCallbacks();
        }


        #region commands
        [RelayCommand]
        async Task Stop(bool? brake = true)
        {
            try
            {
                _cancelCommands?.Cancel();

                byte brakeFlags = 0xff;
                if (brake ?? false)
                    brakeFlags = 0x00;

                await Connection.SetMotorData([0.0, 0.0, 0.0, 0.0, 0.0, 0.0], brakeFlags, 0xff);
                foreach (var item in State.PuPorts)
                    item.Mode = PuPortFunction.PuSimplePwm;
                await Connection.SetServoReferenceInput([0, 0, 0, 0]);

                _cancelCommands = null;
            }
            catch (Exception ex)
            {
                LogCommandException(nameof(Stop), ex);
            }
        }

        public record SineWave(double F, double A);
        [RelayCommand(IncludeCancelCommand = true)]
        async Task SinePWMMotors(object? sine, CancellationToken tokenCommand)
        {
            SineWave? s = sine as SineWave;

            if (s is null)
            {
                _logger?.LogWarning("Sine was null, created");
                s = new SineWave(0.4, 0.4);
            }

            try
            {
                foreach (var item in State.PuPorts)
                    item.Mode = PuPortFunction.PuSimplePwm;

                Queue<MotorDataArgs> path = new Queue<MotorDataArgs>();

                var deltaT = TimeSpan.FromMilliseconds(100);
                var time = TimeSpan.FromSeconds(3);
                double steps = (time.TotalSeconds / deltaT.TotalSeconds);
                for (int i = 0; i < steps; i++)
                {
                    var t = new MotorDataArgs();
                    for (int n = 0; n < t.PfMotors.Length; n++)
                        t.PfMotors[n].TargetValue = s.A * Math.Sin(deltaT.TotalSeconds * i * 2.0 * Math.PI * s.F);
                    path.Enqueue(t);
                }

                await RunMotorData((t) => path.Dequeue(), deltaT, tokenCommand);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogCommandException("Motors Sine PWM", ex);
            }
        }

        [RelayCommand(IncludeCancelCommand = true)]
        async Task SineSpeedMotors(object? sine, CancellationToken tokenCommand)
        {
            SineWave? s = sine as SineWave;

            if (s is null)
            {
                _logger?.LogWarning("Sine was null, created");
                s = new SineWave(0.5, 126);
            }

            try
            {
                foreach (var item in State.PuPorts)
                    item.Mode = PuPortFunction.PuSpeedServo;

                Queue<ExtendedMotorDataArgs> path = new ();

                var deltaT = TimeSpan.FromMilliseconds(100);
                var time = TimeSpan.FromSeconds(3);
                double steps = (time.TotalSeconds / deltaT.TotalSeconds);
                for (int i = 0; i < steps; i++)
                {
                    var t = new ExtendedMotorDataArgs();
                    for (int n = 0; n < t.PuMotors.Length; n++)
                        t.PuMotors[n].TargetValue = s.A * Math.Sin(deltaT.TotalSeconds * i * 2.0 * Math.PI * s.F);
                    path.Enqueue(t);
                }

                await RunExtendedMotorData((t) => path.Dequeue(), TimeSpan.FromMilliseconds(100), tokenCommand);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogCommandException("Motors Sine", ex);
            }
        }

        [RelayCommand(IncludeCancelCommand = true)]
        async Task SinePositionMotors(object? sine, CancellationToken tokenCommand)
        {
            SineWave? s = sine as SineWave;

            if (s is null)
            {
                _logger?.LogWarning("Sine was null, created");
                s = new SineWave(0.2, 200);
            }

            try
            {

                foreach (var item in State.PuPorts)
                    item.Mode = PuPortFunction.PuPositionServo;

                Queue<ExtendedMotorDataArgs> path = new();

                var deltaT = TimeSpan.FromMilliseconds(100);
                var time = TimeSpan.FromSeconds(3);
                double steps = (time.TotalSeconds / deltaT.TotalSeconds);
                for (int i = 0; i < steps; i++)
                {
                    var t = new ExtendedMotorDataArgs();
                    for (int n = 0; n < t.PuMotors.Length; n++)
                        t.PuMotors[n].TargetValue = s.A * Math.Sin(deltaT.TotalSeconds * i * 2.0 * Math.PI * s.F);
                    path.Enqueue(t);
                }

                await RunExtendedMotorData((t) => path.Dequeue(), TimeSpan.FromMilliseconds(100), tokenCommand);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogCommandException("Motors Sine", ex);
            }
        }
        #endregion

        public async Task RunMotorData(GetMotorDataArgs getTargets, TimeSpan minimumWait, CancellationToken? t)
        {
            StepStream stepMotorData = async (el, t) =>
            {
                var target = getTargets(el);
                if (target is null)
                    throw new Exception("Got null from GetMotorDataArgs");
                await Connection.SetMotorData(target);
            };
            await RunMovementStream(stepMotorData, minimumWait, t);
        }

        public async Task RunMotorData(GetMotorDataArgsAsync getTargets, TimeSpan minimumWait, CancellationToken? t)
        {
            StepStream stepMotorData = async (el, t) =>
            {
                var target = await getTargets(el,t);
                if (target is null)
                    throw new Exception("Got null from GetMotorDataArgsAsync");
                await Connection.SetMotorData(target);
            };
            await RunMovementStream(stepMotorData, minimumWait, t);
        }


        public async Task RunExtendedMotorData(GetExtendedMotorDataArgs getTargets, TimeSpan minimumWait, CancellationToken? t)
        {
            StepStream stepMotorData = async (el, t) =>
            {
                var target = getTargets(el);
                if (target is null)
                    throw new Exception("Got null from GetMotorDataArgs");
                await Connection.SetMotorDataExtended(target);
            };
            await RunMovementStream(stepMotorData, minimumWait, t);
        }

        public async Task RunExtendedMotorData(GetExtendedMotorDataArgsAsync getTargets, TimeSpan minimumWait, CancellationToken? t)
        {
            StepStream stepMotorData = async (el, t) =>
            {
                var target = await getTargets(el, t);
                if (target is null)
                    throw new Exception("Got null from GetMotorDataArgsAsync");
                await Connection.SetMotorDataExtended(target);
            };
            await RunMovementStream(stepMotorData, minimumWait, t);
        }


        #region callbacks
        private async void PortChanged(BuWizzPuPort port, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(BuWizzPuPort.Led))
                {
                    await WriteLedToConnection();
                }
                else if (e.PropertyName == nameof(BuWizzPuPort.RequestPidTelemetry))
                {
                    if (!port.RequestPidTelemetry)
                        return;

                    foreach (var item in State.PuPorts)
                        if (item != port)
                            item.RequestPidTelemetry = false;

                    var ind = State.PuPorts.ToList().IndexOf(port);
                    if (ind > 4)
                        throw new ArgumentOutOfRangeException(nameof(ind));
                    await Connection.EnablePIDControllerStateReporting((byte)ind);
                    PidTelemtryTarget = port;
                    _logger?.LogInformation($"Switched Pid Telemetry Target to Port {port.Name}");
                    OnPropertyChanged(nameof(PidTelemtryTarget));
                    port.RequestPidTelemetry = false;
                }
                else if (e.PropertyName == nameof(BuWizzPuPort.Mode))
                {
                    await Connection.SetPUPortFunction(State.PuPorts.Select(p => p.Mode).ToArray());
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "In Port Changed");
            }
        }

        private async void PortChanged(BuWizzPfPort port, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(BuWizzPfPort.CurrentLimit))
                {
                    var lims = State.PfPorts.Select(p => p.CurrentLimit).ToArray();
                    await Connection.Setcurrentlimits(lims);
                    _logger?.LogInformation($"New Current Limits writtrn.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "In Port Changed");
            }
        }

        private static byte GetFrom(string p, int index) => byte.Parse(new string(p.Take(new Range(index, index+2)).ToArray()));

        private async void StateChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(BuWizzState.Name))
                {
                    var n = State.Name;
                    if (n.Length > 12)
                        n = new string(n.Take(12).ToArray());
                    await Connection.SetDeviceName(n);

                    _logger?.LogInformation($"Buwizz Name Updated to {n}");
                }
                else if (e.PropertyName == nameof(BuWizzState.Ports))
                {
                    AddPortCallbacks();
                    await WriteInitialState();
                    _logger?.LogInformation($"Initial State Written after Ports changed");
                }
                else if (e.PropertyName == nameof(BuWizzState.TargetTransferPeriod))
                {
                    if (Connection.ConnectionState != ConnectionStates.Connected)
                        return;

                    await Connection.SetDataTransferPeriod(State.TargetTransferPeriod);

                    _logger?.LogInformation($"Transfer Period Updated to {State.TargetTransferPeriod}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "In Settings changed");
            }
        }

        private async void Buwizz_ConnectionChanged(object? sender, ConnectionStates e)
        {
            _logger?.LogInformation($"Connection switched to: {e}");
            try
            {
                if (e == ConnectionStates.Connecting)
                {
                    while (_deviceWatcher.Status != DeviceWatcherStatus.Started)
                        await Task.Delay(100);
                    _deviceWatcher.Stop();
                    _logger?.LogInformation($"BLE Device Watcher stopped");
                }
                else if (e == ConnectionStates.Disconnected)
                {
                    await Task.Delay(2000);
                    while (_deviceWatcher.Status != DeviceWatcherStatus.Stopped)
                        await Task.Delay(100);
                    _deviceWatcher.Start();
                    _logger?.LogInformation($"BLE Device Watcher Started");
                }
                else if (e == ConnectionStates.Connected)
                {
                    await WriteInitialState();
                    _logger?.LogInformation($"Initial State Written after Connecting");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Connection State Change");
            }
            finally
            {
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanMove));
            }
        }

        private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            try
            {
                _logger?.LogInformation($"Found BLE Device: {args.Name}");

                if (args.Name == State.Name)
                {
                    if (Connection.ConnectionState != ConnectionStates.Disconnected)
                    {
                        _logger?.LogInformation($"Buwizz Found but already connected");
                        return;
                    }

                    _logger?.LogInformation($"Connecting to: {(args.Name != string.Empty ? args.Name : args.Id)}");
                    await Connection.ConnectAsync(args);
                    _logger?.LogInformation($"Buwizz ({args.Name}) Connected!");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "New Device");
            }
        }
        #endregion

        #region helpers
        private async Task DestroyMovementCts(CancellationTokenSource? movementCts)
        {
            await Stop();
            _cancelCommands?.Cancel();
            movementCts?.Dispose();
            _cancelCommands = null;
            OnPropertyChanged(nameof(IsMoving));
            OnPropertyChanged(nameof(CanMove));
        }

        private CancellationTokenSource CreateMovementCts(CancellationToken? tokenCommand)
        {
            ThrowIfNotConnected();

            if (IsMoving)
                throw new Exception("Already moving");

            _cancelCommands = new();
            OnPropertyChanged(nameof(IsMoving));
            OnPropertyChanged(nameof(CanMove));

            var tokenStopCommands = _cancelCommands.Token;
            if (tokenCommand is null)
                return CancellationTokenSource.CreateLinkedTokenSource(tokenStopCommands);
            else
                return CancellationTokenSource.CreateLinkedTokenSource(tokenCommand.Value, tokenStopCommands);
        }


        async Task RunMovementStream(StepStream callback, TimeSpan minimumWait, CancellationToken? tokenCommand)
        {
            CancellationTokenSource? movementCts = null;
            try
            {
                movementCts = CreateMovementCts(tokenCommand);

                var sw = Stopwatch.StartNew();

                var last = sw.Elapsed;
                var target = sw.Elapsed + minimumWait;
                while (!movementCts.Token.IsCancellationRequested)
                {
                    await callback(sw.Elapsed, movementCts.Token);

                    var elapsed = sw.Elapsed;
                    var wait = target - elapsed;
                    await Task.Delay(wait, movementCts.Token);
                    target += minimumWait;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogCommandException(nameof(RunMotorData), ex);
            }
            finally
            {
                await DestroyMovementCts(movementCts);
            }
        }

        private void AddPortCallbacks()
        {
            foreach (var item in State.PuPorts)
                item.PropertyChanged += (sender, e) => PortChanged(item, e);
            foreach (var item in State.PfPorts)
                item.PropertyChanged += (sender, e) => PortChanged(item, e);
        }

        private byte ConvertSubstring(string s, int offset) => Convert.ToByte(s[offset..(offset + 2)], 16);

        private async Task WriteInitialState()
        {
            await Stop();

            await Connection.SetDataTransferPeriod(State.TargetTransferPeriod);
            await WriteLedToConnection();
            await Connection.SetPUPortFunction(State.PuPorts.Select(p => PuPortFunction.PuSimplePwm).ToArray());
            await Connection.Setcurrentlimits(State.PfPorts.Select(p => p.CurrentLimit).ToArray());
        }

        private async Task WriteLedToConnection()
        {
            var r = new List<byte>();
            var g = new List<byte>();
            var b = new List<byte>();

            List<byte[]> ports = State.PuPorts.Select(p =>
            {
                if (p is null)
                {
                    _logger?.LogWarning($"Illegal Color for Buwizz, port was null");
                    return [0xff, 0x00, 0x00];
                }
                var leds = p.Led;
                var r1 = "^#([0-9a-fA-F]{2}){4}$";
                var r2 = "^#([0-9a-fA-F]{2}){3}$";

                if (Regex.IsMatch(p.Led, r1))
                    leds = leds[3..];
                else if (Regex.IsMatch(p.Led, r2))
                    leds = leds[1..];
                else
                {
                    _logger?.LogWarning($"Illegal Color for Buwizz, does not math Regesx {r1} or {r2} : {leds}");
                    return [0xff, 0x00, 0x00];
                }
                return new byte[] { ConvertSubstring(leds, 0), ConvertSubstring(leds, 2), ConvertSubstring(leds, 4) };
            }).ToList();

            await Connection.SetLEDStatus(ports[0], ports[1], ports[2], ports[3]);
        }
        void LogCommandException(string func, Exception ex)
        {
            _logger?.LogError(ex, $"In Command \"{func}\"");
        }
        private void ThrowIfNotConnected()
        {
            if (Connection.ConnectionState != ConnectionStates.Connected)
                throw new Exception($"Not Connected: {Connection.ConnectionState}");
        }

        #endregion
    }
}
