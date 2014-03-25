using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using System.Linq.Expressions;

namespace KSPAPIExtensions
{
    /// <summary>
    /// This utility class replaces the clunky and rather broken KSPEvent system for passing messages, 
    /// plus the also overly broad SendMessage method in Unity. It allows you to send messages to 
    /// the Part class itself, plus PartModules attached to the part. It can optionally pass the message 
    /// on to children of the part, or to the whole ship.
    /// </summary>
    public static class PartMessageUtils
    {
        /// <summary>
        /// Invoke a method on the part and all enabled modules attached to the part. This is similar in scope to
        /// the SendMessage method on GameObject, only you can send it just to the part and its modules, rather
        /// than all the children. To recieve a message, just declare a method with the PartMessageListener attribute
        /// with any access level who's parameters are compatible with what is passed.
        /// </summary>
        /// <param name="part">the part</param>
        /// <param name="messageName">Name of the method to invoke</param>
        /// <param name="args">parameters</param>
        /// <returns>True if at least one reciever recieved the message</returns>
        public static bool SendPartMessage(this Part part, string messageName, params object[] args)
        {
            return SendPartMessage(part, PartMessageScope.Default, messageName, args);
        }

        /// <summary>
        /// Invoke a method on the part and all enabled modules attached to the part. This is similar in scope to
        /// the SendMessage method on GameObject, only you can send it just to the part and its modules, rather
        /// than all the children. To recieve a message, just declare a method with the PartMessageListener attribute
        /// with any access level who's parameters are compatible with what is passed.
        /// </summary>
        /// <param name="part">the part</param>
        /// <param name="messageName">Name of the method to invoke</param>
        /// <param name="scope">Allow a scope for the message - can send up and down the heirachy.</param>
        /// <param name="args">parameters</param>
        /// <returns>True if at least one reciever recieved the message</returns>
        public static bool SendPartMessage(this Part part, PartMessageScope scope, string messageName, params object[] args)
        {
            bool success = false;

            if ((scope & PartMessageScope.Vessel) == PartMessageScope.Vessel)
            {
                PartMessageScope scopeCall = (scope & (PartMessageScope.IgnoreAttribute)) | PartMessageScope.Self | PartMessageScope.Decendents;
                return SendPartMessage(part.localRoot, scopeCall, messageName, args);
            }

            if ((scope & PartMessageScope.Self) == PartMessageScope.Self)
            {
                bool ignoreAttribute = (scope & PartMessageScope.IgnoreAttribute) == PartMessageScope.IgnoreAttribute;
                success = SendPartMessageToModules(part, messageName, ignoreAttribute, args) || success;
            }

            if ((scope & PartMessageScope.Symmetry) == PartMessageScope.Symmetry)
            {
                bool ignoreAttribute = (scope & PartMessageScope.IgnoreAttribute) == PartMessageScope.IgnoreAttribute;
                foreach (Part p in part.symmetryCounterparts)
                    success = SendPartMessageToModules(p, messageName, ignoreAttribute, args) || success;
            }

            if (part.parent != null && (scope & PartMessageScope.Parent) == PartMessageScope.Parent)
            {
                PartMessageScope scopeCall = (scope & (PartMessageScope.IgnoreAttribute | PartMessageScope.Symmetry)) | PartMessageScope.Self;
                if((scope & PartMessageScope.Ancestors) == PartMessageScope.Ancestors)
                    scopeCall = scopeCall | PartMessageScope.Ancestors;

                success = SendPartMessage(part.parent, scopeCall, messageName, args) || success;
            }

            if ((scope & PartMessageScope.Children) == PartMessageScope.Children)
            {
                PartMessageScope scopeCall = (scope & (PartMessageScope.IgnoreAttribute | PartMessageScope.Symmetry)) | PartMessageScope.Self;
                if ((scope & PartMessageScope.Decendents) == PartMessageScope.Decendents)
                    scopeCall = scopeCall | PartMessageScope.Decendents;

                foreach (Part p in part.children)
                    success = SendPartMessage(p, scopeCall, messageName, args) || success;
            }

            return success;
        }

        private static bool SendPartMessageToModules(Part part, string message, bool ignoreAttribute, object[] args)
        {
            bool success = false;
            if (part.enabled)
                success = SendPartMessageToTarget(part, message, ignoreAttribute, args) || success;
            foreach (PartModule module in part.Modules)
                if (module.enabled && module.isEnabled)
                    success = SendPartMessageToTarget(module, message, ignoreAttribute, args) || success;
            return success;
        }

