using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using libSVMWrapper;

namespace StrokeRecognitionLib
{
    ////////////////////////
    // Pattern      Label //
    // ================== //
    // Cross        1     //
    // Crossed Out  2     //
    // Circle       3     //
    // Slash        4     //
    // BackSlash    5     //
    ////////////////////////

    public struct SequencingResult
    {
        public string SequenceNumber { get; set; }
        public ulong StartTime { get; set; }
        public ulong StopTime { get; set; }
        public double CrossLikelihood { get; set; }
        public double CrossedOutLikelihood { get; set; }
        public double EllipseLikelihood { get; set; }
        public double SlashLikelihood { get; set; }
        public double BackSlashLikelihood { get; set; }
    }

    public class Sequence
    {
        public List<Stroke> Strokes { get; set; }

        public Sequence()
        {
            Strokes = new List<Stroke>();
        }
    }

    public class PatternRegion : IDisposable
    {
        private static object lockObj = new object();
        private static bool isInitialized = false;
        private static libSVM svm = new libSVM();
        private static List<double> scaleRange = new List<double>(new double[] {7866.8283337476,155.405508377214,100.0,100.0,72.4173553719008});


        /// <summary>
        /// list of <see cref="Stroke"/> objects crossing this region
        /// </summary>
        public List<Stroke> Strokes { get; set; }

        /// <summary>
        /// the name of this region
        /// </summary>
        public string RegionName { get; set; }

        /// <summary>
        /// properties specifying the size of the region
        /// </summary>
        public Int32 Left { get; set; }
        public Int32 Top { get; set; }
        public Int32 Right { get; set; }
        public Int32 Bottom { get; set; }
        public int Extension { get; set; }

