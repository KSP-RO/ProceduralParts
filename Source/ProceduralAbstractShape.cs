using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

namespace ProceduralParts
{
    public abstract class ProceduralAbstractShape : PartModule
    {
        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
        }

        #region Config data
        [KSPField]
        public string displayName;

        [KSPField]
        public string techRequired;

        [KSPField]
        public string techObsolete;

        #endregion

        #region Objects
        public ProceduralPart pPart
        {
            get
            {
                if (_pPart == null)
                    _pPart = GetComponent<ProceduralPart>();
                return _pPart;
            }
        }
        private ProceduralPart _pPart;

        public Mesh sidesMesh
        {
            get { return pPart.sidesMesh; }
        }

        public Mesh endsMesh
        {
            get { return pPart.endsMesh; }
        }
        #endregion

        #region Shape details

        public float volume
        {
            get { return _volume; }
            protected set
            {
                if (value != _volume)
                {
                    _volume = value;
                    volumeChanged = true;
                }
            }
        }
        private float _volume;
        private bool volumeChanged = false;

        #endregion

        #region Events

        // Events. These will get bound up automatically

        [PartMessageEvent]
        public event ChangePartVolumeDelegate ChangeVolume;

        [PartMessageEvent]
        public event ChangeTextureScaleDelegate ChangeTextureScale;

        [PartMessageEvent]
        public event ChangeAttachNodeSizeDelegate ChangeAttachNodeSize;

        [PartMessageEvent]
        public event PartModelChanged ModelChanged;

        [PartMessageEvent]
        public event PartColliderChanged ColliderChanged;

        protected void RaiseChangeTextureScale(string name, Material material, Vector2 targetScale)
        {
            ChangeTextureScale(name, material, targetScale);
        }
        
        protected void RaiseChangeAttachNodeSize(string name, float minDia, float area, int size)
        {
            ChangeAttachNodeSize(name, minDia, area, size);
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
            this.forceNextUpdate = true;
        }

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
        }

        public void Update()
        {
            try
            {
                using (PartMessageService.Instance.Consolidate(this))
                {
                    bool wasForce = forceNextUpdate;
                    forceNextUpdate = false;

                    UpdateShape(wasForce);

                    if (volumeChanged || wasForce)
                    {
                        ChangeVolume(volume);
                        volumeChanged = false;
                    }
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
        /// <param name="child">Transform offset follower for the attachment</param>
        /// <param name="normalized">If true, the current offset of the attachment is in 'normalized' offset
        /// - where i would be in space on a unit length and diameter cylinder. This method will relocate the object.</param>
        /// <returns>Object used to track the attachment for Remove method</returns>
        public abstract object AddAttachment(TransformFollower attach, bool normalized = false);

        /// <summary>
        /// Remove object attached to the surface of this part.
        /// </summary>
        /// <param name="data">Data returned from child method</param>
        /// <param name="normalize">If true, the transform positon follower will be relocated to a 'normalized' 
        /// offset - where i would appear on a unit length and diameter cylinder</param>
        public abstract TransformFollower RemoveAttachment(object data, bool normalize = false);

        #endregion
    }
}