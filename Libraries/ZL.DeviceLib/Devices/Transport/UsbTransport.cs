using LibUsbDotNet;
using LibUsbDotNet.Main;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Events;

namespace ZL.DeviceLib.Devices.Transport
{
    public sealed class UsbTransport : TransportBaseWithState
    {
        private UsbDevice _device;
        private UsbEndpointReader _reader;
        private UsbEndpointWriter _writer;
        private readonly int _interfaceIndex;

        public UsbTransport(int vendorId, int productId, string deviceKey = null, 
            byte readEndpoint = 0x81, byte writeEndpoint = 0x01, byte configIndex = 1, int interfaceIndex = 0)
            : base(string.IsNullOrEmpty(deviceKey) ? $"USB[{vendorId:X4}:{productId:X4}]" : deviceKey)
        {
            try
            {
                _interfaceIndex = interfaceIndex;

                var finder = new UsbDeviceFinder(vendorId, productId);
                _device = UsbDevice.OpenUsbDevice(finder);
                if (_device == null)
                {
                    SetState(DeviceState.Disconnected, $"设备【{deviceKey}】未找到");
                    throw new InvalidOperationException("USB device not found.");
                }

                if (_device is IUsbDevice whole)
                {
                    whole.SetConfiguration(configIndex);
                    whole.ClaimInterface(_interfaceIndex);
                }

                _reader = _device.OpenEndpointReader((ReadEndpointID)readEndpoint);
                _writer = _device.OpenEndpointWriter((WriteEndpointID)writeEndpoint);

                SetState(DeviceState.Connected);
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "初始化失败: " + ex.Message);
                throw;
            }
        }

        public override bool IsHealthy()
        {
            try
            {
                return _device != null && _device.IsOpen && base.IsHealthy();
            }
            catch
            {
                return false;
            }
        }

        protected override Task<int> WriteAsync(byte[] data, CancellationToken token)
        {
            return Task.Run(() =>
            {
                try
                {
                    ErrorCode ec = _writer.Write(data, 2000, out int written);
                    if (ec != ErrorCode.None)
                    {
                        SetState(DeviceState.Disconnected, "写入失败: " + ec);
                        throw new IOException("USB write failed: " + ec);
                    }

                    SetState(DeviceState.Connected);
                    return written;
                }
                catch (Exception ex)
                {
                    SetState(DeviceState.Disconnected, "写入异常: " + ex.Message);
                    throw;
                }
            }, token);
        }

        protected override int ReadChunk(byte[] buffer, int offset, int count, int timeoutMs, out bool timedOut)
        {
            timedOut = false;
            try
            {
                ErrorCode ec = _reader.Read(buffer, timeoutMs <= 0 ? 1 : timeoutMs, out int bytesRead);
                if (ec == ErrorCode.IoTimedOut)
                {
                    timedOut = true;
                    return 0;
                }
                if (ec != ErrorCode.None)
                {
                    SetState(DeviceState.Disconnected, "读取失败: " + ec);
                    throw new IOException("USB read failed: " + ec);
                }

                return bytesRead;
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "读取异常: " + ex.Message);
                throw;
            }
        }

        protected override void DoFlush()
        {
            try
            {
                _reader.Reset();
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "Flush异常: " + ex.Message);
                throw;
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            try
            {
                if (_device != null)
                {
                    if (_device.IsOpen)
                    {
                        if (_device is IUsbDevice whole)
                            whole.ReleaseInterface(_interfaceIndex);

                        _device.Close();
                    }
                    _device = null;
                }
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "Dispose异常: " + ex.Message);
            }
            finally
            {
                UsbDevice.Exit();
                SetState(DeviceState.Disconnected);
            }
        }
    }
}
