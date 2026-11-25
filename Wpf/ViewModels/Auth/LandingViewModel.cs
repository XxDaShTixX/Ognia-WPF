using Ognia.Wpf.Navigation;
using Ognia.Wpf.Services;

namespace Ognia.Wpf.ViewModels.Auth
{
    class LandingViewModel
    {
        private readonly ApiClientService _api;
        private readonly IAppNavigator _nav;

        public LandingViewModel(ApiClientService api, IAppNavigator nav)
        {
            // Initialize
            _api = api;
            _nav = nav;

            // Instantiate Commands
            GuestCommand = new RelayCommand(_ => _nav.EnterGuestMode());
            LoginCommand = new RelayCommand(_ => _nav.ShowLogin());
        }

        #region Commands

        /// <summary>
        /// Continues into the application without signing in.
        /// </summary>
        public RelayCommand GuestCommand { get; }

        /// <summary>
        /// Shows the login screen.
        /// </summary>
        public RelayCommand LoginCommand { get; }

        #endregion
    }
}
