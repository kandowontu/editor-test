using System;
using System.Windows;

namespace FamidashEditor
{
    public partial class PathfinderSettingsWindow : Window
    {
        /// <summary>
		/// Input timing bias: 0.0 = earliest viable action, 0.25 = early,
		/// 0.5 = neutral, 0.75 = late, 1.0 = latest viable action.
        /// </summary>
        public double JumpTimingBias { get; private set; } = 0.5;

        /// <summary>
        /// True if the user clicked Calculate Path (OK), false if cancelled.
        /// </summary>
        public bool Confirmed { get; private set; } = false;

        /// <summary>
        /// True if the user wants the pathfinder to prefer collecting coins.
        /// </summary>
        public bool PreferCoins { get; private set; } = true;

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
        /// True if the Pathfinder should write its diagnostic files to %TEMP%.
        /// </summary>
        public bool EnablePathfinderLogging { get; private set; } = true;

        /// <summary>
        /// True if generated Mesen replay scripts may write diagnostic files.
        /// </summary>
        public bool EnableMesenLogging { get; private set; } = true;

        public PathfinderSettingsWindow(
            bool enablePathfinderLogging = true,
            bool enableMesenLogging = true)
        {
            InitializeComponent();
            ChkPathfinderLogging.IsChecked = enablePathfinderLogging;
            ChkMesenLogging.IsChecked = enableMesenLogging;
            OptEarliest.Checked += (s, e) => UpdateLabel();
            OptEarly.Checked += (s, e) => UpdateLabel();
            OptMiddle.Checked += (s, e) => UpdateLabel();
            OptLate.Checked += (s, e) => UpdateLabel();
            OptLatest.Checked += (s, e) => UpdateLabel();
            UpdateLabel();
        }

		private void UpdateLabel()
		{
			const string fallback = " Applies to every game mode; unsafe timing " +
				"uses the closest surviving timing only for the necessary segment.";
			if (OptEarliest.IsChecked == true)
				BiasLabel.Text = "Prefer the earliest viable input action." + fallback;
			else if (OptEarly.IsChecked == true)
				BiasLabel.Text = "Prefer early input actions." + fallback;
			else if (OptMiddle.IsChecked == true)
				BiasLabel.Text = "Use neutral timing across every game mode.";
			else if (OptLate.IsChecked == true)
				BiasLabel.Text = "Prefer late input actions." + fallback;
			else if (OptLatest.IsChecked == true)
				BiasLabel.Text = "Prefer the latest viable input action." + fallback;
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
            EnablePathfinderLogging = (ChkPathfinderLogging.IsChecked == true);
            EnableMesenLogging = (ChkMesenLogging.IsChecked == true);

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
