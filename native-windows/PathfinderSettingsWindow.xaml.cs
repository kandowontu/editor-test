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
