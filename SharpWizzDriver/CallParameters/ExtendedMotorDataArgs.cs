using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpWizzDriver.CallParameters
{
    public class MotorArgs
    {
        public double TargetValue { get; set; } = 0.0;
        public bool BreakOnZero { get; set; } = true;
        public bool DeactivateLut { get; set; } = true;
    }

    public class ExtendedMotorDataArgs
    {
        public MotorArgs[] PuMotors { get; }
        public MotorArgs[] PfMotors { get; }

        public ExtendedMotorDataArgs()
        {
            PuMotors = new MotorArgs[4];
            for (int i = 0; i < PuMotors.Length; i++)
                PuMotors[i] = new MotorArgs();

            PfMotors = new MotorArgs[2];
            for (int i = 0; i < PfMotors.Length; i++)
                PfMotors[i] = new MotorArgs();
        }
    }

    public class MotorDataArgs
    {
        public MotorArgs[] PfMotors { get; }

        public MotorDataArgs()
        {
            PfMotors = new MotorArgs[6];
            for (int i = 0; i < PfMotors.Length; i++)
                PfMotors[i] = new MotorArgs();
        }
    }
}
