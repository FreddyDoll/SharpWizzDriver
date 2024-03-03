using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpWizzDriver
{
    /// <summary>
    /// https://buwizz.com/BuWizz_3.0_API_3.6_web.pdf
    /// </summary>
    public enum PuPortFunction : byte
    {
        GenericPwmOutput = 0x00,
        PuSimplePwm = 0x10,
        PuSpeedServo = 0x14,
        PuPositionServo = 0x15,
        PuAbsolutePositionServo = 0x16,
    }

    public enum ConnectionStates
    {
        Disconnected,
        Connecting,
        Connected
    }
}
