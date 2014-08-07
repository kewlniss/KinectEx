﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

#if NETFX_CORE
using WindowsPreview.Kinect;
#else
using Microsoft.Kinect;
#endif

namespace KinectEx.DVR
{
    public class KinectRecorder
    {
        BinaryWriter _writer;
        KinectSensor _sensor;

        ColorRecorder _colorRecorder;
        DepthRecorder _depthRecorder;
        BodyRecorder _bodyRecorder;

        ColorFrameReader _colorReader;
        DepthFrameReader _depthReader;
        BodyFrameReader _bodyReader;

        bool _isStarted = false;
        bool _isStopped = false;
        Task _processFramesTask = null;

        DateTime _previousFlushTime;

        private bool _enableColorRecorder;
        private bool _enableDepthRecorder;
        private bool _enableBodyRecorder;

        public bool IsStarted
        {
            get { return _isStarted; }
        }

        /// <summary>
        /// Determines whether the KinectRecorder will record Color frames. Applies only when the
        /// KinectRecorder is in "Automatic" mode. Cannot be changed after recording has started.
        /// </summary>
        public bool EnableColorRecorder
        {
            get { return _enableColorRecorder; }
            set
            {
                if (_isStarted)
                    throw new InvalidOperationException("Cannot modify recorder properties after recording has started");

                _enableColorRecorder = value;
            }
        }

        /// <summary>
        /// Determines whether the KinectRecorder will record Depth frames. Applies only when the
        /// KinectRecorder is in "Automatic" mode. Cannot be changed after recording has started.
        /// </summary>
        public bool EnableDepthRecorder
        {
            get { return _enableDepthRecorder; }
            set
            {
                if (_isStarted)
                    throw new InvalidOperationException("Cannot modify recorder properties after recording has started");

                _enableDepthRecorder = value;
            }
        }

        /// <summary>
        /// Determines whether the KinectRecorder will record Body frames. Applies only when the
        /// KinectRecorder is in "Automatic" mode. Cannot be changed after recording has started.
        /// </summary>
        public bool EnableBodyRecorder
        {
            get { return _enableBodyRecorder; }
            set
            {
                if (_isStarted)
                    throw new InvalidOperationException("Cannot modify recorder properties after recording has started");

                _enableBodyRecorder = value;
            }
        }

        /// <summary>
        /// The codec used to encode Color frame images. By default, this is a <c>RawColorCodec</c>
        /// that records at full resolution (i.e., 1920 x 1080 x 4 bits/pixel). Cannot be changed
        /// after recording has started.
        /// </summary>
        public IColorCodec ColorRecorderCodec
        {
            get { return _colorRecorder.Codec; }
            set
            {
                if (value == null)
                    throw new NullReferenceException("Cannot set ColorRecorderCodec to null");

                if (_isStarted)
                    throw new InvalidOperationException("Cannot modify recorder properties after recording has started");

                _colorRecorder.Codec = value;
            }
        }
        

        // Simple linked list to serve as a stack for queing frames
        // for encode and save.
        Object _queueMonitorObject = new Object();
        ReplayFrame _firstFrame = null;
        ReplayFrame _lastFrame = null;
        int _queueSize = 0;

        private void EnqueFrame(ReplayFrame replayFrame)
        {
            Monitor.Enter(_queueMonitorObject);
            if (_firstFrame == null)
                _firstFrame = _lastFrame = replayFrame;
            else
                _lastFrame.NextFrame = replayFrame;
            _lastFrame.NextFrame = replayFrame;
            _lastFrame = replayFrame;
            _queueSize++;
            Monitor.Exit(_queueMonitorObject);
        }

        private ReplayFrame DequeFrame()
        {
            Monitor.Enter(_queueMonitorObject);
            ReplayFrame frame = _firstFrame;
            if (frame.NextFrame == null)
                _firstFrame = _lastFrame = null;
            else
                _firstFrame = frame.NextFrame;
            frame.NextFrame = null;
            _queueSize--;
            Monitor.Exit(_queueMonitorObject);
            return frame;
        }

