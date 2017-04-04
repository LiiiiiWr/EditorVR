﻿#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.EditorVR.Extensions;
using UnityEditor.Experimental.EditorVR.Handles;
using UnityEditor.Experimental.EditorVR.Helpers;
using UnityEditor.Experimental.EditorVR.Utilities;
using UnityEngine;
using UnityEngine.InputNew;
using UnityEngine.UI;

namespace UnityEditor.Experimental.EditorVR.Workspaces
{
	sealed class WorkspaceUI : MonoBehaviour, IUsesStencilRef, IUsesViewerScale
	{
		const int k_AngledFaceBlendShapeIndex = 2;
		const int k_ThinFrameBlendShapeIndex = 3;
		const string k_MaterialStencilRef = "_StencilRef";

		const float k_ResizeIconCrossfadeDuration = 0.2f;
		const float k_ResizeIconSmoothFollow = 10f;

		const float k_HandleZOffset = 0.1f;

		static readonly Vector3 k_BaseFrontPanelRotation = Vector3.zero;
		static readonly Vector3 k_MaxFrontPanelRotation = new Vector3(90f, 0f, 0f);

		[SerializeField]
		Transform m_SceneContainer;

		[SerializeField]
		RectTransform m_FrontPanel;

		[SerializeField]
		BaseHandle[] m_Handles;

		[SerializeField]
		Image[] m_ResizeIcons;

		[SerializeField]
		Transform m_LeftHandle;

		[SerializeField]
		Transform m_RightHandle;

		[SerializeField]
		Transform m_BackHandle;

		[SerializeField]
		Transform m_FrontTopHandle;

		[SerializeField]
		Transform m_FrontLeftHandle;

		[SerializeField]
		Transform m_FrontLeftCornerHandle;

		[SerializeField]
		Transform m_FrontRightHandle;

		[SerializeField]
		Transform m_FrontRightCornerHandle;

		[SerializeField]
		Transform m_FrontBottomHandle;

		[SerializeField]
		Transform m_TopFaceContainer;

		[SerializeField]
		WorkspaceHighlight m_TopHighlight;

		[SerializeField]
		SkinnedMeshRenderer m_Frame;

		[SerializeField]
		Transform m_FrameFrontFaceTransform;

		[SerializeField]
		Transform m_FrameFrontFaceHighlightTransform;

		[SerializeField]
		Transform m_TopPanelDividerTransform;

		[SerializeField]
		RectTransform m_UIContentContainer;

		[SerializeField]
		Image m_FrontResizeIcon;

		[SerializeField]
		Image m_RightResizeIcon;

		[SerializeField]
		Image m_LeftResizeIcon;

		[SerializeField]
		Image m_BackResizeIcon;

		[SerializeField]
		Image m_FrontLeftResizeIcon;

		[SerializeField]
		Image m_FrontRightResizeIcon;

		[SerializeField]
		Image m_BackLeftResizeIcon;

		[SerializeField]
		Image m_BackRightResizeIcon;

		[SerializeField]
		Transform m_TopHighlightContainer;

		[SerializeField]
		WorkspaceHighlight m_FrontHighlight;

		[SerializeField]
		float m_FrameHandleSize = 0.01f;

		[SerializeField]
		float m_FrameHeight = 0.09275f;

		[SerializeField]
		float m_ResizeHandleMargin = 0.01f;

		[SerializeField]
		float m_ResizeCornerSize = 0.05f;

		[SerializeField]
		bool m_DynamicFaceAdjustment = true;

		Bounds m_Bounds;
		float? m_TopPanelDividerOffset;

		// Cached for optimization
		float m_PreviousXRotation;
		Coroutine m_FrameThicknessCoroutine;
		Coroutine m_TopFaceVisibleCoroutine;
		Material m_TopFaceMaterial;
		Material m_FrontFaceMaterial;

		float m_LerpAmount;
		float m_FrontZOffset;

		DragState m_DragState;

		[Flags]
		enum ResizeDirection
		{
			Front = 1,
			Back = 2,
			Left = 4,
			Right = 8
		}

		class DragState
		{
			public Transform rayOrigin { get; private set; }
			public bool resizing { get; private set; }
			Vector3 m_PositionOffset;
			Quaternion m_RotationOffset;
			WorkspaceUI m_WorkspaceUI;
			Vector3 m_DragStart;
			Vector3 m_PositionStart;
			Vector3 m_BoundsSizeStart;
			ResizeDirection m_Direction;

