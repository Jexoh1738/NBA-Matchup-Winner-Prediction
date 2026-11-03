using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NbaBetting
{
    /// <summary>One data split: scaled inputs, one-hot targets and bookkeeping for baselines.</summary>
    public sealed class Dataset
    {
        public double[][] X;          // scaled features
        public double[][] T;          // targets: (1,0) = home win, (0,1) = away win
        public int[] Label;           // 0 = home win, 1 = away win
        public bool[] HomeFavored;    // used only for the "always pick the favorite" baseline
        public int Count { get { return X.Length; } }
    }

    /// <summary>
    /// Everything we remember about one team, built ONLY from games already played.
    /// Lists are oldest-first.
    /// </summary>
    public sealed class TeamState
    {
        public readonly List<bool> Results = new List<bool>();       // all games: true = won
        public readonly List<double> Margins = new List<double>();   // points margin from this team's view
        public readonly List<bool> HomeResults = new List<bool>();   // games played at home
        public readonly List<bool> RoadResults = new List<bool>();   // games played away
        public DateTime LastGame;
        public bool HasPlayed;

        public void Add(DateTime date, bool won, bool atHome, double margin)
        {
            Results.Add(won);
            Margins.Add(margin);
            if (atHome) HomeResults.Add(won); else RoadResults.Add(won);
            LastGame = date;
            HasPlayed = true;
        }

        /// <summary>Win rate over the last 'window' games, pulled slightly toward 50% when there are few games.</summary>
        public static double Rate(List<bool> list, int window)
        {
            int n = Math.Min(window, list.Count);
            int wins = 0;
            for (int i = list.Count - n; i < list.Count; i++) if (list[i]) wins++;
            return (wins + 2.5) / (n + 5.0);
        }

        /// <summary>Plain win rate (no smoothing) for display. 0.5 if no games yet.</summary>
        public static double RawRate(List<bool> list, int window)
        {
            int n = Math.Min(window, list.Count);
            if (n == 0) return 0.5;
            int wins = 0;
            for (int i = list.Count - n; i < list.Count; i++) if (list[i]) wins++;
            return (double)wins / n;
        }

        /// <summary>Average points margin over the last 'window' games (smoothed toward 0).</summary>
        public double AvgMargin(int window)
        {
            int n = Math.Min(window, Margins.Count);
            double sum = 0;
            for (int i = Margins.Count - n; i < Margins.Count; i++) sum += Margins[i];
            return sum / (n + 5.0);
        }

        /// <summary>Days since this team's last game, capped at 7.</summary>
        public double RestDays(DateTime date)
        {
            if (!HasPlayed) return 7;
            double d = (date - LastGame).TotalDays;
            return Math.Max(0, Math.Min(7, d));
        }
    }

    /// <summary>Result of loading: the three splits plus what is needed to predict new matchups.</summary>
    public sealed class LoadedData
    {
        public Dataset Train, Val, Test;
        public double[] Min, Max;                       // scaling taken from the training split
        public Dictionary<string, TeamState> Teams;     // each team's state after the LAST game in the file
    }

    /// <summary>
    /// Loads the cleaned NBA CSV and builds PRE-GAME team-form features for every game,
    /// using only games played earlier. The split is chronological
    /// (train = oldest, validation = middle, test = newest) and min-max scaling is
    /// fitted on the training split only. Final scores are used ONLY for the target
    /// and for updating each team's history AFTER its features were computed.
    ///
    /// Required columns (case-insensitive): date, home, away, score_home, score_away, whos_favored
    /// </summary>
    public static class NbaBettingData
    {
        public static readonly string[] FeatureNames = new string[]
        {
            "home_win_rate_20", "away_win_rate_20",
            "home_last10_rate",  "away_last10_rate",
            "home_margin_10",    "away_margin_10",
            "home_home_record",  "away_road_record",
            "home_rest_days",    "away_rest_days"
        };

        /// <summary>Features grouped into the "factors" shown on the form.</summary>
        public static readonly string[] GroupNames = new string[]
        {
            "Win rate (last 20 games)",
            "Recent form (last 10)",
            "Point margin (last 10)",
            "Home court / road record",
            "Rest days"
        };

        public static readonly int[][] GroupIndexes = new int[][]
        {
            new int[] { 0, 1 }, new int[] { 2, 3 }, new int[] { 4, 5 }, new int[] { 6, 7 }, new int[] { 8, 9 }
        };

        private sealed class Game
        {
            public DateTime Date;
            public string Home, Away;
            public bool HomeWon;
            public double Margin;        // home score - away score
            public bool HomeFavored;
        }

        private sealed class Row
        {
            public double[] Features;
            public int Label;
            public bool HomeFavored;
        }

        /// <summary>Builds the 10 input features for a matchup from the two teams' histories.</summary>
        public static double[] BuildFeatures(TeamState home, TeamState away, double homeRest, double awayRest)
        {
            return new double[]
            {
                TeamState.Rate(home.Results, 20), TeamState.Rate(away.Results, 20),
                TeamState.Rate(home.Results, 10), TeamState.Rate(away.Results, 10),
                home.AvgMargin(10),               away.AvgMargin(10),
                TeamState.Rate(home.HomeResults, 20), TeamState.Rate(away.RoadResults, 20),
                homeRest, awayRest
            };
        }

        public static LoadedData Load(string csvPath, double trainFrac, double valFrac, Action<string> log)
        {
            string[] lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2) throw new InvalidDataException("CSV is empty.");

            List<string> header = SplitCsv(lines[0]);
            Dictionary<string, int> col = new Dictionary<string, int>();
            for (int i = 0; i < header.Count; i++) col[header[i].Trim().ToLowerInvariant()] = i;

            string[] required = { "date", "home", "away", "score_home", "score_away", "whos_favored" };
            foreach (string name in required)
                if (!col.ContainsKey(name))
                    throw new InvalidDataException("Column '" + name + "' not found. CSV header is: " + string.Join(", ", header));

            UnknownTeamNames.Clear();
            List<Game> games = new List<Game>();
            int skipped = 0;
            for (int ln = 1; ln < lines.Length; ln++)
            {
                if (string.IsNullOrWhiteSpace(lines[ln])) continue;
                Game g = ParseGame(SplitCsv(lines[ln]), col);
                if (g == null) skipped++; else games.Add(g);
            }

            games = games.OrderBy(g => g.Date).ToList();   // oldest first (stable sort)
            if (games.Count < 100)
                throw new InvalidDataException("Only " + games.Count + " usable games were found - check the CSV format.");

            log(string.Format("Loaded {0} usable games ({1} rows skipped: missing/invalid values or ties).", games.Count, skipped));
            log(string.Format("Date range: {0:yyyy-MM-dd} to {1:yyyy-MM-dd}", games.First().Date, games.Last().Date));

            // Walk through history once. Features come from games BEFORE the current one.
            Dictionary<string, TeamState> teams = new Dictionary<string, TeamState>();
            List<Row> rows = new List<Row>(games.Count);
            foreach (Game g in games)
            {
                TeamState h = GetTeam(teams, g.Home);
                TeamState a = GetTeam(teams, g.Away);

                Row r = new Row();
                r.Features = BuildFeatures(h, a, h.RestDays(g.Date), a.RestDays(g.Date));
                r.Label = g.HomeWon ? 0 : 1;
                r.HomeFavored = g.HomeFavored;
                rows.Add(r);

                // only now add this game to the histories
                h.Add(g.Date, g.HomeWon, true, g.Margin);
                a.Add(g.Date, !g.HomeWon, false, -g.Margin);
            }
            log(string.Format("Teams found: {0}", teams.Count));
            if (UnknownTeamNames.Count > 0)
                log("Team names not recognised (kept exactly as written): " + string.Join(", ", UnknownTeamNames.OrderBy(s => s)));

            int nTrain = (int)(rows.Count * trainFrac);
            int nVal = (int)(rows.Count * valFrac);
            List<Row> trainRows = rows.GetRange(0, nTrain);
            List<Row> valRows = rows.GetRange(nTrain, nVal);
            List<Row> testRows = rows.GetRange(nTrain + nVal, rows.Count - nTrain - nVal);

            int nf = FeatureNames.Length;
            double[] min = new double[nf], max = new double[nf];
            for (int j = 0; j < nf; j++)
            {
                min[j] = trainRows.Min(r => r.Features[j]);
                max[j] = trainRows.Max(r => r.Features[j]);
            }

            LoadedData data = new LoadedData();
            data.Train = Build(trainRows, min, max);
            data.Val = Build(valRows, min, max);
            data.Test = Build(testRows, min, max);
            data.Min = min;
            data.Max = max;
            data.Teams = teams;

            log(string.Format("Chronological split -> train {0} | validation {1} | test {2}",
                data.Train.Count, data.Val.Count, data.Test.Count));
            return data;
        }

        private static TeamState GetTeam(Dictionary<string, TeamState> teams, string name)
        {
            TeamState t;
            if (!teams.TryGetValue(name, out t))
            {
                t = new TeamState();
                teams[name] = t;
            }
            return t;
        }

        private static Game ParseGame(List<string> f, Dictionary<string, int> col)
        {
            Func<string, string> get = name =>
            {
                int idx = col[name];
                return idx < f.Count ? f[idx].Trim() : "";
            };
            CultureInfo inv = CultureInfo.InvariantCulture;
            DateTime date;
            double scoreHome, scoreAway;

            if (!DateTime.TryParse(get("date"), inv, DateTimeStyles.None, out date)) return null;
            if (!double.TryParse(get("score_home"), NumberStyles.Float, inv, out scoreHome)) return null;
            if (!double.TryParse(get("score_away"), NumberStyles.Float, inv, out scoreAway)) return null;
            if (scoreHome == scoreAway) return null;

            string home = CanonicalTeam(get("home"));
            string away = CanonicalTeam(get("away"));
            if (home.Length == 0 || away.Length == 0) return null;

            string fav = get("whos_favored").ToLowerInvariant();
            if (fav != "home" && fav != "away") return null;

            Game g = new Game();
            g.Date = date;
            g.Home = home;
            g.Away = away;
            g.HomeWon = scoreHome > scoreAway;
            g.Margin = scoreHome - scoreAway;
            g.HomeFavored = fav == "home";
            return g;
        }

        // Names in the CSV that TeamInfo could not match (reported in the training log).
        private static readonly HashSet<string> UnknownTeamNames = new HashSet<string>();

        /// <summary>Turns "bos", "GS", "New Jersey Nets", ... into the current full team name.</summary>
        private static string CanonicalTeam(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            TeamInfo info = TeamInfo.Lookup(raw);
            if (info != null) return info.FullName;
            UnknownTeamNames.Add(raw);
            return raw;
        }

        private static Dataset Build(List<Row> rows, double[] min, double[] max)
        {
            Dataset d = new Dataset();
            int n = rows.Count, nf = FeatureNames.Length;
            d.X = new double[n][];
            d.T = new double[n][];
            d.Label = new int[n];
            d.HomeFavored = new bool[n];
            for (int i = 0; i < n; i++)
            {
                d.X[i] = Scale(rows[i].Features, min, max);
                d.Label[i] = rows[i].Label;
                d.T[i] = rows[i].Label == 0 ? new double[] { 1, 0 } : new double[] { 0, 1 };
                d.HomeFavored[i] = rows[i].HomeFavored;
            }
            return d;
        }

        /// <summary>Min-max scaling with the training-set min/max.</summary>
        public static double[] Scale(double[] raw, double[] min, double[] max)
        {
            double[] x = new double[raw.Length];
            for (int j = 0; j < raw.Length; j++)
            {
                double range = max[j] - min[j];
                x[j] = range < 1e-12 ? 0 : (raw[j] - min[j]) / range;
            }
            return x;
        }

        /// <summary>Quote-aware CSV line splitter.</summary>
        private static List<string> SplitCsv(string line)
        {
            List<string> result = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool inQuotes = false;
            foreach (char c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }
    }
}
