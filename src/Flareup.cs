using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Harmony;
using BattleTech;
using BattleTech.UI;
using Localize;
using ColourfulFlashPoints;
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    [JsonObject(MemberSerialization.OptIn)]
    public class Flareup {
        public SimGameState sim;

        public StarSystem location;
        [JsonProperty]
        public string locationID;

        [JsonProperty]
        public string type = "Flareup";

        public FactionValue attacker;
        [JsonProperty]
        public string attackerName;

        [JsonProperty]
        public int countdown;
        [JsonProperty]
        public int daysUntilMission;
        [JsonProperty]
        public int attackerStrength;
        [JsonProperty]
        public int defenderStrength;
        [JsonProperty]
        public string currentContractName = "";
        [JsonProperty]
        public int currentContractForceLoss = 0;

        public bool droppingForContract = false;

        public Flareup() {
            // Empty constructor used for deserialization.
        }

        public Flareup(StarSystem flareupLocation, FactionValue attackerFaction, string flareupType, SimGameState __instance) {
            Settings s = WIIC.settings;

            sim = __instance;
            location = flareupLocation;
            locationID = flareupLocation.ID;
            attacker = attackerFaction;
            attackerName = attackerFaction.Name;
            type = flareupType;
            countdown = Utilities.rng.Next(s.minCountdown, s.maxCountdown);

            int v;
            attackerStrength = s.attackStrength.TryGetValue(attacker.Name, out v) ? v : s.defaultAttackStrength;
            defenderStrength = s.defenseStrength.TryGetValue(location.OwnerValue.Name, out v) ? v : s.defaultDefenseStrength;

            attackerStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);
            defenderStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);

            string stat = $"WIIC_{attacker.Name}_attack_strength";
            attackerStrength += sim.CompanyStats.ContainsStatistic(stat) ? sim.CompanyStats.GetValue<int>(stat) : 0;
            stat = $"WIIC_{location.OwnerValue.Name}_defense_strength";
            defenderStrength += sim.CompanyStats.ContainsStatistic(stat) ? sim.CompanyStats.GetValue<int>(stat) : 0;

            if (type == "Raid") {
                attackerStrength = (int) Math.Ceiling(attackerStrength * s.raidStrengthMultiplier);
                defenderStrength = (int) Math.Ceiling(defenderStrength * s.raidStrengthMultiplier);
            }

            string text = type == "Raid" ? "{0} launches raid on {1} at {2}" : "{0} attacks {1} for control of {2}";
            text = Strings.T(text, attacker.FactionDef.ShortName, location.OwnerValue.FactionDef.ShortName, location.Name);
            WIIC.modLog.Info?.Write(text);
            WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(text));

            if (location == sim.CurSystem) {
                spawnParticipationContracts();
            }
        }

        public FactionValue employer {
            get {
                if (WIIC.sim.CurSystem != location) {
                    return null;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker")) {
                    return attacker;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_defender")) {
                    return location.OwnerValue;
                }
                return null;
            }
        }

        public FactionValue target {
            get {
                if (WIIC.sim.CurSystem != location) {
                    return null;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker")) {
                    return location.OwnerValue;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_defender")) {
                    return attacker;
                }
                return null;
            }
        }

        public bool passDay() {
            Settings s = WIIC.settings;
            if (countdown > 0) {
                countdown--;
                return false;
            }

            if (daysUntilMission > 1) {
                daysUntilMission--;
                return false;
            }

            double rand = Utilities.rng.NextDouble();
            if (rand > 0.5) {
                attackerStrength -= Utilities.rng.Next(s.combatForceLossMin, s.combatForceLossMax);
            } else {
                defenderStrength -= Utilities.rng.Next(s.combatForceLossMin, s.combatForceLossMax);
            }
            WIIC.modLog.Debug?.Write($"{type} progressed at {location.Name}. attackerStrength: {attackerStrength}, defenderStrength: {defenderStrength}");

            daysUntilMission = s.daysBetweenMissions;

            if (attackerStrength <= 0 || defenderStrength <= 0) {
              conclude();
              return true;
            }

            if (employer != null) {
                launchMission();
            }
            return false;
        }

        public void conclude() {
            WIIC.modLog.Info?.Write($"{type} finished at {location.Name}.");

            removeParticipationContracts();

            if (type == "Flareup") {
                if (attackerStrength <= 0) {
                    string text = Strings.T("Battle for {0} concludes - {1} holds off the {2} attack", location.Name, location.OwnerValue.FactionDef.ShortName, attacker.FactionDef.ShortName);
                    sim.RoomManager.ShipRoom.AddEventToast(new Text(text));
                } else if (defenderStrength <= 0) {
                    string text = Strings.T("Battle for {0} concludes - {1} takes the system from {2}", location.Name, attacker.FactionDef.ShortName, location.OwnerValue.FactionDef.ShortName);
                    sim.RoomManager.ShipRoom.AddEventToast(new Text(text));

                    Utilities.applyOwner(location, attacker);
                }
            } else if (type == "Raid") {
                SimGameEventResult result = new SimGameEventResult();
                result.Scope = EventScope.Company;
                result.TemporaryResult = true;
                result.ResultDuration = WIIC.settings.raidResultDuration;

                if (attackerStrength <= 0) {
                    string text = Strings.T("Raid on {0} concludes - {1} drives off the {2} forces", location.Name, location.OwnerValue.FactionDef.ShortName, attacker.FactionDef.ShortName);
                    sim.RoomManager.ShipRoom.AddEventToast(new Text(text));

                    result.Stats[0] = new SimGameStat($"WIIC_{attacker.Name}_attack_strength", 1, false);
                    result.Stats[1] = new SimGameStat($"WIIC_{location.OwnerValue.Name}_defense_strength", -1, false);
                } else if (defenderStrength <= 0) {
                    string text = Strings.T("Raid on {0} concludes - {1} weakens {2} control", location.Name, attacker.FactionDef.ShortName, location.OwnerValue.FactionDef.ShortName);
                    sim.RoomManager.ShipRoom.AddEventToast(new Text(text));

                    result.Stats[0] = new SimGameStat($"WIIC_{attacker.Name}_attack_strength", -1, false);
                    result.Stats[0] = new SimGameStat($"WIIC_{location.OwnerValue.Name}_defense_strength", 1, false);
                }

                SimGameEventResult[] results = {result};
                SimGameState.ApplySimGameEventResult(new List<SimGameEventResult>(results));
            }
        }

        public void addToMap() {
            Settings s = WIIC.settings;
            WIIC.modLog.Info?.Write($"type {type}, {(type == "Raid" ? s.raidMarker : s.flareupMarker)}");
            MapMarker mapMarker = new MapMarker(location.ID, type == "Raid" ? s.raidMarker : s.flareupMarker);
            ColourfulFlashPoints.Main.addMapMarker(mapMarker);

            if (!WIIC.fluffDescriptions.ContainsKey(location.ID)) {
                WIIC.modLog.Debug?.Write($"Filled fluff description entry for {location.ID}: {location.Def.Description.Details}");
                WIIC.fluffDescriptions[location.ID] = location.Def.Description.Details;
            }

            string description = getDescription() + "\n" + WIIC.fluffDescriptions[location.ID];
            AccessTools.Method(typeof(DescriptionDef), "set_Details").Invoke(location.Def.Description, new object[] { description });
        }

        public string getDescription() {
            var description = new StringBuilder();
            if (type == "Raid") {
                description.AppendLine(Strings.T("<b><color=#de0202>{0} is being raided by {1}</color></b>", location.Name, attacker.FactionDef.ShortName));
            } else {
                description.AppendLine(Strings.T("<b><color=#de0202>{0} is under attack by {1}</color></b>", location.Name, attacker.FactionDef.ShortName));
            }

            if (countdown > 0) {
               description.AppendLine(Strings.T("{0} days until the fighting starts", countdown));
            }
            if (daysUntilMission > 0) {
               description.AppendLine(Strings.T("{0} days until the next mission", daysUntilMission));
            }
            description.AppendLine("\n" + Strings.T("{0} forces: {1}", attacker.FactionDef.Name, Utilities.forcesToString(attackerStrength)));
            description.AppendLine(Strings.T("{0} forces: {1}", location.OwnerValue.FactionDef.Name, Utilities.forcesToString(defenderStrength)));

            return description.ToString();
        }

        public void spawnParticipationContracts() {
            Enum.TryParse(WIIC.settings.minReputationToHelpFlareup, out SimGameReputation minRep);
            int diff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);
            string contractPrefix = type == "Flareup" ? "wiic_help" : "wiic_raid";

            if (!WIIC.settings.wontHirePlayer.Contains(attacker.Name) && sim.GetReputation(attacker) >= minRep) {
                WIIC.modLog.Info?.Write($"Adding contract {contractPrefix}_attacker. Target={location.OwnerValue.Name}, Employer={attacker.Name}, TargetSystem={location.ID}, Difficulty={location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
                Contract attackContract = sim.AddContract(new SimGameState.AddContractData {
                    ContractName = $"{contractPrefix}_attacker",
                    Target = location.OwnerValue.Name,
                    Employer = attacker.Name,
                    TargetSystem = location.ID,
                    Difficulty = diff
                });
                attackContract.SetFinalDifficulty(diff);
            }

            if (!WIIC.settings.wontHirePlayer.Contains(location.OwnerValue.Name) && sim.GetReputation(location.OwnerValue) >= minRep) {
                WIIC.modLog.Info?.Write($"Adding contract {contractPrefix}_defender. Target={attacker.Name}, Employer={location.OwnerValue.Name}, TargetSystem={location.ID}, Difficulty={location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
                Contract defendContract = sim.AddContract(new SimGameState.AddContractData {
                    ContractName = $"{contractPrefix}_defender",
                    Target = attacker.Name,
                    Employer = location.OwnerValue.Name,
                    TargetSystem = location.ID,
                    Difficulty = diff
                });
                defendContract.SetFinalDifficulty(diff);
            }
        }

        public void removeParticipationContracts() {
            if (location == WIIC.sim.CurSystem) {
                WIIC.modLog.Debug?.Write($"Cleaning up participation contracts for {location.Name}.");
                WIIC.sim.GlobalContracts.RemoveAll(c => (c.Override.ID == "wiic_help_attacker" || c.Override.ID == "wiic_help_defender" || c.Override.ID == "wiic_raid_attacker" || c.Override.ID == "wiic_raid_defender"));
            }
        }

        public void launchMission() {
            Contract contract = ContractManager.getNewProceduralContract(location, employer, target);

            string title = Strings.T("Flareup Mission");
            string primaryButtonText = Strings.T("Launch mission");
            string cancel = Strings.T("Pass");
            string message = $"{employer.FactionDef.Name} has a mission for us, Commander: {contract.Name}. Details will be provided en-route, but it seems to be a {contract.ContractTypeValue.FriendlyName.ToLower()} mission. Sounds urgent.";
            WIIC.modLog.Debug?.Write(message);

            SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
            queue.QueuePauseNotification(title, message, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, delegate {
                try {
                    WIIC.modLog.Info?.Write($"Accepted {type} mission {contract.Name}.");
                    currentContractName = contract.Name;
                    currentContractForceLoss = Utilities.rng.Next(WIIC.settings.combatForceLossMin, WIIC.settings.combatForceLossMax);

                    WIIC.sim.RoomManager.ForceShipRoomChangeOfRoom(DropshipLocation.CMD_CENTER);
                    WIIC.sim.ForceTakeContract(contract, false);
                } catch (Exception e) {
                    WIIC.modLog.Error?.Write(e);
                }
            }, primaryButtonText, delegate {
                  WIIC.modLog.Info?.Write($"Passed on {type} mission.");
            }, cancel);

            if (!queue.IsOpen) {
                queue.DisplayIfAvailable();
            }
        }

        private WorkOrderEntry_Notification _workOrder;
        public WorkOrderEntry_Notification workOrder {
          get {
            if (_workOrder == null) {
              string title = Strings.T($"{type} contract");
              _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "nextflareupContract", title);
            }

            _workOrder.SetCost(daysUntilMission);
            return _workOrder;
          }
        }

        public string Serialize() {
            string json = JsonConvert.SerializeObject(this);
            return $"WIIC:{json}";
        }

        public static bool isSerializedFlareup(string tag) {
            return tag.StartsWith("WIIC:");
        }

        public static Flareup Deserialize(string tag, SimGameState __instance) {
            Flareup newFlareup = JsonConvert.DeserializeObject<Flareup>(tag.Substring(5));

            newFlareup.sim = __instance;
            newFlareup.location = __instance.GetSystemById(newFlareup.locationID);
            newFlareup.attacker = FactionEnumeration.GetFactionByName(newFlareup.attackerName);

            return newFlareup;
        }
    }
}
