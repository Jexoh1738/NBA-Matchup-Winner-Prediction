using System;
using System.Collections.Generic;
using System.Linq;

namespace NbaBetting
{
    /// <summary>One "factor" bar on the form: a group of inputs and how much the network relies on it.</summary>
    public sealed class FactorShare
    {
        public string Name;
        public double Percent;   // all factors add up to 100
    }

    /// <summary>Everything the form needs to display after pressing Predict.</summary>
    public sealed class PredictionResult
    {
        public string Home, Away;
        public double HomeWinProb, AwayWinProb;          // 0..1, add up to 1
        public char[] HomeLast10, AwayLast10;            // 'W', 'L' or '-' (no game yet), oldest -> newest
        public double HomeAtHomeRate, AwayOnRoadRate;    // win rate at home / on the road (last 20 such games)
        public double HomeLast20, AwayLast20;            // overall win rate, last 20 games
        public List<FactorShare> Factors;                // sorted, biggest first
        public double TestAccuracy;                      // accuracy of the network on the untouched test set
    }

    /// <summary>A trained network plus the team histories needed to predict any matchup.</summary>
    public sealed class MatchPredictor
    {
        private const double NeutralRestDays = 2.0;   // a hypothetical game has no real schedule

        private readonly NeuralNetwork net;
        private readonly double[] min, max;
        private readonly Dictionary<string, TeamState> teams;
        private readonly List<FactorShare> factors;

        public double TestAccuracy { get; private set; }

        public MatchPredictor(NeuralNetwork net, double[] min, double[] max,
                              Dictionary<string, TeamState> teams, List<FactorShare> factors, double testAccuracy)
        {
            this.net = net;
            this.min = min;
            this.max = max;
            this.teams = teams;
            this.factors = factors;
            TestAccuracy = testAccuracy;
        }

        public List<string> TeamNames
        {
            get { return teams.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(); }
        }

        public PredictionResult Predict(string home, string away)
        {
            TeamState h, a;
            if (!teams.TryGetValue(home, out h)) throw new ArgumentException("Unknown team: " + home);
            if (!teams.TryGetValue(away, out a)) throw new ArgumentException("Unknown team: " + away);
            if (home == away) throw new ArgumentException("Home and away team must be different.");

            double[] raw = NbaBettingData.BuildFeatures(h, a, NeutralRestDays, NeutralRestDays);
            double[] x = NbaBettingData.Scale(raw, min, max);
            double[] o = net.Predict(x);
            double sum = o[0] + o[1];
            double pHome = sum < 1e-12 ? 0.5 : o[0] / sum;

            PredictionResult r = new PredictionResult();
            r.Home = home;
            r.Away = away;
            r.HomeWinProb = pHome;
            r.AwayWinProb = 1 - pHome;
            r.HomeLast10 = LastTen(h.Results);
            r.AwayLast10 = LastTen(a.Results);
            r.HomeAtHomeRate = TeamState.RawRate(h.HomeResults, 20);
            r.AwayOnRoadRate = TeamState.RawRate(a.RoadResults, 20);
            r.HomeLast20 = TeamState.RawRate(h.Results, 20);
            r.AwayLast20 = TeamState.RawRate(a.Results, 20);
            r.Factors = factors;
            r.TestAccuracy = TestAccuracy;
            return r;
        }

        private static char[] LastTen(List<bool> results)
        {
            char[] c = new char[10];
            for (int i = 0; i < 10; i++) c[i] = '-';
            int n = Math.Min(10, results.Count);
            for (int i = 0; i < n; i++)
                c[10 - n + i] = results[results.Count - n + i] ? 'W' : 'L';
            return c;
        }
    }

    /// <summary>Shared place where the trained model lives so any form/tab can use it.</summary>
    public static class ModelHolder
    {
        public static MatchPredictor Current { get; private set; }
        public static event Action Changed;

        public static void Set(MatchPredictor model)
        {
            Current = model;
            Action handler = Changed;
            if (handler != null) handler();
        }
    }
}
