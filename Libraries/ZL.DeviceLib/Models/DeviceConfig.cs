using System.Collections.Generic;
namespace ZL.DeviceLib.Models
{
    public class DeviceConfig
    {
        // 设备类型，例如 "scanner"、"power_supply" 等
        public string Type { get; set; }

        // 对应的物理连接字符串，例如串口号或网络地址
        public string ConnectionString { get; set; }

        // 新增的资源标识，用于指明物理通道，实现跨流程互斥
        // 如果旧配置未提供，则默认回退到 ConnectionString
        public string ResourceId { get; set; }

        // 设备自定义设置，以键值对形式存储
        public Dictionary<string, object> Settings { get; set; }
    }
}

