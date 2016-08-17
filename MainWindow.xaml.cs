//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------
namespace Redwood.Kinect.Slowmo
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using System.Collections.Generic;

    using System.Drawing.Imaging;

    /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private KinectSensor kinectSensor = null;
        private ColorFrameReader colorFrameReader = null;
        private WriteableBitmap colorBitmap = null;
        private string statusText = null;

        #region Dependency Properties

        public event PropertyChangedEventHandler PropertyChanged;

        public ImageSource ImageSource
        {
            get
            {
                return colorBitmap;
            }
        }

        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;

                    // notify any bound elements that the text has changed
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        public string StoredFrames
        {
            get
            {
                return storedFrames.Count.ToString();
            }
        }

        public string Lag
        {
            get
            {
                return (idealLag - storedFrames.Count).ToString("0.00");
            }
        }

        #region Blinker Stuff

            Brush _fill = Brushes.Red;
            public Brush EllipseFill
            {
                get { return _fill; }
                set
                {
                    _fill = value;

                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("EllipseFill"));
                    }
                }
            }
        
            public double EllipseDiameter
            {
                get { return _diam; }
                set
                {
                    _diam = value;

                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("EllipseDiameter"));
                    }
                }
            }

            public double EllipseOpacity
            {
                get { return _opacity; }
                set
                {
                    _opacity = value;

                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("EllipseOpacity"));
                    }
                }
            }
        #endregion

        #endregion

        public MainWindow()
        {
            // get the kinectSensor object
            this.kinectSensor = KinectSensor.GetDefault();

            // open the reader for the color frames
            this.colorFrameReader = this.kinectSensor.ColorFrameSource.OpenReader();

            // open the reader for the body frames
            this.bodyFrameReader = this.kinectSensor.BodyFrameSource.OpenReader();

            // wire handler for frame arrival
            this.colorFrameReader.FrameArrived += this.Reader_ColorFrameArrived;

            // wire handler for skeleton data
            if (skeletonActivate )
                this.bodyFrameReader.FrameArrived += this.BodyReader_FrameArrived;

            // create the colorFrameDescription from the ColorFrameSource using Bgra format
            FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);

            // create the bitmap to display
            //this.colorBitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height, 96.0, 96.0, PixelFormats.Bgr32, null);
            this.colorBitmap = BitmapFactory.New(colorFrameDescription.Width, colorFrameDescription.Height);
            //this.colorBitmap = BitmapFactory.New(_width, _height);

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // open the sensor
            this.kinectSensor.Open();

            // set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.NoSensorStatusText;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // initialize the components (controls) of the window
            this.InitializeComponent();
        }

        

        int missedFrameCount = 0;
        public int MissedFrames
        {
            get
            {
                return missedFrameCount;
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (this.colorFrameReader != null)
            {
                // ColorFrameReder is IDisposable
                this.colorFrameReader.Dispose();
                this.colorFrameReader = null;
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }


        private void Reader_ColorFrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            using (ColorFrame colorFrame = e.FrameReference.AcquireFrame())
            {
                if (colorFrame != null)
                {
                    if ( !skeletonActivate || skeletonAcquired )
                    {
                        SlowMotion(colorFrame);
                    }
                    else
                    {
                        Mirror(colorFrame);
                    }
                }
                else {
                    Debug.WriteLine("WARNING: Missed a frame!");
                    missedFrameCount++;
                    this.PropertyChanged(this, new PropertyChangedEventArgs("MissedFrames"));
                }
            }
        }

        // TODO: make all these dependency properties that you can control from the main screen
        private int delay = 3; // number of seconds to show in real time before beginning slowdown
        private double easingDown = 0.05;  // amount to start slowing every second after the initial delay
        private double easingUp = .25;
        private double minSlowFactor = 0.5; // the slowest the slowmo goes. lower = slower
        double maxFastFactor = 2;

        double slowFactor = 1; // the initial speed. 1 = normal speed
        double slowCount = 0;
        double idealLag = 0;
        double counter = 0;
        bool slowDown = true;
        bool nextFrameShouldBeWritten = true;

        private Queue<byte[]> storedFrames = new Queue<byte[]>();

        /// Size for the RGB pixel in bitmap. I don't understand how this is calculated
        private readonly int _bytePerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;

        int consecutiveNoWrites = 0;
        public string ConsecutiveNoWrites
        {
            get
            {
                return consecutiveNoWrites.ToString();
            }
        }
        
        private void SlowMotion(Microsoft.Kinect.ColorFrame colorFrame)
        {
            StoreFrameToQueue(colorFrame);
            this.PropertyChanged(this, new PropertyChangedEventArgs("StoredFrames"));

            ApplySlowFastEffect(); // increments idealLag with slowness added in

            Pulse();

            if (nextFrameShouldBeWritten)
            {
                WriteFrameFromQueue();

                if (consecutiveNoWrites > 0)
                {
                    consecutiveNoWrites = 0;
                    this.PropertyChanged(this, new PropertyChangedEventArgs("ConsecutiveNoWrites"));
                }
            }
            else
            {
                consecutiveNoWrites++;
                if (consecutiveNoWrites > 2)
                {
                    Debug.WriteLine("REWRITE!!");
                }
                this.PropertyChanged(this, new PropertyChangedEventArgs("ConsecutiveNoWrites"));
            }

            counter++;
        }

        private void ResetStuff()
        {
            Debug.WriteLine("resetting stuff");
            storedFrames.Clear();
            slowFactor = 1;
            slowCount = 0;
            counter = 0;
            idealLag = 0;
            slowDown = true;
            nextFrameShouldBeWritten = true;
            consecutiveNoWrites = 0;
            this.PropertyChanged(this, new PropertyChangedEventArgs("Lag"));
            this.PropertyChanged(this, new PropertyChangedEventArgs("ConsecutiveNoWrites"));
            this.PropertyChanged(this, new PropertyChangedEventArgs("StoredFrames"));
            this.EllipseDiameter = 10;
            this.EllipseFill = Brushes.Transparent;
        }

        private void ApplySlowFastEffect()
        {
            if (slowDown)
            {
                // begin slowing down
                if (slowFactor - easingDown >= minSlowFactor )
                {
                    if (slowCount == 30)
                    {
                        Debug.WriteLine("Slowing Down");
                        // bump down slowFactor (by easing) to make things a little slower
                        slowFactor = slowFactor - easingDown;
                        slowCount = 0;
                    }
                    else
                    {
                        slowCount++;
                    }
                }
                else
                {
                    Debug.WriteLine("slowest");
                    slowDown = false;
                }
            }
            else
            {
                // begin speeding up
                if (slowFactor + easingUp <= maxFastFactor && storedFrames.Count != 0)
                {
                    if (slowCount == 30)
                    {
                        Debug.WriteLine("Speeding Up");
                        // bump up slowFactor (by easing) to make things a little faster
                        slowFactor = slowFactor + easingUp;
                        slowCount = 0;
                    }
                    else
                    {
                        slowCount++;
                    }
                }
                else
                {
                    Debug.WriteLine("fastest");
                    slowDown = true;
                }
            }

            // if we're still behind on frames then short circuit
            if (idealLag > storedFrames.Count + 1)
            {
                nextFrameShouldBeWritten = false;
                return;
            }

            idealLag = idealLag + (1 / slowFactor);
            this.PropertyChanged(this, new PropertyChangedEventArgs("Lag"));

            // don't ever get too close!
            if (storedFrames.Count <= 1 && slowFactor > 1)
            {
                Debug.WriteLine("WARNING: we're too close");
                slowFactor = 1;
                slowDown = true;
                slowCount = 0;
            }

            //if we're speeding up then we might need to dequeue more than one frame
            while (idealLag < storedFrames.Count && storedFrames.Count > 1)
            {
                storedFrames.Dequeue();
                this.PropertyChanged(this, new PropertyChangedEventArgs("StoredFrames"));

                if (storedFrames.Count == 0)
                {
                    Debugger.Break();
                }
            }

            nextFrameShouldBeWritten = true;
            idealLag--;

            this.PropertyChanged(this, new PropertyChangedEventArgs("Lag"));   
        }

        double _diam = 40;
        private int _minDiameter = 30;
        private int _maxDiameter = 60;
        private bool _oldSlowDown = true;
        private bool _diamGrowing = true;
        private double _diamGrowthFactor = 1;
        private double _opacity = 1;
        private double _opacityGrowthFactor = .05;

        /// <summary>
        /// Change the indicator light based on app status
        /// </summary>
        private void Pulse()
        {
            if (slowDown != _oldSlowDown )
            {
                if( slowDown )
                    EllipseFill = Brushes.Red;
                else
                    EllipseFill = Brushes.Green;

                _oldSlowDown = slowDown;
            }

            // Grow and shrink effect
            //if (_diamGrowing)
            //{
            //    if (EllipseDiameter + _diamGrowthFactor < _maxDiameter )
            //    {
            //        EllipseDiameter = EllipseDiameter + _diamGrowthFactor;
            //    }
            //    else 
            //        _diamGrowing = false;
            //}
            //else
            //{
            //    if (EllipseDiameter - _diamGrowthFactor > _minDiameter)
            //    {
            //        EllipseDiameter = EllipseDiameter - _diamGrowthFactor;
            //    }
            //    else 
            //        _diamGrowing = true;
            //}

            if (_diamGrowing)
            {
                if (_opacity + _opacityGrowthFactor <= 1)
                {
                    EllipseOpacity = _opacity + _opacityGrowthFactor;
                }
                else
                    _diamGrowing = false;
            }
            else
            {
                if (_opacity - _opacityGrowthFactor >= 0)
                {
                    EllipseOpacity = _opacity - _opacityGrowthFactor;
                }
                else
                    _diamGrowing = true;
            }
        }

        private void WriteFrameFromQueue()
        {
            // this.colorBitmap is a WriteableBitmap on my WPF window
            this.colorBitmap.Lock();

            // write to the bitmap from the stored byte array in the queue
            this.colorBitmap.FromByteArray(storedFrames.Dequeue());

            this.colorBitmap.Unlock();
        }

        private void StoreFrameToQueue(ColorFrame colorFrame)
        {
            FrameDescription colorFrameDescription = colorFrame.FrameDescription;

            using (colorFrame.LockRawImageBuffer())
            {
                // Store the color frame data as a byte array
                byte[] bytes = new byte[colorFrameDescription.Width * colorFrameDescription.Height * _bytePerPixel];
                //byte[] bytes = new byte[_width * _height * _bytePerPixel];

                // the incoming image data is in yuy2
                colorFrame.CopyConvertedFrameDataToArray(bytes, ColorImageFormat.Bgra);

                // store off the frame into a queue
                storedFrames.Enqueue(bytes);
            }
        }


        /// <summary>
        /// Play back 
        /// </summary>
        /// <param name="colorFrame"></param>
        private void Mirror(ColorFrame colorFrame)
        {
            FrameDescription colorFrameDescription = colorFrame.FrameDescription;

            using (KinectBuffer colorBuffer = colorFrame.LockRawImageBuffer())
            {
                this.colorBitmap.Lock();

                // verify data and write the new color frame data to the display bitmap
                if ((colorFrameDescription.Width == this.colorBitmap.PixelWidth) && (colorFrameDescription.Height == this.colorBitmap.PixelHeight))
                {
                    colorFrame.CopyConvertedFrameDataToIntPtr(
                        this.colorBitmap.BackBuffer,
                        (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                        ColorImageFormat.Bgra);

                    this.colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight));
                }

                this.colorBitmap.Unlock();
            }
        }

        private void ApplySlowEffect()
        {
            // at 30 fps the delay is 30*seconds
            if (counter < (30 * delay))
            {
                // if we're still in the display period, just show the next frame
                Debug.WriteLine("Still in delay period");
            }
            else
            {
                Debug.WriteLine("Beginning slowdown");

                // begin slowing down
                if (slowCount == 30 && slowFactor > minSlowFactor)
                {
                    // bump down slowFactor (by easing) to make things a little slower
                    slowFactor = (slowFactor - easingDown) < minSlowFactor ? minSlowFactor : (slowFactor - easingDown);
                    slowCount = 0;
                }
                else
                {
                    slowCount++;
                }

                idealLag = idealLag + (1 / slowFactor);
                Debug.WriteLine("lfc is now " + idealLag);
            }
        }

        private void CheckMemory()
        {
            int maxFrames = 20000;  // run out of memory at around 184 frames right now...which means each frame is 10.8MB (!)

            // if we've reached the storedFrames ceiling then reset
            if (storedFrames.Count > maxFrames)
            {
                storedFrames.Clear();
                counter = 0;
                idealLag = 0;
            }
        }

        #region Compressed

            private WriteableBitmap tempBitmap;

            private int storeCounter = 0;

            private void SlowMotionCompressed(Microsoft.Kinect.ColorFrame colorFrame)
            {
                if (storeCounter == 1)
                {
                    StoreFrameCompressed(colorFrame);
                    storeCounter++;
                }
                else if (storeCounter == 2)
                {
                    WriteFrameCompressed();
                    storeCounter = 0;
                }
                else
                {
                    storeCounter++;
                }
            }

            private void StoreFrameCompressed(ColorFrame colorFrame)
            {
                FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                using (colorFrame.LockRawImageBuffer())
                {
                    BitmapEncoder encoder = new PngBitmapEncoder();

                    // Store the color frame data as a byte array
                    byte[] bytes = new byte[colorFrameDescription.Width * colorFrameDescription.Height * _bytePerPixel];

                    // the incoming image data is in yuy2
                    colorFrame.CopyConvertedFrameDataToArray(bytes, ColorImageFormat.Bgra);

                    // write to the bitmap from the stored byte array in the queue
                    // so i can get a bitmapsource
                    this.tempBitmap.FromByteArray(bytes);

                    // create frame from the writable bitmap and add to encoder
                    BitmapFrame frame = BitmapFrame.Create(this.tempBitmap);
                    encoder.Frames.Add(frame);

                    byte[] bytesToSave;

                    using (MemoryStream streamToSave = new MemoryStream())
                    {
                        encoder.Save(streamToSave);
                        bytesToSave = streamToSave.ToArray();
                    }

                    storedFrames.Enqueue(bytesToSave);
                    Debug.WriteLine("queue size: " + storedFrames.Count);
                    //using (MemoryStream stream = new MemoryStream(bytes))
                    //{
                    //    //***********************************************
                    //    //TODO: figure out how we can get from the colorframe to something the pngencoder can digest
                    //    //***********************************************

                    //    BitmapFrame bmframe = BitmapFrame.Create(stream);

                    //    // populate the encoder with data from the colorFrame
                    //    encoder.Frames.Add(bmframe);
                    //}

                    //// store the compressed byte stream in the queue
                    //using (MemoryStream stream = new MemoryStream())
                    //{
                    //    encoder.Save(stream);
                    //    storedFrames.Enqueue(stream.ToArray());
                    //}

                    //if (storedFrames.Count > maxFrames)
                    //{
                    //    storedFrames.Clear();
                    //    counter = 0;
                    //    nextFrameIndexToDoAShow = 0;
                    //}
                }
            }

            private void WriteFrameCompressed()
        {
            // short circuit and don't change the writeable bitmap
            //if (counter++ != Math.Round(nextFrameIndexToDoAShow))
            //{
            //    return;
            //}

            Debug.WriteLine("Write");

            // this.colorBitmap is a WriteableBitmap on my WPF window
            //this.colorBitmap.Lock();

            byte[] bytes = storedFrames.Dequeue();

            PngBitmapDecoder decoder;

            BitmapSource bitmapSource;

            // Open a Stream and decode a PNG image
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                bitmapSource = decoder.Frames[0];

                // Calculate stride of source
                int stride = bitmapSource.PixelWidth * (bitmapSource.Format.BitsPerPixel / 8);

                // Create data array to hold source pixel data
                byte[] data = new byte[stride * bitmapSource.PixelHeight];

                // Copy source image pixels to the data array
                bitmapSource.CopyPixels(data, stride, 0);

                // Write the pixel data to the WriteableBitmap.
                this.colorBitmap.WritePixels(
                  new Int32Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight),
                  data, stride, 0);

                this.colorBitmap.Lock();
                this.colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight));
                this.colorBitmap.Unlock();
            }

            

            //this.colorBitmap = new WriteableBitmap(bitmapSource);
            //this.colorBitmap = (WriteableBitmap)bitmapSource;

            // write to the bitmap from the stored byte array in the queue
            //this.colorBitmap.FromByteArray(storedFrames.Dequeue());

            //this.colorBitmap.Unlock();

            //// at 30 fps the delay is 30*seconds
            //if (counter < (30 * delay))
            //{
            //    nextFrameIndexToDoAShow++;
            //}
            //else
            //{
            //    if (slowCount == 30 && slowFactor > minSlowFactor)
            //    {
            //        slowFactor = (slowFactor - easing) < minSlowFactor ? minSlowFactor : (slowFactor - easing);
            //        slowCount = 0;
            //    }
            //    else
            //    {
            //        slowCount++;
            //    }

            //    nextFrameIndexToDoAShow = nextFrameIndexToDoAShow + (1 / slowFactor);
            //}
        }

        #endregion

        /// <summary>
        /// Handles the event which the sensor becomes unavailable (E.g. paused, closed, unplugged).
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            // on failure, set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.SensorNotAvailableStatusText;
        }

        #region Skeleton Tracking

            /// <summary>
            /// Reader for body frames
            /// </summary>
            private BodyFrameReader bodyFrameReader = null;

            /// <summary>
            /// Array for the bodies
            /// </summary>
            private Body[] bodies = null;

            private bool skeletonAcquired = false;
            private int missThreshold = 20;
            private int missedCount = 0; // once we've missed 'missedThreshold' consecutive frames we turn off skeletonAcquired

            private int framesAfterSkeleton = 300; // once skeleton is gone, wait this many more frames before resetting (about 10 seconds)
            private int skeletonBuffer = 0;

            private void BodyReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
            {
                bool dataReceived = false;

                //Debug.WriteLine("Missed Count = " + missedCount);

                using (BodyFrame bodyFrame = e.FrameReference.AcquireFrame())
                {
                    if (bodyFrame != null)
                    {
                        if (this.bodies == null)
                        {
                            this.bodies = new Body[bodyFrame.BodyCount];
                        }

                        // The first time GetAndRefreshBodyData is called, Kinect will allocate each Body in the array.
                        // As long as those body objects are not disposed and not set to null in the array,
                        // those body objects will be re-used.
                        bodyFrame.GetAndRefreshBodyData(this.bodies);
                        dataReceived = true;
                    }
                }

                if (dataReceived)
                {
                    foreach (Body body in this.bodies)
                    {
                        if (body.IsTracked)
                        {
                            //Debug.WriteLine("We Have Skeleton!");
                            if (!skeletonAcquired)
                                ResetStuff();
                            skeletonAcquired = true;
                            missedCount = 0;
                            skeletonBuffer = 0;
                            return; // short circuit
                        }
                    }

                    if (missedCount < missThreshold)
                    {
                        missedCount++;
                        return;
                    }
                    else if (skeletonBuffer < framesAfterSkeleton)
                    {
                        skeletonBuffer++;
                        //Debug.WriteLine("skeletonbuffer is " + skeletonBuffer);
                        return;
                    }

                    //Debug.WriteLine("No skeleton tracked");
                    // We're switching modes
                    if (skeletonAcquired)
                        ResetStuff();
                    skeletonAcquired = false;
                }

                
            }

        #endregion
    }
}