			public DragState(WorkspaceUI workspaceUI, Transform rayOrigin, bool resizing)
			{
				m_WorkspaceUI = workspaceUI;
				this.resizing = resizing;
				this.rayOrigin = rayOrigin;

				if (resizing)
				{
					var pointerPosition = m_WorkspaceUI.GetPointerPositionForRayOrigin(rayOrigin);
					m_DragStart = pointerPosition;
					m_PositionStart = workspaceUI.transform.parent.position;
					m_BoundsSizeStart = workspaceUI.bounds.size;
					var localPosition = m_WorkspaceUI.transform.InverseTransformPoint(pointerPosition);
					m_Direction = m_WorkspaceUI.GetResizeDirectionForLocalPosition(localPosition);
				}
				else
				{
					MathUtilsExt.GetTransformOffset(rayOrigin, m_WorkspaceUI.transform.parent, out m_PositionOffset, out m_RotationOffset);
				}
			}

			public void OnDragging()
			{
				if (resizing)
				{
					var viewerScale = m_WorkspaceUI.GetViewerScale();
					var pointerPosition = m_WorkspaceUI.GetPointerPositionForRayOrigin(rayOrigin);
					var dragVector = (pointerPosition - m_DragStart) / viewerScale;
					var bounds = m_WorkspaceUI.bounds;
					var transform = m_WorkspaceUI.transform;

					var positionOffsetForward = Vector3.Dot(dragVector, transform.forward) * 0.5f;
					var positionOffsetRight = Vector3.Dot(dragVector, transform.right) * 0.5f;

					switch (m_Direction)
					{
						default:
							bounds.size = m_BoundsSizeStart + Vector3.back * Vector3.Dot(dragVector, transform.forward);
							positionOffsetRight = 0;
							break;
						case ResizeDirection.Back:
							bounds.size = m_BoundsSizeStart + Vector3.forward * Vector3.Dot(dragVector, transform.forward);
							positionOffsetRight = 0;
							break;
						case ResizeDirection.Left:
							bounds.size = m_BoundsSizeStart + Vector3.left * Vector3.Dot(dragVector, transform.right);
							positionOffsetForward = 0;
							break;
						case ResizeDirection.Right:
							bounds.size = m_BoundsSizeStart + Vector3.right * Vector3.Dot(dragVector, transform.right);
							positionOffsetForward = 0;
							break;
						case ResizeDirection.Front | ResizeDirection.Left:
							bounds.size = m_BoundsSizeStart + Vector3.left * Vector3.Dot(dragVector, transform.right)
								+ Vector3.back * Vector3.Dot(dragVector, transform.forward);
							break;
						case ResizeDirection.Front | ResizeDirection.Right:
							bounds.size = m_BoundsSizeStart + Vector3.right * Vector3.Dot(dragVector, transform.right)
								+ Vector3.back * Vector3.Dot(dragVector, transform.forward);
							break;
						case ResizeDirection.Back | ResizeDirection.Left:
							bounds.size = m_BoundsSizeStart + Vector3.left * Vector3.Dot(dragVector, transform.right)
								+ Vector3.forward * Vector3.Dot(dragVector, transform.forward);
							break;
						case ResizeDirection.Back | ResizeDirection.Right:
							bounds.size = m_BoundsSizeStart + Vector3.right * Vector3.Dot(dragVector, transform.right)
								+ Vector3.forward * Vector3.Dot(dragVector, transform.forward);
							break;
					}

					if (m_WorkspaceUI.resize != null)
						m_WorkspaceUI.resize(bounds);

					var currentExtents = m_WorkspaceUI.bounds.extents;
					var extents = bounds.extents;
					var absRight = Mathf.Abs(positionOffsetRight);
					var absForward = Mathf.Abs(positionOffsetForward);
					var positionOffset = transform.right * (absRight - (currentExtents.x - extents.x)) * Mathf.Sign(positionOffsetRight)
						+ transform.forward * (absForward - (currentExtents.z - extents.z)) * Mathf.Sign(positionOffsetForward);

					m_WorkspaceUI.transform.parent.position = m_PositionStart + positionOffset * viewerScale;
				}
				else
				{
					MathUtilsExt.SetTransformOffset(rayOrigin, m_WorkspaceUI.transform.parent, m_PositionOffset, m_RotationOffset);
				}
			}
		}

		readonly List<Transform> m_HovereringRayOrigins = new List<Transform>();
		readonly Dictionary<Transform, Image> m_LastResizeIcons = new Dictionary<Transform, Image>();

		public event Action closeClicked;
		public event Action resetSizeClicked;

		public bool highlightsVisible
		{
			set
			{
				if (m_TopHighlight.visible == value && m_FrontHighlight.visible == value)
					return;

				m_TopHighlight.visible = value;
				m_FrontHighlight.visible = value;

				if (value)
					IncreaseFrameThickness();
				else
					ResetFrameThickness();
			}
		}

