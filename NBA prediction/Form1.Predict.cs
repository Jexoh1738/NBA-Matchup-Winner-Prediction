using System;
using System.Collections.Generic;
using System.Windows.Forms;
using NbaBetting;

namespace NBA_prediction
{
    /// <summary>
    /// Hooks the trained network into every prediction tab (Eastern, Western, Randomize)
    /// and adds the Training tab. Both methods are called from the Form1 constructor:
    ///
    ///     public Form1()
    ///     {
    ///         InitializeComponent();
    ///         InitPredictTab();
    ///         AddTrainingTab();
    ///     }
    ///
    /// The per-tab work is done by PredictionTabBinder (see PredictionTabBinder.cs).
    /// </summary>
    partial class Form1
    {
        private TrainingPanel trainingPanel;
        private readonly List<PredictionTabBinder> binders = new List<PredictionTabBinder>();

        /// <summary>Connects every tab that has two team drop-downs and a Predict/Randomize button.</summary>
        private void InitPredictTab()
        {
            TabControl tabs = FindTabControlRecursive(this);
            if (tabs != null)
            {
                foreach (TabPage page in tabs.TabPages)
                {
                    PredictionTabBinder binder = PredictionTabBinder.TryCreate(page);
                    if (binder != null) binders.Add(binder);
                }
            }
            FormClosed += delegate
            {
                foreach (PredictionTabBinder b in binders) b.Detach();
            };
        }

        /// <summary>Adds the Training tab and starts training once the form is visible.</summary>
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
    }
}
