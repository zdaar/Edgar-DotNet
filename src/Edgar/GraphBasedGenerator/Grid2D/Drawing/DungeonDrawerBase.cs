using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Edgar.Geometry;
using Edgar.Legacy.GeneralAlgorithms.Algorithms.Polygons;

namespace Edgar.GraphBasedGenerator.Grid2D.Drawing
{
    /// <summary>
    /// Base class for drawing dungeons.
    /// </summary>
    public abstract class DungeonDrawerBase
    {
        protected readonly CachedPolygonPartitioning polygonPartitioning =
            new CachedPolygonPartitioning(new GridPolygonPartitioning());

        protected readonly Random random = new Random(0);

        protected Bitmap bitmap;
        protected Graphics graphics;

        protected void DrawGrid(PolygonGrid2D polygon)
        {
            var rectangles = polygonPartitioning.GetPartitions(polygon);
            using (var gridPen = new Pen(Color.FromArgb(100, 100, 100), 0.05f))
            {
                gridPen.DashStyle = DashStyle.Dash;
                gridPen.DashPattern = new float[] {1.2f, 3.1f};
                gridPen.DashOffset = 0.5f;

                // Draw per-rectangle continuous grid lines instead of
                // building a HashSet of all interior points and checking neighbors.
                // This reduces complexity from O(area) draw calls to O(width+height) per partition.
                foreach (var rectangle in rectangles)
                {
                    var xStart = rectangle.A.X;
                    var xEnd = rectangle.B.X;
                    var yStart = rectangle.A.Y;
                    var yEnd = rectangle.B.Y;

                    for (int x = xStart; x <= xEnd; x++)
                    {
                        graphics.DrawLine(gridPen, x, yStart, x, yEnd);
                    }

                    for (int y = yStart; y <= yEnd; y++)
                    {
                        graphics.DrawLine(gridPen, xStart, y, xEnd, y);
                    }
                }
            }
        }

        protected void DrawRoomBackground(PolygonGrid2D polygon, Color color)
        {
            var polyPoints = polygon.GetPoints().Select(point => new Point(point.X, point.Y)).ToList();

            graphics.FillPolygon(new SolidBrush(color), polyPoints.ToArray());
            graphics.DrawPolygon(new Pen(color, 0.1f), polyPoints.ToArray());
        }

        protected void DrawOutline(PolygonGrid2D polygon, List<OutlineSegment> outlineSegments, Pen outlinePen)
        {
            for (var i = 0; i < outlineSegments.Count; i++)
            {
                var current = outlineSegments[i];
                var previous = outlineSegments[Mod(i - 1, outlineSegments.Count)];
                var next = outlineSegments[Mod(i + 1, outlineSegments.Count)];

                outlinePen.StartCap = previous.IsDoor ? LineCap.Square : LineCap.Round;
                outlinePen.EndCap = next.IsDoor ? LineCap.Square : LineCap.Round;

                if (!current.IsDoor)
                {
                    var from = current.Line.From;
                    var to = current.Line.To;

                    graphics.DrawLine(outlinePen, from.X, from.Y, to.X, to.Y);
                }
            }
        }

        private int Mod(int x, int m)
        {
            return (x % m + m) % m;
        }

        protected void DrawHatching(PolygonGrid2D outline, List<Tuple<RectangleGrid2D, List<Vector2>>> usedPoints,
            Range<float> hatchingClusterOffset, Range<float> hatchingLength)
        {
            using (var pen = new Pen(Color.FromArgb(50, 50, 50), 0.05f))
            {
                // Build a fast lookup of already-used hatching anchor points.
                // The previous implementation scanned all prior rooms/points per candidate (O(n^2)).
                var usedPointKeys = new HashSet<long>();
                if (usedPoints.Count > 0)
                {
                    foreach (var entry in usedPoints)
                    {
                        var prior = entry.Item2;
                        if (prior == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < prior.Count; i++)
                        {
                            usedPointKeys.Add(QuantizeHatchingKey(prior[i]));
                        }
                    }
                }

                var usedPointsAdd = new List<Vector2>();

                foreach (var line in outline.GetLines())
                {
                    var direction = (Vector2) line.GetDirectionVector();
                    var directionPerpendicular = new Vector2(
                        Math.Max(-1, Math.Min(1, direction.Y)),
                        Math.Max(-1, Math.Min(1, direction.X))
                    );

                    if (direction.Y != 0)
                    {
                        directionPerpendicular = -1 * directionPerpendicular;
                    }

                    var points = line.GetPoints().Select(x => (Vector2) x).ToList();
                    points.AddRange(points.Select(x => x + 0.5f * direction).ToList());

                    for (var i = 0; i < points.Count; i++)
                    {
                        var point = points[i];

                        for (int j = 0; j < 2; j++)
                        {
                            var rotation = random.Next(0, 366);

                            var offsetLength = NextFloat(2, 4) * 1.75f / 10;
                            var clusterOffset = GetRandomFromRange(hatchingClusterOffset);

                            if (j == 1)
                            {
                                offsetLength = 0;
                            }

                            var c = point + offsetLength * directionPerpendicular;
                            var key = QuantizeHatchingKey(c);

                            if (usedPointKeys.Contains(key))
                            {
                                continue;
                            }

                            usedPointKeys.Add(key);
                            usedPointsAdd.Add(c);

                            for (int k = -1; k <= 1; k++)
                            {
                                var length = GetRandomFromRange(hatchingLength);
                                var center = point + offsetLength * directionPerpendicular +
                                             k * clusterOffset * directionPerpendicular;
                                center = RotatePoint(center, point + offsetLength * directionPerpendicular, rotation);

                                var from = center + length / 2 * direction;
                                var to = center - length / 2 * direction;

                                from = RotatePoint(from, center, rotation);
                                to = RotatePoint(to, center, rotation);

                                graphics.DrawLine(pen, from.X, from.Y, to.X, to.Y);
                            }
                        }
                    }
                }

                usedPoints.Add(new Tuple<RectangleGrid2D, List<Vector2>>(outline.BoundingRectangle, usedPointsAdd));
            }
        }

