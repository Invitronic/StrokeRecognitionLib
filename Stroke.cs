using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StrokeRecognitionLib
{
    public struct Point
    {
        public ulong Time;
        public int X;
        public int Y;

        public bool isFailedCoord()
        {
            return (X == -1 || Y == -1);
        }

        public static Point operator *(Point point,double factor)
        {
            point.X = (Int32)(point.X*factor);
            point.Y = (Int32)(point.Y*factor);

            return point;
        }

        public static Point operator -(Point point1, Point point2)
        {
            Point point = new Point(point1.X - point2.X, point1.Y - point2.Y);

            return point;
        }

        public Point(int x, int y, ulong time = 0)
        {
            X = x;
            Y = y;
            Time = time;
        }

        public Point(ulong time)
        {
            X = -1;
            Y = -1;
            Time = time;
        }
    }

    public class Stroke : IDisposable
    {
        private List<Point> points = null;
        /// <summary>
        /// list of all <see cref="Point"/> objects belonging to this stroke
        /// </summary>
        public List<Point> Points
        {
            get
            {
                if (points == null)
                    points = new List<Point>();
                return points;
            }
            private set
            { 
                points = value;
            }
        }

        /// <summary>
        /// points are sorted in ascending order on the basis of their time stamp
        /// </summary>
        public void sortPoints()
        {
            Points = Points.OrderBy((e) => e.Time).ToList();
        }

        /// <summary>
        /// properties specifying the bounding rect of this stroke
        /// </summary>
        public Int32 Left { get; set; }
        public Int32 Top { get; set; }
        public Int32 Right { get; set; }
        public Int32 Bottom { get; set; }

        byte strokeId;

        public byte Id
        {
            get { return this.strokeId; }
            set { this.strokeId = value; }
        }

        public ulong StartTime { get; set; }
        public ulong StopTime { get; set; }

        /// <summary>
        /// adds a <see cref="Point"/> to the list of points
        /// </summary>
        /// <param name="point"></param>
        public void addPoint(Point point)
        {
            this.Points.Add(point);
        }

        /// <summary>
        /// calculates the length of his stroke
        /// </summary>
        /// <returns></returns>
        public double getLength()
        {
            double length = 0.0;

            int lastValidCoord = 0;
            for (int i = 1; i < Points.Count; i++)
            {
                Point first = Points[lastValidCoord];
                Point second = Points[i];

                if (!first.isFailedCoord() && !second.isFailedCoord())
                {
                    length +=
                        System.Math.Sqrt(System.Math.Pow((second.X - first.X), 2) + System.Math.Pow((second.Y - first.Y), 2));
                    lastValidCoord = i;
                }
                else if (first.isFailedCoord())
                {
                    lastValidCoord = second.isFailedCoord() ? i - 1 : i;
                }
            }
            return length;
        }

        /// <summary>
        /// counts the number of failed coordinates in this stroke
        /// </summary>
        /// <returns></returns>
        public int getNumberOfFailedCoords()
        {
            int nrFailed = 0;
            for (int i = 0; i < Points.Count; i++)
            {
                if (Points[i].isFailedCoord())
                    nrFailed++;
            }

            return nrFailed;
        }

        public Stroke(byte strokeId/*, int penId*/, ulong startTime, ulong stopTime, Int32 left,
                        Int32 top, Int32 right, Int32 bottom)
        {
            this.strokeId = strokeId;
            StartTime = startTime;
            StopTime = stopTime;
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            //this.penId = penId;
        }

        public Stroke(byte strokeId/*, int penId*/, ulong startTime, ulong stopTime, Int32 left,
                        Int32 top, Int32 right, Int32 bottom, List<Point> points)
        {
            this.Points = new List<Point>(points);
            this.strokeId = strokeId;
            StartTime = startTime;
            StopTime = stopTime;
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            //this.penId = penId;
        }

        public Stroke(byte strokeId/*, int penId*/, ulong startTime, ulong stopTime)
        {
            this.strokeId = strokeId;
            StartTime = startTime;
            StopTime = stopTime;
            //this.penId = penId;
        }

        public void Dispose()
        {
            this.Points.Clear();
        }

    }

}
