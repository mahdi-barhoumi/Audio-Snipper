using System.IO;
using System.ComponentModel;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Utils;
using WPFSoundVisualizationLib;

namespace Audio_Snipper
{
	public class AudioEngine : IDisposable, INotifyPropertyChanged, IWaveformPlayer
	{
		public event PropertyChangedEventHandler PropertyChanged;

		private readonly DispatcherTimer positionTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);

		private const int fftDataSize = (int) FFTDataSize.FFT2048;
		private const int waveformCompressedPointCount = 2000;
		private const int repeatThreshold = 200;

		private bool disposed;
		private bool isPlaying;
		private bool isRecording;
		private bool inRepeatSet;
		private bool inChannelSet;
		private bool inChannelTimerUpdate;
		private double channelLength;
		private double channelPosition;
		private float[] waveformData;
		private WaveOut waveOutDevice;
		private WaveFileWriter writer;
		private WasapiLoopbackCapture capture;
		private WaveStream activeStream;
		private WaveChannel32 inputStream;
		private IgnoreDisposeStream recordStream;
		private SampleAggregator sampleAggregator;
		private SampleAggregator waveformAggregator;
		private TimeSpan repeatStart;
		private TimeSpan repeatStop;

		public AudioEngine()
		{
			positionTimer.Interval = TimeSpan.FromMilliseconds(25);
			positionTimer.Tick += positionTimer_Tick;
			positionTimer.Stop();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					Close();
				}
				disposed = true;
			}
		}

		private void NotifyPropertyChanged(String info)
		{
			if (PropertyChanged != null)
			{
				PropertyChanged(this, new PropertyChangedEventArgs(info));
			}
		}

		public bool IsRecording
		{
			get { return isRecording; }
			protected set
			{
				bool oldValue = isRecording;
				isRecording = value;
				if (oldValue != isRecording)
					NotifyPropertyChanged("isRecording");
			}
		}

		public bool IsPlaying
		{
			get { return isPlaying; }
			protected set
			{
				bool oldValue = isPlaying;
				isPlaying = value;
				if (oldValue != isPlaying)
					NotifyPropertyChanged("IsPlaying");
				positionTimer.IsEnabled = value;
			}
		}

		public TimeSpan SelectionBegin
		{
			get { return repeatStart; }
			set
			{
				if (!inRepeatSet)
				{
					inRepeatSet = true;
					TimeSpan oldValue = repeatStart;
					repeatStart = value;
					if (oldValue != repeatStart)
						NotifyPropertyChanged("SelectionBegin");
					inRepeatSet = false;
				}
			}
		}

		public TimeSpan SelectionEnd
		{
			get { return repeatStop; }
			set
			{
				if (!inChannelSet)
				{
					inRepeatSet = true;
					TimeSpan oldValue = repeatStop;
					repeatStop = value;
					if (oldValue != repeatStop)
						NotifyPropertyChanged("SelectionEnd");
					inRepeatSet = false;
				}
			}
		}

		public float[] WaveformData
		{
			get { return waveformData; }
			protected set
			{
				float[] oldValue = waveformData;
				waveformData = value;
				if (oldValue != waveformData)
					NotifyPropertyChanged("WaveformData");
			}
		}

		public double ChannelLength
		{
			get { return channelLength; }
			protected set
			{
				double oldValue = channelLength;
				channelLength = value;
				if (oldValue != channelLength)
					NotifyPropertyChanged("ChannelLength");
			}
		}

		public double ChannelPosition
		{
			get { return channelPosition; }
			set
			{
				if (!inChannelSet)
				{
					inChannelSet = true; // Avoid recursion
					double oldValue = channelPosition;
					double position = Math.Max(0, Math.Min(value, ChannelLength));
					if (!inChannelTimerUpdate && activeStream != null)
						activeStream.Position = (long) ((position / activeStream.TotalTime.TotalSeconds) * activeStream.Length);
					channelPosition = position;
					if (oldValue != channelPosition)
						NotifyPropertyChanged("ChannelPosition");
					inChannelSet = false;
				}
			}
		}

		private void positionTimer_Tick(object sender, EventArgs e)
		{
			inChannelTimerUpdate = true;
			ChannelPosition = ((double) activeStream.Position / (double) activeStream.Length) * activeStream.TotalTime.TotalSeconds;
			inChannelTimerUpdate = false;
		}

		private void inputStream_Sample(object sender, SampleEventArgs e)
		{
			sampleAggregator.Add(e.Left, e.Right);
			long repeatStartPosition = (long)((SelectionBegin.TotalSeconds / activeStream.TotalTime.TotalSeconds) * activeStream.Length);
			long repeatStopPosition = (long)((SelectionEnd.TotalSeconds / activeStream.TotalTime.TotalSeconds) * activeStream.Length);
			if (((SelectionEnd - SelectionBegin) >= TimeSpan.FromMilliseconds(repeatThreshold)) && activeStream.Position >= repeatStopPosition)
			{
				sampleAggregator.Clear();
				activeStream.Position = repeatStartPosition;
			}
		}

		private void waveStream_Sample(object sender, SampleEventArgs e)
		{
			waveformAggregator.Add(e.Left, e.Right);
		}

		private void GenerateWaveformData()
		{
			recordStream.Position = 0;
			WaveFileReader waveformStream = new WaveFileReader(recordStream);
			WaveChannel32 waveformInputStream = new WaveChannel32(waveformStream);
			waveformInputStream.Sample += waveStream_Sample;

			int frameLength = fftDataSize;
			int frameCount = (int)((double)waveformInputStream.Length / (double)frameLength);
			int waveformLength = frameCount * 2;
			byte[] readBuffer = new byte[frameLength];
			waveformAggregator = new SampleAggregator(frameLength);

			float maxLeftPointLevel = float.MinValue;
			float maxRightPointLevel = float.MinValue;
			int currentPointIndex = 0;
			float[] waveformCompressedPoints = new float[waveformCompressedPointCount];
			List<float> waveformData = new List<float>();
			List<int> waveMaxPointIndexes = new List<int>();

			for (int i = 1; i <= waveformCompressedPointCount; i++)
			{
				waveMaxPointIndexes.Add((int)Math.Round(waveformLength * ((double)i / (double)waveformCompressedPointCount), 0));
			}
			int readCount = 0;
			while (currentPointIndex * 2 < waveformCompressedPointCount)
			{
				waveformInputStream.Read(readBuffer, 0, readBuffer.Length);

				waveformData.Add(waveformAggregator.LeftMaxVolume);
				waveformData.Add(waveformAggregator.RightMaxVolume);

				if (waveformAggregator.LeftMaxVolume > maxLeftPointLevel)
					maxLeftPointLevel = waveformAggregator.LeftMaxVolume;
				if (waveformAggregator.RightMaxVolume > maxRightPointLevel)
					maxRightPointLevel = waveformAggregator.RightMaxVolume;

				if (readCount > waveMaxPointIndexes[currentPointIndex])
				{
					waveformCompressedPoints[(currentPointIndex * 2)] = maxLeftPointLevel;
					waveformCompressedPoints[(currentPointIndex * 2) + 1] = maxRightPointLevel;
					maxLeftPointLevel = float.MinValue;
					maxRightPointLevel = float.MinValue;
					currentPointIndex++;
				}
				if (readCount % 3000 == 0)
				{
					float[] clonedData = (float[])waveformCompressedPoints.Clone();
					App.Current.Dispatcher.Invoke(new Action(() => { WaveformData = clonedData; }));
				}
				readCount++;
			}

			float[] finalClonedData = (float[])waveformCompressedPoints.Clone();
			App.Current.Dispatcher.Invoke(new Action(() => { WaveformData = finalClonedData; }));
			waveformInputStream.Close();
			waveformInputStream.Dispose();
			waveformInputStream = null;
			waveformStream.Close();
			waveformStream.Dispose();
			waveformStream = null;
		}

		public void StartRecording()
		{
			recordStream = new IgnoreDisposeStream(new MemoryStream());
			capture = new WasapiLoopbackCapture();
			writer = new WaveFileWriter(recordStream, capture.WaveFormat);

			capture.DataAvailable += (s, e) =>
			{
				writer.Write(e.Buffer, 0, e.BytesRecorded);
			};
			capture.StartRecording();

			IsRecording = true;
		}

		public void StopRecording()
		{
			if (!isRecording) return;

			capture.StopRecording();
			capture.Dispose();
			writer.Dispose();

			GenerateWaveformData();

			recordStream.Position = 0;
			activeStream = new WaveFileReader(recordStream);
			inputStream = new WaveChannel32(activeStream);
			sampleAggregator = new SampleAggregator(fftDataSize);
			inputStream.Sample += inputStream_Sample;
			waveOutDevice = new WaveOut() { DesiredLatency = 75 };
			waveOutDevice.Init(inputStream);

			ChannelPosition = 0;
			ChannelLength = inputStream.TotalTime.TotalSeconds;

			IsRecording = false;
		}

		public void Play()
		{
			if (waveOutDevice == null)
			{
				return;
			}
			positionTimer.Start();
			waveOutDevice.Play();
			IsPlaying = true;
		}

		public void Save()
		{
			if (activeStream == null || recordStream == null)
			{
				return;
			}

			// Create SaveFileDialog
			Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
			saveFileDialog.Filter = "Wave File (*.wav)|*.wav";
			saveFileDialog.DefaultExt = ".wav";
			saveFileDialog.FileName = "audio";
			saveFileDialog.Title = "Save audio snippet";

			bool? result = saveFileDialog.ShowDialog();

			if (result == true)
			{
				string outputPath = saveFileDialog.FileName;

				// Calculate positions based on selection
				long startPosition = (long)((SelectionBegin.TotalSeconds / activeStream.TotalTime.TotalSeconds) * activeStream.Length);
				long endPosition = (long)((SelectionEnd.TotalSeconds / activeStream.TotalTime.TotalSeconds) * activeStream.Length);

				// If no selection is made, save the entire audio
				if (SelectionEnd <= SelectionBegin)
				{
					startPosition = 0;
					endPosition = activeStream.Length;
				}

				// Ensure positions are aligned to block boundaries
				int blockAlign = activeStream.WaveFormat.BlockAlign;
				startPosition -= startPosition % blockAlign;
				endPosition -= endPosition % blockAlign;

				long bytesToRead = endPosition - startPosition;

				// Create a temporary stream for reading
				recordStream.Position = 0;
				WaveFileReader reader = new WaveFileReader(recordStream);

				// Position the reader at the start of selection
				reader.Position = startPosition;

				// Create output wave file writer
				using (WaveFileWriter writer = new WaveFileWriter(outputPath, reader.WaveFormat))
				{
					byte[] buffer = new byte[reader.WaveFormat.AverageBytesPerSecond];
					long bytesRead = 0;

					while (bytesRead < bytesToRead)
					{
						int bytesToReadNow = (int)Math.Min(buffer.Length, bytesToRead - bytesRead);
						int read = reader.Read(buffer, 0, bytesToReadNow);

						if (read == 0)
							break;

						writer.Write(buffer, 0, read);
						bytesRead += read;
					}
				}

				reader.Dispose();
			}
		}

		public void Pause()
		{
			if (waveOutDevice != null)
			{
				waveOutDevice.Pause();
			}
			positionTimer.Stop();
			IsPlaying = false;
		}

		public void Close()
		{
			positionTimer.Stop();
			if (waveOutDevice != null)
			{
				waveOutDevice.Stop();
			}
			if (activeStream != null)
			{
				inputStream.Close();
				inputStream = null;
				activeStream.Close();
				activeStream = null;
				recordStream.Close();
				recordStream = null;
			}
			if (waveOutDevice != null)
			{
				waveOutDevice.Dispose();
				waveOutDevice = null;
			}
		}
	}
}
