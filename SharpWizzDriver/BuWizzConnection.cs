using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Buffer = System.Buffer;
using Microsoft.Extensions.Logging;
using SharpWizzDriver.Telemetry;
using SharpWizzDriver.CommandParameters;
using Windows.Networking.BackgroundTransfer;
using SharpWizzDriver.CallParameters;

namespace SharpWizzDriver
{
    /// <summary>
    /// Uses Bluetooth
    /// 
    /// https://buwizz.com/BuWizz_3.0_API_3.6_web.pdf
    /// </summary>
    public partial class BuWizzConnection:ObservableObject, IDisposable
    {
        public event EventHandler<BuWizzTelemtry>? TelemtryRecieved;
        public event EventHandler<ConnectionStates>? ConnectionChanged;

        public BuWizzTelemtry? LatestTelemetry { get; private set; }
        public ConnectionStates ConnectionState { get; private set; } = ConnectionStates.Disconnected;

        public TimeSpan TransferPeriod => TimeSpan.FromMilliseconds(_avgTelemetryPeriod);

        BluetoothLEDevice? _device;
        ILogger<BuWizzConnection>? _logger;
        Stopwatch _swTelemetryPeriod = new();
        double _avgTelemetryPeriod = 0.0;
        double _FiltTelemetryPeriod = 0.1;
        GattCharacteristic? _applicationCharackteristic;
        Task? watchdogTask;
        CancellationTokenSource? _cancelWatchdog;

        public BuWizzConnection(ILogger<BuWizzConnection>? logger)
        {
            _logger = logger; 
        }

        public async Task ConnectAsync(DeviceInformation info)
        {
            changeState(ConnectionStates.Connecting);
            try
            {
                _device = await BluetoothLEDevice.FromIdAsync(info.Id);
                _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

                GattDeviceServicesResult result = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                var service = result.Services.First(s => s.Uuid == Guid.Parse("500592d1-74fb-4481-88b3-9919b1676e93"));
                var charachteristics = (await service.GetCharacteristicsAsync()).Characteristics.ToArray();
                _applicationCharackteristic = charachteristics.First(c => c.Uuid == Guid.Parse("50052901-74fb-4481-88b3-9919b1676e93"));

                await _applicationCharackteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

                if (_device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    _device.Dispose();
                    throw new Exception($"Device not connected: {_device.ConnectionStatus}");
                }

                _swTelemetryPeriod.Restart();
                _applicationCharackteristic.ValueChanged += GattValueUpdated;


                changeState(ConnectionStates.Connected);

                _cancelWatchdog = new();
                watchdogTask = RunWatchdog(_cancelWatchdog.Token);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Connection failed");
                _device?.Dispose();
                changeState(ConnectionStates.Disconnected);
                throw;
            }
        }

        #region Background Task
        async Task RunWatchdog(CancellationToken t)
        {
            try
            {
                await Task.Delay(2000,t); //Some Time after startup

                while (!t.IsCancellationRequested)
                {
                    await ActivateConnectionWatchdog(2);
                    await Task.Delay(500, t);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "In Watchdog Task");
            }
        }
        #endregion

        #region callbacks
        private async void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            try
            {
                if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                {
                    _cancelWatchdog?.Cancel();
                    if (watchdogTask is not null)
                        await watchdogTask;

                    _device?.Dispose();
                    changeState(ConnectionStates.Disconnected);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Disconnecting from BLE Device");
            }
        }


        private void GattValueUpdated(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                _avgTelemetryPeriod = _avgTelemetryPeriod * (1.0 - _FiltTelemetryPeriod) + _swTelemetryPeriod.ElapsedMilliseconds * _FiltTelemetryPeriod;
                var info = $"avgPeriod:{_avgTelemetryPeriod}:";
                OnPropertyChanged(nameof(TransferPeriod));

                _swTelemetryPeriod.Restart();

                var data = ReadFromBuffer(args.CharacteristicValue);
                _logger?.LogDebug($"{info}; data:{string.Concat(data.Select(d => $"{d} "))}");

                LatestTelemetry = new BuWizzTelemtry(data);
                OnPropertyChanged(nameof(LatestTelemetry));
                TelemtryRecieved?.Invoke(this, LatestTelemetry);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Receiving Telemetry");
            }
        }

        #endregion

        #region public device calls

