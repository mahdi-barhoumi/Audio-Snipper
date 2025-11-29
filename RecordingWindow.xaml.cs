using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Audio_Snipper
{
	/// <summary>
	/// Interaction logic for Window1.xaml
	/// </summary>

	public partial class RecordingWindow : Window
    {
		private DispatcherTimer timer;
		private int dotCount = 0;

		public RecordingWindow()
        {
            InitializeComponent();

			timer = new DispatcherTimer();
			timer.Interval = TimeSpan.FromMilliseconds(1000); // adjust speed
			timer.Tick += Timer_Tick;
			timer.Start();
		}

		private void Timer_Tick(object sender, EventArgs e)
		{
			dotCount = (dotCount + 1) % 4; // cycles through 0,1,2,3
			string dots = new string('.', dotCount);
			recordingLabel.Content = $"● Recording{dots}";
		}
	}
}