        /// <summary>
        /// adds a stroke to this sequence, if the gravity center of the stroke lies within the region, 
        /// if the stroke contains at least 5 valid <see cref="Point"/> objects and the length of the stroke is
        /// greater than minLength
        /// </summary>
        /// <param name="stroke"> the <see cref="Stroke>"/> object to be added</param>
        /// <param name="minLength"> minimum length the stroke must have to be added</param>
        public bool AddStroke(Stroke stroke, double minLength = 0.0)
        {
            removeEqualPoints(stroke, 5);

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

            double length = stroke.getLength();

            if (this.contains(centerX, centerY, Extension) && (stroke.Points.Count - stroke.getNumberOfFailedCoords()) > 4 && length > minLength )
            {
                // remove failed coordinates at the beginning and the end of this stroke
                removeFailedCoordinates(stroke.Points);
                Strokes.Add(stroke);
                return true;
            }
            else
                return false;
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

        /// <summary>
        /// splits all strokes in this region into different sequences and calculates the pattern likelihoods
        /// for each
        /// </summary>
        /// <param name="showEmptyRegions"> if true the pattern likelihoods for an empty region is also returned</param>
        /// <returns> list of <see cref="SequencingResult"/> sequencing results</returns>
        public List<SequencingResult> Recognize(bool showEmptyRegions = false)
        {
            if (!isInitialized)
            {
                lock (lockObj)
                {
                    if (!isInitialized)
                    {
                        string currentPath = Environment.GetEnvironmentVariable("path");
                        Environment.SetEnvironmentVariable("path", currentPath + ";" + Environment.CurrentDirectory);
                        string path = Environment.CurrentDirectory;

                        // initialize svm model
                        try
                        {
                            svm = libSVM.Load(path + "\\SVM_Model");
                            isInitialized = true;
                        }
                        catch
                        {
                            throw new Exception("Could not load " + path + "\\SVM_Model");
                        }

                        // load scale range
                        //try
                        //{
                        //    scaleRange = loadScaleRange(path + "\\scaleRange.txt");
                        //}
                        //catch
                        //{
                        //    throw new Exception("Could not load " + path + "\\scaleRange.txt");
                        //}
                    }
                }
            }

            List<SequencingResult> result = new List<SequencingResult>();

            if (Strokes.Count == 0 && showEmptyRegions)
            {
                SequencingResult emptyResult = new SequencingResult()
                {
                    SequenceNumber = "0",
                    StartTime = 0,
                    StopTime = 0,
                    CrossLikelihood = 0.0,
                    CrossedOutLikelihood = 0.0,
                    EllipseLikelihood = 0.0,
                    SlashLikelihood = 0.0,
                    BackSlashLikelihood = 0.0,
                };

                result.Add(emptyResult);
                return result;
            }
            else if (Strokes.Count > 0)
            {
                // get a rough sequencing of this regions strokes by the strokes IDs
                List<Sequence> Sequences = getRoughRegionSequencing();

                // count th number of sequences in this region
                int count = 0;

                // save the last sequence 
                List<Stroke> savedSequence = new List<Stroke>();
                SequencingResult savedResult = new SequencingResult();

                // bounding box of last sequence. Saved to check, if the following sequence
                // is drawn on top of this sequence.
                int top = 0;
                int left = 0;
                int bottom = 0;
                int right = 0;

                // gravity center of current sequence
                int gravityX = 0;
                int gravityY = 0;

                foreach (var roughSequence in Sequences)
                {
                    count++;
                    if (roughSequence.Strokes.Count == 1) // no further sequencing necessary
                    {
                        double label = 0.0;
                        SortedDictionary<int, double> likelihoods = getSequenceResult(roughSequence.Strokes, out label);

                        int numberOfPoints = roughSequence.Strokes[0].Points.Count;
                        SequencingResult oneStrokeResult = new SequencingResult()
                        {
                            SequenceNumber = count.ToString(),
                            StartTime = roughSequence.Strokes[0].StartTime,
                            StopTime = roughSequence.Strokes[0].StopTime,
                            CrossLikelihood = likelihoods[1],
                            CrossedOutLikelihood = likelihoods[2],
                            EllipseLikelihood = likelihoods[3],
                            SlashLikelihood = likelihoods[4],
                            BackSlashLikelihood = likelihoods[5],
                        };

                        // if sequence is slash or backslash, save the sequence and the result, to check
                        // if this will become a cross when combined with next rough sequence
                        if (label == 4.0 || label == 5.0) 
                        {
                            if (savedSequence.Count > 0)
                            {
                                count--;
                                savedSequence.AddRange(roughSequence.Strokes);
                                likelihoods = getSequenceResult(savedSequence, out label);
                                numberOfPoints = savedSequence[savedSequence.Count - 1].Points.Count;
                                SequencingResult newStrokeResult = new SequencingResult()
                                {
                                    SequenceNumber = count.ToString(),
                                    StartTime = savedSequence[0].StartTime,
                                    StopTime = savedSequence[savedSequence.Count - 1].StopTime,
                                    CrossLikelihood = likelihoods[1],
                                    CrossedOutLikelihood = likelihoods[2],
                                    EllipseLikelihood = likelihoods[3],
                                    SlashLikelihood = likelihoods[4],
                                    BackSlashLikelihood = likelihoods[5],
                                };

                                result.Add(newStrokeResult);

                                savedSequence.Clear();
                            }
                            else if ( savedSequence.Count == 0 && Sequences.Count > 1)
                            {
                                savedSequence.AddRange(roughSequence.Strokes);
                                savedResult = oneStrokeResult;
                            }
                            else if (savedSequence.Count == 0 && Sequences.Count == 1)
                            {
                                result.Add( oneStrokeResult );
                            }
                        }
                        else // no slash or backslash
                        {
                            if (savedSequence.Count > 0)
                            {
                                result.Add(savedResult);
                                savedSequence.Clear();
                            }

                            if ( right - left > 0 && bottom - top > 0)
                            {
                                getSequenceGravityCenter(roughSequence.Strokes, out gravityX, out gravityY);
                                if( isInRect(top,left,bottom,right,gravityX,gravityY) )
                                    result.Add(oneStrokeResult);
                            }
                            else
                                result.Add(oneStrokeResult);
                        }

                    }

                    else if (roughSequence.Strokes.Count > 1) // a further sequencing might be necessary
                    {
                        bool finished = false;

                        List<Stroke> last = new List<Stroke>();

                        while (!finished)
                        {
                            last.Add(roughSequence.Strokes[roughSequence.Strokes.Count - 1]);
                            roughSequence.Strokes.RemoveAt(roughSequence.Strokes.Count - 1);
                            double labelLast = 0.0;
                            SortedDictionary<int, double> lastLikelihoods = getSequenceResult(last, out labelLast);

                            // check the probabilty of the predicted label
                            int predictedKey = (int)labelLast;
                            // if predicted probability for this label is to small, set label to an invalid value
                            if (lastLikelihoods[predictedKey] < 85.0 && roughSequence.Strokes.Count > 0 )
                                predictedKey = 10;

                            double labelFirst = 0.0;
                            SortedDictionary<int, double> firstLikelihoods = new SortedDictionary<int, double>();

                            switch (predictedKey)
                            {
                                case 3:
                                case 2:
                                    if (roughSequence.Strokes.Count > 0)
                                    {
                                        firstLikelihoods = getSequenceResult(roughSequence.Strokes, out labelFirst);
                                        if (labelFirst == labelLast)
                                        {
                                            SequencingResult firstResult = new SequencingResult()
                                            {
                                                SequenceNumber = count.ToString(),
                                                StartTime = roughSequence.Strokes[0].StartTime,
                                                StopTime = last[last.Count - 1].StopTime,
                                                CrossLikelihood = firstLikelihoods[1],
                                                CrossedOutLikelihood = firstLikelihoods[2],
                                                EllipseLikelihood = firstLikelihoods[3],
                                                SlashLikelihood = firstLikelihoods[4],
                                                BackSlashLikelihood = firstLikelihoods[5],
                                            };

                                            result.Add(firstResult);
                                        }
                                        else
                                        {
                                            SequencingResult firstResult = new SequencingResult()
                                            {
                                                SequenceNumber = count.ToString(),
                                                StartTime = roughSequence.Strokes[0].StartTime,
                                                StopTime = roughSequence.Strokes[roughSequence.Strokes.Count - 1].StopTime,
                                                CrossLikelihood = firstLikelihoods[1],
                                                CrossedOutLikelihood = firstLikelihoods[2],
                                                EllipseLikelihood = firstLikelihoods[3],
                                                SlashLikelihood = firstLikelihoods[4],
                                                BackSlashLikelihood = firstLikelihoods[5],
                                            };

                                            result.Add(firstResult);
                                            count++;

                                            // get gravity center of last sequence
                                            getSequenceGravityCenter(last, out gravityX, out gravityY);

                                            // get Bounding Box of first sequence
                                            //int top = 0;
                                            //int left = 0;
                                            //int bottom = 0;
                                            //int right = 0;
                                            getSequenceBoundingBox(roughSequence.Strokes, out top, out left, out bottom, out right);

                                            if( isInRect(top, left, bottom, right, gravityX, gravityY) )
                                            {
                                                SequencingResult lastResult = new SequencingResult()
                                                {
                                                    SequenceNumber = (count).ToString(),
                                                    StartTime = last[0].StartTime,
                                                    StopTime = last[last.Count - 1].StopTime,
                                                    CrossLikelihood = lastLikelihoods[1],
                                                    CrossedOutLikelihood = lastLikelihoods[2],
                                                    EllipseLikelihood = lastLikelihoods[3],
                                                    SlashLikelihood = lastLikelihoods[4],
                                                    BackSlashLikelihood = lastLikelihoods[5],
                                                };

                                                result.Add(lastResult);
                                            }
                                        }
                                    }
                                    else
                                    {

                                        SequencingResult lastResult = new SequencingResult()
                                        {
                                            SequenceNumber = count.ToString(),
                                            StartTime = last[0].StartTime,
                                            StopTime = last[last.Count - 1].StopTime,
                                            CrossLikelihood = lastLikelihoods[1],
                                            CrossedOutLikelihood = lastLikelihoods[2],
                                            EllipseLikelihood = lastLikelihoods[3],
                                            SlashLikelihood = lastLikelihoods[4],
                                            BackSlashLikelihood = lastLikelihoods[5],
                                        };

                                        result.Add(lastResult);
                                    }
                                    finished = true;
                                    break;
                                case 1:
                                    if (roughSequence.Strokes.Count > 0)
                                    {
                                        firstLikelihoods = getSequenceResult(roughSequence.Strokes, out labelFirst);
                                        int castedLabel = (int)labelFirst;
                                        if (castedLabel == 1 || castedLabel == 4 || castedLabel == 5)
                                        {
                                            List<Stroke> combinedSequence = new List<Stroke>();
                                            combinedSequence.AddRange(roughSequence.Strokes);
                                            combinedSequence.AddRange(last);
                                            firstLikelihoods = getSequenceResult(combinedSequence, out labelFirst);

                                            SequencingResult combinedResult = new SequencingResult()
                                            {
                                                SequenceNumber = count.ToString(),
                                                StartTime = combinedSequence[0].StartTime,
                                                StopTime = combinedSequence[combinedSequence.Count - 1].StopTime,
                                                CrossLikelihood = firstLikelihoods[1],
                                                CrossedOutLikelihood = firstLikelihoods[2],
                                                EllipseLikelihood = firstLikelihoods[3],
                                                SlashLikelihood = firstLikelihoods[4],
                                                BackSlashLikelihood = firstLikelihoods[5],
                                            };

                                            result.Add(combinedResult);

                                            // save last sequences bounding box
                                            getSequenceBoundingBox(last, out top, out left, out bottom, out right);
                                        }
                                        else
                                        {
                                            SequencingResult firstResult = new SequencingResult()
                                            {
                                                SequenceNumber = count.ToString(),
                                                StartTime = roughSequence.Strokes[0].StartTime,
                                                StopTime = roughSequence.Strokes[roughSequence.Strokes.Count - 1].StopTime,
                                                CrossLikelihood = firstLikelihoods[1],
                                                CrossedOutLikelihood = firstLikelihoods[2],
                                                EllipseLikelihood = firstLikelihoods[3],
                                                SlashLikelihood = firstLikelihoods[4],
                                                BackSlashLikelihood = firstLikelihoods[5],
                                            };

                                            result.Add(firstResult);
                                            count++;

                                            SequencingResult lastResult = new SequencingResult()
                                            {
                                                SequenceNumber = (count).ToString(),
                                                StartTime = last[0].StartTime,
                                                StopTime = last[last.Count - 1].StopTime,
                                                CrossLikelihood = lastLikelihoods[1],
                                                CrossedOutLikelihood = lastLikelihoods[2],
                                                EllipseLikelihood = lastLikelihoods[3],
                                                SlashLikelihood = lastLikelihoods[4],
                                                BackSlashLikelihood = lastLikelihoods[5],
                                            };

                                            result.Add(lastResult);

                                            // save last sequences bounding box
                                            getSequenceBoundingBox(last, out top, out left, out bottom, out right);
                                        }
                                    }
                                    else
                                    {

                                        SequencingResult lastResult = new SequencingResult()
                                        {
                                            SequenceNumber = count.ToString(),
                                            StartTime = last[0].StartTime,
                                            StopTime = last[last.Count - 1].StopTime,
                                            CrossLikelihood = lastLikelihoods[1],
                                            CrossedOutLikelihood = lastLikelihoods[2],
                                            EllipseLikelihood = lastLikelihoods[3],
                                            SlashLikelihood = lastLikelihoods[4],
                                            BackSlashLikelihood = lastLikelihoods[5],
                                        };

                                        result.Add(lastResult);

                                        // save last sequences bounding box
                                        getSequenceBoundingBox(last, out top, out left, out bottom, out right);
                                    }
                                    finished = true;
                                    break;
                                case 4:
                                case 5:
                                    if (roughSequence.Strokes.Count == 0)
                                    {
                                        SequencingResult lastResult = new SequencingResult()
                                        {
                                            SequenceNumber = count.ToString(),
                                            StartTime = last[0].StartTime,
                                            StopTime = last[last.Count - 1].StopTime,
                                            CrossLikelihood = lastLikelihoods[1],
                                            CrossedOutLikelihood = lastLikelihoods[2],
                                            EllipseLikelihood = lastLikelihoods[3],
                                            SlashLikelihood = lastLikelihoods[4],
                                            BackSlashLikelihood = lastLikelihoods[5],
                                        };

                                        if (right - left > 0 && bottom - top > 0)
                                        {
                                            getSequenceGravityCenter(last, out gravityX, out gravityY);
                                            if (isInRect(top, left, bottom, right, gravityX, gravityY))
                                            {
                                                result.Add(lastResult);
                                                // save last sequences bounding box
                                                getSequenceBoundingBox(last, out top, out left, out bottom, out right);
                                            }
                                        }
                                        else
                                        {
                                            result.Add(lastResult);
                                            // save last sequences bounding box
                                            getSequenceBoundingBox(last, out top, out left, out bottom, out right);
                                        }

                                        finished = true;
                                    }
                                    break;
                                case 10:
                                    break;
                                default:
                                    // should not come here
                                    finished = true;
                                    throw new Exception("Predicted pattern is not in predetermined list");
                                    //break;
                            } //switch predicted label

                        } // while !finished

                    } // else if current rough sequence contains more then one stroke

                } // foreach rough sequence in this region

            } // if this region contains more then one stroke

            return result;
        }

        /// <summary>
        /// calucaltes the predicted pattern and probability estimates for this sequence 
        /// </summary>
        /// <param name="sequence">the current sequence</param>
        /// <param name="label"> the predicted pattern</param>
        /// <returns> probability estimates. For each pattern a probability is predicted. Patterns are defined by
        /// a label</returns>
        private SortedDictionary<int, double> getSequenceResult(List<Stroke> sequence, out double label)
        {
            SortedDictionary<int, double> likelihoods = new SortedDictionary<int, double>();

            string path = Environment.CurrentDirectory;

            List<double> featureSet = extractFeatureSet(sequence);
            //write features in sorted dictionary
            SortedDictionary<int, double> problem = new SortedDictionary<int, double>();
            for (int i = 0; i < featureSet.Count; i++)
            {
                int currentIdx = i + 1;
                problem.Add(currentIdx, featureSet[i]);
            }

            scaleProblem(problem, scaleRange);           

            // call svm.Predict_Probability at this point to get probabilty estimates
            double[] probability_estimates = new double[5];
            label = svm.Predict_Probability(problem, ref probability_estimates);

            // write probability estimates in dictionary
            for (int i = 0; i < probability_estimates.Length; i++)
            {
                int key = i + 1;
                double value = Math.Round(probability_estimates[i] * 100, 2);
                likelihoods.Add(key, value);
            }

            return likelihoods;
        }

        #region Preprocessing
        /// <summary>
        /// splits the list of strokes of this region into sequences by the strokes Ids. Strokes with
        /// consecutive Ids form out one sequence.
        /// </summary>
        /// <returns>list of <see cref="Sequence"/> sequences</returns>
        private List<Sequence> getRoughRegionSequencing()
        {
            List<Sequence> result = new List<Sequence>();

            byte lastId = this.Strokes[0].Id;
            Sequence sequence = new Sequence();

            foreach (var stroke in this.Strokes)
            {
                byte currentId = stroke.Id;
                if (currentId - lastId == 1 || currentId - lastId == 0)
                {
                    sequence.Strokes.Add(stroke);
                    lastId = currentId;
                }
                else if (System.Math.Abs(currentId - lastId) > 1)
                {
                    result.Add(sequence);
                    sequence = new Sequence();
                    sequence.Strokes.Add(stroke);
                    lastId = currentId;
                }
            }

            result.Add(sequence);
            return result;
        }

        /// <summary>
        /// given sequence is rescaled and smoothed
        /// </summary>
        /// <param name="strokes"> list of <see cref="Stroke"/> objects.</param>
        /// <returns> list of preprocessed <see cref="Stroke"/> objects</returns>
        private List<Stroke> preprocess(List<Stroke> strokes)
        {
            
            // size normalisation and centering
            List<Stroke> rescaledSequence = rescale(strokes);

            // smooth this sequence
            List<Stroke> smoothedSequence = smooth(rescaledSequence);
            List<Stroke> resampledSequence = new List<Stroke>();
            foreach (var stroke in smoothedSequence)
            {
                removeEqualPoints(stroke, 2);
                double length = stroke.getLength();
                double rate = System.Math.Ceiling(length / stroke.Points.Count);
                if (rate < 6)
                {
                    removeEqualPoints(stroke, 6);
                    rate = System.Math.Ceiling(length / stroke.Points.Count);
                }
                resampledSequence.Add(stroke);
            }

            return resampledSequence;
        }

        /// <summary>
        /// scales the given sequence to 100*100 pixel
        /// </summary>
        /// <param name="strokes"> list of <see cref="Stroke"/> objects</param>
        /// <returns> list of rescaled <see cref="Stroke"/> objects</returns>
        private List<Stroke> rescale(List<Stroke> strokes)
        {
            // calculate bounding box of this sequence
            Int32 left = strokes[0].Left;
            Int32 top = strokes[0].Top;
            Int32 right = strokes[0].Right;
            Int32 bottom = strokes[0].Bottom;

            foreach (var stroke in strokes)
            {
                left = Math.Min(left, stroke.Left);
                top = Math.Min(top, stroke.Top);
                right = Math.Max(right, stroke.Right);
                bottom = Math.Max(bottom, stroke.Bottom);
            }

            // get scale factor
            double scaleWidth = 100.0 / (double)(right - left);
            double scaleHeight = 100.0 / (double)(bottom - top);
            double scaleFactor = Math.Min(scaleWidth, scaleHeight);

            // rescale bounding box
            left = (Int32)(left*scaleFactor);
            top = (Int32)(top*scaleFactor);
            right = (Int32)(right*scaleFactor);
            bottom = (Int32)(bottom*scaleFactor);

            Point midpoint = new Point(right - (right - left) / 2, bottom - (bottom - top) / 2);
            int shiftX = midpoint.X - 50;
            int shiftY = midpoint.Y - 50;

            List<Stroke> rescaled = new List<Stroke>();
            // scale each point of this sequence
            foreach (var stroke in strokes)
            {
                Stroke rescaledStroke = new Stroke(stroke.Id,stroke.StartTime,stroke.StopTime);
                for (int i = 0; i < stroke.Points.Count; i++)
                {
                    Point rescaledPoint = stroke.Points[i];
                    if (!rescaledPoint.isFailedCoord())
                    {
                        rescaledPoint = stroke.Points[i] * scaleFactor;
                        rescaledPoint.X -= shiftX;
                        rescaledPoint.Y -= shiftY;
                    }
                    rescaledStroke.Points.Add(rescaledPoint);                    
                }
                rescaled.Add(rescaledStroke);
            }

            return rescaled;
            
        }

        /// <summary>
        /// Smooth the given sequence
        /// </summary>
        /// <param name="strokes"> list of <see cref="Stroke"/> objects</param>
        /// <returns> list of smoothed <see cref="Stroke"/> objects</returns>
        private List<Stroke> smooth(List<Stroke> strokes)
        {
            List<Stroke> smoothedSequence = new List<Stroke>();

            // replace raw coordinates by weighted sum of the neighbour points
            foreach (var stroke in strokes)
            {
                Stroke smoothedStroke = new Stroke(stroke.Id,stroke.StartTime,stroke.StopTime);
                smoothedStroke.Points.Add(stroke.Points[0]);
                for (int i = 1 ; i < stroke.Points.Count-1; i++)
                {
                    if (!stroke.Points[i - 1].isFailedCoord() && !stroke.Points[i].isFailedCoord() && !stroke.Points[i + 1].isFailedCoord())
                    {
                        Point newPoint = new Point();
                        newPoint.Time = stroke.Points[i].Time;
                        newPoint.X = (int)(0.25 * stroke.Points[i - 1].X + 0.5 * stroke.Points[i].X + 0.25 * stroke.Points[i + 1].X);
                        newPoint.Y = (int)(0.25 * stroke.Points[i - 1].Y + 0.5 * stroke.Points[i].Y + 0.25 * stroke.Points[i + 1].Y);
                        smoothedStroke.Points.Add(newPoint);
                    }
                    else
                    {
                        smoothedStroke.Points.Add(stroke.Points[i]);
                    }
                }
                smoothedStroke.Points.Add(stroke.Points[stroke.Points.Count-1]);
                smoothedSequence.Add(smoothedStroke);
            }

            return smoothedSequence;
        }

        /// <summary>
        /// removes points in one stroke that are to close together
        /// </summary>
        /// <param name="stroke">the <see cref="Stroke"/> object</param>
        /// <param name="minDist"> minimum distance betwwen two consecutive points</param>
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
                else if ( !first.isFailedCoord() || !second.isFailedCoord() )
                    dist =
                    System.Math.Sqrt(System.Math.Pow((first.X - second.X), 2) + System.Math.Pow((first.Y - second.Y), 2));

                if (dist < minDist) //TODO: Define propper value
                {
                    stroke.Points.RemoveAt(i);
                    --i;
                }
            }
            
        }
        #endregion