        #region movement
        /// <param name="motorData"> Motor data (signed 8-bit value for each motor output) 
        /// 0x81 (-127): Full backwards 
        /// 0x00 (0): Stop 
        /// 0x7F (127): Full forwards </param>
        /// <param name="brakeFlags"> Brake flags - bit
        /// mapped to bits 5-0 (1 bit per each motor, bit 0 for first motor, bit 5 for the last) 
        /// - If brake flag is set for a target motor, slow-decay control mode will be used
        ///   (short circuiting the motor armature over the inactive phase).
        /// - If flag is not set, the corresponding motor will be controlled in fast-decay control
        ///   method(coasting the motor during the inactive phase).</param>
        /// <param name="lutOptions"> LUT options - bit
        /// mapped to bits 5-0 (1 bit per each motor) 
        /// If LUT bit is set for the target motor, look-up table(LUT) is disabled on that output
        /// port.</param>
        public async Task SetMotorData(sbyte[] motorData, byte brakeFlags, byte lutOptions)
        {
            if (motorData.Length != 6) throw new ArgumentException("Must provide motor data for 6 motors.");

            byte[] command = new byte[9];
            command[0] = 0x30; // Set motor data command
            Array.Copy(motorData, 0, command, 1, 6);
            command[7] = brakeFlags;
            command[8] = lutOptions;
            await WriteCommand(command);
        }

        /// <param name="motorData"> Motor data (signed 8-bit value for each motor output) 
        /// -1.0 : Full backwards 
        /// 0.0  : Stop 
        /// 1.0  : Full forwards </param>
        /// <param name="brakeFlags"> Brake flags - bit
        /// mapped to bits 5-0 (1 bit per each motor, bit 0 for first motor, bit 5 for the last) 
        /// - If brake flag is set for a target motor, slow-decay control mode will be used
        ///   (short circuiting the motor armature over the inactive phase).
        /// - If flag is not set, the corresponding motor will be controlled in fast-decay control
        ///   method(coasting the motor during the inactive phase).</param>
        /// <param name="lutOptions"> LUT options - bit
        /// mapped to bits 5-0 (1 bit per each motor) 
        /// If LUT bit is set for the target motor, look-up table(LUT) is disabled on that output
        /// port.</param>
        public async Task SetMotorData(double[] motorData, byte brakeFlags, byte lutOptions)
        {
            var motor = new sbyte[motorData.Length];
            for (int i = 0; i < motorData.Length; i++)
                motor[i] = ConvertDoubleToPfPWM(motorData[i]);
            await SetMotorData(motor, brakeFlags, lutOptions);
        }

        private static sbyte ConvertDoubleToPfPWM(double motorData)
        {
            return (sbyte)Math.Clamp((int)Math.Round((motorData * 127)), -127, 127);
        }

        public async Task SetMotorData(MotorDataArgs motors)
        {
            List<double> targets = new();
            byte b = 0;
            byte l = 0;
            for (int i = 0; i < motors.PfMotors.Length; i++)
            {
                targets.Add(motors.PfMotors[i].TargetValue);
                if (motors.PfMotors[i].BreakOnZero)
                    b |= (byte)(1 << i);
                if (motors.PfMotors[i].DeactivateLut)
                    l |= (byte)(1 << i);
            }

            await SetMotorData(targets.ToArray(), b, l);
        }

        public async Task SetMotorDataExtended(int[] motorReferences, sbyte[] smallMotorData, byte brakeFlags, byte lutOptions)
        {
            if (motorReferences.Length != 4 || smallMotorData.Length != 2)
                throw new ArgumentException("Invalid motor references or small motor data.");

            byte[] command = new byte[21];
            command[0] = 0x31; // Set motor data (extended) command
            for (int i = 0; i < 4; i++)
            {
                Buffer.BlockCopy(motorReferences, i * sizeof(int), command, 1 + i * 4, sizeof(int));
            }
            Array.Copy(smallMotorData, 0, command, 17, 2);
            command[19] = brakeFlags;
            command[20] = lutOptions;
            await WriteCommand(command);
        }


