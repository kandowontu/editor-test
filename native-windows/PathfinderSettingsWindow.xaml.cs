using System;
using System.Windows;

namespace FamidashEditor
{
    public partial class PathfinderSettingsWindow : Window
    {
        /// <summary>
        /// Jump timing bias: 0.0 = earliest viable jump, 0.25 = early, 0.5 = middle, 0.75 = late, 1.0 = latest.
        /// </summary>
        public double JumpTimingBias { get; private set; } = 0.5;

        /// <summary>
        /// True if the user clicked Calculate Path (OK), false if cancelled.
        /// </summary>
        public bool Confirmed { get; private set; } = false;

        /// <summary>
        /// True if the user wants the pathfinder to prefer collecting coins.
        /// </summary>
        public bool PreferCoins { get; private set; } = false;

        /// <summary>
        /// True if the path line should be drawn on the map when calculation completes.
        /// </summary>
        public bool DrawPathLine { get; private set; } = true;

        /// <summary>
        /// True if speculative paths should be shown in real-time during calculation.
        /// </summary>
        public bool ShowPathfinderLive { get; private set; } = true;

        /// <summary>
        /// True if attempted/backtracked paths should be shown after calculation.
        /// </summary>
        public bool ShowProspectivePaths { get; private set; } = false;

        /// <summary>
        /// Click optimization mode: 0 = none, 1 = least clicks, 2 = most clicks, 3 = just the clicks needed.
        /// </summary>
        public int ClickOptimization { get; private set; } = 0;

        public PathfinderSettingsWindow()
        {
            InitializeComponent();
            OptEarliest.Checked += (s, e) => UpdateLabel();
            OptEarly.Checked += (s, e) => UpdateLabel();
            OptMiddle.Checked += (s, e) => UpdateLabel();
            OptLate.Checked += (s, e) => UpdateLabel();
            OptLatest.Checked += (s, e) => UpdateLabel();
            UpdateLabel();
        }

        private void UpdateLabel()
        {
            if (OptEarliest.IsChecked == true)
                BiasLabel.Text = "Jump at the earliest frame that still survives";
            else if (OptEarly.IsChecked == true)
                BiasLabel.Text = "Jump early — 25% of the way from earliest to latest";
            else if (OptMiddle.IsChecked == true)
                BiasLabel.Text = "Jump at the midpoint between earliest and latest viable timing";
            else if (OptLate.IsChecked == true)
                BiasLabel.Text = "Jump late — 75% of the way from earliest to latest";
            else if (OptLatest.IsChecked == true)
                BiasLabel.Text = "Jump at the last possible frame that still survives";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (OptEarliest.IsChecked == true) JumpTimingBias = 0.0;
            else if (OptEarly.IsChecked == true) JumpTimingBias = 0.25;
            else if (OptMiddle.IsChecked == true) JumpTimingBias = 0.5;
            else if (OptLate.IsChecked == true) JumpTimingBias = 0.75;
            else if (OptLatest.IsChecked == true) JumpTimingBias = 1.0;

            Confirmed = true;
            PreferCoins = (ChkPreferCoins.IsChecked == true);
            DrawPathLine = (ChkDrawPathLine.IsChecked == true);
            ShowPathfinderLive = (ChkShowLive.IsChecked == true);
            ShowProspectivePaths = (ChkShowProspective.IsChecked == true);

            if (OptClicksLeast.IsChecked == true) ClickOptimization = 1;
            else if (OptClicksMost.IsChecked == true) ClickOptimization = 2;
            else if (OptClicksNeeded.IsChecked == true) ClickOptimization = 3;
            else ClickOptimization = 0;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
