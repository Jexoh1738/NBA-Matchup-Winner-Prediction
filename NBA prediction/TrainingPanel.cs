using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using NbaBetting;

namespace CodingBackProp
{
    public class TrainingPanel : UserControl
    {
        private const string CsvFileName = "nba_2008-2026.csv";

        private TextBox txtCsv;
        private Button btnBrowse;
        private Button btnRun;
        private Button btnOpenFolder;
        private ProgressBar progressBar;
        private Label lblStatus;
        private TextBox txtLog;

        public TrainingPanel()
        {
            BuildLayout();
            Load += TrainingPanel_Load;
        }

        private void BuildLayout()
        {
            Padding = new Padding(8);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Row 0: CSV path + Browse button
            Panel pathRow = new Panel();
            pathRow.Dock = DockStyle.Fill;
            txtCsv = new TextBox();
            txtCsv.Dock = DockStyle.Fill;
            txtCsv.ReadOnly = true;
            btnBrowse = new Button();
            btnBrowse.Text = "Browse...";
            btnBrowse.Dock = DockStyle.Right;
            btnBrowse.Width = 90;
            btnBrowse.Click += btnBrowse_Click;
            pathRow.Controls.Add(txtCsv);      // Fill control is added first
            pathRow.Controls.Add(btnBrowse);   // then the edge-docked one
            root.Controls.Add(pathRow, 0, 0);

            // Row 1: action buttons
            FlowLayoutPanel buttonRow = new FlowLayoutPanel();
            buttonRow.Dock = DockStyle.Fill;
            btnRun = new Button();
            btnRun.Text = "Clean data && train network";
            btnRun.Width = 200;
            btnRun.Height = 32;
            btnRun.Click += btnRun_Click;
            btnOpenFolder = new Button();
            btnOpenFolder.Text = "Open output folder";
            btnOpenFolder.Width = 150;
            btnOpenFolder.Height = 32;
            btnOpenFolder.Click += btnOpenFolder_Click;
            buttonRow.Controls.Add(btnRun);
            buttonRow.Controls.Add(btnOpenFolder);
            root.Controls.Add(buttonRow, 0, 1);

            // Row 2: progress bar
            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            root.Controls.Add(progressBar, 0, 2);

            // Row 3: status text
            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Text = "Ready.";
            root.Controls.Add(lblStatus, 0, 3);

            // Row 4: live log
            txtLog = new TextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.BackColor = Color.White;
            root.Controls.Add(txtLog, 0, 4);

            Controls.Add(root);
        }

        private void TrainingPanel_Load(object sender, EventArgs e)
        {
            string found = DataPaths.FindFile(CsvFileName);
            if (found != null)
            {
                txtCsv.Text = found;
                SetStatus("CSV found automatically. Press the button to clean and train.");
            }
            else
            {
                SetStatus("Could not find " + CsvFileName + ". Click Browse... to choose it.");
            }
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Select " + CsvFileName;
                dlg.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtCsv.Text = dlg.FileName;
                    SetStatus("CSV selected.");
                }
            }
        }

        private void btnOpenFolder_Click(object sender, EventArgs e)
        {
            Process.Start("explorer.exe", DataPaths.OutputFolder);
        }

        private async void btnRun_Click(object sender, EventArgs e)
        {
            string originalCsv = txtCsv.Text;
            if (string.IsNullOrEmpty(originalCsv) || !File.Exists(originalCsv))
            {
                MessageBox.Show("Please choose the NBA CSV file first (Browse...).",
                    "CSV not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string outDir = DataPaths.OutputFolder;
            string cleanCsv = Path.Combine(outDir, "nba_clean.csv");

            btnRun.Enabled = false;
            btnBrowse.Enabled = false;
            txtLog.Clear();
            progressBar.Value = 0;
            SetStatus("Cleaning data and training... this can take a few minutes.");

            try
            {
                // Everything inside Task.Run happens on a background thread
                await Task.Run(() =>
                {
                    NbaDataCleaner.CleanFile(originalCsv, cleanCsv, Log);
                    BettingExperiment.Run(cleanCsv, outDir, Log, SetProgress);
                });
                SetStatus("Done. Files saved in: " + outDir);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                SetStatus("Failed - see the log for details.");
            }
            finally
            {
                btnRun.Enabled = true;
                btnBrowse.Enabled = true;
            }
        }


        private void Log(string line)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() => txtLog.AppendText(line + Environment.NewLine)));
            }
            catch (InvalidOperationException) { }   // form closed while training
        }

        private void SetProgress(int percent)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                    progressBar.Value = Math.Max(0, Math.Min(100, percent))));
            }
            catch (InvalidOperationException) { }
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => lblStatus.Text = text)); }
            else { lblStatus.Text = text; }
        }
    }
}
