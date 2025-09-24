using Newtonsoft.Json;
using System;
using ZL.DeviceLib.Utils;

namespace ZL.DeviceLib.Devices.Plc
{
    public static class PlcDeviceBuilder
    {
        private static readonly object _lock = new object();
        private static PlcDevice _instance;

        /// <summary>
        /// 获取单例的 PlcDevice 实例
        /// </summary>
        public static PlcDevice Build(Models.DeviceConfig cfg)
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        // 做参数转换
                        if (string.IsNullOrEmpty(cfg.ConnectionString))
                            throw new Exception($"plc 设备【{cfg.Type}】连接时没有提供 ConnectionString");

                        var plcCfg = JsonConvert.DeserializeAnonymousType(
                            cfg.ConnectionString,
                            new { DeviceType = "", PlcIp = "", PlcRack = (byte)0, PlcSlot = (byte)0, TagFilePath = "" }
                        );

                        var deviceConfig = new Tag.DeviceConfig();
                        deviceConfig.Params["DeviceType"] = plcCfg.DeviceType;
                        deviceConfig.Params["DeviceIp"] = plcCfg.PlcIp;
                        deviceConfig.Params["Port"] = 102;
                        deviceConfig.Params["Rack"] = plcCfg.PlcRack;
                        deviceConfig.Params["Slot"] = plcCfg.PlcSlot;
                        deviceConfig.Params["TagFilePath"] = plcCfg.TagFilePath;

                        // 附加信息
                        deviceConfig.Params["CompanyId"] = cfg.Settings.GetValue("CompanyId", "");
                        deviceConfig.Params["PlantId"] = cfg.Settings.GetValue("PlantId", "");
                        deviceConfig.Params["Line"] = cfg.Settings.GetValue("Line", "");
                        deviceConfig.Params["StationNo"] = cfg.Settings.GetValue("StationNo", "");

                        _instance = new PlcDevice(deviceConfig);
                    }
                }
            }

            return _instance;
        }

        /// <summary>
        /// 释放单例（可选，如果你需要在程序退出或重新连接时销毁并重建）
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                if (_instance != null)
                {
                    _instance.Dispose();
                    _instance = null;
                }
            }
        }
    }

}
