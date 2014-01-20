using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;

public class TransformPositionFollower : MonoBehaviour
{
    private Predicate<Vector3> Translate;
    private Vector3 oldOffset;
    private Quaternion oldParentRotation; 
    private bool hasParent = false;

    public void Update()
    {
        if (hasParent != (transform.parent != null))
        {
            if (hasParent)
            {
                // have lost the parent
                hasParent = false;
                oldOffset = transform.position;
            }
            else
            {
                // have gained the parent
                hasParent = true;
                oldOffset = transform.position - transform.parent.position;
                oldParentRotation = transform.parent.rotation;
            }
            return;
        }

        Vector3 trans;
        if (hasParent)
        {
            if (oldParentRotation != transform.parent.rotation)
            {
                oldOffset = transform.parent.rotation * (Quaternion.Inverse(oldParentRotation) * oldOffset);
                oldParentRotation = transform.parent.rotation;
            }

            Vector3 offset = transform.position - transform.parent.position;

            if (offset == oldOffset)
                return;
            
            trans = offset - oldOffset;
            oldOffset = offset;
        }
        else
        {
            if (transform.position == oldOffset)
                return;

            trans = transform.position - oldOffset;
            oldOffset = transform.position;
        }

        if (!Translate(trans))
            Destroy(gameObject);
    }

    public void Detach()
    {
        Destroy(gameObject);
    }

    public bool detached
    {
        get { return !gameObject; }
    }


    public static Transform createFollower(Transform attached)
    {
        return createFollower(attached.position, createFollowerTranslate(attached));
    }

    public static Transform createFollower(Transform attached, Vector3 offset, Space offsetSpace = Space.Self)
    {
        if (offsetSpace == Space.Self)
            offset = attached.TransformDirection(offset);

        return createFollower(attached.position+offset, createFollowerTranslate(attached));
    }

    public static Transform createFollower(Vector3 position, Predicate<Vector3> Translate)
    {
        GameObject go = new GameObject("childPart", typeof(TransformPositionFollower));
        TransformPositionFollower loc = go.GetComponent<TransformPositionFollower>();

        loc.Translate = Translate;

        go.transform.position = position;
        loc.oldOffset = position;

        return go.transform;
    }

    public static Predicate<Vector3> createFollowerTranslate(Transform attached)
    {
        return (trans => { attached.Translate(trans, Space.World); return attached.gameObject; });
    }

}

