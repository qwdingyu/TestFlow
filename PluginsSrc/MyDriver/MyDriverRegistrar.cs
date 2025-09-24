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
            //factory.Register("noise", (f, cfg) => new NoiseSerialDevice(cfg)); //������
            factory.Register("resistance", (f, cfg) => new ResistanceSerialDevice(cfg)); // ������
            factory.Register("ktdy", (f, cfg) => new KtdyUsbDevice(cfg));   // �ɵ���Դ
            factory.Register("oscilloscope", (f, cfg) => new OscilloscopeUsbDevice(cfg));   // ʾ����
            factory.Register("can_bus", (f, cfg) => new CanDevice(cfg));                      // ������ CAN
            //factory.Register("scanner", (f, cfg) => new RawSerialDevice(cfg));                // ԭ�����ڣ�Raw���� 

        }
    }
}
