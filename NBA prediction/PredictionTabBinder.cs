using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NbaBetting
{
    /// <summary>
    /// Makes ONE tab of the form work: fills its two team drop-downs and connects its
    /// Predict (and Randomize) button to the trained network.
    ///
    /// The tab's controls are found automatically by their text and position, so it works on
    /// any tab that has the same layout (Eastern, Western, Randomize). A control can also be found
    /// by name first: cboHome, cboAway, btnPredict, lblPredictedWinner, lblBarHome, lblBarAway,
    /// pbWinProb, lblOddsValue, lblWinRate, lblTeamA, lblTeamB.
    ///
    /// Which teams a tab offers is decided by its title: containing "east" = Eastern teams,
    /// "west" = Western teams, anything else (e.g. "Randomize") = all 30 teams.
    /// </summary>
    public sealed class PredictionTabBinder
    {
        // true : the Eastern tab lists only Eastern teams, the Western tab only Western teams.
        // false: every tab lists all teams (allows East-vs-West matchups on every tab).
        private const bool LimitToConference = true;

        private enum Mode { East, West, All }

        private readonly TabPage page;
        private readonly Mode mode;
        private readonly bool randomTab;   // tab title contains "random": every click picks a random matchup
        private readonly Random rng = new Random();

        private ComboBox cboHome, cboAway;
        private Control btnPredict, btnRandom;
        private Control lblWinner, lblBarHome, lblBarAway, lblOdds, lblWinRate, lblTeamA, lblTeamB, winBar;
        private readonly List<Control> boxesA = new List<Control>();
        private readonly List<Control> boxesB = new List<Control>();
        private readonly List<Control> factorNames = new List<Control>();
        private readonly List<Control> factorBars = new List<Control>();
        private readonly List<Control> factorPercents = new List<Control>();

        /// <summary>Returns a binder if this tab has two drop-downs and a Predict/Randomize button, otherwise null.</summary>
        public static PredictionTabBinder TryCreate(TabPage page)
        {
            PredictionTabBinder b = new PredictionTabBinder(page);
            return (b.cboHome != null && b.cboAway != null && (b.btnPredict != null || b.btnRandom != null)) ? b : null;
        }

        private PredictionTabBinder(TabPage page)
        {
            this.page = page;
            string title = (page.Text ?? "").ToLowerInvariant();
            if (LimitToConference && title.Contains("east")) mode = Mode.East;
            else if (LimitToConference && title.Contains("west")) mode = Mode.West;
            else mode = Mode.All;
            randomTab = title.Contains("random");

            DetectControls();
            if (cboHome == null || cboAway == null) return;

            cboHome.DropDownStyle = ComboBoxStyle.DropDownList;
            cboAway.DropDownStyle = ComboBoxStyle.DropDownList;
            // On the Randomize tab the main button picks a random matchup; elsewhere it predicts the chosen teams.
            if (btnPredict != null) btnPredict.Click += randomTab ? (EventHandler)RandomClicked : PredictClicked;
            if (btnRandom != null) btnRandom.Click += RandomClicked;
            ModelHolder.Changed += OnModelChanged;
            if (ModelHolder.Current != null) FillCombos();
        }

        /// <summary>Stops listening for model changes (call when the form closes).</summary>
        public void Detach()
        {
            ModelHolder.Changed -= OnModelChanged;
        }

        // ------------------------------------------------------------ finding the controls

        private void DetectControls()
        {
            List<Control> all = new List<Control>();
            Collect(page, all);

            // drop-downs: the top row's two combo boxes, left = home, right = away
            ComboBox named;
            named = FindByName("cboHome") as ComboBox;
            ComboBox namedAway = FindByName("cboAway") as ComboBox;
            if (named != null && namedAway != null)
            {
                cboHome = named;
                cboAway = namedAway;
            }
            else
            {
                List<ComboBox> combos = all.OfType<ComboBox>()
                    .OrderBy(c => PositionIn(c, page).Y).Take(2)
                    .OrderBy(c => PositionIn(c, page).X).ToList();
                if (combos.Count == 2) { cboHome = combos[0]; cboAway = combos[1]; }
            }

            btnPredict = FindByName("btnPredict") ?? all.FirstOrDefault(c => IsButtonLike(c) &&
                c.Text.Trim().Equals("Predict", StringComparison.OrdinalIgnoreCase));
            btnRandom = all.FirstOrDefault(c => IsButtonLike(c) && !ReferenceEquals(c, btnPredict) &&
                c.Text.IndexOf("random", StringComparison.OrdinalIgnoreCase) >= 0);

            // Fallback: the tab's button has some other label - use the first button on the tab.
            if (btnPredict == null && btnRandom == null)
                btnPredict = all.FirstOrDefault(c => IsButtonLike(c));

            lblWinner = FindByName("lblPredictedWinner") ?? FirstText(all, t => t.StartsWith("Predicted Winner", StringComparison.OrdinalIgnoreCase));

            // "Home Team" / "Away Team" appear twice: as drop-down captions (higher up) and beside the bar (lower)
            List<Control> homeLabels = TextControls(all, t => t == "Home Team");
            List<Control> awayLabels = TextControls(all, t => t == "Away Team");
            lblBarHome = FindByName("lblBarHome") ?? (homeLabels.Count >= 2 ? homeLabels[1] : null);
            lblBarAway = FindByName("lblBarAway") ?? (awayLabels.Count >= 2 ? awayLabels[1] : null);

            lblOdds = FindByName("lblOddsValue") ?? FirstText(all, t => t == "Home Team / Away Team");
            lblWinRate = FindByName("lblWinRate") ?? FirstText(all, t => t == "WinRate");
            lblTeamA = FindByName("lblTeamA") ?? FirstText(all, t => t == "Team A");
            lblTeamB = FindByName("lblTeamB") ?? FirstText(all, t => t == "Team B");

            // bars: the top one is the win-probability bar, the rest are the factor bars
            List<Control> bars = all.Where(IsBar).OrderBy(c => PositionIn(c, page).Y).ToList();
            winBar = FindByName("pbWinProb") ?? bars.FirstOrDefault();
            factorBars.AddRange(bars.Where(b => !ReferenceEquals(b, winBar)));

            factorNames.AddRange(all.Where(c => c.Text.StartsWith("Factor", StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(c => PositionIn(c, page).Y));
            factorPercents.AddRange(all.Where(c => c.Text.Trim() == "%")
                                       .OrderBy(c => PositionIn(c, page).Y));

            // last-10 boxes: controls reading exactly W or L, split into two rows
            var boxes = all.Where(c => c.Text == "W" || c.Text == "L")
                           .Select(c => new { Ctl = c, Pos = PositionIn(c, page) }).ToList();
            var rows = boxes.GroupBy(b => b.Pos.Y).OrderBy(g => g.Key).Take(2).ToList();
            if (rows.Count > 0) boxesA.AddRange(rows[0].OrderBy(b => b.Pos.X).Select(b => b.Ctl));
            if (rows.Count > 1) boxesB.AddRange(rows[1].OrderBy(b => b.Pos.X).Select(b => b.Ctl));
        }

        private Control FindByName(string name)
        {
            Control[] found = page.Controls.Find(name, true);
            return found.Length > 0 ? found[0] : null;
        }

        private static bool IsButtonLike(Control c)
        {
            return c is ButtonBase || c.GetType().Name.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<Control> TextControls(List<Control> all, Func<string, bool> match)
        {
            return all.Where(c => !(c is ComboBox) && !IsButtonLike(c) && match(c.Text.Trim()))
                      .OrderBy(c => c.Top).ToList();
        }

        private static Control FirstText(List<Control> all, Func<string, bool> match)
        {
            return all.Where(c => !(c is ComboBox) && !IsButtonLike(c) && match(c.Text.Trim()))
                      .OrderBy(c => c.Top).FirstOrDefault();
        }

        private static bool IsBar(Control c)
        {
            Type t = c.GetType();
            return t.GetProperty("Value") != null && t.GetProperty("Maximum") != null;
        }

        private static void Collect(Control parent, List<Control> list)
        {
            foreach (Control c in parent.Controls)
            {
                list.Add(c);
                Collect(c, list);
            }
        }

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

        // ------------------------------------------------------------ drop-downs

        private void OnModelChanged()
        {
            FillCombos();
        }

        private void FillCombos()
        {
            MatchPredictor model = ModelHolder.Current;
            if (model == null) return;

            List<string> teams;
            if (mode == Mode.East) teams = model.TeamsIn("East");
            else if (mode == Mode.West) teams = model.TeamsIn("West");
            else teams = model.TeamNames;
            if (teams.Count < 2) teams = model.TeamNames;   // fallback if conferences could not be worked out

            FillOne(cboHome, teams);
            FillOne(cboAway, teams);
        }

        private static void FillOne(ComboBox combo, List<string> teams)
        {
            object keep = combo.SelectedItem;
            combo.Items.Clear();
            foreach (string t in teams) combo.Items.Add(t);
            if (keep != null && combo.Items.Contains(keep)) combo.SelectedItem = keep;
        }

        // ------------------------------------------------------------ buttons

        private void RandomClicked(object sender, EventArgs e)
        {
            if (!ModelReady()) return;
            List<string> teams = cboHome.Items.Cast<object>().Select(o => o.ToString()).ToList();
            if (teams.Count < 2) return;

            int i = rng.Next(teams.Count);
            int j = rng.Next(teams.Count - 1);
            if (j >= i) j++;   // guarantees two different teams
            cboHome.SelectedItem = teams[i];
            cboAway.SelectedItem = teams[j];
            RunPrediction();
        }

        private void PredictClicked(object sender, EventArgs e)
        {
            if (!ModelReady()) return;
            RunPrediction();
        }

        private static bool ModelReady()
        {
            if (ModelHolder.Current != null) return true;
            MessageBox.Show("The network is still training (or has not been trained yet).\n" +
                            "Open the Training tab to follow its progress, then try again.",
                "Network not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private void RunPrediction()
        {
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
                ShowResult(ModelHolder.Current.Predict(home, away));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Prediction failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------ showing the result

        private void ShowResult(PredictionResult r)
        {
            bool homeWins = r.HomeWinProb >= 0.5;
            SetText(lblWinner, "Predicted Winner: " + (homeWins ? r.Home : r.Away));

            SetText(lblBarHome, r.HomeAbbrev);
            SetText(lblBarAway, r.AwayAbbrev);
            if (winBar != null) SetBarValue(winBar, r.HomeWinProb * 100.0);

            SetText(lblOdds, string.Format("{0} {1:P0} / {2} {3:P0}",
                r.HomeAbbrev, r.HomeWinProb, r.AwayAbbrev, r.AwayWinProb));
            SetText(lblWinRate, string.Format("WinRate: {0} {1:P0} at home | {2} {3:P0} on the road",
                r.HomeAbbrev, r.HomeAtHomeRate, r.AwayAbbrev, r.AwayOnRoadRate));

            SetText(lblTeamA, r.HomeAbbrev);
            SetText(lblTeamB, r.AwayAbbrev);

            for (int i = 0; i < boxesA.Count && i < 10; i++) SetFormBox(boxesA[i], r.HomeLast10[i]);
            for (int i = 0; i < boxesB.Count && i < 10; i++) SetFormBox(boxesB[i], r.AwayLast10[i]);

            for (int i = 0; i < factorBars.Count; i++)
            {
                bool has = i < r.Factors.Count;
                SetBarValue(factorBars[i], has ? r.Factors[i].Percent : 0);
                if (i < factorNames.Count) factorNames[i].Text = has ? r.Factors[i].Name : "Factor " + (i + 1);
                if (i < factorPercents.Count) factorPercents[i].Text = has ? r.Factors[i].Percent.ToString("F0") + " %" : "%";
            }
        }

        private static void SetText(Control c, string text)
        {
            if (c != null) c.Text = text;
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
