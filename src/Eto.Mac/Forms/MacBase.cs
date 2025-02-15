namespace Eto.Mac.Forms
{
	public interface IMacControlHandler
	{
		NSView ContainerControl { get; }

		Size MinimumSize { get; set; }

		bool IsEventHandled(string eventName);

		NSView ContentControl { get; }
		NSView EventControl { get; }
		NSView FocusControl { get; }
		NSView TextInputControl { get; }

		void RecalculateKeyViewLoop(ref NSView last);

		void InvalidateMeasure();
	}
	
	public enum ObserverType
	{
		Control,
		NotificationCenter
	}

	[Register("ObserverHelper")]
	public class ObserverHelper : NSObject
	{
		bool hasNotification;
		bool hasControl;
		
		public ObserverType Type { get; set; }

		public Action<ObserverActionEventArgs> Action { get; set; }

		public NSString KeyPath { get; set; }

		public IntPtr ControlHandle { get; set; }

		public NSObject Control => Runtime.GetNSObject(ControlHandle);

		WeakReference handler;

		public object Handler { get => handler?.Target; set => handler = new WeakReference(value); }
		
		public Widget Widget => (Handler as Widget.IHandler)?.Widget;

		static readonly Selector selPerformAction = new Selector("performAction:");

		[Export("performAction:")]
		public void PerformAction(NSNotification notification)
		{
			Action(new ObserverActionEventArgs(this, notification));
		}

		public override void ObserveValue(NSString keyPath, NSObject ofObject, NSDictionary change, IntPtr context)
		{
			Action(new ObserverActionEventArgs(this, null));
		}
		
		public void Register()
		{
			if (Type == ObserverType.NotificationCenter)
				AddToNotificationCenter();
			else
				AddToControl();
		}
		
		void AddToNotificationCenter()
		{
			var c = Control;
			if (!hasNotification && c != null)
			{
				// Console.WriteLine($"Adding notification center observer for {KeyPath}, Handler: {Handler?.GetType()}, Control: {c.GetType()}");
				NSNotificationCenter.DefaultCenter.AddObserver(this, selPerformAction, KeyPath, c);
				hasNotification = true;
			}
			else if (!hasNotification)
			{
				Debug.WriteLine($"WARNING: Could not add notification center observer for {KeyPath}, Handler: {Handler?.GetType()}. {ControlHandle} points to a null object");
			}
			
		}

		void AddToControl()
		{
			var c = Control;
			if (!hasControl && c != null)
			{
				// Console.WriteLine($"Adding control observer for {KeyPath}, Handler: {Handler?.GetType()}, Control: {c.GetType()}");
				c.AddObserver(this, KeyPath, NSKeyValueObservingOptions.New, IntPtr.Zero);
				hasControl = true;
			}
			else if (!hasNotification)
			{
				Debug.WriteLine($"WARNING: Could not add control observer for {KeyPath}, Handler: {Handler?.GetType()}. {ControlHandle} points to a null object");
			}
		}

		static readonly IntPtr selRemoveObserverForKeyPath_Handle = Selector.GetHandle("removeObserver:forKeyPath:");

		public void Remove()
		{
			// we use the handle here to remove as it may have been GC'd but we still need to remove it!
			if (hasNotification)
			{
				NSNotificationCenter.DefaultCenter.RemoveObserver(this);

				hasNotification = false;
			}
			if (hasControl)
			{
				//Console.WriteLine ("{0}: 4. Removing observer! {1}, {2}", ((IRef)this.Handler).WidgetID, Handler.GetType (), Control.GetHashCode ());
				Messaging.void_objc_msgSend_IntPtr_IntPtr(ControlHandle, selRemoveObserverForKeyPath_Handle, Handle, KeyPath.Handle);
				hasControl = false;
			}
		}

		protected override void Dispose(bool disposing)
		{
			// this object has a finalizer so let's unsubscribe here
			Remove();
			base.Dispose(disposing);
		}
	}

	public class ObserverActionEventArgs : EventArgs
	{
		readonly ObserverHelper observer;

		public ObserverActionEventArgs(ObserverHelper observer, NSNotification notification)
		{
			this.observer = observer;
			this.Notification = notification;
		}
		
		public Widget Widget => observer.Widget;

		public object Handler => observer.Handler;
		
		public object Control => observer.Control;

		public NSString KeyPath => observer.KeyPath;

		public NSNotification Notification { get; }
	}

	public interface IMacControl
	{
		WeakReference WeakHandler { get; set; }
	}

	static class MacBase
	{
		public static object GetHandler(IntPtr sender) => GetHandler(Runtime.GetNSObject(sender));

		public static object GetHandler(object control)
		{
			var notification = control as NSNotification;
			if (notification != null)
				control = notification.Object;

			var macControl = control as IMacControl;
			if (macControl == null || macControl.WeakHandler == null)
				return null;
			return macControl.WeakHandler.Target;
		}

	}

	public abstract class MacBase<TControl, TWidget, TCallback> : WidgetHandler<TControl, TWidget, TCallback>
		where TControl : class
		where TWidget : Widget
	{
		/// <summary>
		/// Return true to delay notification center observers, then use <see cref="RegisterDelayedNotifications"/>
		/// when appropriate to register them, and <see cref="RemoveNotificationCenterObservers"/> when removed (usually on OnUnload).
		/// </summary>
		internal virtual bool DelayRegisterNotificationCenter => false;

		protected override void Initialize()
		{
			base.Initialize();

			var control = Control as IMacControl;
			if (control != null)
				control.WeakHandler = new WeakReference(this);
		}


		List<ObserverHelper> observers;

		public static object GetHandler(IntPtr sender) => MacBase.GetHandler(Runtime.GetNSObject(sender));

		public static object GetHandler(object control) => MacBase.GetHandler(control);

		public bool AddMethod(IntPtr selector, Delegate action, string arguments, object control)
		{
			if (control is Type type)
			{
				if (!typeof(IMacControl).IsAssignableFrom(type))
					throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "Control '{0}' does not inherit from IMacControl", type));
			}
			else
			{
				type = control.GetType();
				
				if (!typeof(IMacControl).IsAssignableFrom(type))
					throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "Control '{0}' does not inherit from IMacControl", type));
				if (((IMacControl)control).WeakHandler?.Target == null)
					throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "Control '{0}' has a null handler", type));
			}

				
			var classHandle = Class.GetHandle(type);

			return ObjCExtensions.AddMethod(classHandle, selector, action, arguments);
		}

		public bool HasMethod(IntPtr selector, object control)
		{
			var type = control.GetType();
			var classHandle = Class.GetHandle(type);
			return ObjCExtensions.GetInstanceMethod(classHandle, selector) != IntPtr.Zero;
		}

		public NSObject AddObserver(NSString key, Action<ObserverActionEventArgs> action, NSObject control)
		{
			if (observers == null)
			{
				observers = new List<ObserverHelper>();
			}
			var observer = new ObserverHelper
			{
				Type = ObserverType.NotificationCenter,
				Action = action,
				KeyPath = key,
				ControlHandle = control.Handle,
				Handler = this
			};
			if (!DelayRegisterNotificationCenter)
				observer.Register();
			observers.Add(observer);
			return observer;
		}

		public void AddControlObserver(NSString key, Action<ObserverActionEventArgs> action, NSObject control)
		{
			if (observers == null)
			{
				observers = new List<ObserverHelper>();
			}
			var observer = new ObserverHelper
			{
				Type = ObserverType.Control,
				Action = action,
				KeyPath = key,
				ControlHandle = control.Handle,
				Handler = this
			};
			observer.Register();
			observers.Add(observer);
		}

		protected override void Dispose(bool disposing)
		{
			RemoveAllObservers();

			base.Dispose(disposing);
		}
		
		internal void RegisterDelayedNotifications()
		{
			if (observers != null)
			{
				for (int i = 0; i < observers.Count; i++)
				{
					var observer = observers[i];
					// this will only register if not already
					observer.Register();
				}
			}
		}
		
		internal void RemoveNotificationCenterObservers()
		{
			if (observers != null)
			{
				for (int i = 0; i < observers.Count; i++)
				{
					var observer = observers[i];
					if (observer.Type == ObserverType.NotificationCenter)
						observer.Remove();
				}
			}
		}

		internal void RemoveAllObservers()
		{
			if (observers != null)
			{
				for (int i = 0; i < observers.Count; i++)
				{
					var observer = observers[i];
					observer.Remove();
				}
				observers = null;
			}
		}
	}
}

