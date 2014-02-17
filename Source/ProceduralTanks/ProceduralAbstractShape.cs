using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public abstract class ProceduralAbstractShape : PartModule
{
    #region Config data
    [KSPField]
    public string displayName;

    [KSPField]
    public string techRequired;

    #endregion

    #region Objects
    public ProceduralPart tank
    {
        get
        {
            if (_tank == null)
                _tank = GetComponent<ProceduralPart>();
            return _tank;
        }
    }
    private ProceduralPart _tank;

    public Mesh sidesMesh {
        get { return tank.sidesMesh; } 
    }

    public Mesh endsMesh
    {
        get { return tank.endsMesh; }
    }
    #endregion

    #region Shape details

    public float tankVolume
    {
        get { return _tankVolume; }
        protected set
        {
            if (value != _tankVolume)
            {
                _tankVolume = value;
                tankVolumeChanged = true;
            }
        }
    }
    private float _tankVolume;
    private bool tankVolumeChanged = false;

    public Vector2 tankTextureScale
    {
        get { return _tankTextureScale; }
        protected set
        {
            if (value != _tankTextureScale)
            {
                _tankTextureScale = value;
                tankTextureScaleChanged = true;
            }
        }
    }
    private Vector2 _tankTextureScale;
    private bool tankTextureScaleChanged = false;

    #endregion

    #region Callbacks

    private bool skipNextUpdate = false;
    private bool forceNextUpdate = true;

    public void SkipNextUpdate()
    {
        if (skipNextUpdate)
            return;

        skipNextUpdate = true;
        tank.SkipNextUpdate();
    }

    public void ForceNextUpdate()
    {
        this.forceNextUpdate = true;
    }

    public void Update()
    {
        try
        {
            if (skipNextUpdate)
            {
                skipNextUpdate = false;
                return;
            }

            bool wasForce = forceNextUpdate;
            forceNextUpdate = false;

            UpdateShape(wasForce);

            if (tankVolumeChanged || wasForce)
            {
                gameObject.SendMessage("UpdateTankVolume", tankVolume);
                tankVolumeChanged = false;
            }

            if (tankTextureScaleChanged || wasForce)
            {
                gameObject.SendMessage("UpdateTankTextureScale", tankTextureScale);
                tankTextureScaleChanged = false;
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
    /// Add object attached to the surface of this tank.
    /// Base classes should proportionally move the location and orientation (rotation) as the tank stretches.
    /// The return value will be passed back to removeTankAttachment when i's detached
    /// </summary>
    /// <param name="child">Transform offset follower for the attachment</param>
    /// <param name="normalized">If true, the current offset of the attachment is in 'normalized' offset
    /// - where i would be in space on a unit length and diameter cylinder. This method will relocate the object.</param>
    /// <returns>Object used to track the attachment for Remove method</returns>
    public abstract object AddAttachment(TransformFollower attach, bool normalized = false);

    /// <summary>
    /// Remove object attached to the surface of this tank.
    /// </summary>
    /// <param name="data">Data returned from child method</param>
    /// <param name="normalize">If true, the transform positon follower will be relocated to a 'normalized' 
    /// offset - where i would appear on a unit length and diameter cylinder</param>
    public abstract TransformFollower RemoveAttachment(object data, bool normalize = false);

    #endregion
}