using SqlSugar;
using System;
using System.Collections.Generic;

namespace ZL.DeviceLib.Storage
{

    // 实体类定义（使用通用特性，兼容多种数据库）
    [SugarTable("test_params")]
    public class TestParams
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [SugarColumn(Length = 50)]
        public string Line { get; set; }

        [SugarColumn(ColumnName = "station_no", Length = 50)]
        public string StationNo { get; set; }

        [SugarColumn(Length = 50)]
        public string Model { get; set; }

        [SugarColumn(ColumnName = "step_name", Length = 100)]
        public string StepName { get; set; }

        [SugarColumn(ColumnName = "param_json", ColumnDataType = "text")]
        public string ParamJson { get; set; }

        public int Status { get; set; } = 1;
    }

    [SugarTable("test_results")]
    public class TestResult
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [SugarColumn(Length = 100)]
        public string SN { get; set; }

        [SugarColumn(Length = 50)]
        public string Model { get; set; }

        [SugarColumn(ColumnName = "started_at")]
        public DateTime StartedAt { get; set; }

        [SugarColumn(ColumnName = "ended_at", IsNullable = true)]
        public DateTime? EndedAt { get; set; }

        [SugarColumn(ColumnName = "final_status")]
        public int FinalStatus { get; set; }

        [SugarColumn(ColumnName = "report_path", Length = 500, IsNullable = true)]
        public string ReportPath { get; set; }
    }

    [SugarTable("test_steps")]
    public class TestStep
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [SugarColumn(ColumnName = "session_id")]
        public long SessionId { get; set; }

        [SugarColumn(Length = 100)]
        public string SN { get; set; }

        [SugarColumn(Length = 50)]
        public string Model { get; set; }

        [SugarColumn(ColumnName = "step_name", Length = 100)]
        public string StepName { get; set; }

        [SugarColumn(ColumnDataType = "text", IsNullable = true)]
        public string Description { get; set; }

        [SugarColumn(ColumnName = "device_name", Length = 100, IsNullable = true)]
        public string DeviceName { get; set; }

        [SugarColumn(Length = 200, IsNullable = true)]
        public string Command { get; set; }

        [SugarColumn(ColumnName = "parameters_json", ColumnDataType = "text", IsNullable = true)]
        public string ParametersJson { get; set; }

        [SugarColumn(ColumnName = "expected_json", ColumnDataType = "text", IsNullable = true)]
        public string ExpectedJson { get; set; }

        [SugarColumn(ColumnName = "outputs_json", ColumnDataType = "text", IsNullable = true)]
        public string OutputsJson { get; set; }

        public int Success { get; set; }

        [SugarColumn(ColumnDataType = "text", IsNullable = true)]
        public string Message { get; set; }

        [SugarColumn(ColumnName = "started_at")]
        public DateTime StartedAt { get; set; }

        [SugarColumn(ColumnName = "ended_at")]
        public DateTime EndedAt { get; set; }
    }

    ///<summary>
    ///产品测试结果记录表
    ///</summary>
    [SugarTable("seat_results")]
    public partial class SeatResults
    {
        public SeatResults() { }
        /// <summary>
        /// Desc:唯一标识符
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }

        /// <summary>
        /// Desc:产品型号
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 100)]
        public string model { get; set; }

        /// <summary>
        /// Desc:座椅条码
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 100)]
        public string sn { get; set; }

        /// <summary>
        /// Desc:测试日期
        /// Default:
        /// Nullable:False
        /// </summary>           
        public DateTime test_date { get; set; }

        /// <summary>
        /// Desc:测试时间
        /// Default:
        /// Nullable:False
        /// </summary>           
        public DateTime test_time { get; set; }

        /// <summary>
        /// Desc:测试结果
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string test_result { get; set; }

        /// <summary>
        /// Desc:高度向上
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? height_up { get; set; }

        /// <summary>
        /// Desc:高度向下
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? height_down { get; set; }

        /// <summary>
        /// Desc:水平向前
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? track_forward { get; set; }

        /// <summary>
        /// Desc:水平向后
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? track_rearward { get; set; }

        /// <summary>
        /// Desc:靠背向前
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? recline_forward { get; set; }

        /// <summary>
        /// Desc:靠背向后
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? recline_rearward { get; set; }

        /// <summary>
        /// Desc:坐盆向上电流
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? tilt_up_cur { get; set; }

        /// <summary>
        /// Desc:坐盆向下电流
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? tilt_down_cur { get; set; }

        /// <summary>
        /// Desc:腿托向上
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? leg_rest_up { get; set; }

        /// <summary>
        /// Desc:腿托向下
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? leg_rest_down { get; set; }

        /// <summary>
        /// Desc:腿托伸长
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? leg_rest_ex { get; set; }

        /// <summary>
        /// Desc:腿托缩回
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? leg_rest_re { get; set; }

        /// <summary>
        /// Desc:ECU通风
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? ecu_blower { get; set; }

        /// <summary>
        /// Desc:风扇一档
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? blower_fst { get; set; }

        /// <summary>
        /// Desc:风扇二档
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? blower_snd { get; set; }

        /// <summary>
        /// Desc:风扇三档
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? blower_trd { get; set; }

        /// <summary>
        /// Desc:ECU加热
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? ecu_heater { get; set; }

        /// <summary>
        /// Desc:加热
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? heater { get; set; }

        /// <summary>
        /// Desc:氛围灯
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? ambient_light { get; set; }

        /// <summary>
        /// Desc:脚灯
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? foot_light { get; set; }

        /// <summary>
        /// Desc:按摩
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? massage { get; set; }

        /// <summary>
        /// Desc:无线
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? wireless { get; set; }

        /// <summary>
        /// Desc:USB
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? usb { get; set; }

        /// <summary>
        /// Desc:左声音
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? ls_sound { get; set; }

        /// <summary>
        /// Desc:右声音
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? rs_sound { get; set; }

        /// <summary>
        /// Desc:卷缩器
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? retractor { get; set; }

        /// <summary>
        /// Desc:SBR无加载
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? sbr_no_load { get; set; }

        /// <summary>
        /// Desc:SBR轻加载
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? sbr_load1 { get; set; }

        /// <summary>
        /// Desc:SBR重加载
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? sbr_load2 { get; set; }

        /// <summary>
        /// Desc:SBR释放
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? sbr_release { get; set; }

        /// <summary>
        /// Desc:安全锁扣未插入
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? buckle_not_insert { get; set; }

        /// <summary>
        /// Desc:安全锁扣插入
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? buckle_insert { get; set; }

        /// <summary>
        /// Desc:安全锁扣释放
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? buckle_release { get; set; }

        /// <summary>
        /// Desc:PLPNTC
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? plp_ntc { get; set; }

        /// <summary>
        /// Desc:侧安全气囊
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? side_airbag { get; set; }

        /// <summary>
        /// Desc:侧安全对地
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? safe_ground { get; set; }

        /// <summary>
        /// Desc:远端气囊
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? fsab { get; set; }

        /// <summary>
        /// Desc:远端对地
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? f_safe_ground { get; set; }

        /// <summary>
        /// Desc:安全带预紧
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? plp { get; set; }

        /// <summary>
        /// Desc:左音响
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? l_sound { get; set; }

        /// <summary>
        /// Desc:右音响
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? r_sound { get; set; }

        /// <summary>
        /// Desc:发运位置
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? delivery { get; set; }

        /// <summary>
        /// Desc:测试耗时(秒)
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? testing_time { get; set; }

        /// <summary>
        /// Desc:记录创建时间
        /// Default:CURRENT_TIMESTAMP
        /// Nullable:False
        /// </summary>           
        /// 
        [SugarColumn(ColumnName = "created_at")]
        public DateTime created_at { get; set; }

        /// <summary>
        /// Desc:记录更新时间
        /// Default:CURRENT_TIMESTAMP
        /// Nullable:False
        /// </summary>           
        [SugarColumn(ColumnName = "updated_at")]
        public DateTime updated_at { get; set; }

    }


    public class TabColMapping
    {
        public static Dictionary<string, string> SeatResultMapping = new()
        {
            // 高度 / 水平 / 靠背
            { "height_up.value", "height_up" },
            { "height_down.value", "height_down" },
            { "track_forward.value", "track_forward" },
            { "track_rearward.value", "track_rearward" },
            { "recline_forward.value", "recline_forward" },
            { "recline_rearward.value", "recline_rearward" },

            // 坐盆电流
            { "tilt_up_cur.value", "tilt_up_cur" },
            { "tilt_down_cur.value", "tilt_down_cur" },

            // 腿托
            { "leg_rest_up.value", "leg_rest_up" },
            { "leg_rest_down.value", "leg_rest_down" },
            { "leg_rest_ex.value", "leg_rest_ex" },
            { "leg_rest_re.value", "leg_rest_re" },

            // ECU & 风扇
            { "ecu_blower.status", "ecu_blower" },
            { "blower.step1", "blower_fst" },
            { "blower.step2", "blower_snd" },
            { "blower.step3", "blower_trd" },

            // 加热
            { "ecu_heater.status", "ecu_heater" },
            { "heater.level", "heater" },

            // 灯光
            { "ambient_light.status", "ambient_light" },
            { "foot_light.status", "foot_light" },

            // 按摩 / 无线 / USB
            { "massage.status", "massage" },
            { "wireless.status", "wireless" },
            { "usb.status", "usb" },

            // 声音
            { "sound.left", "ls_sound" },
            { "sound.right", "rs_sound" },

            // 安全带 & SBR
            { "retractor.status", "retractor" },
            { "sbr.no_load", "sbr_no_load" },
            { "sbr.load1", "sbr_load1" },
            { "sbr.load2", "sbr_load2" },
            { "sbr.release", "sbr_release" },

            // 安全锁扣
            { "buckle.not_insert", "buckle_not_insert" },
            { "buckle.insert", "buckle_insert" },
            { "buckle.release", "buckle_release" },

            // 温度 & 安全气囊
            { "plp_ntc.value", "plp_ntc" },
            { "side_airbag.status", "side_airbag" },
            { "safe_ground.status", "safe_ground" },
            { "fsab.status", "fsab" },
            { "f_safe_ground.status", "f_safe_ground" },
            { "plp.status", "plp" },

            // 音响
            { "l_sound.status", "l_sound" },
            { "r_sound.status", "r_sound" },

            // 发运位置
            { "delivery.status", "delivery" },

            //// 耗时
            //{ "testing_time.value", "testing_time" },

             // 直接映射判定结果到 test_result
            { "final_check.pass", "test_result" }
        };

    }
}


