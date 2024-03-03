namespace SharpWizzDriver.Telemetry
{
    /// <summary>
    /// https://buwizz.com/BuWizz_3.0_API_3.6_web.pdf
    /// </summary>
    public record PuMotorTelemtry(
        byte MotorType,
        sbyte Velocity,
        ushort AbsolutePosition,
        int Position);
}
