﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    public abstract class ProceduralAbstractSoRShape : ProceduralAbstractShape
    {
        private static readonly string ModTag = "[ProceduralAbstractShapeSoR]";

        #region Config fields

        internal const int MinCircleVertexes = 12;
        internal const float MaxCircleError = 0.01f;
        internal const float MaxDiameterChange = 5.0f;

        [KSPField]
        public string topNodeName = "top";

        [KSPField]
        public string bottomNodeName = "bottom";

        #endregion

        #region Mesh Writing

        protected class ProfilePoint
        {
            public readonly float dia;
            public readonly float y;
            public float v;

            public readonly bool inRender;
            public readonly bool inCollider;

            // the normal as a 2 component unit vector (dia, y)
            // eg: for cylinders this would be (1,0), for endcaps (0,+/-1)
            public readonly Vector2 norm;

            public readonly CirclePoints circ;
            public readonly CirclePoints colliderCirc;

            public ProfilePoint(float dia, float y, float v, Vector2 norm, bool inRender = true, bool inCollider = true, CirclePoints circ = null, CirclePoints colliderCirc = null)
            {
                this.dia = dia;
                this.y = y;
                this.v = v;
                this.norm = norm;
                this.inRender = inRender;
                this.inCollider = inCollider;
                this.circ = inRender ? (circ ?? CirclePoints.ForDiameter(dia, MaxCircleError, MinCircleVertexes)) : null;
                this.colliderCirc = inCollider ? (colliderCirc ?? this.circ ?? CirclePoints.ForDiameter(dia, MaxCircleError, MinCircleVertexes)) : null;
            }

            public bool CustomCollider
            {
                get
                {
                    return circ != colliderCirc;
                }
            }
        }

        private LinkedList<ProfilePoint> lastProfile;


        public Vector3[] GetEndcapVerticies(bool top)
        {
            if (lastProfile == null)
                return new Vector3[0];

            ProfilePoint profilePoint = top ? lastProfile.Last.Value : lastProfile.First.Value;
            Vector3[] verticies = new Vector3[profilePoint.circ.totVertexes];
            bool odd = lastProfile.Count % 2 == 0;

            profilePoint.circ.WriteEndcapVerticies(profilePoint.dia, profilePoint.y, 0, verticies, odd);
            return verticies;
        }
        


        protected void WriteMeshes(params ProfilePoint[] pts)
        {
            WriteMeshes(new LinkedList<ProfilePoint>(pts));
        }

        /// <summary>
        /// Generate the compShape from profile points from pt to bottom.
        /// Note that this list will have extra interpolated points added if the change in radius is high to avoid
        /// texture stretching.
        /// </summary>
        /// <param name="pts"></param>
        protected void WriteMeshes(LinkedList<ProfilePoint> pts)
        {
            if (pts == null || pts.Count < 2)
                return;

            // update nodes
            UpdateNodeSize(pts.First(), bottomNodeName);
            UpdateNodeSize(pts.Last(), topNodeName);

            lastProfile = pts;

            // Horizontal profile point subdivision
            SubdivHorizontal(pts);

            // Tank stats
            float tankVLength = 0;

            int nVrt = 0;
            int nTri = 0;
            int nColVrt = 0;
            int nColTri = 0;
            bool customCollider = false;

            ProfilePoint first = pts.First.Value;
            ProfilePoint last = pts.Last.Value;

            if (!first.inCollider || !last.inCollider)
                throw new InvalidOperationException("First and last profile points must be used in the collider");

            foreach (ProfilePoint pt in pts)
            {
                customCollider = customCollider || pt.CustomCollider;

                if (pt.inRender)
                {
                    nVrt += pt.circ.totVertexes + 1;
                    // one for above, one for below
                    nTri += 2 * pt.circ.totVertexes;
                }

                if (pt.inCollider)
                {
                    nColVrt += pt.colliderCirc.totVertexes + 1;
                    nColTri += 2 * pt.colliderCirc.totVertexes;
                }
            }
            // Have double counted for the first and last circles.
            nTri -= first.circ.totVertexes + last.circ.totVertexes;
            nColTri -= first.colliderCirc.totVertexes + last.colliderCirc.totVertexes;

            UncheckedMesh m = new UncheckedMesh(nVrt, nTri);

            float sumDiameters = 0;
            //Debug.LogWarning("Display mesh vert=" + nVrt + " tris=" + nTri);

            bool odd = false;
            {
                ProfilePoint prev = null;
                int off = 0, prevOff = 0;
                int tOff = 0;
                foreach (ProfilePoint pt in pts)
                {
                    if (!pt.inRender)
                        continue;

                    pt.circ.WriteVertexes(diameter: pt.dia, y: pt.y, v: pt.v, norm: pt.norm, off: off, m: m, odd: odd);
                    if (prev != null)
                    {
                        CirclePoints.WriteTriangles(prev.circ, prevOff, pt.circ, off, m.triangles, tOff * 3, !odd);
                        tOff += prev.circ.totVertexes + pt.circ.totVertexes;

                        // Deprecated: Volume has been moved up to callers. This way we can use the idealized rather than aproximate volume
                        // Work out the area of the truncated cone

                        // integral_y1^y2 pi R(y)^2 dy   where R(y) = ((r2-r1)(y-y1))/(r2-r1) + r1   Integrate circles along a line
                        // integral_y1^y2 pi ( ((r2-r1)(y-y1))/(r2-r1) + r1) ^2 dy                Substituted in formula.
                        // == -1/3 pi (y1-y2) (r1^2+r1*r2+r2^2)                                   Do the calculus
                        // == -1/3 pi (y1-y2) (d1^2/4+d1*d2/4+d2^2/4)                             r = d/2
                        // == -1/12 pi (y1-y2) (d1^2+d1*d2+d2^2)                                  Take out the factor
                        //volume += (Mathf.PI * (pt.y - prev.y) * (prev.dia * prev.dia + prev.dia * pt.dia + pt.dia * pt.dia)) / 12f;

                        float dy = (pt.y - prev.y);
                        float dr = (prev.dia - pt.dia) * 0.5f;

                        //print("dy=" + dy + " dr=" + dr + " len=" + Mathf.Sqrt(dy * dy + dr * dr).ToString("F3"));
                        tankVLength += Mathf.Sqrt(dy * dy + dr * dr);

                        // average diameter weighted by dy
                        sumDiameters += (pt.dia + prev.dia) * dy;
                    }

                    prev = pt;
                    prevOff = off;
                    off += pt.circ.totVertexes + 1;
                    odd = !odd;
                }
            }

            // Use the weighted average diameter across segments to set the ULength
            float tankULength = Mathf.PI * sumDiameters / (last.y - first.y);

            //print("ULength=" + tankULength + " VLength=" + tankVLength);

            // set the texture scale.
            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, tankVLength));

            

            if(HighLogic.LoadedScene == GameScenes.LOADING)
                m.WriteTo(PPart.SidesIconMesh);
            else
                m.WriteTo(SidesMesh);


            // The endcaps.
            nVrt = first.circ.totVertexes + last.circ.totVertexes;
            nTri = first.circ.totVertexes - 2 + last.circ.totVertexes - 2;
            m = new UncheckedMesh(nVrt, nTri);

            first.circ.WriteEndcap(first.dia, first.y, false, 0, 0, m, false);
            last.circ.WriteEndcap(last.dia, last.y, true, first.circ.totVertexes, (first.circ.totVertexes - 2) * 3, m, !odd);

            

            if (HighLogic.LoadedScene == GameScenes.LOADING)
                m.WriteTo(PPart.EndsIconMesh);
            else
                m.WriteTo(EndsMesh);

            // build the collider mesh at a lower resolution than the visual mesh.
            //Debug.LogWarning("Collider mesh vert=" + nColVrt + " tris=" + nColTri);
                
            // collider endcaps
            ProfilePoint firstColPt, lastColPt;

            firstColPt = pts.First(x => x.inCollider);
            lastColPt = pts.Last(x => x.inCollider);

            int nColEndVrt = firstColPt.colliderCirc.totVertexes + lastColPt.colliderCirc.totVertexes;
            int nColEndTri = firstColPt.colliderCirc.totVertexes - 2 + lastColPt.colliderCirc.totVertexes - 2;

            m = new UncheckedMesh(nColVrt+nColEndVrt, nColTri+nColEndTri);
            odd = false;
            {
                ProfilePoint prev = null;
                int off = 0, prevOff = 0;
                int tOff = 0;
                    
                foreach (ProfilePoint pt in pts)
                {
                    if (!pt.inCollider)
                        continue;
                        
                    if(prev == null)
                    {
                        pt.colliderCirc.WriteEndcap(pt.dia, pt.y, false, 0, 0, m, odd);
                        off = firstColPt.colliderCirc.totVertexes;
                        tOff = (firstColPt.colliderCirc.totVertexes - 2);
                    }
                    //Debug.LogWarning("Collider circ (" + pt.dia + ", " + pt.y + ") verts=" + pt.colliderCirc.totVertexes);
                    pt.colliderCirc.WriteVertexes(diameter: pt.dia, y: pt.y, v: pt.v, norm: pt.norm, off: off, m: m, odd: odd);
                    if (prev != null)
                    {
                        CirclePoints.WriteTriangles(prev.colliderCirc, prevOff, pt.colliderCirc, off, m.triangles, tOff * 3, !odd);
                        tOff += prev.colliderCirc.totVertexes + pt.colliderCirc.totVertexes;
                    }

                    prev = pt;
                    prevOff = off;
                    off += pt.colliderCirc.totVertexes + 1;
                    odd = !odd;
                }

                prev.colliderCirc.WriteEndcap(prev.dia, prev.y, true, off, tOff*3, m, odd);
            }

            if (colliderMesh == null)
                colliderMesh = new Mesh();

            m.WriteTo(colliderMesh);
            //m.WriteTo(SidesMesh);
            if (colliderMesh.triangles.Length / 3 > 255)
                Debug.LogWarning("Collider mesh contains " + colliderMesh.triangles.Length / 3 + " triangles. Maximum allowed triangles: 255");

            // If the colliderHolder contains one collider, update it, else delete the old ones and create a new one
            PPart.UpdateOrReplaceSingleCollider(colliderMesh);

            PPart.UpdateProps();

            RaiseModelAndColliderChanged();
        }

        private Mesh colliderMesh;

        /// <summary>
        /// Subdivide profile points according to the max diameter change. 
        /// </summary>
        private void SubdivHorizontal(LinkedList<ProfilePoint> pts)
        {
            ProfilePoint prev = pts.First.Value;
            for (LinkedListNode<ProfilePoint> node = pts.First.Next; node != null; node = node.Next)
            {
                ProfilePoint curr = node.Value;
                if (!curr.inRender)
                    continue;

                float dDiameter = curr.dia - prev.dia;
                float dPercentage = Math.Abs(curr.dia - prev.dia) / (Math.Max(curr.dia, prev.dia) / 100.0f);
                int subdiv = Math.Min((int)(Math.Truncate(dPercentage / MaxDiameterChange)), 30);
                //int subdiv = Math.Min((int)Math.Truncate(Mathf.Abs(dDiameter) / MaxDiameterChange), 30);
                if (subdiv > 1)
                {
                    // slerp alg for normals  http://http://en.wikipedia.org/wiki/Slerp
                    bool doSlerp = prev.norm != curr.norm;
                    float omega = 0, sinOmega = 0;
                    if (doSlerp)
                    {
                        omega = Mathf.Acos(Vector2.Dot(prev.norm, curr.norm));
                        sinOmega = Mathf.Sin(omega);
                    }

                    for (int i = 1; i < subdiv; ++i)
                    {
                        float t = i / (float)subdiv;
                        float tDiameter = prev.dia + dDiameter * t;
                        float tY = Mathf.Lerp(prev.y, curr.y, t);
                        float tV = Mathf.Lerp(prev.v, curr.v, t);

                        Vector2 norm;
                        if (doSlerp)
                            norm = (Mathf.Sin(omega * (1f - t)) / sinOmega * prev.norm + Mathf.Sin(omega * t) / sinOmega * curr.norm);
                        else
                            norm = prev.norm;

                        pts.AddBefore(node, new ProfilePoint(dia: tDiameter, y: tY, v: tV, norm: norm, inCollider: false));
                    }
                }

                prev = curr;
            }
        }

        private void UpdateNodeSize(ProfilePoint pt, string nodeName)
        {
            AttachNode node = part.attachNodes.Find(n => n.id == nodeName);
            if (node == null)
                return;
            node.size = Math.Min((int)(pt.dia / PPart.diameterLargeStep), 3);

            // Breaking force and torque scales with the area of the surface (node size).
            node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);

            // Send messages for the changing of the ends
            RaiseChangeAttachNodeSize(node, pt.dia, Mathf.PI * pt.dia * pt.dia * 0.25f);

            // TODO: separate out the meshes for each end so we can use the scale for texturing.
            RaiseChangeTextureScale(nodeName, PPart.legacyTextureHandler.EndsMaterial, new Vector2(pt.dia, pt.dia));
        }

        #endregion

        #region Circle Points

        public class CirclePoints
        {
            public static CirclePoints ForDiameter(float diameter, float maxError, int minVertexes, int maxVertexes = int.MaxValue)
            {
                int idx = circlePoints.FindIndex(v => (v.totVertexes >= minVertexes) && (v.maxError * diameter * 2f) <= maxError);
                switch (idx)
                {
                    case 0:
                        return circlePoints[0];
                    case -1:
                        CirclePoints prev;
                        if (circlePoints.Count == 0)
                            circlePoints.Add(prev = new CirclePoints(0));
                        else
                            prev = circlePoints.Last();

                        while (prev.totVertexes <= minVertexes)
                            circlePoints.Add(prev = new CirclePoints(prev.subdivCount + 1));

                        while (true)
                        {
                            CirclePoints nxt = new CirclePoints(prev.subdivCount + 1);
                            circlePoints.Add(nxt);
                            if (nxt.totVertexes >= maxVertexes || nxt.maxError * diameter * 2 < maxError)
                                return prev;
                            prev = nxt;
                        }
                    default:
                        return circlePoints[Math.Min(idx - 1, maxVertexes / 4 - 1)];
                }
            }

            public static CirclePoints ForPoints(int vertexes)
            {
                int idx = vertexes / 4 - 1;
                if (idx >= circlePoints.Count)
                {
                    CirclePoints prev = circlePoints.Last();
                    do
                    {
                        circlePoints.Add(prev = new CirclePoints(prev.subdivCount + 1));
                    }
                    while (prev.totVertexes <= vertexes);
                }
                return circlePoints[idx];
            }

            private static readonly List<CirclePoints> circlePoints = new List<CirclePoints>();


            private readonly int subdivCount;
            public readonly int totVertexes;
            private readonly float maxError;

            private static readonly float MaxError0 = Mathf.Sqrt(2) * (Mathf.Sin(Mathf.PI / 4.0f) - 0.5f) * 0.5f;

            private float[][] uCoords;
            private float[][] xCoords;
            private float[][] zCoords;

            private bool complete;

            private CirclePoints(int subdivCount)
            {
                this.subdivCount = subdivCount;
                totVertexes = (1 + subdivCount) * 4;

                if (subdivCount == 0)
                {
                    uCoords = new[] { new[] { 0.0f }, new[] { 0.125f } };
                    xCoords = new[] { new[] { 0.0f }, new[] { -1f / Mathf.Sqrt(2) } };
                    zCoords = new[] { new[] { 1.0f }, new[] { 1f / Mathf.Sqrt(2) } };

                    maxError = MaxError0;
                    complete = true;
                }
                else
                {
                    // calculate the max error.
                    uCoords = new[] { new[] { 0.0f, 1f / totVertexes }, new[] { 0.5f / totVertexes, 1.5f / totVertexes } };
                    float theta = uCoords[0][1] * Mathf.PI * 2.0f;
                    xCoords = new[] { new[] { 0.0f, -Mathf.Sin(theta) }, new[] { -Mathf.Sin(theta * 0.5f), -Mathf.Sin(theta * 1.5f) } };
                    zCoords = new[] { new[] { 1.0f, Mathf.Cos(theta) }, new[] { Mathf.Cos(theta * 0.5f), Mathf.Cos(theta * 1.5f) } };

                    float dX = xCoords[1][0] - xCoords[0][1] / 2.0f;
                    float dY = zCoords[1][0] - (1f + zCoords[0][1]) / 2.0f;

                    maxError = Mathf.Sqrt(dX * dX + dY * dY);

                    complete = subdivCount == 1;
                }
            }

            private void Complete()
            {
                if (complete)
                    return;

                int totalCoords = subdivCount + 1;

                float[][] oldUCoords = uCoords;
                float[][] oldXCoords = xCoords;
                float[][] oldYCoords = zCoords;
                uCoords = new[] { new float[totalCoords], new float[totalCoords] };
                xCoords = new[] { new float[totalCoords], new float[totalCoords] };
                zCoords = new[] { new float[totalCoords], new float[totalCoords] };
                Array.Copy(oldUCoords[0], uCoords[0], 2);
                Array.Copy(oldXCoords[0], xCoords[0], 2);
                Array.Copy(oldYCoords[0], zCoords[0], 2);
                Array.Copy(oldUCoords[1], uCoords[1], 2);
                Array.Copy(oldXCoords[1], xCoords[1], 2);
                Array.Copy(oldYCoords[1], zCoords[1], 2);

                float denom = 4 * (subdivCount + 1);
                for (int i = 2; i <= subdivCount; ++i)
                {
                    uCoords[0][i] = i / denom;
                    uCoords[1][i] = (i + 0.5f) / denom;

                    float theta = uCoords[0][i] * Mathf.PI * 2.0f;
                    float theta1 = uCoords[1][i] * Mathf.PI * 2.0f;
                    xCoords[0][i] = -Mathf.Sin(theta);
                    zCoords[0][i] = Mathf.Cos(theta);
                    xCoords[1][i] = -Mathf.Sin(theta1);
                    zCoords[1][i] = Mathf.Cos(theta1);
                }

                complete = true;
            }

            /// <summary>
            /// writes this.totVerticies + 1 xy, verticies, and tangents and this.totVerticies triangles to the passed arrays for a single endcap.
            /// Callers will need to fill the normals. This will be { 0, 1, 0 } for pt endcap, and { 0, -1, 0 } for bottom.
            /// </summary>
            /// <param name="dia">diameter of circle</param>
            /// <param name="y">y dimension for points</param>
            /// <param name="up">If this endcap faces up</param>
            /// <param name="vOff">offset into xy, verticies, and normal arrays to begin at</param>
            /// <param name="to">offset into triangles array</param>
            /// <param name="m">Mesh to write into</param>
            /// <param name="odd">If this is an odd row</param>
            public void WriteEndcap(float dia, float y, bool up, int vOff, int to, UncheckedMesh m, bool odd)
            {
                Complete();

                int o = odd ? 1 : 0;

                for (int i = 0; i <= subdivCount; ++i)
                {
                    int o0 = vOff + i;
                    m.uv[o0] = new Vector2((-xCoords[o][i] + 1f) * 0.5f, (-zCoords[o][i] + 1f) * 0.5f);
                    m.vertices[o0] = new Vector3(xCoords[o][i] * dia * 0.5f, y, zCoords[o][i] * dia * 0.5f);

                    int o1 = vOff + i + subdivCount + 1;
                    m.uv[o1] = new Vector2((zCoords[o][i] + 1f) * 0.5f, (-xCoords[o][i] + 1f) * 0.5f);
                    m.vertices[o1] = new Vector3(-zCoords[o][i] * dia * 0.5f, y, xCoords[o][i] * dia * 0.5f);

                    int o2 = vOff + i + 2 * (subdivCount + 1);
                    m.uv[o2] = new Vector2((xCoords[o][i] + 1f) * 0.5f, (zCoords[o][i] + 1f) * 0.5f);
                    m.vertices[o2] = new Vector3(-xCoords[o][i] * dia * 0.5f, y, -zCoords[o][i] * dia * 0.5f);

                    int o3 = vOff + i + 3 * (subdivCount + 1);
                    m.uv[o3] = new Vector2((-zCoords[o][i] + 1f) * 0.5f, (xCoords[o][i] + 1f) * 0.5f);
                    m.vertices[o3] = new Vector3(zCoords[o][i] * dia * 0.5f, y, -xCoords[o][i] * dia * 0.5f);

                    m.tangents[o0] = m.tangents[o1] = m.tangents[o2] = m.tangents[o3] = new Vector4(-1, 0, 0, up ? 1 : -1);
                    m.normals[o0] = m.normals[o1] = m.normals[o2] = m.normals[o3] = new Vector3(0, up ? 1 : -1, 0);
                }

                for (int i = 1; i < totVertexes - 1; ++i)
                {
                    m.triangles[to++] = vOff;
                    m.triangles[to++] = vOff + i + (up ? 1 : 0);
                    m.triangles[to++] = vOff + i + (up ? 0 : 1);
                }
            }

            public void WriteEndcapVerticies(float dia, float y, int vOff, Vector3[] verticies, bool odd)
            {
                Complete();

                int o = odd ? 1 : 0;

                for (int i = 0; i <= subdivCount; ++i)
                {
                    int o0 = vOff + i;
                    
                    verticies[o0] = new Vector3(xCoords[o][i] * dia * 0.5f, y, zCoords[o][i] * dia * 0.5f);

                    int o1 = vOff + i + subdivCount + 1;
                    
                    verticies[o1] = new Vector3(-zCoords[o][i] * dia * 0.5f, y, xCoords[o][i] * dia * 0.5f);

                    int o2 = vOff + i + 2 * (subdivCount + 1);
                    
                    verticies[o2] = new Vector3(-xCoords[o][i] * dia * 0.5f, y, -zCoords[o][i] * dia * 0.5f);

                    int o3 = vOff + i + 3 * (subdivCount + 1);
                    
                    verticies[o3] = new Vector3(zCoords[o][i] * dia * 0.5f, y, -xCoords[o][i] * dia * 0.5f);

                }

                
            }

            

            /// <summary>
            /// Write vertexes for the circle.
            /// </summary>
            /// <param name="diameter">diameter of the circle</param>
            /// <param name="y">y coordinate</param>
            /// <param name="norm">unit normal vector along the generator curve for increasing y. The y param becomes the y of the normal, the x multiplies the normals to the circle</param>
            /// <param name="v">v coordinate for UV</param>
            /// <param name="off">offset into following arrays</param>
            /// <param name="odd">If this is an odd row</param>
            /// <param name="m">Mesh to write vertexes into</param>
            public void WriteVertexes(float diameter, float y, float v, Vector2 norm, int off, bool odd, UncheckedMesh m)
            {
                Complete();

                int o = odd ? 1 : 0;

                for (int i = 0; i <= subdivCount; ++i)
                {
                    int o0 = off + i;
                    m.uv[o0] = new Vector2(uCoords[o][i], v);
                    m.vertices[o0] = new Vector3(xCoords[o][i] * 0.5f * diameter, y, zCoords[o][i] * 0.5f * diameter);
                    m.normals[o0] = new Vector3(xCoords[o][i] * norm.x, norm.y, zCoords[o][i] * norm.x);
                    m.tangents[o0] = new Vector4(-zCoords[o][i], 0, xCoords[o][i], -1.0f);
                    //MonoBehaviour.print("Vertex #" + i + " off=" + o0 + " u=" + xy[o0][0] + " coords=" + verticies[o0]);
                    
                    int o1 = off + i + subdivCount + 1;
                    m.uv[o1] = new Vector2(uCoords[o][i] + 0.25f, v);
                    m.vertices[o1] = new Vector3(-zCoords[o][i] * 0.5f * diameter, y, xCoords[o][i] * 0.5f * diameter);
                    m.normals[o1] = new Vector3(-zCoords[o][i] * norm.x, norm.y, xCoords[o][i] * norm.x);
                    m.tangents[o1] = new Vector4(-xCoords[o][i], 0, -zCoords[o][i], -1.0f);

                    int o2 = off + i + 2 * (subdivCount + 1);
                    m.uv[o2] = new Vector2(uCoords[o][i] + 0.50f, v);
                    m.vertices[o2] = new Vector3(-xCoords[o][i] * 0.5f * diameter, y, -zCoords[o][i] * 0.5f * diameter);
                    m.normals[o2] = new Vector3(-xCoords[o][i] * norm.x, norm.y, -zCoords[o][i] * norm.x);
                    m.tangents[o2] = new Vector4(zCoords[o][i], 0, -xCoords[o][i], -1.0f);

                    int o3 = off + i + 3 * (subdivCount + 1);
                    m.uv[o3] = new Vector2(uCoords[o][i] + 0.75f, v);
                    m.vertices[o3] = new Vector3(zCoords[o][i] * 0.5f * diameter, y, -xCoords[o][i] * 0.5f * diameter);
                    m.normals[o3] = new Vector3(zCoords[o][i] * norm.x, norm.y, -xCoords[o][i] * norm.x);
                    m.tangents[o3] = new Vector4(xCoords[o][i], 0, zCoords[o][i], -1.0f);
                }

                // write the wrapping vertex. This is identical to the first one except for u coord += 1
                int lp = off + totVertexes;
                m.uv[lp] = new Vector2(uCoords[o][0] + 1.0f, v);
                m.vertices[lp] = m.vertices[off];
                m.normals[lp] = m.normals[off];
                m.tangents[lp] = m.tangents[off];
            }

            private const float UDelta = 1e-5f;

            public IEnumerable<Vector3> PointsXZU(float uFrom, float uTo)
            {
                Complete();

                int denom = (4 * (subdivCount + 1));

                if (uFrom <= uTo)
                {
                    int iFrom = Mathf.CeilToInt((uFrom + UDelta) * denom);
                    int iTo = Mathf.FloorToInt((uTo - UDelta) * denom);

                    if (iFrom < 0)
                    {
                        int pushUp = (-iFrom / denom + 1) * denom;
                        iFrom += pushUp;
                        iTo += pushUp;
                    }

                    for (int i = iFrom; i <= iTo; ++i)
                        yield return PointXZU(i);
                }
                else
                {
                    int iFrom = Mathf.FloorToInt((uFrom - UDelta) * denom);
                    int iTo = Mathf.CeilToInt((uTo + UDelta) * denom);

                    if (iTo < 0)
                    {
                        int pushUp = (-iTo / denom + 1) * denom;
                        iFrom += pushUp;
                        iTo += pushUp;
                    }

                    for (int i = iFrom; i >= iTo; --i)
                        yield return PointXZU(i);
                }
            }

            private Vector3 PointXZU(int i)
            {
                int o = i % (subdivCount + 1);
                int q = i / (subdivCount + 1) % 4;
                //Debug.LogWarning("PointXZU(" + i + ") o=" + o + " q=" + q + " subdiv=" + subdivCount);
                switch (q)
                {
                    case 0:
                        return new Vector3(xCoords[0][o], zCoords[0][o], uCoords[0][o]);
                    case 1:
                        return new Vector3(-zCoords[0][o], xCoords[0][o], uCoords[0][o] + 0.25f);
                    case 2:
                        return new Vector3(-xCoords[0][o], -zCoords[0][o], uCoords[0][o] + 0.5f);
                    case 3:
                        return new Vector3(zCoords[0][o], -xCoords[0][o], uCoords[0][o] + 0.75f);
                }
                throw new InvalidProgramException("Unreachable code");
            }

            /// <summary>
            /// Creates a.vertexes + b.vertexes triangles to cover the surface between circle a and b.
            /// </summary>
            /// <param name="a">the first circle points</param>
            /// <param name="ao">offset into vertex array for a points</param>
            /// <param name="b">the second circle points</param>
            /// <param name="bo">offset into vertex array for b points</param>
            /// <param name="triangles">triangles array for output</param>
            /// <param name="to">offset into triangles array. This must be a multiple of 3</param>
            /// <param name="odd">Is this an odd row</param>
            public static void WriteTriangles(CirclePoints a, int ao, CirclePoints b, int bo, int[] triangles, int to, bool odd)
            {
                int aq = a.subdivCount + 1, bq = b.subdivCount + 1;
                int ai = 0, bi = 0;
                int ad = (odd ? 1 : 0), bd = (odd ? 0 : 1);

                while (ai < aq || bi < bq)
                {
                    float au = (ai < aq) ? a.uCoords[ad][ai] : (a.uCoords[ad][0] + 0.25f);
                    float bu = (bi < bq) ? b.uCoords[bd][bi] : (b.uCoords[bd][0] + 0.25f);

                    if (au < bu)
                    {
                        //MonoBehaviour.print("A-tri #" + ai + " tOff=" + to);
                        triangles[to++] = ao + ai;
                        triangles[to++] = bo + bi;
                        triangles[to++] = ao + ai + 1;
                        //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                        triangles[to++] = ao + ai + aq;
                        triangles[to++] = bo + bi + bq;
                        triangles[to++] = ao + ai + 1 + aq;
                        //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                        triangles[to++] = ao + ai + 2 * aq;
                        triangles[to++] = bo + bi + 2 * bq;
                        triangles[to++] = ao + ai + 1 + 2 * aq;
                        //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                        triangles[to++] = ao + ai + 3 * aq;
                        triangles[to++] = bo + bi + 3 * bq;
                        triangles[to++] = ao + ai + 1 + 3 * aq;
                        //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                        ++ai;
                    }
                    else
                    {
                        //MonoBehaviour.print("B-tri #" + bi + " tOff=" + to);
                        triangles[to++] = bo + bi;
                        triangles[to++] = bo + bi + 1;
                        triangles[to++] = ao + ai;
                        //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                        triangles[to++] = bo + bi + bq;
                        triangles[to++] = bo + bi + 1 + bq;
                        triangles[to++] = ao + ai + aq;
                        //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                        triangles[to++] = bo + bi + 2 * bq;
                        triangles[to++] = bo + bi + 1 + 2 * bq;
                        triangles[to++] = ao + ai + 2 * aq;
                        //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                        triangles[to++] = bo + bi + 3 * bq;
                        triangles[to++] = bo + bi + 1 + 3 * bq;
                        triangles[to++] = ao + ai + 3 * aq;
                        //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                        ++bi;
                    }
                }
            }

        }
        #endregion
    }
}