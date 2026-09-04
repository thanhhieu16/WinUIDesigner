using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.Designer.Services
{
	/// <summary>A tool that creates a component - either a plain control (click-drag or
	/// toolbox-drop) or, if the hit container has a matching IDrawItemExtension (Line/Polyline/
	/// Path containers), delegates to it instead. Ported from the reference's
	/// Services/CreateComponentTool.cs; ClickOrDragMouseGesture/MouseGestureBase are not ported
	/// (spec decision 8) - CreateComponentMouseGesture below is a small self-contained state
	/// machine against the new IDesignPanel events instead.</summary>
	public class CreateComponentTool : ITool
	{
		readonly Type _componentType;
		readonly object[] _arguments;
		CreateComponentMouseGesture _gesture;
		DesignItem _dropItem;
		ChangeGroup _dropChangeGroup;

		// Set true the moment ANY gesture starts (plain-control drag, or an IDrawItemExtension's
		// own multi-click gesture - DrawPolylineMouseGesture/DrawPathMouseGesture, Tasks 10/13,
		// need MULTIPLE subsequent PointerPressed clicks of their own while this tool is still
		// the active one; without this guard, THIS class's own still-subscribed OnPointerPressed
		// would re-fire on every one of those clicks too and start a second, third, fourth...
		// draw gesture on top of the first. Never reset back to false deliberately - Deactivate
		// (called via the tool-switch-back-to-PointerTool every gesture class already performs
		// on completion) unsubscribes this handler for good, and a CreateComponentTool instance
		// is single-use by design (a fresh one is constructed per toolbox interaction, matching
		// the reference's own ToolboxView.PrepareTool).
		bool _drawing;

		public CreateComponentTool(Type componentType) : this(componentType, null) { }

		public CreateComponentTool(Type componentType, object[] arguments)
		{
			_componentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
			_arguments = arguments;
		}

		public Type ComponentType => _componentType;
		public InputCursor Cursor => InputSystemCursor.Create(InputSystemCursorShape.Cross);

		/// <summary>Test-only seam: exposes the gesture started by the last HandlePointerPressed
		/// call (null before any click, or once the gesture has finished/reset the tool). Needed
		/// because a real click-without-drag only actually adds the item on PointerReleased -
		/// CreateComponentMouseGesture subscribes to IDesignPanel.PointerReleased, a real routed
		/// event a test cannot raise (PointerRoutedEventArgs has no public constructor - Global
		/// Constraints), so a test drives the gesture's own internal HandlePointerReleased/
		/// HandlePointerMoved directly through this property instead.</summary>
		internal CreateComponentMouseGesture Gesture => _gesture;

		public void Activate(IDesignPanel designPanel)
		{
			designPanel.PointerPressed += OnPointerPressed;
			designPanel.DragOver += OnDragOver;
			designPanel.Drop += OnDrop;
			designPanel.DragLeave += OnDragLeave;
		}

		public void Deactivate(IDesignPanel designPanel)
		{
			designPanel.PointerPressed -= OnPointerPressed;
			designPanel.DragOver -= OnDragOver;
			designPanel.Drop -= OnDrop;
			designPanel.DragLeave -= OnDragLeave;
		}

		void OnPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
			var designPanel = (DesignPanel)sender;
			var point = e.GetCurrentPoint(designPanel).Position;
			e.Handled = true;
			HandlePointerPressed(designPanel, point, e.KeyModifiers, e.Pointer);
		}

		/// <summary>The real click-handling logic, extracted so a test can drive it directly -
		/// same Handle*-extraction pattern as DesignPanel.HandlePointerPressed. `pointer` is
		/// optional: production always supplies it (for CapturePointer); a test omits it and
		/// exercises everything except the actual capture call (spec decision 9 / "Known gaps"
		/// #6 - PointerRoutedEventArgs.Pointer can't be fabricated in a test).</summary>
		internal void HandlePointerPressed(IDesignPanel designPanel, Point point, VirtualKeyModifiers modifiers, Pointer pointer = null)
		{
			if (_drawing) return; // see _drawing's own doc comment - a multi-click draw gesture is already running

			var result = designPanel.HitTest(point, false, true, HitTestType.Default);
			if (result.ModelHit == null) return;

			// Capture + focus happen exactly once, here, before branching into either
			// IDrawItemExtension.StartDrawItem or CreateComponentMouseGesture - spec decision 2.
			if (pointer != null) designPanel.CapturePointer(pointer);
			designPanel.Focus(FocusState.Pointer);

			var drawItemBehavior = result.ModelHit.Extensions.OfType<IDrawItemExtension>().FirstOrDefault(x => x.CanItemBeDrawn(_componentType));
			if (drawItemBehavior != null) {
				_drawing = true;
				drawItemBehavior.StartDrawItem(result.ModelHit, _componentType, designPanel, point, modifiers, null);
				return;
			}

			var context = result.ModelHit.Context;
			var changeGroup = context.RootItem.OpenGroup("Add Control");
			var createdItem = CreateItem(context, _componentType, _arguments);
			createdItem.Position = point;

			_drawing = true;
			_gesture = new CreateComponentMouseGesture(result.ModelHit, createdItem, changeGroup);
			_gesture.Start(designPanel, point);
		}

		void OnDragOver(object sender, DragEventArgs e)
		{
			var designPanel = (DesignPanel)sender;
			HandleDragOver(designPanel, e.GetPosition(designPanel), e);
		}

		/// <summary>Toolbox-drop path (spec's "Known gaps" #3: no real toolbox exists yet to
		/// exercise this end-to-end - built and unit-tested via this extracted method, ready for
		/// Phase 5's real toolbox). Deliberately simpler than the reference's live-follow-cursor
		/// MoveLogic-driven version: creates and inserts the item once, on the first DragOver
		/// that hits a valid container, at that point; later DragOver calls while still over the
		/// canvas just keep accepting the Copy operation so cursor feedback stays correct. This
		/// scope narrowing is because MoveLogic (Controls/MoveLogic.cs) operates in window
		/// coordinates while DragEventArgs.GetPosition naturally gives panel-relative ones, and
		/// reconciling the two exactly needs more empirical verification than this
		/// hard-to-exercise, low-priority path (Known gap #3) justifies right now.</summary>
		internal void HandleDragOver(IDesignPanel designPanel, Point point, DragEventArgs e)
		{
			e.AcceptedOperation = DataPackageOperation.Copy;
			e.Handled = true;
			if (_dropItem != null) return;

			var result = designPanel.HitTest(point, false, true, HitTestType.Default);
			if (result.ModelHit == null) return;

			var context = result.ModelHit.Context;
			var changeGroup = context.RootItem.OpenGroup("Add Control");
			var createdItem = CreateItem(context, _componentType, _arguments);
			createdItem.Position = point;

			if (AddItemsWithDefaultSize(result.ModelHit, new[] { createdItem })) {
				_dropItem = createdItem;
				_dropChangeGroup = changeGroup;
			} else {
				changeGroup.Abort();
			}
		}

		void OnDrop(object sender, DragEventArgs e)
		{
			HandleDrop((DesignPanel)sender);
		}

		internal void HandleDrop(IDesignPanel designPanel)
		{
			if (_dropItem == null) return;
			_dropChangeGroup?.Commit();
			_dropChangeGroup = null;
			var toolService = _dropItem.Context.Services.GetService<IToolService>();
			_dropItem = null;
			if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
		}

		void OnDragLeave(object sender, DragEventArgs e)
		{
			HandleDragLeave();
		}

		internal void HandleDragLeave()
		{
			if (_dropItem == null) return;
			_dropChangeGroup?.Abort();
			_dropChangeGroup = null;
			_dropItem = null;
		}

		internal static DesignItem CreateItem(DesignContext context, Type type, object[] arguments)
		{
			var newInstance = context.Services.ExtensionManager.CreateInstanceWithCustomInstanceFactory(type, arguments);
			var item = context.Services.Component.RegisterComponentForDesigner(newInstance);
			// Reference also calls context.Services.Component.SetDefaultPropertyValues(item) here
			// - IComponentService in this port has no such method (confirmed via grep across
			// src/), so it's dropped. ApplyDefaultInitializers still runs (ExtensionManager.cs:283
			// - exists, previously had zero callers anywhere in this port).
			context.Services.ExtensionManager.ApplyDefaultInitializers(item);
			return item;
		}

		internal static bool AddItemsWithDefaultSize(DesignItem container, DesignItem[] createdItems)
		{
			var positions = createdItems.Select(x => new Rect(x.Position, ModelTools.GetDefaultSize(x))).ToList();
			return AddItemsWithCustomSize(container, createdItems, positions);
		}

		internal static bool AddItemsWithCustomSize(DesignItem container, DesignItem[] createdItems, IList<Rect> positions)
		{
			// Reference retries on each ancestor container via container.View.TranslatePoint(...)
			// when refused - UIElement.TranslatePoint doesn't exist in WinUI (root CLAUDE.md's
			// known gaps), so this port tries only the hit container, matching the plain
			// null-return contract every other TryStartInsertNewComponents caller in this
			// codebase already uses (e.g. ModelTools.cs).
			var operation = PlacementOperation.TryStartInsertNewComponents(container, createdItems, positions, PlacementType.AddItem);
			if (operation == null) return false;
			container.Context.Services.Selection.SetSelectedComponents(createdItems);
			operation.Commit();
			return true;
		}
	}

	/// <summary>Click-vs-drag state machine for CreateComponentTool's no-IDrawItemExtension path
	/// - a click without drag adds a default-sized item at the click point; a click-and-drag adds
	/// an item sized to the drag rect. Not a DragListener (that's built for "drag one specific
	/// element already on screen" - this is "any click anywhere on the canvas, hit-tested fresh
	/// each time", spec decision 8). Escape/PointerCaptureLost both abort identically - spec
	/// decision 1.</summary>
	sealed class CreateComponentMouseGesture
	{
		readonly DesignItem _container;
		readonly DesignItem _createdItem;
		ChangeGroup _changeGroup;
		IDesignPanel _designPanel;
		Point _startPoint;
		bool _dragStarted;
		PlacementOperation _operation;

		public CreateComponentMouseGesture(DesignItem container, DesignItem createdItem, ChangeGroup changeGroup)
		{
			_container = container;
			_createdItem = createdItem;
			_changeGroup = changeGroup;
		}

		public void Start(IDesignPanel designPanel, Point startPoint)
		{
			_designPanel = designPanel;
			_startPoint = startPoint;
			designPanel.PointerMoved += OnPointerMoved;
			designPanel.PointerReleased += OnPointerReleased;
			designPanel.PointerCaptureLost += OnPointerCaptureLost;
			designPanel.KeyDown += OnKeyDown;
		}

		void OnPointerMoved(object sender, PointerRoutedEventArgs e) => HandlePointerMoved(e.GetCurrentPoint((UIElement)sender).Position);

		internal void HandlePointerMoved(Point point)
		{
			if (!_dragStarted) {
				if (Math.Abs(point.X - _startPoint.X) < Controls.DragListener.MinimumHorizontalDragDistance
					&& Math.Abs(point.Y - _startPoint.Y) < Controls.DragListener.MinimumVerticalDragDistance) return;
				_dragStarted = true;
				_operation = PlacementOperation.TryStartInsertNewComponents(_container, new[] { _createdItem }, new[] { GetStartToEndRect(point).Round() }, PlacementType.Resize);
				if (_operation != null) _container.Context.Services.Selection.SetSelectedComponents(new[] { _createdItem });
				return;
			}
			if (_operation == null) return;
			var rect = GetStartToEndRect(point).Round();
			foreach (var info in _operation.PlacedItems) {
				info.Bounds = rect;
				_operation.CurrentContainerBehavior.SetPosition(info);
			}
		}

		Rect GetStartToEndRect(Point endPoint) => new Rect(
			Math.Min(_startPoint.X, endPoint.X),
			Math.Min(_startPoint.Y, endPoint.Y),
			Math.Abs(_startPoint.X - endPoint.X),
			Math.Abs(_startPoint.Y - endPoint.Y));

		void OnPointerReleased(object sender, PointerRoutedEventArgs e) => HandlePointerReleased();

		internal void HandlePointerReleased()
		{
			if (_dragStarted) {
				_operation?.Commit();
				_operation = null;
			} else {
				CreateComponentTool.AddItemsWithDefaultSize(_container, new[] { _createdItem });
			}
			_changeGroup?.Commit();
			_changeGroup = null;
			Stop();
		}

		void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => Cancel();

		void OnKeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
		}

		internal void Cancel()
		{
			_operation?.Abort();
			_operation = null;
			_changeGroup?.Abort();
			_changeGroup = null;
			Stop();
		}

		void Stop()
		{
			_designPanel.PointerMoved -= OnPointerMoved;
			_designPanel.PointerReleased -= OnPointerReleased;
			_designPanel.PointerCaptureLost -= OnPointerCaptureLost;
			_designPanel.KeyDown -= OnKeyDown;
			_designPanel.ReleasePointerCaptures();
			var toolService = _container.Context.Services.GetService<IToolService>();
			if (toolService.CurrentTool is CreateComponentTool) toolService.CurrentTool = toolService.PointerTool;
		}
	}
}
