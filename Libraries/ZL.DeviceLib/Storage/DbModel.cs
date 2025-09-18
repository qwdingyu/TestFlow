using SqlSugar;
using System;

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
}


