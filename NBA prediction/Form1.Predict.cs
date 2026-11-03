using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using NbaBetting;

namespace NBA_prediction
{
    /// <summary>
    /// Connects the Predict button on the Eastern tab to the trained network.
    ///
    /// REQUIRES these controls to be named exactly (select the control in the designer,
    /// then change "(Name)" in the Properties window):
    ///   cboHome            the Home Team drop-down
    ///   cboAway            the Away Team drop-down
    ///   btnPredict         the Predict button
    ///   lblPredictedWinner the "Predicted Winner" label
    ///   lblBarHome         the "Home Team" label on the left of the grey bar
    ///   lblBarAway         the "Away Team" label on the right of the grey bar
    ///   pbWinProb          the grey bar between them
    ///   lblOddsValue       the "Home Team / Away Team" label under "Network Odds"
    ///   lblWinRate         the "WinRate" label
    ///   lblTeamA           the "Team A" label (shows the home team)
    ///   lblTeamB           the "Team B" label (shows the away team)
    ///
    /// The 20 W/L boxes and the Factor rows are found automatically by their text and position.
    /// </summary>
    partial class Form1
    {
        private TrainingPanel trainingPanel;

        private readonly List<Control> boxesA = new List<Control>();      // Team A's 10 W/L boxes, left to right
        private readonly List<Control> boxesB = new List<Control>();      // Team B's 10 W/L boxes
        private readonly List<Control> factorNames = new List<Control>(); // "Factor 1", "Factor 2", ...
        private readonly List<Control> factorBars = new List<Control>();
        private readonly List<Control> factorPercents = new List<Control>(); // the "%" labels

        // ---------------------------------------------------------------- setup

        /// <summary>Call once from Form1_Load.</summary>
        private void InitPredictTab()
        {
            CacheDynamicControls();
            btnPredict.Click += PredictButton_Clicked;
            ModelHolder.Changed += OnModelChanged;
            FormClosed += delegate { ModelHolder.Changed -= OnModelChanged; };
            if (ModelHolder.Current != null) OnModelChanged();
        }

        /// <summary>Call once from Form1_Load. Adds the Training tab and starts training once the form is visible.</summary>
        private void AddTrainingTab()
        {
            trainingPanel = new TrainingPanel();
            trainingPanel.Dock = DockStyle.Fill;

            TabControl tabs = FindTabControlRecursive(this);
            if (tabs != null)
            {
                TabPage page = new TabPage("Training");
                page.Controls.Add(trainingPanel);
                tabs.TabPages.Add(page);
            }
            else
            {
                Controls.Add(trainingPanel);
                trainingPanel.BringToFront();
            }

            Shown += delegate { trainingPanel.StartTraining(); };
        }

        private static TabControl FindTabControlRecursive(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                TabControl t = c as TabControl;
                if (t != null) return t;
                TabControl inner = FindTabControlRecursive(c);
                if (inner != null) return inner;
            }
            return null;
        }

