//------------------------------------------------------------------------------
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

    /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>
        /// Active Kinect sensor
        /// </summary>
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

            // wire handler for frame arrival
            this.colorFrameReader.FrameArrived += this.Reader_ColorFrameArrived;

            // create the colorFrameDescription from the ColorFrameSource using Bgra format
            FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);

            // create the bitmap to display
            this.colorBitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height, 96.0, 96.0, PixelFormats.Bgr32, null);

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
                    //OriginalWriteMethod(colorFrame);

                    //MyWriteMethod(colorFrame);

                    SlowMotion(colorFrame);
                }
            }
        }

        //private List<ColorFrame> frameList = new List<ColorFrame>();
        //private List<IntPtr> frameList = new List<IntPtr>();
        //private List<Array<byte>> frameList

        private int counter = 0;
        private int slowFactor = 2;
        private int frameIndex = 0;
        private int maxFrames = 100;
        private List<byte[]> frameList = new List<byte[]>();
        /// Size for the RGB pixel in bitmap. I don't understand how this is calculated
        private readonly int _bytePerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;
        
        private void SlowMotion(Microsoft.Kinect.ColorFrame colorFrame)
        {
            FrameDescription colorFrameDescription = colorFrame.FrameDescription;

            using (colorFrame.LockRawImageBuffer())
            {
                // Store the color frame data as a byte array
                //byte[] bytes = new byte[colorFrameDescription.LengthInPixels * colorFrameDescription.BytesPerPixel];
                byte[] bytes = new byte[colorFrameDescription.Width * colorFrameDescription.Height * _bytePerPixel];

                if (colorFrame.RawColorImageFormat == ColorImageFormat.Bgra)
                {
                    colorFrame.CopyRawFrameDataToArray(bytes);
                }
                else colorFrame.CopyConvertedFrameDataToArray(bytes, ColorImageFormat.Bgra);

                // store off the frame into a buffer
                if(frameList.Count < maxFrames)
                    frameList.Add(bytes);
            }

            // short circuit and don't change the writeable bitmap
            if (counter != slowFactor)
            {
                counter++;
                return;
            }

            // this.colorBitmap is a WriteableBitmap on my WPF window
            this.colorBitmap.Lock();

            Int32Rect frameRect = new Int32Rect(0, 0, colorFrameDescription.Width, colorFrameDescription.Height);

            // write the next byte array in our "buffer" to the output bitmap (?)
            // TODO: need to convert to BGRA format (otherwise it will try YUY2)
            this.colorBitmap.WritePixels(
                frameRect,
                frameList[frameIndex], // this is the byte array I stored away above
                this.colorBitmap.BackBufferStride,
                0);

            frameIndex++;

            // specify that the bitmap has changed
            this.colorBitmap.AddDirtyRect(frameRect);

            this.colorBitmap.Unlock();

            counter = 0;
        }

        private void MyWriteMethod(ColorFrame colorFrame)
        {
            FrameDescription colorFrameDescription = colorFrame.FrameDescription;

            using (KinectBuffer colorBuffer = colorFrame.LockRawImageBuffer())
            {
                

                //IntPtr ptr = new IntPtr();
                //colorFrame.CopyConvertedFrameDataToIntPtr(
                //        ptr,
                //        (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                //        ColorImageFormat.Bgra);

                //// store off the frame into a buffer
                //frameList.Add(ptr);


                // *** Store the color frame as a byte array
                byte[] bytes = new byte[colorFrameDescription.LengthInPixels * colorFrameDescription.BytesPerPixel];
                colorFrame.CopyRawFrameDataToArray(bytes);

                //// *** Store the color frame as a formatted byte array
                //byte[] bytes = new byte[colorFrameDescription.LengthInPixels * colorFrameDescription.BytesPerPixel];
                //colorFrame.CopyRawFrameDataToArray(bytes);

                // store off the frame into a buffer
                frameList.Add(bytes);

                if (counter == slowFactor)
                {
                    //Debug.WriteLine("writing to output");
                    this.colorBitmap.Lock();

                    // verify data and write the new color frame data to the display bitmap
                    if ((colorFrameDescription.Width == this.colorBitmap.PixelWidth) && (colorFrameDescription.Height == this.colorBitmap.PixelHeight))
                    {
                        //Debug.WriteLine("Writing to bitmap");

                        //// write the input colorframe (from the camera) to the output colorframe
                        //colorFrame.CopyConvertedFrameDataToIntPtr(
                        //    this.colorBitmap.BackBuffer,
                        //    (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                        //    ColorImageFormat.Bgra);

                        //this.colorBitmap.back

                        // TODO: write the next byte array in our "buffer" to the output bitmap
                        this.colorBitmap.WritePixels(
                            new Int32Rect(0, 0, colorFrameDescription.Width, colorFrameDescription.Height),
                            frameList[frameIndex],
                            this.colorBitmap.BackBufferStride,
                            0 );

                        Debug.WriteLine("Wrote frame " + frameIndex);

                        frameIndex++;

                        // specify that the bitmap has changed
                        this.colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight));
                    }

                    this.colorBitmap.Unlock();

                    counter = 0;
                }
                else
                {
                    //Debug.WriteLine("Incrementing counter");
                    counter++;
                    //Debug.WriteLine("Counter is " + counter);
                }
            }
        }

        private void OriginalWriteMethod(ColorFrame colorFrame)
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
    }
}
