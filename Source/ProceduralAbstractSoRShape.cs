using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    public abstract class ProceduralAbstractSoRShape : ProceduralAbstractShape
    {
        #region Config fields

        internal const int minCircleVertexes = 12;
        internal const float maxCircleError = 0.01f;
        internal const float maxDiameterChange = 0.05f;

        [KSPField]
        public string topNodeName = "top";

        [KSPField]
        public string bottomNodeName = "bottom";

        #endregion

        #region attachments

        private enum Location
        {
            Top, Bottom, Side
        }

        private class Attachment
        {
            public TransformFollower follower;
            public Location location;
            public Vector2 uv;

            public LinkedListNode<Attachment> node;

            public override string ToString()
            {
                return "Attachment(location:" + location + ", uv=" + uv.ToString("F4") + ")";
            }
        }

        private LinkedList<Attachment> topAttachments = new LinkedList<Attachment>();
        private LinkedList<Attachment> bottomAttachments = new LinkedList<Attachment>();
        private LinkedList<Attachment> sideAttachments = new LinkedList<Attachment>();

        public override object AddAttachment(TransformFollower attach, bool normalized = false)
        {
            if (normalized)
                return AddAttachmentNormalized(attach);
            else
                return AddAttachmentNotNormalized(attach);
        }

        private object AddAttachmentNotNormalized(TransformFollower attach)
        {
            Attachment ret = new Attachment();
            ret.follower = attach;

            if (lastProfile == null)
                throw new InvalidOperationException("Can't child non-normalized attachments prior to the first update");

            // All the code from here down assumes the part is a convex shape, which is fair as it needs to be convex for 
            // partCollider purposes anyhow. If we allow concave shapes it will need some refinement.
            Vector3 position = attach.transform.localPosition;

            // Convert the offset into spherical coords
            float r = position.magnitude;
            float theta, phi;
            if (r > 0f)
            {
                theta = Mathf.Atan2(-position.z, position.x);
                phi = Mathf.Asin(position.y / r);
            }
            else
            {
                // move the origin to the top to avoid divide by zeros.
                r = 0.1f;
                theta = 0;
                phi = Mathf.PI / 2f;
            }


            // pt or bottom?
            if (phi != 0)
            {
                ProfilePoint topBot = (phi < 0) ? lastProfile.First.Value : lastProfile.Last.Value;

                float tbR = Mathf.Sqrt(topBot.y * topBot.y + topBot.dia * topBot.dia * 0.25f);
                float tbPhi = Mathf.Asin(topBot.y / tbR);

                if (Mathf.Abs(phi) >= Mathf.Abs(tbPhi))
                {
                    if (topBot.dia < 0.001f)
                        ret.uv = new Vector2(0.5f, 0.5f);
                    else
                        ret.uv = new Vector2(position.x / topBot.dia * 2f + 0.5f, position.z / topBot.dia * 2f + 0.5f);
                    if (phi > 0)
                    {
                        ret.location = Location.Top;
                        ret.node = topAttachments.AddLast(ret);
                        ret.follower.SetLocalRotationReference(Quaternion.LookRotation(Vector3.up, Vector3.right));
                    }
                    else
                    {
                        ret.location = Location.Bottom;
                        ret.node = bottomAttachments.AddLast(ret);
                        ret.follower.SetLocalRotationReference(Quaternion.LookRotation(Vector3.down, Vector3.left));
                    }
                    //Debug.LogWarning("Adding non-normalized attachment to position=" + position + " location=" + ret.location + " uv=" + ret.uv + " attach=" + attach.name);
                    return ret;
                }
            }

            // THis is the slope of a line projecting out towards our attachment
            float s = position.y / Mathf.Sqrt(position.x * position.x + position.z * position.z);

            ret.location = Location.Side;
            ret.uv[0] = (Mathf.InverseLerp(-Mathf.PI, Mathf.PI, theta) + 0.5f) % 1.0f;

            ProfilePoint pv = null;
            ProfilePoint pt = lastProfile.First.Value;
            for (LinkedListNode<ProfilePoint> ptNode = lastProfile.First.Next; ptNode != null; ptNode = ptNode.Next)
            {
                if (!ptNode.Value.inCollider)
                    continue;
                pv = pt;
                pt = ptNode.Value;

                float ptR = Mathf.Sqrt(pt.y * pt.y + pt.dia * pt.dia * 0.25f);
                float ptPhi = Mathf.Asin(pt.y / ptR);

                //Debug.LogWarning("ptPhi=" + ptPhi + " phi=" + phi);

                if (phi > ptPhi)
                    continue;

                // so we know the attachment is somewhere between the previous and this circle
                // Geometry: draw a line between the point (dia/2, y) in the prev circle and  (dia/2, y) in the current circle (parametric in t)
                // find the point on the line where y = s * dia / 2  and solve for t

                // r(t) = r0 + (r1-r0)t
                // y(t) = y0 + (y1-y0)t
                // y(t) = s * r(t)
                //
                // y0 + (y1-y0)t = s r0 + s (r1-r0) t
                // ((y1-y0)- s(r1-r0))t = s r0 - y0
                // t = (s r0 - y0) / ((y1-y0) - s(r1-r0))

                float r0 = pv.dia * 0.5f;
                float r1 = pt.dia * 0.5f;

                float t = (s * r0 - pv.y) / ((pt.y - pv.y) - s * (r1 - r0));

                //Debug.LogWarning(string.Format("New Attachment: pv=({0:F2}, {1:F2}) pt=({2:F2}, {3:F2}) s={4:F2} t={5:F2}", r0, pv.y, r1, pt.y, s, t));

                ret.uv[1] = Mathf.Lerp(pv.v, pt.v, t);
                if (ret.uv[1] > 1.0f)
                    Debug.LogError("result off end of segment v=" + ret.uv[1] + " pv.v=" + pv.v + " pt.v=" + pt.v + " t=" + t);

                // 
                Vector3 normal;
                Quaternion rot = SideAttachOrientation(pv, pt, theta, out normal);
                ret.follower.SetLocalRotationReference(rot);

                AddSideAttachment(ret);
                //Debug.LogWarning("Adding non-normalized attachment to position=" + position + " location=" + ret.location + " uv=" + ret.uv + " attach=" + attach.name);
                return ret;
            }

            // This should be impossible to reach
            throw new InvalidProgramException("Unreachable code reached");
        }

        private object AddAttachmentNormalized(TransformFollower attach)
        {
            Attachment ret = new Attachment();
            ret.follower = attach;

            Vector3 position = attach.transform.localPosition;

            // This is easy, just get the UV and location correctly and force an update.
            // as the position might be after some rotation and translation, it might not be exactly +/- 0.5
            if (Mathf.Abs(Mathf.Abs(position.y) - 0.5f) < 1e-5f)
            {
                if (position.y > 0)
                {
                    ret.location = Location.Top;
                    ret.uv = new Vector2(position.x + 0.5f, position.z + 0.5f);
                    ret.node = topAttachments.AddLast(ret);
                    ret.follower.SetLocalRotationReference(Quaternion.LookRotation(Vector3.up, Vector3.right));
                }
                else if (position.y < 0)
                {
                    ret.location = Location.Bottom;
                    ret.uv = new Vector2(position.x + 0.5f, position.z + 0.5f);
                    ret.node = bottomAttachments.AddLast(ret);
                    ret.follower.SetLocalRotationReference(Quaternion.LookRotation(Vector3.down, Vector3.left));
                }
            }
            else
            {
                ret.location = Location.Side;
                float theta = Mathf.Atan2(-position.z, position.x);
                ret.uv[0] = (Mathf.InverseLerp(-Mathf.PI, Mathf.PI, theta) + 0.5f) % 1.0f;
                ret.uv[1] = 0.5f - position.y;

                Vector3 normal = new Vector3(position.x * 2f, 0, position.z * 2f);
                ret.follower.SetLocalRotationReference(Quaternion.FromToRotation(Vector3.up, normal));

                // side attachments are kept sorted
                AddSideAttachment(ret);
            }
            ForceNextUpdate();

            //Debug.LogWarning("Adding normalized attachment to position=" + position + " location=" + ret.location + " uv=" + ret.uv + " attach=" + attach.name);
            return ret;
        }

        protected void MoveAttachments(LinkedList<ProfilePoint> pts)
        {
            lastProfile = pts;

            // top points
            ProfilePoint top = pts.Last.Value;
            foreach (Attachment a in topAttachments)
            {
                Vector3 pos = new Vector3(
                    (a.uv[0] - 0.5f) * top.dia * 0.5f,
                    top.y,
                    (a.uv[1] - 0.5f) * top.dia * 0.5f);
                //Debug.LogWarning("Moving attachment:" + a + " to:" + pos.ToString("F7") + " uv: " + a.uv.ToString("F5"));
                a.follower.transform.localPosition = pos;
                a.follower.Update();
            }

            // bottom points
            ProfilePoint bot = pts.First.Value;
            foreach (Attachment a in bottomAttachments)
            {
                Vector3 pos = new Vector3(
                    (a.uv[0] - 0.5f) * bot.dia * 0.5f,
                    bot.y,
                    (a.uv[1] - 0.5f) * bot.dia * 0.5f);
                //Debug.LogWarning("Moving attachment:" + a + " to:" + pos.ToString("F7") + " uv: " + a.uv.ToString("F5"));
                a.follower.transform.localPosition = pos;
                a.follower.Update();
            }

            // sides
            ProfilePoint pv = null;
            ProfilePoint pt = pts.First.Value;
            LinkedListNode<ProfilePoint> ptNode = pts.First;
            foreach (Attachment a in sideAttachments)
            {
                while (pt.v < a.uv[1])
                {
                    ptNode = ptNode.Next;
                    if (ptNode == null)
                    {
                        ptNode = pts.Last;
                        Debug.LogError("Child v greater than last node child v=" + a.uv[1] + " last node v=" + ptNode.Value.v);
                        break;
                    }
                    if (!ptNode.Value.inCollider)
                        continue;
                    pv = pt;
                    pt = ptNode.Value;
                }

                float t = Mathf.InverseLerp(pv.v, pt.v, a.uv[1]);
                //Debug.LogWarning("pv.v=" + pv.v + " pt.v=" + pt.v + " att.v=" + a.uv[1] + " t=" + t);

                // using cylindrical coords
                float r = Mathf.Lerp(pv.dia * 0.5f, pt.dia * 0.5f, t);
                float y = Mathf.Lerp(pv.y, pt.y, t);

                float theta = Mathf.Lerp(0, Mathf.PI * 2f, a.uv[0]);

                float x = Mathf.Cos(theta) * r;
                float z = -Mathf.Sin(theta) * r;

                Vector3 pos = new Vector3(x, y, z);
                //print("Moving attachment:" + a + " to:" + pos.ToString("F3"));
                a.follower.transform.localPosition = pos;

                Vector3 normal;
                Quaternion rot = SideAttachOrientation(pv, pt, theta, out normal);

                //Debug.LogWarning("Moving to orientation: normal: " + normal.ToString("F3") + " theta:" + (theta * 180f / Mathf.PI) + rot.ToStringAngleAxis());

                a.follower.transform.localRotation = rot;
                a.follower.Update();
            }
        }

        private static Quaternion SideAttachOrientation(ProfilePoint pv, ProfilePoint pt, float theta, out Vector3 normal)
        {
            normal = Quaternion.AngleAxis(theta * 180 / Mathf.PI, Vector3.up) * new Vector2(pt.y - pv.y, -(pt.dia - pv.dia) / 2f);
            return Quaternion.FromToRotation(Vector3.up, normal);
        }

        private void AddSideAttachment(Attachment ret)
        {
            for (LinkedListNode<Attachment> node = sideAttachments.First; node != null; node = node.Next)
                if (node.Value.uv[1] > ret.uv[1])
                {
                    ret.node = sideAttachments.AddBefore(node, ret);
                    return;
                }
            ret.node = sideAttachments.AddLast(ret);
        }

        public override TransformFollower RemoveAttachment(object data, bool normalize = false)
        {
            Attachment attach = (Attachment)data;
            switch (attach.location)
            {
                case Location.Top:
                    topAttachments.Remove(attach.node);
                    if (normalize)
                        attach.follower.transform.localPosition = new Vector3(attach.uv[0] - 0.5f, 0.5f, attach.uv[1] - 0.5f);
                    break;
                case Location.Bottom:
                    bottomAttachments.Remove(attach.node);
                    if (normalize)
                        attach.follower.transform.localPosition = new Vector3(attach.uv[0] - 0.5f, -0.5f, attach.uv[1] - 0.5f);
                    break;
                case Location.Side:
                    sideAttachments.Remove(attach.node);

                    if (normalize)
                    {
                        float theta = Mathf.Lerp(0, Mathf.PI * 2f, attach.uv[0]);
                        float x = Mathf.Cos(theta);
                        float z = -Mathf.Sin(theta);

                        Vector3 normal = new Vector3(x, 0, z);
                        attach.follower.transform.localPosition = new Vector3(normal.x * 0.5f, 0.5f - attach.uv[1], normal.z * 0.5f);
                        attach.follower.transform.localRotation = Quaternion.FromToRotation(Vector3.up, normal);
                    }
                    break;
            }

            if (normalize)
                attach.follower.Update();
            return attach.follower;
        }

        #endregion

        #region Mesh Writing

        public class ProfilePoint
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
                this.circ = inRender ? (circ ?? CirclePoints.ForDiameter(dia, maxCircleError, minCircleVertexes)) : null;
                this.colliderCirc = inCollider ? (colliderCirc ?? this.circ ?? CirclePoints.ForDiameter(dia, maxCircleError, minCircleVertexes)) : null;
            }

            public bool customCollider
            {
                get
                {
                    return circ != colliderCirc;
                }
            }
        }

        private LinkedList<ProfilePoint> lastProfile = null;

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

            // Move attachments first, before subdividing
            MoveAttachments(pts);

            // Horizontal profile point subdivision
            SubdivHorizontal(pts);

            // Tank stats
            float tankULength = 0;
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
                customCollider = customCollider || pt.customCollider;

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
            tankULength = Mathf.PI * sumDiameters / (last.y - first.y);

            //print("ULength=" + tankULength + " VLength=" + tankVLength);

            // set the texture scale.
            RaiseChangeTextureScale("sides", pPart.sidesMaterial, new Vector2(tankULength, tankVLength));

            m.WriteTo(sidesMesh);

            // The endcaps.
            nVrt = first.circ.totVertexes + last.circ.totVertexes;
            nTri = first.circ.totVertexes - 2 + last.circ.totVertexes - 2;
            m = new UncheckedMesh(nVrt, nTri);

            first.circ.WriteEndcap(first.dia, first.y, false, 0, 0, m, false);
            last.circ.WriteEndcap(last.dia, last.y, true, first.circ.totVertexes, (first.circ.totVertexes - 2) * 3, m, !odd);

            m.WriteTo(endsMesh);

            // build the collider mesh at a lower resolution than the visual mesh.
            if (customCollider)
            {
                //Debug.LogWarning("Collider mesh vert=" + nColVrt + " tris=" + nColTri);
                m = new UncheckedMesh(nColVrt, nColTri);
                odd = false;
                {
                    ProfilePoint prev = null;
                    int off = 0, prevOff = 0;
                    int tOff = 0;
                    foreach (ProfilePoint pt in pts)
                    {
                        if (!pt.inCollider)
                            continue;
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
                }

                if (colliderMesh == null)
                    colliderMesh = new Mesh();

                m.WriteTo(colliderMesh);

                pPart.colliderMesh = colliderMesh;
            }
            else
            {
                pPart.colliderMesh = sidesMesh;
            }

            RaiseModelAndColliderChanged();
        }

        private Mesh colliderMesh;

        /// <summary>
        /// Subdivide profile points according to the max diameter change. 
        /// </summary>
        private void SubdivHorizontal(LinkedList<ProfilePoint> pts)
        {
            ProfilePoint prev = pts.First.Value;
            ProfilePoint curr;
            for (LinkedListNode<ProfilePoint> node = pts.First.Next; node != null; node = node.Next)
            {
                curr = node.Value;
                if (!curr.inRender)
                    continue;

                float dDiameter = curr.dia - prev.dia;
                int subdiv = Math.Min((int)Math.Truncate(Mathf.Abs(dDiameter) / maxDiameterChange), 30);
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
                        float t = (float)i / (float)subdiv;
                        float tDiameter = prev.dia + dDiameter * t;
                        float tY = Mathf.Lerp(prev.y, curr.y, t);
                        float tV = Mathf.Lerp(prev.v, curr.v, t);

                        Vector2 norm;
                        if (doSlerp)
                            norm = Mathf.Sin(omega * (1f - t)) / sinOmega * prev.norm + Mathf.Sin(omega * t) / sinOmega * curr.norm;
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
            node.size = Math.Min((int)(pt.dia / pPart.diameterLargeStep), 3);

            // Breaking force and torque scales with the area of the surface (node size).
            node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);

            // Send messages for the changing of the ends
            RaiseChangeAttachNodeSize(nodeName, pt.dia, Mathf.PI * pt.dia * pt.dia * 0.25f, node.size);

            // TODO: separate out the meshes for each end so we can use the scale for texturing.
            RaiseChangeTextureScale(nodeName, pPart.endsMaterial, new Vector2(pt.dia, pt.dia));
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
                        throw new InvalidProgramException();
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

            private static List<CirclePoints> circlePoints = new List<CirclePoints>();


            private readonly int subdivCount;
            public readonly int totVertexes;
            public readonly float maxError;

            private static float maxError0 = Mathf.Sqrt(2) * (Mathf.Sin(Mathf.PI / 4.0f) - 0.5f) * 0.5f;

            private float[][] uCoords;
            private float[][] xCoords;
            private float[][] zCoords;

            private bool complete = false;

            private CirclePoints(int subdivCount)
            {
                this.subdivCount = subdivCount;
                this.totVertexes = (1 + subdivCount) * 4;

                if (subdivCount == 0)
                {
                    uCoords = new float[][] { new float[] { 0.0f }, new float[] { 0.125f } };
                    xCoords = new float[][] { new float[] { 0.0f }, new float[] { -1f / Mathf.Sqrt(2) } };
                    zCoords = new float[][] { new float[] { 1.0f }, new float[] { 1f / Mathf.Sqrt(2) } };

                    maxError = maxError0;
                    complete = true;
                }
                else
                {
                    // calculate the max error.
                    uCoords = new float[][] { new float[] { 0.0f, 1f / (float)totVertexes }, new float[] { 0.5f / (float)totVertexes, 1.5f / (float)totVertexes } };
                    float theta = uCoords[0][1] * Mathf.PI * 2.0f;
                    xCoords = new float[][] { new float[] { 0.0f, -Mathf.Sin(theta) }, new float[] { -Mathf.Sin(theta * 0.5f), -Mathf.Sin(theta * 1.5f) } };
                    zCoords = new float[][] { new float[] { 1.0f, Mathf.Cos(theta) }, new float[] { Mathf.Cos(theta * 0.5f), Mathf.Cos(theta * 1.5f) } };

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
                uCoords = new float[][] { new float[totalCoords], new float[totalCoords] };
                xCoords = new float[][] { new float[totalCoords], new float[totalCoords] };
                zCoords = new float[][] { new float[totalCoords], new float[totalCoords] };
                Array.Copy(oldUCoords[0], uCoords[0], 2);
                Array.Copy(oldXCoords[0], xCoords[0], 2);
                Array.Copy(oldYCoords[0], zCoords[0], 2);
                Array.Copy(oldUCoords[1], uCoords[1], 2);
                Array.Copy(oldXCoords[1], xCoords[1], 2);
                Array.Copy(oldYCoords[1], zCoords[1], 2);

                float denom = (float)(4 * (subdivCount + 1));
                for (int i = 2; i <= subdivCount; ++i)
                {
                    uCoords[0][i] = (float)i / denom;
                    uCoords[1][i] = ((float)i + 0.5f) / denom;

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
            /// <param name="vOff">offset into xy, verticies, and normal arrays to begin at</param>
            /// <param name="xy">xy array, data will be written</param>
            /// <param name="verticies">verticies array</param>
            /// <param name="tangents">tangents array</param>
            /// <param name="to">offset into triangles array</param>
            /// <param name="triangles"></param>
            public void WriteEndcap(float dia, float y, bool up, int vOff, int to, UncheckedMesh m, bool odd)
            {
                Complete();

                int o = odd ? 1 : 0;

                for (int i = 0; i <= subdivCount; ++i)
                {
                    int o0 = vOff + i;
                    m.uv[o0] = new Vector2((-xCoords[o][i] + 1f) * 0.5f, (-zCoords[o][i] + 1f) * 0.5f);
                    m.verticies[o0] = new Vector3(xCoords[o][i] * dia * 0.5f, y, zCoords[o][i] * dia * 0.5f);

                    int o1 = vOff + i + subdivCount + 1;
                    m.uv[o1] = new Vector2((zCoords[o][i] + 1f) * 0.5f, (-xCoords[o][i] + 1f) * 0.5f);
                    m.verticies[o1] = new Vector3(-zCoords[o][i] * dia * 0.5f, y, xCoords[o][i] * dia * 0.5f);

                    int o2 = vOff + i + 2 * (subdivCount + 1);
                    m.uv[o2] = new Vector2((xCoords[o][i] + 1f) * 0.5f, (zCoords[o][i] + 1f) * 0.5f);
                    m.verticies[o2] = new Vector3(-xCoords[o][i] * dia * 0.5f, y, -zCoords[o][i] * dia * 0.5f);

                    int o3 = vOff + i + 3 * (subdivCount + 1);
                    m.uv[o3] = new Vector2((-zCoords[o][i] + 1f) * 0.5f, (xCoords[o][i] + 1f) * 0.5f);
                    m.verticies[o3] = new Vector3(zCoords[o][i] * dia * 0.5f, y, -xCoords[o][i] * dia * 0.5f);

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

            /// <summary>
            /// Write vertexes for the circle.
            /// </summary>
            /// <param name="diameter">diameter of the circle</param>
            /// <param name="y">y coordinate</param>
            /// <param name="norm">unit normal vector along the generator curve for increasing y. The y param becomes the y of the normal, the x multiplies the normals to the circle</param>
            /// <param name="v">v coordinate for UV</param>
            /// <param name="off">offset into following arrays</param>
            /// <param name="xy">UVs to copy into</param>
            /// <param name="verticies">vertexes</param>
            /// <param name="normals">normals</param>
            /// <param name="tangents">tangents</param>
            public void WriteVertexes(float diameter, float y, float v, Vector2 norm, int off, bool odd, UncheckedMesh m)
            {
                Complete();

                int o = odd ? 1 : 0;

                for (int i = 0; i <= subdivCount; ++i)
                {
                    int o0 = off + i;
                    m.uv[o0] = new Vector2(uCoords[o][i], v);
                    m.verticies[o0] = new Vector3(xCoords[o][i] * 0.5f * diameter, y, zCoords[o][i] * 0.5f * diameter);
                    m.normals[o0] = new Vector3(xCoords[o][i] * norm.x, norm.y, zCoords[o][i] * norm.x);
                    m.tangents[o0] = new Vector4(zCoords[o][i], 0, -xCoords[o][i], -1.0f);
                    //MonoBehaviour.print("Vertex #" + i + " off=" + o0 + " u=" + xy[o0][0] + " coords=" + verticies[o0]);

                    int o1 = off + i + subdivCount + 1;
                    m.uv[o1] = new Vector2(uCoords[o][i] + 0.25f, v);
                    m.verticies[o1] = new Vector3(-zCoords[o][i] * 0.5f * diameter, y, xCoords[o][i] * 0.5f * diameter);
                    m.normals[o1] = new Vector3(-zCoords[o][i] * norm.x, norm.y, xCoords[o][i] * norm.x);
                    m.tangents[o1] = new Vector4(xCoords[o][i], 0, zCoords[o][i], -1.0f);

                    int o2 = off + i + 2 * (subdivCount + 1);
                    m.uv[o2] = new Vector2(uCoords[o][i] + 0.50f, v);
                    m.verticies[o2] = new Vector3(-xCoords[o][i] * 0.5f * diameter, y, -zCoords[o][i] * 0.5f * diameter);
                    m.normals[o2] = new Vector3(-xCoords[o][i] * norm.x, norm.y, -zCoords[o][i] * norm.x);
                    m.tangents[o2] = new Vector4(-zCoords[o][i], 0, xCoords[o][i], -1.0f);

                    int o3 = off + i + 3 * (subdivCount + 1);
                    m.uv[o3] = new Vector2(uCoords[o][i] + 0.75f, v);
                    m.verticies[o3] = new Vector3(zCoords[o][i] * 0.5f * diameter, y, -xCoords[o][i] * 0.5f * diameter);
                    m.normals[o3] = new Vector3(zCoords[o][i] * norm.x, norm.y, -xCoords[o][i] * norm.x);
                    m.tangents[o3] = new Vector4(-xCoords[o][i], 0, -zCoords[o][i], -1.0f);
                }

                // write the wrapping vertex. This is identical to the first one except for u coord += 1
                int lp = off + totVertexes;
                m.uv[lp] = new Vector2(uCoords[o][0] + 1.0f, v);
                m.verticies[lp] = m.verticies[off];
                m.normals[lp] = m.normals[off];
                m.tangents[lp] = m.tangents[off];
            }

            private const float uDelta = 1e-5f;

            public IEnumerable<Vector3> PointsXZU(float uFrom, float uTo)
            {
                Complete();

                int denom = (4 * (subdivCount + 1));

                if (uFrom <= uTo)
                {
                    int iFrom = Mathf.CeilToInt((uFrom + uDelta) * denom);
                    int iTo = Mathf.FloorToInt((uTo - uDelta) * denom);

                    if (iFrom < 0)
                    {
                        int pushUp = (-iFrom / denom + 1) * denom;
                        iFrom += pushUp;
                        iTo += pushUp;
                    }

                    for (int i = iFrom; i <= iTo; ++i)
                        yield return PointXZU(i);

                    yield break;
                }
                else
                {
                    int iFrom = Mathf.FloorToInt((uFrom - uDelta) * denom);
                    int iTo = Mathf.CeilToInt((uTo + uDelta) * denom);

                    if (iTo < 0)
                    {
                        int pushUp = (-iTo / denom + 1) * denom;
                        iFrom += pushUp;
                        iTo += pushUp;
                    }

                    for (int i = iFrom; i >= iTo; --i)
                        yield return PointXZU(i);

                    yield break;
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
            public static void WriteTriangles(CirclePoints a, int ao, CirclePoints b, int bo, int[] triangles, int too, bool odd)
            {
                int to = too;

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