using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Ink;

namespace StrokeRecognitionLib
{
    public struct TextResult
    {
        public string dictionary { get; set; }
        public string coerceRecognition { get; set; }
        public ulong StartTime { get; set; }
        public ulong StopTime { get; set; }
        public string TopResult { get; set; }
        public string TopConfidence { get; set; }
        public Dictionary<string,string> Alternates { get; set; }
    }

    public class TextRegion : IDisposable
    {
        private const double PenMetricFactor = 0.5d; // TODO: Find correct value

        public double HiMetricFactor { get; set; }
        //public double PenMetricFactor { get; set; }

        /// <summary>
        /// list of <see cref="Stroke"/> objects crossing this regions
        /// </summary>
        public List<Stroke> Strokes { get; set; }

        // TODO: Remove this property. Just for test purpose
        /// <summary>
        /// the name of this region
        /// </summary>
        public string RegionName { get; set; }

        /// <summary>
        /// properties specifying the bounding box of the region
        /// </summary>
        public Int32 Left { get; set; }
        public Int32 Top { get; set; }
        public Int32 Right { get; set; }
        public Int32 Bottom { get; set; }
        public int Extension { get; set; }

        /// <summary>
        /// adds a stroke to this sequence, if the gravity center of the stroke lies within the region
        /// </summary>
        /// <param name="stroke">see <see cref="Stroke>"/></param>
        public void AddStroke(Stroke stroke)
        {

            removeEqualPoints(stroke, 2);

            // calculate centroid 
            int centerX = 0;
            int centerY = 0;

            for (int i = 0; i < stroke.Points.Count; i++)
            {
                if (!stroke.Points[i].isFailedCoord())
                {
                    centerX += stroke.Points[i].X;
                    centerY += stroke.Points[i].Y;
                }
            }

            centerX /= (stroke.Points.Count - stroke.getNumberOfFailedCoords());
            centerY /= (stroke.Points.Count - stroke.getNumberOfFailedCoords());

            if (this.contains(centerX, centerY, Extension))
            {
                Stroke preprocessedStroke = preprocessStroke(stroke);
                if (Strokes.Count > 0)
                {
                    if (!isStrokePresent(preprocessedStroke))
                        Strokes.Add(preprocessedStroke);
                }
                else
                    Strokes.Add(preprocessedStroke);

                //Strokes.Add(stroke);
            }
        }

        /// <summary>
        /// checks if the point lies within the region
        /// </summary>
        /// <param name="x"> x coordinate</param>
        /// <param name="y"> y coordinate</param>
        /// <param name="extension"> size of region is extendend by this value</param>
        /// <returns> true if region contains this point otherwise false</returns>
        public bool contains(int x, int y, int extension = 0)
        {
            return x >= Left - extension && x <= Right + extension && y >= Top - extension && y <= Bottom + extension;
        }

        private void removeEqualPoints(Stroke stroke, double minDist)
        {
            // thin out
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                Point first = stroke.Points[i - 1];
                Point second = stroke.Points[i];

                double dist = 0.0;
                if (first.isFailedCoord() && second.isFailedCoord())
                    dist = minDist;
                else if (!first.isFailedCoord() || !second.isFailedCoord())
                    dist =
                    System.Math.Sqrt(System.Math.Pow((first.X - second.X), 2) + System.Math.Pow((first.Y - second.Y), 2));

                if (dist < minDist) //TODO: Define propper value
                {
                    stroke.Points.RemoveAt(i);
                    --i;
                }
            }

        }

        private Stroke preprocessStroke(Stroke stroke)
        {
            int width = stroke.Right - stroke.Left;
            int height = stroke.Bottom - stroke.Top;

            Pen pen = new Pen(Color.FromArgb(255, 0, 0, 0), 4);
            Bitmap bmp = new Bitmap(width + 1, height + 1);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.FillRectangle(Brushes.White, 0, 0, bmp.Height, bmp.Width);
            }

            Point shift = new Point(stroke.Left,stroke.Top);
            Stroke newStroke = 
                new Stroke(stroke.Id,stroke.StartTime,stroke.StopTime,stroke.Left,stroke.Top,stroke.Right,stroke.Bottom);
            newStroke.Points.Add(stroke.Points[0]);

            Point lastNonPainted = new Point(-1,-1);

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                Point first = stroke.Points[i - 1] - shift;
                Point second = stroke.Points[i] - shift;

