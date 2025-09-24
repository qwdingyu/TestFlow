using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ZL.Forms.Extension;

namespace TestFlowDemo
{

    // DataGridView 绑定模型（行对象），行级自动刷新
    public class StepInfo : GridRowBase
    {
        private string _status = "未开始";
        private double _elapsedMs;
        private string _actual = "";

        public string Name { get; set; }
        public string Description { get; set; }
        public string Target { get; set; }
        public string Command { get; set; }
        public string TechRange { get; set; }

        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        public double ElapsedMs
        {
            get => _elapsedMs;
            set { if (_elapsedMs != value) { _elapsedMs = value; OnPropertyChanged(); } }
        }

        public string Actual
        {
            get => _actual;
            set { if (_actual != value) { _actual = value; OnPropertyChanged(); } }
        }
    }
}
