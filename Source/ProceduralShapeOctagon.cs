using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ProceduralParts
{
    class ProceduralShapeOctagon : ProceduralAbstractShape
    {
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "F3", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float diameter = 1f;
        private float oldDiameter;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Corners", guiUnits = "#", guiFormat = "F0"), UI_FloatRange(minValue = 4, maxValue = 8, stepIncrement = 2, scene = UI_Scene.Editor)]
        public float cornerCount = 8;
        private int oldCornerCount;

        [KSPField]
        public string TopNodeName = "top";

        [KSPField]
        public string BottomNodeName = "bottom";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float Length = 1f;
        private float oldLength;

        public int CornerCount => (int)cornerCount;
        private float CornerCenterCornerAngle => 2 * Mathf.PI / CornerCount;
        private float EdgeToEdgeAngle => Mathf.PI - CornerCenterCornerAngle;
        private float StartAngle => 1.5f * Mathf.PI - CornerCenterCornerAngle / 2f;
        private int SideTriangles => CornerCount * 2;
        private int TrianglesPerCap => CornerCount - 2;

        private float NormHalfSideLength => NormSideLength / 2;
        private float NormSideLength => Mathf.Tan(CornerCenterCornerAngle / 2);
        private const float NormRadius = 0.5f;
        private const float NormHalfHeight = 0.5f;
        private float InnerDiameter
        {
            get => diameter;
            set => diameter = value;
        }

        private float HalfSideLength => NormHalfSideLength * InnerDiameter;
        private float InnerRadius => NormRadius * InnerDiameter;
        private float OuterRadius => InnerRadius / Mathf.Cos(CornerCenterCornerAngle);
        private float HalfHeight => NormHalfHeight * Length;
        private float Area => InnerRadius * HalfSideLength * CornerCount;
        private float VolumeCalculated => Area * Length;
        private int SideVerticesPerCap => CornerCount * 2;
        private float NormHorizontalDiameter => Mathf.Cos(StartAngle - (CornerCount - 2) / 4 * CornerCenterCornerAngle) * -2;

        //4: 1.25 Pi + 0 Pi => 1.25 Pi


        public override void OnStart(StartState state)
        {
            UpdateTechConstraints();
        }

        public override Vector3 FromCylindricCoordinates(ShapeCoordinates coords)
        {
            var position = new Vector3();

            switch (coords.HeightMode)
            {
                case ShapeCoordinates.YMode.RELATIVE_TO_SHAPE:
                    position.y = HalfHeight * coords.y;
                    break;

                case ShapeCoordinates.YMode.OFFSET_FROM_SHAPE_CENTER:
                    position.y = coords.y;
                    break;

                case ShapeCoordinates.YMode.OFFSET_FROM_SHAPE_BOTTOM:
                    position.y = coords.y - HalfHeight;
                    break;

                case ShapeCoordinates.YMode.OFFSET_FROM_SHAPE_TOP:
                    position.y = coords.y + HalfHeight;
                    break;
                default:
                    Debug.LogError("Can not handle PartCoordinate attribute: " + coords.HeightMode);
                    position.y = 0.0f;
                    break;
            }

            var radius = coords.r;

            if (coords.RadiusMode != ShapeCoordinates.RMode.OFFSET_FROM_SHAPE_CENTER)
            {
                radius = coords.RadiusMode == ShapeCoordinates.RMode.OFFSET_FROM_SHAPE_RADIUS ? InnerRadius + coords.r : InnerRadius * coords.r;
            }

            var theta = Mathf.Lerp(0, Mathf.PI * 2f, coords.u);

            position.x = Mathf.Cos(theta) * radius;
            position.z = -Mathf.Sin(theta) * radius;

            Log("FromCylindricCoordinates called: " + coords + ", returned pos: " + position);

            return position;
        }

        private static void Log(string message)
        {
            Debug.Log("[PP] " + message);
        }

        public override void GetCylindricCoordinates(Vector3 position, ShapeCoordinates shapeCoordinates)
        {
            Log("GetCylindricCoordinates called: pos:" + position + ", coords:" + shapeCoordinates);
            var direction = new Vector2(position.x, position.z);

            switch (shapeCoordinates.HeightMode)
            {
                case ShapeCoordinates.YMode.RELATIVE_TO_SHAPE:
                    shapeCoordinates.y = position.y / HalfHeight;
                    if (float.IsNaN(shapeCoordinates.y))
                        shapeCoordinates.y = 0f;
                    break;

                case ShapeCoordinates.YMode.OFFSET_FROM_SHAPE_CENTER:
                    shapeCoordinates.y = position.y;
                    break;

                case ShapeCoordinates.YMode.OFFSET_FROM_SHAPE_BOTTOM:
                    shapeCoordinates.y = position.y - -HalfHeight;
                    break;

                case ShapeCoordinates.YMode.OFFSET_FROM_SHAPE_TOP:
                    shapeCoordinates.y = position.y - HalfHeight;
                    break;
                default:
                    Debug.LogError("Can not handle PartCoordinate attribute: " + shapeCoordinates.HeightMode);
                    shapeCoordinates.y = 0.0f;
                    break;
            }


            shapeCoordinates.r = 0;

            var theta = Mathf.Atan2(-direction.y, direction.x);

            shapeCoordinates.u = (Mathf.InverseLerp(-Mathf.PI, Mathf.PI, theta) + 0.5f) % 1.0f;
            if (float.IsNaN(shapeCoordinates.u))
                shapeCoordinates.u = 0f;

            if (shapeCoordinates.RadiusMode == ShapeCoordinates.RMode.OFFSET_FROM_SHAPE_CENTER)
            {
                shapeCoordinates.r = direction.magnitude;
                return;
            }

            shapeCoordinates.r = shapeCoordinates.RadiusMode == ShapeCoordinates.RMode.OFFSET_FROM_SHAPE_RADIUS ?
                direction.magnitude - InnerRadius :
                direction.magnitude / InnerRadius; // RELATIVE_TO_SHAPE_RADIUS

            // sometimes, if the shapes radius is 0, r rersults in NaN
            if (float.IsNaN(shapeCoordinates.r) || float.IsPositiveInfinity(shapeCoordinates.r) || float.IsNegativeInfinity(shapeCoordinates.r))
            {
                shapeCoordinates.r = 0;
            }
        }

        public override void UpdateTechConstraints()
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (PPart.lengthMin == PPart.lengthMax)
                Fields["Length"].guiActiveEditor = false;
            else
            {
                var lengthEdit = (UI_FloatEdit)Fields["Length"].uiControlEditor;
                lengthEdit.maxValue = PPart.lengthMax;
                lengthEdit.minValue = PPart.lengthMin;
                lengthEdit.incrementLarge = PPart.lengthLargeStep;
                lengthEdit.incrementSmall = PPart.lengthSmallStep;
                Length = Mathf.Clamp(Length, PPart.lengthMin, PPart.lengthMax);
            }

            if (PPart.diameterMin == PPart.diameterMax)
                Fields["Diameter"].guiActiveEditor = false;
            else
            {
                var diameterEdit = (UI_FloatEdit)Fields["Diameter"].uiControlEditor;
                if (null != diameterEdit)
                {
                    diameterEdit.maxValue = PPart.diameterMax;
                    diameterEdit.minValue = PPart.diameterMin;
                    diameterEdit.incrementLarge = PPart.diameterLargeStep;
                    diameterEdit.incrementSmall = PPart.diameterSmallStep;
                    InnerDiameter = Mathf.Clamp(InnerDiameter, PPart.diameterMin, PPart.diameterMax);
                }
                else
                    Debug.LogError("*PP* could not find field 'diameter'");
            }
        }

        public override void UpdateTFInterops()
        {
        }

        protected override void UpdateShape(bool force)
        {
            if (!force && InnerDiameter == oldDiameter && Length == oldLength && CornerCount == oldCornerCount)
            {
                return;
            }
            Debug.Log($"UpdateShape called: {force}, dia: {InnerDiameter}, oldDia: {oldDiameter}, length: {Length}, oldLength: {oldLength}");

            RecalculateVolume();

            UpdateNodeSize(TopNodeName);
            UpdateNodeSize(BottomNodeName);
            MoveAttachments();
            GenerateSideMesh();
            GenerateCapMesh();
            GenerateColliderMesh();

            UpdateProps();
            oldLength = Length;
            oldDiameter = InnerDiameter;
            oldCornerCount = CornerCount;
            RaiseModelAndColliderChanged();
        }

        private void RecalculateVolume()
        {
            var volume = VolumeCalculated;

            if (HighLogic.LoadedSceneIsEditor)
            {
                volume = ClampToVolumeRestrictions(volume);
            }
            Volume = volume;
        }

        private float ClampToVolumeRestrictions(float volume)
        {
            var oldVolume = volume;
            volume = Mathf.Clamp(volume, PPart.volumeMin, PPart.volumeMax);
            if (volume != oldVolume)
            {
                var excessVol = oldVolume - volume;
                if (oldDiameter != InnerDiameter)
                {
                    var requiredDiameter = Mathf.Sqrt(volume / Length / CornerCount / NormHalfSideLength / NormRadius);
                    InnerDiameter = TruncateForSlider(requiredDiameter, -excessVol);
                }
                else
                {
                    var requiredLength = volume / Area;
                    Length = TruncateForSlider(requiredLength, -excessVol);
                }
                volume = VolumeCalculated;
                RefreshPartEditorWindow();
            }

            return volume;
        }

        private void UpdateProps()
        {
            foreach (var pm in GetComponents<PartModule>())
            {
                var prop = pm as IProp;
                if (null != prop)
                    prop.UpdateProp();
            }
        }

        private void GenerateColliderMesh()
        {
            var mesh = new UncheckedMesh(CornerCount * 2, SideTriangles);
            GenerateCapVertices(mesh, -HalfHeight, 0);
            GenerateCapVertices(mesh, HalfHeight, CornerCount);
            GenerateSideTriangles(mesh, CornerCount, 1);

            var colliderMesh = new Mesh();
            mesh.WriteTo(colliderMesh);
            PPart.ColliderMesh = colliderMesh;
        }

        private void GenerateCapMesh()
        {
            var mesh = new UncheckedMesh(CornerCount * 2, TrianglesPerCap * 2);
            GenerateCapVertices(mesh, -HalfHeight, 0);
            GenerateCapVertices(mesh, HalfHeight, CornerCount);
            GenerateCapTriangles(mesh, false);
            GenerateCapTriangles(mesh, true);

            WriteToAppropriateMesh(mesh, PPart.EndsIconMesh, EndsMesh);
        }

        private void GenerateSideMesh()
        {
            var mesh = new UncheckedMesh(SideVerticesPerCap * 2, SideTriangles);
            GenerateSideVertices(mesh, -HalfHeight, 0, 0);
            GenerateSideVertices(mesh, HalfHeight, 1, SideVerticesPerCap);
            GenerateSideTriangles(mesh, SideVerticesPerCap, 2);

            var tankULength = CornerCount * NormSideLength * InnerDiameter * 2;
            var tankVLength = Length;

            //print("ULength=" + tankULength + " VLength=" + tankVLength);

            // set the texture scale.
            RaiseChangeTextureScale("sides", PPart.SidesMaterial, new Vector2(tankULength, tankVLength));
            WriteToAppropriateMesh(mesh, PPart.SidesIconMesh, SidesMesh);
        }

        private void UpdateNodeSize(string nodeName)
        {
            var node = part.attachNodes.Find(n => n.id == nodeName);
            if (node == null)
                return;
            node.size = Math.Min((int)(InnerDiameter / PPart.diameterLargeStep), 3);

            // Breaking force and torque scales with the area of the surface (node size).
            node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);

            // Send messages for the changing of the ends
            RaiseChangeAttachNodeSize(node, InnerDiameter, Mathf.PI * InnerDiameter * InnerDiameter * 0.25f);

            // TODO: separate out the meshes for each end so we can use the scale for texturing.
            RaiseChangeTextureScale(nodeName, PPart.EndsMaterial, new Vector2(InnerDiameter, InnerDiameter));
        }

        private static void WriteToAppropriateMesh(UncheckedMesh mesh, Mesh iconMesh, Mesh normalMesh)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                mesh.WriteTo(iconMesh);
            }
            else
            {
                mesh.WriteTo(normalMesh);
            }
        }

        private void GenerateSideTriangles(UncheckedMesh mesh, int numberOfCapVertices, int verticesPerCorner)
        {
            for (var i = 0; i < CornerCount; i++)
            {
                var baseVertex = i * verticesPerCorner + verticesPerCorner - 1;
                mesh.triangles[i * 6] = baseVertex;
                mesh.triangles[i * 6 + 1] = baseVertex + numberOfCapVertices;
                mesh.triangles[i * 6 + 2] = (baseVertex + 1) % numberOfCapVertices;

                mesh.triangles[i * 6 + 3] = (baseVertex + 1) % numberOfCapVertices;
                mesh.triangles[i * 6 + 4] = baseVertex + numberOfCapVertices;
                mesh.triangles[i * 6 + 5] = (baseVertex + 1) % numberOfCapVertices + numberOfCapVertices;
            }
        }

        private void GenerateCapTriangles(UncheckedMesh mesh, bool up)
        {
            var triangleOffset = up ? TrianglesPerCap * 3 : 0;
            var vertexOffset = up ? CornerCount : 0;
            for (var i = 0; i < TrianglesPerCap; i++)
            {
                mesh.triangles[i * 3 + triangleOffset] = vertexOffset;
                mesh.triangles[i * 3 + 1 + triangleOffset] = (up ? i + 2 : i + 1) + vertexOffset;
                mesh.triangles[i * 3 + 2 + triangleOffset] = (up ? i + 1 : i + 2) + vertexOffset;
            }
        }

        private void GenerateSideVertices(UncheckedMesh mesh, float y, float v, int offset)
        {
            for(var cornerNumber = 0; cornerNumber < CornerCount; cornerNumber++)
            {
                CreateSideCornerVertices(mesh, y, v, offset, cornerNumber);
            }
        }

        private void CreateSideCornerVertices(UncheckedMesh mesh, float y, float v, int offset, int cornerNumber)
        {
            var cornerAngle = GetCornerAngle(cornerNumber);
            var cornerVector = CreateVectorFromAngle(cornerAngle, y, InnerRadius);
            var verticesPerCorner = 2;
            Log("Generating vertex: " + cornerVector);

            for (var vertexCornerIndex = 0; vertexCornerIndex < verticesPerCorner; vertexCornerIndex++)
            {
                var vertexIndex = offset + cornerNumber * verticesPerCorner + vertexCornerIndex;
                mesh.vertices[vertexIndex] = cornerVector;

                SetSideVertexData(mesh, v, cornerNumber, cornerAngle, vertexCornerIndex, vertexIndex);
            }
            mesh.uv[offset].x = 1;
        }

        private void SetSideVertexData(UncheckedMesh mesh, float v, int cornerNumber, float cornerAngle, int vertexCornerIndex, int vertexIndex)
        {
            mesh.uv[vertexIndex] = new Vector2((float)cornerNumber / CornerCount, v);

            var normalAngle = cornerAngle + CornerCenterCornerAngle / 2 * (-1 + 2 * vertexCornerIndex);
            var normal = CreateVectorFromAngle(normalAngle, 0, 1);
            mesh.normals[vertexIndex] = normal;
            mesh.tangents[vertexIndex] = new Vector4(normal.z, 0, -normal.x, 1f);
        }

        private void CreateCapCornerVertices(UncheckedMesh mesh, float y, int offset, int cornerNumber)
        {
            var cornerAngle = GetCornerAngle(cornerNumber);
            var cornerVector = CreateVectorFromAngle(cornerAngle, y, InnerRadius);
            var verticesPerCorner = 1;

            for (var vertexCornerIndex = 0; vertexCornerIndex < verticesPerCorner; vertexCornerIndex++)
            {
                var vertexIndex = offset + cornerNumber * verticesPerCorner + vertexCornerIndex;
                mesh.vertices[vertexIndex] = cornerVector;

                SetCapVertexData(mesh, cornerVector, vertexIndex, y > 0);
            }
        }

        private void SetCapVertexData(UncheckedMesh mesh, Vector3 cornerVector, int vertexIndex, bool up)
        {
            mesh.uv[vertexIndex] = new Vector2(cornerVector.x, cornerVector.z) / InnerDiameter + new Vector2(0.5f, 0.5f); // / MaxHorizontalDiameter;
            mesh.normals[vertexIndex] = new Vector3(0, up ? 1 : -1, 0);
            mesh.tangents[vertexIndex] = new Vector4(1, 0, 0, 1f);
        }

        private float GetCornerAngle(int cornerNumber) => StartAngle + CornerCenterCornerAngle * cornerNumber;
        private static Vector3 CreateVectorFromAngle(float angle, float y, float radius) => new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

        private void GenerateCapVertices(UncheckedMesh mesh, float y, int offset)
        {
            for (var cornerNumber = 0; cornerNumber < CornerCount; cornerNumber++)
            {
                CreateCapCornerVertices(mesh, y, offset, cornerNumber);
            }
        }

        public override object AddAttachment(TransformFollower transformFollower, bool normalized)
        {
            return normalized ? AddNormalizedAttachment(transformFollower) : AddNonNormalizedAttachment(transformFollower);
        }

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

        private readonly LinkedList<Attachment> topAttachments = new LinkedList<Attachment>();
        private readonly LinkedList<Attachment> bottomAttachments = new LinkedList<Attachment>();
        private readonly LinkedList<Attachment> sideAttachments = new LinkedList<Attachment>();

        private object AddNonNormalizedAttachment(TransformFollower transformFollower)
        {
            var position = transformFollower.transform.localPosition;

            var attachmentHeightToRadiusSquared = position.y * position.y / (position.x * position.x + position.z * position.z);
            var tankCornerHeightToRadiusSquared = Length * Length / (InnerDiameter * InnerDiameter);

            if(attachmentHeightToRadiusSquared > tankCornerHeightToRadiusSquared)
            {
                return AddNonNormalizedCapAttachment(transformFollower, position);
            }
            else
            {
                return AddNonNormalizedSideAttachment(transformFollower, position);
            }
        }

        private object AddNonNormalizedSideAttachment(TransformFollower transformFollower, Vector3 position)
        {
            var theta = Mathf.Atan2(-position.z, position.x);
            var uv = GetNonNormalizedSideAttachmentUv(position, theta);
            var orientation = SideAttachOrientation(theta, out var normal);
            var attachment = CreateAttachment(transformFollower, uv, Location.Side, orientation);

            Log("Adding non-normalized side attachment to position=" + position + " location=" + attachment.location + " uv=" + attachment.uv + " attach=" + transformFollower.name);
            AddSideAttachment(attachment);

            return attachment;
        }

        private Vector2 GetNonNormalizedSideAttachmentUv(Vector3 position, float theta)
        {
            var uv = new Vector2((Mathf.InverseLerp(-Mathf.PI, Mathf.PI, theta) + 0.5f) % 1.0f, position.y / Length + 0.5f);
            if (float.IsNaN(uv[0]))
            {
                uv[0] = 0f;
            }

            return uv;
        }

        private Attachment AddNonNormalizedCapAttachment(TransformFollower transformFollower, Vector3 position)
        {
            var uv = new Vector2(position.x / InnerRadius + 0.5f, position.z / InnerRadius + 0.5f);
            return AddCapAttachment(transformFollower, position, uv);
        }

        private Attachment AddCapAttachment(TransformFollower follower, Vector3 position, Vector2 uv)
        {
            Log("Adding normalized attachment to position=" + position + " attach=" + follower.name);
            if (position.y > 0)
            {
                return AddCapAttachment(follower, uv, Location.Top, Quaternion.LookRotation(Vector3.up, Vector3.right));
            }
            else
            {
                return AddCapAttachment(follower, uv, Location.Bottom, Quaternion.LookRotation(Vector3.down, Vector3.left));
            }
        }

        private Attachment AddCapAttachment(TransformFollower transformFollower, Vector2 uv, Location location, Quaternion orientation)
        {
            var attachment = CreateAttachment(transformFollower, uv, location, orientation);

            var attachmentList = location == Location.Top ? topAttachments : bottomAttachments;
            attachment.node = attachmentList.AddLast(attachment);
            Log("Adding non-normalized attachment to position= location=" + attachment.location + " uv=" + attachment.uv + " attach=" + transformFollower.name);

            return attachment;
        }

        private static Attachment CreateAttachment(TransformFollower transformFollower, Vector2 uv, Location location, Quaternion orientation)
        {
            transformFollower.SetLocalRotationReference(orientation);
            return new Attachment
            {
                uv = uv,
                follower = transformFollower,
                location = location,
            };
        }

        private object AddNormalizedAttachment(TransformFollower follower)
        {
            var position = follower.transform.localPosition;
            Attachment attachment;

            // as the position might be after some rotation and translation, it might not be exactly +/- 0.5
            if (Mathf.Abs(Mathf.Abs(position.y) - 0.5f) < 1e-5f)
            {
                attachment = AddNormalizedCapAttachment(follower, position);
            }
            else
            {
                attachment = AddNormalizedSideAttachment(follower, position);
            }
            ForceNextUpdate();
            return attachment;
        }

        private Attachment AddNormalizedSideAttachment(TransformFollower follower, Vector3 position)
        {
            var uv = GetNormalizedSideAttachmentUv(follower, position);
            var normalVector = new Vector3(position.x * 2f, 0, position.z * 2f);
            var attachment = CreateAttachment(follower, uv, Location.Side, Quaternion.FromToRotation(Vector3.up, normalVector));
            AddSideAttachment(attachment);
            return attachment;
        }

        private static Vector2 GetNormalizedSideAttachmentUv(TransformFollower follower, Vector3 position)
        {
            Log("Adding normalized side attachment to position=" + position + " attach=" + follower.name);
            var theta = Mathf.Atan2(-position.z, position.x);
            var uv = new Vector2(GetSideAttachmentU(theta), 0.5f - position.y);
            if (float.IsNaN(uv[0]))
            {
                uv[0] = 0f;
            }

            return uv;
        }

        private static float GetSideAttachmentU(float theta)
        {
            return (Mathf.InverseLerp(-Mathf.PI, Mathf.PI, theta) + 0.5f) % 1.0f;
        }

        private Attachment AddNormalizedCapAttachment(TransformFollower follower, Vector3 position)
        {
            var uv = new Vector2(position.x + 0.5f, position.z + 0.5f);
            return AddCapAttachment(follower, position, uv);
        }

        private void AddSideAttachment(Attachment attachment)
        {
            for (var node = sideAttachments.First; node != null; node = node.Next)
                if (node.Value.uv[1] > attachment.uv[1])
                {
                    attachment.node = sideAttachments.AddBefore(node, attachment);
                    return;
                }
            attachment.node = sideAttachments.AddLast(attachment);
        }

        public override TransformFollower RemoveAttachment(object data, bool normalize)
        {
            Log("Remove attachment called: norm: " + normalize);
            var attach = (Attachment)data;
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
                        var theta = Mathf.Lerp(0, Mathf.PI * 2f, attach.uv[0]);
                        var x = Mathf.Cos(theta);
                        var z = -Mathf.Sin(theta);

                        var normal = new Vector3(x, 0, z);
                        attach.follower.transform.localPosition = new Vector3(normal.x * 0.5f, 0.5f - attach.uv[1], normal.z * 0.5f);
                        attach.follower.transform.localRotation = Quaternion.FromToRotation(Vector3.up, normal);
                    }
                    break;
            }

            if (normalize)
                attach.follower.ForceUpdate();
            return attach.follower;
        }

        private void MoveAttachments()
        {
            foreach (var attachment in topAttachments)
            {
                MoveCapAttachment(attachment, HalfHeight);
            }
            foreach (var attachment in bottomAttachments)
            {
                MoveCapAttachment(attachment, -HalfHeight);
            }
            foreach (var attachment in sideAttachments)
            {
                MoveSideAttachment(attachment);
            }
        }

        private void MoveSideAttachment(Attachment attachment)
        {
            var theta = Mathf.Lerp(0, Mathf.PI * 2f, attachment.uv[0]);

            var x = Mathf.Cos(theta) * InnerRadius;
            var z = -Mathf.Sin(theta) * InnerRadius;

            var pos = new Vector3(x, attachment.uv[1] - 0.5f, z);
            Log("Moving side attachment:" + attachment + " to:" + pos.ToString("F3"));
            attachment.follower.transform.localPosition = pos;

            var orientation = SideAttachOrientation(theta, out var normal);

            Log("Moving to orientation: normal: " + normal.ToString("F3") + " theta:" + (theta * 180f / Mathf.PI) + orientation.ToStringAngleAxis());

            attachment.follower.transform.localRotation = orientation;
            attachment.follower.ForceUpdate();
        }

        private static Quaternion SideAttachOrientation(float theta, out Vector3 normal)
        {
            normal = Quaternion.AngleAxis(theta * 180 / Mathf.PI, Vector3.up) * new Vector2(1, 0);
            return Quaternion.FromToRotation(Vector3.up, normal);
        }

        private void MoveCapAttachment(Attachment attachment, float yCoordinate)
        {
            var pos = new Vector3(
                                (attachment.uv[0] - 0.5f) * InnerRadius,
                                yCoordinate,
                                (attachment.uv[1] - 0.5f) * InnerRadius);
            Log("Moving cap attachment:" + attachment + " to:" + pos.ToString("F7") + " uv: " + attachment.uv.ToString("F5"));
            attachment.follower.transform.localPosition = pos;
            attachment.follower.ForceUpdate();
        }
    }
}