        private static bool SendPartMessageToTarget(object target, string message, bool ignoreAttribute, object[] args)
        {
            bool success = false;
            Type t = target.GetType();
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == message)
                {
                    if (!ignoreAttribute &&  m.GetCustomAttributes(typeof(PartMessageListenerAttribute), true).Length == 0)
                        continue;

                    // Just invoke it and deal with the consequences, rather than stuff around trying to match parameters
                    // MethodInfo does all the parameter checking anyhow.
                    try
                    {
                        m.Invoke(target, args);
                        success = true;
                    }
                    catch (ArgumentException) { }
                    catch (TargetParameterCountException) { }
                }
            return success;
        }
    }

    /// <summary>
    /// Scope to send part messages. The message can go to various heirachy members.
    /// </summary>
    [Flags]
    public enum PartMessageScope
    {
        Self = 0x1,
        Symmetry = 0x2,
        Children = 0x4,
        Decendents = 0xC,
        Parent = 0x10,
        Ancestors = 0x30,
        Vessel = 0xFF,

        Default = Self,
    }

    /// <summary>
    /// Apply this attribute to any method you wish to recieve messages. The access modifier is
    /// important - public listeners can be called by other Assemblies (other mods) while
    /// internal ones will only be called from events within the same Assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple=true, Inherited=true)]
    public class PartMessageListenerAttribute : Attribute 
    {
        public PartMessageListenerAttribute(Type message, UI_Scene scene = UI_Scene.All)
        {
            if(message == null || !message.IsSubclassOf(typeof(Delegate)))
                throw new ArgumentException("Message is not a delegate type");
            if(message.GetCustomAttributes(typeof(PartMessageEventAttribute), true).Length == 0)
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
    /// Apply this attribute to a part message listener to listen for ksp messages.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class PartKSPEventListenerAttribute : Attribute
    {
        /// <summary>
        /// The message to recieve. If unset will default to the name of the method.
        /// </summary>
        public string kspMessage;
        /// <summary>
        /// If set, provides a mapping between the event's parameters, and parameter names
        /// as provided to a KSPEvent type event. If set the corresponding methods marked
        /// with KSPEvent will be recieved.
        /// </summary>
        public string[] kspEventParams;
    }

    /// <summary>
    /// Apply this attribute to any event you wish to send messages. The access modifier is
    /// important - public events can call other Assemblies (other mods) while internal ones 
    /// will only be called from events within the same Assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Delegate, AllowMultiple = true, Inherited = true)]
    public class PartMessageEventAttribute : Attribute 
    {
        public PartMessageEventAttribute(PartMessageScope scope)
        {
            this.scope = scope;
        }

        /// <summary>
        /// Scope to send the message to. This is to other members of the part heirachy.
        /// </summary>
        public PartMessageScope scope = PartMessageScope.Default;
    }

    // Delegates for some standard events

    /// <summary>
    /// Message for when the part's mass is modified.
    /// </summary>
    [PartMessageEvent(PartMessageScope.Vessel)]
    public delegate void OnPartMassChangedDelegate(Part part);

    /// <summary>
    /// Message for when the part's resource list is modified.
    /// </summary>
    [PartMessageEvent(PartMessageScope.Vessel)]
    public delegate void OnResourcesModifiedDelegate(Part part, float oldmass);

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
                foreach (PartMessageEventAttribute attr in deleg.GetCustomAttributes(typeof(PartMessageEventAttribute), true))
                    GenerateEventHandoff(obj, evt, attr);
            }

            foreach (MethodInfo meth in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (PartMessageListenerAttribute attr in meth.GetCustomAttributes(typeof(PartMessageListenerAttribute), true))
                    AddListener(module, meth, attr);

            }
        }

        private class ListenerInfo
        {
            public PartModule module;
            public MethodInfo method;

            public PartMessageListenerAttribute listenerAttr;
            public KSPEvent kspEvent;
        }

        private Dictionary<string, List<ListenerInfo>> listeners = new Dictionary<string, List<ListenerInfo>>();

        private void AddListener(PartModule module, MethodInfo meth, PartMessageListenerAttribute attr)
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

            string name = attr.message ?? meth.Name;

            List<ListenerInfo> listenerList;
            if (!listeners.TryGetValue(name, out listenerList))
            {
                listenerList = new List<ListenerInfo>();
                listeners[name] = listenerList;
            }

            ListenerInfo info = new ListenerInfo();
            info.module = module;
            info.method = meth;
            info.listenerAttr = attr;

            listenerList.Add(info);

            if (attr.kspEventParams != null)
            {
                if (Events[name] != null)
                {
                    BaseEventList list = (module != null) ? module.Events : part.Events;
                    KSPEvent kspEvent = new KSPEvent();
                    kspEvent.active = true;
                    kspEvent.name = name;

                    BaseEvent baseEvent = new BaseEvent(list, name, data => HandleKSPEvent(name, data), kspEvent);

                    Events.Add(baseEvent);
                }
            }
        }

        private void AddListener(PartModule module, MethodInfo meth, KSPEvent attr)
        {
            List<ListenerInfo> listenerList;
            if (!listeners.TryGetValue(attr.name, out listenerList))
            {
                listenerList = new List<ListenerInfo>();
                listeners[attr.name] = listenerList;
            }

            ListenerInfo info = new ListenerInfo();
            info.module = module;
            info.method = meth;
            info.kspEvent = attr;
            listenerList.Add(info);
        }

        private void HandleKSPEvent(string name, BaseEventData data)
        {
            foreach (ListenerInfo info in listeners[name])
            {
                string[] pList = info.listenerAttr.kspEventParams;
                if (pList == null)
                    continue;

                if (info.module != null && !(info.module.isEnabled && info.module.enabled))
                    continue;

                object target = ((info.module==null)?(object)part:info.module);

                if (pList.Length != data.Keys.Count)
                {
                    Debug.LogWarning(string.Format("PartMessageListener {0} defined in {1} has incorrect kspEvent params", name, target.GetType().Name));
                    return;
                }

                object[] args = new object[pList.Length];
                for (int i = 0; i < pList.Length; i++)
                {
                    if (!data.Keys.Cast<string>().Contains(pList[i]))
                    {
                        Debug.LogWarning(string.Format("PartMessageListener {0} defined in {1} has incorrect kspEvent params", name, target.GetType().Name));
                        return;
                    }
                    args[i] = data.Get(pList[i]);
                }

                try {
                    info.method.Invoke(target, args);
                }
                catch(TargetInvocationException ex) {
                    Debug.LogError(string.Format("PartMessageListener {0} defined in {1} Exception threw excepton when calling listener from kspEvent", name, target.GetType().Name));
                    Debug.LogException(ex.InnerException);
                }
                catch (ArgumentException ex)
                {
                    Debug.LogError(string.Format("PartMessageListener {0} defined in {1} Exception has arguments that don't match the types of the kspEvent", name, target.GetType().Name));
                    Debug.LogException(ex);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Exception thrown when using reflection");
                    Debug.LogException(ex);
                }

            }
        }



        private void GenerateEventHandoff(object source, EventInfo evt, PartMessageEventAttribute attr)
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
            Type delegateType = evt.EventHandlerType;
            MethodInfo m = delegateType.GetMethod("Invoke");

            string message = attr.message ?? delegateType.Name;

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
                Expression.Constant(message), Expression.Constant(attr), Expression.Constant(delegateType), Expression.Constant(internalAssem), Expression.Constant(internalAssem), createArr);
            Delegate d = Expression.Lambda(delegateType, invoke, peLst).Compile();

            // Shouldn't need to use a weak delegate here.
            evt.AddEventHandler(source, d);
        }

        private void EventHandler(string message, PartMessageEventAttribute attr, Type delegateType, bool internalCall, Assembly internalAssem, object[] args)
        {
            if (TestFlag(attr.scope, PartMessageScope.Vessel))
            {
                EventHandlerDecendants(message, attr, delegateType, internalCall, internalAssem, args, false, part.localRoot);
            }
            else if (TestFlag(attr.scope, PartMessageScope.Symmetry) && part.symmetryCounterparts != null && part.symmetryCounterparts.Count > 0)
            {
                EventHandlerSymmetry(message, attr, delegateType, internalCall, internalAssem, args);
            }
            else 
            {
                if (TestFlag(attr.scope, PartMessageScope.Ancestors))
                    EventHandlerAncestors(message, attr, delegateType, internalCall, internalAssem, args, part);
                else if (TestFlag(attr.scope, PartMessageScope.Parent) && part.parent != null)
                    IfExistsInvokeHandler(part.parent, message, attr, delegateType, internalCall, internalAssem, args);

                if (TestFlag(attr.scope, PartMessageScope.Self))
                    EventHandlerSelf(message, attr, delegateType, internalCall, internalAssem, args);

                if (TestFlag(attr.scope, PartMessageScope.Decendents))
                    EventHandlerDecendants(message, attr, delegateType, internalCall, internalAssem, args, true, part);
                else if (TestFlag(attr.scope, PartMessageScope.Children))
                    EventHandlerDecendants(message, attr, delegateType, internalCall, internalAssem, args, false, part);
            }
        }

        private void EventHandlerSymmetry(string message, PartMessageEventAttribute attr, Type delegateType, bool internalCall, Assembly internalAssem, object[] args)
        {
            // Invoke the ancestors first, then the symmetrical bits.
            if ((uint)(attr.scope & PartMessageScope.Ancestors) != 0)
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

                if(TestFlag(attr.scope, PartMessageScope.Ancestors)) 
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
                    mgr.EventHandlerSelf(message, attr, delegateType, internalCall, internalAssem, args);
                }
            }

            // Just build a new attribute that doesn't include the parents or the symmetry, and invoke
            PartMessageEventAttribute symAttr = new PartMessageEventAttribute();
            symAttr.message = attr.message;
            symAttr.scope = (attr.scope & ~(PartMessageScope.Symmetry | PartMessageScope.Ancestors)) | PartMessageScope.Self;
            symAttr.kspEventParams = attr.kspEventParams;

            foreach (Part sym in part.symmetryCounterparts)
            {
                PartMessageManager symMgr = sym.GetComponent<PartMessageManager>();
                symMgr.EventHandler(message, symAttr, delegateType, internalCall, internalAssem, args);
            }

            if(TestFlag(attr.scope, PartMessageScope.Self))
                EventHandler(message, symAttr, delegateType, internalCall, internalAssem, args);
        }



        private static void EventHandlerAncestors(string message, PartMessageEventAttribute attr, Type delegateType, bool internalCall, Assembly internalAssem, object[] args, Part part)
        {
            if (part.parent == null)
                return;
            part = part.parent;
            EventHandlerAncestors(message, attr, delegateType, internalCall, internalAssem, args, part);
            IfExistsInvokeHandler(part, message, attr, delegateType, internalCall, internalAssem, args);
        }

        private static void EventHandlerDecendants(string message, PartMessageEventAttribute attr, Type delegateType, bool internalCall, Assembly internalAssem, object[] args, bool recurse, Part part)
        {
            foreach (Part child in part.children)
            {
                IfExistsInvokeHandler(part, message, attr, delegateType, internalCall, internalAssem, args);
                if (recurse)
                    EventHandlerDecendants(message, attr, delegateType, internalCall, internalAssem, args, true, child);
            }
        }

        private static void IfExistsInvokeHandler(Part part, string message, PartMessageEventAttribute attr, Type delegateType, bool internalCall, Assembly internalAssem, object[] args)
        {
            PartMessageManager childManager = part.transform.GetComponent<PartMessageManager>();
            if (childManager != null)
                childManager.EventHandlerSelf(message, attr, delegateType, internalCall, internalAssem, args);
        }

        internal void EventHandlerSelf(string message, PartMessageEventAttribute attr, Type delegateType, bool internalCall, Assembly internalAssem, object[] args)
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



                if (info.listenerAttr != null)
                {
                    Delegate d = Delegate.CreateDelegate(delegateType, (object)info.module ?? part, info.method, false);
                    if (d != null)
                    {
                        try
                        {
                            d.DynamicInvoke(args);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError("Exception when invoking event handler:");
                            Debug.LogException(ex);
                        }
                    }
                }

                if (info.kspEvent != null)
                {

                }
            }
        }

        private static bool TestFlag(PartMessageScope e, PartMessageScope flags)
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
                if (info.GetCustomAttributes(typeof(PartMessageEventAttribute), true).Length > 0)
                    return true;

            foreach (MethodInfo meth in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (meth.GetCustomAttributes(typeof(PartMessageListenerAttribute), true).Length > 0
                    || meth.GetCustomAttributes(typeof(KSPEvent), true).Length > 0)
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
