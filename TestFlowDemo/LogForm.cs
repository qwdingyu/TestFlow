using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestFlowDemo
{
    public partial class LogForm : Form
    {
        private UiLogManager _log;

        public LogForm()
        {
            InitializeComponent();
            InitGrid();

            // 将 DataGridView 传给 UiLogManager
            _log = new UiLogManager(dgvLogs);
        }

        private void InitGrid()
        {
            dgvLogs.AutoGenerateColumns = false;
            dgvLogs.AllowUserToAddRows = false;
            dgvLogs.ReadOnly = true;
            dgvLogs.RowHeadersVisible = false;
            dgvLogs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            dgvLogs.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Time",
                HeaderText = "时间",
                Width = 180
            });

            dgvLogs.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Level",
                HeaderText = "级别",
                Width = 80
            });

            dgvLogs.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Message",
                HeaderText = "消息",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
        }

        private void btnTest_Click(object sender, EventArgs e)
        {
            _log.Add("系统启动", "INFO");
            _log.Add("警告: 温度偏高", "WARN");
            _log.Add("错误: 串口断开", "ERROR");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _log.Dispose(); // 释放后台线程
            base.OnFormClosing(e);
        }
    }
}
