using SqlSugar;
using System;
using System.Collections.Generic;

namespace ZL.DeviceLib.Storage
{
    public class DbServices : IDatabaseService
    {
        private SqlSugarScope _db;
        public DbServices(string dbTypeString, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            SqlSugar.DbType dbType = ParseDbType(dbTypeString);
            init(dbType, connectionString);
        }

        public DbServices(SqlSugar.DbType dbType, string connectionString)
        {
            init(dbType, connectionString);
        }

        public void init(SqlSugar.DbType dbType, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            _db = new SqlSugarScope(new ConnectionConfig()
            {
                ConnectionString = connectionString,
                DbType = dbType,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            });

            InitDb();
        }

        // 从字符串转换为DbType的辅助方法
        public static SqlSugar.DbType ParseDbType(string dbTypeStr)
        {
            if (string.IsNullOrEmpty(dbTypeStr))
                throw new ArgumentException("Database type cannot be null or empty");

            return dbTypeStr.Trim().ToLower() switch
            {
                "mysql" => SqlSugar.DbType.MySql,
                "sqlserver" => SqlSugar.DbType.SqlServer,
                "sqlite" => SqlSugar.DbType.Sqlite,
                "oracle" => SqlSugar.DbType.Oracle,
                "postgresql" => SqlSugar.DbType.PostgreSQL,
                _ => throw new ArgumentException($"Unsupported database type: {dbTypeStr}")
            };
        }

        private void InitDb()
        {
            // 使用 Code First 方式创建表（如果不存在）
            //_db.CodeFirst.InitTables(typeof(TestParams), typeof(TestResult), typeof(TestStep));
        }

        public IEnumerable<TestParams> GetAllActiveParams()
        {
            return _db.Queryable<TestParams>()
                .Where(p => p.Status == 1)
                .ToList();
        }

        public Dictionary<string, object> QueryParamsForModel(string model)
        {
            // 保持接口存在，返回空字典
            return new Dictionary<string, object>();
        }

        public long StartTestSession(string productModel, string barcode)
        {
            var result = new TestResult
            {
                SN = barcode,
                Model = productModel,
                StartedAt = DateTime.Now,
                FinalStatus = 0
            };

            return _db.Insertable(result).ExecuteReturnBigIdentity();
        }

        public void FinishTestSession(long sessionId, int finalStatus = 1)
        {
            _db.Updateable<TestResult>()
                .SetColumns(r => new TestResult
                {
                    EndedAt = DateTime.Now,
                    FinalStatus = finalStatus
                })
                .Where(r => r.Id == sessionId)
                .ExecuteCommand();
        }

        public void AppendStep(
            long sessionId, string productModel, string barcode,
            string stepName, string description, string device, string command,
            string parameters, string expected, string outputs,
            int success, string message,
            DateTime started, DateTime ended)
        {
            var step = new TestStep
            {
                SessionId = sessionId,
                SN = barcode,
                Model = productModel,
                StepName = stepName,
                Description = description,
                DeviceName = device,
                Command = command,
                ParametersJson = parameters,
                ExpectedJson = expected,
                OutputsJson = outputs,
                Success = success,
                Message = message,
                StartedAt = started,
                EndedAt = ended
            };

            _db.Insertable(step).ExecuteCommand();
        }

        public void SaveReportPath(long sessionId, string reportPath)
        {
            _db.Updateable<TestResult>()
                .SetColumns(r => r.ReportPath == reportPath)
                .Where(r => r.Id == sessionId)
                .ExecuteCommand();
        }

        /// <summary>
        /// 保存测试结果（插入一条新记录）
        /// </summary>
        public int SaveSeatResults(SeatResults result)
        {
            try
            {
                return _db.Insertable(result).ExecuteReturnIdentity();
            }
            catch (Exception ex)
            {
                LogHelper.Error($"[DB] 保存失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 更新已有记录（比如修改测试结果或更新时间）
        /// </summary>
        public bool UpdateSeatResults(SeatResults result)
        {
            return _db.Updateable(result).ExecuteCommand() > 0;
        }

        /// <summary>
        /// 查询记录（根据条码）
        /// </summary>
        public SeatResults GetSeatResultBySn(string sn)
        {
            return _db.Queryable<SeatResults>()
                      .First(it => it.sn == sn);
        }
    }
}

