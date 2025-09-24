using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Transport;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;


namespace ZL.DeviceLib.Devices.Sp
{/// <summary>
 /// Modbus 能力设备：支持读写保持寄存器/线圈
 /// </summary>
    public sealed class ModbusDevice : ICapabilityDevice, IDisposable
    {
        private readonly ITransport _io;

        public ModbusDevice(DeviceConfig cfg)
        {
            try
            {
                _io = new SerialTransport(cfg.ConnectionString, cfg.Name);
            }
            catch (Exception)
            {

                throw;
            }
        }

        public void Dispose() => _io.Dispose();

        public ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            var dict = CallAsync(step.Command, step.Parameters ?? new(), ctx).GetAwaiter().GetResult();

            return new ExecutionResult { Success = true, Outputs = dict };
        }

        public async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
        {
            cap = cap ?? "";
            switch (cap)
            {
                case "ReadHoldingRegister":   // 参数: slaveId, address, count
                    {
                        int slave = Convert.ToInt32(args["slaveId"]);
                        int addr = Convert.ToInt32(args["address"]);
                        int count = Convert.ToInt32(args["count"]);
                        // TODO: 调用 Modbus 栈读寄存器（这里只是示意）
                        var values = new int[count];
                        return new() { { "values", values } };
                    }

                case "WriteHoldingRegister":  // 参数: slaveId, address, value
                    {
                        int slave = Convert.ToInt32(args["slaveId"]);
                        int addr = Convert.ToInt32(args["address"]);
                        int val = Convert.ToInt32(args["value"]);
                        // TODO: 调用 Modbus 栈写寄存器
                        return new() { { "written", true } };
                    }

                case "ReadCoil":              // 参数: slaveId, address, count
                    {
                        int slave = Convert.ToInt32(args["slaveId"]);
                        int addr = Convert.ToInt32(args["address"]);
                        int count = Convert.ToInt32(args["count"]);
                        var values = new bool[count];
                        return new() { { "values", values } };
                    }

                case "WriteCoil":             // 参数: slaveId, address, value
                    {
                        int slave = Convert.ToInt32(args["slaveId"]);
                        int addr = Convert.ToInt32(args["address"]);
                        bool val = Convert.ToBoolean(args["value"]);
                        return new() { { "written", true } };
                    }

                default:
                    throw new NotSupportedException($"Modbus capability not supported: {cap}");
            }
        }
    }
}
