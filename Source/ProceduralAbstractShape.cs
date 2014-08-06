using System;
using KSPAPIExtensions;
using UnityEngine;
using KSPAPIExtensions.PartMessage;

namespace ProceduralParts
{
    public abstract class ProceduralAbstractShape : PartModule
    {
        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
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
                }
            }
        }
        private float _volume;

        #endregion

        #region Events

        // Events. These will get bound up automatically

        [PartMessageEvent]
        public event PartVolumeChanged ChangeVolume;

        [PartMessageEvent]
        public event ChangeTextureScaleDelegate ChangeTextureScale;

        [PartMessageEvent]
        public event PartAttachNodeSizeChanged ChangeAttachNodeSize;

        [PartMessageEvent]
        public event PartModelChanged ModelChanged;

        [PartMessageEvent]
        public event PartColliderChanged ColliderChanged;

        protected void RaiseChangeTextureScale(string meshName, Material material, Vector2 targetScale)
        {
            ChangeTextureScale(meshName, material, targetScale);
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

        public void OnUpdateEditor()
        {
            try
            {
                using (PartMessageService.Instance.Consolidate(this))
                {
                    bool wasForce = forceNextUpdate;
                    forceNextUpdate = false;

                    UpdateShape(wasForce);

                    if (wasForce)
                        ChangeVolume(volumeName, Volume);
                }
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

        #endregion
    }
}