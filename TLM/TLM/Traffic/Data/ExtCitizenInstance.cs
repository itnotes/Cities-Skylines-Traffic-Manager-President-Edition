﻿using ColossalFramework;
using CSUtil.Commons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TrafficManager.Custom.PathFinding;
using TrafficManager.State;
using TrafficManager.Traffic.Enums;
using UnityEngine;

namespace TrafficManager.Traffic.Data {
	public struct ExtCitizenInstance {
		public ushort instanceId;

		/// <summary>
		/// Citizen path mode (used for Parking AI)
		/// </summary>
		public ExtPathMode pathMode;

		/// <summary>
		/// Number of times a formerly found parking space is already occupied after reaching its position
		/// </summary>
		public int failedParkingAttempts;

		/// <summary>
		/// Segment id / Building id where a parking space has been found
		/// </summary>
		public ushort parkingSpaceLocationId;

		/// <summary>
		/// Type of object (segment/building) where a parking space has been found
		/// </summary>
		public ExtParkingSpaceLocation parkingSpaceLocation;

		/// <summary>
		/// Path position that is used as a start position when parking fails
		/// </summary>
		public PathUnit.Position? parkingPathStartPosition;

		/// <summary>
		/// Walking path from (alternative) parking spot to target (only used to check if there is a valid walking path, not actually used at the moment)
		/// </summary>
		public uint returnPathId;

		/// <summary>
		/// State of the return path
		/// </summary>
		public ExtPathState returnPathState;

		/// <summary>
		/// Last known distance to the citizen's parked car
		/// </summary>
		public float lastDistanceToParkedCar;

		public override string ToString() {
			return $"[ExtCitizenInstance\n" +
				"\t" + $"instanceId = {instanceId}\n" +
				"\t" + $"pathMode = {pathMode}\n" +
				"\t" + $"failedParkingAttempts = {failedParkingAttempts}\n" +
				"\t" + $"parkingSpaceLocationId = {parkingSpaceLocationId}\n" +
				"\t" + $"parkingSpaceLocation = {parkingSpaceLocation}\n" +
				"\t" + $"parkingPathStartPosition = {parkingPathStartPosition}\n" +
				"\t" + $"returnPathId = {returnPathId}\n" +
				"\t" + $"returnPathState = {returnPathState}\n" +
				"\t" + $"lastDistanceToParkedCar = {lastDistanceToParkedCar}\n" +
				"ExtCitizenInstance]";
		}

		internal ExtCitizenInstance(ushort instanceId) {
			this.instanceId = instanceId;
			pathMode = ExtPathMode.None;
			failedParkingAttempts = 0;
			parkingSpaceLocationId = 0;
			parkingSpaceLocation = ExtParkingSpaceLocation.None;
			parkingPathStartPosition = null;
			returnPathId = 0;
			returnPathState = ExtPathState.None;
			lastDistanceToParkedCar = 0;
		}

		internal bool IsValid() {
			return Constants.ServiceFactory.CitizenService.IsCitizenInstanceValid(instanceId);
		}

		public uint GetCitizenId() {
			uint ret = 0;
			Constants.ServiceFactory.CitizenService.ProcessCitizenInstance(instanceId, delegate (ushort citInstId, ref CitizenInstance citizenInst) {
				ret = citizenInst.m_citizen;
				return true;
			});
			return ret;
		}

		internal void Reset() {
#if DEBUG
			bool citDebug = GlobalConfig.Instance.Debug.CitizenId == 0 || GlobalConfig.Instance.Debug.CitizenId == GetCitizenId();
			bool debug = GlobalConfig.Instance.Debug.Switches[2] && citDebug;
			bool fineDebug = GlobalConfig.Instance.Debug.Switches[4] && citDebug;

			if (fineDebug) {
				Log.Warning($"ExtCitizenInstance.Reset({instanceId}): Resetting ext. citizen instance {instanceId}");
			}
#endif
			//Flags = ExtFlags.None;
			pathMode = ExtPathMode.None;
			failedParkingAttempts = 0;
			parkingSpaceLocation = ExtParkingSpaceLocation.None;
			parkingSpaceLocationId = 0;
			lastDistanceToParkedCar = float.MaxValue;
			//ParkedVehiclePosition = default(Vector3);
			ReleaseReturnPath();
		}

