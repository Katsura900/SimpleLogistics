﻿using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;
using KSP.IO;
using KSP.Localization;
using ToolbarControl_NS;

namespace SimpleLogistics
{
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class SimpleLogistics: MonoBehaviour
	{
		private static SimpleLogistics instance;
		public static SimpleLogistics Instance { get { return instance; } }

		// So many lists...
		private SortedList<string, double> resourcePool;
		private SortedList<string, double> requestPool;
		private SortedList<string, double> vesselSpareSpace;

		private List<PartResource> partResources;

		private bool requested;

		private PluginConfiguration config;

		// GUI vars
		private Rect windowRect;
		private int windowId;
		private bool gamePaused;
		private bool globalHidden;
		private bool active;
		private bool refresh;

		//private ApplicationLauncherButton appLauncherButton;
		//private IButton toolbarButton;
		private ToolbarControl toolbarControl;

		// Same as Debug Toolbar lock mask
		private const ulong lockMask = 900719925474097919;

#region Primary Functions
		private void Awake() {
			if (instance != null) {
				Destroy (this);
				return;
			}

			instance = this;
		}

		private void Start() {
			resourcePool = new SortedList<string, double> ();
			requestPool = new SortedList<string, double> ();
			vesselSpareSpace = new SortedList<string, double> ();

			partResources = new List<PartResource> ();

			config = PluginConfiguration.CreateForType<SimpleLogistics> ();
			config.load ();

			windowRect = config.GetValue<Rect>(this.name, new Rect (0, 0, 400, 400));

			windowId = GUIUtility.GetControlID(FocusType.Passive);

			globalHidden = false;
			gamePaused = false;
			active = false;
			refresh = true;

			requested = false;
			CreateLauncher();

			//GameEvents.onGUIApplicationLauncherReady.Add(CreateLauncher);
			GameEvents.onLevelWasLoaded.Add (onLevelWasLoaded);
			GameEvents.onVesselChange.Add (onVesselChange);
			GameEvents.onHideUI.Add(onHideUI);
			GameEvents.onShowUI.Add(onShowUI);
			GameEvents.onGamePause.Add (onGamePause);
			GameEvents.onGameUnpause.Add (onGameUnpause);
		}

		private void OnDestroy() {
			config.SetValue (this.name, windowRect);
			config.save ();

			//GameEvents.onGUIApplicationLauncherReady.Remove(CreateLauncher);
			GameEvents.onLevelWasLoaded.Remove (onLevelWasLoaded);
			GameEvents.onVesselChange.Remove (onVesselChange);
			GameEvents.onHideUI.Remove(onHideUI);
			GameEvents.onShowUI.Remove(onShowUI);
			GameEvents.onGamePause.Remove (onGamePause);
			GameEvents.onGameUnpause.Remove (onGameUnpause);

			UnlockControls ();
			DestroyLauncher ();

			if (instance == this)
				instance = null;
		}

		private void onVesselChange(Vessel vessel) {
			requestPool.Clear ();
			vesselSpareSpace.Clear ();
			foreach(Part part in vessel.parts) {
				foreach (var resource in part.Resources) {
					if (!requestPool.ContainsKey (resource.info.name)) {
						requestPool.Add (resource.info.name, 0);
						vesselSpareSpace.Add (resource.info.name, resource.maxAmount);
					} else
						vesselSpareSpace [resource.info.name] += resource.maxAmount;
				}
			}
		}

		public void onLevelWasLoaded(GameScenes scene)
		{
			onVesselChange(FlightGlobals.ActiveVessel);
		}

		#endregion

#region UI Functions

		public const string MODID = "SimpleLogisticsUI";
		public const string MODNAME = "SimpleLogistics!";
		private void CreateLauncher() 
		{
			toolbarControl = gameObject.AddComponent<ToolbarControl>();
			toolbarControl.AddToAllToolbars(onAppTrue, onAppFalse,	
				ApplicationLauncher.AppScenes.FLIGHT,
				 MODID,
				"SIButton",
				"SimpleLogistics/Plugins/Textures/simple-logistics-icon",
				"SimpleLogistics/Plugins/Textures/simple-logistics-icon-toolbar",
				MODNAME
			);
#if false
			if (ToolbarManager.ToolbarAvailable) {
				toolbarButton = ToolbarManager.Instance.add ("SimpleLogistics", "AppLaunch");
				toolbarButton.TexturePath = "SimpleLogistics/Plugins/Textures/simple-logistics-icon-toolbar";
				toolbarButton.ToolTip = "Simple Logistics UI";
				toolbarButton.Visible = true;
				toolbarButton.OnClick += (ClickEvent e) => {
					onToggle();
				};
			}
			else if (appLauncherButton == null)
			{
				appLauncherButton = ApplicationLauncher.Instance.AddModApplication(
					onAppTrue,
					onAppFalse,
					null,
					null,
					null,
					null,
					ApplicationLauncher.AppScenes.FLIGHT,
					GameDatabase.Instance.GetTexture("SimpleLogistics/Plugins/Textures/simple-logistics-icon", false)
				);
			}
#endif
		}

		public void DestroyLauncher()
		{
#if false
			if (appLauncherButton != null) {
				ApplicationLauncher.Instance.RemoveModApplication (appLauncherButton);
			}
			if (toolbarButton != null) {
				toolbarButton.Destroy ();
				toolbarButton = null;
			}
#endif
			if (toolbarControl != null)
			{
				toolbarControl.OnDestroy();
				Destroy(toolbarControl);
			}
		}

		public void OnGUI()
		{
			if (gamePaused || globalHidden || !active) return;

			// if (FlightGlobals.ActiveVessel.situation != Vessel.Situations.LANDED) {
			if (!InSituation.NetworkEligible())
			{
#if false
				if (appLauncherButton != null)
					appLauncherButton.SetFalse ();
				else
					onToggle ();
				return;
#endif
				toolbarControl.SetFalse();
			}

			if (refresh) {
				windowRect.height = 0;
				refresh = false;
			}

			windowRect = Layout.Window(
				windowId,
				windowRect,
				DrawGUI,
                Localizer.Format("#SimpleLogistics_WindowTitle"), //"Logistics Network"
                GUILayout.ExpandWidth(true),
				GUILayout.ExpandHeight(true)
			);
			if (windowRect.Contains (Event.current.mousePosition)) {
				LockControls ();
			} else {
				UnlockControls();
			}
		}

		// It's a mess
		private void DrawGUI(int windowId) {
			GUILayout.BeginVertical ();
           
            Layout.LabelAndText(Localizer.Format("#SimpleLogistics_Label1"), Localizer.Format(FlightGlobals.ActiveVessel.RevealName())); //"Current Vessel"

            bool ableToRequest = false;

			LogisticsModule lm = FlightGlobals.ActiveVessel.FindPartModuleImplementing<LogisticsModule> ();
			if (lm != null) {
                Layout.Label(
                    lm.IsActive ? Localizer.Format("#SimpleLogistics_Label2") : Localizer.Format("#SimpleLogistics_Label3"), //"Pluged In""Unplugged"
                    lm.IsActive ? Palette.green : Palette.red
                );

                // "Toggle Plug"
                if (Layout.Button(Localizer.Format("#SimpleLogistics_Label4"), Palette.yellow))
                { 
                    lm.Set (!lm.IsActive);
					refresh = true;
				}
				ableToRequest = !lm.IsActive;
			}

			if (ableToRequest)
				GetVesselSpareSpace ();

            Layout.LabelCentered(Localizer.Format("#SimpleLogistics_Label5"), Palette.yellow); //"Resource Pool:"

            foreach (var resource in resourcePool) {
				GUILayout.BeginHorizontal ();
				Layout.Label (resource.Key, Palette.yellow, GUILayout.Width(170));
				if (ableToRequest && requestPool.ContainsKey (resource.Key)) {
					Layout.Label (requestPool[resource.Key].ToString("0.00") + " / " +
						resource.Value.ToString ("0.00"));
				} else
					Layout.Label (resource.Value.ToString ("0.00"));
				
				GUILayout.EndHorizontal ();
				if (ableToRequest && requestPool.ContainsKey(resource.Key)) {
					GUILayout.BeginHorizontal ();
					if (Layout.Button ("0", GUILayout.Width (20)))
						requestPool [resource.Key] = 0;
					
					requestPool [resource.Key] = Layout.HorizontalSlider (
						requestPool [resource.Key],
						0,
						Math.Min (vesselSpareSpace [resource.Key], resource.Value),
						GUILayout.Width (280)
					);
					if (Layout.Button (vesselSpareSpace [resource.Key].ToString ("0.00")))
						requestPool [resource.Key] = Math.Min (vesselSpareSpace [resource.Key], resource.Value);

					GUILayout.EndHorizontal ();
				}
			}

			if (ableToRequest)
            // "Request Resources"
            if(Layout.Button(Localizer.Format("#SimpleLogistics_Label6"))) {
                requested = true;
			}

            //"Close"
            if (Layout.Button(Localizer.Format("#SimpleLogistics_Label7"), Palette.red))
            {
#if false
				if (appLauncherButton != null)
					appLauncherButton.SetFalse ();
				else
					onToggle ();				
#endif
				toolbarControl.SetFalse();
			}

			GUILayout.EndVertical ();
			GUI.DragWindow ();
		}

		public void onGamePause() {
			gamePaused = true;
			UnlockControls ();
		}

		public void onGameUnpause() {
			gamePaused = false;
		}

		private void onHideUI()
		{
			globalHidden = true;
			UnlockControls ();
		}

		private void onShowUI()
		{
			globalHidden = false;
		}

		public void onAppTrue()
		{
			if (!InSituation.NetworkEligible()) {
                ScreenMessages.PostScreenMessage(Localizer.Format("#SimpleLogistics_msg1")); //"Must be landed to use logistics"
                return;
			}

			active = true;
		}

		public void onAppFalse()
		{
			active = false;
			refresh = true;
			UnlockControls ();
		}

		internal virtual void onToggle()
		{
			if (!InSituation.NetworkEligible()) {
                ScreenMessages.PostScreenMessage(Localizer.Format("#SimpleLogistics_msg2")); //"Must be landed to use logistics"
                return;
			}

			active = !active;
			if (!active) {
				refresh = true;
				UnlockControls ();
			}
		}

		private ControlTypes LockControls()
		{
			return InputLockManager.SetControlLock ((ControlTypes)lockMask, this.name);
		}

		private void UnlockControls()
		{
			InputLockManager.RemoveControlLock(this.name);
		}
#endregion
#region zed'K new code
/*        private Boolean IsSituationValid(Vessel vessel)
        {
            Boolean stati = false;

            if (HighLogic.CurrentGame.Parameters.CustomParams<SimpleLogistics_Options>().globalLogisticsRange > 1) stati = true;
            if (HighLogic.CurrentGame.Parameters.CustomParams<SimpleLogistics_Options>().allowPreLaunch) stati = true;
            if (HighLogic.CurrentGame.Parameters.CustomParams<SimpleLogistics_Options>().allowSplashed) stati = true;
            if (HighLogic.CurrentGame.Parameters.CustomParams<SimpleLogistics_Options>().requireLanded) stati = true;
            return stati;
        }
        private Boolean VesselState(Vessel vessel)
        {
            Log.dbg("name {0}, range {1}, situation {2}", vessel.vesselName, vessel.vesselRanges, vessel.SituationString);
            if ((vessel.situation != Vessel.Situations.LANDED) || (vessel.situation != Vessel.Situations.PRELAUNCH) || (vessel.situation != Vessel.Situations.SPLASHED)) return true;
            return false;
        }
        private Boolean VesselRange(Vessel vessel)
        {
            return true;
        }*/
#endregion
#region Resource Sharing
        private void FixedUpdate() {
			// Find all resources in the network
			partResources.Clear ();
			foreach (Vessel vessel in FlightGlobals.VesselsLoaded) {
                // Log.dbg("name {0}, range {1}, situation {2}", vessel.vesselName, vessel.vesselRanges, vessel.SituationString);
				// if ((vessel.situation != Vessel.Situations.LANDED) || (vessel.situation != Vessel.Situations.PRELAUNCH) || (vessel.situation != Vessel.Situations.SPLASHED))
				//	continue;
                //if (VesselState(vessel) && VesselRange(vessel))
                if (vessel.situation != Vessel.Situations.LANDED)
                    continue;

				LogisticsModule lm = vessel.FindPartModuleImplementing<LogisticsModule> ();
				if (lm != null)
				if (!lm.IsActive)
					continue;
				
				foreach (Part part in vessel.parts) {
					if (part.State == PartStates.DEAD)
						continue;
					
					foreach (PartResource resource in part.Resources) {
						if (resource.info.resourceTransferMode == ResourceTransferMode.NONE ||
							resource._flowMode == PartResource.FlowMode.None ||
							!resource._flowState)
							continue;
						
						partResources.Add (resource);
					}
				}
			}

			// Create a resource pool
			resourcePool.Clear ();
			foreach (var resource in partResources) {
				if (!resourcePool.ContainsKey (resource.info.name))
					resourcePool.Add (resource.info.name, resource.amount);
				else
					resourcePool [resource.info.name] += resource.amount;
			}

			// Spread resources evenly
			foreach (var resource in resourcePool) {
				double value = resource.Value;
				if (requested) {
					if (requestPool.ContainsKey (resource.Key)) {
						value -= requestPool [resource.Key];
					}
				}

				var resList = partResources.FindAll (r => r.info.name == resource.Key);

				// Don't waste time on single one
//				if (resList.Count == 1)
//					continue;

				ShareResource (resList, value);
			}

			if (requested) {
				TransferResources ();
				requested = false;
			}
		}

		/// <summary>
		/// Distributes resource evenly across every capacitor with priority to low capacity first
		/// </summary>
		/// <param name="resources">List of resources</param>
		/// <param name="amount">Overall amount</param>
		private void ShareResource(List<PartResource> resources, double amount) {
			// Portion each may potentially receive
			double portion = amount / resources.Count;

			// Those who may not grab whole portion
			var minors = resources.FindAll (r => r.maxAmount < portion);

			// Those who may grab whole portion and even ask for more :D
			var majors = resources.FindAll (r => r.maxAmount >= portion);

			if (minors.Count > 0) {
				// Some may not handle this much
				foreach (var minor in minors) {
					minor.amount = minor.maxAmount;
					amount -= minor.maxAmount;
				}
				// Love recursion :D
				if (amount > 0)
					ShareResource (majors, amount);
			} else {
				// Portion size is good for everybody
				foreach (var major in majors) {
					major.amount = portion;
				}
			}
		}

		/// <summary>
		/// Get the amount of spare resource space. Calling every physics frame is stupid, but who cares :D
		/// </summary>
		/// <param name="vessel">Vessel.</param>
		private void GetVesselSpareSpace() {
			vesselSpareSpace.Clear ();
			foreach(Part part in FlightGlobals.ActiveVessel.parts) {
				foreach (var resource in part.Resources) {
					if (!vesselSpareSpace.ContainsKey (resource.info.name))
						vesselSpareSpace.Add (resource.info.name, resource.maxAmount - resource.amount);
					else
						vesselSpareSpace [resource.info.name] += resource.maxAmount - resource.amount;
				}
			}
		}

		// Code duplication? No way!
		private void TransferResources() {
			List<PartResource> AVResources = new List<PartResource> ();
			SortedList<string, double> AVPool = new SortedList<string, double> ();

			foreach (Part part in FlightGlobals.ActiveVessel.parts) {
				if (part.State == PartStates.DEAD)
					continue;

				foreach (PartResource resource in part.Resources) {
					if (resource.info.resourceTransferMode == ResourceTransferMode.NONE ||
						resource._flowMode == PartResource.FlowMode.None ||
						!resource._flowState)
						continue;

					AVResources.Add (resource);
				}
			}

			foreach (var resource in AVResources) {
				if (!AVPool.ContainsKey (resource.info.name))
					AVPool.Add (resource.info.name, resource.amount);
				else
					AVPool [resource.info.name] += resource.amount;
			}

			// Spread resources evenly
			foreach (var resource in AVPool) {
				double value = resource.Value;
				var resList = AVResources.FindAll (r => r.info.name == resource.Key);
				value += requestPool [resource.Key];
				requestPool [resource.Key] = 0;

				ShareResource (resList, value);
			}
		}
#endregion
	}
}

