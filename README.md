# NBA Matchup Winner Prediction

A Windows desktop app that predicts which NBA team will win a game, using a **multi-layer perceptron (neural network) written from scratch in C#**. No machine-learning libraries are used. The network, the training loop, the data pipeline, and the evaluation are all hand-coded.

Pick a home team and an away team, press **Predict**, and the app shows each team's win probability, their recent form, and which factors the network relied on most.

---

## Features

- **Custom neural network.** One hidden layer, sigmoid activations, and back-propagation trained one game at a time (`NeuralNetwork.cs`).
- **Automatic data cleaning.** Removes blank rows, short rows, missing scores, missing spreads or totals, missing favorites, tied games, and duplicate games, then prints a cleaning report (`NbaDataCleaner.cs`).
- **No-peeking features.** Each game's inputs are built only from games played *before* it, so the network never sees the future.
- **Grid search.** Tries 18 combinations of learning rate, hidden size, and epochs, and keeps the one with the best validation accuracy.
- **Honest evaluation.** Reports accuracy, a confusion matrix, and precision and recall on a test set the network never trained on. It also compares itself against two simple baselines.
- **Factor importance.** Shows how much the network leans on each group of inputs, as percentages that add up to 100.
- **Four-tab interface (Windows Forms):**
  - **Eastern:** matchups between Eastern Conference teams
  - **Western:** matchups between Western Conference teams
  - **Randomize:** picks a random matchup for you
  - **Training:** runs the cleaning and training, with a live log and progress bar

---

## How it works

1. **Load and clean** the games from `nba_2008-2026.csv`.
2. **Build features** for every game from each team's history (see the table below).
3. **Split by time, not at random:** oldest 70% for training, next 15% for validation, newest 15% for testing. The inputs are scaled to 0–1 using the training split only.
4. **Grid search** on the validation set (learning rate 0.01 / 0.05 / 0.1, hidden nodes 4 / 8 / 16, epochs 200 / 500).
5. **Retrain** with the best settings, then **test once** on the untouched test set.
6. **Predict** any matchup using each team's state after the last game in the file.

### The 10 input features

| Factor group | Inputs (home team and away team) |
|---|---|
| Win rate | Win rate over the last 20 games |
| Recent form | Win rate over the last 10 games |
| Point margin | Average winning or losing margin over the last 10 games |
| Home court | Home team's record at home, away team's record on the road (last 20 such games) |
| Rest days | Days since each team's last game (capped at 7) |

Win rates are nudged slightly toward 50% when a team has few games, so early-season numbers are not extreme.

### Network shape

```
10 inputs  ->  hidden layer (4, 8, or 16 nodes, chosen by grid search)  ->  2 outputs
                                                                 (home win, away win)
```

The two outputs are turned into a win probability for each team.

### Baselines

The test report compares the network to:

- always picking the **home team**
- always picking the **bookmaker's favorite**

Note that the betting lines (spread, total, moneyline) are **not** given to the network as inputs. It only sees team form, so beating the bookmaker's favorite is a hard bar.

---

## Dataset

`NBA prediction/datasets/` contains:

| File | What it is |
|---|---|
| `nba_2008-2026.csv` | Original data: about 24,440 games from the 2007–08 season through 2026 |
| `NbaDataCleaner.cs` | The code that does the cleaning |

Each row is one game. Columns include `season`, `date`, `away`, `home`, final and quarter scores, `whos_favored`, `spread`, `total`, moneylines, and second-half lines. The network uses only `date`, `home`, `away`, `score_home`, `score_away`, and `whos_favored` (the last one is only for the baseline).

Old team names and codes (for example, the New Jersey Nets or Seattle SuperSonics) are mapped to the current franchise, so each team has one continuous history (`TeamInfo.cs`).

---

## Getting started

Follow these steps in order, from nothing installed to a working prediction.

### 1. Install what you need

- **Windows** (this app uses Windows Forms, so it does not run on Mac or Linux)
- **Git**: <https://git-scm.com/downloads>
- **Visual Studio 2022**: <https://visualstudio.microsoft.com/downloads/> (the free Community edition is fine)
  - In the installer, tick the **".NET desktop development"** workload. This also installs .NET Framework 4.7.2, which the project needs.

