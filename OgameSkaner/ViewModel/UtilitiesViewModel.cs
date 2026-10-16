using OgameSkaner.RestClient;
using OgameSkaner.Utils;
using Prism.Commands;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace OgameSkaner.ViewModel
{
    public class UtilitiesViewModel : NotifyPropertyChanged
    {
        public UtilitiesViewModel(IGameRestClient gameRestClient)
        {
            _gameRestClient = gameRestClient;
            EscapeFleetToMoonCommand = new DelegateCommand(async () => { await EscapeFlootToMoon(); }, CanExecute);
            Logs = new ObservableCollection<string>();
        }

        public ObservableCollection<string> Logs
        {
            set
            {
                _logs = value;
                RaisePropertyChanged("Logs");
            }
            get => _logs;
        }

        #region CanExecute

        private bool CanExecute()
        {
            return true;
        }

        #endregion

        #region fields
        private IGameRestClient _gameRestClient;

        private ObservableCollection<string> _logs;

        #endregion

        #region PrivateMethods
        private async Task EscapeFlootToMoon()
        {
            AddLog($"Escape Started");
            var response = await _gameRestClient.GetFleet();
            if (response.Ships.Count < 1)
            {
                AddLog($"No ships to escape");
                return;
            }
            try
            {
                await _gameRestClient.EscapeFleet(response.TargetX, response.TargetY, response.TargetZ, true, response.Ships);
                AddLog($"Fleet escaped");
            }
            catch
            {
                AddLog($"Fleet failed to escape");
            }
        }

        private void AddLog(string log)
        {
            Logs.Add(log);
            RaisePropertyChanged("Logs");
        }


        #endregion

        #region Commands

        public DelegateCommand EscapeFleetToMoonCommand { set; get; }

        #endregion
    }
}