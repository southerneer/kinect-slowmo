﻿//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------
namespace Microsoft.Samples.Kinect.ColorBasics
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
    using System.Drawing;
    using System.Drawing.Imaging;

    /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private KinectSensor kinectSensor = null;

        /// <summary>
        /// Reader for color frames
        /// </summary>
        private ColorFrameReader colorFrameReader = null;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private WriteableBitmap colorBitmap = null;

        /// <summary>
        /// Current status text to display
        /// </summary>
        private string statusText = null;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
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
            this.bodyFrameReader.FrameArrived += this.BodyReader_FrameArrived;

            // create the colorFrameDescription from the ColorFrameSource using Bgra format
            FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);

            // create the bitmap to display
            //this.colorBitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height, 96.0, 96.0, PixelFormats.Bgr32, null);

            //WriteableBitmapExtensions.
            this.colorBitmap = BitmapFactory.New(colorFrameDescription.Width, colorFrameDescription.Height);

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

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource ImageSource
        {
            get
            {
                return this.colorBitmap;
            }
        }

        /// <summary>
        /// Gets or sets the current status text to display
        /// </summary>
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

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
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

        /// <summary>
        /// Handles the user clicking on the screenshot button
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.colorBitmap != null)
            {
                // create a png bitmap encoder which knows how to save a .png file
                BitmapEncoder encoder = new PngBitmapEncoder();

                // create frame from the writable bitmap and add to encoder
                encoder.Frames.Add(BitmapFrame.Create(this.colorBitmap));

                string time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);

                string myPhotos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                string path = Path.Combine(myPhotos, "KinectScreenshot-Color-" + time + ".png");

                // write the new file to disk
                try
                {
                    // FileStream is IDisposable
                    using (FileStream fs = new FileStream(path, FileMode.Create))
                    {
                        encoder.Save(fs);
                    }

                    this.StatusText = string.Format(Properties.Resources.SavedScreenshotStatusTextFormat, path);
                }
                catch (IOException)
                {
                    this.StatusText = string.Format(Properties.Resources.FailedScreenshotStatusTextFormat, path);
                }
            }
        }

        /// <summary>
        /// Handles the color frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_ColorFrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            using (ColorFrame colorFrame = e.FrameReference.AcquireFrame())
            {
                if (colorFrame != null)
                {
                    SlowMotion(colorFrame);
                }
            }
        }

        
        private int delay = 1; // number of seconds to show in real time before beginning slowdown
        private double easing = 0.1;  // amount to start slowing every second after the initial delay
        private double minSlowFactor = 0.5; // the slowest the slowmo goes. lower = slower

        private double slowFactor = 1;
        private double slowCount = 0;
        private int counter = 0;
        private double nextFrameIndexToDoAShow = 0;
        private int maxFrames = 20000;  // run out of memory at around 184 frames right now...which means each frame is 10.8MB (!)


        private Queue<byte[]> storedFrames = new Queue<byte[]>(1000);

        /// Size for the RGB pixel in bitmap. I don't understand how this is calculated
        private readonly int _bytePerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;
        
        private void SlowMotion(Microsoft.Kinect.ColorFrame colorFrame)
        {
            if (skeletonAcquired)
            {
                StoreFrame(colorFrame);

                WriteFrame();
            }
            else
            {
                storedFrames.Clear();
                slowFactor = 1;
                slowCount = 0;
                counter = 0;
                nextFrameIndexToDoAShow = 0;
                this.colorBitmap.Clear(System.Windows.Media.Color.FromRgb(0, 0, 0));
            }
        }

        private void WriteFrame()
        {
            // short circuit and don't change the writeable bitmap
            if (counter++ != Math.Round(nextFrameIndexToDoAShow))
            {
                return;
            }

            //Debug.WriteLine("counter = " + counter + " and nfitdas = " + nextFrameIndexToDoAShow);
            //Debug.WriteLine("FrameList count = " + storedFrames.Count);

            // this.colorBitmap is a WriteableBitmap on my WPF window
            this.colorBitmap.Lock();

            // write to the bitmap from the stored byte array in the queue
            this.colorBitmap.FromByteArray(storedFrames.Dequeue());

            this.colorBitmap.Unlock();

            // at 30 fps the delay is 30*seconds
            if (counter < (30 * delay))
            {
                nextFrameIndexToDoAShow++;
            }
            else
            {
                if (slowCount==30 && slowFactor > minSlowFactor)
                {
                    slowFactor = (slowFactor - easing) < minSlowFactor ? minSlowFactor : (slowFactor - easing);
                    slowCount = 0;
                }
                else
                {
                    slowCount++;
                }

                nextFrameIndexToDoAShow = nextFrameIndexToDoAShow + (1 / slowFactor);
            }
        }

        private void StoreFrame(ColorFrame colorFrame)
        {
            FrameDescription colorFrameDescription = colorFrame.FrameDescription;

            using (colorFrame.LockRawImageBuffer())
            {
                // Store the color frame data as a byte array
                byte[] bytes = new byte[colorFrameDescription.Width * colorFrameDescription.Height * _bytePerPixel];

                // the incoming image data is in yuy2
                colorFrame.CopyConvertedFrameDataToArray(bytes, ColorImageFormat.Bgra);

                if (storedFrames.Count > maxFrames)
                {
                    storedFrames.Clear();
                    counter = 0;
                    nextFrameIndexToDoAShow = 0;
                }

                // store off the frame into a queue
                storedFrames.Enqueue(bytes);
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

                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    //***********************************************
                    //TODO: figure out how we can get from the colorframe to something the pngencoder can digest
                    //***********************************************

                    BitmapFrame bmframe = BitmapFrame.Create(stream);

                    // populate the encoder with data from the colorFrame
                    encoder.Frames.Add(bmframe);
                }

                // store the compressed byte stream in the queue
                using (MemoryStream stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    storedFrames.Enqueue(stream.ToArray());
                }

                if (storedFrames.Count > maxFrames)
                {
                    storedFrames.Clear();
                    counter = 0;
                    nextFrameIndexToDoAShow = 0;
                }
            }
        }

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

            private int framesAfterSkeleton = 300; 
            private int skeletonBuffer = 0; // once skeleton is gone, wait this many more frames before resetting

            private void BodyReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
            {
                bool dataReceived = false;

                Debug.WriteLine("Missed Count = " + missedCount);

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
                            skeletonAcquired = true;
                            missedCount = 0;
                            skeletonBuffer = 0;
                            return;
                        }
                    }
                }

                if( missedCount < missThreshold )
                {
                    missedCount++;
                    return;
                } else if(skeletonBuffer < framesAfterSkeleton) {
                    skeletonBuffer++;
                    Debug.WriteLine("skeletonbuffer is " + skeletonBuffer);
                    return;
                }

                Debug.WriteLine("No skeleton tracked");
                skeletonAcquired = false;
            }

        #endregion
    }
}