		public bool frontHighlightVisible
		{
			set
			{
				if (m_FrontHighlight.visible == value)
					return;

				m_FrontHighlight.visible = value;

				if (value)
					IncreaseFrameThickness();
				else
					ResetFrameThickness();
			}
		}

		public bool amplifyTopHighlight
		{
			set
			{
				this.StopCoroutine(ref m_TopFaceVisibleCoroutine);
				m_TopFaceVisibleCoroutine = value ? StartCoroutine(HideTopFace()) : StartCoroutine(ShowTopFace());
			}
		}


		/// <summary>
		/// (-1 to 1) ranged value that controls the separator mask's X-offset placement
		/// A value of zero will leave the mask in the center of the workspaceUI
		/// </summary>
		public float topPanelDividerOffset
		{
			set
			{
				m_TopPanelDividerOffset = value;
				m_TopPanelDividerTransform.gameObject.SetActive(true);
			}
		}

		public Transform topFaceContainer { get { return m_TopFaceContainer; } set { m_TopFaceContainer = value; } }
		public Transform sceneContainer { get { return m_SceneContainer; } }
		public BaseHandle[] handles { get { return m_Handles; } }
		public RectTransform frontPanel { get { return m_FrontPanel; } }
		public WorkspaceHighlight topHighlight { get { return m_TopHighlight; } }
		public bool dynamicFaceAdjustment { get { return m_DynamicFaceAdjustment; } set { m_DynamicFaceAdjustment = value; } }

		public bool preventResize { get; set; }

		public byte stencilRef { get; set; }

		public Transform leftRayOrigin { private get; set; }
		public Transform rightRayOrigin { private get; set; }

		public event Action<Bounds> resize;
		public Func<Transform, float> getPointerLength { private get; set; }

		public Bounds bounds
		{
			get { return m_Bounds; }
			set
			{
				m_Bounds = value;

				m_Bounds.center = Vector3.down * m_FrameHeight * 0.5f;

				var extents = m_Bounds.extents;
				var size = m_Bounds.size;
				size.y = m_FrameHeight + m_FrameHandleSize;
				m_Bounds.size = size;

				// Because BlendShapes cap at 100, our workspaceUI maxes out at 100m wide
				const float kWidthMultiplier = 0.9616f;
				const float kDepthMultiplier = 0.99385f;
				const float kWidthOffset = -0.165f;
				const float kDepthOffset = -0.038f;

				var width = size.x;
				var depth = size.z;
				var faceWidth = width - Workspace.FaceMargin;
				var faceDepth = depth - Workspace.FaceMargin;

				m_Frame.SetBlendShapeWeight(0, width * kWidthMultiplier + kWidthOffset);
				m_Frame.SetBlendShapeWeight(1, depth * kDepthMultiplier + kDepthOffset);

				// Resize content container
				m_UIContentContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, faceWidth);
				var localPosition = m_UIContentContainer.localPosition;
				localPosition.z = -extents.z;
				m_UIContentContainer.localPosition = localPosition;

				// Resize front panel
				m_FrameFrontFaceTransform.localScale = new Vector3(faceWidth, 1f, 1f);
				const float kFrontFaceHighlightMargin = 0.0008f;
				m_FrameFrontFaceHighlightTransform.localScale = new Vector3(faceWidth + kFrontFaceHighlightMargin, 1f, 1f);

				// Position the separator mask if enabled
				if (m_TopPanelDividerOffset != null)
				{
					m_TopPanelDividerTransform.localPosition = new Vector3(size.x * 0.5f * m_TopPanelDividerOffset.Value, 0f, 0f);
					m_TopPanelDividerTransform.localScale = new Vector3(1f, 1f, faceDepth + Workspace.HighlightMargin);
				}

				// Scale the Top Face and the Top Face Highlight
				const float kHighlightMargin = 0.0005f;
				m_TopHighlightContainer.localScale = new Vector3(faceWidth + kHighlightMargin, 1f, faceDepth + kHighlightMargin);
				m_TopFaceContainer.localScale = new Vector3(faceWidth, 1f, faceDepth);

				AdjustHandlesAndIcons();
			}
		}

		void Awake()
		{
			foreach (var icon in m_ResizeIcons)
			{
				icon.CrossFadeAlpha(0f, 0f, true);
			}

			m_Frame.SetBlendShapeWeight(k_ThinFrameBlendShapeIndex, 50f); // Set default frame thickness to be in middle for a thinner initial frame

			if (m_TopPanelDividerOffset == null)
				m_TopPanelDividerTransform.gameObject.SetActive(false);

			foreach (var handle in m_Handles)
			{
				handle.hoverStarted += OnHandleHoverStarted;
				handle.hoverEnded += OnHandleHoverEnded;
			}
		}

