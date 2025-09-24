using LibUsbDotNet.Main;
using LibUsbDotNet;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZL.Forms;

namespace TestFlowDemo.UI
{
    public partial class Frm_USBDeviceInfo : Form
    {
        UiLogManager _log = null;
        public Frm_USBDeviceInfo()
        {
            InitializeComponent();
        }

        private void Frm_USBDeviceInfo_Load(object sender, EventArgs e)
        {
            _log = new UiLogManager(lstBox_Log);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            _log.Add("查询开始");
            UsbRegDeviceList allDevices = UsbDevice.AllDevices;
            _log.Add($"查询完毕，共【{allDevices.Count}】个设备");
            foreach (UsbRegistry usb in allDevices)
            {
                _log.Add("----------------");
                _log.Add($"Device info: {usb.Device.Info.ProductString}");
                _log.Add($"Pid: {usb.Pid}, VID: {usb.Vid}");
            }
        }
    }
}
