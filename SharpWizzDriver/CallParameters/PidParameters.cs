namespace SharpWizzDriver.CommandParameters
{
    /// <summary>
    /// https://buwizz.com/BuWizz_3.0_API_3.6_web.pdf
    /// </summary>
    public class PidParameters
    {
        public float OutLP { get; set; }
        public float D_LP { get; set; }
        public float Speed_LP { get; set; }
        public float Kp { get; set; }
        public float Ki { get; set; }
        public float Kd { get; set; }
        public float LimI { get; set; }
        public float ReferenceRateLimit { get; set; }
        public sbyte LimOut { get; set; }
        public sbyte DeadbandOut { get; set; }
        public sbyte DeadbandOutBoost { get; set; }
        public PuPortFunction ValidMode { get; set; }

        public byte[] ToByteArray()
        {
            List<byte> byteArray =
            [
                .. BitConverter.GetBytes(OutLP),
                .. BitConverter.GetBytes(D_LP),
                .. BitConverter.GetBytes(Speed_LP),
                .. BitConverter.GetBytes(Kp),
                .. BitConverter.GetBytes(Ki),
                .. BitConverter.GetBytes(Kd),
                .. BitConverter.GetBytes(LimI),
                .. BitConverter.GetBytes(ReferenceRateLimit),
                (byte)LimOut,
                (byte)DeadbandOut,
                (byte)DeadbandOutBoost,
                Convert.ToByte(ValidMode),
            ];

            return byteArray.ToArray();
        }
    }
}
