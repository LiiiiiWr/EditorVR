﻿using System;
using UnityEngine.VR.Modules;
using UnityEngine.VR.Utilities;

namespace UnityEngine.VR.Handles
{
	/// <summary>
	/// Base class for providing draggable handles in 3D (requires PhysicsRaycaster)
	/// </summary>
	public class BaseHandle : MonoBehaviour, IRayBeginDragHandler, IRayEndDragHandler, IRayEnterHandler, IRayExitHandler
	{
		public delegate void DragEventCallback(BaseHandle handle, HandleDragEventData eventData = default(HandleDragEventData));

		public event DragEventCallback onHandleBeginDrag;
		public event DragEventCallback onHandleDrag;
		public event DragEventCallback onHandleEndDrag;

		public event DragEventCallback onDoubleClick;

		public event Action<BaseHandle> onHoverEnter;
		public event Action<BaseHandle> onHoverExit;

		protected bool m_Hovering;
		protected bool m_Dragging;

		protected DateTime m_LastClickTime;

		public Vector3 startDragPosition { get; protected set; }

		public virtual void OnBeginDrag(RayEventData eventData)
		{
			m_Dragging = true;
			startDragPosition = eventData.pointerCurrentRaycast.worldPosition;

			//Double-click logic
			var timeSinceLastClick = (float)(DateTime.Now - m_LastClickTime).TotalSeconds;
			m_LastClickTime = DateTime.Now;
			if (U.Input.DoubleClick(timeSinceLastClick))
			{
				OnDoubleClick();
			}
		}

		public virtual void OnEndDrag(RayEventData eventData)
		{
			m_Dragging = false;
		}

		public virtual void OnRayEnter(RayEventData eventData)
		{
			m_Hovering = true;
			if (onHoverEnter != null)
				onHoverEnter(this);
		}

		public virtual void OnRayExit(RayEventData eventData)
		{
			m_Hovering = false;
			if (onHoverExit != null)
				onHoverExit(this);
		}

		protected virtual void OnHandleBeginDrag(HandleDragEventData eventData = default(HandleDragEventData))
		{
			if (onHandleBeginDrag != null)
				onHandleBeginDrag(this, eventData);
		}

		protected virtual void OnHandleDrag(HandleDragEventData eventData)
		{
			if (onHandleDrag != null)
				onHandleDrag(this, eventData);
		}

		protected virtual void OnHandleEndDrag(HandleDragEventData eventData = default(HandleDragEventData))
		{
			if (onHandleEndDrag != null)
				onHandleEndDrag(this, eventData);
		}

		protected virtual void OnDoubleClick(HandleDragEventData eventData = default(HandleDragEventData))
		{
			if (onDoubleClick != null)
				onDoubleClick(this, eventData);
		}
	}
}