using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralParts
{
    class ProceduralShapePolygon : ProceduralAbstractShape
    {
        #region Config parameters

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Corners", guiUnits = "#", guiFormat = "F0"), UI_FloatRange(minValue = 3, maxValue = 12, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float cornerCount = 8;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "F3", guiUnits = "m"),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float diameter = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3", guiUnits = "m"),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 1f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Circumdiameter", guiFormat = "F3", guiUnits = "\u2009m")]
        public float OuterDiameter = 0;

        [KSPField]
        public string TopNodeName = "top";

        [KSPField]
        public string BottomNodeName = "bottom";

        #endregion

        #region Utility Properties

        private float Length => length;
        private int CornerCount => (int)cornerCount;
        private float CornerCenterCornerAngle => 2 * Mathf.PI / CornerCount;
        private float EdgeToEdgeAngle => Mathf.PI - CornerCenterCornerAngle;
        private float StartAngle => 0.5f * Mathf.PI - CornerCenterCornerAngle / 2f;
        private int SideTriangles => CornerCount * 2;
        private int TrianglesPerCap => CornerCount - 2;
        private float NormHalfSideLength => NormSideLength / 2;
        private float NormSideLength => Mathf.Tan(CornerCenterCornerAngle / 2);
        private const float NormInnerRadius = 0.5f;
        private const float NormHalfHeight = 0.5f;

        private float InnerDiameter => CornerCount % 2 == 0 ? diameter : GetInnerDiameterFromHeight(diameter);
        private float HalfSideLength => NormHalfSideLength * InnerDiameter;
        private float InnerRadius => NormInnerRadius * InnerDiameter;
        private float OuterToInnerFactor => Mathf.Cos(CornerCenterCornerAngle / 2);
        private float OuterRadius => InnerRadius / OuterToInnerFactor;
        private float NormOuterDiameter => 1f / OuterToInnerFactor;
        private float HalfHeight => NormHalfHeight * Length;
        private float Area => InnerRadius * HalfSideLength * CornerCount;
        private float VolumeCalculated => Area * Length;
        private int SideVerticesPerCap => CornerCount * 2;
        private float NormHorizontalDiameter => CornerCount % 4 == 0 ? 1 : NormOuterDiameter;

        private float GetInnerDiameterFromHeight(float height) => height / ((1 + 1 / OuterToInnerFactor) / 2);
        private float ConvertToEditorDiameter(float innerDiameter) => CornerCount % 2 == 0 ? innerDiameter : GetHeightFromInnerDiameter(innerDiameter);
        private float GetHeightFromInnerDiameter(float innerDiameter) => innerDiameter * ((1 + 1 / OuterToInnerFactor) / 2);

        #endregion

        #region Initialization

        public override void OnStart(StartState state)
        {
            UpdateTechConstraints();
            base.OnStart(state);

            Fields[nameof(cornerCount)].uiControlEditor.onSymmetryFieldChanged =
                Fields[nameof(cornerCount)].uiControlEditor.onFieldChanged =
                new Callback<BaseField, object>(OnShapeDimensionChanged);

            Fields[nameof(diameter)].uiControlEditor.onSymmetryFieldChanged =
                Fields[nameof(diameter)].uiControlEditor.onFieldChanged =
                new Callback<BaseField, object>(OnShapeDimensionChanged);

            Fields[nameof(length)].uiControlEditor.onSymmetryFieldChanged =
                Fields[nameof(length)].uiControlEditor.onFieldChanged =
                new Callback<BaseField, object>(OnShapeDimensionChanged);
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.maxValue = PPart.lengthMax;
            lengthEdit.minValue = PPart.lengthMin;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;
            length = Mathf.Clamp(length, PPart.lengthMin, PPart.lengthMax);

            Fields[nameof(diameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit diameterEdit = Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit;
            diameterEdit.maxValue = PPart.diameterMax;
            diameterEdit.minValue = PPart.diameterMin;
            diameterEdit.incrementLarge = PPart.diameterLargeStep;
            diameterEdit.incrementSmall = PPart.diameterSmallStep;
            diameter = Mathf.Clamp(diameter, PPart.diameterMin, PPart.diameterMax);
        }

        #endregion

        #region Coordinate Utilities

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

            return position;
        }

        public override void GetCylindricCoordinates(Vector3 position, ShapeCoordinates shapeCoordinates)
        {
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
        }

        #endregion

        #region Update handlers

        internal override void UpdateShape(bool force = true)
        {
            Volume = CalculateVolume();
            UpdateAttachments();
            GenerateMeshes();
            UpdateProps();
            UpdateFields();
            RaiseModelAndColliderChanged();
        }

        public override void AdjustDimensionBounds()
        {
            if (float.IsPositiveInfinity(PPart.volumeMax)) return;

            if (CalculateVolume(Area, PPart.lengthMax) > PPart.volumeMax)
            {
                (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = PPart.volumeMax / Area;
            }
            //  if (CalculateVolume(AreaFromMaxDiameter, length) > PPart.volumeMax)
            //  {
            //      float maxArea = PPart.volumeMax / length;
            //      //Derive diameter from max area given cornercount, etc.
            //      (Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit).maxValue = maxDiameter;
            //  }
        }

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        private void GenerateMeshes()
        {
            GenerateSideMesh();
            GenerateCapMesh();
            GenerateColliderMesh();
        }

        private void UpdateAttachments()
        {
            UpdateNodeSize(TopNodeName);
            UpdateNodeSize(BottomNodeName);
            MoveAttachments();
        }

        private void UpdateFields()
        {
            OuterDiameter = InnerDiameter / OuterToInnerFactor;
        }

        public override float CalculateVolume() => VolumeCalculated;
        private float CalculateVolume(float area, float length) => area * length;

        private void UpdateProps()
        {
            foreach (var pm in GetComponents<PartModule>())
            {
                if (pm is IProp prop) prop.UpdateProp();
            }
        }

        #endregion

        #region Meshes

        private void GenerateColliderMesh()
        {
            var mesh = new UncheckedMesh(CornerCount * 2, SideTriangles + 2 * TrianglesPerCap);
            GenerateCapVertices(mesh, -HalfHeight, 0);
            GenerateCapVertices(mesh, HalfHeight, CornerCount);
            GenerateSideTriangles(mesh, CornerCount, 1);
            GenerateCapTriangles(mesh, false, SideTriangles);
            GenerateCapTriangles(mesh, true, SideTriangles + TrianglesPerCap);

            var colliderMesh = new Mesh();
            mesh.WriteTo(colliderMesh);
            PPart.ColliderMesh = colliderMesh;
        }

        private void GenerateCapMesh()
        {
            var mesh = new UncheckedMesh(CornerCount * 2, TrianglesPerCap * 2);
            GenerateCapVertices(mesh, -HalfHeight, 0);
            GenerateCapVertices(mesh, HalfHeight, CornerCount);
            GenerateCapTriangles(mesh, false, 0);
            GenerateCapTriangles(mesh, true, TrianglesPerCap);

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

            RaiseChangeTextureScale("sides", PPart.SidesMaterial, new Vector2(tankULength, tankVLength));
            WriteToAppropriateMesh(mesh, PPart.SidesIconMesh, SidesMesh);
        }

        private void UpdateNodeSize(string nodeName)
        {
            var node = part.attachNodes.Find(n => n.id == nodeName);
            if (node == null)
                return;
            node.size = Math.Min((int)(InnerDiameter / PPart.diameterLargeStep), 3);
            node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);

            RaiseChangeAttachNodeSize(node, InnerDiameter, Mathf.PI * InnerDiameter * InnerDiameter * 0.25f);
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

        private void GenerateCapTriangles(UncheckedMesh mesh, bool up, int triangleOffset)
        {
            var triangleIndexOffset = triangleOffset * 3;
            var vertexOffset = up ? CornerCount : 0;
            for (var i = 0; i < TrianglesPerCap; i++)
            {
                mesh.triangles[i * 3 + triangleIndexOffset] = vertexOffset;
                mesh.triangles[i * 3 + 1 + triangleIndexOffset] = (up ? i + 2 : i + 1) + vertexOffset;
                mesh.triangles[i * 3 + 2 + triangleIndexOffset] = (up ? i + 1 : i + 2) + vertexOffset;
            }
        }

        private void GenerateSideVertices(UncheckedMesh mesh, float y, float v, int offset)
        {
            for (var cornerNumber = 0; cornerNumber < CornerCount; cornerNumber++)
            {
                CreateSideCornerVertices(mesh, y, v, offset, cornerNumber);
            }
        }

        private void CreateSideCornerVertices(UncheckedMesh mesh, float y, float v, int offset, int cornerNumber)
        {
            var cornerAngle = GetCornerAngle(cornerNumber);
            var cornerVector = CreateVectorFromAngle(cornerAngle, y, OuterRadius);
            var verticesPerCorner = 2;

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
            var cornerVector = CreateVectorFromAngle(cornerAngle, y, OuterRadius);
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
            mesh.uv[vertexIndex] = new Vector2(cornerVector.x, cornerVector.z) / InnerDiameter / NormHorizontalDiameter + new Vector2(0.5f, 0.5f);
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
        
        #endregion

        #region Attachments

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

            if (attachmentHeightToRadiusSquared > tankCornerHeightToRadiusSquared)
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
            if (Mathf.Abs(Mathf.Abs(position.y) - Mathf.Abs(position.magnitude)) < 1e-5f)
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
            attachment.follower.transform.localPosition = pos;

            var orientation = SideAttachOrientation(theta, out var normal);
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
            attachment.follower.transform.localPosition = pos;
            attachment.follower.ForceUpdate();
        }

        #endregion
    }
}