		IEnumerator Start()
		{
			const string kShaderBlur = "_Blur";
			const string kShaderAlpha = "_Alpha";
			const string kShaderVerticalOffset = "_VerticalOffset";
			const float kTargetDuration = 1.25f;

			m_TopFaceMaterial = MaterialUtils.GetMaterialClone(m_TopFaceContainer.GetComponentInChildren<MeshRenderer>());
			m_TopFaceMaterial.SetFloat("_Alpha", 1f);
			m_TopFaceMaterial.SetInt(k_MaterialStencilRef, stencilRef);

			m_FrontFaceMaterial = MaterialUtils.GetMaterialClone(m_FrameFrontFaceTransform.GetComponentInChildren<MeshRenderer>());
			m_FrontFaceMaterial.SetInt(k_MaterialStencilRef, stencilRef);

			var originalBlurAmount = m_TopFaceMaterial.GetFloat("_Blur");
			var currentBlurAmount = 10f; // also the maximum blur amount
			var currentDuration = 0f;
			var currentVelocity = 0f;

			m_TopFaceMaterial.SetFloat(kShaderBlur, currentBlurAmount);
			m_TopFaceMaterial.SetFloat(kShaderVerticalOffset, 1f); // increase the blur sample offset to amplify the effect
			m_TopFaceMaterial.SetFloat(kShaderAlpha, 0.5f); // set partially transparent

			while (currentDuration < kTargetDuration)
			{
				currentDuration += Time.unscaledDeltaTime;
				currentBlurAmount = MathUtilsExt.SmoothDamp(currentBlurAmount, originalBlurAmount, ref currentVelocity, kTargetDuration, Mathf.Infinity, Time.unscaledDeltaTime);
				m_TopFaceMaterial.SetFloat(kShaderBlur, currentBlurAmount);

				float percentageComplete = currentDuration / kTargetDuration;
				m_TopFaceMaterial.SetFloat(kShaderVerticalOffset, 1 - percentageComplete); // lerp back towards an offset of zero
				m_TopFaceMaterial.SetFloat(kShaderAlpha, percentageComplete * 0.5f + 0.5f); // lerp towards fully opaque from 50% transparent

				yield return null;
			}

			m_TopFaceMaterial.SetFloat(kShaderBlur, originalBlurAmount);
			m_TopFaceMaterial.SetFloat(kShaderVerticalOffset, 0f);
			m_TopFaceMaterial.SetFloat(kShaderAlpha, 1f);

			yield return null;
		}

