using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{

    public class ProceduralShapeBezierCone : ProceduralShapeCone
    {

        private static float[][] shapePresets
            = new float[][] { 
            new float[] { 0.3f, 0.3f, 0.7f, 0.7f },
            new float[] { 0.4f, 0.001f, 1.0f, 0.6f }, 
            new float[] { 0.5f, 0.001f, 0.8f, 0.7f }, 
            new float[] { 0.4f, 0.2f, 0.8f, 0.6f },
            new float[] { 0.3f, 0.2f, 1.0f, 0.5f },
            new float[] { 0.1f, 0.001f, 0.7f, 2f/3f },
            new float[] { 1f/3f, 0.3f, 1.0f, 0.9f }
        };

        private static string[] shapeNames =
        {
            "Straight",
            "Round #1",
            "Round #2",
            "Peaked #1",
            "Peaked #2",
            "Sharp #1",
            "Sharp #2"
        };

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Curve"),
         UI_ChooseOption(scene = UI_Scene.Editor)]
        public int curveIdx = 1;
        private int oldCurveIdx;


        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (!pPart.allowCurveTweaking)
                Fields["curveIdx"].guiActiveEditor = false;
            else
            {
                UI_ChooseOption curveIdxEdit = (UI_ChooseOption)Fields["curveIdx"].uiControlEditor;
                curveIdxEdit.options = shapeNames;
            }
            base.OnStart(state);
        }

        protected override void UpdateShape(bool force)
        {
            if (!force && oldTopDiameter == topDiameter && oldBottomDiameter == bottomDiameter && oldLength == length &&
                curveIdx == oldCurveIdx)
                return;

            if (HighLogic.LoadedSceneIsEditor)
            {
                MaintainParameterRelations();

                UpdateVolumeRange();
            }
            else
            {
                volume = CalcVolume();
            }

            WriteBezier();

            oldTopDiameter = topDiameter; oldBottomDiameter = bottomDiameter; oldLength = length;
            oldCurveIdx = curveIdx;
        }

        #region Control point calculation and volume limits

        private Vector2 p0, p1, p2, p3;

        private void UpdateVolumeRange()
        {
            volume = CalcVolume();

            float vol = volume;
            float inc;
            if (volume < pPart.volumeMin)
            {
                volume = pPart.volumeMin;
                inc = 0.001f;
            }
            else if (volume > pPart.volumeMax)
            {
                volume = pPart.volumeMax;
                inc = -0.001f;
            }
            else
                return;

            if (length != oldLength || curveIdx != oldCurveIdx)
            {
                // The volume is directly proportional to the length
                length *= volume / vol;

                volume = CalcVolume();
            }
            else if (bottomDiameter != oldBottomDiameter)
            {
                IterateLimitVolume(ref bottomDiameter, vol, inc);
            }
            else if (topDiameter != oldTopDiameter)
            {
                IterateLimitVolume(ref topDiameter, vol, inc);
            }
        }

        private void IterateLimitVolume(ref float toTweak, float vol, float inc)
        {
            float oToTweak = toTweak;
            float lVol;
            float lToTweak;
            int count = 1;
            do
            {
                lVol = vol;
                lToTweak = toTweak;
                toTweak = oToTweak + count++ * inc;
                vol = CalcVolume();
            }
            while (Mathf.Abs(vol - volume) < Mathf.Abs(lVol - volume));
            toTweak = lToTweak;
        }

        private float CalcVolume()
        {
            // So we have a rotated bezier curve from bottom to top.
            // There are four control points, the bottom (p0) and the top ones (p3) are obvious
            p0 = new Vector2(bottomDiameter, -length / 2f);
            p3 = new Vector2(topDiameter, length / 2f);

            float[] shape = shapePresets[curveIdx];

            // Pretty obvious below what the shape points mean
            if (bottomDiameter < topDiameter)
            {
                p1 = new Vector2(Mathf.Lerp(p0.x, p3.x, shape[0]), Mathf.Lerp(p0.y, p3.y, shape[1]));
                p2 = new Vector2(Mathf.Lerp(p0.x, p3.x, shape[2]), Mathf.Lerp(p0.y, p3.y, shape[3]));
            }
            else
            {
                p2 = new Vector2(Mathf.Lerp(p3.x, p0.x, shape[0]), Mathf.Lerp(p3.y, p0.y, shape[1]));
                p1 = new Vector2(Mathf.Lerp(p3.x, p0.x, shape[2]), Mathf.Lerp(p3.y, p0.y, shape[3]));
            }

            // The maths for the area under the bezier can be calculated using 
            // pappus's centroid theroem: http://mathworld.wolfram.com/PappussCentroidTheorem.html
            // V = 2pi * Area of lamina * x geometric centroid y axis
            // geometric centroid about y axis = Moment about y / Area of curve
            // so area under curve ends up factoring out.


#if false
            // Here's the formula for area under the curve:
            // http://tug.org/TUGboat/tb33-1/tb103jackowski.pdf

            // I checked the maths myself, it does work out
            // using green's theorem: http://mathworld.wolfram.com/GreensTheorem.html
            // area = int x y' dt, t=0..1   
            // x = B(t) = (1-t)^3 p0_x + t(1-t)^2 p1_x + t^2(1-t) p2_x + t^3 p3_x
            // y = B'(t) = (1-t)^2 (p1_y-p0_y) + t(1-t) (p2_y-p1_y) + t^2 (p3_y-p2_y)

            float area = ((p1.y-p0.y)*(10*p0.x+6*p1.x+3*p2.x+p3.x)
                         +(p2.y-p1.y)*(4*p0.x+6*p1.x+6*p2.x+4*p3.x)
                         +(p3.y-p2.y)*(1*p0.x+3*p1.x+6*p2.x+10*p3.x))/20;

            // Of course it's not required anyhow.
#endif

            // Moment about y.
            // M_y = integrate x^2 y' dt, x=0..1

            // Skipping the several pages of maths workings....
            // If you want to repeat the maths for this one, be my guest! 
            // Don't shove it through alpha - it doesn't factorize it out very well.
            // I've got a spreadsheet that does most of the heavy lifting.

            // factor 1/2 taken out. The 1/4 is because the pN.x are diameters not radii.
            float M_y  = 1f/4f* ((p1.y-p0.y)*(1f/3f * p0.x*p0.x + 1f/4f * p0.x*p1.x + 1f/14f* p0.x*p2.x + 3f/28f* p1.x*p1.x+ 3f/28f * p1.x*p2.x + 1f/84f * p0.x*p3.x + 3f/70f * p2.x*p2.x + 1f/35f* p1.x*p3.x + 1f/28f* p2.x*p3.x + 1f/84f* p3.x*p3.x) +
                                 (p2.y-p1.y)*(1f/12f* p0.x*p0.x + 1f/7f * p0.x*p1.x + 1f/14f* p0.x*p2.x + 3f/28f* p1.x*p1.x+ 6f/35f * p1.x*p2.x + 2f/105f* p0.x*p3.x + 3f/28f * p2.x*p2.x + 1f/14f* p1.x*p3.x + 1f/7f * p2.x*p3.x + 1f/12f* p3.x*p3.x) +
                                 (p3.y-p2.y)*(1f/84f* p0.x*p0.x + 1f/28f* p0.x*p1.x + 1f/35f* p0.x*p2.x + 3f/70f* p1.x*p1.x+ 3f/28f * p1.x*p2.x + 1f/84f * p0.x*p3.x + 3f/28f * p2.x*p2.x + 1f/14f* p1.x*p3.x + 1f/4f * p2.x*p3.x + 1f/3f * p3.x*p3.x));

            // therefore the centroid for x:

            // Back to pappus: (factor 1/2 * 2 taken out)
            return Mathf.PI * M_y;

            // Some regexp to store:
            // Replace from spreadsheet (regexp): 
            // ([xy])_([0-4]) -> p$2.$1 
            // (p[0-3].[xy])\^2 -> $1*$1    
            // (?<=[^a-zA-Z])([0-9]+) -> $1f
        }
        #endregion

        #region Bezier Bits

        [KSPField]
        public bool showHull = false;

        private void WriteBezier()
        {
            if (showHull)
                WriteHull();
            else
                WriteShape();
        }

        private void WriteHull()
        {
            // Perpendicular vector
            float[] lengths = { (p1 - p0).magnitude, (p2 - p1).magnitude, (p3 - p2).magnitude };
            float sum = lengths.Sum();

            Vector2 norm = new Vector2(length, (bottomDiameter - topDiameter) / 2f);
            norm.Normalize();

            WriteMeshes(
                new ProfilePoint(p0.x, p0.y, 0f, Vector2.right),
                new ProfilePoint(p1.x, p1.y, lengths[0] / sum, Vector2.right),
                new ProfilePoint(p2.x, p2.y, (lengths[0] + lengths[1]) / sum, Vector2.right),
                new ProfilePoint(p3.x, p3.y, 1f, Vector2.right)
                );
        }

        private void WriteShape()
        {
            LinkedList<ProfilePoint> points = new LinkedList<ProfilePoint>();

            int colliderTri = 0;

            points.AddLast(CreatePoint(0, ref colliderTri));
            points.AddLast(CreatePoint(1, ref colliderTri));

            colliderTri /= 2;

            Queue<LinkedListNode<ProfilePoint>> process = new Queue<LinkedListNode<ProfilePoint>>();
            process.Enqueue(points.First);

            while (process.Count > 0)
            {
                LinkedListNode<ProfilePoint> node = process.Dequeue();
                ProfilePoint pM = node.Value;
                ProfilePoint pN = node.Next.Value;

                float tM = pM.v;
                float tN = pN.v;

                // So we want to find the point where the curve is maximally distant from the line between pM and pN

                // First we need the normal to the line:
                Vector2 norm = new Vector2(-pN.y + pM.y, pN.dia - pM.dia);

                // The deviation is:
                // Dev = B(t) . norm - B(m) . norm    (where m = t at point M)

                // We want to know the maxima, so take the derivative and solve for = 0
                // Dev' = B'(t) . norm
                //      = 3(1-t)^2 ((p1.x-p0.x) norm.x + (p1.y-p0.y) norm.y) + 6t(1-t) ((p2.x-p1.x) norm.x + (p2.y-p1.y) norm.y) + 3t^2 ((p3.x-p2.x) norm.x + (p3.y-p2.y) norm.y) = 0

                // This is a quadratic, which we can solve directly.

                float a = ((p1.x - p0.x) * norm.x + (p1.y - p0.y) * norm.y);
                float b = ((p2.x - p1.x) * norm.x + (p2.y - p1.y) * norm.y);
                float c = ((p3.x - p2.x) * norm.x + (p3.y - p2.y) * norm.y);

                // solve a (1-t)^2+2 b (t (1-t))+c t^2 = 0

                // t = (-/+ sqrt(b^2-a c)-a+b)/(-a+2 b-c)   for  a-2 b+c!=0
                // t = (2 b-c)/(2 (b-c))                    for  a = 2 b-c and b-c!=0

                List<float> ts = new List<float>(2);
                //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} perp=({2:F3}, {3:F3}) a={4:F3} b={5:F3} c={6:F3}", tM, tN, norm.x, norm.y, a, b, c));

                if (Math.Abs(a - 2 * b + c) < 1e-6f)
                {
                    if (Math.Abs(b - c) < 1e-6f)
                    {
                        // This is the straight line case, no need to subdivide
                        continue;
                    }
                    float t1 = (2f * b - c) / (2f * (b - c));
                    //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} -> {2:F3}", tM, tN, t1));

                    ts.Add(t1);
                }
                else
                {
                    float sqrt = Mathf.Sqrt(b * b - a * c);

                    float t1 = (sqrt - a + b) / (-a + 2 * b - c);
                    float t2 = (-sqrt - a + b) / (-a + 2 * b - c);
                    //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} -> {2:F3} {3:F3} ", tM, tN, t1, t2));


                    ts.Add(t1);
                    ts.Add(t2);

                    ts.Sort();
                }


                for (int i = 0; i < ts.Count; ++i)
                {
                    if (ts[i] < tM || ts[i] > tN)
                        ts.RemoveAt(i--);
                }

                if (ts.Count == 0)
                    throw new InvalidProgramException("There should be a point maximally distant from the line or the maths is really wrong.");

                norm = norm.normalized;
                float devM = pM.dia * norm.x + pM.y * norm.y;

                for (int i = 0; i < ts.Count; ++i)
                {
                    // The difference from the line
                    float devTS = Vector2.Dot(B(ts[i]), norm) - devM;

                    if (Mathf.Abs(devTS) < maxCircleError)
                        ts.RemoveAt(i--);
                }

                switch (ts.Count)
                {
                    case 0:
                        break;
                    case 1:
                        LinkedListNode<ProfilePoint> next = node.List.AddAfter(node, CreatePoint(ts[0], ref colliderTri));
                        process.Enqueue(node);
                        process.Enqueue(next);
                        break;
                    case 2:
                        LinkedListNode<ProfilePoint> next0 = node.List.AddAfter(node, CreatePoint(ts[0], ref colliderTri));
                        LinkedListNode<ProfilePoint> next1 = node.List.AddAfter(next0, CreatePoint(ts[1], ref colliderTri));

                        process.Enqueue(node);
                        process.Enqueue(next0);
                        process.Enqueue(next1);
                        break;
                }
            }


            // Need to figure out the v coords.
            float sumLengths = 0;
            float[] cumLengths = new float[points.Count - 1];

            LinkedListNode<ProfilePoint> pv = points.First;
            LinkedListNode<ProfilePoint> nx = pv.Next;
            for (int i = 0; i < cumLengths.Length; ++i, pv = nx, nx = nx.Next)
            {
                float dX = nx.Value.dia - pv.Value.dia;
                float dY = nx.Value.y - pv.Value.y;

                cumLengths[i] = sumLengths += Mathf.Sqrt(dX * dX + dY * dY);
            }

            points.First.Value.v = 0;
            nx = points.First.Next;
            for (int i = 0; i < cumLengths.Length; ++i, nx = nx.Next)
            {
                nx.Value.v = cumLengths[i] / sumLengths;
            }


            WriteMeshes(points);
        }

        private ProfilePoint CreatePoint(float t, ref int colliderTri)
        {
            // B(t) = (1-t)^3 p0 + t(1-t)^2 p1 + t^2(1-t) p2 + t^3 p3
            Vector2 Bt = B(t);

            // B'(t) = (1-t)^2 (p1-p0) + t(1-t) (p2-p1) + t^2 (p3-p2)
            Vector2 Btdt = Bdt(t);

            // normalized perpendicular to tangent (derivative)
            Vector2 norm = new Vector2(Btdt.y, -Btdt.x / 2f).normalized;

            // Count the number of triangles
            CirclePoints colliderCirc = CirclePoints.ForDiameter(Bt.x, maxCircleError * 4f, 4, 16);
            colliderTri += (colliderCirc.totVertexes + 1) * 2;

            //Debug.LogWarning(string.Format("Creating profile point t={0:F3} coord=({1:F3}, {2:F3})  normal=({3:F3}, {4:F3})", t, Bt.x, Bt.y, norm.x, norm.y));

            // We can have a maxium of 255 triangles in the collider. Will leave a bit of breathing room at the top.
            if (colliderTri <= 220)
                return new ProfilePoint(Bt.x, Bt.y, t, norm, colliderCirc: colliderCirc);
            else
                return new ProfilePoint(Bt.x, Bt.y, t, norm, inCollider: false);
        }

        private Vector2 B(float t)
        {
            return (1 - t) * (1 - t) * (1 - t) * p0 + 3 * t * (1 - t) * (1 - t) * p1 + 3 * t * t * (1 - t) * p2 + t * t * t * p3;
        }

        private Vector2 Bdt(float t)
        {
            return 3 * (1 - t) * (1 - t) * (p1 - p0) + 6 * t * (1 - t) * (p2 - p1) + 3 * t * t * (p3 - p2);
        }

        #endregion
    }
}