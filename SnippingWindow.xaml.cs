using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WPFSoundVisualizationLib;

namespace Audio_Snipper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public partial class SnippingWindow : Window
    {
        private AudioEngine audioEngine;

        public SnippingWindow(AudioEngine audioEngine)
        {
			InitializeComponent();
			this.audioEngine = audioEngine;
			waveformTimeline.RegisterSoundPlayer(audioEngine);
        }

		protected override void OnKeyDown(KeyEventArgs e)
		{
            if (e.Key == Key.Escape)
                this.Exit(this, e);

			base.OnKeyDown(e);
		}

		private void Exit(object sender, RoutedEventArgs e)
        {
			audioEngine.Close();
			this.Hide();
        }

        private void Save(object sender, RoutedEventArgs e)
        {
			audioEngine.Save();
			this.Hide();
			audioEngine.Close();
		}

		private void Play(object sender, RoutedEventArgs e)
		{
			audioEngine.Play();
		}

		private void Pause(object sender, RoutedEventArgs e)
		{
			audioEngine.Pause();
		}
	}
}