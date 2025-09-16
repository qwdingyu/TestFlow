using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace ZL.DeviceLib.Storage
{
    public class DatabaseService : IDatabaseService
    {
        private readonly string _dbPath;

        public DatabaseService(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath)) throw new ArgumentNullException(nameof(dbPath));
            // 归一化路径并确保目录存在
            var full = System.IO.Path.GetFullPath(dbPath);
            var dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            _dbPath = full;
            try
            {
                LogHelper.Info("[DB] SQLite path: " + _dbPath);
            }
            catch { }
            InitDb();
        }

        private SQLiteConnection GetConnection()
        {
            var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
            conn.Open();
            return conn;
        }

        private void InitDb()
        {
            // 使用打包的 Entities.sql 初始化/迁移数据库结构
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var sqlPath = System.IO.Path.Combine(baseDir, "Entities.sql");
            if (!System.IO.File.Exists(sqlPath))
            {
                var alt = System.IO.Path.Combine(baseDir, "Storage", "Entities.sql");
                if (System.IO.File.Exists(alt)) sqlPath = alt;
            }

            using (var conn = GetConnection())
            {
                // 若核心表已存在，则跳过脚本（避免重复 CREATE 导致报错），保持幂等
                bool hasParams;
                using (var check = conn.CreateCommand())
                {
                    check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='test_params'";
                    hasParams = check.ExecuteScalar() != null;
                }

                if (System.IO.File.Exists(sqlPath) && !hasParams)
                {
                    var script = System.IO.File.ReadAllText(sqlPath);
                    var parts = script.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var raw in parts)
                    {
                        var stmt = raw.Trim();
                        if (string.IsNullOrWhiteSpace(stmt)) continue;
                        if (stmt.StartsWith("--")) continue;
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = stmt;
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                else if (!hasParams)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        // 兜底建表逻辑需要与 Entities.sql 保持字段一致，避免旧库缺少 sn/model 字段导致插入报错
                        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS test_steps (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id INTEGER,
    sn TEXT,
    model TEXT,
    step_name TEXT,
    description TEXT,
    device_name TEXT,
    command TEXT,
    parameters_json TEXT,
    expected_json TEXT,
    outputs_json TEXT,
    success INTEGER,
    message TEXT,
    started_at TEXT,
    ended_at TEXT
);";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS test_results (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sn TEXT,
    model TEXT,
    started_at TEXT,
    ended_at TEXT,
    final_status INTEGER,
    report_path TEXT
);";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS test_params (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  line TEXT NOT NULL,
  station_no TEXT NOT NULL,
  model TEXT NOT NULL,
  step_name TEXT NOT NULL,
  param_json TEXT NOT NULL,
  status INTEGER NOT NULL DEFAULT 1
);";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        public IEnumerable<TestParamRow> GetAllActiveParams()
        {
            var list = new List<TestParamRow>();
            using (var conn = GetConnection())
            using (var cmd = new SQLiteCommand(
                "SELECT line, station_no, model, step_name, param_json, status FROM test_params WHERE status=1", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var row = new TestParamRow();
                    row.Line = reader["line"].ToString();
                    row.StationNo = reader["station_no"].ToString();
                    row.Model = reader["model"].ToString();
                    row.StepName = reader["step_name"].ToString();
                    row.ParamJson = reader["param_json"].ToString();
                    row.Status = reader["status"] == null ? 1 : Convert.ToInt32(reader["status"]);
                    list.Add(row);
                }
            }
            return list;
        }

        public Dictionary<string, object> QueryParamsForModel(string model)
        {
            // 当前 schema 中 test_params 为逐步骤参数，不提供整机型聚合 JSON
            // 保持接口存在，返回空字典即可（用于 Mock 设备）。
            return new Dictionary<string, object>();
        }

        public long StartTestSession(string productModel, string barcode)
        {
            using (var conn = GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "INSERT INTO test_results (sn, model, started_at, final_status) VALUES (@sn, @model, @start, 0);";
                cmd.Parameters.AddWithValue("@sn", barcode);
                cmd.Parameters.AddWithValue("@model", productModel);
                cmd.Parameters.AddWithValue("@start", DateTime.Now);
                cmd.ExecuteNonQuery();
                return conn.LastInsertRowId;
            }
        }

        public void FinishTestSession(long sessionId, int finalStatus = 1)
        {
            using (var conn = GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "UPDATE test_results SET ended_at=@end, final_status=@st WHERE id=@id;";
                cmd.Parameters.AddWithValue("@end", DateTime.Now);
                cmd.Parameters.AddWithValue("@st", finalStatus);
                cmd.Parameters.AddWithValue("@id", sessionId);
                cmd.ExecuteNonQuery();
            }
        }

        public void AppendStep(
            long sessionId, string productModel, string barcode,
            string stepName, string description, string device, string command,
            string parameters, string expected, string outputs,
            int success, string message,
            DateTime started, DateTime ended)
        {
            using (var conn = GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                INSERT INTO test_steps
                (session_id, sn, model, step_name, description, device_name, command,
                 parameters_json, expected_json, outputs_json, success, message, started_at, ended_at)
                VALUES
                (@sid, @sn, @model, @sname, @desc, @dev, @cmd, @params, @exp, @out, @succ, @msg, @st, @ed);";

                cmd.Parameters.AddWithValue("@sid", sessionId);
                cmd.Parameters.AddWithValue("@sn", barcode);
                cmd.Parameters.AddWithValue("@model", productModel);
                cmd.Parameters.AddWithValue("@sname", stepName);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@dev", device);
                cmd.Parameters.AddWithValue("@cmd", command);
                cmd.Parameters.AddWithValue("@params", parameters);
                cmd.Parameters.AddWithValue("@exp", expected);
                cmd.Parameters.AddWithValue("@out", outputs);
                cmd.Parameters.AddWithValue("@succ", success);
                cmd.Parameters.AddWithValue("@msg", message);
                cmd.Parameters.AddWithValue("@st", started);
                cmd.Parameters.AddWithValue("@ed", ended);
                cmd.ExecuteNonQuery();
            }
        }

        public void SaveReportPath(long sessionId, string reportPath)
        {
            using (var conn = GetConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "UPDATE test_results SET report_path=@rp WHERE id=@id;";
                cmd.Parameters.AddWithValue("@rp", reportPath);
                cmd.Parameters.AddWithValue("@id", sessionId);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
