using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace ProceduralParts
{
    public enum PartVolumes
	{
		/// <summary>
		/// Tankage - the volume devoted to storage of fuel, life support resources, ect
		/// </summary>
		Tankage,
		/// <summary>
		/// The volume devoted to habitable space.
		/// </summary>
		Habitable,
	}

    public abstract class ProceduralAbstractShape : PartModule
    {
        private static readonly string ModTag = "[ProceduralAbstractShape]";
        internal const float SliderPrecision = 0.001f;
        internal const float IteratorIncrement = 1048.5f / (1024 * 1024); // a float slightly below sliderprecision

        public bool IsAvailable => string.IsNullOrEmpty(techRequired) || ResearchAndDevelopment.GetTechnologyState(techRequired) == RDTech.State.Available;
        public bool IsObsolete => !string.IsNullOrEmpty(techObsolete) && ResearchAndDevelopment.GetTechnologyState(techObsolete) == RDTech.State.Available;
        public virtual Vector3 CoMOffset => Vector3.zero;

        #region Config data
        [KSPField]
        public string displayName;

        [KSPField]
        public string techRequired;

        [KSPField]
        public string techObsolete;

        [KSPField]
        public string volumeName = PartVolumes.Tankage.ToString();

        #endregion

        #region balancing
        // this are additional info fields that can be used by other modules for balancing purposes. shape classes should not use them themself

        [KSPField]
        public float costMultiplier = 1.0f;

        [KSPField]
        public float massMultiplier = 1.0f;

        [KSPField]
        public float resourceMultiplier = 1.0f;
        
        #endregion

        #region Objects

        public ProceduralPart PPart { get => _pPart ??= GetComponent<ProceduralPart>(); }
        private ProceduralPart _pPart;

        public Mesh SidesMesh { get => PPart.SidesMesh; }

        public Mesh EndsMesh { get => PPart.EndsMesh; }

        #endregion

        #region Shape details

        public float Volume
        {
            get => _volume;
            protected set
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (value != _volume)
                {
                    _volume = value;
                    ChangeVolume(volumeName, value);
                    if (HighLogic.LoadedSceneIsEditor)
                        GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
            }
        }
        private float _volume;

        #endregion

        #region Events

        // Find the ProceduralAbstractShape in Part p that is the same ProceduralAbstractShape as the param.
        protected ProceduralAbstractShape FindAbstractShapeModule(Part p, ProceduralAbstractShape type)
        {
            foreach (ProceduralAbstractShape s in p.FindModulesImplementing<ProceduralAbstractShape>())
            {
                if (s.GetType() == type.GetType())
                    return s;
            }
            return null;
        }

        /* Unknown, but in 1.6, symmetry callback happens before primary callback, AND obj is 
         * NOT the previous value!
         * 
         * [LOG 14:15:29.840] [ProceduralShapeCylinder] OnShapeDimensionChangedSYMMETRY! so ignoring.  length from 6 to 6
         * [LOG 14:15:29.841] [ProceduralShapeCylinder] OnShapeDimensionChangedSYMMETRY! so ignoring.  length from 6 to 6
         * [LOG 14:15:29.842] [ProceduralShapeCylinder] OnShapeDimensionChanged override: length from 5 to 6
         */
        public virtual void OnShapeDimensionChanged(BaseField f, object obj)
        {
            if (f.GetValue(this).Equals(obj))
                return;
            Debug.Log($"{ModTag} OnShapeDimensionChanged: {part.name} {f.name} from {obj} to {f.GetValue(this)}");
            AdjustDimensionBounds();
            UpdateShape();
            UpdateInterops();
            TranslateAttachmentsAndNodes(f, obj);
            foreach (Part p in part.symmetryCounterparts)
            {
                if (FindAbstractShapeModule(p, this) is ProceduralAbstractShape pm)
                {
                    pm.AdjustDimensionBounds();
                    pm.UpdateShape();
                    pm.UpdateInterops();
                    pm.TranslateAttachmentsAndNodes(f, obj);
                }
                else
                {
                    Debug.LogError($"{ModTag} Failed to find expected {this.GetType()} partModule");
                }
            }
        }

        public abstract float CalculateVolume();
        public abstract void AdjustDimensionBounds();
        public abstract void TranslateAttachmentsAndNodes(BaseField f, object obj);

        public virtual void HandleLengthChange(float length, float oldLength)
        {
            float trans = length - oldLength;
            foreach (AttachNode node in part.attachNodes)
            {
                // Our nodes are relative to part center 0,0,0.  position.y > 0 are top nodes.
                float direction = (node.position.y > 0) ? 1 : -1;
                Vector3 translation = direction * (trans / 2) * Vector3.up;
                if (node.nodeType == AttachNode.NodeType.Stack)
                {
                    TranslateNode(node, translation);
                    if (node.attachedPart is Part pushTarget)
                    {
                        TranslatePart(pushTarget, translation);
                    }
                }
            }

            // Now push our surface-attached children based on their relative (offset) length.
            foreach (Part p in part.children)
            {
                if (p.FindAttachNodeByPart(part) is AttachNode node && node.nodeType == AttachNode.NodeType.Surface)
                {
                    GetAttachmentNodeLocation(node, out Vector3 worldSpace, out Vector3 localToHere, out ShapeCoordinates coord);
                    float ratio = length / oldLength;
                    coord.y *= ratio;
                    MovePartByAttachNode(node, coord);
                }
            }
        }

        public void TranslatePart(Part pushTarget, Vector3 translation)
        {
            // If the attached part is a child of ours, push it directly.
            // If it is our parent, then we need to find the eldest grandparent and push that, and also ourselves
            if (pushTarget == this.part.parent)
            {
                this.part.transform.Translate(-translation, Space.Self);    // Push ourselves normally
                float sibMult = part.symmetryCounterparts == null ? 1f : 1f / (part.symmetryCounterparts.Count + 1);
                pushTarget = GetEldestParent(this.part);
                translation *= sibMult; // Push once for each symmetry sibling, so scale the parent push.
            }
            // Convert to world space, to deal with bizarre orientation relationships.
            // (ex: pushTarget is inverted, and our top node connects to its top node)
            Vector3 worldSpaceTranslation = part.transform.TransformVector(translation);
            pushTarget.transform.Translate(worldSpaceTranslation, Space.World);
        }

        public virtual void HandleDiameterChange(float diameter, float oldDiameter)
        {
            // Adjust our own surface attach node, and translate ourselves.
            if (part.srfAttachNode is AttachNode srf)
            {
                GetAttachmentNodeLocation(srf, out Vector3 _, out Vector3 _, out ShapeCoordinates coord);
                coord.r *= diameter / oldDiameter;
                Vector3 newNodeLocalPos = FromCylindricCoordinates(coord);
                Vector3 localTranslate = newNodeLocalPos - srf.position;
                Debug.Log($"{ModTag} Moved surface attachment node from {srf.position} (local) to {newNodeLocalPos}");
                if (srf.attachedPart is Part)
                {
                    // We are surface-attached, so translate ourselves.
                    part.transform.Translate(-localTranslate, Space.Self);
                    Debug.Log($"{ModTag} Translated ourselves by {-localTranslate}");
                }
                MoveNode(srf, newNodeLocalPos);
            }
            // Nothing to do for stack-attached nodes.
            foreach (Part p in part.children)
            {
                if (p.FindAttachNodeByPart(part) is AttachNode node && node.nodeType == AttachNode.NodeType.Surface)
                {
                    GetAttachmentNodeLocation(node, out Vector3 worldSpace, out Vector3 localToHere, out ShapeCoordinates coord);
                    float ratio = diameter / oldDiameter;
                    coord.r *= ratio;
                    MovePartByAttachNode(node, coord);
                }
            }
        }

        public float RoundToDirection(float val, int dir)
        {
            if (dir < 0)
                return Mathf.Floor(val);
            else if (dir == 0)
                return Mathf.Round(val);
            else
                return Mathf.Ceil(val);
        }

        public abstract bool SeekVolume(float targetVolume, int dir=0);
        public virtual bool SeekVolume(float targetVolume, BaseField scaledField, int dir=0)
        {
            float orig = (float) scaledField.GetValue(this);
            float maxLength = (scaledField.uiControlEditor as UI_FloatEdit).maxValue;
            float minLength = (scaledField.uiControlEditor as UI_FloatEdit).minValue;
            float precision = (scaledField.uiControlEditor as UI_FloatEdit).incrementSlide;
            float scaledValue = orig * targetVolume / Volume;
            scaledValue = RoundToDirection(scaledValue / precision, dir) * precision;
            float clampedScaledValue = Mathf.Clamp(scaledValue, minLength, maxLength);
            bool closeEnough = Mathf.Abs((clampedScaledValue / scaledValue) - 1) < 0.01;
            scaledField.SetValue(clampedScaledValue, this);
            foreach (Part p in part.symmetryCounterparts)
            {
                // Propagate the change to other parts in symmetry group
                if (FindAbstractShapeModule(p, this) is ProceduralAbstractShape pm)
                {
                    scaledField.SetValue(clampedScaledValue, pm);
                }
            }
            OnShapeDimensionChanged(scaledField, orig);
            MonoUtilities.RefreshPartContextWindow(part);
            return closeEnough;
        }

        public virtual void GetAttachmentNodeLocation(AttachNode node, out Vector3 worldSpace, out Vector3 localSpace, out ShapeCoordinates coord)
        {
            worldSpace = node.owner.transform.TransformPoint(node.position);
            localSpace = part.transform.InverseTransformPoint(worldSpace);
            coord = new ShapeCoordinates();
            GetCylindricCoordinates(localSpace, coord);
        }

        public virtual void MovePartByAttachNode(AttachNode node, ShapeCoordinates coord)
        {
            Vector3 oldWorldSpace = node.owner.transform.TransformPoint(node.position);
            Vector3 target = FromCylindricCoordinates(coord);
            Vector3 newWorldspace = part.transform.TransformPoint(target);
            node.owner.transform.Translate(newWorldspace - oldWorldSpace, Space.World);
        }

        public Part GetEldestParent(Part p) => (p.parent is null) ? p : GetEldestParent(p.parent);

        public void ChangeVolume(string volName, double newVolume)
        {
            var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
            data.Set<string> ("volName", volName);
            data.Set<double> ("newTotalVolume", newVolume);
            Debug.Log($"{ModTag} {part.name} OnPartVolumeChanged volName:{volName} vol:{newVolume:F4}");
            part.SendEvent ("OnPartVolumeChanged", data, 0);
        }

        public void ChangeAttachNodeSize(AttachNode node, float minDia, float area)
        {
            var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
            data.Set("node", node);
            data.Set<float>("minDia", minDia);
            data.Set<float>("area", area);
            part.SendEvent("OnPartAttachNodeSizeChanged", data, 0);
        }

        protected void RaiseChangeTextureScale(string meshName, Material material, Vector2 targetScale)
        {
            var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
            data.Set<string>("meshName", meshName);
            data.Set("material", material);
            data.Set("targetScale", targetScale);
            part.SendEvent("OnChangeTextureScale", data, 0);
        }
        
        protected void RaiseChangeAttachNodeSize(AttachNode node, float minDia, float area) => ChangeAttachNodeSize(node, minDia, area);
        private void ModelChanged() => part.SendEvent("OnPartModelChanged", null, 0);
        private void ColliderChanged() => part.SendEvent("OnPartColliderChanged", null, 0);
        protected void RaiseModelAndColliderChanged()
        {
            ModelChanged();
            ColliderChanged();
        }

        #endregion

        #region Callbacks

        public void UpdateInterops()
        {
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                if(ProceduralPart.installedFAR)
                    part.SendMessage("GeometryPartModuleRebuildMeshData");

                _pPart.UpdateTFInterops();
            }
        }

        public abstract void UpdateTFInterops();

        internal void FixEditorIconScale()
        {
            var meshBounds = CalculateBounds(part.partInfo.iconPrefab.gameObject);
            if (meshBounds.extents == Vector3.zero)
                meshBounds = PPart.SidesIconMesh.bounds;
            var maxSize = Mathf.Max(meshBounds.size.x, meshBounds.size.y, meshBounds.size.z);

            float factor = (40f / maxSize) / 40f;

            part.partInfo.iconScale = 1f / maxSize;
            var iconMainTrans = part.partInfo.iconPrefab.transform.GetChild(0).transform;

            iconMainTrans.localScale *= factor;
            iconMainTrans.localPosition -= meshBounds.center;
        }

        //Code from PartIconFixer addon
        private static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true).ToList();

            if (renderers.Count == 0) return default;

            var boundsList = new List<Bounds>();

            renderers.ForEach(r =>
            {
                // why wouldn't it be enabled? not necessarily a problem though

                if (r is SkinnedMeshRenderer)
                {
                    var smr = r as SkinnedMeshRenderer;

                    // the localBounds of the SkinnedMeshRenderer are initially large enough
                    // to accomodate all animation frames; they're likely to be far off for 
                    // parts that do a lot of animation-related movement (like solar panels expanding)
                    //
                    // We can get correct mesh bounds by baking the current animation into a mesh
                    // note: vertex positions in baked mesh are relative to smr.transform; any scaling
                    // is already baked in
                    Mesh mesh = new Mesh();
                    smr.BakeMesh(mesh);

                    // while the mesh bounds will now be correct, they don't consider orientation at all.
                    // If a long part is oriented along the wrong axis in world space, the bounds we'd get
                    // here could be very wrong. We need to come up with essentially the renderer bounds:
                    // a bounding box in world space that encompasses the mesh
                    Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one
                        /* remember scale already factored in!*/);
                    var vertices = mesh.vertices;

                    Bounds smrBounds = new Bounds(m.MultiplyPoint3x4(vertices[0]), Vector3.zero);

                    for (int i = 1; i < vertices.Length; ++i)
                        smrBounds.Encapsulate(m.MultiplyPoint3x4(vertices[i]));

                    Destroy(mesh);

                    boundsList.Add(smrBounds);
                }
                else if (r is MeshRenderer) // note: there are ParticleRenderers, LineRenderers, and TrailRenderers
                {
                    r.gameObject.GetComponent<MeshFilter>().sharedMesh.RecalculateBounds();
                    boundsList.Add(r.bounds);
                }
            });


            Bounds bounds = boundsList[0];
            boundsList.Skip(1).ToList().ForEach(b => bounds.Encapsulate(b));

            return bounds;
        }

        /// <summary>
        /// Called to update the compShape.
        /// </summary>
        internal abstract void UpdateShape(bool force=true);

        internal abstract void InitializeAttachmentNodes();

        internal virtual void InitializeAttachmentNodes(float length, float diameter)
        {
            InitializeStackAttachmentNodes(length);
            InitializeSurfaceAttachmentNode(length, diameter);
        }

        internal virtual void InitializeStackAttachmentNodes(float length)
        {
//            Debug.Log($"{ModTag} InitializeStackAttachmentNodes for {this} length {length}");
            foreach (AttachNode node in part.attachNodes)
            {
                if (node.owner != part)
                    node.owner = part;
                float direction = (node.position.y > 0) ? 1 : -1;
                Vector3 translation = direction * (length / 2) * Vector3.up;
                if (node.nodeType == AttachNode.NodeType.Stack)
                    MoveNode(node, translation);
            }
        }

        internal virtual void InitializeSurfaceAttachmentNode(float length, float diameter)
        {
//            Debug.Log($"{ModTag} InitializeSurfaceAttachmentNode for {this} diameter {diameter}");
            if (part.srfAttachNode is AttachNode node)
            {
                if (node.owner != part)
                    node.owner = part;
                ShapeCoordinates coord = new ShapeCoordinates();
                PPart.CurrentShape.GetCylindricCoordinates(node.position, coord);
                coord.r = diameter / 2;
                MoveNode(node, PPart.CurrentShape.FromCylindricCoordinates(coord));
            }
        }

        // FIXME, Temporarily set to public to be able to use it in horizontal offset
        public void TranslateNode(AttachNode node, Vector3 translation) => MoveNode(node, node.position + translation);
        private void MoveNode(AttachNode node, Vector3 destination)
        {
            if (Vector3.Distance(node.position, destination) > 0.01f)
            {
//                Debug.Log($"{ModTag} MoveNode() moved {node.id} from {node.position} to {destination} = {part.transform.TransformPoint(destination)} (worldspace)");
                if (node.nodeTransform is Transform)
                {
                    node.nodeTransform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    node.nodeTransform.Translate(destination, Space.Self);
                }
                else
                {
                    node.position = destination;
                }
                node.originalPosition = node.position;
                var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
                data.Set("node", node);
                part.SendEvent("OnPartNodeMoved", data, 0);
            }
        }

        #endregion

        #region ShapeCoordinates

        public class ShapeCoordinates
        {
            public float u;
            public float y;
            public float r;

            public override string ToString() => $"(u: {u} y: {y} r: {r})";
        }

        public virtual void GetCylindricCoordinates(Vector3 position, ShapeCoordinates coords)
        {
            Vector2 direction = new Vector2(position.x, position.z);
            coords.y = position.y;
            coords.r = direction.magnitude;

            float theta = Mathf.Atan2(-direction.y, direction.x);
            coords.u = (Mathf.InverseLerp(-Mathf.PI, Mathf.PI, theta) + 0.5f) % 1.0f;
            if (float.IsNaN(coords.u))
                coords.u = 0f;
        }

        public virtual Vector3 FromCylindricCoordinates(ShapeCoordinates coords)
        {
            Vector3 position = new Vector3(0, coords.y, 0);
            float radius = coords.r;
            float theta = Mathf.Lerp(0, Mathf.PI * 2f, coords.u);

            position.x = Mathf.Cos(theta) * radius;
            position.z = -Mathf.Sin(theta) * radius;
            return position;
        }

        public abstract void NormalizeCylindricCoordinates(ShapeCoordinates coords);
        public abstract void UnNormalizeCylindricCoordinates(ShapeCoordinates coords);

        #endregion

        public float GetCurrentCostMult() => costMultiplier;

        public abstract void UpdateTechConstraints();
    }
}