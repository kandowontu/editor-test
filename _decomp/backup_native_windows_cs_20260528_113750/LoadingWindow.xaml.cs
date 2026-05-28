using System.Windows;

namespace FamidashEditor
{
    public partial class LoadingWindow : Window
    {
        public LoadingWindow()
        {
            InitializeComponent();
        }

        public void SetMessage(string message)
        {
            if (MessageText != null)
            {
                MessageText.Text = message;
            }
        }
    }
}
