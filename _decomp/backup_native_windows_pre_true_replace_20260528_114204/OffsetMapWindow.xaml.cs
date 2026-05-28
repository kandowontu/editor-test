using System;
using System.Windows;

namespace FamidashEditor
{
    public partial class OffsetMapWindow : Window
    {
        public OffsetMapWindow()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (int.TryParse(ShiftXBox.Text.Trim(), out int dx) && int.TryParse(ShiftYBox.Text.Trim(), out int dy))
                {
                    // Call owner MainWindow to apply the offset
                    if (this.Owner is MainWindow mw)
                    {
                        mw.ApplyMapOffset(dx, dy);
                    }
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(this, "Please enter integer values for X and Y shifts.", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to apply offset: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
