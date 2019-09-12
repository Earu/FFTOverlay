using NAudio.Wave;
using System.Windows;
using Accord.Math;
using System.Numerics;
using System.Windows.Shapes;
using System.Windows.Media;
using System;
using System.Diagnostics;
using System.Threading;
using FFTOverlay.Helpers;

namespace FFTOverlay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //private const int Rate = 44100; // sample rate of the sound card
        private const int BufferSize = 8192; // must be a multiple of 2
        private const int Points = 256;

        private readonly BufferedWaveProvider Provider;
        private readonly WasapiLoopbackCapture CaptureInstance;
        private readonly Timer UpdateTimer;

        public MainWindow()
        {
            this.CaptureInstance = new WasapiLoopbackCapture();
            this.CaptureInstance.DataAvailable += (_, e) => this.Provider.AddSamples(e.Buffer, 0, e.BytesRecorded);

            this.Provider = new BufferedWaveProvider(this.CaptureInstance.WaveFormat)
            {
                BufferLength = BufferSize * 2,
                DiscardOnBufferOverflow = true
            };

            this.InitializeComponent();
            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Height = 400;
            this.Left = 0;
            this.Top = SystemParameters.PrimaryScreenHeight - 400;
            this.Topmost = true;

            this.InitializeRectangles();
            this.CaptureInstance.StartRecording();

            this.UpdateTimer = new Timer(_ => Application.Current.Dispatcher.Invoke(() => this.OnTimer()));
            this.UpdateTimer.Change(0, 5);

            AppDomain.CurrentDomain.UnhandledException += this.OnUnhandledException;
        }

        private void OnUnhandledException(object _, UnhandledExceptionEventArgs e) 
            => Debug.WriteLine(e.ExceptionObject);

        private void InitializeRectangles()
        {
            double rectWidth = (this.Width / Points) - 2.0;
            double colPoint = 1.0 / Points;
            for (double i = 0.0; i < Points; i++)
            {
                ColorRGB rgb = ColorHelper.HSL2RGB(colPoint * i, 0.5, 0.5);
                SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(255, rgb.R, rgb.G, rgb.B));
                TranslateTransform tt = new TranslateTransform((rectWidth + 2.0) * i, this.Height);
                Rectangle rect = new Rectangle
                {
                    Width = rectWidth,
                    Height = 0,
                    Fill = brush,
                    RenderTransform = tt,
                };

                this.Canvas.Children.Add(rect);
            }
        }

        private double[] FFT(double[] data)
        {
            double[] fft = new double[data.Length];
            Complex[] fftComplex = new Complex[data.Length];
            for (int i = 0; i < data.Length; i++)
                fftComplex[i] = new Complex(data[i], 0.0);
            FourierTransform.FFT(fftComplex, FourierTransform.Direction.Forward);
            for (int i = 0; i < data.Length; i++)
                fft[i] = fftComplex[i].Magnitude;
            return fft;
        }

        private int ZeroBytesCount = 0;
        private void OnTimer() 
        { 
            int frameSize = BufferSize;
            byte[] audioBytes = new byte[frameSize];
            this.Provider.Read(audioBytes, 0, frameSize);

            if (audioBytes.Length == 0) return;
            if (audioBytes[frameSize - 2] == 0 && this.ZeroBytesCount <= 10)
            {
                this.ZeroBytesCount++;
                return;
            }

            this.ZeroBytesCount = 0;

            // incoming data is 16-bit (2 bytes per audio point)
            int bytesPerPoint = 2;
            int graphPointCount = audioBytes.Length / bytesPerPoint;

            double[] pcm = new double[graphPointCount];
            double[] fftReal = new double[graphPointCount / 2];

            for (int i = 0; i < graphPointCount; i++)
            {
                // read the int16 from the two bytes
                short val = BitConverter.ToInt16(audioBytes, i * 2);

                // store the value in Ys as a percent (+/- 100% = 200%)
                pcm[i] = val / Math.Pow(2, 16) * 200.0;
            }

            double[] fft = this.FFT(pcm);
            // just keep the real half (the other half imaginary)
            Array.Copy(fft, fftReal, fftReal.Length);
            fftReal.Sort();
            fftReal = fftReal.Reversed();

            double offset = 5.0;
            for (int i = 0; i < Points / 2; i++)
            {
                Rectangle left = (Rectangle)this.Canvas.Children[i];
                Rectangle right = (Rectangle)this.Canvas.Children[Points - i - 1];

                double height = Math.Log10(fftReal[i]) * 200.0;
                double leftX = left.RenderTransform.Value.OffsetX;
                double rightX = right.RenderTransform.Value.OffsetX;

                if (height > left.Height)
                {
                    double actualHeight = Math.Min(left.Height + offset, height);
                    left.Height = actualHeight;
                    right.Height = actualHeight;
                } 
                else if (height < left.Height)
                {
                    double actualHeight = height <= 0 ? 0 : Math.Max(left.Height - offset, height);
                    left.Height = actualHeight;
                    right.Height = actualHeight;
                }

                TranslateTransform leftTt = new TranslateTransform(leftX, this.Height - left.Height);
                TranslateTransform rightTt = new TranslateTransform(rightX, this.Height - right.Height);
                left.RenderTransform = leftTt;
                right.RenderTransform = rightTt;
            }
        }
    }
}
