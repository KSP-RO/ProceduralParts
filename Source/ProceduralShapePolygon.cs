using KSPAPIExtensions;
using System;
using UnityEngine;

namespace ProceduralParts
{
    class ProceduralShapePolygon : ProceduralAbstractShape
    {
        private static readonly string ModTag = "[ProceduralShapePolygon]";
        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, diameter);

        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Corners", guiUnits = "#", guiFormat = "F0", groupName = ProceduralPart.PAWGroupName), 
            UI_FloatRange(minValue = 3, maxValue = 12, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float cornerCount = 8;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float diameter = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 1f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Circumdiameter", guiFormat = "F3", guiUnits = "\u2009m", groupName = ProceduralPart.PAWGroupName)]
        public float OuterDiameter = 0;

        [KSPField]
        public string TopNodeName = "top";

        [KSPField]
        public string BottomNodeName = "bottom";

        #endregion

        #region Utility Properties

        internal class SimPart
        {
            internal float cornerCount = 8, length = 1, diameter = 1, outerDiameter = 0;
            internal SimPart() { }
            internal int CornerCount => (int)cornerCount;
            internal float CornerCenterCornerAngle => 2 * Mathf.PI / CornerCount;
            internal float EdgeToEdgeAngle => Mathf.PI - CornerCenterCornerAngle;
            internal float StartAngle => 0.5f * Mathf.PI - CornerCenterCornerAngle / 2f;
            internal int SideTriangles => CornerCount * 2;
            internal int TrianglesPerCap => CornerCount - 2;
            internal float NormHalfSideLength => NormSideLength / 2;
            internal float NormSideLength => Mathf.Tan(CornerCenterCornerAngle / 2);
            internal const float NormInnerRadius = 0.5f;
            internal const float NormHalfHeight = 0.5f;

            internal float InnerDiameter => CornerCount % 2 == 0 ? diameter : GetInnerDiameterFromHeight(diameter);
            internal float HalfSideLength => NormHalfSideLength * InnerDiameter;
            internal float InnerRadius => NormInnerRadius * InnerDiameter;
            internal float OuterToInnerFactor => Mathf.Cos(CornerCenterCornerAngle / 2);
            internal float OuterRadius => InnerRadius / OuterToInnerFactor;
            internal float NormOuterDiameter => 1f / OuterToInnerFactor;
            internal float HalfHeight => NormHalfHeight * length;
            internal float Area => InnerRadius * HalfSideLength * CornerCount;
            internal float VolumeCalculated => Area * length;
            internal int SideVerticesPerCap => CornerCount * 2;
            internal float NormHorizontalDiameter => CornerCount % 4 == 0 ? 1 : NormOuterDiameter;

            internal float GetInnerDiameterFromHeight(float height) => height / ((1 + 1 / OuterToInnerFactor) / 2);
            internal float ConvertToEditorDiameter(float innerDiameter) => CornerCount % 2 == 0 ? innerDiameter : GetHeightFromInnerDiameter(innerDiameter);
            internal float GetHeightFromInnerDiameter(float innerDiameter) => innerDiameter * ((1 + 1 / OuterToInnerFactor) / 2);

            internal float GetInnerDiameterFromArea(float area) => Mathf.Sqrt(area * 4 / (CornerCount * NormSideLength));
            // Area => InnerRadius * HalfSideLength * CornerCount;
            //      => (0.5 * innerDiameter) * (NormSideLength/2 * innerDiameter) * CornerCount
            //      => (0.25 * innerDIameter * innerDiameter * normSideLength * cornerCount)
        }

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
        private float GetInnerDiameterFromArea(float area) => Mathf.Sqrt(area * 4 / (CornerCount * NormSideLength));
        // Area => InnerRadius * HalfSideLength * CornerCount;
        //      => (0.5 * innerDiameter) * (NormSideLength/2 * innerDiameter) * CornerCount
        //      => (0.25 * innerDIameter * innerDiameter * normSideLength * cornerCount)

        #endregion

        #region Initialization

        public override void OnStart(StartState state)
        {
            UpdateTechConstraints();
            base.OnStart(state);

            Fields[nameof(cornerCount)].uiControlEditor.onFieldChanged =
                new Callback<BaseField, object>(OnShapeDimensionChanged);

            Fields[nameof(diameter)].uiControlEditor.onFieldChanged =
                new Callback<BaseField, object>(OnShapeDimensionChanged);

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

        #region Update handlers

        internal override void UpdateShape(bool force = true)
        {
            part.CoMOffset = CoMOffset;
            Volume = CalculateVolume();
            OuterDiameter = InnerDiameter / OuterToInnerFactor;
            GenerateMeshes();
            // WriteMeshes in AbstractSoRShape typically does UpdateNodeSize, UpdateProps, RaiseModelAndColliderChanged
            UpdateNodeSize(TopNodeName);
            UpdateNodeSize(BottomNodeName);
            PPart.UpdateProps();
            RaiseModelAndColliderChanged();
        }

        public override void AdjustDimensionBounds()
        {
            if (float.IsPositiveInfinity(PPart.volumeMax)) return;

            if (CalculateVolume(Area, PPart.lengthMax) > PPart.volumeMax)
            {
                (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = PPart.volumeMax / Area;
            }
            SimPart sim = new SimPart
            {
                cornerCount = cornerCount,
                diameter = PPart.diameterMax,
                length = length,
                outerDiameter = OuterDiameter
            };
            if (sim.VolumeCalculated > PPart.volumeMax)
            {
                float maxDiameter = sim.GetInnerDiameterFromArea(PPart.volumeMax / length);
                (Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit).maxValue = maxDiameter;
            }
        }

        public override float CalculateVolume() => VolumeCalculated;
        private float CalculateVolume(float area, float length) => area * length;
        public override bool SeekVolume(float targetVolume) => SeekVolume(targetVolume, Fields[nameof(length)]);

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(diameter) && obj is float oldDiameter)
            {
                HandleDiameterChange((float)f.GetValue(this), oldDiameter);
            }
            if (f.name == nameof(length) && obj is float oldLen)
            {
                HandleLengthChange((float)f.GetValue(this), oldLen);
            }
            if (f.name == nameof(cornerCount))
            {
            //    HandleCornerCountChanged(f, obj);
            }
        }

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (diameter / 2);
            coords.y /= length;
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= (diameter / 2);
            coords.y *= length;
        }

        private void GenerateMeshes()
        {
            GenerateSideMesh();
            GenerateCapMesh();
            GenerateColliderMesh();
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

            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, tankVLength));
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
            RaiseChangeTextureScale(nodeName, PPart.legacyTextureHandler.EndsMaterial, new Vector2(InnerDiameter, InnerDiameter));
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

        private static float GetSideAttachmentU(float theta)
        {
            return (Mathf.InverseLerp(-Mathf.PI, Mathf.PI, theta) + 0.5f) % 1.0f;
        }

        private static Quaternion SideAttachOrientation(float theta, out Vector3 normal)
        {
            normal = Quaternion.AngleAxis(theta * 180 / Mathf.PI, Vector3.up) * new Vector2(1, 0);
            return Quaternion.FromToRotation(Vector3.up, normal);
        }

        #endregion
    }
}
