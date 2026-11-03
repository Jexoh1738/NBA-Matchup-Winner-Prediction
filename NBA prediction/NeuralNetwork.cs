using System;
using System.IO;

namespace NbaBetting
{
    public sealed class NeuralNetwork
    {
        public int InputCount { get; private set; }
        public int HiddenCount { get; private set; }
        public int OutputCount { get; private set; }

        private readonly double[,] wInputHidden;   // [hidden, input]
        private readonly double[,] wHiddenOutput;  // [output, hidden]
        private readonly double[] biasHidden;
        private readonly double[] biasOutput;

        private readonly double[] hiddenOut;
        private readonly double[] finalOut;
        private readonly double[] deltaOut;
        private readonly double[] deltaHidden;

        public NeuralNetwork(int inputCount, int hiddenCount, int outputCount, int seed)
        {
            InputCount = inputCount;
            HiddenCount = hiddenCount;
            OutputCount = outputCount;

            wInputHidden = new double[hiddenCount, inputCount];
            wHiddenOutput = new double[outputCount, hiddenCount];
            biasHidden = new double[hiddenCount];
            biasOutput = new double[outputCount];

            hiddenOut = new double[hiddenCount];
            finalOut = new double[outputCount];
            deltaOut = new double[outputCount];
            deltaHidden = new double[hiddenCount];

            // Small random starting weights (Xavier/Glorot uniform range)
            Random rng = new Random(seed);
            double r1 = Math.Sqrt(6.0 / (inputCount + hiddenCount));
            double r2 = Math.Sqrt(6.0 / (hiddenCount + outputCount));
            for (int h = 0; h < hiddenCount; h++)
                for (int i = 0; i < inputCount; i++)
                    wInputHidden[h, i] = (rng.NextDouble() * 2 - 1) * r1;
            for (int k = 0; k < outputCount; k++)
                for (int h = 0; h < hiddenCount; h++)
                    wHiddenOutput[k, h] = (rng.NextDouble() * 2 - 1) * r2;
        }

        private static double Sigmoid(double x)
        {
            return 1.0 / (1.0 + Math.Exp(-x));
        }

        private void Forward(double[] x)
        {
            for (int h = 0; h < HiddenCount; h++)
            {
                double sum = biasHidden[h];
                for (int i = 0; i < InputCount; i++)
                    sum += wInputHidden[h, i] * x[i];
                hiddenOut[h] = Sigmoid(sum);
            }
            for (int k = 0; k < OutputCount; k++)
            {
                double sum = biasOutput[k];
                for (int h = 0; h < HiddenCount; h++)
                    sum += wHiddenOutput[k, h] * hiddenOut[h];
                finalOut[k] = Sigmoid(sum);
            }
        }

        public double[] Predict(double[] x)
        {
            Forward(x);
            return (double[])finalOut.Clone();
        }

        public int PredictClass(double[] x)
        {
            Forward(x);
            int best = 0;
            for (int k = 1; k < OutputCount; k++)
                if (finalOut[k] > finalOut[best]) best = k;
            return best;
        }

        public double TrainSample(double[] x, double[] target, double learningRate)
        {
            Forward(x);

            double loss = 0;
            for (int k = 0; k < OutputCount; k++)
            {
                double err = target[k] - finalOut[k];
                loss += 0.5 * err * err;
                deltaOut[k] = finalOut[k] * (1 - finalOut[k]) * err;
            }

            for (int h = 0; h < HiddenCount; h++)
            {
                double back = 0;
                for (int k = 0; k < OutputCount; k++)
                    back += wHiddenOutput[k, h] * deltaOut[k];
                deltaHidden[h] = hiddenOut[h] * (1 - hiddenOut[h]) * back;
            }

            for (int k = 0; k < OutputCount; k++)
            {
                for (int h = 0; h < HiddenCount; h++)
                    wHiddenOutput[k, h] += learningRate * deltaOut[k] * hiddenOut[h];
                biasOutput[k] += learningRate * deltaOut[k];
            }

            for (int h = 0; h < HiddenCount; h++)
            {
                for (int i = 0; i < InputCount; i++)
                    wInputHidden[h, i] += learningRate * deltaHidden[h] * x[i];
                biasHidden[h] += learningRate * deltaHidden[h];
            }

            return loss;
        }

        public double AverageLoss(double[][] inputs, double[][] targets)
        {
            double total = 0;
            for (int n = 0; n < inputs.Length; n++)
            {
                Forward(inputs[n]);
                for (int k = 0; k < OutputCount; k++)
                {
                    double err = targets[n][k] - finalOut[k];
                    total += 0.5 * err * err;
                }
            }
            return total / inputs.Length;
        }

        public void Train(double[][] trainX, double[][] trainT, double[][] valX, double[][] valT,
                          int epochs, double learningRate, int logEvery, int seed,
                          TextWriter csvLog, Action<string> log)
        {
            Random rng = new Random(seed);
            int n = trainX.Length;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            if (csvLog != null) csvLog.WriteLine("epoch,train_loss,val_loss");

            for (int epoch = 1; epoch <= epochs; epoch++)
            {
                for (int i = n - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
                }

                double epochLoss = 0;
                for (int s = 0; s < n; s++)
                    epochLoss += TrainSample(trainX[order[s]], trainT[order[s]], learningRate);
                epochLoss /= n;

                if (epoch == 1 || epoch % logEvery == 0 || epoch == epochs)
                {
                    double v = (valX != null && valX.Length > 0) ? AverageLoss(valX, valT) : double.NaN;
                    string line = string.Format("Epoch {0,5} | train loss {1:F5} | val loss {2:F5}", epoch, epochLoss, v);
                    if (log != null) log(line);
                    if (csvLog != null)
                        csvLog.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0},{1:F6},{2:F6}", epoch, epochLoss, v));
                }
            }
        }
    }
}