		/// <summary>
		/// Releases the return path
		/// </summary>
		internal void ReleaseReturnPath() {
#if DEBUG
			bool citDebug = GlobalConfig.Instance.Debug.CitizenId == 0 || GlobalConfig.Instance.Debug.CitizenId == GetCitizenId();
			bool debug = GlobalConfig.Instance.Debug.Switches[2] && citDebug;
			bool fineDebug = GlobalConfig.Instance.Debug.Switches[4] && citDebug;
#endif

			if (returnPathId != 0) {
#if DEBUG
				if (debug)
					Log._Debug($"Releasing return path {returnPathId} of citizen instance {instanceId}. ReturnPathState={returnPathState}");
#endif

				Singleton<PathManager>.instance.ReleasePath(returnPathId);
				returnPathId = 0;
			}
			returnPathState = ExtPathState.None;
		}

		/// <summary>
		/// Checks the calculation state of the return path
		/// </summary>
		internal void UpdateReturnPathState() {
#if DEBUG
			bool citDebug = GlobalConfig.Instance.Debug.CitizenId == 0 || GlobalConfig.Instance.Debug.CitizenId == GetCitizenId();
			bool debug = GlobalConfig.Instance.Debug.Switches[2] && citDebug;
			bool fineDebug = GlobalConfig.Instance.Debug.Switches[4] && citDebug;

			if (fineDebug)
				Log._Debug($"ExtCitizenInstance.UpdateReturnPathState() called for citizen instance {instanceId}");
#endif
			if (returnPathId != 0 && returnPathState == ExtPathState.Calculating) {
				byte returnPathFlags = CustomPathManager._instance.m_pathUnits.m_buffer[returnPathId].m_pathFindFlags;
				if ((returnPathFlags & PathUnit.FLAG_READY) != 0) {
					returnPathState = ExtPathState.Ready;
#if DEBUG
					if (fineDebug)
						Log._Debug($"CustomHumanAI.CustomSimulationStep: Return path {returnPathId} SUCCEEDED. Flags={returnPathFlags}. Setting ReturnPathState={returnPathState}");
#endif
				} else if ((returnPathFlags & PathUnit.FLAG_FAILED) != 0) {
					returnPathState = ExtPathState.Failed;
#if DEBUG
					if (debug)
						Log._Debug($"CustomHumanAI.CustomSimulationStep: Return path {returnPathId} FAILED. Flags={returnPathFlags}. Setting ReturnPathState={returnPathState}");
#endif
				}
			}
		}

