using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Devices.Can;
using ZL.DeviceLib.Devices.Plc;
using ZL.DeviceLib.Devices.Plugin;
using ZL.DeviceLib.Devices.Sp;

namespace MyCompany.MyDriver
{
    public class MyDriverRegistrar : IDeviceRegistrar
    {
        public void Register(DeviceFactory factory)
        {
            //factory.Register("MyDriver", (_, cfg) => new MyDriverDevice(cfg));

            factory.Register("plc", (f, cfg) => PlcDeviceBuilder.Build(cfg)); //PLC
            //factory.Register("noise", (f, cfg) => new NoiseSerialDevice(cfg)); //噪音仪
            factory.Register("resistance", (f, cfg) => new ResistanceSerialDevice(cfg)); // 电阻仪
            factory.Register("ktdy", (f, cfg) => new KtdyUsbDevice(cfg));   // 可调电源
            factory.Register("oscilloscope", (f, cfg) => new OscilloscopeUsbDevice(cfg));   // 示波器
            factory.Register("can_bus", (f, cfg) => new CanDevice(cfg));                      // 能力版 CAN
            //factory.Register("scanner", (f, cfg) => new RawSerialDevice(cfg));                // 原生串口：Raw能力 

        }
    }
}
