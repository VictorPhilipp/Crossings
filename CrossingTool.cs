﻿/*
 * NodeSelector.cs - Defines the node selector tool
 *
 */
using UnityEngine;
//using UnityEngine.EventSystems;

using System;
using System.Threading;
using System.Reflection;
using System.Collections;

using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.Globalization;

namespace Crossings
{
	public class CrossingTool : ToolBase
	{
		public CrossingsUIToggle button;
		public ushort currentNode;
		NetTool.ControlPoint m_controlPoint;
		NetTool.ControlPoint m_cachedControlPoint;
		ToolErrors m_buildErrors;
		ToolErrors m_cachedErrors;
		Ray m_mouseRay;
		float m_mouseRayLength;
		bool m_mouseRayValid;
		NetInfo m_prefab;

		private object m_cacheLock;

		protected override void Awake()
		{
			m_toolController = GameObject.FindObjectOfType<ToolController>();
			m_cacheLock = new object();
		}

		private ushort GetSegmentAtCursor()
		{
			Vector3 mousePosition = Input.mousePosition;
			RaycastInput input = new RaycastInput(Camera.main.ScreenPointToRay(mousePosition), Camera.main.farClipPlane);
			RaycastOutput output;
			input.m_netService = new RaycastService(ItemClass.Service.Road, ItemClass.SubService.None, ItemClass.Layer.Default);
			input.m_ignoreSegmentFlags = NetSegment.Flags.None;
			input.m_ignoreTerrain = true;
			if (RayCast(input, out output))
			{
				Debug.Log("Found segment " + output.m_netSegment);
				return output.m_netSegment;
			}
			else
			{
				return 0;
			}
		}

		protected override void OnToolGUI()
		{
			bool isInsideUI = this.m_toolController.IsInsideUI;
			Event current = Event.current;
			if (current.type == EventType.MouseDown && !isInsideUI && current.button == 0)
			{
				if (this.m_cachedErrors == ToolBase.ToolErrors.None)
				{
					Singleton<SimulationManager>.instance.AddAction<bool>(this.CreateCrossing());
				}
				else
				{
				//	Singleton<SimulationManager>.instance.AddAction(this.CreateFailed());
				}
			}
		}

		override protected void OnToolUpdate()
		{
			while (!Monitor.TryEnter(this.m_cacheLock, SimulationManager.SYNCHRONIZE_TIMEOUT)) {}
			try {
				this.m_cachedControlPoint = this.m_controlPoint;
				this.m_cachedErrors = this.m_buildErrors;
			} finally {
				Monitor.Exit(this.m_cacheLock);
			}

			base.OnToolUpdate();
			// Cast ray, find node
			ushort segmentID = GetSegmentAtCursor ();

			if (segmentID != 0) {
				NetSegment segment = NetManager.instance.m_segments.m_buffer [segmentID];
				m_prefab = PrefabCollection<NetInfo>.GetPrefab (segment.m_infoIndex);
			} else {
				m_prefab = null;
			}
		}

		protected override void OnToolLateUpdate()
		{
			if (m_prefab == null)
			{
				return;
			}
			Vector3 mousePosition = Input.mousePosition;
			this.m_mouseRay = Camera.main.ScreenPointToRay(mousePosition);
			this.m_mouseRayLength = Camera.main.farClipPlane;
			this.m_mouseRayValid = (!this.m_toolController.IsInsideUI && Cursor.visible);
			InfoManager.InfoMode mode;
			InfoManager.SubInfoMode subMode;
			m_prefab.m_netAI.GetPlacementInfoMode(out mode, out subMode);
			base.ForceInfoMode(mode, subMode);
		}

		override public void SimulationStep()
		{
			ServiceTypeGuide optionsNotUsed = Singleton<NetManager>.instance.m_optionsNotUsed;
			if (optionsNotUsed != null && !optionsNotUsed.m_disabled)
			{
				optionsNotUsed.Disable();
			}

			Vector3 position = this.m_controlPoint.m_position;
			bool failed = false;

			NetTool.ControlPoint controlPoint = default(NetTool.ControlPoint);
			NetNode.Flags ignoreNodeFlags;
			NetSegment.Flags ignoreSegmentFlags;

			ignoreNodeFlags = NetNode.Flags.None;
			ignoreSegmentFlags = NetSegment.Flags.None;

			Building.Flags ignoreBuildingFlags = Building.Flags.All; // Untouchable ?

			if (m_prefab != null) {
				if (this.m_mouseRayValid && NetTool.MakeControlPoint (this.m_mouseRay, this.m_mouseRayLength, m_prefab, false, ignoreNodeFlags, ignoreSegmentFlags, ignoreBuildingFlags, 0, out controlPoint)) {
					if (controlPoint.m_node == 0 && controlPoint.m_segment == 0 && !controlPoint.m_outside) {
						controlPoint.m_position.y = NetSegment.SampleTerrainHeight (m_prefab, controlPoint.m_position, false) + controlPoint.m_elevation;
					}
				} else {
					failed = true;
				}
			}
			this.m_controlPoint = controlPoint;

			this.m_toolController.ClearColliding();

			ToolBase.ToolErrors toolErrors = ToolBase.ToolErrors.None;
			if (failed)
			{
				toolErrors |= ToolBase.ToolErrors.RaycastFailed;
			}
			while (!Monitor.TryEnter(this.m_cacheLock, SimulationManager.SYNCHRONIZE_TIMEOUT))
			{
			}
			try
			{
				this.m_buildErrors = toolErrors;
			}
			finally
			{
				Monitor.Exit(this.m_cacheLock);
			}

		}

