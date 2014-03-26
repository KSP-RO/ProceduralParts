using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using System.Linq.Expressions;

namespace KSPAPIExtensions.PartMessage
{

    /// <summary>
    /// Scope to send part messages. The message can go to various heirachy members.
    /// </summary>
    [Flags]
    public enum PartRelationship
    {
        Self = 0x1,
        Symmetry = 0x2,
        Children = 0x4,
        Decendents = 0xC,
        Parent = 0x10,
        Ancestors = 0x30,
        Vessel = 0xFF,
    }

    /// <summary>
    /// Apply this attribute to any method you wish to recieve messages. 
    /// 
    /// The access modifier is important - public events can call other Assemblies (other mods) and should use a public
    /// delegate and will pass messages to public listener methods. 
    /// Internal messages are internal to a particular Assembly, will pass messages to internal listeners in the
    /// same assembly, and should use internal delegates.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple=true, Inherited=true)]
    public class PartMessageListener : Attribute 
    {
        public PartMessageListener(Type message, UI_Scene scene = UI_Scene.All)
        {
            if(message == null || !message.IsSubclassOf(typeof(Delegate)))
                throw new ArgumentException("Message is not a delegate type");
            if(message.GetCustomAttributes(typeof(PartMessageEvent), true).Length == 0)
                throw new ArgumentException("Message does not have the PartMessageEvent attribute");

            this.message = message;
            this.scene = scene;
        }