		void AdjustHandlesAndIcons()
		{
			// Handles
			var extents = m_Bounds.extents;
			var size = m_Bounds.size;
			var halfWidth = extents.x;
			var handleScaleX = size.x - m_FrameHandleSize;
			var handleScaleZ = size.z + m_FrameHandleSize;
			var halfHeight = -m_FrameHeight * 0.5f;
			var halfDepth = extents.z;
			var handleHeight = m_FrameHeight + m_FrameHandleSize;

			var transform = m_LeftHandle.transform;
			transform.localPosition = new Vector3(-halfWidth, halfHeight, 0);
			transform.localScale = new Vector3(m_FrameHandleSize, handleHeight, handleScaleZ);

			transform = m_RightHandle.transform;
			transform.localPosition = new Vector3(halfWidth, halfHeight, 0);
			transform.localScale = new Vector3(m_FrameHandleSize, handleHeight, handleScaleZ);

			transform = m_BackHandle.transform;
			transform.localPosition = new Vector3(0, halfHeight, halfDepth);
			transform.localScale = new Vector3(handleScaleX, handleHeight, m_FrameHandleSize);

			transform = m_FrontTopHandle.transform;
			transform.localPosition = new Vector3(0, 0, -halfDepth);
			transform.localScale = new Vector3(handleScaleX, m_FrameHandleSize, m_FrameHandleSize);

			transform = m_FrontBottomHandle.transform;
			var halfFrameHandleSize = m_FrameHandleSize * 0.5f;
			var botHandleYPosition = (m_FrameHeight) * (m_LerpAmount - 1);
			transform.localPosition = new Vector3(0, botHandleYPosition, -halfDepth - m_FrontZOffset);
			transform.localScale = new Vector3(handleScaleX + m_FrameHandleSize * 2, m_FrameHandleSize, m_FrameHandleSize);

			const float kLerpScale = 1.15f;
			var halfFrontZOffset = m_FrontZOffset * 0.5f;
			transform = m_FrontLeftHandle.transform;
			transform.localPosition = new Vector3(-halfWidth, botHandleYPosition * 0.5f, -halfDepth - halfFrontZOffset);
			transform.localRotation = Quaternion.AngleAxis(Mathf.Clamp01(m_LerpAmount * kLerpScale) * 90f, Vector3.right);
			transform.localScale = new Vector3(m_FrameHandleSize, m_FrameHeight, m_FrameHandleSize);

			var rightTransform = m_FrontRightHandle.transform;
			var localPosition = transform.localPosition;
			localPosition.x = halfWidth;
			rightTransform.localPosition = localPosition;
			rightTransform.localRotation = transform.localRotation;
			rightTransform.localScale = transform.localScale;

			var cornerScale = m_FrameHeight + m_FrameHandleSize;
			transform = m_FrontLeftCornerHandle.transform;
			var zOffset = m_FrontZOffset - k_HandleZOffset * 0.5f;
			transform.localPosition = new Vector3(-halfWidth, -cornerScale * (1f - m_LerpAmount * 0.5f) + m_FrameHandleSize * (1 - m_LerpAmount) * 0.5f, -halfDepth - zOffset - halfFrameHandleSize);
			transform.localScale = new Vector3(m_FrameHandleSize, (cornerScale - m_FrameHandleSize) * m_LerpAmount, cornerScale - m_FrameHandleSize * 0.75f);

			rightTransform = m_FrontRightCornerHandle.transform;
			localPosition = transform.localPosition;
			localPosition.x = halfWidth;
			rightTransform.localPosition = localPosition;
			rightTransform.localRotation = transform.localRotation;
			rightTransform.localScale = transform.localScale;

			// Resize icons
			var resizePositionX = halfWidth + m_ResizeHandleMargin;
			var resizePositionZ = halfDepth + m_ResizeHandleMargin;
			transform = m_FrontResizeIcon.transform;
			localPosition = transform.localPosition;
			localPosition.z = -resizePositionZ - m_FrontZOffset;
			transform.localPosition = localPosition;

			transform = m_RightResizeIcon.transform;
			localPosition = transform.localPosition;
			localPosition.x = resizePositionX;
			transform.localPosition = localPosition;

			transform = m_LeftResizeIcon.transform;
			localPosition = transform.localPosition;
			localPosition.x = -resizePositionX;
			transform.localPosition = localPosition;

			transform = m_BackResizeIcon.transform;
			localPosition = transform.localPosition;
			localPosition.z = resizePositionZ;
			transform.localPosition = localPosition;

			const float cornerMarginScale = 0.7071067811865475f; // 1 / sqrt(2)
			var resizeCornerPositionX = halfWidth + m_ResizeHandleMargin * cornerMarginScale;
			var resizeCornerPositionZ = halfDepth + m_ResizeHandleMargin * cornerMarginScale;
			transform = m_FrontLeftResizeIcon.transform;
			localPosition = transform.localPosition;
			localPosition.x = -resizeCornerPositionX;
			localPosition.z = -resizeCornerPositionZ - m_FrontZOffset;
			transform.localPosition = localPosition;

			transform = m_FrontRightResizeIcon.transform;
			localPosition = transform.localPosition;
			localPosition.x = resizeCornerPositionX;
			localPosition.z = -resizeCornerPositionZ - m_FrontZOffset;
			transform.localPosition = localPosition;

			transform = m_BackLeftResizeIcon.transform;
			localPosition = transform.localPosition;
			localPosition.x = -resizeCornerPositionX;
			localPosition.z = resizeCornerPositionZ;
			transform.localPosition = localPosition;

			transform = m_BackRightResizeIcon.transform;
			localPosition = transform.localPosition;
			localPosition.x = resizeCornerPositionX;
			localPosition.z = resizeCornerPositionZ;
			transform.localPosition = localPosition;
		}

		void OnHandleHoverStarted(BaseHandle handle, HandleEventData eventData)
		{
			if (m_HovereringRayOrigins.Count == 0 && m_DragState == null)
				IncreaseFrameThickness();

			m_HovereringRayOrigins.Add(eventData.rayOrigin);
		}

		ResizeDirection GetResizeDirectionForLocalPosition(Vector3 localPosition)
		{
			var direction = localPosition.z > 0 ? ResizeDirection.Back : ResizeDirection.Front;
			var xDirection = localPosition.x > 0 ? ResizeDirection.Right : ResizeDirection.Left;

			var zDistance = bounds.extents.z - Mathf.Abs(localPosition.z);
			if (localPosition.z < 0)
				zDistance += m_FrontZOffset;
			var cornerZ = zDistance < m_ResizeCornerSize;
			var cornerX = bounds.extents.x - Mathf.Abs(localPosition.x) < m_ResizeCornerSize;

			if (cornerZ && cornerX)
				direction |= xDirection;
			else if (cornerX)
				direction = xDirection;

			return direction;
		}