        // Finds the W/L boxes and Factor rows once, while they still show their design-time text.
        private void CacheDynamicControls()
        {
            Control scope = btnPredict.Parent;
            while (scope != null && !(scope is TabPage)) scope = scope.Parent;
            if (scope == null) scope = this;

            List<Control> all = new List<Control>();
            Collect(scope, all);

            // W/L boxes: every control whose text is exactly W or L, grouped into 2 rows by vertical position
            var boxes = all.Where(c => c.Text == "W" || c.Text == "L")
                           .Select(c => new { Ctl = c, Pos = PositionIn(c, scope) })
                           .ToList();
            var rows = boxes.GroupBy(b => b.Pos.Y).OrderBy(g => g.Key).Take(2).ToList();
            if (rows.Count > 0) boxesA.AddRange(rows[0].OrderBy(b => b.Pos.X).Select(b => b.Ctl));
            if (rows.Count > 1) boxesB.AddRange(rows[1].OrderBy(b => b.Pos.X).Select(b => b.Ctl));

            // Factor rows, top to bottom
            factorNames.AddRange(all.Where(c => c.Text.StartsWith("Factor", StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(c => PositionIn(c, scope).Y));
            factorPercents.AddRange(all.Where(c => c.Text.Trim() == "%")
                                       .OrderBy(c => PositionIn(c, scope).Y));
            factorBars.AddRange(all.Where(c => IsBar(c) && !ReferenceEquals(c, pbWinProb))
                                   .OrderBy(c => PositionIn(c, scope).Y));
        }

        private static void Collect(Control parent, List<Control> list)
        {
            foreach (Control c in parent.Controls)
            {
                list.Add(c);
                Collect(c, list);
            }
        }

        // Position of a control relative to 'scope', adding up Left/Top through its parents.
        private static Point PositionIn(Control c, Control scope)
        {
            int x = 0, y = 0;
            for (Control p = c; p != null && !ReferenceEquals(p, scope); p = p.Parent)
            {
                x += p.Left;
                y += p.Top;
            }
            return new Point(x, y);
        }

        private static bool IsBar(Control c)
        {
            Type t = c.GetType();
            return t.GetProperty("Value") != null && t.GetProperty("Maximum") != null;
        }

        // ---------------------------------------------------------------- events

        private void OnModelChanged()
        {
            if (InvokeRequired) { BeginInvoke(new Action(OnModelChanged)); return; }

            MatchPredictor model = ModelHolder.Current;
            if (model == null) return;

            object keepHome = cboHome.SelectedItem;
            object keepAway = cboAway.SelectedItem;
            cboHome.Items.Clear();
            cboAway.Items.Clear();
            foreach (string team in model.TeamNames)
            {
                cboHome.Items.Add(team);
                cboAway.Items.Add(team);
            }
            if (keepHome != null && cboHome.Items.Contains(keepHome)) cboHome.SelectedItem = keepHome;
            if (keepAway != null && cboAway.Items.Contains(keepAway)) cboAway.SelectedItem = keepAway;
        }

        private void PredictButton_Clicked(object sender, EventArgs e)
        {
            MatchPredictor model = ModelHolder.Current;
            if (model == null)
            {
                MessageBox.Show("The network is still training (or has not been trained yet).\n" +
                                "Open the Training tab to follow its progress, then press Predict again.",
                    "Network not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string home = cboHome.SelectedItem == null ? null : cboHome.SelectedItem.ToString();
            string away = cboAway.SelectedItem == null ? null : cboAway.SelectedItem.ToString();
            if (home == null || away == null)
            {
                MessageBox.Show("Please choose both a home team and an away team.",
                    "Missing team", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (home == away)
            {
                MessageBox.Show("The home team and the away team must be different.",
                    "Same team", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                ShowResult(model.Predict(home, away));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Prediction failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------------------------------------------------------- showing the result

        private void ShowResult(PredictionResult r)
        {
            bool homeWins = r.HomeWinProb >= 0.5;
            lblPredictedWinner.Text = "Predicted Winner: " + (homeWins ? r.Home : r.Away);

            // win-probability bar (fills with the HOME team's chance)
            lblBarHome.Text = r.Home;
            lblBarAway.Text = r.Away;
            SetBarValue(pbWinProb, r.HomeWinProb * 100.0);

            lblOddsValue.Text = string.Format("{0} {1:P0} / {2} {3:P0}",
                r.Home, r.HomeWinProb, r.Away, r.AwayWinProb);

            lblWinRate.Text = string.Format("WinRate: {0} {1:P0} at home | {2} {3:P0} on the road",
                r.Home, r.HomeAtHomeRate, r.Away, r.AwayOnRoadRate);

            lblTeamA.Text = r.Home;
            lblTeamB.Text = r.Away;

            for (int i = 0; i < boxesA.Count && i < 10; i++) SetFormBox(boxesA[i], r.HomeLast10[i]);
            for (int i = 0; i < boxesB.Count && i < 10; i++) SetFormBox(boxesB[i], r.AwayLast10[i]);

            // factor bars
            for (int i = 0; i < factorBars.Count; i++)
            {
                bool has = i < r.Factors.Count;
                SetBarValue(factorBars[i], has ? r.Factors[i].Percent : 0);
                if (i < factorNames.Count) factorNames[i].Text = has ? r.Factors[i].Name : "Factor " + (i + 1);
                if (i < factorPercents.Count) factorPercents[i].Text = has ? r.Factors[i].Percent.ToString("F0") + " %" : "%";
            }
        }

        private static void SetFormBox(Control box, char result)
        {
            box.Text = result.ToString();
            try
            {
                if (result == 'W') box.BackColor = Color.FromArgb(198, 239, 206);
                else if (result == 'L') box.BackColor = Color.FromArgb(255, 199, 206);
                else box.BackColor = SystemColors.Control;
            }
            catch (Exception) { }
        }

        // Works for ProgressBar and for custom progress-bar controls that have Value/Minimum/Maximum.
        private static void SetBarValue(Control bar, double percent)
        {
            try
            {
                Type t = bar.GetType();
                PropertyInfo valueProp = t.GetProperty("Value");
                if (valueProp == null) return;
                PropertyInfo maxProp = t.GetProperty("Maximum");
                PropertyInfo minProp = t.GetProperty("Minimum");
                int max = maxProp != null ? Convert.ToInt32(maxProp.GetValue(bar, null)) : 100;
                int min = minProp != null ? Convert.ToInt32(minProp.GetValue(bar, null)) : 0;
                double clamped = Math.Max(0, Math.Min(100, percent));
                int v = min + (int)Math.Round((max - min) * clamped / 100.0);
                valueProp.SetValue(bar, Convert.ChangeType(v, valueProp.PropertyType), null);
            }
            catch (Exception) { }
        }
    }
}
