namespace SharpWizzDriver.Telemetry
{
    /// <summary>
    /// https://buwizz.com/BuWizz_3.0_API_3.6_web.pdf
    /// </summary>
    public record PidControllerState(
        float ProcessValue,
        float Error,
        float PidOutput,
        sbyte IntegratorState,
        sbyte MotorOutput);
}