### 2. Clone the repo

Open a terminal (Command Prompt, PowerShell, or Git Bash) and run:

```bash
git clone https://github.com/Jexoh1738/NBA-Matchup-Winner-Prediction.git
cd NBA-Matchup-Winner-Prediction
```

No Git? On the GitHub page, click **Code > Download ZIP**, then unzip it.

### 3. Open the project

- Double-click **`NBA prediction.slnx`**, or open Visual Studio and choose **File > Open > Project/Solution**.
- If Visual Studio says it cannot open `.slnx` files (older versions), open **`NBA prediction/NBA prediction.csproj`** instead. It is the same project.

### 4. Run it

1. Wait for Visual Studio to finish loading the project.
2. Press **F5** (or click the green **Start** button).
3. The window opens. The data file `nba_2008-2026.csv` is copied next to the program automatically.
   - If the app cannot find it, a file picker appears. Select `NBA prediction/datasets/nba_2008-2026.csv`.

### 5. Wait for training

1. Open the **Training** tab. Training starts by itself as soon as the window appears.
2. Watch the log and the progress bar. The app cleans the data, tries 18 settings, retrains the best one, and prints a test report.
3. Wait until the progress bar reaches **100%**. This can take a few minutes, depending on your computer.

You must wait for this step. The prediction tabs need the trained network.

### 6. Make a prediction

1. Go to the **Eastern**, **Western**, or **Randomize** tab.
2. Choose a **Home Team** and an **Away Team**. (On Eastern and Western, only teams from that conference are listed.)
3. Press **Predict**.
4. Read the result: the predicted winner, each team's win chance, each team's last 10 games (W or L), and the factors the network relied on most.

On the **Randomize** tab, each click picks a random matchup for you.

### If something goes wrong

| Problem | What to try |
|---|---|
| Visual Studio will not open the `.slnx` file | Open `NBA prediction/NBA prediction.csproj` instead, or update Visual Studio |
| "Project requires .NET Framework 4.7.2" | Re-run the Visual Studio installer and add the ".NET desktop development" workload |
| A file picker asks for the CSV | Select `NBA prediction/datasets/nba_2008-2026.csv` |
| The prediction tabs are empty or the Predict button does nothing | Training is not finished yet. Check the Training tab |
| You cannot find the output files | On the Training tab, click **Open output folder** |

### Files the program writes

These appear next to the running `.exe` (usually `NBA prediction/bin/Debug`):

| File | Contents |
|---|---|
| `nba_clean.csv` | The cleaned games |
| `experiments.csv` | Result of every grid-search run (learning rate, hidden nodes, epochs, train loss, validation accuracy) |
| `training_log.csv` | Training and validation loss for the final network, logged every 100 epochs |

---

## Project structure

```
NBA-Matchup-Winner-Prediction/
├── NBA prediction.slnx            Visual Studio solution
├── Back-Propagation/              Placeholder folder (currently empty)
└── NBA prediction/
    ├── NeuralNetwork.cs           The neural network and training loop
    ├── NbaBettingData.cs          Loading, feature building, splitting, scaling
    ├── BettingExperiment.cs       Grid search, final training, test report, factor importance
    ├── MatchPredictor.cs          Turns two team names into a prediction
    ├── TeamInfo.cs                30 teams: names, codes, conferences, old-name mapping
    ├── DataPaths.cs               Finds data files without hard-coded paths
    ├── TrainingPanel.cs           The Training tab (built in code)
    ├── PredictionTabBinder.cs     Connects each prediction tab to the model
    ├── Form1*.cs                  Main window and tab setup
    └── datasets/                  CSV data and the data cleaner
```

---

## Limitations

- The model sees only recent team results. It does not know about injuries, trades, or player stats.
- Rest days are set to a neutral 2 days for hypothetical matchups, because a made-up game has no real schedule.
- Conferences are the current ones, applied to the whole history.
- This is a learning project. It is **not** betting advice.

---

## Built with

- C# and Windows Forms (.NET Framework 4.7.2)
- A hand-written multi-layer perceptron, with no external ML libraries
