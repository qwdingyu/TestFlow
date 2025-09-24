using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.DeviceLib;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.PlcBase;
using ZL.Tag;

public class PlcDevice : SiemensS7Driver, IDevice, ICapabilityDevice, IHealthyDevice, IDisposable
{
    private readonly ZL.Tag.DeviceConfig _cfg;
    string connString;
    public PlcDevice(ZL.Tag.DeviceConfig cfg) : base(cfg)
    {
        _cfg = cfg;
    }
    public bool IsHealthy() => !IsClosed;
    public ExecutionResult Execute(StepConfig step, StepContext ctx)
    {
        var dict = CallAsync(step.Command, step.Parameters ?? new(), ctx).GetAwaiter().GetResult();
        return new ExecutionResult { Success = true, Outputs = dict };
    }

    public async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
    {
        try
        {
            switch (cap)
            {
                case "Write":
                    {
                        string id = args["id"].ToString();
                        object value = args["value"];
                        var res = Write(id, value);
                        return new() { { "id", id }, { "address", getTagItemById(id)?.Address }, { "value", value }, { "result", res.IsSuccess } };
                    }
                case "Read":
                    {
                        string id = args["id"].ToString();
                        object value = Read<object>(id);
                        return new() { { "id", id }, { "address", getTagItemById(id)?.Address }, { "value", value }, { "result", true } };
                    }
                default:
                    throw new NotSupportedException($"{connString} PLC unsupported capability: {cap}");
            }
        }
        catch (Exception ex)
        {

            throw;
        }
    }
    public override void grp_DataChange(string id, object value, TagItem tag)
    {
        string tagId = tag.Id;
        bool tagVal = (bool)value;
        string address = tag.Address;
        LogHelper.Info("tag_id:" + tagId + ", address=" + address + " , val=" + tagVal);

        if (tagId == "3" && tagVal)
        {
            //LogHelper.Info($"上位机存储【{Sn}】对应的电批数据完成!!!", 1);
        }
    }
}