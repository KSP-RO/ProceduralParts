using System;
using UnityEngine;
using System.Reflection;

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
        public override void OnAwake()
        {
            base.OnAwake();
            //PartMessageService.Register(this);
            //this.RegisterOnUpdateEditor(OnUpdateEditor);
        }

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
        
        /////////////////////////////////////
        
        #endregion

        #region Objects
        public ProceduralPart PPart
        {
            get { return _pPart ?? (_pPart = GetComponent<ProceduralPart>()); }
        }
        private ProceduralPart _pPart;

        public Mesh SidesMesh
        {
            get { return PPart.SidesMesh; }
        }

        public Mesh EndsMesh
        {
            get { return PPart.EndsMesh; }
        }
        #endregion

        #region Shape details

        public float Volume
        {
            get { return _volume; }
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

        // Events. These will get bound up automatically

        //[PartMessageEvent]
        //public event PartVolumeChanged ChangeVolume;

		public void ChangeVolume(string volName, double newVolume)
		{
			var data = new BaseEventData (BaseEventData.Sender.USER);
			data.Set<string> ("volName", volName);
			data.Set<double> ("newTotalVolume", newVolume);
			part.SendEvent ("OnPartVolumeChanged", data, 0);
		}

        //[PartMessageEvent]
        //public event ChangeTextureScaleDelegate ChangeTextureScale;

        //[PartMessageEvent]
        //public event PartAttachNodeSizeChanged ChangeAttachNodeSize;

		public void ChangeAttachNodeSize(AttachNode node, float minDia, float area)
		{
			var data = new BaseEventData (BaseEventData.Sender.USER);
			data.Set<AttachNode> ("node", node);
			data.Set<float> ("minDia", minDia);
			data.Set<float> ("area", area);
			part.SendEvent ("OnPartAttachNodeSizeChanged", data, 0);
		}

        //[PartMessageEvent]
        //public event PartModelChanged ModelChanged;

		private void ModelChanged()
		{
			part.SendEvent ("OnPartModelChanged");
		}

        //[PartMessageEvent]
        //public event PartColliderChanged ColliderChanged;

		private void ColliderChanged()
		{
			part.SendEvent ("OnPartColliderChanged");
		}
		
        protected void RaiseChangeTextureScale(string meshName, Material material, Vector2 targetScale)
        {
            //ChangeTextureScale(meshName, material, targetScale);

			var data = new BaseEventData (BaseEventData.Sender.USER);
			data.Set<string> ("meshName", meshName);
			data.Set<Material> ("material", material);
			data.Set<Vector2> ("targetScale", targetScale);
			part.SendEvent ("OnChangeTextureScale", data, 0);

        }
        
        protected void RaiseChangeAttachNodeSize(AttachNode node, float minDia, float area)
        {
            ChangeAttachNodeSize(node, minDia, area);
        }

        protected void RaiseModelAndColliderChanged()
        {
            ModelChanged();
            ColliderChanged();
        }

        #endregion

        #region Callbacks

        private bool forceNextUpdate = true;

        public void ForceNextUpdate()
        {
            forceNextUpdate = true;
        }

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
        }

        public override void OnUpdate()
        {
            OnUpdateEditor();
        }
        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
                OnUpdateEditor();
        }

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

        public void OnUpdateEditor()
        {
            try
            {
                //using (PartMessageService.Instance.Consolidate(this))
                //{
                    bool wasForce = forceNextUpdate;
                    forceNextUpdate = false;

                    UpdateShape(wasForce);

                    if (wasForce)
                    {
                        ChangeVolume(volumeName, Volume);
                        if (HighLogic.LoadedSceneIsEditor)
                            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                    }
                //}
            }
            catch (Exception ex)
            {
                print(ex);
                enabled = false;
            }
        }

        /// <summary>
        /// Called to update the compShape.
        /// </summary>
        protected abstract void UpdateShape(bool force);

        #endregion

        #region Attachments

        /// <summary>
        /// Add object attached to the surface of this part.
        /// Base classes should proportionally move the location and orientation (rotation) as the part stretches.
        /// The return value will be passed back to removeTankAttachment when i's detached
        /// </summary>
        /// <param name="attach">Transform offset follower for the attachment</param>
        /// <param name="normalized">If true, the current offset of the attachment is in 'normalized' offset
        /// - where i would be in space on a unit length and diameter cylinder. This method will relocate the object.</param>
        /// <returns>Object used to track the attachment for Remove method</returns>
        public abstract object AddAttachment(TransformFollower attach, bool normalized);

        /// <summary>
        /// Remove object attached to the surface of this part.
        /// </summary>
        /// <param name="data">Data returned from child method</param>
        /// <param name="normalize">If true, the transform positon follower will be relocated to a 'normalized' 
        /// offset - where i would appear on a unit length and diameter cylinder</param>
        public abstract TransformFollower RemoveAttachment(object data, bool normalize);

        public class ShapeCoordinates
        {
            public enum RMode
            {
                OFFSET_FROM_SHAPE_CENTER,
                OFFSET_FROM_SHAPE_RADIUS,
                RELATIVE_TO_SHAPE_RADIUS
            }

            public enum YMode
            {
                OFFSET_FROM_SHAPE_CENTER,
                OFFSET_FROM_SHAPE_TOP,
                OFFSET_FROM_SHAPE_BOTTOM,
                RELATIVE_TO_SHAPE
            }

            public RMode RadiusMode = RMode.OFFSET_FROM_SHAPE_RADIUS;
            public YMode HeightMode = YMode.RELATIVE_TO_SHAPE;

            public float u;
            public float y;
            public float r;

            public override string ToString()
            {
                return "(u: " + u + " y: " + y + " r: " + r + ") R: " +RadiusMode + "Y: " + HeightMode;
            }
        }

        public abstract void GetCylindricCoordinates(Vector3 position, ShapeCoordinates coords);

        public abstract Vector3 FromCylindricCoordinates(ShapeCoordinates coords);

        #endregion

        public float GetCurrentCostMult()
        {
            return costMultiplier;
        }

        public abstract void UpdateTechConstraints();
    }
}