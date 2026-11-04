using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NbaBetting
{
    /// <summary>Accuracy, confusion matrix, precision/recall and baseline comparison.</summary>
    public static class Evaluation
    {
        public static double Accuracy(NeuralNetwork net, Dataset d)
        {
            int ok = 0;
            for (int i = 0; i < d.Count; i++)
                if (net.PredictClass(d.X[i]) == d.Label[i]) ok++;
            return (double)ok / d.Count;
        }

        public static void Report(NeuralNetwork net, Dataset d, Action<string> log)
        {
            int[,] cm = new int[2, 2];   // [actual, predicted]
            for (int i = 0; i < d.Count; i++)
                cm[d.Label[i], net.PredictClass(d.X[i])]++;

            log("");
            log("=== TEST SET REPORT ===");
            log("Confusion matrix (rows = actual, columns = predicted)");
            log("                 Pred HomeWin   Pred AwayWin");
            log(string.Format("Actual HomeWin   {0,12}   {1,12}", cm[0, 0], cm[0, 1]));
            log(string.Format("Actual AwayWin   {0,12}   {1,12}", cm[1, 0], cm[1, 1]));

            string[] names = { "HomeWin", "AwayWin" };
            for (int c = 0; c < 2; c++)
            {
                int tp = cm[c, c];
                int predicted = cm[0, c] + cm[1, c];
                int actual = cm[c, 0] + cm[c, 1];
                double precision = predicted == 0 ? 0 : (double)tp / predicted;
                double recall = actual == 0 ? 0 : (double)tp / actual;
                log(string.Format("{0}: precision {1:P1} | recall {2:P1}", names[c], precision, recall));
            }

            double acc = Accuracy(net, d);
            int homeWins = 0, favWins = 0;
            for (int i = 0; i < d.Count; i++)
            {
                if (d.Label[i] == 0) homeWins++;
                if (d.HomeFavored[i] == (d.Label[i] == 0)) favWins++;
            }
            log("");
            log(string.Format("Network accuracy                  : {0:P2}", acc));
            log(string.Format("Baseline: always pick home team    : {0:P2}", (double)homeWins / d.Count));
            log(string.Format("Baseline: always pick the favorite : {0:P2}", (double)favWins / d.Count));
            log(acc > (double)favWins / d.Count
                ? "Result: the network beats the bookmaker's favorite."
                : "Result: the network does not beat the bookmaker's favorite (it only sees team form, not the betting line).");
        }
    }

    /// <summary>
    /// Runs the whole experiment: load -> grid search on VALIDATION data -> retrain best
    /// settings -> report on the untouched TEST data -> measure factor importance.
    /// Writes experiments.csv and training_log.csv and returns a MatchPredictor for the form.
    /// </summary>
    public static class BettingExperiment
    {

        /// <summary>Same as above, but also reports progress as a percentage (0-100).</summary>
        public static MatchPredictor Run(string csvPath, string outDir, Action<string> log, Action<int> progress)
        {
            Directory.CreateDirectory(outDir);
            LoadedData data = NbaBettingData.Load(csvPath, 0.70, 0.15, log);
            Dataset train = data.Train, val = data.Val, test = data.Test;
            if (progress != null) progress(5);

            int inputs = NbaBettingData.FeatureNames.Length;
            const int outputs = 2;

            double[] rates = { 0.01, 0.05, 0.1 };
            int[] hiddenSizes = { 4, 8, 16 };
            int[] epochOptions = { 200, 500 };

            double bestAcc = -1, bestRate = 0;
            int bestHidden = 0, bestEpochs = 0;
            int runsDone = 0;
            int totalRuns = rates.Length * hiddenSizes.Length * epochOptions.Length;

            using (StreamWriter exp = new StreamWriter(Path.Combine(outDir, "experiments.csv")))
            {
                exp.WriteLine("learning_rate,hidden_nodes,epochs,final_train_loss,val_accuracy");
                foreach (double lr in rates)
                    foreach (int hid in hiddenSizes)
                        foreach (int ep in epochOptions)
                        {
                            NeuralNetwork net = new NeuralNetwork(inputs, hid, outputs, 42);
                            net.Train(train.X, train.T, val.X, val.T, ep, lr, 100, 7, null, null);
                            double trainLoss = net.AverageLoss(train.X, train.T);
                            double valAcc = Evaluation.Accuracy(net, val);
                            exp.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "{0},{1},{2},{3:F6},{4:F4}", lr, hid, ep, trainLoss, valAcc));
                            log(string.Format("lr={0} hidden={1} epochs={2} -> val acc {3:P2}", lr, hid, ep, valAcc));
                            if (valAcc > bestAcc) { bestAcc = valAcc; bestRate = lr; bestHidden = hid; bestEpochs = ep; }
                            runsDone++;
                            if (progress != null) progress(5 + 85 * runsDone / totalRuns);
                        }
            }

            log("");
            log(string.Format("Best settings (by validation accuracy {0:P2}): lr={1}, hidden={2}, epochs={3}",
                bestAcc, bestRate, bestHidden, bestEpochs));
            log("Retraining with the best settings (loss logged every 100 epochs)...");

            NeuralNetwork finalNet = new NeuralNetwork(inputs, bestHidden, outputs, 42);
            using (StreamWriter csv = new StreamWriter(Path.Combine(outDir, "training_log.csv")))
            {
                finalNet.Train(train.X, train.T, val.X, val.T, bestEpochs, bestRate, 100, 7, csv, log);
            }

            Evaluation.Report(finalNet, test, log);
            double testAcc = Evaluation.Accuracy(finalNet, test);

            List<FactorShare> factors = FactorImportance(finalNet, val);
            log("");
            log("Factors the network relies on (permutation importance on validation data):");
            foreach (FactorShare f in factors)
                log(string.Format("  {0,-28} {1,5:F1} %", f.Name, f.Percent));

            if (progress != null) progress(100);
            return new MatchPredictor(finalNet, data.Min, data.Max, data.Teams, factors, testAcc);
        }

        /// <summary>
        /// For each group of inputs: shuffle that group across the validation games and see how
        /// much accuracy drops. A bigger drop = the network leans on that factor more.
        /// Results are scaled to add up to 100%.
        /// </summary>
        private static List<FactorShare> FactorImportance(NeuralNetwork net, Dataset d)
        {
            Random rng = new Random(123);
            double baseAcc = Evaluation.Accuracy(net, d);
            int groups = NbaBettingData.GroupNames.Length;
            double[] drop = new double[groups];
            const int repeats = 3;
            int n = d.Count;

            for (int g = 0; g < groups; g++)
            {
                double sum = 0;
                for (int rep = 0; rep < repeats; rep++)
                {
                    int[] perm = Enumerable.Range(0, n).OrderBy(i => rng.Next()).ToArray();
                    int correct = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double[] x = (double[])d.X[i].Clone();
                        foreach (int idx in NbaBettingData.GroupIndexes[g])
                            x[idx] = d.X[perm[i]][idx];
                        if (net.PredictClass(x) == d.Label[i]) correct++;
                    }
                    sum += baseAcc - (double)correct / n;
                }
                drop[g] = Math.Max(0, sum / repeats);
            }

            double total = drop.Sum();
            List<FactorShare> result = new List<FactorShare>();
            for (int g = 0; g < groups; g++)
            {
                FactorShare f = new FactorShare();
                f.Name = NbaBettingData.GroupNames[g];
                f.Percent = total < 1e-9 ? 100.0 / groups : drop[g] / total * 100.0;
                result.Add(f);
            }
            return result.OrderByDescending(f => f.Percent).ToList();
        }
    }
}
