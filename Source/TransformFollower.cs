using UnityEngine;

namespace ProceduralParts
{
    public class TransformFollower : MonoBehaviour
    {
        [SerializeField]
        private Transformable target;
        [SerializeField]
        private bool hasParent;
        [SerializeField]
        private Vector3 oldOffset;
        [SerializeField]
        private Quaternion oldParentRotation;
        [SerializeField]
        private Quaternion oldLocalRotation;

        private void SetParentInternal(Transform newParent)
        {
            if (newParent == null)
            {
                hasParent = false;
                transform.parent = null;
                oldOffset = transform.position;
                oldParentRotation = Quaternion.identity;
                oldLocalRotation = transform.localRotation;
            }
            else
            {
                hasParent = true;
                transform.parent = newParent;
                oldOffset = transform.position - newParent.position;
                oldParentRotation = transform.parent.rotation;
                oldLocalRotation = transform.localRotation;
            }
        }

        private void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                ForceUpdate();
        }

        public void ForceUpdate()
        {
            if (target.Destroyed)
            {
                Destroy(gameObject);
                print("Target destroyed: " + ((target==null)?"null":target.name));
                return;
            }
            
            if (hasParent != (transform.parent != null))
            {
                print("Setting parent during update: " + transform.parent);
                SetParentInternal(transform.parent);
                return;
            }

            DoTranslation();

            DoRotation();
        }

        /// <summary>
        /// Updates the local rotation to the orientation supplied. This will *not* pass the orientation change
        /// to the attached object, so is useful to set the orientation to some known 'reference' (ie the surface normal)
        /// </summary>
        public void SetLocalRotationReference(Quaternion orientation)
        {
            DoRotation();
            oldLocalRotation = transform.localRotation = orientation;
        }

        private void DoTranslation()
        {
            Vector3 trans;
            if (hasParent)
            {
                if (oldParentRotation != transform.parent.rotation) // http://docs.unity3d.com/ScriptReference/Quaternion-operator_ne.html
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
            target.Translate(trans);
        }

        private void DoRotation()
        {
            if (oldLocalRotation == transform.localRotation)
                return;

            // What we want to apply is the change in rotation. This is equivalent to undoing (inverse) the old rotation, and applying the new one
            Quaternion rot;
            if (hasParent)
                rot = transform.parent.rotation * (oldLocalRotation.Inverse() * transform.localRotation) * transform.parent.rotation.Inverse();
            else
                rot = oldLocalRotation.Inverse() * transform.localRotation;

            target.Rotate(rot);
            oldLocalRotation = transform.localRotation;
        }

        public static TransformFollower CreateFollower(Transform attached)
        {
            return CreateFollower(null, attached.position, new TransformTransformable(attached));
        }

        public static TransformFollower CreateFollower(Transform parent, Transform attached)
        {
            return CreateFollower(parent, attached.position, new TransformTransformable(attached));
        }

        public static TransformFollower CreateFollower(Transform attached, Vector3 offset, Space offsetSpace = Space.Self)
        {
            return CreateFollower(null, attached, offset, offsetSpace);
        }

        public static TransformFollower CreateFollower(Transform parent, Transform attached, Vector3 offset, Space offsetSpace = Space.Self)
        {
            Vector3 worldOffset, localOffset;
            if (offsetSpace == Space.Self)
            {
                worldOffset = attached.TransformDirection(offset);
                localOffset = offset;
            }
            else
            {
                worldOffset = offset;
                localOffset = attached.InverseTransformDirection(offset);
            }
            return CreateFollower(parent, attached.position + worldOffset, new TransformTransformable(attached, localOffset));
        }

        public static TransformFollower CreateFollower(Vector3 position, Transformable target)
        {
            return CreateFollower(null, position, target);
        }

        public static TransformFollower CreateFollower(Transform parent, Vector3 position, Transformable target)
        {
            GameObject go = new GameObject("Follower:" + target, typeof(TransformFollower));
            TransformFollower loc = go.GetComponent<TransformFollower>();

            loc.target = target;

            go.transform.position = position;
            loc.SetParentInternal(parent);

            return loc;
        }

        public abstract class Transformable : Object
        {
            public abstract bool Destroyed { get; }

            public abstract void Translate(Vector3 translation);

            public abstract void Rotate(Quaternion rotate);
        }

        public class TransformTransformable : Transformable
        {

            [SerializeField]
            private readonly Transform transform;
            [SerializeField]
            private Vector3? offset;

            // ReSharper disable once RedundantArgumentDefaultValue
            public TransformTransformable(Transform transform) : this(transform, null) { }

            public TransformTransformable(Transform transform, Vector3 offset, Space offsetSpace = Space.Self) : this(transform, (Vector3?)offset, offsetSpace) { }

            private TransformTransformable(Transform transform, Vector3? offset = null, Space offsetSpace = Space.Self)
            {
                this.transform = transform;
                if (offset == null || offset.Value == Vector3.zero)
                    this.offset = null;
                else if (offsetSpace == Space.Self)
                    this.offset = offset;
                else
                    this.offset = transform.InverseTransformDirection(offset.Value);
            }

            override public bool Destroyed
            {
                get
                {
                    return !transform;
                }
            }

            override public void Translate(Vector3 translation)
            {
                transform.Translate(translation, Space.World);
            }

            override public void Rotate(Quaternion rotate)
            {
                if (offset == null)
                {
                    transform.rotation = rotate * transform.rotation;
                }
                else
                {
                    transform.Translate(offset.Value);
                    transform.rotation = rotate * transform.rotation;
                    transform.Translate(-offset.Value);
                }
            }

            public override string ToString()
            {
                return transform.ToString();
            }
        }
    }

}