        /// <summary>
        /// <para>
        ///     Creates a new instance of a KinectRecorder which can save frames to the
        ///     referenced stream.
        /// </para>
        /// <para>
        ///     The KinectRecorder can operate in two distinct modes. The "Automatic" mode
        ///     requires only that you pass a valid <c>KinectSensor</c> object to this
        ///     constructor. Recording of frames for each enabled frame type happens automatically
        ///     between the time Start() and StopAsync() are called.
        /// </para>
        /// <para>
        ///     In certain situations, the developer may wish to have more precise control
        ///     over when and how frames are recorded. If no <c>KinectSensor</c> is passed in
        ///     to this constructor, Start() and StopAsync() must still be called to begin and
        ///     end the recording session. However, the KinectRecorder will be in "Manual" mode,
        ///     and frames are recorded only when passed in to the RecordFrame() method.
        /// </para>
        /// </summary>
        /// <param name="stream">
        ///     The stream to which frames will be stored.
        /// </param>
        /// <param name="sensor">
        ///     If supplied, the <c>KinectSensor</c> from which frames will be automatically
        ///     retrieved for recording.
        /// </param>
        public KinectRecorder(Stream stream, KinectSensor sensor = null)
        {
            _writer = new BinaryWriter(stream);
            _sensor = sensor;
            _colorRecorder = new ColorRecorder(_writer);
            _depthRecorder = new DepthRecorder(_writer);
            _bodyRecorder = new BodyRecorder(_writer);
        }

        /// <summary>
        /// Start the KinectRecorder session. This will write the file header and
        /// 
        /// </summary>
        public void Start()
        {
            if (_isStarted)
                return;

            if (_isStopped)
                throw new InvalidOperationException("Cannot restart a recording after it has been stopped");

            if (_sensor != null)
            {
                if (EnableColorRecorder)
                {
                    _colorReader = _sensor.ColorFrameSource.OpenReader();
                    _colorReader.FrameArrived += _colorReader_FrameArrived;
                }

                if (EnableDepthRecorder)
                {
                    _depthReader = _sensor.DepthFrameSource.OpenReader();
                    _depthReader.FrameArrived += _depthReader_FrameArrived;
                }

                if (EnableBodyRecorder)
                {
                    _bodyReader = _sensor.BodyFrameSource.OpenReader();
                    _bodyReader.FrameArrived += _bodyReader_FrameArrived;
                }

                if (!_sensor.IsOpen)
                    _sensor.Open();

            }

            _isStarted = true;

            // initialize and write file metadata
            var metadata = new FileMetadata()
            {
                Version = this.GetType().GetTypeInfo().Assembly.GetName().Version.ToString(),
                ColorCodecId = this.ColorRecorderCodec.CodecId,
                DepthFrameToCameraSpaceTable = _sensor.CoordinateMapper.GetDepthFrameToCameraSpaceTable()
            };
            _writer.Write(JsonConvert.SerializeObject(metadata));

            _processFramesTask = ProcessFramesAsync();
        }

