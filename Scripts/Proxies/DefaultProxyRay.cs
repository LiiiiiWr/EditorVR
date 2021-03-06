﻿#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEditor.Experimental.EditorVR.Extensions;
using UnityEditor.Experimental.EditorVR.Utilities;

namespace UnityEditor.Experimental.EditorVR.Proxies
{
	sealed class DefaultProxyRay : MonoBehaviour, IUsesViewerScale
	{
		[SerializeField]
		private VRLineRenderer m_LineRenderer;

		[SerializeField]
		private GameObject m_Tip;

		[SerializeField]
		private float m_LineWidth;

		[SerializeField]
		private MeshFilter m_Cone;

		private Vector3 m_TipStartScale;
		Transform m_ConeTransform;
		Vector3 m_OriginalConeLocalScale;
		Coroutine m_RayVisibilityCoroutine;
		Coroutine m_ConeVisibilityCoroutine;
		Material m_RayMaterial;

		/// <summary>
		/// The object that is set when LockRay is called while the ray is unlocked.
		/// As long as this reference is set, and the ray is locked, only that object can unlock the ray.
		/// If the object reference becomes null, the ray will be free to show/hide/lock/unlock until another locking entity takes ownership.
		/// </summary>
		private object m_LockRayObject;

		public bool LockRay(object lockCaller)

		{
			// Allow the caller to lock the ray
			// If the reference to the lockCaller is destroyed, and the ray was not properly
			// unlocked by the original locking caller, then allow locking by another object
			if (m_LockRayObject == null)
			{
				m_LockRayObject = lockCaller;
				return true;
			}

			return false;
		}

		public bool UnlockRay(object unlockCaller)
		{
			// Only allow unlocking if the original lock caller is null or there is no locker caller set
			if (m_LockRayObject == unlockCaller)
			{
				m_LockRayObject = null;
				return true;
			}

			return false;
		}

		/// <summary>
		/// The length of the direct selection pointer
		/// </summary>
		public float pointerLength
		{
			get { return (m_Cone.transform.TransformPoint(m_Cone.sharedMesh.bounds.size.z * Vector3.forward) - m_Cone.transform.position).magnitude; }
		}

		public bool rayVisible { get; private set; }
		public bool coneVisible { get; private set; }

		void OnDisable()
		{
			this.StopCoroutine(ref m_RayVisibilityCoroutine);
			this.StopCoroutine(ref m_ConeVisibilityCoroutine);
		}

		public void Hide(bool rayOnly = false)
		{
			if (isActiveAndEnabled && m_LockRayObject == null)
			{
				if (rayVisible)
				{
					rayVisible = false;
					this.StopCoroutine(ref m_RayVisibilityCoroutine);
					m_RayVisibilityCoroutine = StartCoroutine(HideRay());
				}

				if (!rayOnly && coneVisible)
				{
					coneVisible = false;
					this.StopCoroutine(ref m_ConeVisibilityCoroutine);
					m_ConeVisibilityCoroutine = StartCoroutine(HideCone());
				}
			}
		}

		public void Show(bool rayOnly = false)
		{
			if (isActiveAndEnabled && m_LockRayObject == null)
			{
				if (!rayVisible)
				{
					rayVisible = true;
					this.StopCoroutine(ref m_RayVisibilityCoroutine);
					m_RayVisibilityCoroutine = StartCoroutine(ShowRay());
				}

				if (!rayOnly && !coneVisible)
				{
					coneVisible = true;
					this.StopCoroutine(ref m_ConeVisibilityCoroutine);
					m_ConeVisibilityCoroutine = StartCoroutine(ShowCone());
				}
			}
		}

		public void SetLength(float length)
		{
			if (!rayVisible)
				return;

			var viewerScale = this.GetViewerScale();
			var scaledWidth = m_LineWidth * viewerScale;
			var scaledLength = length / viewerScale;

			var lineRendererTransform = m_LineRenderer.transform;
			lineRendererTransform.localScale = Vector3.one * scaledLength;
			m_LineRenderer.SetWidth(scaledWidth, scaledWidth * scaledLength);
			m_Tip.transform.position = transform.position + transform.forward * length;
			m_Tip.transform.localScale = scaledLength * m_TipStartScale;
		}

