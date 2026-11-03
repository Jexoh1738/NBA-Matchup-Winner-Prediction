using CodingBackProp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NBA_prediction
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

            string originalCsv = DataPaths.FindFile("nba_2008-2026.csv");

            // Fallback: if it isn't found, let the user pick the file
            if (originalCsv == null)
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "Select nba_2008-2026.csv";
                    dlg.Filter = "CSV files (*.csv)|*.csv";
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    originalCsv = dlg.FileName;
                }
            }

            string cleanCsv = Path.Combine(DataPaths.OutputFolder, "nba_clean.csv");
            NbaDataCleaner.CleanFile(originalCsv, cleanCsv);

        }

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }

        private void tabPage3_Click(object sender, EventArgs e)
        {

        }

        private void tabPage1_Click(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label8_Click(object sender, EventArgs e)
        {

        }

        private void progressBar2_Click(object sender, EventArgs e)
        {

        }

        private void label42_Click(object sender, EventArgs e)
        {

        }
    }
}