        #region csvExport
        // This is just for analyzing new features
        /// <summary>
        /// writes the extracted features in .csv file. Features will be extracted for all strokes in this region
        /// </summary>
        /// <param name="filename"></param>
        public void writeDataInCsv(string filename)
        {
            List<double> csvData = getDataForCsvExport();

            if (!File.Exists(filename))
            {
                //File.Create(filename);
                StreamWriter sw = new StreamWriter(filename);
                //write header
                var header = new List<string>{"Region","Varianz Winkelaenderung","WinkelDurchschnitt Grundlinie",
                                                "10-80","100-170","Black-Pixel-Anteil" };
                var sb = new StringBuilder();
                foreach (string field in header)
                {
                    if (sb.Length > 0)
                        sb.Append(";");

                    sb.Append(field);
                }

                sw.WriteLine(sb.ToString());
                sw.Close();
            }
            else if (File.Exists(filename))
            {
                //sbRow.AppendFormat("=\"{0}\"", row.Cells[i].Value.ToString());
                using (StreamWriter w = File.AppendText(filename))
                {
                    string entry = this.RegionName;
                    foreach (var value in csvData)
                    {
                        entry += ";" + value.ToString();
                    }
                    w.WriteLine(entry);
                    w.Close();
                }
            }
        }

        /// <summary>
        /// gets all features for the strokes in this region
        /// </summary>
        /// <returns> list of features</returns>
        private List<double> getDataForCsvExport()
        {
            List<double> dataForExport = new List<double>();

            dataForExport = extractFeatureSet(this.Strokes);

            return dataForExport;
        }