        /// <summary>
        /// The delegate type that we are listening for.
        /// </summary>
        public readonly Type message;
        /// <summary>
        /// Scene to listen for message in. Defaults to All.
        /// </summary>
        public readonly UI_Scene scene;
    }

    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Delegate)]
    public class PartMessageEvent : Attribute { }


    /// <summary>
    /// PartMessageListeners can use the properties in this class to examine the source of the message
    /// </summary>
    public static class PartMessageCallerInfo
    {
        public static object source 
        { 
            get { 
                return curr.Peek().source; 
            } 
        }

        public static EventInfo srcEvent 
        {   
            get
            {
                return curr.Peek().evt;
            }
        }

        public static Part srcPart { 
            get { 
                object src = source;
                if(src is Part)
                    return (Part)src;
                if(src is PartModule)
                    return ((PartModule)src).part;
                return null;
            } 
        }

        public static PartModule srcModule
        {
            get { return source as PartModule; }
        }

        public static Type srcDelegateType 
        { 
            get { return srcEvent.EventHandlerType; } 
        }

        public static bool isSourcePartRelation(Part listener, PartRelationship relation)
        {
            Part src = srcPart;
            if (src == null)
                return false;

            if (TestFlag(relation, PartRelationship.Vessel))
                return src.localRoot == listener.localRoot;

            if (TestFlag(relation, PartRelationship.Self) && src == listener)
                return true;

            if (TestFlag(relation, PartRelationship.Ancestors)) 
            {
                for (Part upto = listener.parent; upto != null; upto = upto.parent) 
                    if(upto == src)
                        return true;
            }
            else if (TestFlag(relation, PartRelationship.Parent) && src == listener.parent)
                    return true;

            if (TestFlag(relation, PartRelationship.Decendents))
            {
                for (Part upto = src.parent; upto != null; upto = upto.parent)
                    if (upto == listener)
                        return true;
            }
            else if (TestFlag(relation, PartRelationship.Children) && src.parent == listener)
                return true;

            if (TestFlag(relation, PartRelationship.Symmetry))
                foreach (Part sym in listener.symmetryCounterparts)
                    if (src == sym)
                        return true;

            return false;
        }

        public static bool isSourceSamePart(Part listener)
        {
            return srcPart == listener;
        }

        public static bool isSourceSameVessel(Part listener)
        {
            return srcPart.localRoot == listener.localRoot;
        }

        #region Internal Bits
        private static Stack<Info> curr = new Stack<Info>();

        static internal IDisposable Push(object source, EventInfo evt)
        {
            return new Info(source, evt);
        }

        private class Info : IDisposable
        {
            internal Info(object source, EventInfo evt)
            {
                this.source = source;
                this.evt = evt;
                curr.Push(this);
            }

            internal object source;
            internal EventInfo evt;

            void IDisposable.Dispose()
            {
                curr.Pop();
            }
        }

        private static bool TestFlag(PartRelationship e, PartRelationship flags)
        {
            return (e & flags) == flags;
        }
        #endregion
    }

    // Delegates for some standard events

    /// <summary>
    /// Message for when the part's mass is modified.
    /// </summary>
    [PartMessageEvent]
    public delegate void PartMassChanged();

    /// <summary>
    /// Message for when the part's CoMOffset changes.
    /// </summary>
    [PartMessageEvent]
    public delegate void PartCoMOffsetChanged();

    /// <summary>
    /// Message for when the part's resource list is modified (added to or subtracted from).
    /// </summary>
    [PartMessageEvent]
    public delegate void PartResourceListModified();

    /// <summary>
    /// Message for when the max amount of a resource is modified.
    /// </summary>
    [PartMessageEvent]
    public delegate void PartResourceMaxAmountModified(PartResource resource);

    /// <summary>
    /// Message for when some change has been made to the part's model or collider.
    /// </summary>
    [PartMessageEvent]
    public delegate void PartModelModified();

    internal class PartMessageManager : PartModule
    {
        public override void OnInitialize()
        {
            if(HighLogic.LoadedScene != GameScenes.EDITOR)
                return;
            ScanAndAttach();
        }

        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;
            // We need to delay the initialization of the listeners until on load in flight mode, since the root
            // gets munged.
            ScanAndAttach();
        }

        private void ScanAndAttach()
        {
            ScanAndAttach(part, null);
            foreach (PartModule child in part.Modules)
                ScanAndAttach(child, child);
        }

        private void ScanAndAttach(object obj, PartModule module) 
        {
            Type t = obj.GetType();

            foreach (EventInfo evt in t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Type deleg = evt.EventHandlerType;
                foreach (PartMessageEvent attr in deleg.GetCustomAttributes(typeof(PartMessageEvent), true))
                    GenerateEventHandoff(obj, module, evt, attr);
            }

            foreach (MethodInfo meth in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (PartMessageListener attr in meth.GetCustomAttributes(typeof(PartMessageListener), true))
                    AddListener(module, meth, attr);
            }
        }

        private class ListenerInfo
        {
            public PartModule module;
            public MethodInfo method;

            public PartMessageListener listenerAttr;
        }

        private Dictionary<Type, List<ListenerInfo>> listeners = new Dictionary<Type, List<ListenerInfo>>();

        private void AddListener(PartModule module, MethodInfo meth, PartMessageListener attr)
        {
            switch (HighLogic.LoadedScene)
            {
                case GameScenes.EDITOR:
                    if ((attr.scene & UI_Scene.Editor) != UI_Scene.Editor)
                        return;
                    break;
                case GameScenes.FLIGHT:
                    if ((attr.scene & UI_Scene.Flight) != UI_Scene.Flight)
                        return;
                    break;
                default:
                    return;
            }

            Type message = attr.message;

            if (Delegate.CreateDelegate(attr.message, meth, false) == null)
            {
                Debug.LogError(string.Format("PartMessageListener method {0}.{1} does not support the delegate type {2} as declared in the attribute", meth.DeclaringType, meth.Name, attr.message.Name));
                return;
            }

            List<ListenerInfo> listenerList;
            if (!listeners.TryGetValue(message, out listenerList))
            {
                listenerList = new List<ListenerInfo>();
                listeners.Add(message, listenerList);
            }

            ListenerInfo info = new ListenerInfo();
            info.module = module;
            info.method = meth;
            info.listenerAttr = attr;

            listenerList.Add(info);
        }

        private void GenerateEventHandoff(object source, PartModule module, EventInfo evt, PartMessageEvent attr)
        {
            MethodAttributes addAttrs = evt.GetAddMethod(true).Attributes;

            if ((uint)(addAttrs & (MethodAttributes.Public | MethodAttributes.Assembly)) == 0)
            {
                Debug.LogWarning(string.Format("Event {0} in class {1} is not public or internal, cannot generate message manager.", evt.Name, source.GetType().Name));
                return;
            }

            bool internalCall = ((addAttrs & MethodAttributes.Assembly) == MethodAttributes.Assembly);
            Assembly internalAssem = Assembly.GetAssembly(source.GetType());

            // This generates a dynamic method that pulls the properties of the event
            // plus the arguments passed and hands it off to the EventHandler method below.
            Type message = evt.EventHandlerType;
            MethodInfo m = message.GetMethod("Invoke");

            ParameterInfo[] pLst = m.GetParameters();
            ParameterExpression[] peLst = new ParameterExpression[pLst.Length];
            Expression[] cvrt = new Expression[pLst.Length];
            for (int i = 0; i < pLst.Length; i++)
            {
                peLst[i] = Expression.Parameter(pLst[i].ParameterType, pLst[i].Name);
                cvrt[i] = Expression.Convert(peLst[i], typeof(object));
            }

            Expression createArr = Expression.NewArrayInit(typeof(object), cvrt);

            Expression invoke = Expression.Call(Expression.Constant(this), GetType().GetMethod("EventHandler"),
                Expression.Constant(message), Expression.Constant(module), Expression.Constant(evt), Expression.Constant(attr), Expression.Constant(internalCall), Expression.Constant(internalAssem), createArr);
            Delegate d = Expression.Lambda(message, invoke, peLst).Compile();

            // Shouldn't need to use a weak delegate here.
            evt.AddEventHandler(source, d);
        }

        private void EventHandler(Type message, PartModule module, EventInfo evt, PartMessageEvent attr, bool internalCall, Assembly internalAssem, object[] args)
        {
            using (var info = new PartMessageCallerInfo.Info(part, module, evt, attr))
            {
                if (TestFlag(attr.scope, PartRelationship.Vessel))
                {
                    EventHandlerDecendants(message, attr, internalCall, internalAssem, args, false, part.localRoot);
                }
                else if (TestFlag(attr.scope, PartRelationship.Symmetry) && part.symmetryCounterparts != null && part.symmetryCounterparts.Count > 0)
                {
                    EventHandlerSymmetry(message, attr, internalCall, internalAssem, args);
                }
                else
                {
                    if (TestFlag(attr.scope, PartRelationship.Ancestors))
                        EventHandlerAncestors(message, attr, internalCall, internalAssem, args, part);
                    else if (TestFlag(attr.scope, PartRelationship.Parent) && part.parent != null)
                        IfExistsInvokeHandler(part.parent, message, attr, internalCall, internalAssem, args);

                    if (TestFlag(attr.scope, PartRelationship.Self))
                        EventHandlerSelf(message, attr, internalCall, internalAssem, args);

                    if (TestFlag(attr.scope, PartRelationship.Decendents))
                        EventHandlerDecendants(message, attr, internalCall, internalAssem, args, true, part);
                    else if (TestFlag(attr.scope, PartRelationship.Children))
                        EventHandlerDecendants(message, attr, internalCall, internalAssem, args, false, part);
                }
            }
        }

        private void EventHandlerSymmetry(Type message, PartMessageEvent attr, bool internalCall, Assembly internalAssem, object[] args)
        {
            // Invoke the ancestors first, then the symmetrical bits.
            if ((uint)(attr.scope & PartRelationship.Ancestors) != 0)
            {
                // so at some point between here and the common ancestor, ancestors may merge
                // Consider (parts A, B, C. Bx2 represents symmetry)  A -> ( Bx2 -> ( Cx3, Cx3, Cx3 ), Bx2 -> ( Cx3, Cx3, Cx3 ) )

                // So build a list of all the parents through to the root.

                LinkedList<Part> toInvoke = new LinkedList<Part>();
                HashSet<Part> inList = new HashSet<Part>();

                // Add the direct parents to the list.
                foreach (Part p in part.symmetryCounterparts)
                    if(!inList.Add(part.parent))
                        toInvoke.AddLast(p.parent);

                if(TestFlag(attr.scope, PartRelationship.Ancestors)) 
                    for (var next = toInvoke.First; next != null; )
                    {
                        // if not already present, add the parent to the list
                        if (next.Value.parent != null && !inList.Add(next.Value.parent))
                            toInvoke.AddLast(next.Value.parent);

                        // Remove any element from the list that doesn't have a message manager
                        if (next.Value.transform.GetComponent<PartMessageManager>() == null)
                        {
                            var tmp = next;
                            next = next.Next;
                            toInvoke.Remove(tmp);
                        }
                        else
                        {
                            next = next.Next;
                        }
                    }

                // go backwards down the list and call the event handler
                for (var next = toInvoke.Last; next != null; next = next.Previous)
                {
                    PartMessageManager mgr = next.Value.transform.GetComponent<PartMessageManager>();
                    mgr.EventHandlerSelf(message, attr, internalCall, internalAssem, args);
                }
            }

            // Just build a new attribute that doesn't include the parents or the symmetry, and invoke the handler again.
            PartMessageEvent symAttr = new PartMessageEvent((attr.scope & ~(PartRelationship.Symmetry | PartRelationship.Ancestors)) | PartRelationship.Self);

            foreach (Part sym in part.symmetryCounterparts)
            {
                PartMessageManager symMgr = sym.GetComponent<PartMessageManager>();
                symMgr.EventHandler(message, PartMessageCallerInfo.srcModule, PartMessageCallerInfo.srcEvent, symAttr, internalCall, internalAssem, args);
            }

            if(TestFlag(attr.scope, PartRelationship.Self))
                EventHandler(message, PartMessageCallerInfo.srcModule, PartMessageCallerInfo.srcEvent, symAttr, internalCall, internalAssem, args);
        }

        private static void EventHandlerAncestors(Type message, PartMessageEvent attr, bool internalCall, Assembly internalAssem, object[] args, Part part)
        {
            if (part.parent == null)
                return;
            part = part.parent;
            EventHandlerAncestors(message, attr, internalCall, internalAssem, args, part);
            IfExistsInvokeHandler(part, message, attr, internalCall, internalAssem, args);
        }

        private static void EventHandlerDecendants(Type message, PartMessageEvent attr, bool internalCall, Assembly internalAssem, object[] args, bool recurse, Part part)
        {
            foreach (Part child in part.children)
            {
                IfExistsInvokeHandler(part, message, attr, internalCall, internalAssem, args);
                if (recurse)
                    EventHandlerDecendants(message, attr, internalCall, internalAssem, args, true, child);
            }
        }

        private static void IfExistsInvokeHandler(Part part, Type message, PartMessageEvent attr, bool internalCall, Assembly internalAssem, object[] args)
        {
            PartMessageManager childManager = part.transform.GetComponent<PartMessageManager>();
            if (childManager != null)
                childManager.EventHandlerSelf(message, attr, internalCall, internalAssem, args);
        }

        internal void EventHandlerSelf(Type message, PartMessageEvent attr, bool internalCall, Assembly internalAssem, object[] args)
        {
            List<ListenerInfo> listeners;
            if (!this.listeners.TryGetValue(message, out listeners))
                return;

            foreach (ListenerInfo info in listeners)
            {
                // Access levels must match, and internals match only the same assembly
                bool internalListener = (info.method.Attributes & MethodAttributes.Assembly) == MethodAttributes.Assembly;
                if (internalCall != internalListener || (internalCall && internalAssem != ((object)info.module ?? part).GetType().Assembly))
                    continue;

                // Module needs to be enabled
                if (info.module != null && (!info.module.isEnabled || !info.module.enabled))
                    continue;

                object target = (object)info.module ?? part;

                if (info.listenerAttr != null)
                {
                    try
                    {
                        info.method.Invoke(target, args);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("Exception when invoking event handler:");
                        Debug.LogException(ex);
                    }
                }
            }
        }

        private static bool TestFlag(PartRelationship e, PartRelationship flags)
        {
            return (e & flags) == flags;
        }

    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal class PartMessageActivator : MonoBehaviour
    {
        // Version of the compatibility checker itself.
        private static int _version = 1;

        private static FieldInfo moduleListListField
            = typeof(PartModuleList)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(t => t.FieldType == typeof(List<PartModule>))
                .First();

        public void Start()
        {
            // Checkers are identified by the type name and version field name.
            FieldInfo[] fields =
                getAllTypes()
                .Where(t => t.Name == typeof(PartMessageActivator).Name)
                .Select(t => t.GetField("_version", BindingFlags.Static | BindingFlags.NonPublic))
                .Where(f => f != null)
                .Where(f => f.FieldType == typeof(int))
                .ToArray();

            // Let the latest version of the checker execute.
            if (_version != fields.Max(f => (int)f.GetValue(null))) { return; }

            Debug.Log(String.Format("[PartMessageActivator] Running {3} version {0} from '{1}'", _version, Assembly.GetExecutingAssembly().GetName().Name, typeof(PartMessageActivator).Name));

            // Other checkers will see this version and not run.
            // This accomplishes the same as an explicit "ran" flag with fewer moving parts.
            _version = int.MaxValue;

            // Run through all the available parts, and add the message manager to any that send messages.
            foreach (AvailablePart p in PartLoader.LoadedPartsList)
            {
                Part part = p.partPrefab;

                bool needsManager = NeedsManager(part.GetType());
                if (!needsManager)
                    foreach (PartModule module in part.Modules)
                        if (needsManager = NeedsManager(module.GetType()))
                            break;

                if (!needsManager)
                    continue;

                PartMessageManager manager = p.partPrefab.gameObject.AddComponent<PartMessageManager>();

                // Need to do a bit of reflection to stick it first in the module list.
                List<PartModule> moduleList = (List<PartModule>)moduleListListField.GetValue(part);
                moduleList.Insert(0, manager);
            }

        }

        private static bool NeedsManager(Type t)
        {
            foreach (EventInfo info in t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (info.GetCustomAttributes(typeof(PartMessageEvent), true).Length > 0)
                    return true;

            foreach (MethodInfo meth in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (meth.GetCustomAttributes(typeof(PartMessageListener), true).Length > 0)
                    return true;

            return false;
        }



        private static IEnumerable<Type> getAllTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (Exception)
                {
                    types = Type.EmptyTypes;
                }

                foreach (var type in types)
                {
                    yield return type;
                }
            }
        }
    }
}