        public async Task SetMotorDataExtended(ExtendedMotorDataArgs motors)
        {
            List<int> puTargets = new();
            byte b = 0;
            byte l = 0;
            int offset = 0;
            for (int i = 0; i < motors.PuMotors.Length; i++)
            {
                puTargets.Add((int)Math.Round(motors.PuMotors[i].TargetValue));
                if (motors.PuMotors[i].BreakOnZero)
                    b |= (byte)(1 << i);
                if (motors.PuMotors[i].DeactivateLut)
                    l |= (byte)(1 << i);
                offset++;
            }

            List<sbyte> pfTargets = new();
            for (int i = 0; i < motors.PfMotors.Length; i++)
            {
                pfTargets.Add(ConvertDoubleToPfPWM(motors.PfMotors[i].TargetValue));
                if (motors.PfMotors[i].BreakOnZero)
                    b |= (byte)(1 << offset);
                if (motors.PfMotors[i].DeactivateLut)
                    l |= (byte)(1 << offset);
                offset++;
            }

            await SetMotorDataExtended(puTargets.ToArray(), pfTargets.ToArray(), b, l);
        }
        #endregion

        #region device settings

        public async Task SetDataTransferPeriod(byte period)
        {
            if (period < 20 || period > 255) throw new ArgumentException("Period must be between 20 and 255.");
            await WriteCommand(new byte[] { 0x32, period });
        }
        public async Task SetDataTransferPeriod(TimeSpan period)
        {
            var p = Math.Round(period.TotalMilliseconds);
            if (p < 20 || p > 255)
                throw new ArgumentException("Period must be between 20 and 255 ms.");
            await WriteCommand(new byte[] { 0x32, (byte)p });
        }

        public async Task SetDeviceName(string name)
        {
            byte[] command = new byte[13];
            command[0] = 0x20; // Set device name command
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            Array.Copy(nameBytes, 0, command, 1, Math.Min(12, nameBytes.Length));
            if (nameBytes.Length < 12)
            {
                command[nameBytes.Length + 1] = 0; // Null termination if name is shorter than 12 chars
            }
            await WriteCommand(command);
        }

        /// <summary>
        /// UNTESTED!
        /// </summary>
        public async Task EnableDisableMotionWakeUp(bool enable)
        {
            await WriteCommand(new byte[] { 0x21, Convert.ToByte(enable) });
        }

        /// <summary>
        /// UNTESTED!
        /// </summary>
        public async Task SetAccelerationSensorCalibrationData(float[] calibrationFactors)
        {
            if (calibrationFactors.Length != 6) throw new ArgumentException("Must provide exactly 6 calibration factors.");

            byte[] command = new byte[25];
            command[0] = 0x22; // Set calibration factors command
            System.Buffer.BlockCopy(calibrationFactors, 0, command, 1, calibrationFactors.Length * sizeof(float));
            await WriteCommand(command);
        }

        /// <summary>
        /// UNTESTED!
        /// </summary>
        public async Task ReadAccelerationSensorCalibrationData()
        {
            await WriteCommand(new byte[] { 0x23 });
        }
        
        public async Task EnablePIDControllerStateReporting(byte portIndex)
        {
            await WriteCommand(new byte[] { 0x51, portIndex });
        }

        #region port settings
        //untested
        public async Task SetMotorRampUpAndDownRates(byte[] rampUpRates, byte[] rampDownRates)
        {
            if (rampUpRates.Length != 6 || rampDownRates.Length != 6)
                throw new ArgumentException("Must provide exactly 6 ramp-up and 6 ramp-down rates.");

            byte[] command = new byte[13];
            command[0] = 0x33; // Set ramp-up and ramp-down rates command
            Array.Copy(rampUpRates, 0, command, 1, 6);
            Array.Copy(rampDownRates, 0, command, 7, 6);
            await WriteCommand(command);
        }

        //untested
        public async Task SetMotorTimeoutConfiguration(byte configuration)
        {
            await WriteCommand(new byte[] { 0x34, configuration });
        }

        public async Task SetLEDStatus(byte[] motor1RGB, byte[] motor2RGB, byte[] motor3RGB, byte[] motor4RGB, byte[]? ledBlinkingConfiguration = null)
        {

            byte[] command = new byte[ledBlinkingConfiguration != null ? 17 : 13];
            command[0] = 0x36; // Set LED status command
            Array.Copy(motor1RGB, 0, command, 1, 3);
            Array.Copy(motor2RGB, 0, command, 4, 3);
            Array.Copy(motor3RGB, 0, command, 7, 3);
            Array.Copy(motor4RGB, 0, command, 10, 3);
            if (ledBlinkingConfiguration != null)
            {
                //LED blinking configuration(1 byte per LED)
                //bits 7…4: blinking frequency(0 - 15 Hz), LED is off if value is 0
                //bits 3...0: duty cycle(0 = 1 / 16, 15 = 100 %) 
                //Default on connection established: 0xFF(solid ON in white)
                Array.Copy(ledBlinkingConfiguration, 0, command, 13, 4);
            }
            await WriteCommand(command);
        }