		Image GetResizeIconForDirection(ResizeDirection direction)
		{
			switch (direction)
			{
				default:
					return m_FrontResizeIcon;
				case ResizeDirection.Back:
					return m_BackResizeIcon;
				case ResizeDirection.Left:
					return m_LeftResizeIcon;
				case ResizeDirection.Right:
					return m_RightResizeIcon;
				case ResizeDirection.Front | ResizeDirection.Left:
					return m_FrontLeftResizeIcon;
				case ResizeDirection.Front | ResizeDirection.Right:
					return m_FrontRightResizeIcon;
				case ResizeDirection.Back | ResizeDirection.Left:
					return m_BackLeftResizeIcon;
				case ResizeDirection.Back | ResizeDirection.Right:
					return m_BackRightResizeIcon;
			}
		}

		void OnHandleHoverEnded(BaseHandle handle, HandleEventData eventData)
		{
			var rayOrigin = eventData.rayOrigin;
			if (m_HovereringRayOrigins.Remove(rayOrigin))
			{
				Image lastResizeIcon;
				if (m_LastResizeIcons.TryGetValue(rayOrigin, out lastResizeIcon))
				{
					lastResizeIcon.CrossFadeAlpha(0f, k_ResizeIconCrossfadeDuration, true);
					m_LastResizeIcons.Remove(rayOrigin);
				}
			}

			if (m_HovereringRayOrigins.Count == 0)
				ResetFrameThickness();
		}

		void Update()
		{
			if (!m_DynamicFaceAdjustment)
				return;

			var currentXRotation = transform.rotation.eulerAngles.x;
			if (Mathf.Approximately(currentXRotation, m_PreviousXRotation))
				return; // Exit if no x rotation change occurred for this frame

			m_PreviousXRotation = currentXRotation;

			// a second additional value added to the y offset of the front panel when it is in mid-reveal,
			// lerped in at the middle of the rotation/reveal, and lerped out at the beginning & end of the rotation/reveal
			const int kRevealCompensationBlendShapeIndex = 5;
			const float kLerpPadding = 1.2f; // pad lerp values increasingly as it increases, displaying the "front face reveal" sooner
			const float kCorrectiveRevealShapeMultiplier = 1.85f;
			var angledAmount = Mathf.Clamp(Mathf.DeltaAngle(currentXRotation, 0f), 0f, 90f);
			var midRevealCorrectiveShapeAmount = Mathf.PingPong(angledAmount * kCorrectiveRevealShapeMultiplier, 90);
			// add lerp padding to reach and maintain the target value sooner
			m_LerpAmount = angledAmount / 90f;
			var paddedLerp = m_LerpAmount * kLerpPadding;

			// offset front panel according to workspaceUI rotation angle
			const float kAdditionalFrontPanelLerpPadding = 1.1f;
			const float kFrontPanelYOffset = 0.03f;
			const float kFrontPanelZStartOffset = 0.0084f;
			const float kFrontPanelZEndOffset = -0.05f;
			m_FrontPanel.localRotation = Quaternion.Euler(Vector3.Lerp(k_BaseFrontPanelRotation, k_MaxFrontPanelRotation, paddedLerp * kAdditionalFrontPanelLerpPadding));
			m_FrontPanel.localPosition = Vector3.Lerp(Vector3.forward * kFrontPanelZStartOffset, new Vector3(0, kFrontPanelYOffset, kFrontPanelZEndOffset), paddedLerp);

			m_FrontZOffset = k_HandleZOffset * Mathf.Clamp01(paddedLerp * kAdditionalFrontPanelLerpPadding);

			AdjustHandlesAndIcons();

			// change blendshapes according to workspaceUI rotation angle
			m_Frame.SetBlendShapeWeight(k_AngledFaceBlendShapeIndex, angledAmount * kLerpPadding);
			m_Frame.SetBlendShapeWeight(kRevealCompensationBlendShapeIndex, midRevealCorrectiveShapeAmount);
		}

