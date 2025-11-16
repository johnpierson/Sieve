using System.Windows;
using Sieve.ViewModels;

namespace Sieve.Views
{
    /// <summary>
    /// Interaction logic for WatchlistWindow.xaml
    /// </summary>
    public partial class WatchlistWindow : Window
    {
        public WatchlistWindow()
        {
            InitializeComponent();
        }

        public WatchlistWindow(WatchlistWindowViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Save before closing
            if (DataContext is WatchlistWindowViewModel viewModel)
            {
                viewModel.SaveOnClose();
            }
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Auto-save on window close
            if (DataContext is WatchlistWindowViewModel viewModel)
            {
                viewModel.SaveOnClose();
            }
            base.OnClosing(e);
        }

        public Models.Watchlist? GetWatchlist()
        {
            if (DataContext is WatchlistWindowViewModel viewModel)
            {
                return viewModel.GetWatchlist();
            }
            return null;
        }
    }
}

