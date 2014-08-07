﻿using KinectEx.DVR;
using KinectEx.Smoothing;
using Microsoft.Kinect;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KinectEx.RecorderDemo.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        KinectSensor _sensor = null;
        BodyFrameReader _bodyReader = null;
        ColorFrameReader _colorReader = null;
        DepthFrameReader _depthReader = null;

        ColorFrameBitmap _colorBitmap = new ColorFrameBitmap();
        DepthFrameBitmap _depthBitmap = new DepthFrameBitmap();

        KinectRecorder _recorder = null;

        List<CustomBody> _bodies = null;
        SmoothedBodyList<KalmanSmoother> _kalmanBodies = null;
        SmoothedBodyList<ExponentialSmoother> _exponentialBodies = null;

        public MainWindow()
        {
            InitializeComponent();

            RecordButton.Click += RecordButton_Click;

            CompressionCombo.Items.Add("None (1920x1080)");
            CompressionCombo.Items.Add("None (1280x720)");
            CompressionCombo.Items.Add("None (640x360)");
            CompressionCombo.Items.Add("Jpeg (1920x1080)");
            CompressionCombo.Items.Add("Jpeg (1280x720)");
            CompressionCombo.Items.Add("Jpeg (640x360)");
            CompressionCombo.SelectionChanged += CompressionCombo_SelectionChanged;
            CompressionCombo.SelectedIndex = 0;

            SmoothingCombo.Items.Add("None");
            SmoothingCombo.Items.Add("Kalman Filter");
            SmoothingCombo.Items.Add("Double Exponential");
            SmoothingCombo.SelectionChanged += SmoothingCombo_SelectionChanged;
            SmoothingCombo.SelectedIndex = 0;

            DisplayCombo.Items.Add("Body");
            DisplayCombo.Items.Add("Color");
            DisplayCombo.Items.Add("Depth");
            DisplayCombo.SelectionChanged += DisplayCombo_SelectionChanged;
            DisplayCombo.SelectedIndex = 0;

            _sensor = KinectSensor.GetDefault();

            _bodyReader = _sensor.BodyFrameSource.OpenReader();
            _bodyReader.FrameArrived += _bodyReader_FrameArrived;

            _colorReader = _sensor.ColorFrameSource.OpenReader();
            _colorReader.FrameArrived += _colorReader_FrameArrived;

            _depthReader = _sensor.DepthFrameSource.OpenReader();
            _depthReader.FrameArrived += _depthReader_FrameArrived;

            _sensor.Open();
            OutputImage.Source = _colorBitmap.Bitmap;
        }

        void _bodyReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            IEnumerable<IBody> bodies = null; // to make the GetBitmap call a little cleaner
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame != null)
                {
                    if (SmoothingCombo.SelectedIndex == 0)
                    {
                        frame.GetAndRefreshBodyData(_bodies);
                        bodies = _bodies;
                    }
                    else if (SmoothingCombo.SelectedIndex == 1)
                    {
                        frame.GetAndRefreshBodyData(_kalmanBodies);
                        bodies = _kalmanBodies;
                    }
                    else
                    {
                        frame.GetAndRefreshBodyData(_exponentialBodies);
                        bodies = _exponentialBodies;
                    }
                }
            }

            if (DisplayCombo.SelectedIndex == 0)
            {
                if (bodies != null)
                    OutputImage.Source = bodies.GetBitmap(Colors.LightGreen, Colors.Yellow);
                else
                    OutputImage.Source = null;
            }
        }

        void _colorReader_FrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            if (DisplayCombo.SelectedIndex == 1)
            {
                _colorBitmap.Update(e.FrameReference);
            }
        }

        private void _depthReader_FrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            if (DisplayCombo.SelectedIndex == 2)
            {
                _depthBitmap.Update(e.FrameReference);
            }
        }

        async void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_recorder == null)
            {
                var dlg = new SaveFileDialog()
                {
                    FileName = DateTime.Now.ToString("MM-dd-yyyy-hh-mm-ss"),
                    DefaultExt = ".kdvr",
                    Filter = "KinectEx.DVR Files (*.kdvr)|*.kdvr"
                };

                if (dlg.ShowDialog().GetValueOrDefault())
                {
                    _recorder = new KinectRecorder(File.Open(dlg.FileName, FileMode.Create), _sensor);
                    _recorder.EnableBodyRecorder = BodyCheckBox.IsChecked.GetValueOrDefault();
                    _recorder.EnableColorRecorder = ColorCheckBox.IsChecked.GetValueOrDefault();
                    _recorder.EnableDepthRecorder = DepthCheckBox.IsChecked.GetValueOrDefault();

                    // NOTE : Default ColorRecorderCodec is Raw @ 1920 x 1080. Only need to change the
                    //        bits that differ from the default.

                    if (CompressionCombo.SelectedIndex > 2)
                    {
                        _recorder.ColorRecorderCodec = new JpegColorCodec();
                    }
                    if (CompressionCombo.SelectedIndex % 3 == 1) // 1280 x 720
                    {
                        _recorder.ColorRecorderCodec.OutputWidth = 1280;
                        _recorder.ColorRecorderCodec.OutputHeight = 720;
                    }
                    else if (CompressionCombo.SelectedIndex % 3 == 2) // 640 x 360
                    {
                        _recorder.ColorRecorderCodec.OutputWidth = 640;
                        _recorder.ColorRecorderCodec.OutputHeight = 360;
                    }

                    _recorder.Start();

                    RecordButton.Content = "Stop Recording";
                    BodyCheckBox.IsEnabled = false;
                    ColorCheckBox.IsEnabled = false;
                    DepthCheckBox.IsEnabled = false;
                    CompressionCombo.IsEnabled = false;
                }
            }
            else
            {
                RecordButton.IsEnabled = false;

                await _recorder.StopAsync();
                _recorder = null;

                RecordButton.Content = "Record";
                RecordButton.IsEnabled = true;
                BodyCheckBox.IsEnabled = true;
                ColorCheckBox.IsEnabled = true;
                DepthCheckBox.IsEnabled = true;
                CompressionCombo.IsEnabled = true;
            }
        }

        void CompressionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        void SmoothingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _bodies = new List<CustomBody>();
            _kalmanBodies = new SmoothedBodyList<KalmanSmoother>();
            _exponentialBodies = new SmoothedBodyList<ExponentialSmoother>();
        }

        void DisplayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DisplayCombo.SelectedIndex == 0)
            {
                OutputImage.Source = null;
            }
            else if (DisplayCombo.SelectedIndex == 1)
            {
                OutputImage.Source = _colorBitmap.Bitmap;
            }
            else
            {
                OutputImage.Source = _depthBitmap.Bitmap;
            }
        }
    }
}