		public void ProcessInput(WorkspaceInput input, ConsumeControlDelegate consumeControl)
		{
			var secondaryLeft = input.secondaryLeft;
			var secondaryRight = input.secondaryRight;

			var primaryLeft = input.primaryLeft;
			var primaryRight = input.primaryRight;

			if (m_DragState == null)
			{
				var adjustedBounds = bounds;
				adjustedBounds.size += Vector3.forward * m_FrontZOffset;
				adjustedBounds.center += Vector3.back * m_FrontZOffset * 0.5f;
				Transform dragRayOrigin = null;
				Image dragResizeIcon = null;
				var resizing = false;

				var leftPosition = transform.InverseTransformPoint(GetPointerPositionForRayOrigin(leftRayOrigin));
				if (secondaryLeft.wasJustPressed && adjustedBounds.Contains(leftPosition))
				{
					dragRayOrigin = leftRayOrigin;
					m_LastResizeIcons.TryGetValue(dragRayOrigin, out dragResizeIcon);
					consumeControl(secondaryLeft);
				}

				var rightPosition = transform.InverseTransformPoint(GetPointerPositionForRayOrigin(rightRayOrigin));
				if (secondaryRight.wasJustPressed && adjustedBounds.Contains(rightPosition))
				{
					dragRayOrigin = rightRayOrigin;
					m_LastResizeIcons.TryGetValue(dragRayOrigin, out dragResizeIcon);
					consumeControl(secondaryRight);
				}

				if (!dragRayOrigin)
				{
					for (int i = 0; i < m_HovereringRayOrigins.Count; i++)
					{
						var rayOrigin = m_HovereringRayOrigins[i];
						Image lastResizeIcon;
						m_LastResizeIcons.TryGetValue(rayOrigin, out lastResizeIcon);
						if (rayOrigin == leftRayOrigin && primaryLeft.wasJustPressed && !preventResize)
						{
							consumeControl(primaryLeft);
							dragRayOrigin = rayOrigin;
							dragResizeIcon = lastResizeIcon;
							resizing = true;
						}
						if (rayOrigin == rightRayOrigin && primaryRight.wasJustPressed && !preventResize)
						{
							consumeControl(primaryRight);
							dragRayOrigin = rayOrigin;
							dragResizeIcon = lastResizeIcon;
							resizing = true;
						}

						if (!preventResize)
						{
							const float kVisibleOpacity = 0.75f;
							var localPosition = transform.InverseTransformPoint(GetPointerPositionForRayOrigin(rayOrigin));
							var direction = GetResizeDirectionForLocalPosition(localPosition);
							var resizeIcon = GetResizeIconForDirection(direction);

							if (lastResizeIcon != null)
							{
								if (resizeIcon != lastResizeIcon)
								{
									resizeIcon.CrossFadeAlpha(kVisibleOpacity, k_ResizeIconCrossfadeDuration, true);
									lastResizeIcon.CrossFadeAlpha(0f, k_ResizeIconCrossfadeDuration, true);
								}
							}
							else
							{
								resizeIcon.CrossFadeAlpha(kVisibleOpacity, k_ResizeIconCrossfadeDuration, true);
							}

							m_LastResizeIcons[rayOrigin] = resizeIcon;

							var iconTransform = resizeIcon.transform;
							var iconPosition = iconTransform.localPosition;
							var smoothFollow = lastResizeIcon == null ? 1 : k_ResizeIconSmoothFollow * Time.unscaledDeltaTime;
							var localDirection = localPosition - transform.InverseTransformPoint(rayOrigin.position);
							switch (direction)
							{
								case ResizeDirection.Front:
								case ResizeDirection.Back:
									var iconPositionX = iconPosition.x;
									var positionOffsetX = Mathf.Sign(localDirection.x) * m_ResizeHandleMargin;
									var tergetPositionX = localPosition.x + positionOffsetX;
									if (Mathf.Abs(tergetPositionX) > bounds.extents.x - m_ResizeCornerSize)
										tergetPositionX = localPosition.x - positionOffsetX;

									iconPosition.x = Mathf.Lerp(iconPositionX, tergetPositionX, smoothFollow);
									break;
								case ResizeDirection.Left:
								case ResizeDirection.Right:
									var iconPositionZ = iconPosition.z;
									var positionOffsetZ = Mathf.Sign(localDirection.z) * m_ResizeHandleMargin;
									var tergetPositionZ = localPosition.z + positionOffsetZ;
									if (Mathf.Abs(tergetPositionZ) > bounds.extents.z - m_ResizeCornerSize)
										tergetPositionZ = localPosition.z - positionOffsetZ;

									iconPosition.z = Mathf.Lerp(iconPositionZ, tergetPositionZ, smoothFollow);
									break;
							}
							iconTransform.localPosition = iconPosition;
						}
					}
				}

				if (dragRayOrigin)
				{
					m_DragState = new DragState(this, dragRayOrigin, resizing);
					if (dragResizeIcon != null)
						dragResizeIcon.CrossFadeAlpha(0f, k_ResizeIconCrossfadeDuration, true);

					ResetFrameThickness();

					foreach (var smoothMotion in GetComponentsInChildren<SmoothMotion>())
					{
						smoothMotion.enabled = false;
					}
				}
			}

			if (m_DragState != null)
			{
				var rayOrigin = m_DragState.rayOrigin;
				var resizing = m_DragState.resizing;

				var resizeEnded = resizing
					&& ((rayOrigin == leftRayOrigin && primaryLeft.wasJustReleased)
						|| (rayOrigin == rightRayOrigin && primaryRight.wasJustReleased));

				var moveEnded = !resizing
					&& ((rayOrigin == leftRayOrigin && secondaryLeft.wasJustReleased)
						|| (rayOrigin == rightRayOrigin && secondaryRight.wasJustReleased));

				if (resizeEnded || moveEnded)
				{
					m_DragState = null;

					foreach (var smoothMotion in GetComponentsInChildren<SmoothMotion>())
					{
						smoothMotion.enabled = true;
					}

					if (m_HovereringRayOrigins.Contains(rayOrigin))
					{
						var localPosition = transform.InverseTransformPoint(GetPointerPositionForRayOrigin(rayOrigin));
						var direction = GetResizeDirectionForLocalPosition(localPosition);
						GetResizeIconForDirection(direction);
					}
				}
				else
				{
					m_DragState.OnDragging();
				}
			}
		}

