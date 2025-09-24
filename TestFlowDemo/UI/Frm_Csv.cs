using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CsvDataMaintenance
{
    public partial class Frm_Csv : Form
    {
        private CsvDataManager dataManager;
        private DataGridView dataGridView;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private ToolStrip toolStrip;
        private ComboBox fileTypeComboBox;
        private TreeView fileTreeView;
        private SplitContainer splitContainer;
        
        private string configDirectory = @"参数文件";

        private Dictionary<string, string> fileCategories = new Dictionary<string, string>
        {
            { "座椅参数", "前排主驾高配.csv,前排主驾低配.csv,前排副驾高配.csv,前排副驾低配.csv,二排左高配.csv,二排左低配.csv,二排左中配.csv,二排右高配.csv,二排右中配.csv,二排右低配.csv" },
            { "测试配置", "测试项目.csv,配置文件.csv,SCU设置.csv,系统信息.csv,测试参数设置.csv" },
            { "其他配置", "" }
        };

        public Frm_Csv()
        {
            InitializeComponent();
            InitializeDataManager();
            InitializeFileTree();
        }

        private void InitializeComponent()
        {
            this.Text = "CSV数据维护工具";
            this.Size = new System.Drawing.Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 创建工具栏
            toolStrip = new ToolStrip();
            
            var loadButton = new ToolStripButton("加载", null, LoadFile_Click);
            var saveButton = new ToolStripButton("保存", null, SaveFile_Click);
            var newButton = new ToolStripButton("新建", null, NewFile_Click);
            var addRowButton = new ToolStripButton("添加行", null, AddRow_Click);
            var deleteRowButton = new ToolStripButton("删除行", null, DeleteRow_Click);
            var addColumnButton = new ToolStripButton("添加列", null, AddColumn_Click);
            
            fileTypeComboBox = new ComboBox();
            fileTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            fileTypeComboBox.Items.AddRange(new string[] { "所有文件", "座椅参数", "测试配置", "其他配置" });
            fileTypeComboBox.SelectedIndex = 0;
            fileTypeComboBox.SelectedIndexChanged += FileTypeComboBox_SelectedIndexChanged;
            
            var fileTypeLabel = new ToolStripLabel("文件类型:");
            var fileTypeHost = new ToolStripControlHost(fileTypeComboBox);
            
            toolStrip.Items.AddRange(new ToolStripItem[] 
            {
                loadButton, saveButton, newButton, new ToolStripSeparator(),
                addRowButton, deleteRowButton, addColumnButton, new ToolStripSeparator(),
                fileTypeLabel, fileTypeHost
            });

            // 创建分割容器
            splitContainer = new SplitContainer();
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.SplitterDistance = 250;
            this.Controls.Add(splitContainer);
            this.Controls.Add(toolStrip);

            // 左侧文件树
            fileTreeView = new TreeView();
            fileTreeView.Dock = DockStyle.Fill;
            fileTreeView.AfterSelect += FileTreeView_AfterSelect;
            splitContainer.Panel1.Controls.Add(fileTreeView);

            // 右侧数据网格
            dataGridView = new DataGridView();
            dataGridView.Dock = DockStyle.Fill;
            dataGridView.AllowUserToAddRows = true;
            dataGridView.AllowUserToDeleteRows = true;
            dataGridView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridView.BackgroundColor = System.Drawing.SystemColors.Control;
            dataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView.MultiSelect = true;
            
            splitContainer.Panel2.Controls.Add(dataGridView);

            // 状态栏
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("准备就绪");
            statusStrip.Items.Add(statusLabel);
            this.Controls.Add(statusStrip);
        }

        private void InitializeDataManager()
        {
            dataManager = new CsvDataManager();
            dataManager.DataChanged += () => UpdateUI();
            dataManager.StatusUpdate += (msg) => statusLabel.Text = msg;
            dataManager.FileLoaded += (filename) => this.Text = $"CSV数据维护工具 - {filename}";
            
            dataGridView.DataSource = dataManager.DataTable;
        }

        private void InitializeFileTree()
        {
            fileTreeView.Nodes.Clear();
            
            if (!Directory.Exists(configDirectory))
                return;

            // 加载分类节点
            foreach (var category in fileCategories)
            {
                var categoryNode = fileTreeView.Nodes.Add(category.Key);
                categoryNode.Tag = "category";
                
                if (!string.IsNullOrEmpty(category.Value))
                {
                    var fileNames = category.Value.Split(',');
                    foreach (var fileName in fileNames)
                    {
                        if (File.Exists(Path.Combine(configDirectory, fileName.Trim())))
                        {
                            var fileNode = categoryNode.Nodes.Add(Path.GetFileNameWithoutExtension(fileName));
                            fileNode.Tag = Path.Combine(configDirectory, fileName.Trim());
                            fileNode.ImageKey = fileNode.SelectedImageKey = "file";
                        }
                    }
                }
                else
                {
                    // 对于"其他配置"，动态加载目录中的CSV文件
                    LoadOtherCsvFiles(categoryNode);
                }
            }
            
            fileTreeView.ExpandAll();
        }

        private void LoadOtherCsvFiles(TreeNode categoryNode)
        {
            if (!Directory.Exists(configDirectory))
                return;

            var csvFiles = Directory.GetFiles(configDirectory, "*.csv");
            var existingFiles = fileCategories.Values.SelectMany(v => v.Split(',') as IEnumerable<string>)
                .Where(v => !string.IsNullOrEmpty(v)).ToHashSet();
            
            foreach (var csvFile in csvFiles)
            {
                var fileName = Path.GetFileName(csvFile);
                if (!existingFiles.Contains(fileName))
                {
                    var fileNode = categoryNode.Nodes.Add(Path.GetFileNameWithoutExtension(fileName));
                    fileNode.Tag = csvFile;
                    fileNode.ImageKey = fileNode.SelectedImageKey = "file";
                }
            }
        }

        private void FileTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag != null && File.Exists(e.Node.Tag.ToString()))
            {
                if (CheckUnsavedChanges())
                    dataManager.LoadCsvFile(e.Node.Tag.ToString());
            }
        }

        private void FileTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            FilterFileTree(fileTypeComboBox.SelectedItem.ToString());
        }

        private void FilterFileTree(string fileType)
        {
            if (fileType == "所有文件")
            {
                fileTreeView.CollapseAll();
                fileTreeView.ExpandAll();
            }
            else
            {
                fileTreeView.CollapseAll();
            }
        }

        private void LoadFile_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "CSV文件|*.csv|所有文件|*.*";
                openFileDialog.InitialDirectory = Application.StartupPath;
                
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (CheckUnsavedChanges())
                        dataManager.LoadCsvFile(openFileDialog.FileName);
                }
            }
        }

        private void SaveFile_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(dataManager.CurrentFileName) && dataManager.CurrentFileName != "New File")
            {
                dataManager.SaveCsvFile();
            }
            else
            {
                SaveFileAs();
            }
        }

        private void SaveFileAs()
        {
            using (var saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "CSV文件|*.csv|所有文件|*.*";
                saveFileDialog.InitialDirectory = configDirectory;
                
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    dataManager.SaveCsvFile(saveFileDialog.FileName);
                    InitializeFileTree(); // 刷新文件树
                }
            }
        }

        private void NewFile_Click(object sender, EventArgs e)
        {
            if (CheckUnsavedChanges())
            {
                dataManager.CreateNewFile();
            }
        }

        private void AddRow_Click(object sender, EventArgs e)
        {
            dataManager.AddRow();
        }

        private void DeleteRow_Click(object sender, EventArgs e)
        {
            if (dataGridView.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow selectedRow in dataGridView.SelectedRows)
                {
                    if (!selectedRow.IsNewRow)
                    {
                        int rowIndex = selectedRow.Index;
                        if (rowIndex < dataManager.DataTable.Rows.Count)
                        {
                            dataManager.DataTable.Rows[rowIndex].Delete();
                        }
                    }
                }
            }
        }

        private void AddColumn_Click(object sender, EventArgs e)
        {
            using (var form = new Form())
            {
                form.Text = "添加新列";
                form.Size = new System.Drawing.Size(300, 120);
                form.StartPosition = FormStartPosition.CenterParent;

                var label = new Label() { Left = 20, Top = 20, Text = "列名:" };
                var textBox = new TextBox() { Left = 80, Top = 20, Width = 180 };
                var okButton = new Button() { Text = "确定", Left = 80, Top = 60, Width = 80 };
                var cancelButton = new Button() { Text = "取消", Left = 180, Top = 60, Width = 80 };

                okButton.Click += (s, e) => { form.DialogResult = DialogResult.OK; form.Close(); };
                cancelButton.Click += (s, e) => { form.DialogResult = DialogResult.Cancel; form.Close(); };

                form.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(textBox.Text))
                {
                    dataManager.AddColumn(textBox.Text);
                }
            }
        }

        private bool CheckUnsavedChanges()
        {
            if (dataManager.HasChanges)
            {
                var result = MessageBox.Show("文件有未保存的更改。是否保存？", "警告", 
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                
                if (result == DialogResult.Yes)
                {
                    SaveFile_Click(null, null);
                }
                else if (result == DialogResult.Cancel)
                {
                    return false;
                }
            }
            return true;
        }

        private void UpdateUI()
        {
            // 更新状态栏
            var rowCount = dataManager.DataTable.Rows.Count;
            var colCount = dataManager.DataTable.Columns.Count;
            statusLabel.Text = $"记录: {rowCount}  列: {colCount}  {(dataManager.HasChanges ? "(未保存)" : "")}";
        }

        //protected override void OnFormClosing(CancelEventArgs e)
        //{
        //    if (!CheckUnsavedChanges())
        //    {
        //        e.Cancel = true;
        //    }
        //    base.OnFormClosing(e);
        //}
    }
}