		public void SetColor(Color c)
		{
			m_RayMaterial.color = c;
		}

		private void Awake()
		{
			m_RayMaterial = MaterialUtils.GetMaterialClone(m_LineRenderer.GetComponent<MeshRenderer>());
			m_ConeTransform = m_Cone.transform;
			m_OriginalConeLocalScale = m_ConeTransform.localScale;
		}

		private void Start()
		{
			m_TipStartScale = m_Tip.transform.localScale;
			rayVisible = true;
		}

		private IEnumerator HideRay()
		{
			m_Tip.transform.localScale = Vector3.zero;

			// cache current width for smooth animation to target value without snapping
			var currentWidth = m_LineRenderer.widthStart;
			const float kTargetWidth = 0f;
			const float kSmoothTime = 0.1875f;
			var smoothVelocity = 0f;
			var currentDuration = 0f;
			while (currentDuration < kSmoothTime)
			{
				currentDuration += Time.deltaTime;
				currentWidth = MathUtilsExt.SmoothDamp(currentWidth, kTargetWidth, ref smoothVelocity, kSmoothTime, Mathf.Infinity, Time.deltaTime);
				m_LineRenderer.SetWidth(currentWidth, currentWidth);
				yield return null;
			}

			m_LineRenderer.SetWidth(kTargetWidth, kTargetWidth);
			m_RayVisibilityCoroutine = null;
		}

		private IEnumerator ShowRay()
		{
			m_Tip.transform.localScale = m_TipStartScale;

			float viewerScale = this.GetViewerScale();
			float scaledWidth;
			var currentWidth = m_LineRenderer.widthStart / viewerScale;
			var smoothVelocity = 0f;
			const float kSmoothTime = 0.3125f;
			var currentDuration = 0f;
			while (currentDuration < kSmoothTime)
			{
				viewerScale = this.GetViewerScale();
				currentDuration += Time.deltaTime;
				currentWidth = MathUtilsExt.SmoothDamp(currentWidth, m_LineWidth, ref smoothVelocity, kSmoothTime, Mathf.Infinity, Time.deltaTime);
				scaledWidth = currentWidth * viewerScale;
				m_LineRenderer.SetWidth(scaledWidth, scaledWidth);
				yield return null;
			}

			viewerScale = this.GetViewerScale();
			scaledWidth = m_LineWidth * viewerScale;
			m_LineRenderer.SetWidth(scaledWidth, scaledWidth);
			m_RayVisibilityCoroutine = null;
		}

		IEnumerator HideCone()
		{
			var currentScale = m_ConeTransform.localScale;
			var smoothVelocity = Vector3.one;
			const float kSmoothTime = 0.1875f;
			var currentDuration = 0f;
			while (currentDuration < kSmoothTime)
			{
				currentDuration += Time.deltaTime;
				currentScale = MathUtilsExt.SmoothDamp(currentScale, Vector3.zero, ref smoothVelocity, kSmoothTime, Mathf.Infinity, Time.deltaTime);
				m_ConeTransform.localScale = currentScale;
				yield return null;
			}

			m_ConeTransform.localScale = Vector3.zero;
			m_ConeVisibilityCoroutine = null;
		}

		IEnumerator ShowCone()
		{
			var currentScale = m_ConeTransform.localScale;
			var smoothVelocity = Vector3.zero;
			const float kSmoothTime = 0.3125f;
			var currentDuration = 0f;
			while (currentDuration < kSmoothTime)
			{
				currentDuration += Time.deltaTime;
				currentScale = MathUtilsExt.SmoothDamp(currentScale, m_OriginalConeLocalScale, ref smoothVelocity, kSmoothTime, Mathf.Infinity, Time.deltaTime);
				m_ConeTransform.localScale = currentScale;
				yield return null;
			}

			m_ConeTransform.localScale = m_OriginalConeLocalScale;
			m_ConeVisibilityCoroutine = null;
		}

		void OnDestroy()
		{
			ObjectUtils.Destroy(m_RayMaterial);
		}
	}
}
#endif
