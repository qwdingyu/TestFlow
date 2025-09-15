using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Devices.Plugin;

namespace MyCompany.MyDriver
{
    public class MyDriverRegistrar : IDeviceRegistrar
    {
        public void Register(DeviceFactory factory)
        {
            factory.Register("MyDriver", (_, cfg) => new MyDriverDevice(cfg));
        }
    }
}
