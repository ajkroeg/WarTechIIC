using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    public class Settings {
        public bool debug = false;
        public bool trace = false;
        public double dailyFlareupChance = 0;
        public bool setActiveFactionsForAllSystems = false;
        public List<string> ignoreFactions = new List<string>();

        public double aggressionPerSystemOwned = 0;
        public Dictionary<string, double> reputationMultiplier = new Dictionary<string, double>();
        public Dictionary<string, double> aggression = new Dictionary<string, double>();

        public List<string> cantBeAttacked = new List<string>();
        public Dictionary<string, Dictionary<string, double>> hatred = new Dictionary<string, Dictionary<string, double>>();
        public bool limitTargetsToFactionEnemies = true;

        public FpMarker flareupMarker = new FpMarker();

        public int minCountdown = 30;
        public int maxCountdown = 45;
        public int defaultAttackStrength = 10;
        public int defaultDefenseStrength = 10;
        public int strengthVariation = 0;
        public Dictionary<string, int> attackStrength = new Dictionary<string, int>();
        public Dictionary<string, int> defenseStrength = new Dictionary<string, int>();

        public string minReputationToHelp = "DISLIKED";
        public List<string> wontHirePlayer = new List<string>();
        public int daysBetweenMissions = 2;
        public int combatForceLossMin = 2;
        public int combatForceLossMax = 5;
        public int flareupMissionBonusPerHalfSkull = 0;

        public List<string> pirateTags = new List<string>();

        public List<int> customContractEnums = new List<int>();

        public string saveFolder;
    }
}