        //untested
        public async Task SetAccelerationDataLowPassFilterConstant(byte sensorSamplingSetting)
        {
            await WriteCommand(new byte[] { 0x37, sensorSamplingSetting });
        }

        /// <param name="motorData">in Ampere</param>
        public async Task Setcurrentlimits(double[] motorData)
        {
            var motor = new byte[motorData.Length];
            for (int i = 0; i < motorData.Length; i++)
                motor[i] = (byte)Math.Clamp((int)Math.Round(motorData[i] * 1000 / 30), 0, 255);
            await Setcurrentlimits(motor);
        }

        /// <param name="motorData">Steps of 30mA</param>
        public async Task Setcurrentlimits(byte[] motorData)
        {
            if (motorData.Length != 6) throw new ArgumentException("Must provide motor data for 6 motors.");

            byte[] command = new byte[7];
            command[0] = 0x38;
            Array.Copy(motorData, 0, command, 1, 6);
            await WriteCommand(command);
        }
        #endregion

        #region Powered Up

        //untested
        public async Task UARTBaudrateSetup(byte channelID, uint baudrate)
        {
            byte[] command = new byte[7];
            command[0] = 0x40; // UART baudrate setup command
            command[1] = 0x10; // Set baudrate
            command[2] = channelID;
            byte[] baudrateBytes = BitConverter.GetBytes(baudrate);
            if (BitConverter.IsLittleEndian) Array.Reverse(baudrateBytes); // Ensure big-endian byte order
            Array.Copy(baudrateBytes, 0, command, 3, 4);
            await WriteCommand(command);
        }
        public async Task SetPUPortFunction(PuPortFunction port1, PuPortFunction port2, PuPortFunction port3, PuPortFunction port4) => await SetPUPortFunction(new[] { port1, port2, port3, port4 });

        public async Task SetPUPortFunction(PuPortFunction[] portFunctions)
        {
            if (portFunctions.Length != 4) throw new ArgumentException("Must provide exactly 4 PU port functions.");

            byte[] command = new byte[5];
            command[0] = 0x50; // Set PU port function command
            Array.Copy(portFunctions, 0, command, 1, 4);
            await WriteCommand(command);
        }

        /// <summary>
        /// Sets Target Values for PID controllers
        /// </summary>
        /// <param name="referenceValues">PU Lego XL Speed: -127 -> 127</param>
        public async Task SetServoReferenceInput(Int32[] referenceValues)
        {
            if (referenceValues.Length != 4) throw new ArgumentException("Must provide reference values for each channel.");

            byte[] command = new byte[17];
            command[0] = 0x52; // Set PID reference command
            Buffer.BlockCopy(referenceValues, 0, command, 1, 16);
            await WriteCommand(command);
        }

        //untested
        public async Task SetServoPIDParameters(byte portIndex, PidParameters parameters)
        {
            byte[] command = new byte[38];
            command[0] = 0x53; // Set PID parameters command
            command[1] = portIndex;
            Buffer.BlockCopy(parameters.ToByteArray(), 0, command, 2, 36);
            await WriteCommand(command);
        }
        #endregion

        #endregion

        #endregion

        #region private device calls

        private async Task ActivateConnectionWatchdog(byte timeout) => await WriteCommand([0x35, timeout]);

        private async Task WriteCommand(byte[] led)
        {
            if (ConnectionState != ConnectionStates.Connected)
                throw new Exception("Not Connected");
            if (_applicationCharackteristic is null)
                throw new ArgumentNullException(nameof(_applicationCharackteristic));

            var buff = WindowsRuntimeBuffer.Create(led, 0, led.Length, led.Length);
            await _applicationCharackteristic.WriteValueAsync(buff, GattWriteOption.WriteWithoutResponse);
        }
        #endregion

        #region helper
        static byte[] ReadFromBuffer(IBuffer read)
        {
            using (var dataReader = DataReader.FromBuffer(read))
            {
                var data = new byte[read.Length];
                dataReader.ReadBytes(data);
                return data;
            }
        }

        void changeState(ConnectionStates s)
        {
            ConnectionState = s;
            ConnectionChanged?.Invoke(this, s);
            OnPropertyChanged(nameof(ConnectionState));
        }
        #endregion

        public void Dispose()
        {
            _device?.Dispose();
        }
    }
}