        #endregion

        #region Feature Extraction
        /// <summary>
        /// returns the arc length of this sequence
        /// </summary>
        /// <param name="sequence"> list of <see cref="Stroke"/> objects</param>
        /// <returns> the length of this sequence</returns>
        private double getLength(List<Stroke> sequence)
        {
            double length = 0.0;

            // combine all strokes of this sequence to one single stroke
            List<Point> allPoints = new List<Point>();
            foreach (var stroke in sequence)
            {
                allPoints.AddRange(stroke.Points);
            }

            int lastValidIndex = 0;
            for (int i = 1; i < allPoints.Count; i++)
            {
                Point first = allPoints[lastValidIndex];
                Point second = allPoints[i];

                if (!first.isFailedCoord() && !second.isFailedCoord())
                {
                    length +=
                        System.Math.Sqrt(System.Math.Pow((second.X - first.X), 2) + System.Math.Pow((second.Y - first.Y), 2));

                    lastValidIndex = i;
                }
                else if (first.isFailedCoord())
                {
                    lastValidIndex = second.isFailedCoord() ? i - 1 : i;
                }
            }

            return length;
        }

        /// <summary>
        /// calculates the variance of angle changes
        /// </summary>
        /// <param name="sequence"> list of <see cref="Stroke"/> objects</param>
        /// <returns> the variance of angle changes</returns>
        private double getAngleInformation(List<Stroke> sequence)
        {
            //List<double> data = new List<double>();

            // combine all strokes of this sequence to one single stroke
            List<Point> allPoints = new List<Point>();
            foreach (var stroke in sequence)
            {
                allPoints.AddRange(stroke.Points);
            }

            double summedAngle = 0.0;
            List<double> angleChanges = new List<double>();
            double previousAngle = 0.0;

            for (int i = 0; i < allPoints.Count - 2; ++i)
            {
                Point first = allPoints[i];
                Point second = allPoints[i + 1];
                Point third = allPoints[i + 2];

                if (!first.isFailedCoord() && !second.isFailedCoord() && !third.isFailedCoord())
                {
                    double nummerator = (first.X - second.X) * (third.X - second.X) + (first.Y - second.Y) * (third.Y - second.Y);
                    double denominator = System.Math.Sqrt(System.Math.Pow((first.X - second.X), 2) + System.Math.Pow((first.Y - second.Y), 2)) *
                                         System.Math.Sqrt(System.Math.Pow((third.X - second.X), 2) + System.Math.Pow((third.Y - second.Y), 2));


                    double fraction = nummerator / denominator;
                    if (fraction < -1)
                        fraction = -1;
                    else if (fraction > 1)
                        fraction = 1;

                    double currentAngle = Math.Acos(fraction);
                    double currentInDegree = currentAngle * 180 / Math.PI;
                    double currentChange = 0;
                    if (i > 0)
                    {
                        currentChange = Math.Abs(previousAngle - currentInDegree);
                    }

                    angleChanges.Add(currentChange);
                    previousAngle = currentInDegree;

                    summedAngle += currentInDegree;
                }
            }

            // calculate mean change of angle
            double meanChange = 0.0;
            foreach (var change in angleChanges)
            {
                meanChange += change;
            }

            if (angleChanges.Count == 0)
                angleChanges.Add(0.0);

            meanChange /= angleChanges.Count;

            // calculate variance of angle change
            double squareDeviation = 0.0;
            foreach (var angle in angleChanges)
            {
                squareDeviation += ((angle - meanChange) * (angle - meanChange));
            }

            double variance = squareDeviation / angleChanges.Count;
            //data.Add(variance);

            return variance;
            //return data;
        }

