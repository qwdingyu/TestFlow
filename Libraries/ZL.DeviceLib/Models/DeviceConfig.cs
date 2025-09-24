using System.Collections.Generic;

namespace ZL.DeviceLib.Models
{
    public class DeviceConfig
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new Dictionary<string, object>();
    }
    /// <summary>
    /// 可复用的握手/初始化执行器
    /// </summary>
    public sealed class HandshakeSpec
    {
        public bool OnLoad { get; set; } = true;
        public string Cmd { get; set; } = "SYST:REM\n";
        public bool ExpectResponse { get; set; } = false;
        public int TimeoutMs { get; set; } = 300;
        public int Retries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 200;
        public int DelayAfterMs { get; set; } = 0;

        public VerifySpec Verify { get; set; } // 可空
    }

    public sealed class VerifySpec
    {
        public string Cmd { get; set; } = "*IDN?\n";
        public string Contains { get; set; }   // 例如 "HP3544"
    }

    public sealed class InitStepSpec
    {
        public string Cmd { get; set; }
        public bool ExpectResponse { get; set; } = false;
        public int TimeoutMs { get; set; } = 300;
        public int DelayAfterMs { get; set; } = 0;
    }

}

