using System.Collections.Generic;
namespace ZL.DeviceLib.Models
{
    public class DeviceConfig
    {
        public string Type { get; set; }
        public string ConnectionString { get; set; }
        public Dictionary<string, object> Settings { get; set; }
    }
}

