using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NbaBetting
{
    public sealed class Dataset
    {
        public double[][] X;          // scaled features
        public double[][] T;          // targets: (1,0) = home win, (0,1) = away win
        public int[] Label;           // 0 = home win, 1 = away win
        public bool[] HomeFavored;    // used for the "always pick the favorite" baseline
        public int Count { get { return X.Length; } }
    }

    public static class NbaBettingData
    {
        public static readonly string[] FeatureNames = new string[]
        {
            "home_spread",        // signed: negative = home favored
            "total",
            "home_implied_prob",  // from moneyline, vig removed
            "home_raw_prob",
            "away_raw_prob",
            "playoffs"
        };

        private sealed class Row
        {
            public DateTime Date;
            public double[] Features;
            public int Label;
            public bool HomeFavored;
        }

        public static void Load(string csvPath, double trainFrac, double valFrac,
                                out Dataset train, out Dataset val, out Dataset test,
                                Action<string> log)
        {
            string[] lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2) throw new InvalidDataException("CSV is empty.");

            List<string> header = SplitCsv(lines[0]);
            Dictionary<string, int> col = new Dictionary<string, int>();
            for (int i = 0; i < header.Count; i++) col[header[i].Trim().ToLowerInvariant()] = i;

            string[] required = { "date", "spread", "total", "moneyline_away", "moneyline_home",
                                  "whos_favored", "playoffs", "score_away", "score_home" };
            foreach (string name in required)
                if (!col.ContainsKey(name))
                    throw new InvalidDataException("Column '" + name + "' not found. CSV header is: " + string.Join(", ", header));

            int cHome = col.ContainsKey("home") ? col["home"] : -1;
            int cAway = col.ContainsKey("away") ? col["away"] : -1;

            List<Row> rows = new List<Row>();
            int skipped = 0;
            for (int ln = 1; ln < lines.Length; ln++)
            {
                if (string.IsNullOrWhiteSpace(lines[ln])) continue;
                List<string> f = SplitCsv(lines[ln]);
                Row r = ParseRow(f, col, cHome, cAway);
                if (r == null) skipped++; else rows.Add(r);
            }

            rows = rows.OrderBy(r => r.Date).ToList();   // chronological order
            log(string.Format("Loaded {0} usable games ({1} rows skipped: missing/invalid values or ties).", rows.Count, skipped));
            log(string.Format("Date range: {0:yyyy-MM-dd} to {1:yyyy-MM-dd}", rows.First().Date, rows.Last().Date));

            int nTrain = (int)(rows.Count * trainFrac);
            int nVal = (int)(rows.Count * valFrac);
            List<Row> trainRows = rows.GetRange(0, nTrain);
            List<Row> valRows = rows.GetRange(nTrain, nVal);
            List<Row> testRows = rows.GetRange(nTrain + nVal, rows.Count - nTrain - nVal);

            // Min-max scaling fitted on the training rows only
            int nf = FeatureNames.Length;
            double[] min = new double[nf], max = new double[nf];
            for (int j = 0; j < nf; j++)
            {
                min[j] = trainRows.Min(r => r.Features[j]);
                max[j] = trainRows.Max(r => r.Features[j]);
            }

            train = Build(trainRows, min, max);
            val = Build(valRows, min, max);
            test = Build(testRows, min, max);

            log(string.Format("Chronological split -> train {0} | validation {1} | test {2}", train.Count, val.Count, test.Count));
        }

        private static Row ParseRow(List<string> f, Dictionary<string, int> col, int cHome, int cAway)
        {
            Func<string, string> get = name =>
            {
                int idx = col[name];
                return idx < f.Count ? f[idx].Trim() : "";
            };
            double spread, total, mlAway, mlHome, scoreAway, scoreHome, playoffs;
            DateTime date;
            CultureInfo inv = CultureInfo.InvariantCulture;

            if (!DateTime.TryParse(get("date"), inv, DateTimeStyles.None, out date)) return null;
            if (!double.TryParse(get("spread"), NumberStyles.Float, inv, out spread)) return null;
            if (!double.TryParse(get("total"), NumberStyles.Float, inv, out total)) return null;
            if (!double.TryParse(get("moneyline_away"), NumberStyles.Float, inv, out mlAway)) return null;
            if (!double.TryParse(get("moneyline_home"), NumberStyles.Float, inv, out mlHome)) return null;
            if (!double.TryParse(get("score_away"), NumberStyles.Float, inv, out scoreAway)) return null;
            if (!double.TryParse(get("score_home"), NumberStyles.Float, inv, out scoreHome)) return null;
            if (scoreAway == scoreHome) return null;               // no ties in basketball
            if (mlAway == 0 || mlHome == 0) return null;

            string pl = get("playoffs").ToLowerInvariant();
            playoffs = (pl == "1" || pl == "true" || pl == "yes" || pl == "y") ? 1.0 : 0.0;

            // Who is the favorite?
            string fav = get("whos_favored").ToLowerInvariant();
            bool homeFav;
            if (fav == "home" || fav == "h") homeFav = true;
            else if (fav == "away" || fav == "a") homeFav = false;
            else if (cHome >= 0 && cHome < f.Count && fav == f[cHome].Trim().ToLowerInvariant()) homeFav = true;
            else if (cAway >= 0 && cAway < f.Count && fav == f[cAway].Trim().ToLowerInvariant()) homeFav = false;
            else return null;

            double absSpread = Math.Abs(spread);
            double homeSpread = homeFav ? -absSpread : absSpread;

            double pHome = ImpliedProb(mlHome);
            double pAway = ImpliedProb(mlAway);
            double homeFair = pHome / (pHome + pAway);   // remove the bookmaker's margin

            Row r = new Row();
            r.Date = date;
            r.Features = new double[] { homeSpread, total, homeFair, pHome, pAway, playoffs };
            r.Label = scoreHome > scoreAway ? 0 : 1;
            r.HomeFavored = homeFav;
            return r;
        }

        private static double ImpliedProb(double ml)
        {
            return ml < 0 ? (-ml) / (-ml + 100.0) : 100.0 / (ml + 100.0);
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
                double[] x = new double[nf];
                for (int j = 0; j < nf; j++)
                {
                    double range = max[j] - min[j];
                    x[j] = range < 1e-12 ? 0 : (rows[i].Features[j] - min[j]) / range;
                }
                d.X[i] = x;
                d.Label[i] = rows[i].Label;
                d.T[i] = rows[i].Label == 0 ? new double[] { 1, 0 } : new double[] { 0, 1 };
                d.HomeFavored[i] = rows[i].HomeFavored;
            }
            return d;
        }

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
