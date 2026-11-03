using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CodingBackProp
{
    public static class NbaDataCleaner
    {
        public static void CleanFile(string originalFilePath, string cleanFilePath, Action<string> log = null)
        {
            Action<string> say = log ?? Console.WriteLine;

            string[] allLines = File.ReadAllLines(originalFilePath);
            if (allLines.Length < 2) throw new Exception("The CSV file is empty: " + originalFilePath);

            string headerLine = allLines[0];
            string[] headerNames = headerLine.Split(',');

            // Find which column number each column name sits in
            int colDate = FindColumn(headerNames, "date");
            int colAway = FindColumn(headerNames, "away");
            int colHome = FindColumn(headerNames, "home");
            int colScoreAway = FindColumn(headerNames, "score_away");
            int colScoreHome = FindColumn(headerNames, "score_home");
            int colWhosFavored = FindColumn(headerNames, "whos_favored");
            int colSpread = FindColumn(headerNames, "spread");
            int colTotal = FindColumn(headerNames, "total");

            int highestColumnNeeded = Math.Max(Math.Max(Math.Max(colDate, colAway), Math.Max(colHome, colScoreAway)),
                Math.Max(Math.Max(colScoreHome, colWhosFavored), Math.Max(colSpread, colTotal)));

            // Counters for the report
            int numRowsAtStart = allLines.Length - 1;
            int numBlankLines = 0;
            int numShortRows = 0;
            int numMissingScore = 0;
            int numMissingSpread = 0;
            int numMissingTotal = 0;
            int numMissingFavorite = 0;
            int numTiedGames = 0;
            int numDuplicateGames = 0;

            List<string> goodLines = new List<string>();
            HashSet<string> gamesAlreadySeen = new HashSet<string>();

            for (int line = 1; line < allLines.Length; ++line)
            {
                string currentLine = allLines[line];

                // Problem: empty line
                if (currentLine.Trim() == "")
                {
                    ++numBlankLines;
                    continue;
                }

                string[] parts = currentLine.Split(',');

                // Problem: row has fewer columns than we need
                if (parts.Length <= highestColumnNeeded)
                {
                    ++numShortRows;
                    continue;
                }

                // Problem: final score missing or not a number
                double scoreAway, scoreHome;
                if (!TryReadNumber(parts[colScoreAway], out scoreAway) ||
                    !TryReadNumber(parts[colScoreHome], out scoreHome))
                {
                    ++numMissingScore;
                    continue;
                }

                // Problem: spread missing
                double spread;
                if (!TryReadNumber(parts[colSpread], out spread))
                {
                    ++numMissingSpread;
                    continue;
                }

                // Problem: total points missing
                double totalPoints;
                if (!TryReadNumber(parts[colTotal], out totalPoints))
                {
                    ++numMissingTotal;
                    continue;
                }

                // Problem: we don't know who the favorite is
                string favorite = parts[colWhosFavored].Trim().ToLower();
                if (favorite != "home" && favorite != "away")
                {
                    ++numMissingFavorite;
                    continue;
                }

                // Problem: tied game (a basketball game should always have a winner)
                if (scoreHome == scoreAway)
                {
                    ++numTiedGames;
                    continue;
                }

                // Problem: the same game listed twice (same date and same two teams)
                string gameKey = parts[colDate] + "|" + parts[colAway] + "|" + parts[colHome];
                if (!gamesAlreadySeen.Add(gameKey))
                {
                    ++numDuplicateGames;
                    continue;
                }

                goodLines.Add(currentLine); // passed every check
            }

            // Save the clean file (same columns, only the good rows)
            List<string> linesToSave = new List<string>();
            linesToSave.Add(headerLine);
            linesToSave.AddRange(goodLines);
            File.WriteAllLines(cleanFilePath, linesToSave);

            // Report
            int numRemoved = numRowsAtStart - goodLines.Count;
            say("==============================================");
            say("  DATA CLEANING REPORT");
            say("==============================================");
            say("  Games in original file:  " + numRowsAtStart.ToString("N0"));
            say("");
            say("  Problem                      Rows removed");
            say("  Empty line                   " + numBlankLines);
            say("  Row too short                " + numShortRows);
            say("  Missing final score          " + numMissingScore);
            say("  Missing spread               " + numMissingSpread);
            say("  Missing total points         " + numMissingTotal);
            say("  Missing favorite             " + numMissingFavorite);
            say("  Tied game                    " + numTiedGames);
            say("  Same game listed twice       " + numDuplicateGames);
            say("");
            say("  Total removed:           " + numRemoved.ToString("N0"));
            say("  Games kept (clean):      " + goodLines.Count.ToString("N0"));
            say("  Saved to: " + cleanFilePath);
            say("==============================================");
            say("");
        }

        // Turns text into a number. Returns false if the text is empty or not a number.
        private static bool TryReadNumber(string text, out double number)
        {
            return double.TryParse(text.Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number);
        }

        private static int FindColumn(string[] headerNames, string wantedName)
        {
            for (int i = 0; i < headerNames.Length; ++i)
                if (headerNames[i].Trim().ToLower() == wantedName)
                    return i;
            throw new Exception("Column not found in CSV: " + wantedName +
                ". Header found: " + string.Join(", ", headerNames));
        }
    }
}
