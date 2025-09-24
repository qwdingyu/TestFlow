using SqlSugar;
using System;
using System.Collections.Generic;

namespace ZL.DeviceLib.Storage
{

    // ʵ���ඨ�壨ʹ��ͨ�����ԣ����ݶ������ݿ⣩
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
    ///��Ʒ���Խ����¼��
    ///</summary>
    [SugarTable("seat_results")]
    public partial class SeatResults
    {
        public SeatResults() { }
        /// <summary>
        /// Desc:Ψһ��ʶ��
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }

        /// <summary>
        /// Desc:��Ʒ�ͺ�
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 100)]
        public string model { get; set; }

        /// <summary>
        /// Desc:��������
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length = 100)]
        public string sn { get; set; }

        /// <summary>
        /// Desc:��������
        /// Default:
        /// Nullable:False
        /// </summary>           
        public DateTime test_date { get; set; }

        /// <summary>
        /// Desc:����ʱ��
        /// Default:
        /// Nullable:False
        /// </summary>           
        public DateTime test_time { get; set; }

        /// <summary>
        /// Desc:���Խ��
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string test_result { get; set; }

        /// <summary>
        /// Desc:�߶�����
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? height_up { get; set; }

        /// <summary>
        /// Desc:�߶�����
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? height_down { get; set; }

        /// <summary>
        /// Desc:ˮƽ��ǰ
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? track_forward { get; set; }

        /// <summary>
        /// Desc:ˮƽ���
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? track_rearward { get; set; }

        /// <summary>
        /// Desc:������ǰ
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? recline_forward { get; set; }

        /// <summary>
        /// Desc:�������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? recline_rearward { get; set; }

        /// <summary>
        /// Desc:�������ϵ���
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? tilt_up_cur { get; set; }

        /// <summary>
        /// Desc:�������µ���
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? tilt_down_cur { get; set; }

        /// <summary>
        /// Desc:��������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? leg_rest_up { get; set; }

        /// <summary>
        /// Desc:��������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? leg_rest_down { get; set; }

        /// <summary>
        /// Desc:�����쳤
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? leg_rest_ex { get; set; }

        /// <summary>
        /// Desc:��������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? leg_rest_re { get; set; }

        /// <summary>
        /// Desc:ECUͨ��
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? ecu_blower { get; set; }

        /// <summary>
        /// Desc:����һ��
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? blower_fst { get; set; }

        /// <summary>
        /// Desc:���ȶ���
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? blower_snd { get; set; }

        /// <summary>
        /// Desc:��������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? blower_trd { get; set; }

        /// <summary>
        /// Desc:ECU����
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? ecu_heater { get; set; }

        /// <summary>
        /// Desc:����
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? heater { get; set; }

        /// <summary>
        /// Desc:��Χ��
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? ambient_light { get; set; }

        /// <summary>
        /// Desc:�ŵ�
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? foot_light { get; set; }

        /// <summary>
        /// Desc:��Ħ
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? massage { get; set; }

        /// <summary>
        /// Desc:����
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
        /// Desc:������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? ls_sound { get; set; }

        /// <summary>
        /// Desc:������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? rs_sound { get; set; }

        /// <summary>
        /// Desc:������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? retractor { get; set; }

        /// <summary>
        /// Desc:SBR�޼���
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? sbr_no_load { get; set; }

        /// <summary>
        /// Desc:SBR�����
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? sbr_load1 { get; set; }

        /// <summary>
        /// Desc:SBR�ؼ���
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? sbr_load2 { get; set; }

        /// <summary>
        /// Desc:SBR�ͷ�
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? sbr_release { get; set; }

        /// <summary>
        /// Desc:��ȫ����δ����
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? buckle_not_insert { get; set; }

        /// <summary>
        /// Desc:��ȫ���۲���
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? buckle_insert { get; set; }

        /// <summary>
        /// Desc:��ȫ�����ͷ�
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
        /// Desc:�లȫ����
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? side_airbag { get; set; }

        /// <summary>
        /// Desc:�లȫ�Ե�
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? safe_ground { get; set; }

        /// <summary>
        /// Desc:Զ������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? fsab { get; set; }

        /// <summary>
        /// Desc:Զ�˶Ե�
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? f_safe_ground { get; set; }

        /// <summary>
        /// Desc:��ȫ��Ԥ��
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? plp { get; set; }

        /// <summary>
        /// Desc:������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? l_sound { get; set; }

        /// <summary>
        /// Desc:������
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? r_sound { get; set; }

        /// <summary>
        /// Desc:����λ��
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public byte? delivery { get; set; }

        /// <summary>
        /// Desc:���Ժ�ʱ(��)
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public float? testing_time { get; set; }

        /// <summary>
        /// Desc:��¼����ʱ��
        /// Default:CURRENT_TIMESTAMP
        /// Nullable:False
        /// </summary>           
        /// 
        [SugarColumn(ColumnName = "created_at")]
        public DateTime created_at { get; set; }

        /// <summary>
        /// Desc:��¼����ʱ��
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
            // �߶� / ˮƽ / ����
            { "height_up.value", "height_up" },
            { "height_down.value", "height_down" },
            { "track_forward.value", "track_forward" },
            { "track_rearward.value", "track_rearward" },
            { "recline_forward.value", "recline_forward" },
            { "recline_rearward.value", "recline_rearward" },

            // �������
            { "tilt_up_cur.value", "tilt_up_cur" },
            { "tilt_down_cur.value", "tilt_down_cur" },

            // ����
            { "leg_rest_up.value", "leg_rest_up" },
            { "leg_rest_down.value", "leg_rest_down" },
            { "leg_rest_ex.value", "leg_rest_ex" },
            { "leg_rest_re.value", "leg_rest_re" },

            // ECU & ����
            { "ecu_blower.status", "ecu_blower" },
            { "blower.step1", "blower_fst" },
            { "blower.step2", "blower_snd" },
            { "blower.step3", "blower_trd" },

            // ����
            { "ecu_heater.status", "ecu_heater" },
            { "heater.level", "heater" },

            // �ƹ�
            { "ambient_light.status", "ambient_light" },
            { "foot_light.status", "foot_light" },

            // ��Ħ / ���� / USB
            { "massage.status", "massage" },
            { "wireless.status", "wireless" },
            { "usb.status", "usb" },

            // ����
            { "sound.left", "ls_sound" },
            { "sound.right", "rs_sound" },

            // ��ȫ�� & SBR
            { "retractor.status", "retractor" },
            { "sbr.no_load", "sbr_no_load" },
            { "sbr.load1", "sbr_load1" },
            { "sbr.load2", "sbr_load2" },
            { "sbr.release", "sbr_release" },

            // ��ȫ����
            { "buckle.not_insert", "buckle_not_insert" },
            { "buckle.insert", "buckle_insert" },
            { "buckle.release", "buckle_release" },

            // �¶� & ��ȫ����
            { "plp_ntc.value", "plp_ntc" },
            { "side_airbag.status", "side_airbag" },
            { "safe_ground.status", "safe_ground" },
            { "fsab.status", "fsab" },
            { "f_safe_ground.status", "f_safe_ground" },
            { "plp.status", "plp" },

            // ����
            { "l_sound.status", "l_sound" },
            { "r_sound.status", "r_sound" },

            // ����λ��
            { "delivery.status", "delivery" },

            //// ��ʱ
            //{ "testing_time.value", "testing_time" },

             // ֱ��ӳ���ж������ test_result
            { "final_check.pass", "test_result" }
        };

    }
}


