namespace SharpWizzDriver.Telemetry
{
    /// <summary>
    /// https://buwizz.com/BuWizz_3.0_API_3.6_web.pdf
    /// </summary>
    public class BuWizzTelemtry
    {
        public byte Command => _value[0];
        public byte StatusFlags => _value[1];
        public bool UsbConnectionStatus => (StatusFlags & 0b01000000) != 0;
        public bool BatteryChargingStatus => (StatusFlags & 0b00100000) != 0;
        public byte BatteryLevelStatus => (byte)((StatusFlags & 0b00011000) >> 3);
        public bool BleLongRangePhyEnabled => (StatusFlags & 0b00000100) != 0;
        public bool Error => (StatusFlags & 0b00000001) != 0;
        public float BatteryVoltage => 9 + _value[2] * 0.05f;
        public float[] MotorCurrents => new float[] { _value[3] * 0.015f, _value[4] * 0.015f, _value[5] * 0.015f, _value[6] * 0.015f, _value[7] * 0.015f, _value[8] * 0.015f };
        public sbyte MicrocontrollerTemperature => (sbyte)_value[9];
        double mgPerCnt => 0.488 / 1000.0;
        public double AccelerometerXAxisValue => (short)(_value[11] << 8 | _value[10]) * mgPerCnt;
        public double AccelerometerYAxisValue => (short)(_value[13] << 8 | _value[12]) * mgPerCnt;
        public double AccelerometerZAxisValue => (short)(_value[15] << 8 | _value[14]) * mgPerCnt;
        public byte BootloaderResponseCommand => _value[16];
        public byte BootloaderResponseCode => _value[17];
        public byte[] BootloaderResponseData => new byte[] { _value[18], _value[19], _value[20] };
        public ushort BatteryChargeCurrent => BitConverter.ToUInt16(_value, 21);
        public PuMotorTelemtry[] PoweredUpMotorData => new PuMotorTelemtry[]
        {
            new PuMotorTelemtry(_value[22], (sbyte)_value[23], BitConverter.ToUInt16(_value, 24), BitConverter.ToInt32(_value, 26)),
            new PuMotorTelemtry(_value[27], (sbyte)_value[28], BitConverter.ToUInt16(_value, 29), BitConverter.ToInt32(_value, 31)),
            new PuMotorTelemtry(_value[32], (sbyte)_value[33], BitConverter.ToUInt16(_value, 34), BitConverter.ToInt32(_value, 36)),
            new PuMotorTelemtry(_value[37], (sbyte)_value[38], BitConverter.ToUInt16(_value, 39), BitConverter.ToInt32(_value, 41))
        };
        public PidControllerState? OptionalPidControllerState
        {
            get
            {
                if (_value.Length > 42)
                {
                    return new PidControllerState(
                    BitConverter.ToSingle(_value, 42),
                    BitConverter.ToSingle(_value, 46),
                    BitConverter.ToSingle(_value, 50),
                    (sbyte)_value[54],
                    (sbyte)_value[55]
                    );
                }
                return null;
            }
        }

        private byte[] _value { get; }

        public BuWizzTelemtry(byte[] value)
        {
            if (value.Length != 68 && value.Length != 54)
            {
                throw new ArgumentException("Value must be 68 or 54 bytes long.", nameof(value));
            }
            if (value[0] != 0x01)
            {
                throw new ArgumentException($"Wrong Command must be 0x01 was {value[0]}");
            }

            _value = value;
        }
    }
}