        private static long QuantizeHatchingKey(Vector2 point)
        {
            // Quantize to half-grid resolution to approximate the old
            // "distance < 0.5f" duplicate suppression without scanning lists.
            var qx = (int) Math.Round(point.X * 2f);
            var qy = (int) Math.Round(point.Y * 2f);
            unchecked
            {
                return ((long) qx << 32) ^ (uint) qy;
            }
        }

        private float GetRandomFromRange(Range<float> range)
        {
            return NextFloat(range.Minimum, range.Maximum);
        }

        protected void DrawShading(List<OutlineSegment> outlineSegments, Pen shadePen)
        {
            foreach (var outlineSegment in outlineSegments)
            {
                if (!outlineSegment.IsDoor)
                {
                    var from = outlineSegment.Line.From;
                    var to = outlineSegment.Line.To;

                    graphics.DrawLine(shadePen, from.X, from.Y, to.X, to.Y);
                }
            }
        }

        private float NextFloat(float from, float to)
        {
            return (float) random.NextDouble() * (to - from) + from;
        }

        private static Vector2 RotatePoint(Vector2 pointToRotate, Vector2 centerPoint, double angleInDegrees)
        {
            double angleInRadians = angleInDegrees * (Math.PI / 180);
            double cosTheta = Math.Cos(angleInRadians);
            double sinTheta = Math.Sin(angleInRadians);
            return new Vector2
            (
                (float)
                (cosTheta * (pointToRotate.X - centerPoint.X) -
                    sinTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.X),
                (float)
                (sinTheta * (pointToRotate.X - centerPoint.X) +
                 cosTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.Y)
            );
        }

        protected void DrawTextOntoPolygon(PolygonGrid2D polygon, string text, float penWidth)
        {
            var partitions = polygonPartitioning.GetPartitions(polygon);
            var orderedRectangles = partitions
                .OrderBy(x => Vector2Int.ManhattanDistance(x.Center, polygon.BoundingRectangle.Center)).ToList();
            var targetRectangle = orderedRectangles.First();

            if (orderedRectangles.Any(x => x.Width > 6 && x.Height > 3))
            {
                targetRectangle = orderedRectangles.First(x => x.Width > 6 && x.Height > 3);
            }

            using (var font = new Font("Baskerville Old Face", penWidth, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var rect = new RectangleF(
                    targetRectangle.A.X,
                    targetRectangle.A.Y,
                    targetRectangle.Width,
                    targetRectangle.Height + 0.2f * penWidth
                );

                var sf = new StringFormat
                {
                    LineAlignment = StringAlignment.Center,
                    Alignment = StringAlignment.Center
                };

                graphics.DrawString(text, font, Brushes.Black, rect, sf);
            }
        }

        protected List<OutlineSegment> GetOutline(PolygonGrid2D polygon, List<OrthogonalLineGrid2D> doorLines,
            Vector2Int offset = default)
        {
            var old = GetOutlineOld(polygon, doorLines);
            var outline = new List<OutlineSegment>();

            for (int i = 0; i < old.Count; i++)
            {
                var current = old[i];
                var previous = old[Mod(i - 1, old.Count)];
                outline.Add(new OutlineSegment(
                    new OrthogonalLineGrid2D(previous.Item1 + offset, current.Item1 + offset), current.Item2 == false));
            }

            return outline;
        }

        private List<Tuple<Vector2Int, bool>> GetOutlineOld(PolygonGrid2D polygon, List<OrthogonalLineGrid2D> doorLines)
        {
            var outline = new List<Tuple<Vector2Int, bool>>();
            doorLines = doorLines?.ToList();

            foreach (var line in polygon.GetLines())
            {
                AddToOutline(Tuple.Create(line.From, true));

                if (doorLines == null)
                    continue;

                var doorDistances = doorLines.Select(x =>
                        new Tuple<OrthogonalLineGrid2D, int>(x, Math.Min(line.Contains(x.From), line.Contains(x.To))))
                    .ToList();
                doorDistances.Sort((x1, x2) => x1.Item2.CompareTo(x2.Item2));

                foreach (var pair in doorDistances)
                {
                    if (pair.Item2 == -1)
                        continue;

                    var doorLine = pair.Item1;

                    if (line.Contains(doorLine.From) != pair.Item2)
                    {
                        doorLine = doorLine.SwitchOrientation();
                    }

                    doorLines.Remove(pair.Item1);

                    AddToOutline(Tuple.Create(doorLine.From, true));
                    AddToOutline(Tuple.Create(doorLine.To, false));
                }
            }

            return outline;

            void AddToOutline(Tuple<Vector2Int, bool> point)
            {
                if (outline.Count == 0)
                {
                    outline.Add(point);
                    return;
                }

                var lastPoint = outline[outline.Count - 1];

                if (!lastPoint.Item2 && point.Item2 && lastPoint.Item1 == point.Item1)
                    return;

                outline.Add(point);
            }
        }

        protected class OutlineSegment
        {
            public OutlineSegment(OrthogonalLineGrid2D line, bool isDoor)
            {
                Line = line;
                IsDoor = isDoor;
            }

            public OrthogonalLineGrid2D Line { get; }

            public bool IsDoor { get; }

            public override string ToString()
            {
                return $"{Line} {IsDoor}";
            }
        }
    }
}