        /// <summary>
        /// calculates the mean base line angle and the rate of angles between 10° and 80° and the rate
        /// of angles between 100° and 170°.
        /// </summary>
        /// <param name="vectors"> list of stroke segments. 
        /// A stroke segment is the vector between to consecutive points in one sequence</param>
        /// <returns></returns>
        private List<double> getBaseLineAngles(List<Point> vectors)
        {
            List<double> angles = new List<double>();

            Point first = new Point(1, 0);
            Point second = new Point(0, 0);

            double meanBaseLineAngle = 0;

            double count_10_80 = 0;
            double count_100_170 = 0;

            List<double> allAngles = new List<double>();

            for (int i = 0; i < vectors.Count; i++)
            {
                Point third = vectors[i];

                // change direction of current vector if necessary
                if (Math.Sign(third.X) != Math.Sign(third.Y) && Math.Sign(third.X) < 0 && !(third.Y == 0))
                    third *= (-1);
                else if(Math.Sign(third.X) == Math.Sign(third.Y) && Math.Sign(third.X) > 0 && !(third.Y == 0))
                    third *= (-1);

                // calculate current baseline angle
                double nummerator = (first.X - second.X) * (third.X - second.X) + (first.Y - second.Y) * (third.Y - second.Y);
                double denominator = System.Math.Sqrt(System.Math.Pow((first.X - second.X), 2) + System.Math.Pow((first.Y - second.Y), 2)) *
                                     System.Math.Sqrt(System.Math.Pow((third.X - second.X), 2) + System.Math.Pow((third.Y - second.Y), 2));


                double fraction = nummerator / denominator;
                if (fraction < -1)
                    fraction = -1;
                else if (fraction > 1)
                    fraction = 1;

                double currentAngle = Math.Acos(fraction);
                double currentInDegree = currentAngle * 180 / Math.PI;

                if (10.0 <= currentInDegree && currentInDegree <= 80.0)
                    count_10_80++;
                else if (100.0 <= currentInDegree && currentInDegree <= 170.0)
                    count_100_170++;

                allAngles.Add(currentInDegree);

                meanBaseLineAngle += currentInDegree;
                
            }

            count_10_80 /= vectors.Count;
            count_100_170 /= vectors.Count;
            meanBaseLineAngle /= vectors.Count;

            angles.Add(meanBaseLineAngle);
            angles.Add(count_10_80 * 100);
            angles.Add(count_100_170 * 100);

            return angles;
        }

        /// <summary>
        /// calculates all directional vectors in this sequence
        /// </summary>
        /// <param name="segment">list of <see cref="Stroke"/> objects</param>
        /// <returns> list of all directional vectors in this sequence</returns>
        private List<Point> getVectorsOfSegment(List<Stroke> sequence)
        {
            List<Point> vectors = new List<Point>();

            // merge all points in this sequence
            List<Point> allPoints = new List<Point>();
            foreach (var stroke in sequence)
            {
                allPoints.AddRange(stroke.Points);
            }

            for (int i = 1; i < allPoints.Count; i++)
            {
                Point first = allPoints[i-1];
                Point second = allPoints[i];

                if (!first.isFailedCoord() && !second.isFailedCoord())
                {
                    Point current = new Point(allPoints[i].X - allPoints[i - 1].X, allPoints[i].Y - allPoints[i - 1].Y);
                    vectors.Add(current);
                }
            }

            return vectors;
        }

