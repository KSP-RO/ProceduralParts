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

    public void SetParent(Transform newParent)
    {
        if (newParent == null)
        {
            hasParent = false;
            transform.parent = null;
            oldOffset = transform.position;
        }
        else
        {
            hasParent = true;
            transform.parent = newParent;
            oldOffset = transform.position - newParent.position;
            oldParentRotation = transform.parent.rotation;
        }
    }

    public void Update()
    {
        if (hasParent != (transform.parent != null))
        {
            SetParent(transform.parent);
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


    public static TransformPositionFollower createFollower(Transform attached)
    {
        return createFollower(null, attached.position, createFollowerTranslate(attached));
    }

    public static TransformPositionFollower createFollower(Transform parent, Transform attached)
    {
        return createFollower(parent, attached.position, createFollowerTranslate(attached));
    }

    public static TransformPositionFollower createFollower(Transform attached, Vector3 offset, Space offsetSpace = Space.Self)
    {
        return createFollower(null, attached, offset, offsetSpace);
    }

    public static TransformPositionFollower createFollower(Transform parent, Transform attached, Vector3 offset, Space offsetSpace = Space.Self)
    {
        if (offsetSpace == Space.Self)
            offset = attached.TransformDirection(offset);

        return createFollower(parent, attached.position + offset, createFollowerTranslate(attached));
    }

    public static TransformPositionFollower createFollower(Vector3 position, Predicate<Vector3> Translate)
    {
        return createFollower(null, position, Translate);
    }

    public static TransformPositionFollower createFollower(Transform parent, Vector3 position, Predicate<Vector3> Translate)
    {
        GameObject go = new GameObject("childPart", typeof(TransformPositionFollower));
        TransformPositionFollower loc = go.GetComponent<TransformPositionFollower>();

        loc.Translate = Translate;

        go.transform.position = position;
        loc.oldOffset = position;
        loc.SetParent(parent);

        return loc;
    }

    public static Predicate<Vector3> createFollowerTranslate(Transform attached)
    {
        return (trans => { attached.Translate(trans, Space.World); return attached.gameObject; });
    }

}

