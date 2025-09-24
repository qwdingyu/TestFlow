PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for test_params
-- ----------------------------
DROP TABLE IF EXISTS "test_params";
CREATE TABLE "test_params" (
  "id" INTEGER PRIMARY KEY AUTOINCREMENT,
  "line" TEXT NOT NULL,
  "station_no" TEXT NOT NULL,
  "model" TEXT NOT NULL,
  "step_name" TEXT NOT NULL,
  "param_json" TEXT NOT NULL,
  "status" INTEGER NOT NULL DEFAULT 1,
  UNIQUE ("line" ASC, "station_no" ASC, "model" ASC, "step_name" ASC)
);

-- ----------------------------
-- Table structure for test_results
-- ----------------------------
DROP TABLE IF EXISTS "test_results";
CREATE TABLE "test_results" (
  "id" INTEGER PRIMARY KEY AUTOINCREMENT,
  "sn" TEXT,
  "model" TEXT,
  "started_at" TEXT,
  "ended_at" TEXT,
  "final_status" INTEGER,
  "report_path" TEXT
);

-- ----------------------------
-- Table structure for test_steps
-- ----------------------------
DROP TABLE IF EXISTS "test_steps";
CREATE TABLE "test_steps" (
  "id" INTEGER PRIMARY KEY AUTOINCREMENT,
  "session_id" INTEGER,
  "sn" TEXT,
  "model" TEXT,
  "step_name" TEXT,
  "description" TEXT,
  "device_name" TEXT,
  "command" TEXT,
  "parameters_json" TEXT,
  "expected_json" TEXT,
  "outputs_json" TEXT,
  "success" INTEGER,
  "message" TEXT,
  "started_at" TEXT,
  "ended_at" TEXT
);

-- ----------------------------
-- Auto increment value for test_params
-- ----------------------------
UPDATE "sqlite_sequence" SET seq = 21 WHERE name = 'test_params';

-- ----------------------------
-- Indexes structure for table test_params
-- ----------------------------
CREATE INDEX "idx_params_keys"
ON "test_params" (
  "line" ASC,
  "station_no" ASC,
  "model" ASC,
  "step_name" ASC,
  "status" ASC
);

-- ----------------------------
-- Records of test_params
-- ----------------------------
INSERT INTO "test_params" VALUES (1, 'L1', 'ST01', '*', '唤醒报文', '{"id":"0x12D","data":["0x00","0x00","0x00","0x00","0x0C","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (2, 'L1', 'ST01', '*', '主驾按摩', '{"id":"0x4C1","data":["0x00","0x18","0x00","0x00","0x80","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (3, 'L1', 'ST01', '*', '腰托按摩PMS主驾', '{"id":"0x21","data":["0x00","0x00","0x00","0x00","0x04","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (4, 'L1', 'ST01', '*', '腰托按摩SMA主驾', '{"id":"0x22","data":["0x03","0x04","0x00","0x00","0x40","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (5, 'L1', 'ST01', '*', '腰托向前SMA副驾', '{"id":"0x22","data":["0x00","0x38","0x00","0x00","0x00","0x00","0x00","0x15"]}', 1);
INSERT INTO "test_params" VALUES (6, 'L1', 'ST01', '*', '腰托向后SMA副驾', '{"id":"0x22","data":["0x00","0x58","0x00","0x00","0x00","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (7, 'L1', 'ST01', '*', '腰托向上SMA副驾', '{"id":"0x22","data":["0x00","0x78","0x00","0x00","0x00","0x00","0x00","0x15"]}', 1);
INSERT INTO "test_params" VALUES (8, 'L1', 'ST01', '*', '腰托向下SMA副驾', '{"id":"0x22","data":["0x00","0x98","0x00","0x00","0x00","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (9, 'L1', 'ST01', '*', '腰托向前SMA主驾', '{"id":"0x22","data":["0x07","0x00","0x00","0x00","0x00","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (10, 'L1', 'ST01', '*', '腰托向后SMA主驾', '{"id":"0x22","data":["0x0B","0x00","0x00","0x00","0x00","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (11, 'L1', 'ST01', '*', '腰托向上SMA主驾1', '{"id":"0x22","data":["0x0F","0x00","0x00","0x00","0x00","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (12, 'L1', 'ST01', '*', '腰托向上SMA主驾2', '{"id":"0x22","data":["0x13","0x00","0x00","0x00","0x00","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (13, 'L1', 'ST01', '*', 'PMS', '{"id":"0x21","data":["0x00","0x00","0x00","0x00","0x00","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (14, 'L1', 'ST01', 'E901', '唤醒报文', '{"id":"0x12D","data":["0x00","0x00","0x00","0x00","0x0C","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (15, 'L1', 'ST01', 'E901', '主驾按摩', '{"id":"0x4C1","data":["0x00","0x18","0x00","0x00","0x80","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (16, '*', '*', '*', '唤醒报文', '{"id":"0x12D","data":["0","0","0","0","0","0","0","0"]}', 1);
INSERT INTO "test_params" VALUES (17, '*', '*', '*', '主驾按摩', '{"id":"0x4C1","data":["0","0","0","0","0","0","0","0"]}', 1);
INSERT INTO "test_params" VALUES (18, 'L1', 'ST01', 'E311', '唤醒报文', '{"id":"0x12D","data":["0x00","0x00","0x00","0x00","0x0C","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (19, 'L1', 'ST01', 'E311', '主驾按摩', '{"id":"0x4C1","data":["0x00","0x18","0x00","0x00","0x80","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (20, 'L1', 'ST01', 'E541', '唤醒报文', '{"id":"0x12D","data":["0x00","0x00","0x00","0x00","0x0C","0x00","0x00","0x00"]}', 1);
INSERT INTO "test_params" VALUES (21, 'L1', 'ST01', 'E541', '主驾按摩', '{"id":"0x4C1","data":["0x00","0x18","0x00","0x00","0x80","0x00","0x00","0x00"]}', 1);


PRAGMA foreign_keys = true;