        /// <summary>
        /// removes failed coordinates at the beginning and the end of one stroke
        /// </summary>
        /// <param name="stroke">list of <see cref="Point"/> objects</param>
        /// <returns></returns>
        private void removeFailedCoordinates(List<Point> stroke)
        {
            // remove failed coordinates at the beginning
            for (int i = 0; i < stroke.Count && i == 0; i++)
            {
                if (i == 0 && stroke[i].isFailedCoord())
                {
                    stroke.RemoveAt(i);
                    i--;
                }
            }

            // remove failed coordinates at the end
            for (int i = stroke.Count - 1; i > 0; i--)
            {
                if (i == stroke.Count - 1 && stroke[i].isFailedCoord())
                {
                    stroke.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// calculates the rate of black pixels in an calculated rectangle
        /// </summary>
        /// <param name="sequence">list of <see cref="Stroke"/> objects</param>
        /// <returns> the black pixel rate</returns>
        private double getBlackPixelRate(List<Stroke> sequence)
        {
            int gravityX = 0;
            int gravityY = 0;
            getSequenceGravityCenter(sequence, out gravityX, out gravityY);

            //calculate the mean dist of all points in this sequence to gravity center.
            int meanDist = (int)getMeanDistToGravityCenter(sequence,gravityX,gravityY);

            // determine the width and height of the bitmap
            int width = /*80*/ 2*meanDist;
            width -= width / 100 * 20;
            int height = width;

            Bitmap bmp = new Bitmap(width, height);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.FillRectangle(Brushes.White, 0, 0, bmp.Height, bmp.Width);
            }

            // calculate the pen width
            double penWidth = (width*8) / 100;
            Pen pen = new Pen(Color.FromArgb(255, 0, 0, 0), (int)penWidth);

            gravityX -= width / 2;
            gravityY -= height / 2;

            // paint the strokes in this bitmap
            foreach (var stroke in sequence)
            {
                for (int i = 1; i < stroke.Points.Count; i++)
                {
                    Point first = stroke.Points[i - 1];
                    Point second = stroke.Points[i];

                    if (!first.isFailedCoord() && !second.isFailedCoord())
                    {
                        int firstX = first.X - gravityX;
                        int firstY = first.Y - gravityY;
                        int secondX = second.X - gravityX;
                        int secondY = second.Y - gravityY;

                        if (isInRect(width, height, firstX, firstY) && isInRect(width, height, secondX, secondY))
                        {
                            PointF start = new PointF(firstX, firstY);
                            PointF end = new PointF(secondX, secondY);
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                g.DrawLine(pen, start, end);
                            }
                        }
                        else // calculate intersection point of stroke segment with all edges
                        {
                            // points or stroke segment
                            Point l2p1 = new Point(firstX, firstY);
                            Point l2p2 = new Point(secondX, secondY);

                            // intersection with left vertical edge
                            Point l1p1 = new Point(0, 0);
                            Point l1p2 = new Point(0, height-1);
                            calculateLineIntersection(l1p1, l1p2, ref l2p1, ref l2p2);

                            // intersection with right vertical edge
                            l1p1.X = width-1;
                            l1p2.X = width-1;
                            calculateLineIntersection(l1p1, l1p2, ref l2p1, ref l2p2);

                            //intersection with upper horizontal ege
                            l1p1.X = 0;
                            l1p1.Y = 0;
                            l1p2.X = width-1;
                            l1p2.Y = 0;
                            calculateLineIntersection(l1p1, l1p2, ref l2p1, ref l2p2);

                            // intersection with lower horizontal edge
                            l1p1.Y = height-1;
                            l1p2.Y = height-1;
                            calculateLineIntersection(l1p1, l1p2, ref l2p1, ref l2p2);

                            // check again, if new calculated points of stroke segment lies 
                            // within the rectangle
                            if (isInRect(width, height, l2p1.X, l2p1.Y) && isInRect(width, height, l2p2.X, l2p2.Y))
                            {
                                PointF start = new PointF(l2p1.X, l2p1.Y);
                                PointF end = new PointF(l2p2.X, l2p2.Y);
                                using (Graphics g = Graphics.FromImage(bmp))
                                {
                                    g.DrawLine(pen, start, end);
                                }
                            }

                        } // if at least one point of stroke segment lies outside the rectangle
                    }  // if both points are valid coordinates                
                    
                } // foreach point in this stroke
            } // foreach stroke

            double rate = CountPixels(bmp, Color.FromArgb(255, 0, 0, 0)) * 100 / (bmp.Width * bmp.Height);

            bmp.Dispose();

            return rate;
        }

        /// <summary>
        /// calculates the center of gravity
        /// </summary>
        /// <param name="sequence"></param>
        /// <param name="x"> x coordinate of center</param>
        /// <param name="y"> y coordinate of center</param>
        private void getSequenceGravityCenter(List<Stroke> sequence, out int x, out int y)
        {
            x = 0;
            y = 0;
            int nrValidPoints = 0;
            foreach (var stroke in sequence)
            {
                foreach (var point in stroke.Points)
                {
                    if (!point.isFailedCoord())
                    {
                        x += point.X;
                        y += point.Y;
                        nrValidPoints++;
                    }
                }
            }

            x /= nrValidPoints;
            y /= nrValidPoints;
        }

        private void getSequenceBoundingBox(List<Stroke> sequence, out int top, out int left, out int bottom, out int right)
        {
            top = sequence[0].Top;
            left = sequence[0].Left;
            bottom = sequence[0].Bottom;
            right = sequence[0].Right;

            //calculate bounding box of all strokes
            foreach (var stroke in sequence)
            {
                top = Math.Min(top, stroke.Top);
                left = Math.Min(left, stroke.Left);
                bottom = Math.Max(bottom, stroke.Bottom);
                right = Math.Max(right, stroke.Right);
            }
        }

        private double getMeanDistToGravityCenter(List<Stroke> sequence,int gravityX, int gravityY)
        {
            double meanDist = 0.0;
            int validPoints = 0;

            foreach (var stroke in sequence)
            {
                foreach (var point in stroke.Points)
                {
                    if (!point.isFailedCoord())
                    {
                        meanDist += Math.Sqrt(Math.Pow(gravityX - point.X, 2) + Math.Pow(gravityY - point.Y, 2));
                        validPoints++;
                    }
                }
            }

            meanDist /= validPoints;

            return meanDist;
        }

        /// <summary>
        /// checks if coordinate lies within the rectangle given by width and height
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="x"> x coordinate to check</param>
        /// <param name="y"> y coordinate to check</param>
        /// <returns> true if point lies within given rectangle</returns>
        private bool isInRect(int width, int height, int x, int y)
        {
            return x >= 0 && y >= 0 && x < width && y < height;
        }

        private bool isInRect(int Top, int Left, int Bottom, int Right, int x, int y)
        {
            return x >= Left && y >= Top && x <= Right && y <= Bottom;
        }

        /// <summary>
        /// calculates the intersection point of two line segments, line one is edge of rect
        /// second line is a stroke segment.
        /// </summary>
        /// <param name="l1p1"> point 1 of line 1</param>
        /// <param name="l1p2"> point 2 of line 1</param>
        /// <param name="l2p1"> point 1 of line 2</param>
        /// <param name="l2p2"> point 2 of line 2</param>
        private void calculateLineIntersection(Point l1p1, Point l1p2, ref Point l2p1, ref Point l2p2)
        {
            // rectangle
            double A1 = l1p2.Y - l1p1.Y;
            double B1 = l1p1.X - l1p2.X;
            double C1 = A1 * l1p1.X + B1 * l1p1.Y;

            //stroke segment
            double A2 = l2p2.Y - l2p1.Y;
            double B2 = l2p1.X - l2p2.X;
            double C2 = A2 * l2p1.X + B2 * l2p1.Y;

            double det = A1 * B2 - A2 * B1;
            if( det == 0)
                return;

            double x = (B2 * C1 - B1 * C2) / det;
            double y = (A1 * C2 - A2 * C1) / det;

            // check if point of intersection lies on rectangle edge and stroke segment
            if ((Math.Min(l1p1.X, l1p2.X) <= x && x <= Math.Max(l1p1.X, l1p2.X)) &&
                (Math.Min(l1p1.Y, l1p2.Y) <= x && y <= Math.Max(l1p1.Y, l1p2.Y)) &&
                (Math.Min(l2p1.X, l2p2.X) <= x && x <= Math.Max(l2p1.X, l2p2.X)) &&
                (Math.Min(l2p1.Y, l2p2.Y) <= y && y <= Math.Max(l2p1.Y, l2p2.Y)))
            {
                // check which point of stroke segment should be moved
                if (l1p1.X == l1p2.X && l1p1.X == 0) // left vertical rectangle edge
                {
                    // move point with smaller x coordinate
                    if (l2p1.X < l2p2.X)
                    {
                        l2p1.X = (int)x;
                        l2p1.Y = (int)y;
                    }
                    else
                    {
                        l2p2.X = (int)x;
                        l2p2.Y = (int)y;
                    }
                }

                if (l1p1.X == l1p2.X && l1p1.X > 0) // right vertical rectangle edge
                {
                    // move point with bigger x coordinate
                    if (l2p1.X > l2p2.X)
                    {
                        l2p1.X = (int)x;
                        l2p1.Y = (int)y;
                    }
                    else
                    {
                        l2p2.X = (int)x;
                        l2p2.Y = (int)y;
                    }
                }

                if (l1p1.Y == l1p2.Y && l1p1.Y == 0) // upper horizontal rectangle edge
                {
                    // move point with smaller x coordinate
                    if (l2p1.Y < l2p2.Y)
                    {
                        l2p1.X = (int)x;
                        l2p1.Y = (int)y;
                    }
                    else
                    {
                        l2p2.X = (int)x;
                        l2p2.Y = (int)y;
                    }
                }

                if (l1p1.Y == l1p2.Y && l1p1.Y > 0) // lower horizontal rectangle edge
                {
                    // move point with bigger x coordinate
                    if (l2p1.Y > l2p2.Y)
                    {
                        l2p1.X = (int)x;
                        l2p1.Y = (int)y;
                    }
                    else
                    {
                        l2p2.X = (int)x;
                        l2p2.Y = (int)y;
                    }
                }
            }
        }

        /// <summary>
        ///  Returns the number of matching pixels.
        /// </summary>
        /// <param name="bm"></param>
        /// <param name="target_color"></param>
        /// <returns></returns>
        private double CountPixels(Bitmap bmp, Color target_color)
        {
            // Loop through the pixels.
            double matches = 0.0;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y) == target_color)
                        matches++;
                }
            }

            return matches;
        }

        private List<double> extractFeatureSet(List<Stroke> sequence)
        {
            //double rate = getBlackPixelRate(sequence);

            List<double> featureSet = new List<double>();
            List<Stroke> preprocessedSequence = preprocess(sequence);
            double rate = getBlackPixelRate(preprocessedSequence);
            featureSet.Add(getAngleInformation(preprocessedSequence));

            List<Point> vectors = getVectorsOfSegment(preprocessedSequence);
            featureSet.AddRange(getBaseLineAngles(vectors));
            featureSet.Add(rate);

            return featureSet;
        }

        #endregion


        /// <summary>
        /// scales a LibSVM problem using given scale Range
        /// </summary>
        /// <param name="problem"> problem in LibSVM format: label index:value index:value...</param>
        /// <param name="scaleRange"> list of scale values. scaleRange.Count >= max index</param>
        private void scaleLibSVMProblem(libSVM_Problem problem, List<double> scaleRange)
        {
            for (int i = 0; i < scaleRange.Count; i++)
            {
                int currentKey = i + 1;
                double currentScale = scaleRange[i];

                foreach (var dictionary in problem.samples)
                {
                    if( dictionary.ContainsKey(currentKey))
                        dictionary[currentKey] /= currentScale;
                }
            }
        }

        /// <summary>
        /// scales problem using scale range
        /// </summary>
        /// <param name="problem"> problem given in dictionary format. Key == index, Value == value</param>
        /// <param name="scaleRange"> list of scale values. scaleRange.Count >= maxKey</param>
        private void scaleProblem(SortedDictionary<int,double> problem, List<double> scaleRange)
        {
            for (int i = 0; i < scaleRange.Count; i++)
            {
                int currentKey = i + 1;
                double currentScale = scaleRange[i];

                if (problem.ContainsKey(currentKey))
                    problem[currentKey] /= currentScale;
            }
        }

        /// <summary>
        /// loads a given scale Range in txt format
        /// </summary>
        /// <param name="path"> path</param>
        /// <returns>list of scale values</returns>
        private List<double> loadScaleRange(string path)
        {
            List<double> ranges = new List<double>();

            if (!File.Exists(path)) throw new Exception("file " + path + " not existing!");
 
            StreamReader sr = new StreamReader(File.Open(path, FileMode.Open));

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine().Trim();
                if (!line.Equals(""))
                 ranges.Add(Convert.ToDouble(line,CultureInfo.InvariantCulture));
            }

            sr.Close();

            return ranges;
        }

        #region Training and Testing
        /// <summary>
        /// crates a problem in LibSVM Format: label index:value index:value index:value ...
        /// </summary>
        /// <param name="sequence"> list of strokes</param>
        /// <param name="filename"> full filename</param>
        /// <param name="label"> label of current sequence</param>
        public void createLibSVMProblem(List<Stroke> sequence, string filename, int label = 0)
        {
            string path = Environment.CurrentDirectory;

            // extract features for Feature Set 
            List<double> featureSet = extractFeatureSet(sequence);
            writeLibSVMDataInFile(featureSet, path + "\\" + filename + ".dat", label);

        }

        /// <summary>
        /// save data in a file in LibSVM format.
        /// </summary>
        /// <param name="data">feature set</param>
        /// <param name="filename"> full filename</param>
        /// <param name="label">label of sequence; label = 0 if unknown;</param>
        private void writeLibSVMDataInFile(List<double> data, string filename, int label = 0)
        {
            if (!File.Exists(filename))
            {
                StreamWriter sw = new StreamWriter(filename);
                sw.Write(label.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < data.Count; i++)
                {
                    int currentIndex = i + 1;
                    sw.Write(" " + currentIndex + ":" + data[i].ToString(CultureInfo.InvariantCulture));
                }
                sw.WriteLine("");
                sw.Close();
            }
            else if (File.Exists(filename))
            {
                using (StreamWriter sw = File.AppendText(filename))
                {
                    sw.Write(label.ToString(CultureInfo.InvariantCulture));
                    for (int i = 0; i < data.Count; i++)
                    {
                        int currentIndex = i + 1;
                        sw.Write(" " + currentIndex + ":" + data[i].ToString(CultureInfo.InvariantCulture));
                    }
                    sw.WriteLine("");
                    sw.Close();
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void trainSVMModel(string filename)
        {
            string path = Environment.CurrentDirectory;
            if (!isInitialized)
            {
                lock (lockObj)
                {
                    if (!isInitialized)
                    {
                        isInitialized = true;
                        string currentPath = Environment.GetEnvironmentVariable("path");
                        Environment.SetEnvironmentVariable("path", currentPath + ";" + Environment.CurrentDirectory);
                    }
                }
            }

            //libSVM svm = new libSVM();
            libSVM_Parameter parameter = new libSVM_Parameter();
            parameter.svm_type = SVM_TYPE.C_SVC;
            parameter.kernel_type = KERNEL_TYPE.RBF;
            parameter.probability = true;
            libSVM_Problem problem = new libSVM_Problem();
            try
            {
                problem =
                    libSVM_Problem.Load(path + "\\" + filename + ".dat");
            }
            catch
            {
                throw new Exception("could not load " + path + "\\" + filename + ".dat.");
            }

            List<double> scaleRange = 
                calculateScaleRange(problem, path + "\\scaleRange.txt");
            scaleLibSVMProblem(problem, scaleRange);
            svm.TrainAuto(10, problem, parameter);

            if (File.Exists(path + "\\SVM_Model"))
                File.Delete(path + "\\SVM_Model");

            svm.Save(path + "\\SVM_Model");
            svm.Dispose();

            File.Delete(path + "\\" + filename + ".dat");
        }

        public void testSVMModel(string filename)
        {
            string path = Environment.CurrentDirectory;

            if (!isInitialized)
            {
                lock (lockObj)
                {
                    if (!isInitialized)
                    {
                        isInitialized = true;
                        string currentPath = Environment.GetEnvironmentVariable("path");
                        Environment.SetEnvironmentVariable("path", currentPath + ";" + Environment.CurrentDirectory);
                        // initialize svm model
                        try
                        {
                            svm = libSVM.Load(path + "\\SVM_Model");
                        }
                        catch
                        {
                            throw new Exception("Could not load " + path + "\\SVM_Model");
                        }

                        // load scale range
                        //try
                        //{
                        //    scaleRange = loadScaleRange(path + "\\scaleRange.txt");
                        //}
                        //catch
                        //{
                        //    throw new Exception("Could not load " + path + "\\scaleRange.txt");
                        //}
                    }
                }
            }

            libSVM_Problem problem = new libSVM_Problem();
            try
            {
                problem = libSVM_Problem.Load(path + "\\" + filename + ".dat");
            }
            catch
            {
                throw new Exception("Could not load " + path + "\\" + filename + ".dat");
            }

            scaleLibSVMProblem(problem, scaleRange);

            double accuracy_model = svm.GetAccuracy(problem);
            foreach (var sample in problem.samples)
            {
                double label = svm.Predict(sample);
            }

            svm.Dispose();
        }

        /// <summary>
        /// calculates the appropriate scale range to a given libSVM problem. The scale range is once 
        /// calculated on the training data set and then used for all further problems. 
        /// See libSVM documentatin for more informations
        /// </summary>
        /// <param name="problem">libSVM problem</param>
        /// <param name="path">save path</param>
        /// <returns> list of scale values</returns>
        private List<double> calculateScaleRange(libSVM_Problem problem, string path)
        {
            List<double> ranges = new List<double>();

            for (int i = 0; i < problem.samples.Length; i++)
            {
                List<double> currentValues = new List<double>();

                foreach (var item in problem.samples[i])
                {
                    currentValues.Add(Math.Abs(item.Value));
                }

                if (ranges.Count == 0)
                    ranges.AddRange(currentValues);
                else if (ranges.Count > 0)
                {
                    for (int j = 0; j < currentValues.Count; j++)
                    {
                        if (j < ranges.Count)
                            ranges[j] = Math.Max(ranges[j], Math.Abs(currentValues[j]));
                        else if (j >= ranges.Count)
                            ranges.Add(currentValues[j]);
                    }
                }

            }

            // save scale ranges to file
            if( File.Exists(path))
                File.Delete(path);

            StreamWriter sw = new StreamWriter(path);
            for (int i = 0; i < ranges.Count; i++)
            {
                sw.WriteLine(ranges[i].ToString(CultureInfo.InvariantCulture));
            }

            sw.WriteLine("");
            sw.Close();

            return ranges;

        }
        #endregion

        #region Constructor
        public PatternRegion()
        {
            Strokes = new List<Stroke>();
        }

        public PatternRegion(Int32 left, Int32 top, Int32 right, Int32 bottom, int extension, 
                       string regionName = "")
        {
            Strokes = new List<Stroke>();
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            Extension = extension;
            RegionName = regionName;
        }

        public PatternRegion(Int32 left, Int32 top, Int32 right, Int32 bottom, int extension,
                          List<Stroke> strokes, string regionName = "")
        {
            Strokes = new List<Stroke>(strokes);
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            Extension = extension;
            RegionName = regionName;
        }

        public void Dispose()
        {
            this.Strokes.Clear();
        }
        #endregion
    }
}