		private static void AddCrossings(NetLane.Flags flag, NetInfo.Direction direction, NetNode node)
		{
			NetManager netManager = NetManager.instance;
			NetInfo info = node.Info;
			foreach (NetInfo.Lane lane in info.m_lanes)
			{
				lane.m_laneType |= NetInfo.LaneType.Pedestrian;
			}
		}

		public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
		{
			Debug.Log ("Render Overlay");
			base.RenderOverlay(cameraInfo);

			if (!this.m_toolController.IsInsideUI && Cursor.visible) {  /* && (this.m_cachedErrors & (ToolBase.ToolErrors.RaycastFailed | ToolBase.ToolErrors.Pending)) == ToolBase.ToolErrors.None*/
				NetTool.ControlPoint controlPoint;
				controlPoint = this.m_cachedControlPoint;

				Color toolColor2 = base.GetToolColor (false, this.m_cachedErrors != ToolBase.ToolErrors.None);
				Color toolColor3 = base.GetToolColor (true, false);
				this.m_toolController.RenderColliding (cameraInfo, toolColor2, toolColor3, toolColor2, toolColor3, 0, 0);
				Vector3 position = controlPoint.m_position;
				BuildingInfo buildingInfo;
				Vector3 vector;
				Vector3 vector2;
				int num3;
				if (m_prefab != null) {
					m_prefab.m_netAI.CheckBuildPosition (false, false, true, true, ref controlPoint, ref controlPoint, ref controlPoint, out buildingInfo, out vector, out vector2, out num3);
					bool flag = position != controlPoint.m_position;
					Bezier3 bezier;
					bezier.a = controlPoint.m_position;
					bezier.d = controlPoint.m_position;
					bool smoothStart = true;
					bool smoothEnd = true;
					NetSegment.CalculateMiddlePoints (bezier.a, controlPoint.m_direction, bezier.d, -controlPoint.m_direction, smoothStart, smoothEnd, out bezier.b, out bezier.c);
					Segment3 segment;

					segment.a = new Vector3 (-100000f, 0f, -100000f);
					segment.b = new Vector3 (-100000f, 0f, -100000f);

					ToolManager toolManager = Singleton<ToolManager>.instance;
					toolManager.m_drawCallData.m_overlayCalls = toolManager.m_drawCallData.m_overlayCalls + 1;
					Singleton<RenderManager>.instance.OverlayEffect.DrawBezier (cameraInfo, toolColor2, bezier, m_prefab.m_halfWidth * 2f, -100000f, -100000f, -1f, 1280f, false, false);

					if (this.m_cachedErrors == ToolBase.ToolErrors.None && Vector3.SqrMagnitude (bezier.d - bezier.a) >= 1f) {
						float radius;
						bool capped;
						Color color;
						m_prefab.m_netAI.GetEffectRadius (out radius, out capped, out color);
						if (radius > m_prefab.m_halfWidth) {
							toolManager.m_drawCallData.m_overlayCalls = toolManager.m_drawCallData.m_overlayCalls + 1;
							Singleton<RenderManager>.instance.OverlayEffect.DrawBezier (cameraInfo, color, bezier, radius * 2f, (!capped) ? -100000f : radius, (!capped) ? -100000f : radius, -1f, 1280f, false, true);
						}
					}
				}
			}
		}

		// I am not sure why the tool controller needs to be injected here.
		// ToolBase suggests this should happen on its own, but it does not.
		protected override void OnEnable()
		{
			m_toolController.ClearColliding ();
			m_buildErrors = ToolBase.ToolErrors.Pending;
			m_cachedErrors = ToolBase.ToolErrors.Pending;
			base.OnEnable();
		}

		protected override void OnDisable()
		{
			base.ToolCursor = null;
			m_buildErrors = ToolBase.ToolErrors.Pending;
			m_cachedErrors = ToolBase.ToolErrors.Pending;
			button.textColor = new Color32(255, 255, 255, 255);
			m_mouseRayValid = false;
			base.OnDisable();
		}

		private bool CreateNode(bool switchDirection)
		{
			//TODO: See NetTool.CreateNode[Impl]
			return true;
		}

	}
}

