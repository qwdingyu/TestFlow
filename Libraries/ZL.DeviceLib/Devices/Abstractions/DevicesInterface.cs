using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public interface IDevice
    {
        ExecutionResult Execute(StepConfig step, StepContext context);
    }
    public class ExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Outputs { get; set; }
    }
    /// <summary> 
    public interface IHealthyDevice
    {
        bool IsHealthy();
    }
    /// ֧���ԡ������� + ���������õ��豸ͳһ�ӿڡ� 
    /// �������豸�ӿڣ��Ƽ����� CAN��PLC �ȸ����豸��
    /// </summary>
    public interface ICapabilityDevice : IDevice
    {
        Task<Dictionary<string, object>> CallAsync(string capability, Dictionary<string, object> args, StepContext stepCtx);
    }
}