		/// <summary>
		/// Starts path-finding of the walking path from parking position <paramref name="parkPos"/> to target position <paramref name="targetPos"/>.
		/// </summary>
		/// <param name="parkPos">Parking position</param>
		/// <param name="targetPos">Target position</param>
		/// <returns></returns>
		internal bool CalculateReturnPath(Vector3 parkPos, Vector3 targetPos) {
#if DEBUG
			bool citDebug = GlobalConfig.Instance.Debug.CitizenId == 0 || GlobalConfig.Instance.Debug.CitizenId == GetCitizenId();
			bool debug = GlobalConfig.Instance.Debug.Switches[2] && citDebug;
			bool fineDebug = GlobalConfig.Instance.Debug.Switches[4] && citDebug;
#endif

			ReleaseReturnPath();

			PathUnit.Position parkPathPos;
			PathUnit.Position targetPathPos;
			if (Constants.ManagerFactory.ExtPathManager.FindPathPositionWithSpiralLoop(parkPos, ItemClass.Service.Road, NetInfo.LaneType.Pedestrian, VehicleInfo.VehicleType.None, NetInfo.LaneType.None, VehicleInfo.VehicleType.None, false, false, GlobalConfig.Instance.ParkingAI.MaxBuildingToPedestrianLaneDistance, out parkPathPos) &&
				Constants.ManagerFactory.ExtPathManager.FindPathPositionWithSpiralLoop(targetPos, ItemClass.Service.Road, NetInfo.LaneType.Pedestrian, VehicleInfo.VehicleType.None, NetInfo.LaneType.None, VehicleInfo.VehicleType.None, false, false, GlobalConfig.Instance.ParkingAI.MaxBuildingToPedestrianLaneDistance, out targetPathPos)) {

				PathUnit.Position dummyPathPos = default(PathUnit.Position);
				uint pathId;
				PathCreationArgs args;
				args.extPathType = ExtPathType.WalkingOnly;
				args.extVehicleType = ExtVehicleType.None;
				args.vehicleId = 0;
				args.buildIndex = Singleton<SimulationManager>.instance.m_currentBuildIndex;
				args.startPosA = parkPathPos;
				args.startPosB = dummyPathPos;
				args.endPosA = targetPathPos;
				args.endPosB = dummyPathPos;
				args.vehiclePosition = dummyPathPos;
				args.laneTypes = NetInfo.LaneType.Pedestrian | NetInfo.LaneType.PublicTransport;
				args.vehicleTypes = VehicleInfo.VehicleType.None;
				args.maxLength = 20000f;
				args.isHeavyVehicle = false;
				args.hasCombustionEngine = false;
				args.ignoreBlocked = false;
				args.ignoreFlooded = false;
				args.ignoreCosts = false;
				args.randomParking = false;
				args.stablePath = false;
				args.skipQueue = false;

				if (CustomPathManager._instance.CustomCreatePath(out pathId, ref Singleton<SimulationManager>.instance.m_randomizer, args)) {
#if DEBUG
					if (debug)
						Log._Debug($"ExtCitizenInstance.CalculateReturnPath: Path-finding starts for return path of citizen instance {instanceId}, path={pathId}, parkPathPos.segment={parkPathPos.m_segment}, parkPathPos.lane={parkPathPos.m_lane}, targetPathPos.segment={targetPathPos.m_segment}, targetPathPos.lane={targetPathPos.m_lane}");
#endif

					returnPathId = pathId;
					returnPathState = ExtPathState.Calculating;
					return true;
				}
			}

#if DEBUG
			if (debug)
				Log._Debug($"ExtCitizenInstance.CalculateReturnPath: Could not find path position(s) for either the parking position or target position of citizen instance {instanceId}.");
#endif

			return false;
		}

		/// <summary>
		/// Determines the path type through evaluating the current path mode.
		/// </summary>
		/// <returns></returns>
		public ExtPathType GetPathType() {
			switch (pathMode) {
				case ExtPathMode.CalculatingCarPathToAltParkPos:
				case ExtPathMode.CalculatingCarPathToKnownParkPos:
				case ExtPathMode.CalculatingCarPathToTarget:
				case ExtPathMode.DrivingToAltParkPos:
				case ExtPathMode.DrivingToKnownParkPos:
				case ExtPathMode.DrivingToTarget:
				case ExtPathMode.RequiresCarPath:
				case ExtPathMode.ParkingFailed:
					return ExtPathType.DrivingOnly;
				case ExtPathMode.CalculatingWalkingPathToParkedCar:
				case ExtPathMode.CalculatingWalkingPathToTarget:
				case ExtPathMode.RequiresWalkingPathToParkedCar:
				case ExtPathMode.RequiresWalkingPathToTarget:
				case ExtPathMode.ApproachingParkedCar:
				case ExtPathMode.WalkingToParkedCar:
				case ExtPathMode.WalkingToTarget:
					return ExtPathType.WalkingOnly;
				default:
					return ExtPathType.None;
			}
		}

		/// <summary>
		/// Converts an ExtPathState to a ExtSoftPathState.
		/// </summary>
		/// <param name="state"></param>
		/// <returns></returns>
		public static ExtSoftPathState ConvertPathStateToSoftPathState(ExtPathState state) {
			return (ExtSoftPathState)((int)state);
		}
	}
}
