using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public abstract class AbstractSurfaceOfRevolutionShape : ProceduralTankShape
{

    #region config fields

    public const float maxCircleError = 0.005f;
    public const float maxDiameterChange = 0.125f;

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

    public override object AddTankAttachment(TransformFollower attach, bool normalized = false)
    {
        if (normalized)
            return AddTankAttachmentNormalized(attach);
        else
            return AddTankAttachmentNotNormalized(attach);
    }

    private object AddTankAttachmentNotNormalized(TransformFollower attach)
    {
        Attachment ret = new Attachment();
        ret.follower = attach;

        if (lastProfile == null)
            throw new InvalidOperationException("Can't attach non-normalized attachments prior to the first update");

        // All the code from here down assumes the tank is a convex shape, which is fair as it needs to be convex for 
        // tankCollider purposes anyhow. If we allow concave shapes it will need some refinement.
        Vector3 position = attach.transform.localPosition;

        // Convert the position into spherical coords
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
            ProfilePoint topBot = (phi > 0) ? lastProfile.First.Value : lastProfile.Last.Value;

            float tbR = Mathf.Sqrt(topBot.y * topBot.y + topBot.dia * topBot.dia * 0.25f);
            float tbPhi = Mathf.Asin(topBot.y / tbR);

            if (Mathf.Abs(phi) > Mathf.Abs(tbPhi))
            {
                ret.uv = new Vector2(position.x / topBot.dia * 2f, position.z / topBot.dia * 2f);
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
                return ret;
            }
        }

        // THis is the slope of a line projecting out towards our attachment
        float s = position.y / Mathf.Sqrt(position.x * position.x + position.z * position.z);

        ret.location = Location.Side;
        ret.uv[0] = (Mathf.InverseLerp(-Mathf.PI, Mathf.PI, theta) + 0.5f) % 1.0f;

        ProfilePoint pv = lastProfile.First.Value;
        ProfilePoint pt = null;
        for (LinkedListNode<ProfilePoint> next = lastProfile.First.Next; next != null; next = next.Next, pv = pt)
        {
            pt = next.Value;

            float ptR = Mathf.Sqrt(pt.y * pt.y + pt.dia * pt.dia * 0.25f);
            float ptPhi = Mathf.Asin(pt.y / ptR);

            if (phi < ptPhi)
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

            float t = -(s * r0 - pv.y) / ((pv.y - pt.y) - s * (r1 - r0));

            ret.uv[1] = Mathf.Lerp(pv.v, pt.v, t);
            if (ret.uv[1] > 1.0f)
            {
                print("result off end of segment v=" + ret.uv[1] + " pv.v=" + pv.v + " pt.v=" + pt.v + " t=" + t);
            }

            // 
            Vector3 normal;
            Quaternion rot = SideAttachOrientation(pv.norm, pt.norm, t, theta, out normal);
            ret.follower.SetLocalRotationReference(rot);

            AddSideAttachment(ret);
            return ret;
        }

        // This should be impossible to reach
        throw new InvalidProgramException("Unreachable code reached");
    }

    private object AddTankAttachmentNormalized(TransformFollower attach)
    {
        Attachment ret = new Attachment();
        ret.follower = attach;

        Vector3 position = attach.transform.localPosition;

        // This is easy, just get the UV and location correctly and force an update.
        if (position.y == 0.5f)
        {
            ret.location = Location.Top;
            ret.uv = new Vector2(position.x, position.z);
            ret.node = topAttachments.AddLast(ret);
            ret.follower.SetLocalRotationReference(Quaternion.LookRotation(Vector3.up, Vector3.right));
        }
        else if (position.y == -0.5f)
        {
            ret.location = Location.Bottom;
            ret.uv = new Vector2(position.x, position.z);
            ret.node = bottomAttachments.AddLast(ret);
            ret.follower.SetLocalRotationReference(Quaternion.LookRotation(Vector3.down, Vector3.left));
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
        return ret;
    }

    protected void MoveAttachments(LinkedList<ProfilePoint> pts)
    {
        lastProfile = pts;

        // top points
        ProfilePoint top = pts.First.Value;
        foreach (Attachment a in topAttachments)
        {
            Vector3 pos = new Vector3(
                a.uv[0] * top.dia * 0.5f,
                top.y,
                a.uv[1] * top.dia * 0.5f);
            //print("Moving attachment:" + a + " to:" + pos);
            a.follower.transform.localPosition = pos;
        }

        // bottom points
        ProfilePoint bot = pts.Last.Value;
        foreach (Attachment a in bottomAttachments)
        {
            Vector3 pos = new Vector3(
                a.uv[0] * bot.dia * 0.5f,
                bot.y,
                a.uv[1] * bot.dia * 0.5f);
            //print("Moving attachment:" + a + " to:" + pos);
            a.follower.transform.localPosition = pos;
        }

        // sides
        LinkedListNode<ProfilePoint> ptNode = pts.First.Next;
        foreach (Attachment a in sideAttachments)
        {
            while (ptNode.Value.v < a.uv[1])
            {
                ptNode = ptNode.Next;
                if (ptNode == null)
                {
                    ptNode = pts.Last;
                    print("As I suspected... attach v=" + a.uv[1] + " last node v=" + ptNode.Value.v);
                    break;
                }

            }

            ProfilePoint pv = ptNode.Previous.Value;
            ProfilePoint pt = ptNode.Value;

            float t = Mathf.InverseLerp(pv.v, pt.v, a.uv[1]);

            // using cylindrical coords
            float r = Mathf.Lerp(pv.dia * 0.5f, pt.dia * 0.5f, t);
            float y = Mathf.Lerp(pv.y, pt.y, t);

            float theta = Mathf.Lerp(0, Mathf.PI*2f, a.uv[0]);

            float x = Mathf.Cos(theta) * r;
            float z = -Mathf.Sin(theta) * r;

            Vector3 pos = new Vector3(x, y, z);
            //print("Moving attachment:" + a + " to:" + pos.ToString("F3"));
            a.follower.transform.localPosition = pos;

            Vector3 normal;
            Quaternion rot = SideAttachOrientation(pv.norm, pt.norm, t, theta, out normal);

            //print("Moving to orientation: normal: " + normal.ToString("F3") + " theta:" + (theta * 180f / Mathf.PI) + rot.ToStringAngleAxis());

            a.follower.transform.localRotation = rot;
        }
    }

    private static Quaternion SideAttachOrientation(Vector3 pvNorm, Vector3 ptNorm, float t, float theta, out Vector3 normal)
    {
        normal = Quaternion.AngleAxis(theta*180/Mathf.PI, Vector3.up) * Vector3.Slerp(pvNorm, ptNorm, t);
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

    public override TransformFollower RemoveTankAttachment(object data, bool normalize = false)
    {
        Attachment attach = (Attachment)data;
        switch (attach.location)
        {
            case Location.Top:
                topAttachments.Remove(attach.node);
                if (normalize)
                    attach.follower.transform.localPosition = new Vector3(attach.uv[0], 0.5f, attach.uv[1]);
                break;
            case Location.Bottom:
                bottomAttachments.Remove(attach.node);
                if (normalize)
                    attach.follower.transform.localPosition = new Vector3(attach.uv[0], -0.5f, attach.uv[1]);
                break;
            case Location.Side:
                sideAttachments.Remove(attach.node);

                if (normalize)
                {
                    float theta = Mathf.Lerp(0, Mathf.PI * 2f, attach.uv[0]);
                    float x = Mathf.Cos(theta);
                    float z = -Mathf.Sin(theta);

                    Vector3 normal = new Vector3(x, 0, z);
                    attach.follower.transform.localPosition = new Vector3(normal.x*0.5f, 0.5f - attach.uv[1], normal.z*0.5f);
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
        public readonly float v;
        public readonly bool inCollider;

        // the normal as a 2 component unit vector (dia, y)
        // eg: for cylinders this would be (1,0), for endcaps (0,+/-1)
        public readonly Vector2 norm;

        public readonly CirclePoints circ;

        public ProfilePoint(float dia, float y, float v, Vector2 norm, bool inCollider = true, CirclePoints circ = null)
        {
            this.dia = dia;
            this.y = y;
            this.v = v;
            this.norm = norm;
            this.circ = circ ?? CirclePoints.ForDiameter(dia, maxCircleError);
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
        UpdateNodeSize(pts.First(), topNodeName);
        UpdateNodeSize(pts.Last(), bottomNodeName);



        // Move attachments first, before subdividing
        MoveAttachments(pts);

        // Horizontal profile point subdivision
        SubdivHorizontal(pts);

        // Tank stats
        float tankVolume = 0;
        float tankULength = 0;
        float tankVLength = 0;

        int nVrt = 0;
        int nTri = 0;

        ProfilePoint first = pts.First.Value;
        ProfilePoint last = pts.Last.Value;

        {
            int i = 0;
            foreach (ProfilePoint pt in pts)
            {
                nVrt += pt.circ.totVertexes + 1;

                // one for above, one for below
                nTri += 2 * pt.circ.totVertexes;

                ++i;
                last = pt;
            }
        }

        // Have double counted for the first and last circles.
        nTri -= first.circ.totVertexes + last.circ.totVertexes;

        UncheckedMesh m = new UncheckedMesh(nVrt, nTri);

        float sumDiameters = 0;

        {
            ProfilePoint prev = null;
            int vOff = 0, prevVOff = 0;
            int tOff = 0;
            foreach (ProfilePoint pt in pts)
            {
                pt.circ.WriteVertexes(diameter: pt.dia, y: pt.y, v: pt.v, norm: pt.norm, off: vOff, m: m);
                if (prev != null)
                {
                    CirclePoints.WriteTriangles(prev.circ, prevVOff, pt.circ, vOff, m.triangles, tOff * 3);
                    tOff += prev.circ.totVertexes + pt.circ.totVertexes;

                    // Work out the area of the truncated cone

                    // integral_y1^y2 pi R(y)^2 dy   where R(y) = ((r2-r1)(y-y1))/(r2-r1) + r1   Integrate circles along a line
                    // integral_y1^y2 pi ( ((r2-r1)(y-y1))/(r2-r1) + r1) ^2 dy                Substituted in formula.
                    // == -1/3 pi (y1-y2) (r1^2+r1*r2+r2^2)                                   Do the calculus
                    // == -1/3 pi (y1-y2) (d1^2/4+d1*d2/4+d2^2/4)                             r = d/2
                    // == -1/12 pi (y1-y2) (d1^2+d1*d2+d2^2)                                  Take out the factor
                    tankVolume += (Mathf.PI * (prev.y - pt.y) * (prev.dia * prev.dia + prev.dia * pt.dia + pt.dia * pt.dia)) / 12f;

                    float dy = (prev.y - pt.y);
                    float dr = (prev.dia - pt.dia) * 0.5f;

                    //print("dy=" + dy + " dr=" + dr + " len=" + Mathf.Sqrt(dy * dy + dr * dr).ToString("F3"));
                    tankVLength += Mathf.Sqrt(dy * dy + dr * dr);

                    // average diameter weighted by dy
                    sumDiameters += (pt.dia + prev.dia) * dy;
                }

                prev = pt;
                prevVOff = vOff;
                vOff += pt.circ.totVertexes + 1;
            }
        }

        // Use the weighted average diameter across segments to set the ULength
        tankULength = Mathf.PI * sumDiameters / (first.y - last.y);

        //print("ULength=" + tankULength + " VLength=" + tankVLength);

        // set the properties.
        this.tankVolume = tankVolume;
        this.tankTextureScale = new Vector2(tankULength, tankVLength);

        m.WriteTo(sidesMesh);

        // The endcaps.
        nVrt = first.circ.totVertexes + last.circ.totVertexes;
        nTri = first.circ.totVertexes - 2 + last.circ.totVertexes - 2;
        m = new UncheckedMesh(nVrt, nTri);

        first.circ.WriteEndcap(first.dia, first.y, true, 0, 0, m);
        last.circ.WriteEndcap(last.dia, last.y, false, first.circ.totVertexes, (first.circ.totVertexes - 2) * 3, m);

        m.WriteTo(endsMesh);

        // TODO: build the collider mesh at a lower resolution than the visual mesh.
        tank.SetColliderMeshes(endsMesh, sidesMesh);
    }

    /// <summary>
    /// Subdivide profile points according to the max diameter change. 
    /// </summary>
    private void SubdivHorizontal(LinkedList<ProfilePoint> pts)
    {
        ProfilePoint prev = pts.First.Value;
        ProfilePoint curr;
        for (LinkedListNode<ProfilePoint> node = pts.First.Next; node != null; prev = curr, node = node.Next)
        {
            curr = node.Value;

            float dDiameter = curr.dia - prev.dia;
            int subdiv = (int)Math.Truncate(Mathf.Abs(dDiameter) / maxDiameterChange);
            if (subdiv <= 1)
                continue;

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
    }

    private void UpdateNodeSize(ProfilePoint pt, string nodeName)
    {
        AttachNode node = part.attachNodes.Find(n => n.id == nodeName);
        if (node == null)
            return;
        node.size = Math.Min((int)(pt.dia / tank.diameterLargeStep), 3);
    }

    #endregion

    #region Circle Points

    public class CirclePoints
    {
        public static CirclePoints ForDiameter(float diameter, float maxError)
        {
            // I'm not sure this lock is strictly required, but will do i anyhow.
            lock (circlePoints)
            {
                int idx = circlePoints.FindIndex(v => (v.maxError * diameter) < maxError);
                switch (idx)
                {
                    case 0:
                        return circlePoints[0];
                    case -1:
                        CirclePoints ret;
                        do
                        {
                            ret = new CirclePoints(circlePoints.Count);
                            circlePoints.Add(ret);
                        } while ((ret.maxError * diameter) > maxError);
                        return ret;
                    default:
                        return circlePoints[idx - 1];
                }
            }

        }

        private static List<CirclePoints> circlePoints = new List<CirclePoints>();


        public readonly int subdivCount;
        public readonly int totVertexes;
        public readonly float maxError;

        private static float maxError0 = Mathf.Sqrt(2) * (Mathf.Sin(Mathf.PI / 4.0f) - 0.5f) * 0.5f;

        private float[] uCoords;
        private float[] xCoords;
        private float[] zCoords;

        private bool complete = false;

        public CirclePoints(int subdivCount)
        {
            this.subdivCount = subdivCount;
            this.totVertexes = (1 + subdivCount) * 4;

            if (subdivCount == 0)
            {
                uCoords = new float[] { 0.0f };
                xCoords = new float[] { 0.0f };
                zCoords = new float[] { 1.0f };

                maxError = maxError0;
                complete = true;
            }
            else
            {
                // calculate the max error.
                uCoords = new float[] { 0.0f, (float)1 / (float)(subdivCount + 1) / 4.0f };
                float theta = uCoords[1] * Mathf.PI * 2.0f;
                xCoords = new float[] { 0.0f, Mathf.Sin(theta) };
                zCoords = new float[] { 1.0f, Mathf.Cos(theta) };

                float dX = Mathf.Sin(theta / 2.0f) - xCoords[1] / 2.0f;
                float dY = Mathf.Cos(theta / 2.0f) - zCoords[1] / 2.0f - 0.5f;

                maxError = Mathf.Sqrt(dX * dX + dY * dY) * 0.5f;

                complete = subdivCount == 1;
            }
        }

        private void Complete()
        {
            if (complete)
                return;

            int totalCoords = subdivCount + 1;

            float[] oldUCoords = uCoords;
            float[] oldXCoords = xCoords;
            float[] oldYCoords = zCoords;
            uCoords = new float[totalCoords];
            xCoords = new float[totalCoords];
            zCoords = new float[totalCoords];
            Array.Copy(oldUCoords, uCoords, 2);
            Array.Copy(oldXCoords, xCoords, 2);
            Array.Copy(oldYCoords, zCoords, 2);

            float denom = (float)(4 * (subdivCount + 1));
            for (int i = 2; i <= subdivCount; ++i)
            {
                uCoords[i] = (float)i / denom;
                float theta = uCoords[i] * Mathf.PI * 2.0f;
                xCoords[i] = Mathf.Sin(theta);
                zCoords[i] = Mathf.Cos(theta);
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
        public void WriteEndcap(float dia, float y, bool up, int vOff, int to, UncheckedMesh m)
        {
            Complete();

            for (int i = 0; i <= subdivCount; ++i)
            {
                int o0 = vOff + i;
                m.uv[o0] = new Vector2((-xCoords[i] + 1f) * 0.5f, (zCoords[i] + 1f) * 0.5f);
                m.verticies[o0] = new Vector3(xCoords[i] * dia * 0.5f, y, zCoords[i] * dia * 0.5f);

                int o1 = vOff + i + subdivCount + 1;
                m.uv[o1] = new Vector2((-zCoords[i] + 1f) * 0.5f, (-xCoords[i] + 1f) * 0.5f);
                m.verticies[o1] = new Vector3(zCoords[i] * dia * 0.5f, y, -xCoords[i] * dia * 0.5f);

                int o2 = vOff + i + 2 * (subdivCount + 1);
                m.uv[o2] = new Vector2((xCoords[i] + 1f) * 0.5f, (-zCoords[i] + 1f) * 0.5f);
                m.verticies[o2] = new Vector3(-xCoords[i] * dia * 0.5f, y, -zCoords[i] * dia * 0.5f);

                int o3 = vOff + i + 3 * (subdivCount + 1);
                m.uv[o3] = new Vector2((zCoords[i] + 1f) * 0.5f, (xCoords[i] + 1f) * 0.5f);
                m.verticies[o3] = new Vector3(-zCoords[i] * dia * 0.5f, y, xCoords[i] * dia * 0.5f);

                m.tangents[o0] = m.tangents[o1] = m.tangents[o2] = m.tangents[o3] = new Vector4(-1, 0, 0, up ? 1 : -1);
                m.normals[o0] = m.normals[o1] = m.normals[o2] = m.normals[o3] = new Vector3(0, up ? 1 : -1, 0);
            }

            for (int i = 1; i < totVertexes - 1; ++i)
            {
                m.triangles[to++] = vOff;
                m.triangles[to++] = vOff + i + (up ? 0 : 1);
                m.triangles[to++] = vOff + i + (up ? 1 : 0);
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
        public void WriteVertexes(float diameter, float y, float v, Vector2 norm, int off, UncheckedMesh m)
        {
            Complete();
            for (int i = 0; i <= subdivCount; ++i)
            {
                int o0 = off + i;
                m.uv[o0] = new Vector2(uCoords[i], v);
                m.verticies[o0] = new Vector3(xCoords[i] * 0.5f * diameter, y, zCoords[i] * 0.5f * diameter);
                m.normals[o0] = new Vector3(xCoords[i] * norm.x, norm.y, zCoords[i] * norm.x);
                m.tangents[o0] = new Vector4(zCoords[i], 0, -xCoords[i], 1.0f);
                //MonoBehaviour.print("Vertex #" + i + " off=" + o0 + " u=" + xy[o0][0] + " coords=" + verticies[o0]);

                int o1 = off + i + subdivCount + 1;
                m.uv[o1] = new Vector2(uCoords[i] + 0.25f, v);
                m.verticies[o1] = new Vector3(zCoords[i] * 0.5f * diameter, y, -xCoords[i] * 0.5f * diameter);
                m.normals[o1] = new Vector3(zCoords[i] * norm.x, norm.y, -xCoords[i] * norm.x);
                m.tangents[o1] = new Vector4(-xCoords[i], 0, -zCoords[i], 1.0f);

                int o2 = off + i + 2 * (subdivCount + 1);
                m.uv[o2] = new Vector2(uCoords[i] + 0.50f, v);
                m.verticies[o2] = new Vector3(-xCoords[i] * 0.5f * diameter, y, -zCoords[i] * 0.5f * diameter);
                m.normals[o2] = new Vector3(-xCoords[i] * norm.x, norm.y, -zCoords[i] * norm.x);
                m.tangents[o2] = new Vector4(-zCoords[i], 0, xCoords[i], 1.0f);

                int o3 = off + i + 3 * (subdivCount + 1);
                m.uv[o3] = new Vector2(uCoords[i] + 0.75f, v);
                m.verticies[o3] = new Vector3(-zCoords[i] * 0.5f * diameter, y, xCoords[i] * 0.5f * diameter);
                m.normals[o3] = new Vector3(-zCoords[i] * norm.x, norm.y, xCoords[i] * norm.x);
                m.tangents[o3] = new Vector4(xCoords[i], 0, zCoords[i], 1.0f);
            }

            // write the wrapping vertex. This is identical to the first one except for u coord = 1
            int lp = off + totVertexes;
            m.uv[lp] = new Vector2(1.0f, v);
            m.verticies[lp] = m.verticies[off];
            m.normals[lp] = m.normals[off];
            m.tangents[lp] = m.tangents[off];
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
        public static void WriteTriangles(CirclePoints a, int ao, CirclePoints b, int bo, int[] triangles, int to)
        {
            bool flip = false;
            if (a.totVertexes < b.totVertexes)
            {
                Swap(ref a, ref b);
                Swap(ref ao, ref bo);
                //MonoBehaviour.print("Flipping");
                flip = true;
            }

            int bq = b.subdivCount + 1;
            int aq = a.subdivCount + 1;

            int bi = 0;
            bool bcomplete = false;
            for (int ai = 0; ai < aq; ++ai)
            {
                //MonoBehaviour.print("A-tri #" + ai + " tOff=" + to);
                triangles[to++] = ao + ai;
                triangles[to++] = flip ? (ao + ai + 1) : (bo + bi);
                triangles[to++] = flip ? (bo + bi) : (ao + ai + 1);
                //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                triangles[to++] = ao + ai + aq;
                triangles[to++] = flip ? (ao + ai + 1 + aq) : (bo + bi + bq);
                triangles[to++] = flip ? (bo + bi + bq) : (ao + ai + 1 + aq);
                //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                triangles[to++] = ao + ai + 2 * aq;
                triangles[to++] = flip ? (ao + ai + 1 + 2 * aq) : (bo + bi + 2 * bq);
                triangles[to++] = flip ? (bo + bi + 2 * bq) : (ao + ai + 1 + 2 * aq);
                //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                triangles[to++] = ao + ai + 3 * aq;
                triangles[to++] = flip ? (ao + ai + 1 + 3 * aq) : (bo + bi + 3 * bq);
                triangles[to++] = flip ? (bo + bi + 3 * bq) : (ao + ai + 1 + 3 * aq);
                //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                if (bcomplete)
                    continue;

                float nxau = (ai + 1 < aq) ? a.uCoords[ai + 1] : 0.25f;
                float nxbu = (bi + 1 < bq) ? b.uCoords[bi + 1] : 0.25f;
                //MonoBehaviour.print("Offsets: nxau=" + nxau + " nxbu=" + nxbu + " (nxau-au)=" + (nxau - a.uCoords[ai]) + " (nxbu-nxau)=" + (nxbu - nxau));

                if ((nxau - a.uCoords[ai]) > (nxbu - nxau))
                {
                    //MonoBehaviour.print("B-tri #" + bi + " tOff=" + to);
                    triangles[to++] = bo + bi;
                    triangles[to++] = flip ? (ao + ai + 1) : (bo + bi + 1);
                    triangles[to++] = flip ? (bo + bi + 1) : (ao + ai + 1);
                    //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                    triangles[to++] = bo + bi + bq;
                    triangles[to++] = flip ? (ao + ai + 1 + aq) : (bo + bi + 1 + bq);
                    triangles[to++] = flip ? (bo + bi + 1 + bq) : (ao + ai + 1 + aq);
                    //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                    triangles[to++] = bo + bi + 2 * bq;
                    triangles[to++] = flip ? (ao + ai + 1 + 2 * aq) : (bo + bi + 1 + 2 * bq);
                    triangles[to++] = flip ? (bo + bi + 1 + 2 * bq) : (ao + ai + 1 + 2 * aq);
                    //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                    triangles[to++] = bo + bi + 3 * bq;
                    triangles[to++] = flip ? (ao + ai + 1 + 3 * aq) : (bo + bi + 1 + 3 * bq);
                    triangles[to++] = flip ? (bo + bi + 1 + 3 * bq) : (ao + ai + 1 + 3 * aq);
                    //MonoBehaviour.print(" (" + triangles[to - 3] + ", " + triangles[to - 2] + ", " + triangles[to - 1] + ") ");

                    bcomplete = (++bi == bq);
                }
            }
        }

        private static void Swap<T>(ref T one, ref T two)
        {
            T tmp = one;
            one = two;
            two = tmp;
        }

    }
    #endregion

}
