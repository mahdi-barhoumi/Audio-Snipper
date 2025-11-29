using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Audio_Snipper
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>

	public partial class App : Application
	{
		[DllImport("user32.dll")]
		static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

		[DllImport("user32.dll")]
		static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		const int MOD_WIN = 0x0008;
		const int MOD_SHIFT = 0x0004;
		const int WM_HOTKEY = 0x0312;
		const int HOTKEY_ID = 9000;
		const uint VK_A = 0x41;

		private AudioEngine audioEngine;
		private RecordingWindow recordingWindow;
		private SnippingWindow snippingWindow;

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			audioEngine = new AudioEngine();
			recordingWindow = new RecordingWindow();
			snippingWindow = new SnippingWindow(audioEngine);

			RegisterHotKey(IntPtr.Zero, HOTKEY_ID, MOD_WIN | MOD_SHIFT, VK_A);
			ComponentDispatcher.ThreadFilterMessage += HotkeyFilterMessage;
		}

		protected override void OnExit(ExitEventArgs e)
		{
			UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
			ComponentDispatcher.ThreadFilterMessage -= HotkeyFilterMessage;

			base.OnExit(e);
		}

		private void HotkeyFilterMessage(ref MSG msg, ref bool handled)
		{
			if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HOTKEY_ID)
			{
				RecordOrStop();
				handled = true;
			}
		}

		private void RecordOrStop()
		{
			Trace.WriteLine("Hotkey pressed!");

			if (audioEngine.IsRecording)
			{
				audioEngine.StopRecording();
				recordingWindow.Hide();
				snippingWindow.Show();
			}
			else
			{
				audioEngine.StartRecording();
				snippingWindow.Hide();
				recordingWindow.Show();
			}
		}
	}
}