		Vector3 GetPointerPositionForRayOrigin(Transform rayOrigin)
		{
			return rayOrigin.position + rayOrigin.forward * getPointerLength(rayOrigin);
		}

		void OnDestroy()
		{
			ObjectUtils.Destroy(m_TopFaceMaterial);
			ObjectUtils.Destroy(m_FrontFaceMaterial);
		}

		public void CloseClick()
		{
			if (closeClicked != null)
				closeClicked();
		}

		public void ResetSizeClick()
		{
			if (resetSizeClicked != null)
				resetSizeClicked();
		}

		void IncreaseFrameThickness()
		{
			this.StopCoroutine(ref m_FrameThicknessCoroutine);
			const float kTargetBlendAmount = 0f;
			m_FrameThicknessCoroutine = StartCoroutine(ChangeFrameThickness(kTargetBlendAmount));
		}

		void ResetFrameThickness()
		{
			this.StopCoroutine(ref m_FrameThicknessCoroutine);
			const float kTargetBlendAmount = 50f;
			m_FrameThicknessCoroutine = StartCoroutine(ChangeFrameThickness(kTargetBlendAmount));
		}

		IEnumerator ChangeFrameThickness(float targetBlendAmount)
		{
			const float kTargetDuration = 0.25f;
			var currentDuration = 0f;
			var currentBlendAmount = m_Frame.GetBlendShapeWeight(k_ThinFrameBlendShapeIndex);
			var currentVelocity = 0f;
			while (currentDuration < kTargetDuration)
			{
				currentDuration += Time.unscaledDeltaTime;
				currentBlendAmount = MathUtilsExt.SmoothDamp(currentBlendAmount, targetBlendAmount, ref currentVelocity, kTargetDuration, Mathf.Infinity, Time.unscaledDeltaTime);
				m_Frame.SetBlendShapeWeight(k_ThinFrameBlendShapeIndex, currentBlendAmount);
				yield return null;
			}

			m_FrameThicknessCoroutine = null;
		}

		IEnumerator ShowTopFace()
		{
			const string kMaterialHighlightAlphaProperty = "_Alpha";
			const float kTargetAlpha = 1f;
			const float kTargetDuration = 0.35f;
			var currentDuration = 0f;
			var currentAlpha = m_TopFaceMaterial.GetFloat(kMaterialHighlightAlphaProperty);
			var currentVelocity = 0f;
			while (currentDuration < kTargetDuration)
			{
				currentDuration += Time.unscaledDeltaTime;
				currentAlpha = MathUtilsExt.SmoothDamp(currentAlpha, kTargetAlpha, ref currentVelocity, kTargetDuration, Mathf.Infinity, Time.unscaledDeltaTime);
				m_TopFaceMaterial.SetFloat(kMaterialHighlightAlphaProperty, currentAlpha);
				yield return null;
			}

			m_TopFaceVisibleCoroutine = null;
		}

		IEnumerator HideTopFace()
		{
			const string kMaterialHighlightAlphaProperty = "_Alpha";
			const float kTargetAlpha = 0f;
			const float kTargetDuration = 0.2f;
			var currentDuration = 0f;
			var currentAlpha = m_TopFaceMaterial.GetFloat(kMaterialHighlightAlphaProperty);
			var currentVelocity = 0f;
			while (currentDuration < kTargetDuration)
			{
				currentDuration += Time.unscaledDeltaTime;
				currentAlpha = MathUtilsExt.SmoothDamp(currentAlpha, kTargetAlpha, ref currentVelocity, kTargetDuration, Mathf.Infinity, Time.unscaledDeltaTime);
				m_TopFaceMaterial.SetFloat(kMaterialHighlightAlphaProperty, currentAlpha);
				yield return null;
			}

			m_TopFaceVisibleCoroutine = null;
		}
	}
}
#endif