        public async Task StopAsync()
        {
            if (_isStopped)
                return;

            System.Diagnostics.Debug.WriteLine(">>> StopAsync (queue size {0})", _queueSize);

            _isStarted = false;
            _isStopped = true;

            if (_colorReader != null)
            {
                _colorReader.FrameArrived -= _colorReader_FrameArrived;
                _colorReader.Dispose();
                _colorReader = null;
            }

            if (_depthReader != null)
            {
                _depthReader.FrameArrived -= _depthReader_FrameArrived;
                _depthReader.Dispose();
                _depthReader = null;
            }

            if (_bodyReader != null)
            {
                _bodyReader.FrameArrived -= _bodyReader_FrameArrived;
                _bodyReader.Dispose();
                _bodyReader = null;
            }

            await Task.Run(async () =>
            {
                try
                {
                    await _processFramesTask;

                    if (_writer != null)
                    {
                        _writer.Flush();

                        if (_writer.BaseStream != null)
                        {
                            _writer.BaseStream.Flush();
                        }

                        _writer.Dispose();
                        _writer = null;
                    }
                }
                catch (Exception ex)
                {
                    // TODO: Change to log the error
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            });
            System.Diagnostics.Debug.WriteLine("<<< StopAsync (DONE!)");
        }

        public void RecordFrame(ColorFrame frame)
        {
            if (!_isStarted)
                throw new InvalidOperationException("Cannot record frames unless the KinectRecorder is started.");

            if (frame != null)
            {
                EnqueFrame(new ReplayColorFrame(frame));
                System.Diagnostics.Debug.WriteLine("+++ Enqueued Color Frame ({0})", _queueSize);
            }
        }

        public void RecordFrame(ColorFrame frame, byte[] bytes)
        {
            if (!_isStarted)
                throw new InvalidOperationException("Cannot record frames unless the KinectRecorder is started.");

            if (frame != null)
            {
                EnqueFrame(new ReplayColorFrame(frame, bytes));
                System.Diagnostics.Debug.WriteLine("+++ Enqueued Color Frame ({0})", _queueSize);
            }
        }

        public void RecordFrame(DepthFrame frame)
        {
            if (!_isStarted)
                throw new InvalidOperationException("Cannot record frames unless the KinectRecorder is started.");

            if (frame != null)
            {
                EnqueFrame(new ReplayDepthFrame(frame));
                System.Diagnostics.Debug.WriteLine("+++ Enqueued Depth Frame ({0})", _queueSize);
            }
        }

        public void RecordFrame(DepthFrame frame, ushort[] frameData)
        {
            if (!_isStarted)
                throw new InvalidOperationException("Cannot record frames unless the KinectRecorder is started.");

            if (frame != null)
            {
                EnqueFrame(new ReplayDepthFrame(frame, frameData));
                System.Diagnostics.Debug.WriteLine("+++ Enqueued Depth Frame ({0})", _queueSize);
            }
        }

        public void RecordFrame(BodyFrame frame)
        {
            if (!_isStarted)
                throw new InvalidOperationException("Cannot record frames unless the KinectRecorder is started.");

            if (frame != null)
            {
                EnqueFrame(new ReplayBodyFrame(frame));
                System.Diagnostics.Debug.WriteLine("+++ Enqueued Body Frame ({0})", _queueSize);
            }
        }

        public void RecordFrame(BodyFrame frame, List<CustomBody> bodies)
        {
            if (!_isStarted)
                throw new InvalidOperationException("Cannot record frames unless the KinectRecorder is started.");

            if (frame != null)
            {
                EnqueFrame(new ReplayBodyFrame(frame, bodies));
                System.Diagnostics.Debug.WriteLine("+++ Enqueued Body Frame ({0})", _queueSize);
            }
        }

#if NETFX_CORE
        void _colorReader_FrameArrived(ColorFrameReader sender, ColorFrameArrivedEventArgs args)
#else
        void _colorReader_FrameArrived(object sender, ColorFrameArrivedEventArgs args)
#endif
        {
            using (var frame = args.FrameReference.AcquireFrame())
            {
                RecordFrame(frame);
            }
        }

#if NETFX_CORE
        void _depthReader_FrameArrived(DepthFrameReader sender, DepthFrameArrivedEventArgs args)
#else
        void _depthReader_FrameArrived(object sender, DepthFrameArrivedEventArgs args)
#endif
        {
            using (var frame = args.FrameReference.AcquireFrame())
            {
                RecordFrame(frame);
            }
        }

#if NETFX_CORE
        void _bodyReader_FrameArrived(BodyFrameReader sender, BodyFrameArrivedEventArgs args)
#else
        void _bodyReader_FrameArrived(object sender, BodyFrameArrivedEventArgs args)
#endif
        {
            using (var frame = args.FrameReference.AcquireFrame())
            {
                RecordFrame(frame);
            }
        }

        private async Task ProcessFramesAsync()
        {
            _previousFlushTime = DateTime.Now;
            await Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        if (_queueSize > 0)
                        {
                            var frame = DequeFrame();

                            if (frame is ReplayColorFrame)
                            {
                                await _colorRecorder.RecordAsync((ReplayColorFrame)frame);
                                System.Diagnostics.Debug.WriteLine("--- Processed Color Frame ({0})", _queueSize);
                            }
                            else if (frame is ReplayDepthFrame)
                            {
                                await _depthRecorder.RecordAsync((ReplayDepthFrame)frame);
                                System.Diagnostics.Debug.WriteLine("--- Processed Depth Frame ({0})", _queueSize);
                            }
                            else if (frame is ReplayBodyFrame)
                            {
                                await _bodyRecorder.RecordAsync((ReplayBodyFrame)frame);
                                System.Diagnostics.Debug.WriteLine("--- Processed Body Frame ({0})", _queueSize);
                            }
                            Flush();
                        }
                        else
                        {
                            await Task.Delay(100);
                            if (_queueSize == 0 && _isStarted == false)
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // TODO: Change to log the error
                        System.Diagnostics.Debug.WriteLine(ex);
                    }
                }
            }).ConfigureAwait(false);
        }

        private void Flush()
        {
            var now = DateTime.Now;

            if (now.Subtract(_previousFlushTime).TotalSeconds > 10)
            {
                _previousFlushTime = now;
                _writer.Flush();
            }
        }
    }
}