                if (bmp.GetPixel(second.X, second.Y) != Color.FromArgb(255, 0, 0, 0))
                {
                    if (!lastNonPainted.isFailedCoord())
                    {
                        newStroke.Points.Add(lastNonPainted);
                        lastNonPainted.X = -1;
                        lastNonPainted.Y = -1;
                    }
                    newStroke.Points.Add(stroke.Points[i]);

                    // paint this stroke segment in bmp
                    PointF start = new PointF(first.X, first.Y);
                    PointF end = new PointF(second.X, second.Y);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.DrawLine(pen, start, end);
                    }

                }
                else
                {
                    lastNonPainted = stroke.Points[i];
                }
            }

            bmp.Dispose();

            return newStroke;
        }

        private bool isInBoundingRect(Stroke strokeToAdd)
        {
            int top = Strokes[0].Top;
            int left = Strokes[0].Left;
            int bottom = Strokes[0].Bottom;
            int right = Strokes[0].Right;

            //calculate bounding box of all strokes
            foreach (var stroke in Strokes)
            {
                top = Math.Min(top, stroke.Top);
                left = Math.Min(left, stroke.Left);
                bottom = Math.Max(bottom, stroke.Bottom);
                right = Math.Max(right, stroke.Right);
            }

            top -= 5;
            left -= 5;
            bottom += 5;
            right += 5;

            return top <= strokeToAdd.Top && left <= strokeToAdd.Left && right >= strokeToAdd.Right && bottom >= strokeToAdd.Bottom;
        }

        private bool isStrokePresent(Stroke strokeToAdd)
        {
            int top = Strokes[0].Top;
            int left = Strokes[0].Left;
            int bottom = Strokes[0].Bottom;
            int right = Strokes[0].Right;

            //calculate bounding box of all strokes
            foreach (var stroke in Strokes)
            {
                top = Math.Min(top, stroke.Top);
                left = Math.Min(left, stroke.Left);
                bottom = Math.Max(bottom, stroke.Bottom);
                right = Math.Max(right, stroke.Right);
            }

            top = Math.Min(top, strokeToAdd.Top);
            left = Math.Min(left, strokeToAdd.Left);
            bottom = Math.Max(bottom, strokeToAdd.Bottom);
            right = Math.Max(right, strokeToAdd.Right);
            
            top -= 5;
            left -= 5;
            bottom += 5;
            right += 5;

            int width = right - left;
            int height = bottom - top;

            Pen pen = new Pen(Color.FromArgb(255, 0, 0, 0), 6);
            Bitmap bmp = new Bitmap(width+1, height+1);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.FillRectangle(Brushes.White, 0, 0, bmp.Height, bmp.Width);
            }

            Point shift = new Point(left, top);

            // paint all strokes in bitmap
            foreach (var stroke in Strokes)
            {
                for (int i = 1; i < stroke.Points.Count; i++)
                {
                    PointF start = new PointF((stroke.Points[i-1]-shift).X,(stroke.Points[i-1]-shift).Y);
                    PointF end = new PointF((stroke.Points[i] - shift).X, (stroke.Points[i] - shift).Y);

                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.DrawLine(pen, start, end);
                    }
                }
            }

            // count number of points in new stroke, that are already drawn
            double count = 0.0;
            foreach (var point in strokeToAdd.Points)
            {
                if (bmp.GetPixel(point.X - shift.X, point.Y - shift.Y) == Color.FromArgb(255, 0, 0, 0))
                    count++;
            }

            bmp.Dispose();

            double limit = 50.0;
            bool present = (count / strokeToAdd.Points.Count) * 100.0 > limit;

            return present;
        }

        /// <summary>
        /// Recognizes the written text. If a dictionary is defined the recognition is limited to this dictionary.
        /// </summary>
        /// <param name="lcid"> the language code identifier</param>
        /// <param name="theDictionary"> the dictionary</param>
        /// <param name="forceRecognition">if this is set true a recognition based on the dictionary is enforced</param>
        /// <returns></returns>
        public TextResult Recognize(int lcid, string[] theDictionary = null, bool coerceRecognition = false)
        {
            string bestResult = "";
            string confidence = "";
            string dict = "";
            // create a RecognizerContext object
            RecognizerContext recoContext;
            try
            {
                Recognizers recos = new Recognizers();//Check for recognizer.
                //System.Collections.ArrayList theArrayList = new System.Collections.ArrayList(recos.Count);

                ////Version using IEnumerator
                //System.Collections.IEnumerator theEnumerator = recos.GetEnumerator();
                //while (theEnumerator.MoveNext())
                //{

                //    Recognizer theRecognizer = (Recognizer)theEnumerator.Current;
                //    theArrayList.Add(theRecognizer.Name);
                //}

                //int theLCID = 0x0409;
                //Recognizer selectedRecognizer = recos.GetDefaultRecognizer(theLCID);

                Recognizer selectedRecognizer = null;
                foreach (var currentReco in recos)
                {
                    foreach (var langId in currentReco.Languages)
                    {
                        if (langId == lcid) // check if German is supported
                            selectedRecognizer = currentReco;
                    }
                }

                if (selectedRecognizer == null) // if no german recognizer is available
                {
                    int theLCID = 0x0409;
                    selectedRecognizer = recos.GetDefaultRecognizer(theLCID);
                }

                recoContext = selectedRecognizer.CreateRecognizerContext();

            }
            catch
            {
                throw new Exception( "No recognizers installed!");
            }

            // if  a dictionary is defined

            if (theDictionary != null && theDictionary.Length != 0)
            {
                //string[] parts = theDictionary.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

                // the WordList defined by the user
                WordList theUserWordList = new WordList();
                for (int i = 0; i < theDictionary.Length; i++)
                {
                    theUserWordList.Add(theDictionary[i]);
                    dict += " ";
                    dict += theDictionary[i];
                }
                
                // add the user defined WordList to the recognizers WordList
                recoContext.WordList = theUserWordList;

                // limit the search to the WordList that is associated
                recoContext.Factoid = Factoid.WordList;
                if (coerceRecognition)
                    recoContext.RecognitionFlags |= RecognitionModes.Coerce;
            }

            var inkCollector = new InkCollector();
            Ink ink = inkCollector.Ink;

            foreach (var stroke in Strokes)
            {
                if (stroke.Points.Count > 0)
                {
                    System.Drawing.Point[] _stroke = new System.Drawing.Point[stroke.Points.Count];

                    for (int i = 0; i < stroke.Points.Count; i++)
                    {
                        _stroke[i] =
                            new System.Drawing.Point() { X = (int)(stroke.Points[i].X * HiMetricFactor * PenMetricFactor), Y = (int)(stroke.Points[i].Y * HiMetricFactor * PenMetricFactor) };
                    }

                    // add stroke to ink object
                    var inkStroke = ink.CreateStroke(_stroke);
                }
            }

            TextResult result;
            Dictionary<string, string> allAlternates = new Dictionary<string, string>();

            if (ink.Strokes.Count > 0)
            {
                RecognitionStatus status;
                RecognitionResult recoResult;
                recoContext.Strokes = ink.Strokes;
                recoResult = recoContext.Recognize(out status);

                // Check the recognition status.
                if (Microsoft.Ink.RecognitionStatus.NoError != status)
                {
                    bestResult = null;
                }

                // Check for a recognition result.
                if (null == recoResult)
                {
                    bestResult = null;
                }
                else
                    bestResult = recoResult.TopString;

                if (bestResult == null)
                {
                    bestResult = "No valid answer! Please review!";
                    confidence = "Strong";
                }
                else
                {
                    RecognitionConfidence conf = recoResult.TopConfidence;
                    confidence = conf.ToString();

                    RecognitionAlternates confidenceAlternates = recoResult.TopAlternate.ConfidenceAlternates;
                    RecognitionAlternates selectionAlternates = recoResult.GetAlternatesFromSelection();
                    foreach (RecognitionAlternate alternate in selectionAlternates)
                    {
                        string alternateConfidence = alternate.Confidence.ToString();
                        string alternateResult = alternate.ToString();
                        if( !allAlternates.ContainsKey(alternateResult) )
                            allAlternates.Add(alternateResult, alternateConfidence);
                    }
                }

                result = new TextResult()
                {
                    dictionary = dict,
                    coerceRecognition = coerceRecognition.ToString(),
                    StartTime = Strokes[0].StartTime,
                    StopTime = Strokes[Strokes.Count - 1].StopTime,
                    TopResult = bestResult,
                    TopConfidence = confidence,
                    Alternates = new Dictionary<string, string>(allAlternates),
                };
            }
            else 
            {
                result = new TextResult()
                {
                    dictionary = dict,
                    coerceRecognition = coerceRecognition.ToString(),
                    StartTime = 0,
                    StopTime = 0,
                    TopResult = bestResult,
                    TopConfidence = confidence,
                    Alternates = new Dictionary<string, string>(),
                };
            }

            ink.Dispose();
            ink = null;
            recoContext.Dispose();
            recoContext = null;

            return result;
        }

        private void writeInFile(string result)
        {
            string filename = "C:\\Temp\\PreProcessed_Coerce_Dict.txt";
            if (!File.Exists(filename))
            {
                StreamWriter sw = new StreamWriter(filename);

                sw.WriteLine( RegionName + " "  + result);
                sw.Close();
            }

            else if (File.Exists(filename))
            {
                using (StreamWriter w = File.AppendText(filename))
                {
                    w.WriteLine(RegionName + " " + result);
                    w.Close();
                }
            }
        }

        #region Constructor
        public TextRegion()
        {
            Strokes = new List<Stroke>();
        }

        public TextRegion(Int32 left, Int32 top, Int32 right, Int32 bottom, int extension, string name/*, double penDPI*/)
        {
            Strokes = new List<Stroke>();
            RegionName = name;
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            Extension = extension;
            HiMetricFactor = 1000 * 2.54 / 96;
        }

        public TextRegion(Int32 left, Int32 top, Int32 right, Int32 bottom, int extension/*, double penDPI*/)
        {
            Strokes = new List<Stroke>();
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            Extension = extension;
            HiMetricFactor = 1000 * 2.54 / 96;
            RegionName = "";
        }

        public void Dispose()
        {
            this.Strokes.Clear();
        }
        #endregion
    }

}
